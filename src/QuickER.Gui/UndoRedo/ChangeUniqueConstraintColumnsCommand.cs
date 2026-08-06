using System.Collections.Generic;
using QuickER.Resources;
using QuickER.ViewModels;

namespace QuickER.UndoRedo;

/// <summary>一意制約の構成列（宣言順のカラム Guid 一覧）を差し替えるコマンド</summary>
/// <remarks>
/// 列行 1 つの確定・差し替え・削除が 1 履歴になる。構成列は「順序付きの集合」で個別の差分を持たないため、
/// 変更前後の一覧をそのままスナップショットとして保持する
/// </remarks>
public sealed class ChangeUniqueConstraintColumnsCommand : IUndoableCommand
{
    /// <summary>構成列を差し替える対象の一意制約</summary>
    private readonly UniqueConstraintViewModel _constraint;

    /// <summary>変更前の構成列（宣言順）</summary>
    private readonly IReadOnlyList<Guid> _before;

    /// <summary>変更後の構成列（宣言順）</summary>
    private readonly IReadOnlyList<Guid> _after;

    /// <summary><see cref="ChangeUniqueConstraintColumnsCommand"/> を生成する</summary>
    public ChangeUniqueConstraintColumnsCommand(
        UniqueConstraintViewModel constraint,
        IReadOnlyList<Guid> before,
        IReadOnlyList<Guid> after
    )
    {
        _constraint = constraint;
        _before = before;
        _after = after;
    }

    /// <inheritdoc />
    public string Description => Strings.Undo_ChangeUniqueConstraintColumns;

    /// <inheritdoc />
    public void Execute() => _constraint.SetColumnIds(_after);

    /// <inheritdoc />
    public void Undo() => _constraint.SetColumnIds(_before);
}
