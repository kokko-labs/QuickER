using System.Collections.Generic;

namespace QuickER.Provider.Sync.Assessment;

internal static partial class AssessmentRules
{
    /// <summary>
    /// DB にある外部キーのうち、この同期の列の変更で同期後に両端の型が並べられなくなるものを、
    /// <see cref="SyncPlanWarningKind.ForeignKeyColumnTypeMismatch"/> で名指しする（明示的に外す外部キーは除く）
    /// </summary>
    /// <param name="facts">計画の事実（<see cref="AssessmentFacts.PostSync"/>・<see cref="AssessmentFacts.Live"/>・<see cref="AssessmentFacts.Capabilities"/>）</param>
    /// <returns>名指しの警告（DB の今の図の外部キーの並び。Detail は制約の実効名）</returns>
    /// <remarks>
    /// 方言に依らない。計画はこの外部キーを外し直さない（付け直しが必ず断られる）ので、外部キーを張ったまま列を変え、DB が断れば
    /// 列の変更が何も変えずに失敗する。判定は <see cref="SyncPlanner.HasPostSyncTypeMismatch"/>（DB が受け入れている組は数えない）。
    /// DB の今の図が無ければ何も返さない。
    /// </remarks>
    public static IEnumerable<SyncPlanWarning> WarnLiveForeignKeysWithPostSyncTypeMismatch(
        AssessmentFacts facts
    )
    {
        var postSync = facts.PostSync;
        var capabilities = facts.Capabilities;
        var liveSchema = facts.Live;
        var warnings = new List<SyncPlanWarning>();

        if (liveSchema is null)
        {
            return warnings;
        }

        var limit = capabilities.IdentifierLengthLimit;
        var typeDecidingColumns = postSync.TypeDecidingColumns;
        var explicitlyDropped = postSync.DroppedForeignKeys;

        foreach (var fk in liveSchema.ForeignKeys)
        {
            var childTable = SchemaDiffService.NormalizeTable(fk.Child);
            var parentTable = SchemaDiffService.NormalizeTable(fk.Parent);

            if (
                !explicitlyDropped.Contains(
                    SyncPlanner.ForeignKeyKey(
                        childTable,
                        parentTable,
                        fk.ColumnPairs,
                        fk.Relationship,
                        limit
                    )
                )
                && SyncPlanner.HasPostSyncTypeMismatch(
                    fk,
                    childTable,
                    parentTable,
                    typeDecidingColumns,
                    capabilities
                )
            )
            {
                warnings.Add(
                    new SyncPlanWarning(
                        SyncPlanWarningKind.ForeignKeyColumnTypeMismatch,
                        childTable,
                        SyncPlanner.ResolveForeignKeyName(
                            fk.Relationship,
                            childTable,
                            parentTable,
                            limit,
                            liveSchema
                        )
                    )
                );
            }
        }

        return warnings;
    }
}
