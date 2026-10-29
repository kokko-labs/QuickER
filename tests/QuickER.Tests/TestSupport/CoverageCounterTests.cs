using AwesomeAssertions;
using QuickER.Tests.TestSupport.Engines;
using Xunit.Sdk;
using static QuickER.Tests.TestSupport.DimensionTableTests;

namespace QuickER.Tests.TestSupport;

/// <summary>
/// 網羅の数え上げ（<see cref="CoverageCounter"/>）と、網羅しない組の一覧（<see cref="CoverageGap"/>）との照合を固定する
/// </summary>
public sealed class CoverageCounterTests
{
    private const string Scope = "entrance";

    /// <summary>A(a1,a2) × B(b1,b2) × C(c1,c2)・A=a2 と C=c2 は原理的に起こり得ない</summary>
    private static DimensionTable Table() =>
        new(
            "E",
            [F("A", "a1", "a2"), F("B", "b1", "b2"), F("C", "c1", "c2")],
            isValid: d =>
                !(
                    d.TryGetValue("A", out var a)
                    && a == "a2"
                    && d.TryGetValue("C", out var c)
                    && c == "c2"
                )
        );

    private static Dictionary<string, string> Row(string a, string b, string c) =>
        new()
        {
            ["A"] = a,
            ["B"] = b,
            ["C"] = c,
        };

    private static SeedPlan Plan(
        bool isDefaultCount,
        EngineProfile profile = EngineProfile.Regular
    ) =>
        new(
            [1],
            IsSingle: false,
            IsDefaultCount: isDefaultCount,
            profile,
            new SeedSettings("QUICKER_TEST_SEEDS", 1, 1),
            []
        );

    [Fact(DisplayName = "ケース・水準・組の観測を数え、観測しなかった組を並べる")]
    public void Counts_AndMissing()
    {
        var counter = new CoverageCounter(Table());
        counter.Observe(Row("a1", "b1", "c1"));
        counter.Observe(Row("a1", "b1", "c2"));

        var report = counter.Report();

        report.Cases.Should().Be(2);
        counter.Cases.Should().Be(2);
        report.LevelCounts[("A", "a1")].Should().Be(2);
        report.LevelCounts[("A", "a2")].Should().Be(0);
        report.PairCounts[new CoverageTuple([("A", "a1"), ("B", "b1")])].Should().Be(2);
        // A×B 4・A×C 3（a2×c2 は起こり得ない）・B×C 4
        report.PairCounts.Should().HaveCount(11);
        report
            .Missing.Select(t => t.Key)
            .Should()
            .Equal(
                "A=a1 × B=b2",
                "A=a2 × B=b1",
                "A=a2 × B=b2",
                "A=a2 × C=c1",
                "B=b2 × C=c1",
                "B=b2 × C=c2"
            );
    }

    [Fact(DisplayName = "並列に観測しても数え落とさない")]
    public void Observe_IsThreadSafe()
    {
        var counter = new CoverageCounter(Table());

        Parallel.For(0, 4000, i => counter.Observe(Row(i % 2 == 0 ? "a1" : "a2", "b1", "c1")));

        var report = counter.Report();
        report.Cases.Should().Be(4000);
        report.LevelCounts[("A", "a1")].Should().Be(2000);
        report.PairCounts[new CoverageTuple([("B", "b1"), ("C", "c1")])].Should().Be(4000);
    }

    private static Dictionary<string, string> BadAssignment(string what) =>
        what switch
        {
            "宣言していない水準" => Row("a9", "b1", "c1"),
            "足りない因子" => new() { ["A"] = "a1", ["B"] = "b1" },
            "表に無い因子" => new()
            {
                ["A"] = "a1",
                ["B"] = "b1",
                ["C"] = "c1",
                ["Z"] = "z",
            },
            "起こり得ない組み合わせ" => Row("a2", "b1", "c2"),
            _ => throw new ArgumentOutOfRangeException(nameof(what), what, null),
        };

    [Theory(
        DisplayName = "宣言と食い違う割り当ては宣言の誤りとして投げ、受け止められても Report が投げ直す"
    )]
    [InlineData("宣言していない水準")]
    [InlineData("足りない因子")]
    [InlineData("表に無い因子")]
    [InlineData("起こり得ない組み合わせ")]
    public void Observe_DeclarationErrors(string what)
    {
        var assignment = BadAssignment(what);
        var counter = new CoverageCounter(Table());
        counter.Observe(Row("a1", "b1", "c1"));

        FluentActions
            .Invoking(() => counter.Observe(assignment))
            .Should()
            .Throw<CoverageDeclarationException>(what);

        // エンジンが種の失敗として受け止めた形
        try
        {
            counter.Observe(assignment);
        }
        catch (CoverageDeclarationException) { }

        FluentActions.Invoking(counter.Report).Should().Throw<CoverageDeclarationException>(what);
    }

    [Fact(DisplayName = "固定した水準と違う水準の観測は宣言の誤りになる")]
    public void Observe_FixedLevelMismatch()
    {
        var counter = new CoverageCounter(Table(), new Dictionary<string, string> { ["B"] = "b2" });
        counter.Observe(Row("a1", "b2", "c1"));

        FluentActions
            .Invoking(() => counter.Observe(Row("a1", "b1", "c1")))
            .Should()
            .Throw<CoverageDeclarationException>()
            .WithMessage("*因子 B は b2 に固定したのに b1 を観測した*");
        FluentActions.Invoking(counter.Report).Should().Throw<CoverageDeclarationException>();
    }

    [Fact(DisplayName = "固定した因子の組は固定した水準のときだけ網羅すべき組になる")]
    public void FixedLevels_RestrictRequired()
    {
        var counter = new CoverageCounter(Table(), new Dictionary<string, string> { ["B"] = "b2" });

        var report = counter.Report();

        // A×B 2・A×C 3・B×C 2
        report.PairCounts.Should().HaveCount(7);
        report.Missing.Should().NotContain(t => t.Key.Contains("b1", StringComparison.Ordinal));
    }

    /// <summary>A=a1 × B=b1 だけを流した報告（観測しない組は 8 個）</summary>
    private static CoverageReport PartialReport()
    {
        var counter = new CoverageCounter(Table());
        counter.Observe(Row("a1", "b1", "c1"));
        return counter.Report();
    }

    private static CoverageGapEntry Gap(params (string Factor, string Level)[] items) =>
        CoverageGap.Tuple(Scope, "P1-3", "P1-4", "まだ作れない", items);

    /// <summary>報告の欠けをすべて組の項目で載せた一覧（除く組を指定できる）</summary>
    private static List<CoverageGapEntry> AllMissing(
        CoverageReport report,
        params string[] except
    ) => report.Missing.Where(t => !except.Contains(t.Key)).Select(t => Gap([.. t.Items])).ToList();

    [Fact(DisplayName = "観測しなかった組がすべて一覧に載っていれば通る")]
    public void AssertGaps_ListedMissing_IsClean()
    {
        var report = PartialReport();

        FluentActions
            .Invoking(() => report.AssertGaps(CoverageGap.Set("E", AllMissing(report)), Scope))
            .Should()
            .NotThrow();
    }

    [Fact(DisplayName = "一覧に無い欠けは新しい失敗として赤になる")]
    public void AssertGaps_UnlistedMissing_IsRed()
    {
        var report = PartialReport();
        var gaps = CoverageGap.Set("E", AllMissing(report, "A=a2 × B=b2"));

        FluentActions
            .Invoking(() => report.AssertGaps(gaps, Scope))
            .Should()
            .Throw<XunitException>()
            .WithMessage("*網羅しない組（entrance）*新しい失敗 1 件*A=a2 × B=b2*");
    }

    [Fact(DisplayName = "一覧に載っているのに網羅した組は、起きなかった項目として赤になる")]
    public void AssertGaps_ListedButCovered_IsRed()
    {
        var report = PartialReport();
        var entries = AllMissing(report);
        entries.Add(Gap(("A", "a1"), ("B", "b1")));

        FluentActions
            .Invoking(() => report.AssertGaps(CoverageGap.Set("E", entries), Scope))
            .Should()
            .Throw<XunitException>()
            .WithMessage("*起きなかった項目 1 件*A=a1 × B=b1*");
    }

    [Fact(
        DisplayName = "述語の項目は自分の因子の組の欠けだけに当たり、ほかの因子の組の欠けは赤のまま"
    )]
    public void AssertGaps_MatchingIsLimitedToItsFactorPair()
    {
        var report = PartialReport();
        // A×B の欠けをすべて述語でまとめて載せ、A×C・B×C の欠けは組で載せる
        var entries = report
            .Missing.Where(t => t.FactorKey != "A × B")
            .Select(t => Gap([.. t.Items]))
            .ToList();
        entries.Add(
            CoverageGap.Matching(
                Scope,
                "P1-3",
                "P1-4",
                "まだ作れない",
                "B",
                "A",
                (b, a) => a != "a1" || b != "b1"
            )
        );

        FluentActions
            .Invoking(() => report.AssertGaps(CoverageGap.Set("E", entries), Scope))
            .Should()
            .NotThrow();

        // B×C の欠けを 1 つ外すと、A×C のどの水準にも当たる述語があっても赤
        var withoutOne = entries
            .Where(e => e.Entry.Signature != "B=b2 × C=c1" && e.FactorKey != "A × C")
            .ToList();
        withoutOne.Add(
            CoverageGap.Matching(Scope, "P1-3", "P1-4", "まだ作れない", "A", "C", (_, _) => true)
        );

        FluentActions
            .Invoking(() => report.AssertGaps(CoverageGap.Set("E", withoutOne), Scope))
            .Should()
            .Throw<XunitException>()
            .WithMessage("*新しい失敗 1 件*B=b2 × C=c1*");
    }

    [Fact(DisplayName = "既定でない種の数で流したときは照合しない")]
    public void AssertGaps_NonDefaultSeedCount_IsNotChecked()
    {
        var report = PartialReport();
        var empty = CoverageGap.Set("E", []);

        FluentActions
            .Invoking(() => report.AssertGaps(empty, Scope, Plan(isDefaultCount: false)))
            .Should()
            .NotThrow();
        FluentActions
            .Invoking(() => report.AssertGaps(empty, Scope, Plan(isDefaultCount: true)))
            .Should()
            .Throw<XunitException>();
    }

    [Fact(
        DisplayName = "夜間の既定の種の数では、一覧に載っているのに網羅した組を赤にせず「夜間なら埋まる組」として出力へ書く"
    )]
    public void AssertGaps_NightlyListedButCovered_IsWrittenNotRed()
    {
        var report = PartialReport();
        var entries = AllMissing(report);
        entries.Add(Gap(("A", "a1"), ("B", "b1")));
        var output = new RecordingOutput();

        FluentActions
            .Invoking(() =>
                report.AssertGaps(
                    CoverageGap.Set("E", entries),
                    Scope,
                    Plan(isDefaultCount: true, EngineProfile.Nightly),
                    output
                )
            )
            .Should()
            .NotThrow();
        output.Output.Should().Contain("[E] 夜間なら埋まる組（entrance）: 1 項目");
        output.Output.Should().Contain("A=a1 × B=b1");
    }

    [Fact(DisplayName = "夜間の既定の種の数でも、一覧に無い欠けは赤になる")]
    public void AssertGaps_NightlyUnlistedMissing_IsRed()
    {
        var report = PartialReport();
        var entries = AllMissing(report, "A=a2 × B=b2");
        entries.Add(Gap(("A", "a1"), ("B", "b1")));

        FluentActions
            .Invoking(() =>
                report.AssertGaps(
                    CoverageGap.Set("E", entries),
                    Scope,
                    Plan(isDefaultCount: true, EngineProfile.Nightly),
                    new RecordingOutput()
                )
            )
            .Should()
            .Throw<XunitException>()
            .WithMessage("*新しい失敗 1 件*A=a2 × B=b2*")
            .Which.Message.Should()
            .NotContain("起きなかった項目 1 件");
    }

    [Fact(DisplayName = "夜間でも、一覧が欠けとちょうど一致すれば何も書かずに通る")]
    public void AssertGaps_NightlyExactList_WritesNothing()
    {
        var report = PartialReport();
        var output = new RecordingOutput();

        report.AssertGaps(
            CoverageGap.Set("E", AllMissing(report)),
            Scope,
            Plan(isDefaultCount: true, EngineProfile.Nightly),
            output
        );

        output.Output.Should().BeEmpty();
    }

    [Fact(DisplayName = "夜間でも、個別に指定した種の数で流したときは照合しない")]
    public void AssertGaps_NightlyNonDefaultSeedCount_IsNotChecked()
    {
        var report = PartialReport();

        FluentActions
            .Invoking(() =>
                report.AssertGaps(
                    CoverageGap.Set("E", []),
                    Scope,
                    Plan(isDefaultCount: false, EngineProfile.Nightly)
                )
            )
            .Should()
            .NotThrow();
    }

    [Fact(DisplayName = "網羅すべき組でない項目は宣言の誤りとして断る（因子の並び・水準の綴り）")]
    public void AssertGaps_UndeclaredGap_IsDeclarationError()
    {
        var report = PartialReport();
        var entries = AllMissing(report);
        entries.Add(Gap(("B", "b1"), ("A", "a2")));

        FluentActions
            .Invoking(() => report.AssertGaps(CoverageGap.Set("E", entries), Scope))
            .Should()
            .Throw<CoverageDeclarationException>()
            .WithMessage("*B=b1 × A=a2 は網羅すべき組でない*");
    }

    [Fact(DisplayName = "項目ごとに当たった欠けの数と、因子の組がまるごと欠けているかを数える")]
    public void GapUsage_CountsPerEntry()
    {
        var counter = new CoverageCounter(Table());
        counter.Observe(Row("a1", "b1", "c1"));
        counter.Observe(Row("a2", "b2", "c1"));
        counter.Observe(Row("a1", "b2", "c2"));
        counter.Observe(Row("a2", "b1", "c1"));
        var report = counter.Report();
        report.Missing.Select(t => t.Key).Should().Equal("B=b1 × C=c2");

        var bc = CoverageGap.Matching(
            Scope,
            "P1-3",
            "P1-4",
            "まだ作れない",
            "B",
            "C",
            (_, _) => true
        );
        var usage = report.GapUsage(CoverageGap.Set("E", [bc]), Scope);

        usage.Should().ContainSingle();
        usage[0].Matched.Should().Be(1);
        usage[0].RequiredInFactorGroup.Should().Be(4);
        usage[0].CoversWholeFactorGroup.Should().BeFalse();

        var none = new CoverageCounter(Table()).Report();
        var whole = none.GapUsage(CoverageGap.Set("E", [bc]), Scope).Single();
        whole.Matched.Should().Be(4);
        whole.CoversWholeFactorGroup.Should().BeTrue();
    }

    [Fact(
        DisplayName = "網羅しない組の項目ごとの当たり方を書き、因子の組を丸ごと覆う項目に印を付ける"
    )]
    public void WriteGapUsage_WritesCountsAndMarksWholeFactorGroup()
    {
        var bc = CoverageGap.Matching(
            Scope,
            "P1-3",
            "P1-4",
            "まだ作れない",
            "B",
            "C",
            (_, _) => true
        );
        var gaps = CoverageGap.Set("E", [bc]);

        var counter = new CoverageCounter(Table());
        counter.Observe(Row("a1", "b1", "c1"));
        counter.Observe(Row("a2", "b2", "c1"));
        counter.Observe(Row("a1", "b2", "c2"));
        counter.Observe(Row("a2", "b1", "c1"));
        var partial = new RecordingOutput();
        counter.Report().WriteGapUsage(gaps, Scope, partial);

        partial.Lines.Should().Contain("[E] 網羅しない組の一覧（entrance）: 1 項目");
        partial
            .Lines.Should()
            .Contain("[E]   P1-3（Pending P1-4） B × C: 当たり 1 / 4（理由：まだ作れない）");

        var whole = new RecordingOutput();
        new CoverageCounter(Table()).Report().WriteGapUsage(gaps, Scope, whole);

        whole
            .Lines.Should()
            .Contain(
                "[E]   P1-3（Pending P1-4） B × C: 当たり 4 / 4・因子の組を丸ごと覆う（理由：まだ作れない）"
            );
    }

    [Fact(DisplayName = "網羅しない組の項目は理由と直す区切りが必須")]
    public void CoverageGap_RequiresReasonAndFixedIn()
    {
        FluentActions
            .Invoking(() => CoverageGap.Tuple(Scope, "P1-3", "P1-4", " ", ("A", "a1"), ("B", "b1")))
            .Should()
            .Throw<ArgumentException>();
        FluentActions
            .Invoking(() => CoverageGap.Tuple(Scope, "P1-3", "", "理由", ("A", "a1"), ("B", "b1")))
            .Should()
            .Throw<ArgumentException>();
        FluentActions
            .Invoking(() =>
                CoverageGap.Matching(Scope, "P1-3", "P1-4", "", "A", "B", (_, _) => true)
            )
            .Should()
            .Throw<ArgumentException>();
        FluentActions
            .Invoking(() =>
                CoverageGap.Matching(Scope, "P1-3", " ", "理由", "A", "B", (_, _) => true)
            )
            .Should()
            .Throw<ArgumentException>();
    }

    [Fact(DisplayName = "受け入れた組の項目は Accepted で直す区切りを持たず、理由が必須")]
    public void AcceptedGap_IsAcceptedAndRequiresReason()
    {
        var tuple = CoverageGap.AcceptedTuple(
            Scope,
            "P1-3",
            "製品の事実",
            ("A", "a1"),
            ("B", "b1")
        );
        var matching = CoverageGap.AcceptedMatching(
            Scope,
            "P1-3",
            "製品の事実",
            "A",
            "B",
            (_, _) => true
        );

        foreach (var entry in new[] { tuple, matching })
        {
            entry.Entry.Kind.Should().Be(KnownKind.Accepted);
            entry.Entry.FixedIn.Should().BeNull();
            entry.Entry.Reason.Should().Be("製品の事実");
        }

        FluentActions
            .Invoking(() => CoverageGap.AcceptedTuple(Scope, "P1-3", " ", ("A", "a1"), ("B", "b1")))
            .Should()
            .Throw<ArgumentException>();
        FluentActions
            .Invoking(() =>
                CoverageGap.AcceptedMatching(Scope, "P1-3", "", "A", "B", (_, _) => true)
            )
            .Should()
            .Throw<ArgumentException>();
        FluentActions
            .Invoking(() =>
                CoverageGap.AcceptedMatching(Scope, "P1-3", "理由", "A", "A", (_, _) => true)
            )
            .Should()
            .Throw<ArgumentException>();
    }

    [Fact(DisplayName = "受け入れた組の項目も欠けに当たり、載っているのに網羅した組は赤のまま")]
    public void AcceptedGap_MatchesLikePending_AndListedButCoveredIsRed()
    {
        var report = PartialReport();
        // A×B の欠けは受け入れた述語で、ほかの欠けは受け入れた組でまとめて載せる
        var entries = report
            .Missing.Where(t => t.FactorKey != "A × B")
            .Select(t => CoverageGap.AcceptedTuple(Scope, "P1-3", "製品の事実", [.. t.Items]))
            .ToList();
        entries.Add(
            CoverageGap.AcceptedMatching(
                Scope,
                "P1-3",
                "製品の事実",
                "A",
                "B",
                (a, b) => a != "a1" || b != "b1"
            )
        );

        FluentActions
            .Invoking(() => report.AssertGaps(CoverageGap.Set("E", entries), Scope))
            .Should()
            .NotThrow();

        var withCovered = entries
            .Append(
                CoverageGap.AcceptedTuple(Scope, "P1-3", "製品の事実", ("A", "a1"), ("C", "c1"))
            )
            .ToList();

        FluentActions
            .Invoking(() => report.AssertGaps(CoverageGap.Set("E", withCovered), Scope))
            .Should()
            .Throw<XunitException>()
            .WithMessage("*起きなかった項目 1 件*A=a1 × C=c1*");

        var withCoveredMatching = entries
            .Append(
                CoverageGap.AcceptedMatching(
                    Scope,
                    "P1-3",
                    "製品の事実",
                    "B",
                    "C",
                    (b, c) => b == "b1" && c == "c1"
                )
            )
            .ToList();

        FluentActions
            .Invoking(() => report.AssertGaps(CoverageGap.Set("E", withCoveredMatching), Scope))
            .Should()
            .Throw<XunitException>()
            .WithMessage("*起きなかった項目 1 件*");
    }

    [Fact(
        DisplayName = "網羅しない組の当たり方は、項目ごとに Pending の区切りか Accepted かを書く"
    )]
    public void WriteGapUsage_DistinguishesPendingAndAccepted()
    {
        var gaps = CoverageGap.Set(
            "E",
            [
                CoverageGap.Matching(Scope, "P1-3", "P2", "まだ作れない", "A", "B", (_, _) => true),
                CoverageGap.AcceptedMatching(Scope, "P1-3", "製品の事実", "B", "C", (_, _) => true),
            ]
        );
        var output = new RecordingOutput();

        new CoverageCounter(Table()).Report().WriteGapUsage(gaps, Scope, output);

        output
            .Lines.Should()
            .Contain(line =>
                line.StartsWith("[E]   P1-3（Pending P2） A × B:", StringComparison.Ordinal)
            );
        output
            .Lines.Should()
            .Contain(line =>
                line.StartsWith("[E]   P1-3（Accepted） B × C:", StringComparison.Ordinal)
                && line.EndsWith("（理由：製品の事実）", StringComparison.Ordinal)
            );
    }

    [Fact(DisplayName = "要約に因子ごとの水準の数と欠けの内訳を書く")]
    public void WriteSummary_WritesCounts()
    {
        var output = new RecordingOutput();

        PartialReport().WriteSummary(output);

        var text = string.Join("\n", output.Lines);
        text.Should().Contain("[E] A: a1=1, a2=0");
        text.Should().Contain("網羅すべき組 11（2 因子 11・3 因子 0）・網羅 3・欠け 8");
        text.Should().Contain("A × B 3 件");
    }

    /// <summary>索引で数えた結果と素朴な数え方を比べる種（種の下位 4 ビットで、3 因子の集まり・固定・isValid・宣言の誤りの有無を決める）</summary>
    public static TheoryData<int> ParitySeeds() => new(Enumerable.Range(0, 48));

    [Theory(DisplayName = "索引で数えた結果が、網羅すべき組を全部なめる素朴な数え方と一致する")]
    [MemberData(nameof(ParitySeeds))]
    public void IndexedCounting_MatchesNaiveScan(int seed)
    {
        var random = new Random(seed);
        var hasGroups = (seed & 1) != 0;
        var hasFixed = (seed & 2) != 0;
        var hasRules = (seed & 4) != 0;
        var withErrors = (seed & 8) != 0;

        // 因子 4〜6・水準 2〜4（種の 3 つに 1 つは先頭の因子を 1 水準にする）
        var factorCount = random.Next(4, 7);
        var factors = Enumerable
            .Range(0, factorCount)
            .Select(f =>
            {
                var levelCount = f == 0 && seed % 3 == 0 ? 1 : random.Next(2, 5);
                return F(
                    $"F{f}",
                    Enumerable.Range(0, levelCount).Select(l => $"f{f}l{l}").ToArray()
                );
            })
            .ToList();

        var groups = new List<FactorGroup>();

        if (hasGroups)
        {
            for (var g = random.Next(1, 3); g > 0; g--)
            {
                var size = random.Next(3, Math.Min(4, factorCount) + 1);
                groups.Add(
                    new FactorGroup(
                        factors.Select(f => f.Name).OrderBy(_ => random.Next()).Take(size).ToArray()
                    )
                );
            }
        }

        // 固定は 2 水準以上の因子から選び、固定しない因子を 3 つ以上残す
        var fixedLevels = new Dictionary<string, string>(StringComparer.Ordinal);

        if (hasFixed)
        {
            var candidates = factors.Where(f => f.Levels.Count >= 2).OrderBy(_ => random.Next());

            foreach (var factor in candidates.Take(Math.Min(2, factorCount - 3)))
            {
                fixedLevels[factor.Name] = factor.Levels[random.Next(factor.Levels.Count)].Name;
            }
        }

        // isValid は固定しない 2 水準以上の因子の水準の組を禁じる（最初の規則は 2 因子＝必ず組を除く）
        var rules = new List<(string Factor, string Level)[]>();

        if (hasRules)
        {
            var candidates = factors
                .Where(f => f.Levels.Count >= 2 && !fixedLevels.ContainsKey(f.Name))
                .ToList();

            for (var r = random.Next(1, 4); r > 0; r--)
            {
                var size = rules.Count == 0 ? 2 : Math.Min(random.Next(2, 4), candidates.Count);
                rules.Add(
                    candidates
                        .OrderBy(_ => random.Next())
                        .Take(size)
                        .Select(f => (f.Name, f.Levels[random.Next(f.Levels.Count)].Name))
                        .ToArray()
                );
            }
        }

        bool IsValid(IReadOnlyDictionary<string, string> d) =>
            !rules.Any(rule =>
                rule.All(item =>
                    d.TryGetValue(item.Factor, out var level)
                    && string.Equals(level, item.Level, StringComparison.Ordinal)
                )
            );

        var table = new DimensionTable("P", factors, groups, hasRules ? IsValid : null);

        // 素朴な数え方の宣言の検査（本物の Check とは独立に書く）
        bool NaiveAccepts(IReadOnlyDictionary<string, string> assignment) =>
            factors.All(f =>
                assignment.TryGetValue(f.Name, out var level)
                && f.Levels.Any(l => string.Equals(l.Name, level, StringComparison.Ordinal))
                && (
                    !fixedLevels.TryGetValue(f.Name, out var fixedLevel)
                    || string.Equals(fixedLevel, level, StringComparison.Ordinal)
                )
            )
            && assignment.Keys.All(key => factors.Any(f => f.Name == key))
            && table.IsValid(assignment);

        Dictionary<string, string> RandomAssignment(bool corrupt)
        {
            var assignment = new Dictionary<string, string>(StringComparer.Ordinal);

            foreach (var factor in factors)
            {
                assignment[factor.Name] =
                    !corrupt && fixedLevels.TryGetValue(factor.Name, out var fixedLevel)
                        ? fixedLevel
                        : factor.Levels[random.Next(factor.Levels.Count)].Name;
            }

            if (corrupt)
            {
                switch (random.Next(4))
                {
                    case 0:
                        assignment.Remove(factors[random.Next(factorCount)].Name);
                        break;
                    case 1:
                        assignment[factors[random.Next(factorCount)].Name] = "undeclared";
                        break;
                    case 2:
                        assignment["Extra"] = "x";
                        break;
                }
            }

            return assignment;
        }

        var observations = Enumerable
            .Range(0, random.Next(40, 151))
            .Select(_ => RandomAssignment(withErrors && random.Next(3) == 0))
            .ToList();
        var accepted = observations.Where(NaiveAccepts).ToList();

        // 宣言の誤りを含む列：観測ごとに投げるかが一致し、Report も投げ直す
        if (withErrors)
        {
            accepted.Count.Should().BeLessThan(observations.Count, "宣言の誤りを含む列を作る");

            var withRejects = new CoverageCounter(table, fixedLevels);

            foreach (var assignment in observations)
            {
                var observe = () => withRejects.Observe(assignment);

                if (NaiveAccepts(assignment))
                {
                    observe.Should().NotThrow();
                }
                else
                {
                    observe.Should().Throw<CoverageDeclarationException>();
                }
            }

            var rethrow = () => withRejects.Report();
            rethrow.Should().Throw<CoverageDeclarationException>();
        }

        accepted.Should().NotBeEmpty("宣言どおりの割り当てを数える");

        // 素朴な数え方：網羅すべき組を全部なめ、割り当てに含まれる組を数える
        var naivePairs = table.RequiredPairs(fixedLevels);
        var naiveTriples = table.RequiredTriples(fixedLevels);
        var naiveRequired = naivePairs.Concat(naiveTriples).Distinct().ToList();
        var naiveCounts = naiveRequired.ToDictionary(
            tuple => tuple,
            tuple => accepted.Count(tuple.IsContainedIn)
        );

        var counter = new CoverageCounter(table, fixedLevels);
        accepted.ForEach(counter.Observe);
        var report = counter.Report();

        report.Cases.Should().Be(accepted.Count);
        report
            .PairCounts.Select(pair => (pair.Key.Key, pair.Value))
            .Should()
            .Equal(naivePairs.Select(tuple => (tuple.Key, naiveCounts[tuple])));
        report
            .TripleCounts.Select(pair => (pair.Key.Key, pair.Value))
            .Should()
            .Equal(naiveTriples.Select(tuple => (tuple.Key, naiveCounts[tuple])));
        report
            .Missing.Select(tuple => tuple.Key)
            .Should()
            .Equal(naiveRequired.Where(tuple => naiveCounts[tuple] == 0).Select(t => t.Key));
        report
            .LevelCounts.Select(pair => (pair.Key.Factor, pair.Key.Level, pair.Value))
            .Should()
            .Equal(
                factors.SelectMany(f =>
                    f.Levels.Select(l => (f.Name, l.Name, accepted.Count(a => a[f.Name] == l.Name)))
                )
            );

        // 種が決めた観点を実際に通ったか（黙って空振りしない）
        if (hasGroups)
        {
            naiveTriples.Should().NotBeEmpty();
            report.TripleCounts.Values.Should().Contain(count => count > 0);
        }

        if (hasFixed)
        {
            fixedLevels.Should().NotBeEmpty();
            naivePairs.Count.Should().BeLessThan(table.RequiredPairs().Count, "固定が組を減らす");
        }

        if (hasRules)
        {
            var unruled = new DimensionTable("P", factors, groups);
            naivePairs
                .Count.Should()
                .BeLessThan(unruled.RequiredPairs(fixedLevels).Count, "isValid が組を除く");
        }
    }

    private sealed class RecordingOutput : ITestOutputHelper
    {
        public List<string> Lines { get; } = [];

        public string Output => string.Join("\n", Lines);

        public void Write(string message) => Lines.Add(message);

        public void Write(string format, params object[] args) =>
            Lines.Add(
                string.Format(System.Globalization.CultureInfo.InvariantCulture, format, args)
            );

        public void WriteLine(string message) => Lines.Add(message);

        public void WriteLine(string format, params object[] args) =>
            Lines.Add(
                string.Format(System.Globalization.CultureInfo.InvariantCulture, format, args)
            );
    }
}
