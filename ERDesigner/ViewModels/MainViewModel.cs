using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
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
    public UndoRedoManager UndoRedo { get; } = new();

    public ObservableCollection<EntityViewModel> Entities { get; } = new();
    public ObservableCollection<RelationshipViewModel> Relationships { get; } = new();

    [ObservableProperty] private EntityViewModel? _selectedEntity;
    [ObservableProperty] private RelationshipViewModel? _selectedRelationship;

    /// <summary>
    /// Entity awaiting a partner during relationship-creation mode.
    /// </summary>
    [ObservableProperty] private EntityViewModel? _pendingRelationshipSource;
    [ObservableProperty] private RelationshipType _pendingRelationshipType;
    [ObservableProperty] private bool _isRelationshipMode;

    /// <summary>プロパティパネルで選択中のカラム（DataGrid の SelectedItem）。</summary>
    [ObservableProperty] private ColumnViewModel? _selectedColumn;

    /// <summary>ER 図上のカラム行に「説明」を表示するか (ツールバーから ON/OFF 切替)。</summary>
    [ObservableProperty] private bool _showColumnDescriptionsInDiagram;

    /// <summary>キャンバスの動的幅 (エンティティの最右端 + 余白)。</summary>
    public double CanvasWidth => Math.Max(2400, Entities.Count == 0 ? 2400 : Entities.Max(e => e.X + e.Width) + 400);
    /// <summary>キャンバスの動的高さ (エンティティの最下端 + 余白)。</summary>
    public double CanvasHeight => Math.Max(1600, Entities.Count == 0 ? 1600 : Entities.Max(e => e.Y + 300) + 400);

    /// <summary>型 ComboBox に表示する SQL Server のデータ型一覧。</summary>
    public IReadOnlyList<string> SqlDataTypes => SqlServerDataTypes.All;

    public MainViewModel()
    {
        Entities.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(Entities));
            RefreshCanvasSize();
        };
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

    // ---------------- Auto-save / restore ----------------

    private static readonly string AutoSavePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "ERDesigner", "last_diagram.json");

    /// <summary>現在のダイアグラムを自動保存ファイルに書き出します。</summary>
    public void AutoSave()
    {
        try
        {
            var dir = Path.GetDirectoryName(AutoSavePath)!;
            Directory.CreateDirectory(dir);
            JsonStorageService.Save(AutoSavePath, this);
        }
        catch { /* 自動保存の失敗は無視 */ }
    }

    /// <summary>起動時に前回の自動保存ファイルを復元します。</summary>
    private void RestoreLastDiagram()
    {
        if (!File.Exists(AutoSavePath)) return;
        try
        {
            var diagram = JsonStorageService.Load(AutoSavePath);
            foreach (var e in diagram.Entities)
                Entities.Add(new EntityViewModel(e));
            foreach (var r in diagram.Relationships)
            {
                var src = Entities.FirstOrDefault(e => e.Id == r.SourceEntityId);
                var tgt = Entities.FirstOrDefault(e => e.Id == r.TargetEntityId);
                if (src is null || tgt is null) continue;
                Relationships.Add(new RelationshipViewModel(r, src, tgt));
            }
        }
        catch { /* 復元失敗時は空で起動 */ }
    }

    // ---------------- Commands ----------------

    [RelayCommand]
    private void NewDiagram()
    {
        if (Entities.Count > 0)
        {
            var ans = System.Windows.MessageBox.Show(
                "現在のダイアグラムをクリアします。よろしいですか？",
                "確認",
                System.Windows.MessageBoxButton.OKCancel,
                System.Windows.MessageBoxImage.Question);
            if (ans != System.Windows.MessageBoxResult.OK) return;
        }
        foreach (var r in Relationships) r.Detach();
        Entities.Clear();
        Relationships.Clear();
        UndoRedo.Clear();
        SelectedEntity = null;
        SelectedRelationship = null;
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
                new Column { Name = "ID", DataType = "int", IsPrimaryKey = true }
            }
        };
        var vm = new EntityViewModel(model);
        UndoRedo.Execute(new AddEntityCommand(this, vm));
        SelectedEntity = vm;
    }

    [RelayCommand(CanExecute = nameof(CanRemoveEntity))]
    private void RemoveSelectedEntity()
    {
        if (SelectedEntity is null) return;
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
        if (SelectedRelationship is null) return;
        UndoRedo.Execute(new RemoveRelationshipCommand(this, SelectedRelationship));
        SelectedRelationship = null;
    }
    private bool CanRemoveRelationship() => SelectedRelationship is not null;

    [RelayCommand(CanExecute = nameof(CanAddColumn))]
    private void AddColumn()
    {
        if (SelectedEntity is null) return;
        SelectedEntity.Columns.Add(new ColumnViewModel(new Column
        {
            Name = "NewColumn",
            DataType = SqlServerDataTypes.All[3] // "int"
        }));
    }
    private bool CanAddColumn() => SelectedEntity is not null;

    /// <summary>指定カラムを選択中エンティティから削除します。</summary>
    [RelayCommand]
    private void RemoveColumn(ColumnViewModel? column)
    {
        if (SelectedEntity is null || column is null) return;
        SelectedEntity.Columns.Remove(column);
        if (SelectedColumn == column) SelectedColumn = null;
    }

    /// <summary>DataGrid で選択中のカラムを削除します（ツールバーボタン用）。</summary>
    [RelayCommand(CanExecute = nameof(CanRemoveSelectedColumn))]
    private void RemoveSelectedColumn()
    {
        if (SelectedEntity is null || SelectedColumn is null) return;
        var col = SelectedColumn;
        SelectedEntity.Columns.Remove(col);
        SelectedColumn = null;
    }
    private bool CanRemoveSelectedColumn() => SelectedEntity is not null && SelectedColumn is not null;

    partial void OnSelectedColumnChanged(ColumnViewModel? value)
        => RemoveSelectedColumnCommand.NotifyCanExecuteChanged();

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
            if (PendingRelationshipSource == entity)
            {
                PendingRelationshipSource = null;
                return;
            }

            var rel = new RelationshipViewModel(
                new Relationship
                {
                    SourceEntityId = PendingRelationshipSource.Id,
                    TargetEntityId = entity.Id,
                    Type = PendingRelationshipType
                },
                PendingRelationshipSource,
                entity);

            UndoRedo.Execute(new AddRelationshipCommand(this, rel));

            IsRelationshipMode = false;
            PendingRelationshipSource = null;
        }
        else
        {
            // single-selection
            foreach (var e in Entities) e.IsSelected = (e == entity);
            SelectedEntity = entity;
            SelectedRelationship = null;
        }
    }

    /// <summary>リレーションがクリックされたときに呼ばれます。</summary>
    public void OnRelationshipClicked(RelationshipViewModel rel)
    {
        foreach (var e in Entities) e.IsSelected = false;
        foreach (var r in Relationships) r.IsSelected = (r == rel);
        SelectedEntity = null;
        SelectedRelationship = rel;
    }

    public void OnCanvasClicked()
    {
        foreach (var e in Entities) e.IsSelected = false;
        foreach (var r in Relationships) r.IsSelected = false;
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
    private void Undo() => UndoRedo.Undo();

    [RelayCommand]
    private void Redo() => UndoRedo.Redo();

    [RelayCommand]
    private void EntityClick(EntityViewModel? entity)
    {
        if (entity is not null) OnEntityClicked(entity);
    }

    [RelayCommand]
    private void RelationshipClick(RelationshipViewModel? rel)
    {
        if (rel is not null) OnRelationshipClicked(rel);
    }

    [RelayCommand]
    private void CanvasClick() => OnCanvasClicked();

    // ---------------- Duplicate (Ctrl+D) ----------------

    /// <summary>選択中エンティティを Undo 可能な形で複製します（Ctrl+D 用）。</summary>
    [RelayCommand(CanExecute = nameof(CanDuplicateSelectedEntity))]
    private void DuplicateSelectedEntity()
    {
        if (SelectedEntity is null) return;
        var cmd = new DuplicateEntityCommand(this, SelectedEntity);
        UndoRedo.Execute(cmd);
        if (cmd.Duplicated is not null)
        {
            foreach (var e in Entities) e.IsSelected = (e == cmd.Duplicated);
            SelectedEntity = cmd.Duplicated;
        }
    }
    private bool CanDuplicateSelectedEntity() => SelectedEntity is not null;

    // ---------------- Auto layout ----------------

    /// <summary>エンティティを格子状に整列します。</summary>
    [RelayCommand]
    private void AutoLayoutGrid() => AutoLayoutService.LayoutGrid(Entities);

    /// <summary>エンティティをツリー状（リレーション階層）で整列します。</summary>
    [RelayCommand]
    private void AutoLayoutTree() => AutoLayoutService.LayoutTree(Entities, Relationships);

    // ---------------- Export ----------------

    /// <summary>キャンバス Visual を PNG に書き出します。</summary>
    /// <param name="visual">XAML から渡されるキャンバスの Visual。</param>
    [RelayCommand]
    private void ExportPng(object? visual)
    {
        if (visual is not Visual v) return;
        var dlg = new SaveFileDialog { Filter = "PNG Image (*.png)|*.png", DefaultExt = ".png" };
        if (dlg.ShowDialog() == true)
            ImageExportService.ExportPng(v, dlg.FileName);
    }

    /// <summary>現在のダイアグラムを SVG に書き出します。</summary>
    [RelayCommand]
    private void ExportSvg()
    {
        var dlg = new SaveFileDialog { Filter = "SVG Image (*.svg)|*.svg", DefaultExt = ".svg" };
        if (dlg.ShowDialog() == true)
            ImageExportService.ExportSvg(this, dlg.FileName);
    }

    /// <summary>現在のダイアグラムから SQL DDL を書き出します。</summary>
    [RelayCommand]
    private void ExportDdl()
    {
        var dlg = new SaveFileDialog { Filter = "SQL Script (*.sql)|*.sql", DefaultExt = ".sql" };
        if (dlg.ShowDialog() == true)
            DdlExporter.SaveTo(this, dlg.FileName);
    }

    // ---------------- SQL Server 取込 ----------------

    /// <summary>SQL Server に接続してスキーマを取得し、ダイアグラムに反映します。</summary>
    [RelayCommand]
    private async System.Threading.Tasks.Task ImportFromSqlServerAsync()
    {
        var dialog = new Views.SqlConnectionDialog
        {
            Owner = System.Windows.Application.Current?.MainWindow
        };
        if (dialog.ShowDialog() != true || dialog.ViewModel.Result is null) return;

        try
        {
            var importer = new SqlServerSchemaImporter();
            var result = await importer.ImportAsync(dialog.ViewModel.Result).ConfigureAwait(true);

            // 既存と差分があるかチェックして置換確認
            if (Entities.Count > 0)
            {
                var currentSig = SqlServerSchemaImporter.ComputeSignature(
                    Entities.Select(e => e.ToModel()),
                    Relationships.Select(r => r.ToModel()));
                var newSig = SqlServerSchemaImporter.ComputeSignature(result.Entities, result.Relationships);
                if (currentSig != newSig)
                {
                    var ans = System.Windows.MessageBox.Show(
                        "現在のダイアグラムを取得結果で置換します。よろしいですか？",
                        "確認",
                        System.Windows.MessageBoxButton.OKCancel,
                        System.Windows.MessageBoxImage.Question);
                    if (ans != System.Windows.MessageBoxResult.OK) return;
                }
            }

            // 取り込んだ後に自動レイアウト (Tree)
            var cmd = new ImportSchemaCommand(this, result.Entities, result.Relationships);
            UndoRedo.Execute(cmd);
            AutoLayoutService.LayoutTree(Entities, Relationships);
        }
        catch (System.Exception ex)
        {
            System.Windows.MessageBox.Show("取り込みに失敗しました: " + ex.Message,
                "エラー", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
        }
    }

    // ---------------- DB 書き込み (スキーマ同期) ----------------

    /// <summary>SQL Server に接続し、現在のダイアグラムとの差分を ALTER 文で書き戻します。</summary>
    [RelayCommand]
    private void SyncToSqlServer()
    {
        var connDlg = new Views.SqlConnectionDialog
        {
            Owner = System.Windows.Application.Current?.MainWindow,
            Title = "SQL Server へ同期"
        };
        if (connDlg.ShowDialog() != true || connDlg.ViewModel.Result is null) return;

        var targetEntities = Entities.Select(e => e.ToModel()).ToList();
        var targetRelationships = Relationships.Select(r => r.ToModel()).ToList();

        var vm = new SchemaSyncDialogViewModel(connDlg.ViewModel.Result, targetEntities, targetRelationships);
        var dlg = new Views.SchemaSyncDialog(vm)
        {
            Owner = System.Windows.Application.Current?.MainWindow
        };
        dlg.ShowDialog();
    }

    // ---------------- AI 生成 ----------------

    /// <summary>ChatGPT/Ollama にスキーマ生成を依頼し、ダイアグラムへ反映します。</summary>
    [RelayCommand]
    private void GenerateFromAi()
    {
        var dialog = new Views.AiGenerateDialog
        {
            Owner = System.Windows.Application.Current?.MainWindow
        };
        if (dialog.ShowDialog() != true || dialog.ViewModel.Result is null) return;

        var (entities, relationships) = dialog.ViewModel.Result.ToDomain();
        if (entities.Count == 0)
        {
            System.Windows.MessageBox.Show("AI 応答にテーブルが含まれていませんでした。",
                "情報", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
            return;
        }

        if (Entities.Count > 0)
        {
            var currentSig = SqlServerSchemaImporter.ComputeSignature(
                Entities.Select(e => e.ToModel()),
                Relationships.Select(r => r.ToModel()));
            var newSig = SqlServerSchemaImporter.ComputeSignature(entities, relationships);
            if (currentSig != newSig)
            {
                var ans = System.Windows.MessageBox.Show(
                    "現在のダイアグラムを AI 生成結果で置換します。よろしいですか？",
                    "確認",
                    System.Windows.MessageBoxButton.OKCancel,
                    System.Windows.MessageBoxImage.Question);
                if (ans != System.Windows.MessageBoxResult.OK) return;
            }
        }

        var cmd = new ImportSchemaCommand(this, entities, relationships);
        UndoRedo.Execute(cmd);
        AutoLayoutService.LayoutTree(Entities, Relationships);
    }

    // ---------------- Save / Load ----------------

    [RelayCommand]
    private void Save()
    {
        var dlg = new SaveFileDialog { Filter = "ER Diagram (*.json)|*.json", DefaultExt = ".json" };
        if (dlg.ShowDialog() == true)
            JsonStorageService.Save(dlg.FileName, this);
    }

    /// <summary>JSON ファイルからダイアグラムを読み込みます（ダイアログ表示）。</summary>
    [RelayCommand]
    private void Open()
    {
        var dlg = new OpenFileDialog { Filter = "ER Diagram (*.json)|*.json" };
        if (dlg.ShowDialog() != true) return;

        var diagram = JsonStorageService.Load(dlg.FileName);

        foreach (var r in Relationships) r.Detach();
        Entities.Clear();
        Relationships.Clear();

        foreach (var e in diagram.Entities)
            Entities.Add(new EntityViewModel(e));

        foreach (var r in diagram.Relationships)
        {
            var src = Entities.FirstOrDefault(e => e.Id == r.SourceEntityId);
            var tgt = Entities.FirstOrDefault(e => e.Id == r.TargetEntityId);
            if (src is null || tgt is null) continue;
            Relationships.Add(new RelationshipViewModel(r, src, tgt));
        }
        UndoRedo.Clear();
    }
}
