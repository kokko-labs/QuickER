using System.Collections.Generic;
using System.Linq;
using QuickER.Provider.Sync.Steps;

namespace QuickER.Provider.Sync.Scheduling;

internal static partial class RebuildDeclarations
{
    /// <summary>
    /// 列を変える間、その列を構成列に持つ一意制約は外れていなければならない（列の変更でキーの制約の外し直しを要する方言＝
    /// <see cref="QuickER.Dialects.SyncDialectCapabilities.AlterColumnRequiresKeyConstraintRebuild"/>）
    /// </summary>
    /// <remarks>
    /// 構成列を名前へ解決できない一意制約は対象にしない（外す文を書けない）。型を変えない列の変更（NULL 許容だけ）も含む。
    /// </remarks>
    private static IEnumerable<LiveConstraint> RequireUniqueConstraintsOffAlteredColumns(
        SchedulingInput input
    )
    {
        if (!input.Capabilities.AlterColumnRequiresKeyConstraintRebuild)
        {
            yield break;
        }

        var alteredColumns = AlteredColumns(input);

        if (alteredColumns.Count == 0)
        {
            yield break;
        }

        foreach (var table in input.Live.Entities)
        {
            var tableName = SchemaDiffService.NormalizeTable(table);

            foreach (var constraint in table.UniqueConstraints)
            {
                if (
                    UniqueConstraintNaming.TryResolveColumnNames(table, constraint, out var columns)
                    && columns.Any(column =>
                        alteredColumns.Contains(SyncPlanner.ColumnKey(tableName, column))
                    )
                )
                {
                    yield return new LiveUniqueConstraint(table, constraint);
                }
            }
        }
    }

    /// <summary>
    /// 列を変える間、その列を構成列に持つ主キーは外れていなければならない（列の変更でキーの制約の外し直しを要する方言＝
    /// <see cref="QuickER.Dialects.SyncDialectCapabilities.AlterColumnRequiresKeyConstraintRebuild"/>）
    /// </summary>
    private static IEnumerable<LiveConstraint> RequirePrimaryKeysOffAlteredColumns(
        SchedulingInput input
    )
    {
        if (!input.Capabilities.AlterColumnRequiresKeyConstraintRebuild)
        {
            yield break;
        }

        var alteredColumns = AlteredColumns(input);

        if (alteredColumns.Count == 0)
        {
            yield break;
        }

        foreach (var table in input.Live.Entities)
        {
            var tableName = SchemaDiffService.NormalizeTable(table);

            if (
                table
                    .GetPrimaryKeyColumnsInOrder()
                    .Any(column =>
                        alteredColumns.Contains(SyncPlanner.ColumnKey(tableName, column.Name))
                    )
            )
            {
                yield return new LivePrimaryKey(table);
            }
        }
    }

    /// <summary>選んだ文が変える列</summary>
    private static HashSet<State.ColumnId> AlteredColumns(SchedulingInput input) =>
        input
            .Steps.Select(s => s.Step)
            .OfType<AlterColumnStep>()
            .Select(alter => SyncPlanner.ColumnKey(alter.TableName, alter.Column.Name))
            .ToHashSet();
}
