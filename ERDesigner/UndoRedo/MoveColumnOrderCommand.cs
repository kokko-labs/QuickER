using System.Collections.ObjectModel;
using ERDesigner.ViewModels;

namespace ERDesigner.UndoRedo;

/// <summary>
/// カラムの並び順変更を Undo / Redo するコマンドです。
/// </summary>
public class MoveColumnOrderCommand : IUndoableCommand
{
    private readonly ObservableCollection<ColumnViewModel> _columns;
    private readonly ColumnViewModel _column;
    private readonly int _oldIndex;
    private readonly int _newIndex;

    /// <summary>新しい <see cref="MoveColumnOrderCommand"/> を生成します。</summary>
    public MoveColumnOrderCommand(ObservableCollection<ColumnViewModel> columns, ColumnViewModel column, int newIndex)
    {
        _columns = columns;
        _column = column;
        _oldIndex = columns.IndexOf(column);
        _newIndex = newIndex;
    }

    /// <inheritdoc />
    public string Description => $"列順変更: {_column.Name}";

    /// <inheritdoc />
    public void Execute()
    {
        MoveTo(_newIndex);
    }

    /// <inheritdoc />
    public void Undo()
    {
        MoveTo(_oldIndex);
    }

    /// <summary>
    /// 現在位置を基準に安全に移動します。
    /// </summary>
    private void MoveTo(int targetIndex)
    {
        var currentIndex = _columns.IndexOf(_column);

        if (currentIndex < 0)
        {
            return;
        }

        var normalizedIndex = Math.Clamp(targetIndex, 0, _columns.Count - 1);

        if (currentIndex == normalizedIndex)
        {
            return;
        }

        _columns.Move(currentIndex, normalizedIndex);
    }
}
