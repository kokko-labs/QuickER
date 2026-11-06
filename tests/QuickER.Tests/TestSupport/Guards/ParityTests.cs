using AwesomeAssertions;
using QuickER.Tests.TestSupport.Engines;

namespace QuickER.Tests.TestSupport.Guards;

/// <summary>照合の表を当てる（1 行 1 照合）・比べ方の自己検査・表の整合・網羅の照合</summary>
public sealed class ParityTests
{
    /// <summary>行の Id</summary>
    public static TheoryData<string> RowIds => [.. ParityRows.All.Select(row => row.Id)];

    /// <summary>比べ方の自己検査の行（比べ方・正本・写し・一致するか）</summary>
    internal static readonly (
        ParityCompare Compare,
        string[] Canon,
        string[] Copy,
        bool Agrees
    )[] ComparerProbes =
    [
        (ParityCompare.Set, ["a", "b"], ["b", "a", "a"], true),
        (ParityCompare.Set, ["a", "b"], ["a"], false),
        (ParityCompare.Multiset, ["a", "b", "a"], ["a", "a", "b"], true),
        (ParityCompare.Multiset, ["a", "b"], ["a", "b", "b"], false),
        (ParityCompare.Sequence, ["a", "b"], ["a", "b"], true),
        (ParityCompare.Sequence, ["a", "b"], ["b", "a"], false),
        (ParityCompare.CopyWithinCanon, ["a", "b", "c"], ["c", "a"], true),
        (ParityCompare.CopyWithinCanon, ["a", "b"], ["a", "x"], false),
        (ParityCompare.CanonWithinCopy, ["a", "b"], ["b", "x", "a"], true),
        (ParityCompare.CanonWithinCopy, ["a", "b"], ["a", "x"], false),
    ];

    /// <summary>比べ方の自己検査の行の番号</summary>
    public static TheoryData<int> ComparerProbeIndexes =>
        [.. Enumerable.Range(0, ComparerProbes.Length)];

    [Theory(DisplayName = "照合の表：正本と写しが決めた比べ方で一致する")]
    [MemberData(nameof(RowIds))]
    public void Parity_Holds(string id)
    {
        var row = ParityRows.ById(id);
        var canon = row.Canon();
        var copy = row.Copy();

        canon
            .Count.Should()
            .BeGreaterThanOrEqualTo(
                row.MinimumCanon,
                $"[{row.Id}] 正本の項目が少なすぎる（正本を読めずに空振りしていないか）"
            );

        var problems = Problems(row.Compare, canon, copy);

        problems
            .Should()
            .BeEmpty(
                $"[{row.Id}] {row.Because}（比べ方 {row.Compare}）:"
                    + Environment.NewLine
                    + string.Join(Environment.NewLine, problems)
            );
    }

    [Theory(DisplayName = "照合の表の自己検査：比べ方は一致を通し、食い違いを名指しする")]
    [MemberData(nameof(ComparerProbeIndexes))]
    public void Comparer_DetectsDisagreement(int index)
    {
        var (compare, canon, copy, agrees) = ComparerProbes[index];

        Problems(compare, canon, copy)
            .Should()
            .HaveCount(
                agrees ? 0 : 1,
                $"{compare} の [{string.Join(",", canon)}] と [{string.Join(",", copy)}]"
            );
    }

    [Fact(DisplayName = "照合の表：Id が一意で、守ることが空でなく、どの比べ方も使われている")]
    public void Table_IsWellFormed()
    {
        ParityRows.All.Select(row => row.Id).Should().OnlyHaveUniqueItems();
        ParityRows
            .All.Where(row => string.IsNullOrWhiteSpace(row.Because))
            .Select(row => row.Id)
            .Should()
            .BeEmpty("守ることを書く");
        Enum.GetValues<ParityCompare>()
            .Except(ParityRows.All.Select(row => row.Compare))
            .Should()
            .BeEmpty("使われない比べ方は列挙から消す（自己検査だけの比べ方を残さない）");
    }

    [Fact(DisplayName = "[網羅] 照合の表の比べ方の自己検査は、比べ方 × 結果の組を網羅する")]
    public void Coverage_MatchesGaps()
    {
        var counter = new CoverageCounter(GuardTextDimensions.ParityTable);

        foreach (var probe in ComparerProbes)
        {
            counter.Observe(
                new Dictionary<string, string>
                {
                    [GuardTextDimensions.Compare] = probe.Compare.ToString(),
                    [GuardTextDimensions.Outcome] = probe.Agrees
                        ? GuardTextDimensions.Agrees
                        : GuardTextDimensions.Disagrees,
                }
            );
        }

        var report = counter.Report();
        report.WriteSummary();
        report.AssertGaps(CoverageGap.Set(EngineIds.Guard, []), GuardTextDimensions.ParityScope);
    }

    /// <summary>正本と写しの食い違い（無ければ空。食い違いがあれば 1 行にまとめる）</summary>
    internal static IReadOnlyList<string> Problems(
        ParityCompare compare,
        IReadOnlyList<string> canon,
        IReadOnlyList<string> copy
    )
    {
        string? problem = compare switch
        {
            ParityCompare.Set => Describe(
                canon.Except(copy, StringComparer.Ordinal),
                copy.Except(canon, StringComparer.Ordinal)
            ),
            ParityCompare.Multiset => Describe(
                MultisetMinus(canon, copy),
                MultisetMinus(copy, canon)
            ),
            ParityCompare.Sequence => canon.SequenceEqual(copy, StringComparer.Ordinal)
                ? null
                : $"順が違う：正本 [{string.Join(", ", canon)}]・写し [{string.Join(", ", copy)}]",
            ParityCompare.CopyWithinCanon => Describe(
                [],
                copy.Except(canon, StringComparer.Ordinal)
            ),
            ParityCompare.CanonWithinCopy => Describe(
                canon.Except(copy, StringComparer.Ordinal),
                []
            ),
            _ => throw new ArgumentOutOfRangeException(nameof(compare), compare, null),
        };

        return problem is null ? [] : [problem];
    }

    private static string? Describe(IEnumerable<string> onlyInCanon, IEnumerable<string> onlyInCopy)
    {
        var canon = onlyInCanon.ToList();
        var copy = onlyInCopy.ToList();

        if (canon.Count == 0 && copy.Count == 0)
        {
            return null;
        }

        return $"正本だけにある [{string.Join(", ", canon)}]・写しだけにある [{string.Join(", ", copy)}]";
    }

    private static List<string> MultisetMinus(
        IReadOnlyList<string> left,
        IReadOnlyList<string> right
    )
    {
        var remaining = right
            .GroupBy(item => item, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
        var result = new List<string>();

        foreach (var item in left)
        {
            if (remaining.TryGetValue(item, out var count) && count > 0)
            {
                remaining[item] = count - 1;
            }
            else
            {
                result.Add(item);
            }
        }

        return result;
    }
}
