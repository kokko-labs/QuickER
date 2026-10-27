using AwesomeAssertions;
using QuickER.Tests.TestSupport.Engines;

namespace QuickER.Tests.TestSupport;

/// <summary>
/// エンジンの次元表（<see cref="DimensionTable"/>）の構築時の検査と、網羅すべき組の数え方を固定する
/// </summary>
public sealed class DimensionTableTests
{
    /// <summary>因子を水準の名前から作る</summary>
    internal static Factor F(string name, params string[] levels) =>
        new(name, levels.Select(level => new Level(level)).ToList());

    private enum Sample
    {
        First,
    }

    private sealed class Options
    {
        public int BatchSize { get; init; }
    }

    [Fact(DisplayName = "整っていない表は、問題をすべて並べて断る")]
    public void Construction_ListsAllProblems()
    {
        FluentActions
            .Invoking(() =>
                new DimensionTable(
                    "",
                    [F("A", "a", "a"), F("A", "x"), F("B"), F("C=1", "c×")],
                    [new FactorGroup("A", "B"), new FactorGroup("A", "B", "Z")]
                )
            )
            .Should()
            .Throw<ArgumentException>()
            .Which.Message.Should()
            .ContainAll(
                "エンジンの ID が空",
                "因子 A の水準 a が重複している",
                "因子 A が重複している",
                "因子 B に水準が無い",
                "因子 C=1 の名前に × か = がある",
                "因子 C=1 の水準 c× の名前に × か = がある",
                "3 因子の集まり [A, B] の因子が 3 個未満",
                "3 因子の集まり [A, B, Z] の因子 Z が表に無い"
            );
    }

    [Fact(DisplayName = "因子が 1 個しかない表は断る")]
    public void Construction_RejectsSingleFactor()
    {
        FluentActions
            .Invoking(() => new DimensionTable("E", [F("A", "a")]))
            .Should()
            .Throw<ArgumentException>()
            .WithMessage("*因子が 1 個しかない*");
    }

    [Fact(DisplayName = "網羅すべき 2 因子の組は、因子の組ごとに水準の直積を宣言の順に並べる")]
    public void RequiredPairs_AreProductPerFactorPair()
    {
        var table = new DimensionTable(
            "E",
            [F("A", "a1", "a2"), F("B", "b1", "b2", "b3"), F("C", "c1")]
        );

        var pairs = table.RequiredPairs();

        pairs.Should().HaveCount(2 * 3 + 2 * 1 + 3 * 1);
        pairs[0].Key.Should().Be("A=a1 × B=b1");
        pairs[0].FactorKey.Should().Be("A × B");
        pairs.Last().Key.Should().Be("B=b3 × C=c1");
    }

    [Fact(DisplayName = "isValid が偽を返す組は網羅すべき組から外れる")]
    public void RequiredPairs_RespectIsValid()
    {
        var table = new DimensionTable(
            "E",
            [F("A", "a1", "a2"), F("B", "b1", "b2")],
            isValid: d =>
                !(
                    d.TryGetValue("A", out var a)
                    && a == "a1"
                    && d.TryGetValue("B", out var b)
                    && b == "b2"
                )
        );

        table
            .RequiredPairs()
            .Select(t => t.Key)
            .Should()
            .NotContain("A=a1 × B=b2")
            .And.HaveCount(3);
    }

    [Fact(
        DisplayName = "固定した因子を含む組は、固定した水準のときだけ要り、isValid には固定した水準も渡す"
    )]
    public void RequiredPairs_RestrictFixedLevels()
    {
        var seen = new List<IReadOnlyDictionary<string, string>>();
        var table = new DimensionTable(
            "E",
            [F("Kind", "k1", "k2"), F("A", "a1", "a2"), F("B", "b1", "b2")],
            isValid: d =>
            {
                lock (seen)
                {
                    seen.Add(new Dictionary<string, string>(d));
                }

                return true;
            }
        );
        var fixedLevels = new Dictionary<string, string> { ["Kind"] = "k2" };

        var pairs = table.RequiredPairs(fixedLevels).Select(t => t.Key).ToList();

        pairs.Should().HaveCount(2 + 2 + 4);
        pairs.Should().NotContain(key => key.Contains("k1", StringComparison.Ordinal));
        seen.Should().OnlyContain(d => d["Kind"] == "k2");
    }

    [Fact(DisplayName = "表に無い因子・宣言していない水準を固定しようとすると断る")]
    public void FixedLevels_AreValidated()
    {
        var table = new DimensionTable("E", [F("A", "a1"), F("B", "b1")]);

        FluentActions
            .Invoking(() =>
                table.RequiredPairs(new Dictionary<string, string> { ["A"] = "zz", ["Q"] = "q" })
            )
            .Should()
            .Throw<ArgumentException>()
            .Which.Message.Should()
            .ContainAll("固定する水準 A=zz が宣言されていない", "固定する因子 Q が表に無い");
    }

    [Fact(DisplayName = "3 因子の組は集まりの 3 つ組すべてで、集まりをまたいで重複を除く")]
    public void RequiredTriples_FromGroups()
    {
        var table = new DimensionTable(
            "E",
            [F("A", "a1", "a2"), F("B", "b1", "b2"), F("C", "c1"), F("D", "d1", "d2")],
            [new FactorGroup("A", "B", "C", "D"), new FactorGroup("D", "B", "A")]
        );

        var triples = table.RequiredTriples();

        // 3 つ組は ABC・ABD・ACD・BCD の 4 つ（後の集まりの ABD は重複）
        triples.Should().HaveCount(2 * 2 * 1 + 2 * 2 * 2 + 2 * 1 * 2 + 2 * 1 * 2);
        triples
            .Select(t => t.FactorKey)
            .Distinct()
            .Should()
            .Equal("A × B × C", "A × B × D", "A × C × D", "B × C × D");
        triples.Should().OnlyHaveUniqueItems();
    }

    [Fact(DisplayName = "組の等しさは鍵の序数の一致で決まる")]
    public void CoverageTuple_EqualityByKey()
    {
        var a = new CoverageTuple([("A", "a"), ("B", "b")]);
        var b = new CoverageTuple([("A", "a"), ("B", "b")]);
        var c = new CoverageTuple([("A", "a"), ("B", "B")]);

        a.Should().Be(b);
        a.GetHashCode().Should().Be(b.GetHashCode());
        a.Should().NotBe(c);
        new HashSet<CoverageTuple> { a, b, c }
            .Should()
            .HaveCount(2);
    }

    [Fact(DisplayName = "軸の項目は、定義されていない列挙の値と無いプロパティを断る")]
    public void AxisMember_Validates()
    {
        AxisMember.Of(Sample.First).Should().Be(new AxisMember(typeof(Sample), "First"));
        AxisMember
            .Property(typeof(Options), nameof(Options.BatchSize))
            .Member.Should()
            .Be("BatchSize");

        FluentActions
            .Invoking(() => AxisMember.Of((Sample)99))
            .Should()
            .Throw<ArgumentOutOfRangeException>();
        FluentActions
            .Invoking(() => AxisMember.Property(typeof(Options), "Missing"))
            .Should()
            .Throw<ArgumentException>();
    }

    [Fact(DisplayName = "因子は名前で引け、無い因子は名指しして断る")]
    public void Factor_Lookup()
    {
        var table = new DimensionTable("E", [F("A", "a1"), F("B", "b1")]);

        table.Factor("B").Levels.Should().ContainSingle();
        FluentActions
            .Invoking(() => table.Factor("Z"))
            .Should()
            .Throw<KeyNotFoundException>()
            .WithMessage("*Z*");
    }
}
