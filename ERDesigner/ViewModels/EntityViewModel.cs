using System;
using System.Collections.Specialized;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using ERDesigner.Models;
using ERDesigner.Services;

namespace ERDesigner.ViewModels;

/// <summary>
/// エンティティをビューに表示、ドラッグや選択を可能にするための ViewModel です。
/// </summary>
public partial class EntityViewModel : ObservableObject
{
    /// <summary>モデルと同じ ID。</summary>
    public Guid Id { get; }

    /// <summary>テーブル名。</summary>
    [ObservableProperty] private string _tableName;
    /// <summary>キャンバス上の X 座標 (px)。ドラッグで更新されます。</summary>
    [ObservableProperty] private double _x;
    /// <summary>キャンバス上の Y 座標 (px)。ドラッグで更新されます。</summary>
    [ObservableProperty] private double _y;
    /// <summary>カードの横幅 (px)。</summary>
    [ObservableProperty] private double _width;
    /// <summary>ER 図上で説明表示を行うかどうか。</summary>
    private bool _showDescriptionsInDiagram;
    /// <summary>メモ。プロパティパネルで編集されます。</summary>
    [ObservableProperty] private string _memo;
    /// <summary>テーブルの説明 (SQL Server の <c>MS_Description</c> と同期)。</summary>
    [ObservableProperty] private string _description;
    /// <summary>選択中かどうか。枚線スタイルを切り替えるためのフラグ。</summary>
    [ObservableProperty] private bool _isSelected;

    /// <summary>このエンティティに含まれるカラム一覧。</summary>
    public ObservableCollection<ColumnViewModel> Columns { get; }

    /// <summary>ER 図上で説明表示を行うかどうか。</summary>
    public bool ShowDescriptionsInDiagram
    {
        get => _showDescriptionsInDiagram;
        set
        {
            if (SetProperty(ref _showDescriptionsInDiagram, value))
                OnPropertyChanged(nameof(DisplayHeight));
        }
    }

    /// <summary>現在の表示状態に応じたエンティティの表示高さです。</summary>
    public double DisplayHeight => DiagramMetricsService.EstimateEntityHeight(this, ShowDescriptionsInDiagram);

    /// <summary>モデルから ViewModel を生成します。</summary>
    public EntityViewModel(Entity model)
    {
        Id = model.Id;
        _tableName = model.TableName;
        _x = model.X;
        _y = model.Y;
        _width = model.Width <= 0 ? 200 : model.Width;
        _memo = model.Memo;
        _description = model.Description ?? string.Empty;
        Columns = new ObservableCollection<ColumnViewModel>(
            model.Columns.Select(c => new ColumnViewModel(c)));

        Columns.CollectionChanged += OnColumnsChanged;
        foreach (var column in Columns)
            column.PropertyChanged += OnColumnPropertyChanged;
    }

    /// <summary>内容に合わせてエンティティ幅を自動調整します。</summary>
    public void AutoFitWidth()
    {
        Width = DiagramMetricsService.CalculateAutoWidth(this);
    }

    partial void OnWidthChanged(double value)
        => OnPropertyChanged(nameof(DisplayHeight));

    partial void OnDescriptionChanged(string value)
        => OnPropertyChanged(nameof(DisplayHeight));

    private void OnColumnsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
        {
            foreach (ColumnViewModel column in e.OldItems)
                column.PropertyChanged -= OnColumnPropertyChanged;
        }

        if (e.NewItems is not null)
        {
            foreach (ColumnViewModel column in e.NewItems)
                column.PropertyChanged += OnColumnPropertyChanged;
        }

        OnPropertyChanged(nameof(DisplayHeight));
    }

    private void OnColumnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ColumnViewModel.Description))
            OnPropertyChanged(nameof(DisplayHeight));
    }

    /// <summary>現在の状態をモデルにコピーして返します。</summary>
    public Entity ToModel() => new()
    {
        Id = Id,
        TableName = TableName,
        X = X,
        Y = Y,
        Width = Width,
        Memo = Memo,
        Description = Description ?? string.Empty,
        Columns = Columns.Select(c => c.ToModel()).ToList()
    };
}
