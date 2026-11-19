using System.Collections.Generic;
using System.Linq;

namespace QuickER.Provider.Sync.Assessment;

internal static partial class AssessmentRules
{
    /// <summary>
    /// テーブル再構築の方言で、主キーを変える表を参照する DB の外部キーのうち、参照先の列が同期後に候補キーでなくなりうるものを、
    /// <see cref="SyncPlanWarningKind.ForeignKeyRebuildMayLoseCandidateKey"/> で名指しする
    /// </summary>
    /// <param name="facts">計画の事実（<see cref="AssessmentFacts.PostSync"/>・<see cref="AssessmentFacts.Live"/>・<see cref="AssessmentFacts.Capabilities"/>）</param>
    /// <returns>名指しの警告（DB の今の図の外部キーの並び。表は子の表・Detail は制約の実効名）</returns>
    /// <remarks>
    /// <para>
    /// テーブル再構築の方言でなければ何も返さない（逐次 DDL の方言は同じ外部キーを外して付け直し、その見込みは
    /// <see cref="WarnDetachedForeignKeysAtRisk"/> が名指しする）。DB の今の図が無くても何も返さない。
    /// </para>
    /// <para>
    /// 再構築の方言は外部キーを外して付け直すのではなく、子の表の外部キーの宣言を残したまま親の表を作り直す。参照先の列が候補キーで
    /// なくなると、同期後の <c>foreign_key_check</c> が foreign key mismatch で失敗する（巻き戻るので安全側だが、逐次 DDL の方言と同じく
    /// 実行前に知らせる）。判定は <see cref="SyncPlanner.StaysCandidateKey"/> を逐次 DDL の方言と共有する。
    /// </para>
    /// <para>
    /// 明示的に削除する外部キー（選んだ外部キーの削除）と、削除する子の表（選んだ表の削除）の外部キーは、同期後に残らないので見ない。
    /// </para>
    /// </remarks>
    public static IEnumerable<SyncPlanWarning> WarnForeignKeysLosingCandidateKeyByTableRebuild(
        AssessmentFacts facts
    )
    {
        var postSync = facts.PostSync;
        var capabilities = facts.Capabilities;
        var liveSchema = facts.Live;
        var warnings = new List<SyncPlanWarning>();

        if (!facts.IsTableRebuildDialect || liveSchema is null)
        {
            return warnings;
        }

        var limit = capabilities.IdentifierLengthLimit;
        var pkChangedTables = postSync.PrimaryKeyChanges;

        if (pkChangedTables.Count == 0)
        {
            return warnings;
        }

        var postSyncUniques = postSync.UniqueColumnSets;
        var dropped = postSync.Dropped;
        var explicitlyDropped = postSync.DroppedForeignKeys;

        foreach (var fk in liveSchema.ForeignKeys)
        {
            var childTable = SchemaDiffService.NormalizeTable(fk.Child);
            var parentTable = SchemaDiffService.NormalizeTable(fk.Parent);

            if (
                !pkChangedTables.TryGetValue(parentTable, out var newPkColumns)
                || dropped.Tables.Contains(childTable)
                || explicitlyDropped.Contains(
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

            var referencedColumns = ForeignKeyColumnPairResolver
                .ParentColumns(fk.ColumnPairs)
                .ToList();

            if (
                SyncPlanner.StaysCandidateKey(
                    newPkColumns,
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
                    SyncPlanWarningKind.ForeignKeyRebuildMayLoseCandidateKey,
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

        return warnings;
    }
}
