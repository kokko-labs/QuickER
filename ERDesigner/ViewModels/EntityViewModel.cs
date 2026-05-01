using System;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using ERDesigner.Models;

namespace ERDesigner.ViewModels;

/// <summary>
/// エンティティをビューに表示、ドラッグや選択を可能にするための ViewModel です。
/// </summary>
public partial class EntityViewModel : ObservableObject
{
    /// <summary>モデルと同じ ID。</summary>
    public Guid Id { get; }

    /// <summary>画面表示名。</summary>
    [ObservableProperty] private string _displayName;
    /// <summary>テーブル名。</summary>
    [ObservableProperty] private string _tableName;
    /// <summary>キャンバス上の X 座標 (px)。ドラッグで更新されます。</summary>
    [ObservableProperty] private double _x;
    /// <summary>キャンバス上の Y 座標 (px)。ドラッグで更新されます。</summary>
    [ObservableProperty] private double _y;
    /// <summary>カードの横幅 (px)。</summary>
    [ObservableProperty] private double _width;
    /// <summary>メモ。プロパティパネルで編集されます。</summary>
    [ObservableProperty] private string _memo;
    /// <summary>テーブルの説明 (SQL Server の <c>MS_Description</c> と同期)。</summary>
    [ObservableProperty] private string _description;
    /// <summary>選択中かどうか。枚線スタイルを切り替えるためのフラグ。</summary>
    [ObservableProperty] private bool _isSelected;

    /// <summary>このエンティティに含まれるカラム一覧。</summary>
    public ObservableCollection<ColumnViewModel> Columns { get; }

    /// <summary>モデルから ViewModel を生成します。</summary>
    /// <param name="model">コピー元の <see cref="Entity"/> モデル。</param>
    public EntityViewModel(Entity model)
    {
        Id = model.Id;
        _displayName = model.DisplayName;
        _tableName = model.TableName;
        _x = model.X;
        _y = model.Y;
        _width = model.Width <= 0 ? 200 : model.Width;
        _memo = model.Memo;
        _description = model.Description ?? string.Empty;
        Columns = new ObservableCollection<ColumnViewModel>(
            model.Columns.Select(c => new ColumnViewModel(c)));
    }

    /// <summary>現在の状態をモデルにコピーして返します。</summary>
    public Entity ToModel() => new()
    {
        Id = Id,
        DisplayName = DisplayName,
        TableName = TableName,
        X = X,
        Y = Y,
        Width = Width,
        Memo = Memo,
        Description = Description ?? string.Empty,
        Columns = Columns.Select(c => c.ToModel()).ToList()
    };
}
