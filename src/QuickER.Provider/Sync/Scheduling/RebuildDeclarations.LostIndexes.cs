using System.Collections.Generic;
using System.Linq;
using QuickER.Model;
using QuickER.Provider.Sync.Steps;

namespace QuickER.Provider.Sync.Scheduling;

internal static partial class RebuildDeclarations
{
    /// <summary>
    /// 主キー・一意制約を外す間、子の列がその索引の先頭に乗る外部キーは外れていなければならない（外部キーが子の列を先頭に持つ索引を
    /// 要する方言＝<see cref="QuickER.Dialects.SyncDialectCapabilities.ForeignKeyRequiresLeadingIndex"/>）
    /// </summary>
    /// <remarks>
    /// 外す索引は、主キーを変える表の DB の今の主キーと、同じ計画が外す一意制約（選んだ削除・作り直しの展開・同期後の状態の差として
    /// 足した削除）。外部キーを支える唯一の索引を外すと DB が断るので、子の列が先頭の列とちょうど同じ集合になる外部キーを外す
    /// （<see cref="SyncPlanner.IsLeadingPrefix"/>）。別の索引が支えている外部キーも外し直すことになるが、外して付け直すだけ。
    /// </remarks>
    private static IEnumerable<LiveConstraint> RequireForeignKeysOffLostIndexes(
        SchedulingInput input
    )
    {
        if (!input.Capabilities.ForeignKeyRequiresLeadingIndex)
        {
            yield break;
        }

        // 表 → 外す索引の列の並び
        var lostIndexes = new Dictionary<string, List<IReadOnlyList<string>>>(SchemaNames.Comparer);

        foreach (var table in input.PostSync.PrimaryKeyChanges.Keys)
        {
            if (input.Live.Tables.TryGet(table, out var live))
            {
                IndexesOf(table).Add([.. live.GetPrimaryKeyColumnsInOrder().Select(c => c.Name)]);
            }
        }

        foreach (var drop in input.Steps.Select(s => s.Step).OfType<DropUniqueConstraintStep>())
        {
            IndexesOf(SchemaNames.ToStoredForm(drop.TableName)).Add(drop.Columns);
        }

        if (lostIndexes.Count == 0)
        {
            yield break;
        }

        foreach (var fk in input.Live.ForeignKeys)
        {
            if (
                lostIndexes.TryGetValue(SchemaDiffService.NormalizeTable(fk.Child), out var indexes)
                && indexes.Any(index => SyncPlanner.IsLeadingPrefix(index, fk.ColumnPairs))
            )
            {
                yield return new LiveForeignKey(fk);
            }
        }

        List<IReadOnlyList<string>> IndexesOf(string table)
        {
            if (!lostIndexes.TryGetValue(table, out var indexes))
            {
                indexes = [];
                lostIndexes[table] = indexes;
            }

            return indexes;
        }
    }
}
