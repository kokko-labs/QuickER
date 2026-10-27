using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace QuickER.Tests.TestSupport.Engines;

/// <summary>
/// 次元表の網羅すべき組（2 因子の組と 3 因子の組）を、少ない行ですべて含む割り当ての並びを作る
/// </summary>
/// <remarks>
/// <para>
/// AETG 型の貪欲法。まだ網羅していない組が残る間、候補の行を <see cref="CandidateCount"/> 個作り、
/// 未網羅の組を最も多く含む候補（同数なら先の候補）を採る。候補は、未網羅の組のうち並びの先頭のものの水準を固定し、
/// 残りの因子を種で決めた順に、未網羅の組を最も多く新たに含む水準（同数なら種で決めた順の先）で埋めて作る。
/// どの段でも <see cref="DimensionTable.IsValid"/> が偽になる水準は選ばない。
/// </para>
/// <para>
/// 貪欲に埋めて行き詰まった候補は捨てる。候補がすべて行き詰まったら、先頭の組を含む行を宣言の順の総当たり
/// （起こり得ない割り当てで枝を刈る）で探し、それでも無ければ、その組を名指しして <see cref="InvalidOperationException"/> にする
/// （網羅すべきとされたのに、起こり得る完全な行がその組を含まない＝<c>isValid</c> の規則の誤り）。
/// </para>
/// <para>
/// 乱数は <c>new Random(seed)</c> だけを使うので、同じ表と同じ種なら同じ行の並びを返す。
/// </para>
/// </remarks>
public static class CoveringArray
{
    /// <summary>1 行ごとに作る候補の数</summary>
    public const int CandidateCount = 20;

    /// <summary>候補が行き詰まったときの総当たりで試す水準の数の上限</summary>
    public const int MaxExhaustiveTrials = 100_000;

    /// <summary>網羅すべき組をすべて含む割り当ての並びを作る</summary>
    /// <param name="table">次元表</param>
    /// <param name="seed">種</param>
    /// <returns>行（各行は表のすべての因子に水準を割り当て、因子の宣言の順に並ぶ）</returns>
    /// <exception cref="InvalidOperationException">網羅すべき組を含む起こり得る行が無い</exception>
    public static IReadOnlyList<IReadOnlyDictionary<string, string>> Generate(
        DimensionTable table,
        int seed
    )
    {
        ArgumentNullException.ThrowIfNull(table);

        var targets = table.RequiredPairs().Concat(table.RequiredTriples()).Distinct().ToList();
        var uncovered = new HashSet<CoverageTuple>(targets);
        var byItem = new Dictionary<(string Factor, string Level), List<CoverageTuple>>();

        foreach (var tuple in targets)
        {
            foreach (var item in tuple.Items)
            {
                if (!byItem.TryGetValue(item, out var list))
                {
                    list = [];
                    byItem[item] = list;
                }

                list.Add(tuple);
            }
        }

        var random = new Random(seed);
        var rows = new List<IReadOnlyDictionary<string, string>>();
        var cursor = 0;

        while (uncovered.Count > 0)
        {
            while (!uncovered.Contains(targets[cursor]))
            {
                cursor++;
            }

            var first = targets[cursor];
            Dictionary<string, string>? best = null;
            var bestCount = -1;

            for (var k = 0; k < CandidateCount; k++)
            {
                var candidate = BuildCandidate(table, first, uncovered, byItem, random);

                if (candidate is null)
                {
                    continue;
                }

                var count = uncovered.Count(tuple => tuple.IsContainedIn(candidate));

                if (count > bestCount)
                {
                    best = candidate;
                    bestCount = count;
                }
            }

            var completed =
                best
                ?? CompleteExhaustively(table, first)
                ?? throw new InvalidOperationException(
                    $"{table.EngineId}: 組 {first.Key} は網羅すべきとされたが、起こり得る完全な行がこの組を含まない（isValid の規則を確かめる）"
                );
            var row = table.Factors.ToDictionary(
                factor => factor.Name,
                factor => completed[factor.Name],
                StringComparer.Ordinal
            );

            if (!table.IsValid(row))
            {
                throw new InvalidOperationException(
                    $"{table.EngineId}: 作った行が起こり得ない（isValid が一部の割り当てと完全な割り当てで矛盾している）: {Describe(row)}"
                );
            }

            rows.Add(row);
            uncovered.RemoveWhere(tuple => tuple.IsContainedIn(row));
        }

        return rows;
    }

    /// <summary>先頭の組を固定し、残りの因子を貪欲に埋めた候補（行き詰まれば null）</summary>
    private static Dictionary<string, string>? BuildCandidate(
        DimensionTable table,
        CoverageTuple first,
        HashSet<CoverageTuple> uncovered,
        Dictionary<(string Factor, string Level), List<CoverageTuple>> byItem,
        Random random
    )
    {
        var row = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var (factor, level) in first.Items)
        {
            row[factor] = level;
        }

        if (!table.IsValid(Copy(row)))
        {
            return null;
        }

        var remaining = table
            .Factors.Select(factor => factor.Name)
            .Where(name => !row.ContainsKey(name))
            .ToList();
        Shuffle(remaining, random);

        foreach (var name in remaining)
        {
            var levels = table.Factor(name).Levels.Select(level => level.Name).ToList();
            Shuffle(levels, random);

            string? chosen = null;
            var chosenGain = -1;

            foreach (var level in levels)
            {
                var trial = Copy(row);
                trial[name] = level;

                if (!table.IsValid(trial))
                {
                    continue;
                }

                var gain = Gain(name, level, row, uncovered, byItem);

                if (gain > chosenGain)
                {
                    chosen = level;
                    chosenGain = gain;
                }
            }

            if (chosen is null)
            {
                return null;
            }

            row[name] = chosen;
        }

        return table.IsValid(Copy(row)) ? row : null;
    }

    /// <summary>水準を加えたときに新たに含む未網羅の組の数</summary>
    private static int Gain(
        string factor,
        string level,
        Dictionary<string, string> row,
        HashSet<CoverageTuple> uncovered,
        Dictionary<(string Factor, string Level), List<CoverageTuple>> byItem
    )
    {
        if (!byItem.TryGetValue((factor, level), out var tuples))
        {
            return 0;
        }

        var gain = 0;

        foreach (var tuple in tuples)
        {
            if (!uncovered.Contains(tuple))
            {
                continue;
            }

            var completes = tuple.Items.All(item =>
                string.Equals(item.Factor, factor, StringComparison.Ordinal)
                || (
                    row.TryGetValue(item.Factor, out var assigned)
                    && string.Equals(assigned, item.Level, StringComparison.Ordinal)
                )
            );

            if (completes)
            {
                gain++;
            }
        }

        return gain;
    }

    /// <summary>組を含む起こり得る行を、宣言の順の総当たりで探す（無ければ null）</summary>
    private static Dictionary<string, string>? CompleteExhaustively(
        DimensionTable table,
        CoverageTuple first
    )
    {
        var row = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var (factor, level) in first.Items)
        {
            row[factor] = level;
        }

        if (!table.IsValid(Copy(row)))
        {
            return null;
        }

        var remaining = table.Factors.Where(factor => !row.ContainsKey(factor.Name)).ToList();
        var tried = 0;

        return Search(0) ? row : null;

        bool Search(int depth)
        {
            if (depth == remaining.Count)
            {
                return true;
            }

            var factor = remaining[depth];

            foreach (var level in factor.Levels)
            {
                // 総当たりは最悪で指数時間なので、上限を超えたら止まらずに組を名指しして断る
                if (++tried > MaxExhaustiveTrials)
                {
                    throw new InvalidOperationException(
                        string.Create(
                            CultureInfo.InvariantCulture,
                            $"{table.EngineId}: 組 {first.Key} を含む起こり得る行を、{MaxExhaustiveTrials} 通り試しても見つけられない（isValid の規則を確かめる）"
                        )
                    );
                }

                row[factor.Name] = level.Name;

                if (table.IsValid(Copy(row)) && Search(depth + 1))
                {
                    return true;
                }
            }

            row.Remove(factor.Name);
            return false;
        }
    }

    /// <summary>規則へ渡す割り当て（規則が受け取った辞書を持ち続けても、後の変更が及ばないように写す）</summary>
    private static Dictionary<string, string> Copy(Dictionary<string, string> row) =>
        new(row, StringComparer.Ordinal);

    private static void Shuffle<T>(List<T> items, Random random)
    {
        for (var i = items.Count - 1; i > 0; i--)
        {
            var j = random.Next(i + 1);
            (items[i], items[j]) = (items[j], items[i]);
        }
    }

    private static string Describe(IReadOnlyDictionary<string, string> row) =>
        string.Join(CoverageTuple.Separator, row.Select(pair => $"{pair.Key}={pair.Value}"));
}
