using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
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

    /// <summary>型 ComboBox に表示する SQL Server のデータ型一覧。</summary>
    public IReadOnlyList<string> SqlDataTypes => SqlServerDataTypes.All;

    public MainViewModel()
    {
        Entities.CollectionChanged += (_, _) => OnPropertyChanged(nameof(Entities));
    }

    // ---------------- Commands ----------------

    [RelayCommand]
    private void NewDiagram()
    {
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
            DisplayName = "新規エンティティ",
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
