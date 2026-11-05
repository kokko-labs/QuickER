using AwesomeAssertions;
using QuickER.Tests.TestSupport.Engines;

namespace QuickER.Tests.Provider;

/// <summary>取込警告の表（<see cref="DialectImportWarningRows"/>）を流すテスト</summary>
public sealed class DialectImportWarningTableTests(ITestOutputHelper output)
{
    /// <summary>行の Id</summary>
    public static TheoryData<string> Ids => DialectImportWarningRows.Table.Ids();

    [Theory(
        DisplayName = "[取込警告] すべての種別の文と見出しを英日で整形できる（埋め込み漏れ・訳漏れが無い）"
    )]
    [MemberData(nameof(Ids))]
    public void Row_MatchesExpectation(string id)
    {
        var row = DialectImportWarningRows.Table[id];
        var problems = DialectImportWarningRows.Check(row.Input);

        problems
            .Should()
            .BeEmpty($"行 {id}（{row.Description}）：\n  " + string.Join("\n  ", problems));
    }

    [Fact(DisplayName = "[取込警告] 網羅しない組は一覧のとおり（種別 × 文の種類 × 言語）")]
    public void Coverage_MatchesDeclaredGaps()
    {
        var counter = new CoverageCounter(DialectImportWarningDimensions.Table);

        foreach (var row in DialectImportWarningRows.Table)
        {
            DialectImportWarningDimensions.Observe(counter, row.Input);
        }

        var report = counter.Report();
        report.WriteSummary(output);
        report.AssertGaps(
            DialectImportWarningDimensions.Gaps,
            DialectImportWarningDimensions.Scope,
            output: output
        );
    }
}
