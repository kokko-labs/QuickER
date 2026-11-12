using AwesomeAssertions;
using QuickER.Tests.TestSupport.Engines;
using Xunit.Sdk;

namespace QuickER.Tests.TestSupport;

/// <summary>
/// テストのエンジンの報告（<see cref="EngineReport"/>）の打ち切りと再現の手順を固定する
/// </summary>
public sealed class EngineReportTests
{
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

    private static EngineFailure Failure(int i, string scope = "sqlite") =>
        new(scope, $"sig-{i}", $"detail-{i}", Seed: i);

    [Fact(
        DisplayName = "先頭の件数だけを再現の手順つきで詳しく書き、残りは範囲ごとの件数にまとめる"
    )]
    public void Render_TruncatesAfterMaxDetailed()
    {
        var report = new EngineReport("E", maxDetailed: 3);

        for (var i = 1; i <= 5; i++)
        {
            report.Add(Failure(i, i <= 4 ? "sqlite" : "mysql"), $"`X_SEED={i}` で 1 種だけ流す");
        }

        var text = report.Render();

        text.Should()
            .Contain("sqlite / 種 1 | sig-1")
            .And.Contain("再現: `X_SEED=1` で 1 種だけ流す");
        text.Should().Contain("sig-3").And.NotContain("sig-4").And.NotContain("detail-5");
        text.Should().Contain("ほか 2 件（mysql 1 件・sqlite 1 件）");
        text.Should().Contain("    mysql の種：5\n    sqlite の種：4");
    }

    /// <summary>詳しく書かなかった失敗の種の番号の行（範囲ごと・小さい順・重ねない・上限を超えた分は数だけ・種の無い失敗は書かない）</summary>
    public static TheoryData<string, int[], string[], string[]> TruncatedSeedRows() =>
        new()
        {
            // 範囲ごとに小さい順で並べ、同じ種の 2 つ目の失敗は重ねない
            {
                "seeds-sorted-and-distinct-per-scope",
                [900, 7, 120001, 7, 300],
                [
                    "  ほか 6 件（random 5 件・scenario-a 1 件）",
                    "    random の種：7・300・900・120001",
                ],
                ["種：7・7"]
            },
            // 上限ちょうどは全部並べ、数の付け足しを書かない
            {
                "seeds-at-cap-are-all-listed",
                [.. Enumerable.Range(1, EngineReport.MaxListedSeeds)],
                [$"    random の種：1・2・3・"],
                ["ほか 0 種", $"{EngineReport.MaxListedSeeds} ほか"]
            },
            // 上限を超えた分は数だけ
            {
                "seeds-over-cap-show-count",
                [.. Enumerable.Range(1, EngineReport.MaxListedSeeds + 3)],
                [$"・{EngineReport.MaxListedSeeds} ほか 3 種"],
                [$"・{EngineReport.MaxListedSeeds + 1}"]
            },
        };

    [Theory(
        DisplayName = "打ち切った失敗の種の番号を、範囲ごとに上限まで並べ、超えた分は数だけにする"
    )]
    [MemberData(nameof(TruncatedSeedRows))]
    public void Render_ListsSeedsOfTruncatedFailures(
        string id,
        int[] seeds,
        string[] contains,
        string[] absent
    )
    {
        // 先頭の 1 件だけを詳しく書く（種 0 の失敗）。並べるのは打ち切った残りの種
        var report = new EngineReport("E", maxDetailed: 1);
        report.Add(Failure(0, "random"), "`X_SEED=0`");

        foreach (var seed in seeds)
        {
            report.Add(Failure(seed, "random"), "`X_SEED=n`");
        }

        // 種の無い失敗（シナリオ）は件数だけで、種の行を作らない
        report.Add(new EngineFailure("scenario-a", "s", "d"), "シナリオ");

        var text = report.Render();

        foreach (var expected in contains)
        {
            text.Should().Contain(expected, id);
        }

        foreach (var unexpected in absent)
        {
            text.Should().NotContain(unexpected, id);
        }

        text.Should().NotContain("scenario-a の種", id);
        text.Should().NotContain("種：0", id);
    }

    [Fact(DisplayName = "件数が打ち切り以下なら、まとめの行を書かない")]
    public void Render_NoSummaryLineWhenWithinLimit()
    {
        var report = new EngineReport("E");
        report.Add(
            new EngineFailure("24-unique", "s", "line1\nline2"),
            EngineReport.ScenarioReproduction("24-unique")
        );

        var text = report.Render();

        text.Should().NotContain("ほか");
        text.Should().Contain("24-unique | s").And.Contain("    line1\n    line2");
        text.Should().Contain("再現: シナリオ `24-unique` を流す");
    }

    [Fact(DisplayName = "再現の手順の無い失敗は加えられない")]
    public void Add_RequiresReproduction()
    {
        var report = new EngineReport("E");

        FluentActions
            .Invoking(() => report.Add(Failure(1), " "))
            .Should()
            .Throw<ArgumentException>();
        FluentActions
            .Invoking(() => new EngineReport("E", 0))
            .Should()
            .Throw<ArgumentOutOfRangeException>();
    }

    [Fact(DisplayName = "照合の失敗に報告を渡すと、新しい失敗を打ち切りと再現の手順つきで書く")]
    public void AssertClean_UsesReportForUnknown()
    {
        var report = new EngineReport("E", maxDetailed: 1);
        report.Add(Failure(1), "`X_SEED=1` で 1 種だけ流す");
        report.Add(Failure(2), "`X_SEED=2` で 1 種だけ流す");
        var plan = SeedSource.Resolve(new SeedSettings("X_SEEDS", 2, 2), _ => null);
        var known = new KnownFailureSet("E", []).Classify(report.Failures, plan);

        FluentActions
            .Invoking(() => known.AssertClean("E", report))
            .Should()
            .Throw<XunitException>()
            .WithMessage("*再現: `X_SEED=1`*ほか 1 件（sqlite 1 件）*");
    }

    [Fact(DisplayName = "要約は失敗の件数と既知の失敗の照合の件数を出力へ書く")]
    public void WriteSummary_WritesCounts()
    {
        var report = new EngineReport("E");
        var failure = new EngineFailure("s", "sig", "d");
        report.Add(failure, EngineReport.ScenarioReproduction("s"));
        var entry = new KnownEntry("F-1", KnownKind.Pending, "P1-2", null, "s", KnownMatch.KeyOnly);
        var plan = SeedSource.Resolve(new SeedSettings("X_SEEDS", 1, 1), _ => null);
        var known = new KnownFailureSet("E", [entry]).Classify(report.Failures, plan);
        var output = new RecordingOutput();

        report.WriteSummary(output, known);

        output.Output.Should().Contain("[E] 失敗 1 件");
        output
            .Output.Should()
            .Contain("[E] 既知 1 件・新しい失敗 0 件・起きなかった項目 0 件・理由の食い違い 0 件");
        output.Output.Should().Contain("[known] F-1: s | sig");
    }
}
