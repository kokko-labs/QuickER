using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using QuickER.Model;
using QuickER.ViewModels;

namespace QuickER.UndoRedo;

/// <summary>
/// エンティティ・カラム・リレーションのプロパティ変更を追跡し
/// Undo / Redo 用のスナップショット差分をコマンドとして UndoRedo スタックへ積むクラス
/// </summary>
/// <remarks>
/// コレクションへの項目の出入りは所有者（<see cref="ViewModels.MainViewModel"/>）からの
/// Attach / Detach 呼び出しで通知を受ける
/// </remarks>
public sealed class DiagramChangeTracker
{
    /// <summary>追跡対象とするエンティティのプロパティ群</summary>
    private static readonly ITrackedProperty[] TrackedEntityProperties =
    [
        new TrackedProperty<EntityViewModel>(
            nameof(EntityViewModel.TableName),
            x => x.TableName,
            (x, v) => x.TableName = (string)v!
        ),
        new TrackedProperty<EntityViewModel>(
            nameof(EntityViewModel.Memo),
            x => x.Memo,
            (x, v) => x.Memo = (string)v!
        ),
        new TrackedProperty<EntityViewModel>(
            nameof(EntityViewModel.Description),
            x => x.Description,
            (x, v) => x.Description = (string)v!
        ),
        new TrackedProperty<EntityViewModel>(
            nameof(EntityViewModel.TitleBackgroundColor),
            x => x.TitleBackgroundColor,
            (x, v) => x.TitleBackgroundColor = (string)v!
        ),
    ];

    /// <summary>追跡対象とするリレーションのプロパティ群</summary>
    private static readonly ITrackedProperty[] TrackedRelationshipProperties =
    [
        new TrackedProperty<RelationshipViewModel>(
            nameof(RelationshipViewModel.Type),
            x => x.Type,
            (x, v) => x.Type = (RelationshipType)v!
        ),
        new TrackedProperty<RelationshipViewModel>(
            nameof(RelationshipViewModel.SourceColumnId),
            x => x.SourceColumnId,
            (x, v) => x.SourceColumnId = (Guid?)v
        ),
        new TrackedProperty<RelationshipViewModel>(
            nameof(RelationshipViewModel.TargetColumnId),
            x => x.TargetColumnId,
            (x, v) => x.TargetColumnId = (Guid?)v
        ),
        new TrackedProperty<RelationshipViewModel>(
            nameof(RelationshipViewModel.ConstraintName),
            x => x.ConstraintName,
            (x, v) => x.ConstraintName = (string?)v
        ),
        new TrackedProperty<RelationshipViewModel>(
            nameof(RelationshipViewModel.OnDelete),
            x => x.OnDelete,
            (x, v) => x.OnDelete = (ForeignKeyReferentialAction)v!
        ),
        new TrackedProperty<RelationshipViewModel>(
            nameof(RelationshipViewModel.OnUpdate),
            x => x.OnUpdate,
            (x, v) => x.OnUpdate = (ForeignKeyReferentialAction)v!
        ),
    ];

    /// <summary>追跡対象とするカラムのプロパティ群</summary>
    private static readonly ITrackedProperty[] TrackedColumnProperties =
    [
        new TrackedProperty<ColumnViewModel>(
            nameof(ColumnViewModel.Name),
            x => x.Name,
            (x, v) => x.Name = (string)v!
        ),
        new TrackedProperty<ColumnViewModel>(
            nameof(ColumnViewModel.DataType),
            x => x.DataType,
            (x, v) => x.DataType = (string)v!
        ),
        new TrackedProperty<ColumnViewModel>(
            nameof(ColumnViewModel.IsPrimaryKey),
            x => x.IsPrimaryKey,
            (x, v) => x.IsPrimaryKey = (bool)v!
        ),
        new TrackedProperty<ColumnViewModel>(
            nameof(ColumnViewModel.IsForeignKey),
            x => x.IsForeignKey,
            (x, v) => x.IsForeignKey = (bool)v!
        ),
        new TrackedProperty<ColumnViewModel>(
            nameof(ColumnViewModel.IsNullable),
            x => x.IsNullable,
            (x, v) => x.IsNullable = (bool)v!
        ),
        new TrackedProperty<ColumnViewModel>(
            nameof(ColumnViewModel.Description),
            x => x.Description,
            (x, v) => x.Description = (string)v!
        ),
    ];

    /// <summary>差分コマンドを積む Undo / Redo スタック</summary>
    private readonly UndoRedoManager _undoRedo;

    /// <summary>追跡対象のエンティティコレクション</summary>
    private readonly ObservableCollection<EntityViewModel> _entities;

    /// <summary>追跡対象のリレーションコレクション</summary>
    private readonly ObservableCollection<RelationshipViewModel> _relationships;

    /// <summary>リレーションに基づく外部キー列ルールを適用するアクション</summary>
    private readonly Action<object?> _applyRelationshipColumnRules;

    /// <summary>対象オブジェクトごとの直近スナップショット（プロパティ名 → 値）</summary>
    /// <remarks>変更前後の差分算出に用いる</remarks>
    private readonly Dictionary<object, Dictionary<string, object?>> _trackedPropertySnapshots =
        new();

    /// <summary>追跡を一時停止中かどうか（Undo / Redo の再適用中に多重記録を防ぐ）</summary>
    private bool _suspendUndoTracking;

    /// <summary><see cref="DiagramChangeTracker"/> を生成する</summary>
    /// <param name="undoRedo">Undo / Redo スタック</param>
    /// <param name="entities">追跡対象のエンティティコレクション</param>
    /// <param name="relationships">追跡対象のリレーションコレクション</param>
    /// <param name="applyRelationshipColumnRules">リレーションに基づくカラムルール適用アクション</param>
    public DiagramChangeTracker(
        UndoRedoManager undoRedo,
        ObservableCollection<EntityViewModel> entities,
        ObservableCollection<RelationshipViewModel> relationships,
        Action<object?> applyRelationshipColumnRules
    )
    {
        _undoRedo = undoRedo;
        _entities = entities;
        _relationships = relationships;
        _applyRelationshipColumnRules = applyRelationshipColumnRules;
    }

    /// <summary>エンティティとその配下カラムの変更追跡を開始する</summary>
    public void AttachEntity(EntityViewModel entity)
    {
        entity.PropertyChanged += OnTrackedEntityPropertyChanged;
        entity.Columns.CollectionChanged += OnEntityColumnsCollectionChanged;
        CaptureTrackedProperties(entity, TrackedEntityProperties);

        foreach (var column in entity.Columns)
        {
            AttachColumn(column);
        }
    }

    /// <summary>エンティティとその配下カラムの変更追跡を終了する</summary>
    public void DetachEntity(EntityViewModel entity)
    {
        entity.PropertyChanged -= OnTrackedEntityPropertyChanged;
        entity.Columns.CollectionChanged -= OnEntityColumnsCollectionChanged;

        foreach (var column in entity.Columns)
        {
            DetachColumn(column);
        }

        _trackedPropertySnapshots.Remove(entity);
    }

    /// <summary>カラム単体の変更追跡を開始する</summary>
    private void AttachColumn(ColumnViewModel column)
    {
        column.IsPrimaryKeyChanging += OnColumnIsPrimaryKeyChanging;
        column.IsPrimaryKeyChangeCompleted += OnColumnIsPrimaryKeyChangeCompleted;
        column.PropertyChanged += OnTrackedColumnPropertyChanged;
        CaptureTrackedProperties(column, TrackedColumnProperties);
    }

    /// <summary>カラム単体の変更追跡を終了する</summary>
    private void DetachColumn(ColumnViewModel column)
    {
        column.IsPrimaryKeyChanging -= OnColumnIsPrimaryKeyChanging;
        column.IsPrimaryKeyChangeCompleted -= OnColumnIsPrimaryKeyChangeCompleted;
        column.PropertyChanged -= OnTrackedColumnPropertyChanged;
        _trackedPropertySnapshots.Remove(column);
    }

    /// <summary>リレーションの変更追跡を開始する</summary>
    public void AttachRelationship(RelationshipViewModel relationship)
    {
        relationship.TypeChanging += OnRelationshipTypeChanging;
        relationship.TypeChangeCompleted += OnRelationshipTypeChangeCompleted;
        relationship.PropertyChanged += OnRelationshipPropertyChanged;
        CaptureTrackedProperties(relationship, TrackedRelationshipProperties);
    }

    /// <summary>リレーションの変更追跡を終了する</summary>
    public void DetachRelationship(RelationshipViewModel relationship)
    {
        relationship.TypeChanging -= OnRelationshipTypeChanging;
        relationship.TypeChangeCompleted -= OnRelationshipTypeChangeCompleted;
        relationship.PropertyChanged -= OnRelationshipPropertyChanged;
        _trackedPropertySnapshots.Remove(relationship);
    }

    /// <summary>Undo 追跡を一時停止して <paramref name="action"/> を実行し、終了後にスナップショットを更新する</summary>
    /// <param name="action">追跡を止めて実行する処理</param>
    /// <param name="excludedSnapshotTarget">スナップショット再取得から除外する対象（呼び出し側で別途更新済みの対象）</param>
    public void RunWithoutTracking(Action action, object? excludedSnapshotTarget = null)
    {
        var old = _suspendUndoTracking;
        _suspendUndoTracking = true;

        try
        {
            action();
        }
        finally
        {
            _suspendUndoTracking = old;
            RefreshTrackedPropertySnapshots(excludedSnapshotTarget);
        }
    }

    /// <summary>エンティティのカラム増減に追従し、出入りしたカラムの追跡を着脱する</summary>
    private void OnEntityColumnsCollectionChanged(
        object? sender,
        NotifyCollectionChangedEventArgs e
    )
    {
        if (e.OldItems is not null)
        {
            foreach (ColumnViewModel column in e.OldItems)
            {
                DetachColumn(column);
            }
        }

        if (e.NewItems is not null)
        {
            foreach (ColumnViewModel column in e.NewItems)
            {
                AttachColumn(column);
            }
        }
    }

    /// <summary>IsPrimaryKey 変更直前に変更前の全プロパティスナップショットを取得する</summary>
    private void OnColumnIsPrimaryKeyChanging(object? sender, EventArgs e)
    {
        if (sender is ColumnViewModel column && !_suspendUndoTracking)
        {
            CaptureTrackedProperties(column, TrackedColumnProperties);
        }
    }

    /// <summary>IsPrimaryKey の連動変更完了後にスナップショット差分を Undo スタックへ Push する</summary>
    private void OnColumnIsPrimaryKeyChangeCompleted(object? sender, EventArgs e)
    {
        if (sender is ColumnViewModel column && !_suspendUndoTracking)
        {
            PushGroupedPropertyChanges(
                column,
                TrackedColumnProperties,
                afterPush: () => _applyRelationshipColumnRules(column)
            );
        }
    }

    /// <summary>カラムのプロパティ変更を追跡し、外部キー化に伴うルール再適用を行う</summary>
    private void OnTrackedColumnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not ColumnViewModel column)
        {
            return;
        }

        if (_suspendUndoTracking)
        {
            return;
        }

        // IsPrimaryKey/IsNullable の変更は IsPrimaryKeyChanging/IsPrimaryKeyChangeCompleted イベントで処理するためスキップする
        if (
            e.PropertyName
            is nameof(ColumnViewModel.IsPrimaryKey)
                or nameof(ColumnViewModel.IsNullable)
        )
        {
            return;
        }

        TrackPropertyChange(sender, e, TrackedColumnProperties);

        // 外部キー化の切り替えはリレーション側の整合性に影響するためルールを再適用する
        if (e.PropertyName == nameof(ColumnViewModel.IsForeignKey))
        {
            _applyRelationshipColumnRules(column);
        }
    }

    /// <summary>エンティティのプロパティ変更を追跡する</summary>
    private void OnTrackedEntityPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        TrackPropertyChange(sender, e, TrackedEntityProperties);
    }

    /// <summary>Relationship.Type 変更直前に変更前の全プロパティスナップショットを取得する</summary>
    private void OnRelationshipTypeChanging(object? sender, EventArgs e)
    {
        if (sender is RelationshipViewModel relationship && !_suspendUndoTracking)
        {
            CaptureTrackedProperties(relationship, TrackedRelationshipProperties);
        }
    }

    /// <summary>Type の連動変更完了後にスナップショット差分を Undo スタックへ Push する</summary>
    private void OnRelationshipTypeChangeCompleted(object? sender, EventArgs e)
    {
        if (sender is RelationshipViewModel relationship && !_suspendUndoTracking)
        {
            PushGroupedPropertyChanges(
                relationship,
                TrackedRelationshipProperties,
                afterPush: () => _applyRelationshipColumnRules(relationship)
            );
        }
    }

    /// <summary>リレーションのプロパティ変更を追跡し、外部キー列ルールを再適用する</summary>
    private void OnRelationshipPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not RelationshipViewModel relationship)
        {
            return;
        }

        if (_suspendUndoTracking)
        {
            return;
        }

        // Type 変更に関連する変更は TypeChanging/TypeChangeCompleted イベントで処理するためスキップする
        if (e.PropertyName == nameof(RelationshipViewModel.Type))
        {
            return;
        }

        if (
            e.PropertyName
            is nameof(RelationshipViewModel.SourceColumnId)
                or nameof(RelationshipViewModel.TargetColumnId)
        )
        {
            // Type 更新に伴う列付け替えは TypeChangeCompleted 側で一括記録するため、ここでは二重記録しない
            if (!relationship.IsUpdatingType)
            {
                TrackPropertyChange(sender, e, TrackedRelationshipProperties);
            }

            _applyRelationshipColumnRules(relationship);
            return;
        }

        TrackPropertyChange(sender, e, TrackedRelationshipProperties);
    }

    /// <summary>単一プロパティの変更差分を <see cref="PropertyChangeCommand"/> として記録する</summary>
    private void TrackPropertyChange(
        object? sender,
        PropertyChangedEventArgs e,
        IReadOnlyList<ITrackedProperty> trackedProperties
    )
    {
        var property = trackedProperties.FirstOrDefault(p => p.Name == e.PropertyName);

        if (_suspendUndoTracking || sender is null || property is null)
        {
            return;
        }

        // スナップショット未取得時は記録せず現在値を基準として取り込み直す
        if (
            !_trackedPropertySnapshots.TryGetValue(sender, out var snapshots)
            || !snapshots.TryGetValue(property.Name, out var oldValue)
        )
        {
            CaptureTrackedProperties(sender, trackedProperties);
            return;
        }

        var newValue = property.GetValue(sender);

        // 実質的に値が変わっていなければ履歴を汚さない
        if (Equals(oldValue, newValue))
        {
            return;
        }

        _undoRedo.Push(
            new PropertyChangeCommand(
                sender,
                property,
                oldValue,
                newValue,
                CreateAfterPropertyApplyAction(sender, property.Name)
            )
        );
        snapshots[property.Name] = newValue;
    }

    /// <summary>
    /// 変更前スナップショット全体と現在値全体を <see cref="SnapshotChangeCommand"/> として Undo スタックへ Push する
    /// </summary>
    /// <remarks>
    /// 連動変更（IsPrimaryKey ↔ IsNullable、Type ↔ SourceColumnId / TargetColumnId）を
    /// 1 回の Undo / Redo で往復させるために用いる
    /// </remarks>
    private void PushGroupedPropertyChanges(
        object sender,
        IReadOnlyList<ITrackedProperty> trackedProperties,
        Action? afterPush = null
    )
    {
        // 変更前スナップショット未取得時は記録せず現在値で取り直す
        if (!_trackedPropertySnapshots.TryGetValue(sender, out var originalSnapshots))
        {
            CaptureTrackedProperties(sender, trackedProperties);
            afterPush?.Invoke();
            RefreshTrackedPropertySnapshots(sender);
            return;
        }

        // 変更後の現在値スナップショットを取得する
        var currentSnapshots = trackedProperties.ToDictionary(p => p.Name, p => p.GetValue(sender));

        var hasChange = trackedProperties.Any(p =>
            !Equals(originalSnapshots[p.Name], currentSnapshots[p.Name])
        );

        if (hasChange)
        {
            // Undo/Redo 時に RunWithoutTracking 内で全プロパティを一括セットするコマンドを登録する
            _undoRedo.Push(
                new SnapshotChangeCommand(
                    sender,
                    new Dictionary<string, object?>(originalSnapshots),
                    currentSnapshots,
                    applySnapshot: ApplySnapshot,
                    afterApply: () => _applyRelationshipColumnRules(sender)
                )
            );
        }

        _trackedPropertySnapshots[sender] = currentSnapshots;
        afterPush?.Invoke();
        RefreshTrackedPropertySnapshots(sender);
    }

    /// <summary>スナップショット辞書の値を対象オブジェクトへ追跡停止下で一括設定する</summary>
    /// <remarks>
    /// <see cref="RelationshipViewModel"/> では列選択整合化処理を一時停止してから設定し、
    /// 復元中の意図しない列付け替えを防ぐ
    /// </remarks>
    private void ApplySnapshot(object target, IReadOnlyDictionary<string, object?> snapshot)
    {
        RunWithoutTracking(
            () =>
            {
                // Type 設定時に列選択整合化が走らないよう抑制する
                if (target is RelationshipViewModel rel)
                {
                    rel.SuppressColumnSelectionConsistency = true;

                    try
                    {
                        ApplySnapshotValues(target, snapshot);
                    }
                    finally
                    {
                        rel.SuppressColumnSelectionConsistency = false;
                    }
                }
                else
                {
                    ApplySnapshotValues(target, snapshot);
                }
            },
            excludedSnapshotTarget: target
        );
    }

    /// <summary>対象オブジェクトの型に応じた追跡プロパティ一覧を返す</summary>
    private static IReadOnlyList<ITrackedProperty> PropertiesFor(object target) =>
        target switch
        {
            EntityViewModel => TrackedEntityProperties,
            ColumnViewModel => TrackedColumnProperties,
            RelationshipViewModel => TrackedRelationshipProperties,
            _ => Array.Empty<ITrackedProperty>(),
        };

    /// <summary>スナップショット辞書の各値を対象オブジェクトの該当プロパティへ設定する</summary>
    private static void ApplySnapshotValues(
        object target,
        IReadOnlyDictionary<string, object?> snapshot
    )
    {
        var properties = PropertiesFor(target);
        foreach (var (name, value) in snapshot)
        {
            properties.FirstOrDefault(p => p.Name == name)?.SetValue(target, value);
        }
    }

    /// <summary>対象オブジェクトの現在値を差分算出の基準スナップショットとして保存する</summary>
    private void CaptureTrackedProperties(
        object target,
        IReadOnlyList<ITrackedProperty> trackedProperties
    )
    {
        var snapshots = trackedProperties.ToDictionary(p => p.Name, p => p.GetValue(target));
        _trackedPropertySnapshots[target] = snapshots;
    }

    /// <summary>全エンティティ・カラム・リレーションの基準スナップショットを現在値で取り直す</summary>
    /// <param name="excludedTarget">再取得から除外する対象（呼び出し側で更新済みの対象）</param>
    private void RefreshTrackedPropertySnapshots(object? excludedTarget = null)
    {
        foreach (var entity in _entities)
        {
            if (!ReferenceEquals(entity, excludedTarget))
            {
                CaptureTrackedProperties(entity, TrackedEntityProperties);
            }

            foreach (var column in entity.Columns)
            {
                if (!ReferenceEquals(column, excludedTarget))
                {
                    CaptureTrackedProperties(column, TrackedColumnProperties);
                }
            }
        }

        foreach (var relationship in _relationships)
        {
            if (!ReferenceEquals(relationship, excludedTarget))
            {
                CaptureTrackedProperties(relationship, TrackedRelationshipProperties);
            }
        }
    }

    /// <summary>プロパティ適用後に外部キー列ルール再適用が必要な対象に対し、その後処理を生成する</summary>
    /// <returns>後処理が不要な場合は null</returns>
    private Action? CreateAfterPropertyApplyAction(object sender, string propertyName)
    {
        if (
            sender is RelationshipViewModel
            && propertyName
                is nameof(RelationshipViewModel.Type)
                    or nameof(RelationshipViewModel.SourceColumnId)
                    or nameof(RelationshipViewModel.TargetColumnId)
        )
        {
            return () => _applyRelationshipColumnRules(sender);
        }

        if (
            sender is ColumnViewModel
            && propertyName
                is nameof(ColumnViewModel.IsPrimaryKey)
                    or nameof(ColumnViewModel.IsForeignKey)
        )
        {
            return () => _applyRelationshipColumnRules(sender);
        }

        return null;
    }
}
