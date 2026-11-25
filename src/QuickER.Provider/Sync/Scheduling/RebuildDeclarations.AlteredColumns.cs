using System.Collections.Generic;
using System.Linq;
using QuickER.Provider.Sync.State;

namespace QuickER.Provider.Sync.Scheduling;

internal static partial class RebuildDeclarations
{
    /// <summary>
    /// 列を変える間、その列を端に持つ外部キーは外れていなければならない（列の変更で外部キーの外し直しを要する方言＝
    /// <see cref="QuickER.Dialects.SyncDialectCapabilities.AlterColumnRequiresForeignKeyRebuild"/>）
    /// </summary>
    /// <remarks>
    /// 子の列でも親の列でも、列の組のどれか 1 つでも変える列なら対象。型を変えない列の変更（NULL 許容だけ）も含む。
    /// </remarks>
    private static IEnumerable<LiveConstraint> RequireForeignKeysOffAlteredColumns(
        SchedulingInput input
    )
    {
        if (!input.Capabilities.AlterColumnRequiresForeignKeyRebuild)
        {
            yield break;
        }

        var alteredColumns = AlteredColumns(input);

        if (alteredColumns.Count == 0)
        {
            yield break;
        }

        foreach (var fk in input.Live.ForeignKeys)
        {
            var childTable = SchemaDiffService.NormalizeTable(fk.Child);
            var parentTable = SchemaDiffService.NormalizeTable(fk.Parent);

            if (
                fk.ColumnPairs.Any(pair =>
                    alteredColumns.Contains(PlanIdentity.ColumnKey(childTable, pair.ChildColumn))
                    || alteredColumns.Contains(
                        PlanIdentity.ColumnKey(parentTable, pair.ParentColumn)
                    )
                )
            )
            {
                yield return new LiveForeignKey(fk);
            }
        }
    }
}
