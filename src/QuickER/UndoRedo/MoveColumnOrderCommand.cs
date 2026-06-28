using System.Collections.ObjectModel;
using QuickER.ViewModels;

namespace QuickER.UndoRedo;

/// <summary>カラムの並び順変更を Undo / Redo するコマンド</summary>
public class MoveColumnOrderCommand : IUndoableCommand
{
    /// <summary>並び替え対象のカラムコレクション</summary>
    private readonly ObservableCollection<ColumnViewModel> _columns;

    /// <summary>移動対象のカラム</summary>
    private readonly ColumnViewModel _column;

    /// <summary>移動前のインデックス（Undo 時の戻り先）</summary>
    private readonly int _oldIndex;

    /// <summary>移動後のインデックス</summary>
    private readonly int _newIndex;

    /// <summary><see cref="MoveColumnOrderCommand"/> を生成する</summary>
    public MoveColumnOrderCommand(
        ObservableCollection<ColumnViewModel> columns,
        ColumnViewModel column,
        int newIndex
    )
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

    /// <summary>現在位置を基準にカラムを安全に移動する</summary>
    /// <remarks>記録時から件数や位置が変動していても破綻しないよう都度現在位置を取得する</remarks>
    private void MoveTo(int targetIndex)
    {
        var currentIndex = _columns.IndexOf(_column);

        // 対象カラムが既にコレクションから除去されている場合は何もしない
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
