using System.Collections.ObjectModel;
using QuickER.Resources;
using QuickER.ViewModels;

namespace QuickER.UndoRedo;

/// <summary>一意制約を 1 件削除するコマンド（Undo で元の位置へ復元する）</summary>
public sealed class RemoveUniqueConstraintCommand : IUndoableCommand
{
    /// <summary>削除対象を含む一意制約コレクション</summary>
    private readonly ObservableCollection<UniqueConstraintViewModel> _constraints;

    /// <summary>削除対象の一意制約</summary>
    private readonly UniqueConstraintViewModel _constraint;

    /// <summary>削除前のインデックス（Undo 時の復元位置）</summary>
    private readonly int _index;

    /// <summary><see cref="RemoveUniqueConstraintCommand"/> を生成する</summary>
    public RemoveUniqueConstraintCommand(
        ObservableCollection<UniqueConstraintViewModel> constraints,
        UniqueConstraintViewModel constraint
    )
    {
        _constraints = constraints;
        _constraint = constraint;
        _index = constraints.IndexOf(constraint);
    }

    /// <inheritdoc />
    public string Description => Strings.Undo_RemoveUniqueConstraint;

    /// <inheritdoc />
    public void Execute()
    {
        _constraints.Remove(_constraint);
    }

    /// <inheritdoc />
    public void Undo()
    {
        // Undo 連打などによる二重挿入を避ける
        if (_constraints.Contains(_constraint))
        {
            return;
        }

        // コレクション件数が削除時から変動している場合に備えて範囲内へ丸める
        _constraints.Insert(Math.Clamp(_index, 0, _constraints.Count), _constraint);
    }
}
