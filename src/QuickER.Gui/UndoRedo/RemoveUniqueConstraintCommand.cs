using System.Collections.Generic;
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

    /// <summary>取り除いた一意制約と元の位置（Undo の復元元）</summary>
    private List<(int Index, UniqueConstraintViewModel Item)> _removed = new();

    /// <summary><see cref="RemoveUniqueConstraintCommand"/> を生成する</summary>
    public RemoveUniqueConstraintCommand(
        ObservableCollection<UniqueConstraintViewModel> constraints,
        UniqueConstraintViewModel constraint
    )
    {
        _constraints = constraints;
        _constraint = constraint;
    }

    /// <inheritdoc />
    public string Description => Strings.Undo_RemoveUniqueConstraint;

    /// <inheritdoc />
    public void Execute()
    {
        _removed = IndexedRemoval.RemoveWhere(_constraints, c => ReferenceEquals(c, _constraint));
    }

    /// <inheritdoc />
    public void Undo()
    {
        IndexedRemoval.Restore(_constraints, _removed);
    }
}
