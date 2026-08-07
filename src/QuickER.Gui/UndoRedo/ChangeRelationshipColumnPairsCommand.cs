using System.Collections.Generic;
using QuickER.Model;
using QuickER.Resources;
using QuickER.ViewModels;

namespace QuickER.UndoRedo;

/// <summary>リレーションの列ペア（宣言順の親列・子列の組）を差し替えるコマンド</summary>
/// <remarks>
/// 列ペア行 1 つの確定・差し替え・削除が 1 履歴になる。列ペアは「順序付きの組の並び」で個別の差分を
/// 持たないため、変更前後の一覧をそのままスナップショットとして保持する
/// （UNIQUE 制約の <see cref="ChangeUniqueConstraintColumnsCommand"/> と同型）
/// </remarks>
public sealed class ChangeRelationshipColumnPairsCommand : IUndoableCommand
{
    /// <summary>列ペアを差し替える対象のリレーション</summary>
    private readonly RelationshipViewModel _relationship;

    /// <summary>変更前の列ペア（宣言順）</summary>
    private readonly IReadOnlyList<RelationshipColumnPair> _before;

    /// <summary>変更後の列ペア（宣言順）</summary>
    private readonly IReadOnlyList<RelationshipColumnPair> _after;

    /// <summary>適用後に呼ぶ後処理（外部キー列ルールの再適用）</summary>
    private readonly Action? _afterApply;

    /// <summary><see cref="ChangeRelationshipColumnPairsCommand"/> を生成する</summary>
    public ChangeRelationshipColumnPairsCommand(
        RelationshipViewModel relationship,
        IReadOnlyList<RelationshipColumnPair> before,
        IReadOnlyList<RelationshipColumnPair> after,
        Action? afterApply = null
    )
    {
        _relationship = relationship;
        _before = before;
        _after = after;
        _afterApply = afterApply;
    }

    /// <inheritdoc />
    public string Description => Strings.Undo_ChangeRelationshipColumnPairs;

    /// <inheritdoc />
    public void Execute()
    {
        _relationship.SetColumnPairs(_after);
        _afterApply?.Invoke();
    }

    /// <inheritdoc />
    public void Undo()
    {
        _relationship.SetColumnPairs(_before);
        _afterApply?.Invoke();
    }
}
