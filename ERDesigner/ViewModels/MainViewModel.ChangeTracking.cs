using System.Collections.Specialized;
using System.ComponentModel;
using ERDesigner.UndoRedo;

namespace ERDesigner.ViewModels;

/// <summary>
/// MainViewModel の変更追跡基盤 (partial)。
/// エンティティ・カラム・リレーションのコレクション変更とプロパティ変更を購読し、
/// Undo/Redo 用のスナップショット差分をコマンドとして UndoRedo スタックへ積みます。
/// </summary>
public partial class MainViewModel
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

    private readonly Dictionary<object, Dictionary<string, object?>> _trackedPropertySnapshots = new();
    private bool _suspendUndoTracking;

    private void OnEntitiesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
        {
            foreach (EntityViewModel entity in e.OldItems)
            {
                entity.PropertyChanged -= OnEntityPropertyChanged;
                entity.PropertyChanged -= OnTrackedEntityPropertyChanged;
                entity.Columns.CollectionChanged -= OnEntityColumnsCollectionChanged;

                foreach (var column in entity.Columns)
                {
                    column.IsPrimaryKeyChanging -= OnColumnIsPrimaryKeyChanging;
                    column.IsPrimaryKeyChangeCompleted -= OnColumnIsPrimaryKeyChangeCompleted;
                    column.PropertyChanged -= OnTrackedColumnPropertyChanged;
                    _trackedPropertySnapshots.Remove(column);
                }

                _trackedPropertySnapshots.Remove(entity);
            }
        }

        if (e.NewItems is not null)
        {
            foreach (EntityViewModel entity in e.NewItems)
            {
                entity.ShowDescriptionsInDiagram = ShowColumnDescriptionsInDiagram;
                entity.ShowNullabilityInDiagram = ShowNullabilityInDiagram;
                entity.PropertyChanged += OnEntityPropertyChanged;
                entity.PropertyChanged += OnTrackedEntityPropertyChanged;
                entity.Columns.CollectionChanged += OnEntityColumnsCollectionChanged;
                CaptureTrackedProperties(entity, TrackedEntityPropertyNames);

                foreach (var column in entity.Columns)
                {
                    column.IsPrimaryKeyChanging += OnColumnIsPrimaryKeyChanging;
                    column.IsPrimaryKeyChangeCompleted += OnColumnIsPrimaryKeyChangeCompleted;
                    column.PropertyChanged += OnTrackedColumnPropertyChanged;
                    CaptureTrackedProperties(column, TrackedColumnPropertyNames);
                }
            }
        }

        OnPropertyChanged(nameof(Entities));
        RefreshCanvasSize();
    }

    private void OnEntityPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(EntityViewModel.X) or nameof(EntityViewModel.Y) or nameof(EntityViewModel.Width) or nameof(EntityViewModel.DisplayHeight))
        {
            RefreshCanvasSize();
        }
    }

    private void OnTrackedEntityPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        TrackPropertyChange(sender, e, TrackedEntityPropertyNames);
    }

    private void OnEntityColumnsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
        {
            foreach (ColumnViewModel column in e.OldItems)
            {
                column.IsPrimaryKeyChanging -= OnColumnIsPrimaryKeyChanging;
                column.IsPrimaryKeyChangeCompleted -= OnColumnIsPrimaryKeyChangeCompleted;
                column.PropertyChanged -= OnTrackedColumnPropertyChanged;
                _trackedPropertySnapshots.Remove(column);
            }
        }

        if (e.NewItems is not null)
        {
            foreach (ColumnViewModel column in e.NewItems)
            {
                column.IsPrimaryKeyChanging += OnColumnIsPrimaryKeyChanging;
                column.IsPrimaryKeyChangeCompleted += OnColumnIsPrimaryKeyChangeCompleted;
                column.PropertyChanged += OnTrackedColumnPropertyChanged;
                CaptureTrackedProperties(column, TrackedColumnPropertyNames);
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
            PushGroupedPropertyChanges(column, TrackedColumnPropertyNames, afterPush: () => ApplyRelationshipColumnRules(column));
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
            ApplyRelationshipColumnRules(column);
        }
    }

    private void OnRelationshipsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
        {
            foreach (RelationshipViewModel relationship in e.OldItems)
            {
                relationship.TypeChanging -= OnRelationshipTypeChanging;
                relationship.TypeChangeCompleted -= OnRelationshipTypeChangeCompleted;
                relationship.PropertyChanged -= OnRelationshipPropertyChanged;
                _trackedPropertySnapshots.Remove(relationship);
            }
        }

        if (e.NewItems is not null)
        {
            foreach (RelationshipViewModel relationship in e.NewItems)
            {
                relationship.TypeChanging += OnRelationshipTypeChanging;
                relationship.TypeChangeCompleted += OnRelationshipTypeChangeCompleted;
                relationship.PropertyChanged += OnRelationshipPropertyChanged;
                CaptureTrackedProperties(relationship, TrackedRelationshipPropertyNames);
            }
        }

        ApplyRelationshipColumnRules();
        OnPropertyChanged(nameof(Relationships));
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
            PushGroupedPropertyChanges(relationship, TrackedRelationshipPropertyNames, afterPush: () => ApplyRelationshipColumnRules(relationship));
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

            ApplyRelationshipColumnRules(relationship);
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

        UndoRedo.Push(new PropertyChangeCommand(sender, e.PropertyName, oldValue, newValue, CreateAfterPropertyApplyAction(sender, e.PropertyName)));
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
            // Undo/Redo 時に RunWithoutUndoTracking 内で全プロパティを一括セットするコマンドを登録する
            UndoRedo.Push(
                new SnapshotChangeCommand(
                    sender,
                    new Dictionary<string, object?>(originalSnapshots),
                    currentSnapshots,
                    applySnapshot: ApplySnapshot,
                    afterApply: () => ApplyRelationshipColumnRules(sender)
                )
            );
        }

        _trackedPropertySnapshots[sender] = currentSnapshots;
        afterPush?.Invoke();
        RefreshTrackedPropertySnapshots(sender);
    }

    /// <summary>
    /// スナップショット辞書の値をターゲットオブジェクトに RunWithoutUndoTracking 内で一括セットします。
    /// RelationshipViewModel の場合は EnsureColumnSelectionConsistency を一時停止してからセットします。
    /// </summary>
    private void ApplySnapshot(object target, IReadOnlyDictionary<string, object?> snapshot)
    {
        RunWithoutUndoTracking(
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
        foreach (var entity in Entities)
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

        foreach (var relationship in Relationships)
        {
            if (!ReferenceEquals(relationship, excludedTarget))
            {
                CaptureTrackedProperties(relationship, TrackedRelationshipPropertyNames);
            }
        }
    }

    private void RunWithoutUndoTracking(Action action, object? excludedSnapshotTarget = null)
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

    private Action? CreateAfterPropertyApplyAction(object sender, string propertyName)
    {
        if (
            sender is RelationshipViewModel
            && propertyName is nameof(RelationshipViewModel.Type) or nameof(RelationshipViewModel.SourceColumnId) or nameof(RelationshipViewModel.TargetColumnId)
        )
        {
            return () => ApplyRelationshipColumnRules(sender);
        }

        if (sender is ColumnViewModel && propertyName is nameof(ColumnViewModel.IsPrimaryKey) or nameof(ColumnViewModel.IsForeignKey))
        {
            return () => ApplyRelationshipColumnRules(sender);
        }

        return null;
    }
}
