using System.Collections.Generic;
using System.Linq;

namespace QuickER.Provider.Sync.Assessment;

internal static partial class AssessmentRules
{
    /// <summary>
    /// 選んだ文を流す間、外れていなければならない DB の外部キーのうち、付け直せないものと、付け直しを証明できないものを名指しする
    /// </summary>
    /// <param name="assessment">計画の事実と、組み立ての結果（外し直しの宣言が答えた制約）</param>
    /// <returns>名指しの警告（DB の今の図の外部キーの並び。1 つの外部キーにつき多くて 1 件）</returns>
    /// <remarks>
    /// <para>明示的に外す外部キーは見ない。外部キー 1 つにつき、次の順に当たった最初の 1 つだけを行う。</para>
    /// <list type="number">
    /// <item>参照アクションが SET NULL で、子の列が同期後に NOT NULL になる（作れない方言＝
    /// <see cref="SyncPlanner.BlocksSetNullRebuild"/>）：<see cref="SyncPlanWarningKind.SetNullOnNotNullColumn"/>
    /// （Detail は「制約名 (同期後に NOT NULL の子の列, …)」）。計画はこの外部キーを外さない。</item>
    /// <item>同期後に両端の型が並べられない（<see cref="SyncPlanner.HasPostSyncTypeMismatch"/>）：何も返さない
    /// （<see cref="WarnLiveForeignKeysWithPostSyncTypeMismatch"/> が名指しする。計画はこの外部キーも外さない）。</item>
    /// <item>参照先の列が、同期後の主キーにも一意制約にも列の集合で一致しない（<see cref="SyncPlanner.StaysCandidateKey"/>）：
    /// <see cref="SyncPlanWarningKind.ForeignKeyRebuildMayLoseCandidateKey"/>（Detail は制約の実効名）。計画は外して付け直す。</item>
    /// </list>
    /// </remarks>
    public static IEnumerable<SyncPlanWarning> WarnDetachedForeignKeysAtRisk(
        PlanAssessment assessment
    )
    {
        var postSync = assessment.PostSync;
        var capabilities = assessment.Capabilities;
        var liveSchema = assessment.Live;
        var required = assessment.Schedule.Required;
        var warnings = new List<SyncPlanWarning>();

        if (liveSchema is null || required is null)
        {
            return warnings;
        }

        var limit = capabilities.IdentifierLengthLimit;
        var explicitlyDropped = postSync.DroppedForeignKeys;
        var postSyncUniques = postSync.UniqueColumnSets;

        foreach (var fk in liveSchema.ForeignKeys)
        {
            if (!required.ForeignKeys.Contains(fk))
            {
                continue;
            }

            var childTable = SchemaDiffService.NormalizeTable(fk.Child);
            var parentTable = SchemaDiffService.NormalizeTable(fk.Parent);

            if (
                explicitlyDropped.Contains(
                    SyncPlanner.ForeignKeyKey(
                        childTable,
                        parentTable,
                        fk.ColumnPairs,
                        fk.Relationship,
                        limit
                    )
                )
            )
            {
                continue;
            }

            var constraintName = SyncPlanner.ResolveForeignKeyName(
                fk.Relationship,
                childTable,
                parentTable,
                limit,
                liveSchema
            );

            if (SyncPlanner.BlocksSetNullRebuild(fk, childTable, capabilities, postSync.AllowsNull))
            {
                warnings.Add(
                    SyncPlanner.SetNullWarning(
                        childTable,
                        constraintName,
                        SyncPlanner.PostSyncNotNullChildColumns(fk, childTable, postSync.AllowsNull)
                    )
                );
                continue;
            }

            if (
                SyncPlanner.HasPostSyncTypeMismatch(
                    fk,
                    childTable,
                    parentTable,
                    postSync.TypeDecidingColumns,
                    capabilities
                )
            )
            {
                continue;
            }

            var referencedColumns = ForeignKeyColumnPairResolver
                .ParentColumns(fk.ColumnPairs)
                .ToList();

            if (
                !SyncPlanner.StaysCandidateKey(
                    postSync.PrimaryKeyOf(parentTable),
                    postSyncUniques,
                    parentTable,
                    referencedColumns
                )
            )
            {
                warnings.Add(
                    new SyncPlanWarning(
                        SyncPlanWarningKind.ForeignKeyRebuildMayLoseCandidateKey,
                        childTable,
                        constraintName
                    )
                );
            }
        }

        return warnings;
    }
}
