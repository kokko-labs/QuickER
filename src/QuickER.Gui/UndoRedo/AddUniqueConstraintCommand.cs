using System.Collections.ObjectModel;
using QuickER.Resources;
using QuickER.ViewModels;

namespace QuickER.UndoRedo;

/// <summary>一意制約を 1 件追加するコマンド（Undo で同じ制約を取り除く）</summary>
public sealed class AddUniqueConstraintCommand : IUndoableCommand
{
    /// <summary>追加先の一意制約コレクション</summary>
    private readonly ObservableCollection<UniqueConstraintViewModel> _constraints;

    /// <summary>追加対象の一意制約</summary>
    private readonly UniqueConstraintViewModel _constraint;

    /// <summary><see cref="AddUniqueConstraintCommand"/> を生成する</summary>
    public AddUniqueConstraintCommand(
        ObservableCollection<UniqueConstraintViewModel> constraints,
        UniqueConstraintViewModel constraint
    )
    {
        _constraints = constraints;
        _constraint = constraint;
    }

    /// <inheritdoc />
    public string Description => Strings.Undo_AddUniqueConstraint;

    /// <inheritdoc />
    public void Execute()
    {
        // Redo 連打などで二重追加にならないよう既存チェックを行う
        if (_constraints.Contains(_constraint))
        {
            return;
        }

        _constraints.Add(_constraint);
    }

    /// <inheritdoc />
    public void Undo()
    {
        _constraints.Remove(_constraint);
    }
}
