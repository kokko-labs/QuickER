using System;
using CommunityToolkit.Mvvm.ComponentModel;
using ERDesigner.Models;

namespace ERDesigner.ViewModels;

/// <summary>
/// 1 つのカラムをビューにバインドするための ViewModel です。
/// <see cref="INotifyPropertyChanged"/> は CommunityToolkit.Mvvm のソースジェネレーターで自動生成されます。
/// </summary>
public partial class ColumnViewModel : ObservableObject
{
    /// <summary>モデルと同じ ID。Save/Load の際のマッチングに使います。</summary>
    public Guid Id { get; }

    /// <summary>カラム名。</summary>
    [ObservableProperty] private string _name;
    /// <summary>データ型 (例: int, varchar(100))。</summary>
    [ObservableProperty] private string _dataType;
    /// <summary>主キーかどうか。</summary>
    [ObservableProperty] private bool _isPrimaryKey;
    /// <summary>外部キーかどうか。</summary>
    [ObservableProperty] private bool _isForeignKey;

    /// <summary>モデルから ViewModel を生成します。</summary>
    /// <param name="model">コピー元の <see cref="Column"/> モデル。</param>
    public ColumnViewModel(Column model)
    {
        Id = model.Id;
        _name = model.Name;
        _dataType = model.DataType;
        _isPrimaryKey = model.IsPrimaryKey;
        _isForeignKey = model.IsForeignKey;
    }

    /// <summary>現在の状態をモデルにコピーして返します（保存時に使用）。</summary>
    public Column ToModel() => new()
    {
        Id = Id,
        Name = Name,
        DataType = DataType,
        IsPrimaryKey = IsPrimaryKey,
        IsForeignKey = IsForeignKey
    };
}
