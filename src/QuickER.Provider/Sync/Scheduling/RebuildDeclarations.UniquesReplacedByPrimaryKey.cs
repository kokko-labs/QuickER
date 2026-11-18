using System.Collections.Generic;
using System.Linq;
using QuickER.Model;
using QuickER.Provider.Sync.Steps;

namespace QuickER.Provider.Sync.Scheduling;

internal static partial class RebuildDeclarations
{
    /// <summary>
    /// 主キーが一意制約の列へ移り、その一意制約を外す間、その一意制約に支えられた外部キーは外れていなければならない（どの方言でも）
    /// </summary>
    /// <remarks>
    /// 対象は、主キーを変える表を参照し、参照する列が新しい主キーと同じ列の集合で、同じ計画がその表から外す一意制約のどれかが
    /// 参照する列を支えうる外部キー（<see cref="SyncPlanner.CanSupportForeignKey"/>）。付け直しは新しい主キーが支える。
    /// </remarks>
    private static IEnumerable<LiveConstraint> RequireForeignKeysOffUniquesReplacedByPrimaryKey(
        SchedulingInput input
    )
    {
        var primaryKeyChanges = input.PostSync.PrimaryKeyChanges;
        var uniqueDrops = input
            .Steps.Select(s => s.Step)
            .OfType<DropUniqueConstraintStep>()
            .ToList();

        foreach (var fk in input.Live.ForeignKeys)
        {
            var parentTable = SchemaDiffService.NormalizeTable(fk.Parent);

            if (!primaryKeyChanges.TryGetValue(parentTable, out var newPrimaryKey))
            {
                continue;
            }

            var referencedColumns = ForeignKeyColumnPairResolver
                .ParentColumns(fk.ColumnPairs)
                .ToList();

            if (
                SyncPlanner.IsSameColumnSet(newPrimaryKey, referencedColumns)
                && uniqueDrops.Any(drop =>
                    SchemaNames.AreSame(SchemaNames.ToStoredForm(drop.TableName), parentTable)
                    && SyncPlanner.CanSupportForeignKey(
                        input.Capabilities,
                        drop.Columns,
                        referencedColumns
                    )
                )
            )
            {
                yield return new LiveForeignKey(fk);
            }
        }
    }
}
