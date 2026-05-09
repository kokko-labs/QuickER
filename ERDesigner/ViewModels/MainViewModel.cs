using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ERDesigner.Models;
using ERDesigner.Services;
using ERDesigner.UndoRedo;
using Microsoft.Win32;

namespace ERDesigner.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private static readonly string[] TrackedEntityPropertyNames = [nameof(EntityViewModel.TableName), nameof(EntityViewModel.Memo), nameof(EntityViewModel.Description)];
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

    public UndoRedoManager UndoRedo { get; } = new();

    /// <summary>確認ダイアログを表示するかどうか。テスト時は false にできます。</summary>
    public bool IsConfirmationEnabled { get; set; } = true;

    private readonly Dictionary<object, Dictionary<string, object?>> _trackedPropertySnapshots = new();
    private bool _suspendUndoTracking;

    public ObservableCollection<EntityViewModel> Entities { get; } = new();
    public ObservableCollection<RelationshipViewModel> Relationships { get; } = new();

    [ObservableProperty]
    private EntityViewModel? _selectedEntity;

    [ObservableProperty]
    private RelationshipViewModel? _selectedRelationship;

    /// <summary>
    /// Entity awaiting a partner during relationship-creation mode.
    /// </summary>
    [ObservableProperty]
    private EntityViewModel? _pendingRelationshipSource;

    [ObservableProperty]
    private RelationshipType _pendingRelationshipType;

    [ObservableProperty]
    private bool _isRelationshipMode;

    /// <summary>プロパティパネルで選択中のカラム（DataGrid の SelectedItem）。</summary>
    [ObservableProperty]
    private ColumnViewModel? _selectedColumn;

    /// <summary>ER 図上のカラム行に「説明」を表示するか (ツールバーから ON/OFF 切替)。</summary>
    [ObservableProperty]
    private bool _showColumnDescriptionsInDiagram;

    /// <summary>ER 図上のカラム行に NULL 許容を表示するか (ツールバーから ON/OFF 切替)。</summary>
    private bool _showNullabilityInDiagram = true;

    /// <summary>キャンバスの動的幅 (エンティティの最右端 + 余白)。</summary>
    public double CanvasWidth => Math.Max(2400, Entities.Count == 0 ? 2400 : Entities.Max(e => e.X + e.Width) + 400);

    /// <summary>キャンバスの動的高さ (エンティティの最下端 + 余白)。</summary>
    public double CanvasHeight => Math.Max(1600, Entities.Count == 0 ? 1600 : Entities.Max(e => e.Y + e.DisplayHeight) + 400);

    /// <summary>型 ComboBox に表示する SQL Server のデータ型一覧。</summary>
    public IReadOnlyList<string> SqlDataTypes => SqlServerDataTypes.All;

    public MainViewModel()
    {
        Entities.CollectionChanged += OnEntitiesCollectionChanged;
        Relationships.CollectionChanged += OnRelationshipsCollectionChanged;
    }

    /// <summary>起動時に前回の自動保存ファイルを復元します。アプリ起動時に1回呼んでください。</summary>
    public void Initialize()
    {
        RestoreLastDiagram();
    }

    /// <summary>キャンバスサイズを再計算して通知します。ドラッグ終了や移動後に呼び出してください。</summary>
    public void RefreshCanvasSize()
    {
        OnPropertyChanged(nameof(CanvasWidth));
        OnPropertyChanged(nameof(CanvasHeight));
    }

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
        if (e.PropertyName is nameof(RelationshipViewModel.Type) or nameof(RelationshipViewModel.SourceColumnId) or nameof(RelationshipViewModel.TargetColumnId))
        {
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

    private void ReplaceDiagram(IEnumerable<Entity> entities, IEnumerable<Relationship> relationships, bool clearUndoHistory)
    {
        RunWithoutUndoTracking(() =>
        {
            foreach (var r in Relationships)
            {
                r.Detach();
            }

            Relationships.Clear();
            Entities.Clear();

            foreach (var entity in entities)
            {
                Entities.Add(new EntityViewModel(entity));
            }

            foreach (var relationship in relationships)
            {
                var src = Entities.FirstOrDefault(e => e.Id == relationship.SourceEntityId);
                var tgt = Entities.FirstOrDefault(e => e.Id == relationship.TargetEntityId);

                if (src is null || tgt is null)
                {
                    continue;
                }

                Relationships.Add(new RelationshipViewModel(relationship, src, tgt));
            }

            SelectedEntity = null;
            SelectedRelationship = null;
            SelectedColumn = null;
        });

        if (clearUndoHistory)
        {
            UndoRedo.Clear();
        }
    }

    /// <summary>履歴対象外でダイアグラムを置換し、必要なレイアウトもまとめて適用します。</summary>
    private void ReplaceDiagramWithoutHistory(IEnumerable<Entity> entities, IEnumerable<Relationship> relationships, bool autoLayout)
    {
        ReplaceDiagram(entities, relationships, clearUndoHistory: true);

        RunWithoutUndoTracking(() =>
        {
            AutoFitEntityWidths(Entities);

            if (autoLayout)
            {
                AutoLayoutService.LayoutTree(Entities, Relationships);
            }

            RefreshCanvasSize();
        });

        UndoRedo.Clear();
    }

    partial void OnShowColumnDescriptionsInDiagramChanged(bool value)
    {
        foreach (var entity in Entities)
        {
            entity.ShowDescriptionsInDiagram = value;
        }

        RefreshCanvasSize();
    }

    public bool ShowNullabilityInDiagram
    {
        get => _showNullabilityInDiagram;
        set
        {
            if (!SetProperty(ref _showNullabilityInDiagram, value))
            {
                return;
            }

            foreach (var entity in Entities)
            {
                entity.ShowNullabilityInDiagram = value;
            }

            RefreshCanvasSize();
        }
    }

    // ---------------- Auto-save / restore ----------------

    private static readonly string AutoSavePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ERDesigner", "last_diagram.json");
    private static readonly string UiStatePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ERDesigner", "ui_state.json");

    /// <summary>現在のダイアグラムを自動保存ファイルに書き出します。</summary>
    public void AutoSave()
    {
        try
        {
            var dir = Path.GetDirectoryName(AutoSavePath)!;
            Directory.CreateDirectory(dir);
            JsonStorageService.Save(AutoSavePath, this);
            File.WriteAllText(
                UiStatePath,
                System.Text.Json.JsonSerializer.Serialize(
                    new UiState { ShowColumnDescriptionsInDiagram = ShowColumnDescriptionsInDiagram, ShowNullabilityInDiagram = ShowNullabilityInDiagram }
                )
            );
        }
        catch
        { /* 自動保存の失敗は無視 */
        }
    }

    /// <summary>起動時に前回の自動保存ファイルを復元します。</summary>
    private void RestoreLastDiagram()
    {
        try
        {
            if (File.Exists(UiStatePath))
            {
                var uiState = System.Text.Json.JsonSerializer.Deserialize<UiState>(File.ReadAllText(UiStatePath));

                if (uiState is not null)
                {
                    ShowColumnDescriptionsInDiagram = uiState.ShowColumnDescriptionsInDiagram;
                    ShowNullabilityInDiagram = uiState.ShowNullabilityInDiagram;
                }
            }
        }
        catch { }

        if (!File.Exists(AutoSavePath))
        {
            return;
        }

        try
        {
            var diagram = JsonStorageService.Load(AutoSavePath);

            ReplaceDiagram(diagram.Entities, diagram.Relationships, clearUndoHistory: true);
        }
        catch
        { /* 復元失敗時は空で起動 */
        }
    }

    // ---------------- Commands ----------------

    [RelayCommand]
    private void NewDiagram()
    {
        if (IsConfirmationEnabled && Entities.Count > 0)
        {
            var ans = MessageBox.Show("現在のダイアグラムをクリアします。よろしいですか？", "確認", MessageBoxButton.OKCancel, MessageBoxImage.Question);

            if (ans != MessageBoxResult.OK)
            {
                return;
            }
        }

        ReplaceDiagram(Array.Empty<Entity>(), Array.Empty<Relationship>(), clearUndoHistory: true);
    }

    [RelayCommand]
    private void AddEntity()
    {
        var model = new Entity
        {
            TableName = "NewTable",
            X = 60 + Entities.Count * 30,
            Y = 60 + Entities.Count * 30,
            Columns =
            {
                new Column
                {
                    Name = "ID",
                    DataType = "int",
                    IsPrimaryKey = true,
                    IsNullable = false,
                },
            },
        };

        var vm = new EntityViewModel(model);
        UndoRedo.Execute(new AddEntityCommand(this, vm));
        SelectedEntity = vm;
    }

    [RelayCommand(CanExecute = nameof(CanRemoveEntity))]
    private void RemoveSelectedEntity()
    {
        if (SelectedEntity is null)
        {
            return;
        }

        UndoRedo.Execute(new RemoveEntityCommand(this, SelectedEntity));
        SelectedEntity = null;
    }

    private bool CanRemoveEntity() => SelectedEntity is not null;

    [RelayCommand]
    private void StartAddOneToOne() => StartRelationshipMode(RelationshipType.OneToOne);

    [RelayCommand]
    private void StartAddOneToMany() => StartRelationshipMode(RelationshipType.OneToMany);

    [RelayCommand]
    private void StartAddManyToMany() => StartRelationshipMode(RelationshipType.ManyToMany);

    [RelayCommand]
    private void CancelRelationshipMode()
    {
        IsRelationshipMode = false;
        PendingRelationshipSource = null;
    }

    private void StartRelationshipMode(RelationshipType type)
    {
        PendingRelationshipType = type;
        PendingRelationshipSource = null;
        IsRelationshipMode = true;
    }

    [RelayCommand(CanExecute = nameof(CanRemoveRelationship))]
    private void RemoveSelectedRelationship()
    {
        if (SelectedRelationship is null)
        {
            return;
        }

        UndoRedo.Execute(new RemoveRelationshipCommand(this, SelectedRelationship));
        SelectedRelationship = null;
    }

    private bool CanRemoveRelationship() => SelectedRelationship is not null;

    /// <summary>選択対象に応じて Delete キーの削除対象を切り替えます。</summary>
    [RelayCommand]
    private void DeleteSelected()
    {
        if (SelectedRelationship is not null)
        {
            RemoveSelectedRelationship();
            return;
        }

        if (SelectedEntity is not null)
        {
            RemoveSelectedEntity();
        }
    }

    [RelayCommand(CanExecute = nameof(CanAddColumn))]
    private void AddColumn()
    {
        if (SelectedEntity is null)
        {
            return;
        }

        var column = new ColumnViewModel(
            new Column
            {
                Name = "NewColumn",
                DataType = SqlServerDataTypes.All[3], // "int"
            }
        );

        UndoRedo.Execute(new AddColumnCommand(SelectedEntity.Columns, column));
        SelectedColumn = column;
    }

    private bool CanAddColumn() => SelectedEntity is not null;

    /// <summary>指定カラムを選択中エンティティから削除します。</summary>
    [RelayCommand]
    private void RemoveColumn(ColumnViewModel? column)
    {
        if (SelectedEntity is null || column is null)
        {
            return;
        }

        var affected = FindRelationshipsUsingColumn(column);
        UndoRedo.Execute(new RemoveColumnCommand(SelectedEntity.Columns, column, affected, () => ApplyRelationshipColumnRules()));

        if (SelectedColumn == column)
        {
            SelectedColumn = null;
        }
    }

    /// <summary>DataGrid で選択中のカラムを削除します（ツールバーボタン用）。</summary>
    [RelayCommand(CanExecute = nameof(CanRemoveSelectedColumn))]
    private void RemoveSelectedColumn()
    {
        if (SelectedEntity is null || SelectedColumn is null)
        {
            return;
        }

        var col = SelectedColumn;
        var affected = FindRelationshipsUsingColumn(col);
        UndoRedo.Execute(new RemoveColumnCommand(SelectedEntity.Columns, col, affected, () => ApplyRelationshipColumnRules()));
        SelectedColumn = null;
    }

    private bool CanRemoveSelectedColumn() => SelectedEntity is not null && SelectedColumn is not null;

    partial void OnSelectedColumnChanged(ColumnViewModel? value) => RemoveSelectedColumnCommand.NotifyCanExecuteChanged();

    /// <summary>指定カラムを SourceColumnId または TargetColumnId として参照しているリレーション一覧を返します。</summary>
    private IReadOnlyList<RelationshipViewModel> FindRelationshipsUsingColumn(ColumnViewModel column) =>
        Relationships.Where(r => r.SourceColumnId == column.Id || r.TargetColumnId == column.Id).ToList();

    // ---------------- Selection / Click handling ----------------

    public void OnEntityClicked(EntityViewModel entity)
    {
        if (IsRelationshipMode)
        {
            if (PendingRelationshipSource is null)
            {
                PendingRelationshipSource = entity;
                return;
            }

            if (HasSameRelationship(PendingRelationshipSource, entity))
            {
                if (IsConfirmationEnabled)
                {
                    MessageBox.Show(
                        "同じ関係のリレーションはすでに存在します。既存のリレーションを編集してください。",
                        "重複リレーション",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information
                    );
                }

                IsRelationshipMode = false;
                PendingRelationshipSource = null;
                return;
            }

            var rel = new RelationshipViewModel(
                new Relationship
                {
                    SourceEntityId = PendingRelationshipSource.Id,
                    TargetEntityId = entity.Id,
                    Type = PendingRelationshipType,
                    SourceColumnId = PendingRelationshipSource.Columns.FirstOrDefault(c => c.IsPrimaryKey)?.Id,
                    TargetColumnId = ResolveDefaultForeignKeyColumn(PendingRelationshipSource, entity)?.Id,
                    ConstraintName = $"FK_{SqlIdentifier.SafeName(entity.TableName)}_{SqlIdentifier.SafeName(PendingRelationshipSource.TableName)}",
                },
                PendingRelationshipSource,
                entity
            );

            UndoRedo.Execute(new AddRelationshipCommand(this, rel));

            IsRelationshipMode = false;
            PendingRelationshipSource = null;
        }
        else
        {
            // single-selection
            foreach (var e in Entities)
            {
                e.IsSelected = (e == entity);
            }

            SelectedEntity = entity;
            SelectedRelationship = null;
        }
    }

    /// <summary>同一の始点・終点・種別を持つリレーションが既に存在するかを判定します。</summary>
    private bool HasSameRelationship(EntityViewModel source, EntityViewModel target)
    {
        return Relationships.Any(relationship => relationship.Source == source && relationship.Target == target);
    }

    /// <summary>リレーションがクリックされたときに呼ばれます。</summary>
    public void OnRelationshipClicked(RelationshipViewModel rel)
    {
        foreach (var e in Entities)
        {
            e.IsSelected = false;
        }

        foreach (var r in Relationships)
        {
            r.IsSelected = (r == rel);
        }

        SelectedEntity = null;
        SelectedRelationship = rel;
    }

    public void OnCanvasClicked()
    {
        foreach (var e in Entities)
        {
            e.IsSelected = false;
        }

        foreach (var r in Relationships)
        {
            r.IsSelected = false;
        }

        SelectedEntity = null;
        SelectedRelationship = null;
    }

    partial void OnSelectedEntityChanged(EntityViewModel? value)
    {
        RemoveSelectedEntityCommand.NotifyCanExecuteChanged();
        AddColumnCommand.NotifyCanExecuteChanged();
        DuplicateSelectedEntityCommand.NotifyCanExecuteChanged();
    }

    partial void OnSelectedRelationshipChanged(RelationshipViewModel? value)
    {
        RemoveSelectedRelationshipCommand.NotifyCanExecuteChanged();
    }

    // ---------------- Undo/Redo ----------------

    [RelayCommand]
    private void Undo() => RunWithoutUndoTracking(() => UndoRedo.Undo());

    [RelayCommand]
    private void Redo() => RunWithoutUndoTracking(() => UndoRedo.Redo());

    [RelayCommand]
    private void EntityClick(EntityViewModel? entity)
    {
        if (entity is not null)
        {
            OnEntityClicked(entity);
        }
    }

    [RelayCommand]
    private void RelationshipClick(RelationshipViewModel? rel)
    {
        if (rel is not null)
        {
            OnRelationshipClicked(rel);
        }
    }

    [RelayCommand]
    private void CanvasClick() => OnCanvasClicked();

    // ---------------- Duplicate (Ctrl+D) ----------------

    /// <summary>選択中エンティティを Undo 可能な形で複製します（Ctrl+D 用）。</summary>
    [RelayCommand(CanExecute = nameof(CanDuplicateSelectedEntity))]
    private void DuplicateSelectedEntity()
    {
        if (SelectedEntity is null)
        {
            return;
        }

        var cmd = new DuplicateEntityCommand(this, SelectedEntity);
        UndoRedo.Execute(cmd);

        if (cmd.Duplicated is not null)
        {
            foreach (var e in Entities)
            {
                e.IsSelected = (e == cmd.Duplicated);
            }

            SelectedEntity = cmd.Duplicated;
        }
    }

    private bool CanDuplicateSelectedEntity() => SelectedEntity is not null;

    // ---------------- Auto layout ----------------

    /// <summary>エンティティを格子状に整列します。</summary>
    [RelayCommand]
    private void AutoLayoutGrid()
    {
        AutoLayoutService.LayoutGrid(Entities);
        RefreshCanvasSize();
    }

    /// <summary>エンティティをツリー状（リレーション階層）で整列します。</summary>
    [RelayCommand]
    private void AutoLayoutTree()
    {
        AutoLayoutService.LayoutTree(Entities, Relationships);
        RefreshCanvasSize();
    }

    /// <summary>全エンティティの表示幅を内容に合わせて自動調整します。</summary>
    [RelayCommand]
    private void AutoFitEntityWidths()
    {
        AutoFitEntityWidths(Entities);
        RefreshCanvasSize();
    }

    /// <summary>指定エンティティ群の表示幅を一括で自動調整します。</summary>
    private static void AutoFitEntityWidths(IEnumerable<EntityViewModel> entities)
    {
        foreach (var entity in entities)
        {
            entity.AutoFitWidth();
        }
    }

    // ---------------- Export ----------------

    /// <summary>キャンバス Visual を PNG に書き出します。</summary>
    /// <param name="visual">XAML から渡されるキャンバスの Visual。</param>
    [RelayCommand]
    private void ExportPng(object? visual)
    {
        if (visual is not Visual v)
        {
            return;
        }

        var dlg = new SaveFileDialog { Filter = "PNG Image (*.png)|*.png", DefaultExt = ".png" };

        if (dlg.ShowDialog() == true)
        {
            ImageExportService.ExportPng(v, dlg.FileName);
        }
    }

    /// <summary>現在のダイアグラムを SVG に書き出します。</summary>
    [RelayCommand]
    private void ExportSvg()
    {
        var dlg = new SaveFileDialog { Filter = "SVG Image (*.svg)|*.svg", DefaultExt = ".svg" };

        if (dlg.ShowDialog() == true)
        {
            ImageExportService.ExportSvg(this, dlg.FileName);
        }
    }

    /// <summary>現在のダイアグラムから SQL DDL を書き出します。</summary>
    [RelayCommand]
    private void ExportDdl()
    {
        var dlg = new SaveFileDialog { Filter = "SQL Script (*.sql)|*.sql", DefaultExt = ".sql" };

        if (dlg.ShowDialog() == true)
        {
            DdlExporter.SaveTo(this, dlg.FileName);
        }
    }

    /// <summary>現在のダイアグラムから Excel 形式のテーブル定義書を書き出します。</summary>
    [RelayCommand]
    private void ExportTableDefinitionDocument()
    {
        var dlg = new SaveFileDialog { Filter = "Excel Workbook (*.xlsx)|*.xlsx", DefaultExt = ".xlsx" };

        if (dlg.ShowDialog() == true)
        {
            try
            {
                TableDefinitionDocumentExporter.SaveTo(this, dlg.FileName);
                MessageBox.Show("テーブル定義書の出力が完了しました。", "完了", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"テーブル定義書を出力できませんでした。{Environment.NewLine}{ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    /// <summary>このアプリが出力した Excel 形式のテーブル定義書を読み込みます。</summary>
    [RelayCommand]
    private void ImportTableDefinitionDocument()
    {
        var dlg = new OpenFileDialog { Filter = "Excel Workbook (*.xlsx)|*.xlsx" };

        if (dlg.ShowDialog() != true)
        {
            return;
        }

        try
        {
            var diagram = TableDefinitionDocumentImporter.Load(dlg.FileName);

            if (Entities.Count > 0)
            {
                var currentSig = SqlServerSchemaImporter.ComputeSignature(Entities.Select(e => e.ToModel()), Relationships.Select(r => r.ToModel()));
                var newSig = SqlServerSchemaImporter.ComputeSignature(diagram.Entities, diagram.Relationships);

                if (currentSig != newSig)
                {
                    var ans = MessageBox.Show("現在のダイアグラムを定義書の内容で置換します。よろしいですか？", "確認", MessageBoxButton.OKCancel, MessageBoxImage.Question);

                    if (ans != MessageBoxResult.OK)
                    {
                        return;
                    }
                }
            }

            ReplaceDiagramWithoutHistory(diagram.Entities, diagram.Relationships, autoLayout: true);
            MessageBox.Show("定義書の取り込みが完了しました。", "完了", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"定義書を取り込めませんでした。{Environment.NewLine}{ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // ---------------- SQL Server 取込 ----------------

    /// <summary>SQL Server に接続してスキーマを取得し、ダイアグラムに反映します。</summary>
    [RelayCommand]
    private async Task ImportFromSqlServerAsync()
    {
        var dialog = new Views.SqlConnectionDialog { Owner = Application.Current?.MainWindow };

        if (dialog.ShowDialog() != true || dialog.ViewModel.Result is null)
        {
            return;
        }

        try
        {
            var importer = new SqlServerSchemaImporter();
            var result = await importer.ImportAsync(dialog.ViewModel.Result).ConfigureAwait(true);

            // 既存と差分があるかチェックして置換確認
            if (Entities.Count > 0)
            {
                var currentSig = SqlServerSchemaImporter.ComputeSignature(Entities.Select(e => e.ToModel()), Relationships.Select(r => r.ToModel()));
                var newSig = SqlServerSchemaImporter.ComputeSignature(result.Entities, result.Relationships);

                if (currentSig != newSig)
                {
                    var ans = MessageBox.Show("現在のダイアグラムを取得結果で置換します。よろしいですか？", "確認", MessageBoxButton.OKCancel, MessageBoxImage.Question);

                    if (ans != MessageBoxResult.OK)
                    {
                        return;
                    }
                }
            }

            // 取込結果の反映は履歴対象外にします。
            ReplaceDiagramWithoutHistory(result.Entities, result.Relationships, autoLayout: true);
        }
        catch (System.Exception ex)
        {
            MessageBox.Show("取り込みに失敗しました: " + ex.Message, "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // ---------------- DB 書き込み (スキーマ同期) ----------------

    /// <summary>SQL Server に接続し、現在のダイアグラムとの差分を ALTER 文で書き戻します。</summary>
    [RelayCommand]
    private void SyncToSqlServer()
    {
        var connDlg = new Views.SqlConnectionDialog { Owner = Application.Current?.MainWindow, Title = "SQL Server へ同期" };

        if (connDlg.ShowDialog() != true || connDlg.ViewModel.Result is null)
        {
            return;
        }

        var targetEntities = Entities.Select(e => e.ToModel()).ToList();
        var targetRelationships = Relationships.Select(r => r.ToModel()).ToList();

        var vm = new SchemaSyncDialogViewModel(connDlg.ViewModel.Result, targetEntities, targetRelationships);
        var dlg = new Views.SchemaSyncDialog(vm) { Owner = Application.Current?.MainWindow };

        dlg.ShowDialog();
    }

    // ---------------- AI 生成 ----------------

    /// <summary>ChatGPT/Ollama にスキーマ生成を依頼し、ダイアグラムへ反映します。</summary>
    [RelayCommand]
    private void GenerateFromAi()
    {
        var dialog = new Views.AiGenerateDialog { Owner = Application.Current?.MainWindow };

        if (dialog.ShowDialog() != true || dialog.ViewModel.Result is null)
        {
            return;
        }

        var (entities, relationships) = dialog.ViewModel.Result.ToDomain();

        if (entities.Count == 0)
        {
            MessageBox.Show("AI 応答にテーブルが含まれていませんでした。", "情報", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (Entities.Count > 0)
        {
            var currentSig = SqlServerSchemaImporter.ComputeSignature(Entities.Select(e => e.ToModel()), Relationships.Select(r => r.ToModel()));
            var newSig = SqlServerSchemaImporter.ComputeSignature(entities, relationships);

            if (currentSig != newSig)
            {
                var ans = MessageBox.Show("現在のダイアグラムを AI 生成結果で置換します。よろしいですか？", "確認", MessageBoxButton.OKCancel, MessageBoxImage.Question);

                if (ans != MessageBoxResult.OK)
                {
                    return;
                }
            }
        }

        // AI 生成結果の反映は履歴対象外にします。
        ReplaceDiagramWithoutHistory(entities, relationships, autoLayout: true);
    }

    // ---------------- Save / Load ----------------

    [RelayCommand]
    private void Save()
    {
        var dlg = new SaveFileDialog { Filter = "ER Diagram (*.json)|*.json", DefaultExt = ".json" };

        if (dlg.ShowDialog() == true)
        {
            JsonStorageService.Save(dlg.FileName, this);
        }
    }

    /// <summary>JSON ファイルからダイアグラムを読み込みます（ダイアログ表示）。</summary>
    [RelayCommand]
    private void Open()
    {
        var dlg = new OpenFileDialog { Filter = "ER Diagram (*.json)|*.json" };

        if (dlg.ShowDialog() != true)
        {
            return;
        }

        var diagram = JsonStorageService.Load(dlg.FileName);

        ReplaceDiagram(diagram.Entities, diagram.Relationships, clearUndoHistory: true);
    }

    /// <summary>リレーションに基づいて各カラムの PK/FK 編集可否と FK 状態を同期します。</summary>
    public void ApplyRelationshipColumnRules(object? excludedSnapshotTarget = null)
    {
        RunWithoutUndoTracking(
            () =>
            {
                foreach (var entity in Entities)
                {
                    foreach (var column in entity.Columns)
                    {
                        column.IsPrimaryKeyEditable = true;
                        column.IsForeignKeyEditable = true;

                        if (column.IsForeignKeyManagedByRelationship)
                        {
                            column.IsForeignKey = false;
                            column.IsForeignKeyManagedByRelationship = false;
                        }
                    }
                }

                foreach (var relationship in Relationships)
                {
                    LockRelationshipColumns(relationship);
                }
            },
            excludedSnapshotTarget
        );
    }

    /// <summary>対象リレーションで使用中の列をロックし、FK フラグを同期します。</summary>
    private static void LockRelationshipColumns(RelationshipViewModel relationship)
    {
        var sourceColumn = relationship.SourceColumnId is null ? null : relationship.Source.Columns.FirstOrDefault(c => c.Id == relationship.SourceColumnId);
        var targetColumn = relationship.TargetColumnId is null ? null : relationship.Target.Columns.FirstOrDefault(c => c.Id == relationship.TargetColumnId);

        if (sourceColumn is not null)
        {
            sourceColumn.IsPrimaryKeyEditable = false;
            sourceColumn.IsForeignKeyEditable = false;
        }

        if (targetColumn is not null)
        {
            targetColumn.IsPrimaryKeyEditable = false;
            targetColumn.IsForeignKeyEditable = false;
            targetColumn.IsForeignKeyManagedByRelationship = true;
            targetColumn.IsForeignKey = true;
        }
    }

    /// <summary>追加時の既定 FK 列を解決します。PK 同名列を優先し、無ければ従来どおり最初の非 PK 列を採用します。</summary>
    private static ColumnViewModel? ResolveDefaultForeignKeyColumn(EntityViewModel source, EntityViewModel target)
    {
        if (ReferenceEquals(source, target))
        {
            var sourcePrimaryKeyByName = source.Columns.FirstOrDefault(c => c.IsPrimaryKey);

            return source.Columns.FirstOrDefault(c => !c.IsPrimaryKey && !string.Equals(c.Name, sourcePrimaryKeyByName?.Name, StringComparison.OrdinalIgnoreCase));
        }

        var sourcePrimaryKey = source.Columns.FirstOrDefault(c => c.IsPrimaryKey);

        if (sourcePrimaryKey is null)
        {
            return target.Columns.FirstOrDefault(c => !c.IsPrimaryKey) ?? target.Columns.FirstOrDefault();
        }

        var sameName = target.Columns.FirstOrDefault(c => string.Equals(c.Name, sourcePrimaryKey.Name, StringComparison.OrdinalIgnoreCase));

        if (sameName is not null)
        {
            return sameName;
        }

        return target.Columns.FirstOrDefault(c => !c.IsPrimaryKey) ?? target.Columns.FirstOrDefault();
    }

    private sealed class UiState
    {
        public bool ShowColumnDescriptionsInDiagram { get; init; }

        public bool ShowNullabilityInDiagram { get; init; } = true;
    }
}
