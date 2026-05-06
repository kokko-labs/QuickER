using System.Collections.ObjectModel;
using ERDesigner.ViewModels;

namespace ERDesigner.UndoRedo;

/// <summary>
/// カラムを 1 つ追加するコマンドです。Undo で同じカラムを削除します。
/// </summary>
public class AddColumnCommand : IUndoableCommand
{
    private readonly ObservableCollection<ColumnViewModel> _columns;
    private readonly ColumnViewModel _column;
    private readonly int _index;

    /// <summary>新しい <see cref="AddColumnCommand"/> を生成します。</summary>
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
        if (_columns.Contains(_column))
        {
            return;
        }

        var insertIndex = Math.Clamp(_index, 0, _columns.Count);
        _columns.Insert(insertIndex, _column);
    }

    /// <inheritdoc />
    public void Undo()
    {
        _columns.Remove(_column);
    }
}
