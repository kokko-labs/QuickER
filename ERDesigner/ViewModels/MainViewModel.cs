using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ERDesigner.Models;
using ERDesigner.Services;
using ERDesigner.UndoRedo;

namespace ERDesigner.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private const string DefaultCSharpNamespace = "Generated.Entities";

    public UndoRedoManager UndoRedo { get; } = new();

    public IRelayCommand CopySelectedEntityCommand { get; }

    public IRelayCommand PasteCopiedEntityCommand { get; }

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

    /// <summary>DataGrid のコピー元として保持するカラム内容です。</summary>
    private Column? _copiedColumn;

    /// <summary>エンティティコピー用に保持するモデル内容です。</summary>
    private Entity? _copiedEntity;

    /// <summary>同じコピー元からのペースト回数です。</summary>
    private int _copiedEntityPasteCount;

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

    /// <summary>C# 生成時に使用する既定の namespace です。</summary>
    public string CSharpGenerationNamespace { get; set; } = DefaultCSharpNamespace;

    /// <summary>エンティティ見出しの背景色プリセット一覧です。</summary>
    public IReadOnlyList<EntityTitleColorOption> EntityTitleColorOptions => EntityTitleColorPalette.Options;

    /// <summary>確認・通知ダイアログの表示先です。テストではスタブに差し替えられます。</summary>
    private readonly IDialogService _dialogs;

    private readonly DiagramChangeTracker _changeTracker;

    public MainViewModel()
        : this(new MessageBoxDialogService()) { }

    /// <summary>ダイアログ表示を差し替えたい場合 (単体テスト等) に使用します。</summary>
    public MainViewModel(IDialogService dialogService)
    {
        _dialogs = dialogService;
        _changeTracker = new DiagramChangeTracker(UndoRedo, Entities, Relationships, ApplyRelationshipColumnRules);
        CopySelectedEntityCommand = new RelayCommand(CopySelectedEntity, CanCopySelectedEntity);
        PasteCopiedEntityCommand = new RelayCommand(PasteCopiedEntity, CanPasteCopiedEntity);
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

    private void ReplaceDiagram(IEnumerable<Entity> entities, IEnumerable<Relationship> relationships, bool clearUndoHistory)
    {
        _changeTracker.RunWithoutTracking(() =>
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
            ClearUndoRedoHistory();
        }
    }

    /// <summary>Undo/Redo 履歴をクリアし、ツールバーの有効状態も更新します。</summary>
    private void ClearUndoRedoHistory()
    {
        UndoRedo.Clear();
        OnPropertyChanged(nameof(UndoRedo));
    }

    /// <summary>現在のエンティティ位置を履歴用にスナップショットします。</summary>
    private Dictionary<Guid, (double X, double Y)> CaptureEntityLayoutSnapshot()
    {
        return Entities.ToDictionary(entity => entity.Id, entity => (entity.X, entity.Y));
    }

    /// <summary>整列操作を適用し、Undo/Redo できる履歴として登録します。</summary>
    private void ApplyLayoutWithUndo(Action layoutAction, string description)
    {
        var before = CaptureEntityLayoutSnapshot();

        _changeTracker.RunWithoutTracking(() =>
        {
            layoutAction();
            RefreshCanvasSize();
        });

        var after = CaptureEntityLayoutSnapshot();

        if (before.Count == after.Count && before.All(pair => after.TryGetValue(pair.Key, out var value) && value == pair.Value))
        {
            return;
        }

        UndoRedo.Push(new ArrangeEntitiesCommand(Entities, before, after, RefreshCanvasSize, description));
    }

    /// <summary>履歴対象外でダイアグラムを置換し、必要なレイアウトもまとめて適用します。</summary>
    private void ReplaceDiagramWithoutHistory(IEnumerable<Entity> entities, IEnumerable<Relationship> relationships, bool autoLayout)
    {
        ReplaceDiagram(entities, relationships, clearUndoHistory: true);

        _changeTracker.RunWithoutTracking(() =>
        {
            AutoFitEntityWidths(Entities);

            if (autoLayout)
            {
                AutoLayoutService.LayoutTree(Entities, Relationships);
            }

            RefreshCanvasSize();
        });

        ClearUndoRedoHistory();
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

    // ---------------- Commands ----------------

    [RelayCommand]
    private void NewDiagram()
    {
        if (Entities.Count > 0 && !_dialogs.Confirm("現在のダイアグラムをクリアします。よろしいですか？", "確認"))
        {
            return;
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

    /// <summary>選択中エンティティの内容をコピーして内部バッファへ保持します。</summary>
    private void CopySelectedEntity()
    {
        if (SelectedEntity is null)
        {
            return;
        }

        _copiedEntity = SelectedEntity.ToModel();
        _copiedEntityPasteCount = 0;
        PasteCopiedEntityCommand.NotifyCanExecuteChanged();
    }

    private bool CanCopySelectedEntity() => SelectedEntity is not null;

    /// <summary>コピー済みエンティティを少しずらした位置へ複製追加します。</summary>
    private void PasteCopiedEntity()
    {
        if (_copiedEntity is null)
        {
            return;
        }

        _copiedEntityPasteCount++;
        var pastedEntity = CreateEntityCopy(_copiedEntity, _copiedEntityPasteCount);
        UndoRedo.Execute(new AddEntityCommand(this, pastedEntity));
        SelectSingleEntity(pastedEntity);
    }

    private bool CanPasteCopiedEntity() => _copiedEntity is not null;

    /// <summary>選択中カラムの内容をコピーして内部バッファへ保持します。</summary>
    [RelayCommand(CanExecute = nameof(CanCopySelectedColumn))]
    private void CopySelectedColumn()
    {
        if (SelectedColumn is null)
        {
            return;
        }

        _copiedColumn = SelectedColumn.ToModel().Clone(preserveId: false);
        PasteCopiedColumnCommand.NotifyCanExecuteChanged();
    }

    private bool CanCopySelectedColumn() => SelectedColumn is not null;

    /// <summary>コピー済みカラムを選択中カラムの直下へ複製追加します。</summary>
    [RelayCommand(CanExecute = nameof(CanPasteCopiedColumn))]
    private void PasteCopiedColumn()
    {
        if (SelectedEntity is null || SelectedColumn is null || _copiedColumn is null)
        {
            return;
        }

        var insertIndex = SelectedEntity.Columns.IndexOf(SelectedColumn);

        if (insertIndex < 0)
        {
            return;
        }

        var pastedColumn = new ColumnViewModel(_copiedColumn.Clone(preserveId: false));
        UndoRedo.Execute(new AddColumnCommand(SelectedEntity.Columns, pastedColumn, insertIndex + 1));
        SelectedColumn = pastedColumn;
    }

    private bool CanPasteCopiedColumn() => SelectedEntity is not null && SelectedColumn is not null && _copiedColumn is not null;

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

    partial void OnSelectedColumnChanged(ColumnViewModel? value)
    {
        RemoveSelectedColumnCommand.NotifyCanExecuteChanged();
        CopySelectedColumnCommand.NotifyCanExecuteChanged();
        PasteCopiedColumnCommand.NotifyCanExecuteChanged();
    }

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
                _dialogs.ShowInformation("同じ関係のリレーションはすでに存在します。既存のリレーションを編集してください。", "重複リレーション");

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
            SelectSingleEntity(entity);
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
        CopySelectedEntityCommand.NotifyCanExecuteChanged();
        PasteCopiedColumnCommand.NotifyCanExecuteChanged();
        DuplicateSelectedEntityCommand.NotifyCanExecuteChanged();
    }

    partial void OnSelectedRelationshipChanged(RelationshipViewModel? value)
    {
        RemoveSelectedRelationshipCommand.NotifyCanExecuteChanged();
    }

    // ---------------- Undo/Redo ----------------

    [RelayCommand]
    private void Undo() => _changeTracker.RunWithoutTracking(() => UndoRedo.Undo());

    [RelayCommand]
    private void Redo() => _changeTracker.RunWithoutTracking(() => UndoRedo.Redo());

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
            SelectSingleEntity(cmd.Duplicated);
        }
    }

    private bool CanDuplicateSelectedEntity() => SelectedEntity is not null;

    // ---------------- Auto layout ----------------

    /// <summary>エンティティを格子状に整列します。</summary>
    [RelayCommand]
    private void AutoLayoutGrid()
    {
        ApplyLayoutWithUndo(() => AutoLayoutService.LayoutGrid(Entities), "整列(格子)");
    }

    /// <summary>エンティティをツリー状（リレーション階層）で整列します。</summary>
    [RelayCommand]
    private void AutoLayoutTree()
    {
        ApplyLayoutWithUndo(() => AutoLayoutService.LayoutTree(Entities, Relationships), "整列(木)");
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

    /// <summary>リレーションに基づいて各カラムの PK/FK 編集可否と FK 状態を同期します。</summary>
    public void ApplyRelationshipColumnRules(object? excludedSnapshotTarget = null)
    {
        _changeTracker.RunWithoutTracking(
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

        // 参照先の PK と同名の場合は同じ意味の列（同テーブル固有 ID）なので FK 列として選ばない
        var sameName = target.Columns.FirstOrDefault(c => string.Equals(c.Name, sourcePrimaryKey.Name, StringComparison.OrdinalIgnoreCase) && !c.IsPrimaryKey);

        if (sameName is not null)
        {
            return sameName;
        }

        return target.Columns.FirstOrDefault(c => !c.IsPrimaryKey) ?? target.Columns.FirstOrDefault();
    }

    /// <summary>コピー元エンティティから位置をずらした複製 ViewModel を生成します。</summary>
    internal EntityViewModel CreateEntityCopy(EntityViewModel source, int offsetMultiplier = 1) => CreateEntityCopy(source.ToModel(), offsetMultiplier);

    /// <summary>コピー元エンティティモデルから位置をずらした複製 ViewModel を生成します。</summary>
    internal EntityViewModel CreateEntityCopy(Entity source, int offsetMultiplier = 1)
    {
        var copy = source.Clone(preserveId: false);
        var normalizedOffsetMultiplier = Math.Max(1, offsetMultiplier);
        var offset = 30 * normalizedOffsetMultiplier;

        copy.TableName = GenerateCopyTableName(source.TableName);
        copy.X += offset;
        copy.Y += offset;

        return new EntityViewModel(copy);
    }

    /// <summary>エンティティを単一選択状態に切り替えます。</summary>
    private void SelectSingleEntity(EntityViewModel entity)
    {
        foreach (var currentEntity in Entities)
        {
            currentEntity.IsSelected = (currentEntity == entity);
        }

        foreach (var relationship in Relationships)
        {
            relationship.IsSelected = false;
        }

        SelectedEntity = entity;
        SelectedRelationship = null;
    }

    /// <summary>複製時に衝突しないテーブル名を決定します。</summary>
    private string GenerateCopyTableName(string originalTableName)
    {
        var normalizedTableName = string.IsNullOrWhiteSpace(originalTableName) ? "NewTable" : originalTableName.Trim();
        var candidate = $"{normalizedTableName}_Copy";
        var suffix = 2;

        while (Entities.Any(entity => string.Equals(entity.TableName, candidate, StringComparison.OrdinalIgnoreCase)))
        {
            candidate = $"{normalizedTableName}_Copy{suffix}";
            suffix++;
        }

        return candidate;
    }

    // ---------------- Collection changed handlers ----------------

    private void OnEntitiesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
        {
            foreach (EntityViewModel entity in e.OldItems)
            {
                entity.PropertyChanged -= OnEntityPropertyChanged;
                _changeTracker.DetachEntity(entity);
            }
        }

        if (e.NewItems is not null)
        {
            foreach (EntityViewModel entity in e.NewItems)
            {
                entity.ShowDescriptionsInDiagram = ShowColumnDescriptionsInDiagram;
                entity.ShowNullabilityInDiagram = ShowNullabilityInDiagram;
                entity.PropertyChanged += OnEntityPropertyChanged;
                _changeTracker.AttachEntity(entity);
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

    private void OnRelationshipsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
        {
            foreach (RelationshipViewModel relationship in e.OldItems)
            {
                _changeTracker.DetachRelationship(relationship);
            }
        }

        if (e.NewItems is not null)
        {
            foreach (RelationshipViewModel relationship in e.NewItems)
            {
                _changeTracker.AttachRelationship(relationship);
            }
        }

        ApplyRelationshipColumnRules();
        OnPropertyChanged(nameof(Relationships));
    }
}
