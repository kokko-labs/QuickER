using System.Collections.ObjectModel;
using ERDesigner.ViewModels;

namespace ERDesigner.UndoRedo;

/// <summary>カラムを 1 件追加するコマンド（Undo で同じカラムを削除する）</summary>
public class AddColumnCommand : IUndoableCommand
{
    /// <summary>追加先のカラムコレクション</summary>
    private readonly ObservableCollection<ColumnViewModel> _columns;

    /// <summary>追加対象のカラム</summary>
    private readonly ColumnViewModel _column;

    /// <summary>挿入位置（未指定時は末尾）</summary>
    private readonly int _index;

    /// <summary><see cref="AddColumnCommand"/> を生成する</summary>
    /// <param name="index">挿入位置 null 指定で末尾に追加する</param>
    public AddColumnCommand(ObservableCollection<ColumnViewModel> columns, ColumnViewModel column, int? index = null)
    {
        _columns = columns;
        _column = column;
        _index = index ?? columns.Count;
    }

    /// <inheritdoc />
    public string Description => $"カラム追加: {_column.Name}";

    /// <inheritdoc />
    public void Execute()
    {
        // Redo 連打などで二重追加にならないよう既存チェックを行う
        if (_columns.Contains(_column))
        {
            return;
        }

        // コレクション件数が Undo 時から変動している場合に備えて範囲内へ丸める
        var insertIndex = Math.Clamp(_index, 0, _columns.Count);
        _columns.Insert(insertIndex, _column);
    }

    /// <inheritdoc />
    public void Undo()
    {
        _columns.Remove(_column);
    }
}
