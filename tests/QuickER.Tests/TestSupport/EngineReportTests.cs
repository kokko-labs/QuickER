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
