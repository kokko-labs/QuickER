using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using ERDesigner.Models;
using ERDesigner.ViewModels;

namespace ERDesigner.UndoRedo;

/// <summary>
/// エンティティ・カラム・リレーションのプロパティ変更を追跡し、
/// Undo/Redo 用のスナップショット差分をコマンドとして UndoRedo スタックへ積みます。
/// コレクションへの項目の出入りは所有者 (MainViewModel) から Attach/Detach で通知を受けます。
/// </summary>
public sealed class DiagramChangeTracker
{
    private static readonly ITrackedProperty[] TrackedEntityProperties =
    [
        new TrackedProperty<EntityViewModel>(nameof(EntityViewModel.TableName), x => x.TableName, (x, v) => x.TableName = (string)v!),
        new TrackedProperty<EntityViewModel>(nameof(EntityViewModel.Memo), x => x.Memo, (x, v) => x.Memo = (string)v!),
        new TrackedProperty<EntityViewModel>(nameof(EntityViewModel.Description), x => x.Description, (x, v) => x.Description = (string)v!),
        new TrackedProperty<EntityViewModel>(nameof(EntityViewModel.TitleBackgroundColor), x => x.TitleBackgroundColor, (x, v) => x.TitleBackgroundColor = (string)v!),
    ];
    private static readonly ITrackedProperty[] TrackedRelationshipProperties =
    [
        new TrackedProperty<RelationshipViewModel>(nameof(RelationshipViewModel.Type), x => x.Type, (x, v) => x.Type = (RelationshipType)v!),
        new TrackedProperty<RelationshipViewModel>(nameof(RelationshipViewModel.SourceColumnId), x => x.SourceColumnId, (x, v) => x.SourceColumnId = (Guid?)v),
        new TrackedProperty<RelationshipViewModel>(nameof(RelationshipViewModel.TargetColumnId), x => x.TargetColumnId, (x, v) => x.TargetColumnId = (Guid?)v),
        new TrackedProperty<RelationshipViewModel>(nameof(RelationshipViewModel.ConstraintName), x => x.ConstraintName, (x, v) => x.ConstraintName = (string?)v),
        new TrackedProperty<RelationshipViewModel>(nameof(RelationshipViewModel.OnDelete), x => x.OnDelete, (x, v) => x.OnDelete = (ForeignKeyReferentialAction)v!),
        new TrackedProperty<RelationshipViewModel>(nameof(RelationshipViewModel.OnUpdate), x => x.OnUpdate, (x, v) => x.OnUpdate = (ForeignKeyReferentialAction)v!),
    ];
    private static readonly ITrackedProperty[] TrackedColumnProperties =
    [
        new TrackedProperty<ColumnViewModel>(nameof(ColumnViewModel.Name), x => x.Name, (x, v) => x.Name = (string)v!),
        new TrackedProperty<ColumnViewModel>(nameof(ColumnViewModel.DataType), x => x.DataType, (x, v) => x.DataType = (string)v!),
        new TrackedProperty<ColumnViewModel>(nameof(ColumnViewModel.IsPrimaryKey), x => x.IsPrimaryKey, (x, v) => x.IsPrimaryKey = (bool)v!),
        new TrackedProperty<ColumnViewModel>(nameof(ColumnViewModel.IsForeignKey), x => x.IsForeignKey, (x, v) => x.IsForeignKey = (bool)v!),
        new TrackedProperty<ColumnViewModel>(nameof(ColumnViewModel.IsNullable), x => x.IsNullable, (x, v) => x.IsNullable = (bool)v!),
        new TrackedProperty<ColumnViewModel>(nameof(ColumnViewModel.Description), x => x.Description, (x, v) => x.Description = (string)v!),
    ];

    private readonly UndoRedoManager _undoRedo;
    private readonly ObservableCollection<EntityViewModel> _entities;
    private readonly ObservableCollection<RelationshipViewModel> _relationships;
    private readonly Action<object?> _applyRelationshipColumnRules;

    private readonly Dictionary<object, Dictionary<string, object?>> _trackedPropertySnapshots = new();
    private bool _suspendUndoTracking;

    /// <summary>新しい <see cref="DiagramChangeTracker"/> を生成します。</summary>
    /// <param name="undoRedo">Undo/Redo スタック。</param>
    /// <param name="entities">追跡対象のエンティティコレクション。</param>
    /// <param name="relationships">追跡対象のリレーションコレクション。</param>
    /// <param name="applyRelationshipColumnRules">リレーションに基づくカラムルール適用アクション。</param>
    public DiagramChangeTracker(
        UndoRedoManager undoRedo,
        ObservableCollection<EntityViewModel> entities,
        ObservableCollection<RelationshipViewModel> relationships,
        Action<object?> applyRelationshipColumnRules)
    {
        _undoRedo = undoRedo;
        _entities = entities;
        _relationships = relationships;
        _applyRelationshipColumnRules = applyRelationshipColumnRules;
    }

    /// <summary>エンティティの変更追跡を開始します。</summary>
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

    /// <summary>エンティティの変更追跡を終了します。</summary>
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

    private void AttachColumn(ColumnViewModel column)
    {
        column.IsPrimaryKeyChanging += OnColumnIsPrimaryKeyChanging;
        column.IsPrimaryKeyChangeCompleted += OnColumnIsPrimaryKeyChangeCompleted;
        column.PropertyChanged += OnTrackedColumnPropertyChanged;
        CaptureTrackedProperties(column, TrackedColumnProperties);
    }

    private void DetachColumn(ColumnViewModel column)
    {
        column.IsPrimaryKeyChanging -= OnColumnIsPrimaryKeyChanging;
        column.IsPrimaryKeyChangeCompleted -= OnColumnIsPrimaryKeyChangeCompleted;
        column.PropertyChanged -= OnTrackedColumnPropertyChanged;
        _trackedPropertySnapshots.Remove(column);
    }

    /// <summary>リレーションの変更追跡を開始します。</summary>
    public void AttachRelationship(RelationshipViewModel relationship)
    {
        relationship.TypeChanging += OnRelationshipTypeChanging;
        relationship.TypeChangeCompleted += OnRelationshipTypeChangeCompleted;
        relationship.PropertyChanged += OnRelationshipPropertyChanged;
        CaptureTrackedProperties(relationship, TrackedRelationshipProperties);
    }

    /// <summary>リレーションの変更追跡を終了します。</summary>
    public void DetachRelationship(RelationshipViewModel relationship)
    {
        relationship.TypeChanging -= OnRelationshipTypeChanging;
        relationship.TypeChangeCompleted -= OnRelationshipTypeChangeCompleted;
        relationship.PropertyChanged -= OnRelationshipPropertyChanged;
        _trackedPropertySnapshots.Remove(relationship);
    }

    /// <summary>
    /// Undo 追跡を一時停止して <paramref name="action"/> を実行し、終了後にスナップショットを更新します。
    /// </summary>
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

    private void OnEntityColumnsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
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

    /// <summary>IsPrimaryKey 変更直前に呼ばれ、変更前の全プロパティスナップショットをキャプチャします。</summary>
    private void OnColumnIsPrimaryKeyChanging(object? sender, EventArgs e)
    {
        if (sender is ColumnViewModel column && !_suspendUndoTracking)
        {
            CaptureTrackedProperties(column, TrackedColumnProperties);
        }
    }

    /// <summary>IsPrimaryKey の連動変更を含む全処理完了後に呼ばれ、スナップショット差分を Undo スタックに Push します。</summary>
    private void OnColumnIsPrimaryKeyChangeCompleted(object? sender, EventArgs e)
    {
        if (sender is ColumnViewModel column && !_suspendUndoTracking)
        {
            PushGroupedPropertyChanges(column, TrackedColumnProperties, afterPush: () => _applyRelationshipColumnRules(column));
        }
    }

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
        if (e.PropertyName is nameof(ColumnViewModel.IsPrimaryKey) or nameof(ColumnViewModel.IsNullable))
        {
            return;
        }

        TrackPropertyChange(sender, e, TrackedColumnProperties);

        if (e.PropertyName == nameof(ColumnViewModel.IsForeignKey))
        {
            _applyRelationshipColumnRules(column);
        }
    }

    private void OnTrackedEntityPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        TrackPropertyChange(sender, e, TrackedEntityProperties);
    }

    /// <summary>Relationship.Type 変更直前に呼ばれ、変更前の全プロパティスナップショットをキャプチャします。</summary>
    private void OnRelationshipTypeChanging(object? sender, EventArgs e)
    {
        if (sender is RelationshipViewModel relationship && !_suspendUndoTracking)
        {
            CaptureTrackedProperties(relationship, TrackedRelationshipProperties);
        }
    }

    /// <summary>Type の連動変更を含む全処理完了後に呼ばれ、スナップショット差分を Undo スタックに Push します。</summary>
    private void OnRelationshipTypeChangeCompleted(object? sender, EventArgs e)
    {
        if (sender is RelationshipViewModel relationship && !_suspendUndoTracking)
        {
            PushGroupedPropertyChanges(relationship, TrackedRelationshipProperties, afterPush: () => _applyRelationshipColumnRules(relationship));
        }
    }

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

        if (e.PropertyName is nameof(RelationshipViewModel.SourceColumnId) or nameof(RelationshipViewModel.TargetColumnId))
        {
            if (!relationship.IsUpdatingType)
            {
                TrackPropertyChange(sender, e, TrackedRelationshipProperties);
            }

            _applyRelationshipColumnRules(relationship);
            return;
        }

        TrackPropertyChange(sender, e, TrackedRelationshipProperties);
    }

    private void TrackPropertyChange(object? sender, PropertyChangedEventArgs e, IReadOnlyList<ITrackedProperty> trackedProperties)
    {
        var property = trackedProperties.FirstOrDefault(p => p.Name == e.PropertyName);

        if (_suspendUndoTracking || sender is null || property is null)
        {
            return;
        }

        if (!_trackedPropertySnapshots.TryGetValue(sender, out var snapshots) || !snapshots.TryGetValue(property.Name, out var oldValue))
        {
            CaptureTrackedProperties(sender, trackedProperties);
            return;
        }

        var newValue = property.GetValue(sender);

        if (Equals(oldValue, newValue))
        {
            return;
        }

        _undoRedo.Push(new PropertyChangeCommand(sender, property, oldValue, newValue, CreateAfterPropertyApplyAction(sender, property.Name)));
        snapshots[property.Name] = newValue;
    }

    /// <summary>
    /// 対象オブジェクトについて、_trackedPropertySnapshots に保存された変更前スナップショット全体と
    /// 現在値スナップショット全体を <see cref="SnapshotChangeCommand"/> として Undo スタックに Push します。
    /// 連動変更（IsPrimaryKey↔IsNullable、Type↔SourceColumnId/TargetColumnId）を1回の Undo/Redo で往復させるために使います。
    /// </summary>
    private void PushGroupedPropertyChanges(object sender, IReadOnlyList<ITrackedProperty> trackedProperties, Action? afterPush = null)
    {
        if (!_trackedPropertySnapshots.TryGetValue(sender, out var originalSnapshots))
        {
            CaptureTrackedProperties(sender, trackedProperties);
            afterPush?.Invoke();
            RefreshTrackedPropertySnapshots(sender);
            return;
        }

        // 変更後のスナップショットを取得
        var currentSnapshots = trackedProperties.ToDictionary(p => p.Name, p => p.GetValue(sender));

        var hasChange = trackedProperties.Any(p => !Equals(originalSnapshots[p.Name], currentSnapshots[p.Name]));

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

    /// <summary>
    /// スナップショット辞書の値をターゲットオブジェクトに RunWithoutTracking 内で一括セットします。
    /// RelationshipViewModel の場合は EnsureColumnSelectionConsistency を一時停止してからセットします。
    /// </summary>
    private void ApplySnapshot(object target, IReadOnlyDictionary<string, object?> snapshot)
    {
        RunWithoutTracking(
            () =>
            {
                // RelationshipViewModel は Type セット時に EnsureColumnSelectionConsistency が走るのを抑制する
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

    /// <summary>対象オブジェクトの型に応じた追跡プロパティ一覧を返します。</summary>
    private static IReadOnlyList<ITrackedProperty> PropertiesFor(object target) =>
        target switch
        {
            EntityViewModel => TrackedEntityProperties,
            ColumnViewModel => TrackedColumnProperties,
            RelationshipViewModel => TrackedRelationshipProperties,
            _ => Array.Empty<ITrackedProperty>(),
        };

    private static void ApplySnapshotValues(object target, IReadOnlyDictionary<string, object?> snapshot)
    {
        var properties = PropertiesFor(target);
        foreach (var (name, value) in snapshot)
        {
            properties.FirstOrDefault(p => p.Name == name)?.SetValue(target, value);
        }
    }

    private void CaptureTrackedProperties(object target, IReadOnlyList<ITrackedProperty> trackedProperties)
    {
        var snapshots = trackedProperties.ToDictionary(p => p.Name, p => p.GetValue(target));
        _trackedPropertySnapshots[target] = snapshots;
    }

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

    private Action? CreateAfterPropertyApplyAction(object sender, string propertyName)
    {
        if (
            sender is RelationshipViewModel
            && propertyName is nameof(RelationshipViewModel.Type) or nameof(RelationshipViewModel.SourceColumnId) or nameof(RelationshipViewModel.TargetColumnId)
        )
        {
            return () => _applyRelationshipColumnRules(sender);
        }

        if (sender is ColumnViewModel && propertyName is nameof(ColumnViewModel.IsPrimaryKey) or nameof(ColumnViewModel.IsForeignKey))
        {
            return () => _applyRelationshipColumnRules(sender);
        }

        return null;
    }
}
