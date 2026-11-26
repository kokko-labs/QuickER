using QuickER.Model;
using QuickER.Provider.Sync.State;

namespace QuickER.Provider.Sync.Scheduling;

/// <summary>
/// 列順を同期する逐次 DDL の方言（<see cref="SyncDialectCapabilities.ColumnReorder"/> が
/// <see cref="ColumnReorderMode.Native"/>＝MySQL）の、選んだ列順の項目から列を動かす計画を組む（呼ぶのは
/// <see cref="SequentialScheduler"/>）
/// </summary>
internal static class ColumnReorders
{
    /// <summary>選択済み ReorderColumns をネイティブ並べ替え計画（<see cref="TableReorderPlan"/>）へ変換する</summary>
    /// <remarks>
    /// live スキーマから移動集合を計算するため live の図（<paramref name="liveSchema"/>）が必須（rebuild 方言と同じく、選択された ReorderColumns が
    /// あるのに live の図が無ければ呼び出し側のバグとして例外にする）。live に無いテーブルの項目は、そのテーブル自体が
    /// 同期対象外とみなして黙って落とす。
    /// </remarks>
    internal static IReadOnlyList<TableReorderPlan> BuildReorderPlans(
        List<SchemaDiffItem> selected,
        LiveSchema? liveSchema
    )
    {
        var reorderItems = selected.Where(i => i.Kind == SchemaDiffKind.ReorderColumns).ToList();

        if (reorderItems.Count == 0)
        {
            return [];
        }

        if (liveSchema is null)
        {
            // ネイティブ並べ替えの移動集合計算には live スキーマが必須（合成定義も live 由来）
            throw new InvalidOperationException(
                "Native column-reorder dialects require a SyncPlanContext (live schema) to compute column moves."
            );
        }

        var liveByName = liveSchema.Tables;

        var plans = new List<TableReorderPlan>();

        foreach (var item in reorderItems)
        {
            var table = SchemaNames.ToStoredForm(item.TableName);

            // live に無いテーブル（そのテーブル自体が同期対象外）・target 不明なら並べ替えられない
            if (!liveByName.TryGet(table, out var live) || item.Entity is null)
            {
                continue;
            }

            var moves = ComputeColumnMoves(live, item.Entity, selected, table);

            // 実質移動が無い（既に目標順）場合は計画に含めない
            if (moves.Count == 0)
            {
                continue;
            }

            plans.Add(
                new TableReorderPlan
                {
                    TableName = live.TableName,
                    Moves = moves,
                    SourceItems = [item],
                }
            );
        }

        return plans;
    }

    /// <summary>1 テーブル分の列移動集合を最小移動（LIS を不動とする）で計算する</summary>
    /// <remarks>
    /// <para>
    /// 実効列順 = live の列順に「選択済み DropColumn を除外・選択済み AddColumn を末尾追加」した並び。
    /// 目標順 = target の列順のうち実効列集合に含まれるもの。目標順にある列（tracked）を実効順で見たとき、
    /// 最長増加部分列（LIS）に入る列は既に相対順が正しいため不動とし、それ以外だけを移動対象にする。
    /// </para>
    /// <para>
    /// 移動は目標順に前から確定し、各移動列の <c>AFTER</c> は目標順で直前の列（先頭なら <c>FIRST</c>）にする。
    /// 直前の列は目標順の左から順に確定済みになっているため、最終的な並びは目標順どおりになる。
    /// 移動列の定義は合成スキーマ由来（選択済み AlterColumn / AddColumn があればその新定義、無ければ live 定義）。
    /// </para>
    /// </remarks>
    private static IReadOnlyList<ColumnMove> ComputeColumnMoves(
        Entity live,
        Entity target,
        List<SchemaDiffItem> selected,
        string table
    )
    {
        // このテーブルの選択済み Add / Drop / Alter を集める
        var tableSelected = selected
            .Where(i => SchemaNames.AreSame(SchemaNames.ToStoredForm(i.TableName), table))
            .ToList();
        var droppedNames = tableSelected
            .Where(i => i.Kind == SchemaDiffKind.DropColumn && i.ColumnName is not null)
            .Select(i => i.ColumnName!)
            .ToHashSet(SchemaNames.Comparer);
        var addedColumns = tableSelected
            .Where(i => i.Kind == SchemaDiffKind.AddColumn && i.Column is not null)
            .Select(i => i.Column!)
            .ToList();

        // 実効列順 = live − 選択 Drop ＋ 選択 Add（末尾）
        var effective = new List<string>();

        foreach (var column in live.Columns)
        {
            if (!droppedNames.Contains(column.Name))
            {
                effective.Add(column.Name);
            }
        }

        effective.AddRange(addedColumns.Select(c => c.Name));

        var effectiveSet = effective.ToHashSet(SchemaNames.Comparer);

        // 目標順 = target の列順のうち実効列集合に含まれるもの（＝並べ替え対象になり得る列）
        var targetOrder = target.Columns.Select(c => c.Name).Where(effectiveSet.Contains).ToList();
        var targetIndex = new Dictionary<string, int>(SchemaNames.Comparer);

        for (var i = 0; i < targetOrder.Count; i++)
        {
            targetIndex.TryAdd(targetOrder[i], i);
        }

        // 実効順に並ぶ tracked 列（目標順にある列）の目標インデックス列を作り、LIS を不動集合にする
        var effectiveTrackedIndices = effective
            .Where(targetIndex.ContainsKey)
            .Select(name => targetIndex[name])
            .ToList();
        var fixedIndices = LongestIncreasingSubsequence(effectiveTrackedIndices);

        var moves = new List<ColumnMove>();

        for (var ti = 0; ti < targetOrder.Count; ti++)
        {
            if (fixedIndices.Contains(ti))
            {
                continue;
            }

            var name = targetOrder[ti];
            var after = ti == 0 ? null : targetOrder[ti - 1];
            moves.Add(
                new ColumnMove(ResolveMovedColumnDefinition(name, tableSelected, live), after)
            );
        }

        return moves;
    }

    /// <summary>移動する列の定義を合成スキーマ規則で解決する（選択 Add → 選択 Alter → live の順）</summary>
    private static Column ResolveMovedColumnDefinition(
        string name,
        IReadOnlyList<SchemaDiffItem> tableSelected,
        Entity live
    )
    {
        // 選択済み AddColumn の新規列（live に無い）はその定義で移動する
        var add = tableSelected.FirstOrDefault(i =>
            i.Kind == SchemaDiffKind.AddColumn
            && i.Column is not null
            && SchemaNames.AreSame(i.Column.Name, name)
        );

        if (add?.Column is not null)
        {
            return add.Column;
        }

        // 選択済み AlterColumn があれば新定義で移動する（未選択の型変更は紛れ込ませないため live 定義）
        var alter = tableSelected.FirstOrDefault(i =>
            i.Kind == SchemaDiffKind.AlterColumn
            && i.Column is not null
            && SchemaNames.AreSame(i.Column.Name, name)
        );

        if (alter?.Column is not null)
        {
            return alter.Column;
        }

        return live.Columns.FirstOrDefault(c => SchemaNames.AreSame(c.Name, name))
            ?? new Column { Name = name };
    }

    /// <summary>
    /// 数列の最長増加部分列（厳密増加）に含まれる値の集合を返す（O(n log n)・ペイシェンスソート＋前駆復元）。
    /// </summary>
    /// <remarks>値は目標インデックス（distinct）。ここに残った値の列は移動不要（不動）とみなす。</remarks>
    private static HashSet<int> LongestIncreasingSubsequence(IReadOnlyList<int> sequence)
    {
        var result = new HashSet<int>();
        var count = sequence.Count;

        if (count == 0)
        {
            return result;
        }

        // tails[k] = 長さ k+1 の増加部分列の末尾になり得る最小値の、sequence 内インデックス
        var tails = new List<int>();
        var predecessor = new int[count];

        for (var i = 0; i < count; i++)
        {
            predecessor[i] = -1;
            // 厳密増加のため sequence[i] 以上になる最初の位置（lower_bound）を二分探索する
            var lo = 0;
            var hi = tails.Count;

            while (lo < hi)
            {
                var mid = (lo + hi) / 2;

                if (sequence[tails[mid]] < sequence[i])
                {
                    lo = mid + 1;
                }
                else
                {
                    hi = mid;
                }
            }

            if (lo > 0)
            {
                predecessor[i] = tails[lo - 1];
            }

            if (lo == tails.Count)
            {
                tails.Add(i);
            }
            else
            {
                tails[lo] = i;
            }
        }

        // 末尾から前駆をたどって 1 本の LIS を復元する
        for (var k = tails[^1]; k >= 0; k = predecessor[k])
        {
            result.Add(sequence[k]);
        }

        return result;
    }
}
