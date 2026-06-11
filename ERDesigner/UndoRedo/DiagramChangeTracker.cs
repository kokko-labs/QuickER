using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using ERDesigner.ViewModels;

namespace ERDesigner.UndoRedo;

/// <summary>
/// エンティティ・カラム・リレーションのプロパティ変更を追跡し、
/// Undo/Redo 用のスナップショット差分をコマンドとして UndoRedo スタックへ積みます。
/// コレクションへの項目の出入りは所有者 (MainViewModel) から Attach/Detach で通知を受けます。
/// </summary>
public sealed class DiagramChangeTracker
{
    private static readonly string[] TrackedEntityPropertyNames =
    [
        nameof(EntityViewModel.TableName),
        nameof(EntityViewModel.Memo),
        nameof(EntityViewModel.Description),
        nameof(EntityViewModel.TitleBackgroundColor),
    ];
    private static readonly string[] TrackedRelationshipPropertyNames =
    [
        nameof(RelationshipViewModel.Type),
        nameof(RelationshipViewModel.SourceColumnId),
        nameof(RelationshipViewModel.TargetColumnId),
        nameof(RelationshipViewModel.ConstraintName),
        nameof(RelationshipViewModel.OnDelete),
        nameof(RelationshipViewModel.OnUpdate),
    ];
    private static readonly string[] TrackedColumnPropertyNames =
    [
        nameof(ColumnViewModel.Name),
        nameof(ColumnViewModel.DataType),
        nameof(ColumnViewModel.IsPrimaryKey),
        nameof(ColumnViewModel.IsForeignKey),
        nameof(ColumnViewModel.IsNullable),
        nameof(ColumnViewModel.Description),
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
        CaptureTrackedProperties(entity, TrackedEntityPropertyNames);

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
        CaptureTrackedProperties(column, TrackedColumnPropertyNames);
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
        CaptureTrackedProperties(relationship, TrackedRelationshipPropertyNames);
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
            CaptureTrackedProperties(column, TrackedColumnPropertyNames);
        }
    }

    /// <summary>IsPrimaryKey の連動変更を含む全処理完了後に呼ばれ、スナップショット差分を Undo スタックに Push します。</summary>
    private void OnColumnIsPrimaryKeyChangeCompleted(object? sender, EventArgs e)
    {
        if (sender is ColumnViewModel column && !_suspendUndoTracking)
        {
            PushGroupedPropertyChanges(column, TrackedColumnPropertyNames, afterPush: () => _applyRelationshipColumnRules(column));
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

        TrackPropertyChange(sender, e, TrackedColumnPropertyNames);

        if (e.PropertyName == nameof(ColumnViewModel.IsForeignKey))
        {
            _applyRelationshipColumnRules(column);
        }
    }

    private void OnTrackedEntityPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        TrackPropertyChange(sender, e, TrackedEntityPropertyNames);
    }

    /// <summary>Relationship.Type 変更直前に呼ばれ、変更前の全プロパティスナップショットをキャプチャします。</summary>
    private void OnRelationshipTypeChanging(object? sender, EventArgs e)
    {
        if (sender is RelationshipViewModel relationship && !_suspendUndoTracking)
        {
            CaptureTrackedProperties(relationship, TrackedRelationshipPropertyNames);
        }
    }

    /// <summary>Type の連動変更を含む全処理完了後に呼ばれ、スナップショット差分を Undo スタックに Push します。</summary>
    private void OnRelationshipTypeChangeCompleted(object? sender, EventArgs e)
    {
        if (sender is RelationshipViewModel relationship && !_suspendUndoTracking)
        {
            PushGroupedPropertyChanges(relationship, TrackedRelationshipPropertyNames, afterPush: () => _applyRelationshipColumnRules(relationship));
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
                TrackPropertyChange(sender, e, TrackedRelationshipPropertyNames);
            }

            _applyRelationshipColumnRules(relationship);
            return;
        }

        TrackPropertyChange(sender, e, TrackedRelationshipPropertyNames);
    }

    private void TrackPropertyChange(object? sender, PropertyChangedEventArgs e, IReadOnlyList<string> trackedProperties)
    {
        if (_suspendUndoTracking || sender is null || string.IsNullOrEmpty(e.PropertyName) || !trackedProperties.Contains(e.PropertyName))
        {
            return;
        }

        if (!_trackedPropertySnapshots.TryGetValue(sender, out var snapshots) || !snapshots.TryGetValue(e.PropertyName, out var oldValue))
        {
            CaptureTrackedProperties(sender, trackedProperties);
            return;
        }

        var newValue = sender.GetType().GetProperty(e.PropertyName)?.GetValue(sender);

        if (Equals(oldValue, newValue))
        {
            return;
        }

        _undoRedo.Push(new PropertyChangeCommand(sender, e.PropertyName, oldValue, newValue, CreateAfterPropertyApplyAction(sender, e.PropertyName)));
        snapshots[e.PropertyName] = newValue;
    }

    /// <summary>
    /// 対象オブジェクトについて、_trackedPropertySnapshots に保存された変更前スナップショット全体と
    /// 現在値スナップショット全体を <see cref="SnapshotChangeCommand"/> として Undo スタックに Push します。
    /// 連動変更（IsPrimaryKey↔IsNullable、Type↔SourceColumnId/TargetColumnId）を1回の Undo/Redo で往復させるために使います。
    /// </summary>
    private void PushGroupedPropertyChanges(object sender, IReadOnlyList<string> trackedProperties, Action? afterPush = null)
    {
        if (!_trackedPropertySnapshots.TryGetValue(sender, out var originalSnapshots))
        {
            CaptureTrackedProperties(sender, trackedProperties);
            afterPush?.Invoke();
            RefreshTrackedPropertySnapshots(sender);
            return;
        }

        // 変更後のスナップショットを取得
        var currentSnapshots = trackedProperties.ToDictionary(name => name, name => sender.GetType().GetProperty(name)?.GetValue(sender));

        var hasChange = trackedProperties.Any(name => !Equals(originalSnapshots[name], currentSnapshots[name]));

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
                        foreach (var (name, value) in snapshot)
                        {
                            target.GetType().GetProperty(name)?.SetValue(target, value);
                        }
                    }
                    finally
                    {
                        rel.SuppressColumnSelectionConsistency = false;
                    }
                }
                else
                {
                    foreach (var (name, value) in snapshot)
                    {
                        target.GetType().GetProperty(name)?.SetValue(target, value);
                    }
                }
            },
            excludedSnapshotTarget: target
        );
    }

    private void CaptureTrackedProperties(object target, IReadOnlyList<string> trackedProperties)
    {
        var snapshots = trackedProperties.ToDictionary(name => name, name => target.GetType().GetProperty(name)?.GetValue(target));
        _trackedPropertySnapshots[target] = snapshots;
    }

    private void RefreshTrackedPropertySnapshots(object? excludedTarget = null)
    {
        foreach (var entity in _entities)
        {
            if (!ReferenceEquals(entity, excludedTarget))
            {
                CaptureTrackedProperties(entity, TrackedEntityPropertyNames);
            }

            foreach (var column in entity.Columns)
            {
                if (!ReferenceEquals(column, excludedTarget))
                {
                    CaptureTrackedProperties(column, TrackedColumnPropertyNames);
                }
            }
        }

        foreach (var relationship in _relationships)
        {
            if (!ReferenceEquals(relationship, excludedTarget))
            {
                CaptureTrackedProperties(relationship, TrackedRelationshipPropertyNames);
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
