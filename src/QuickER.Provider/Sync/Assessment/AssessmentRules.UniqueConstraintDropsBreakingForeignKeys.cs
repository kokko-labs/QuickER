using System.Collections.Generic;
using System.Linq;
using QuickER.Model;
using QuickER.Provider.Sync.State;

namespace QuickER.Provider.Sync.Assessment;

internal static partial class AssessmentRules
{
    /// <summary>
    /// 選択された一意制約の削除が既存 FK の被参照列を候補キーでなくしうる場合に警告を作る。
    /// </summary>
    /// <param name="facts">計画の事実（<see cref="AssessmentFacts.PostSync"/>・<see cref="AssessmentFacts.Live"/>・<see cref="AssessmentFacts.Capabilities"/>）</param>
    /// <returns>
    /// 名指しの警告（外部キーを支えうる一意制約の削除 1 つにつき 1 件。同じ外部キーを支えうる削除が 2 つ以上なら同じ値を
    /// 続けて返し、<see cref="Add"/> が 1 件にする）
    /// </returns>
    /// <remarks>
    /// 削除する一意制約がその外部キーを支えうるか（<see cref="PlanFacts.CanSupportForeignKey"/>＝前寄せと同じ判定。MySQL は参照する列を先頭に
    /// 並べる一意制約も）で判定する。ほかのキーが同じ列を覆っていれば実際には壊れないが、誤警告を許容してほかのキーは数えず、
    /// 実行はブロックしない（レンダラーは DROP を出す）。
    /// </remarks>
    public static IEnumerable<SyncPlanWarning> WarnUniqueConstraintDropsBreakingForeignKeys(
        AssessmentFacts facts
    )
    {
        var postSync = facts.PostSync;
        var liveSchema = facts.Live;
        var capabilities = facts.Capabilities;
        var warnings = new List<SyncPlanWarning>();
        var limit = capabilities.IdentifierLengthLimit;

        if (liveSchema is null)
        {
            return warnings;
        }

        var drops = postSync
            .Items.Where(i => i.Kind == SchemaDiffKind.DropUniqueConstraint)
            .ToList();

        if (drops.Count == 0)
        {
            return warnings;
        }

        // 同期で外す外部キー（明示的に外す・端が消える＝計画の入口で外す項目を足している）は壊れようがないので警告しない
        var explicitlyDropped = postSync.DroppedForeignKeys;
        var dropped = postSync.Dropped;
        var pkChangedTables = postSync.PrimaryKeyChanges;

        foreach (var fk in liveSchema.ForeignKeys)
        {
            var parentTable = SchemaDiffService.NormalizeTable(fk.Parent);
            var childTable = SchemaDiffService.NormalizeTable(fk.Child);

            // 新しい主キーが参照する列とちょうど同じなら、候補キーは主キーが保つ（主キーの変更の外し直しが新しい主キーへ付け直す）
            if (
                dropped.Touches(fk, childTable, parentTable)
                || explicitlyDropped.Contains(
                    PlanIdentity.ForeignKeyKey(
                        childTable,
                        parentTable,
                        fk.ColumnPairs,
                        fk.Relationship,
                        limit
                    )
                )
                || (
                    pkChangedTables.TryGetValue(parentTable, out var newKey)
                    && PlanFacts.IsSameColumnSet(
                        newKey,
                        ForeignKeyColumnPairResolver.ParentColumns(fk.ColumnPairs)
                    )
                )
            )
            {
                continue;
            }

            var referenced = ForeignKeyColumnPairResolver.ParentColumns(fk.ColumnPairs).ToList();

            foreach (var drop in drops)
            {
                if (!SchemaNames.AreSame(SchemaNames.ToStoredForm(drop.TableName), parentTable))
                {
                    continue;
                }

                if (
                    !PlanFacts.CanSupportForeignKey(
                        capabilities,
                        drop.UniqueConstraintColumns,
                        referenced
                    )
                )
                {
                    continue;
                }

                // Detail は「子の表.外部キーの名前」（外部キーの名前が表ごとの方言で、別の子の表の同じ名前の外部キーを 1 行に畳まない）
                warnings.Add(
                    new SyncPlanWarning(
                        SyncPlanWarningKind.UniqueConstraintDropMayBreakForeignKey,
                        parentTable,
                        childTable
                            + "."
                            + PlanIdentity.ResolveForeignKeyName(
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
