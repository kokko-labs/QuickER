using System.Collections.Generic;
using System.Linq;
using QuickER.Model;
using QuickER.Provider.Sync.State;

namespace QuickER.Provider.Sync.Assessment;

internal static partial class AssessmentRules
{
    /// <summary>
    /// 選んだ外部キーの追加（作り直しは展開後の追加）の参照先の列が、同期後の主キーにも一意制約にもちょうど一致しない
    /// ものを <see cref="SyncPlanWarningKind.ForeignKeyNotOnCandidateKey"/> で名指しする。
    /// </summary>
    /// <param name="facts">計画の事実（<see cref="AssessmentFacts.PostSync"/>・<see cref="AssessmentFacts.Live"/>・<see cref="AssessmentFacts.Capabilities"/>）</param>
    /// <returns>名指しの警告</returns>
    /// <remarks>
    /// <para>
    /// 同期後の主キーは、主キーの変更を選んだ表はその新しい主キー、追加を選んだ表は図の主キー、それ以外は live の主キー。
    /// 同期後の一意制約は <see cref="PostSyncSchema.UniqueColumnSets"/>（live − 削除 ＋ 追加、追加を選んだ表の一意制約を含む）。
    /// 比べ方は暗黙の付け直しと同じ <see cref="SyncPlanner.StaysCandidateKey"/>（列の集合）。
    /// </para>
    /// <para>
    /// 参照先の表が live にも追加にも無い項目は対象外（選んでいない表の追加を前提にする項目は計画の入口で外している）。
    /// </para>
    /// </remarks>
    public static IEnumerable<SyncPlanWarning> WarnAddedForeignKeysNotOnCandidateKey(
        AssessmentFacts facts
    )
    {
        var postSync = facts.PostSync;
        var capabilities = facts.Capabilities;
        var liveSchema = facts.Live;
        var warnings = new List<SyncPlanWarning>();

        var adds = postSync
            .Items.Where(i =>
                i.Kind == SchemaDiffKind.AddForeignKey
                && i.ChildEntity is not null
                && i.ParentEntity is not null
            )
            .ToList();

        if (liveSchema is null || adds.Count == 0)
        {
            return warnings;
        }

        var postSyncUniques = postSync.UniqueColumnSets;

        foreach (var add in adds)
        {
            var childTable = SchemaDiffService.NormalizeTable(add.ChildEntity!);
            var parentTable = SchemaDiffService.NormalizeTable(add.ParentEntity!);
            var postSyncKey = postSync.PrimaryKeyOf(parentTable);

            if (postSyncKey is null)
            {
                continue;
            }

            var referencedColumns = ForeignKeyColumnPairResolver
                .ParentColumns(SyncScriptBuilderHelper.ResolveColumnPairs(add))
                .ToList();

            if (
                referencedColumns.Count == 0
                || SyncPlanner.StaysCandidateKey(
                    postSyncKey,
                    postSyncUniques,
                    parentTable,
                    referencedColumns
                )
            )
            {
                continue;
            }

            warnings.Add(
                new SyncPlanWarning(
                    SyncPlanWarningKind.ForeignKeyNotOnCandidateKey,
                    childTable,
                    SyncPlanner.SelectedForeignKeyName(add, capabilities.IdentifierLengthLimit)
                )
            );
        }

        return warnings;
    }
}
