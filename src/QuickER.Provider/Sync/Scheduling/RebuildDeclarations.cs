using System.Collections.Generic;
using System.Linq;

namespace QuickER.Provider.Sync.Scheduling;

/// <summary>外し直しの宣言 1 つ：選んだ文を流す間、外れていなければならない DB の今の制約を返す</summary>
/// <param name="input">選んだ文・同期後の事実・DB の今の図・方言の宣言</param>
/// <returns>外れていなければならない制約（同じ制約を 2 回返してもよい）</returns>
internal delegate IEnumerable<LiveConstraint> RebuildDeclaration(SchedulingInput input);

/// <summary>
/// 外し直しの宣言（1 宣言 1 ファイル＝<c>RebuildDeclarations.{宣言}.cs</c>）と、宣言の表
/// </summary>
/// <remarks>
/// <para>
/// 宣言は「どの制約が外れていなければならないか」だけを答える。同じ計画が明示に外す制約を除くこと・付け直せない制約を外さないこと
/// （<see cref="UnreaddableConstraints"/>）・外す文と付け直す文を作ること（<see cref="Readds"/>）は、宣言の後の段が受け持つ。
/// </para>
/// <para>
/// 宣言の答えは集合として合わせる。外す文の並びは DB の今の図の制約の並びで決まるので、表の並びは結果に効かない。
/// </para>
/// </remarks>
internal static partial class RebuildDeclarations
{
    /// <summary>宣言の表（掛けるかどうかの門＝方言の宣言は、宣言の中に置く）</summary>
    internal static readonly RebuildDeclaration[] All =
    [
        RequireForeignKeysOffDroppedPrimaryKeys,
        RequireForeignKeysOffUniquesReplacedByPrimaryKey,
        RequireForeignKeysOffAlteredColumns,
        RequireForeignKeysOffDivergingColumnChanges,
        RequireForeignKeysOffLostIndexes,
        RequireUniqueConstraintsOffAlteredColumns,
        RequirePrimaryKeysOffAlteredColumns,
    ];

    /// <summary>宣言の表を回して、外れていなければならない制約を種類ごとの集合に集める</summary>
    /// <param name="input">外し直しを決める段の入力</param>
    /// <returns>外れていなければならない制約（参照で比べる集合）</returns>
    public static RequiredConstraints Collect(SchedulingInput input)
    {
        var required = new RequiredConstraints(
            new(ReferenceEqualityComparer.Instance),
            new(ReferenceEqualityComparer.Instance),
            new(ReferenceEqualityComparer.Instance)
        );

        foreach (var constraint in All.SelectMany(declaration => declaration(input)))
        {
            switch (constraint)
            {
                case LiveForeignKey foreignKey:
                    required.ForeignKeys.Add(foreignKey.ForeignKey);
                    break;

                case LiveUniqueConstraint unique:
                    required.UniqueConstraints.Add(unique.Constraint);
                    break;

                case LivePrimaryKey primaryKey:
                    required.PrimaryKeys.Add(primaryKey.Table);
                    break;

                default:
                    throw new System.InvalidOperationException(
                        $"The constraint {constraint.GetType().Name} is not handled."
                    );
            }
        }

        return required;
    }
}
