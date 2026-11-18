using System.Collections.Generic;
using System.Linq;

namespace QuickER.Provider.Sync.Scheduling;

internal static partial class RebuildDeclarations
{
    /// <summary>
    /// 主キーを外す間、その主キーに支えられた外部キーは外れていなければならない（どの方言でも）
    /// </summary>
    /// <remarks>
    /// 対象は、主キーを変える表を参照し、参照する列を DB の今の主キーが支えうる外部キー
    /// （<see cref="SyncPlanner.DependsOnOldPrimaryKey"/>＝同じ列の集合。外部キーが参照先の索引の先頭の列を要する方言では、参照する列を
    /// 先頭に並べる主キーも）。一意制約に支えられた外部キーは、主キーの解除に巻き込まれない。
    /// </remarks>
    private static IEnumerable<LiveConstraint> RequireForeignKeysOffDroppedPrimaryKeys(
        SchedulingInput input
    )
    {
        var primaryKeyChanges = input.PostSync.PrimaryKeyChanges;

        foreach (var fk in input.Live.ForeignKeys)
        {
            var parentTable = SchemaDiffService.NormalizeTable(fk.Parent);

            if (
                primaryKeyChanges.ContainsKey(parentTable)
                && SyncPlanner.DependsOnOldPrimaryKey(
                    input.Live.Tables,
                    input.Capabilities,
                    parentTable,
                    [.. ForeignKeyColumnPairResolver.ParentColumns(fk.ColumnPairs)]
                )
            )
            {
                yield return new LiveForeignKey(fk);
            }
        }
    }
}
