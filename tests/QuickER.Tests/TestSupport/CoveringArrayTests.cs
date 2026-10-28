using AwesomeAssertions;
using QuickER.Tests.TestSupport.Engines;
using static QuickER.Tests.TestSupport.DimensionTableTests;

namespace QuickER.Tests.TestSupport;

/// <summary>
/// 網羅すべき組を少ない行ですべて含む割り当ての並び（<see cref="CoveringArray"/>）を固定する
/// </summary>
public sealed class CoveringArrayTests
{
    private static DimensionTable SixByThree(
        Func<IReadOnlyDictionary<string, string>, bool>? isValid = null
    ) =>
        new(
            "E",
            new[] { "A", "B", "C", "D", "E", "F" }
                .Select(name => F(name, $"{name}1", $"{name}2", $"{name}3"))
                .ToList(),
            isValid: isValid
        );

    private static void ShouldCoverAll(
        DimensionTable table,
        IReadOnlyList<IReadOnlyDictionary<string, string>> rows
    )
    {
        foreach (var tuple in table.RequiredPairs().Concat(table.RequiredTriples()))
        {
            rows.Should()
                .Contain(row => tuple.IsContainedIn(row), $"組 {tuple.Key} を含む行が要る");
        }
    }

    [Fact(DisplayName = "網羅すべき 2 因子の組をすべて含み、行の数は直積よりずっと少ない")]
    public void Generate_CoversAllPairs_WithFewRows()
    {
        var table = SixByThree();

        var rows = CoveringArray.Generate(table, seed: 1);

        ShouldCoverAll(table, rows);
        rows.Should().HaveCountLessThanOrEqualTo(25);
        rows.Should().OnlyContain(row => row.Count == 6);
        rows[0].Keys.Should().Equal("A", "B", "C", "D", "E", "F");
    }

    [Fact(DisplayName = "3 因子の集まりの組もすべて含む")]
    public void Generate_CoversTriples()
    {
        var table = new DimensionTable(
            "E",
            [F("A", "a1", "a2"), F("B", "b1", "b2"), F("C", "c1", "c2"), F("D", "d1", "d2", "d3")],
            [new FactorGroup("A", "B", "C")]
        );

        var rows = CoveringArray.Generate(table, seed: 7);

        ShouldCoverAll(table, rows);
        table.RequiredTriples().Should().HaveCount(8);
    }

    [Fact(DisplayName = "同じ種なら同じ行の並びを返し、違う種では違い得る")]
    public void Generate_IsDeterministicPerSeed()
    {
        var table = SixByThree();

        static string Render(IReadOnlyList<IReadOnlyDictionary<string, string>> rows) =>
            string.Join("\n", rows.Select(row => string.Join(",", row.Values)));

        var first = Render(CoveringArray.Generate(table, 3));
        var again = Render(CoveringArray.Generate(table, 3));
        var others = Enumerable
            .Range(4, 5)
            .Select(seed => Render(CoveringArray.Generate(table, seed)));

        again.Should().Be(first);
        others.Should().Contain(other => other != first);
    }

    /// <summary>A=A1 と B=B1 は同時に起こり得ない・C=C3 のときは D=D3 だけ</summary>
    private static bool IsValid(IReadOnlyDictionary<string, string> d) =>
        !(d.TryGetValue("A", out var a) && a == "A1" && d.TryGetValue("B", out var b) && b == "B1")
        && !(
            d.TryGetValue("C", out var c) && c == "C3" && d.TryGetValue("D", out var x) && x != "D3"
        );

    [Fact(DisplayName = "どの行も isValid を満たす（起こり得ない組を含まない）")]
    public void Generate_RespectsIsValid()
    {
        var table = SixByThree(IsValid);

        var rows = CoveringArray.Generate(table, seed: 11);

        rows.Should().OnlyContain(row => IsValid(row));
        ShouldCoverAll(table, rows);
    }

    [Fact(DisplayName = "網羅すべきなのに、起こり得る完全な行が含まない組は名指しして断る")]
    public void Generate_UnrealizableTuple_Throws()
    {
        // A=a1 と B=b1 の組そのものは起こり得るが、C が加わるとどの水準でも起こり得ない（規則の誤りの形）
        var table = new DimensionTable(
            "E",
            [F("A", "a1", "a2"), F("B", "b1", "b2"), F("C", "c1", "c2")],
            isValid: d =>
                !(
                    d.TryGetValue("A", out var a)
                    && a == "a1"
                    && d.TryGetValue("B", out var b)
                    && b == "b1"
                    && d.ContainsKey("C")
                )
        );

        FluentActions
            .Invoking(() => CoveringArray.Generate(table, 1))
            .Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*A=a1 × B=b1*");
    }

    [Fact(DisplayName = "行き詰まったときの総当たりは上限で止まり、組を名指しして断る")]
    public void Generate_ExhaustiveSearch_StopsAtLimit()
    {
        // 部分の割り当てはどれも起こり得るが、完全な割り当ては 1 つも起こり得ない（2^18 通りは上限を超える）
        var factors = Enumerable.Range(0, 20).Select(i => F($"X{i}", "a", "b")).ToList();
        var table = new DimensionTable("E", factors, isValid: d => d.Count < factors.Count);

        FluentActions
            .Invoking(() => CoveringArray.Generate(table, 1))
            .Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*X0=a × X1=a*通り試しても*");
    }
}
