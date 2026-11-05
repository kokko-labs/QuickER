using AwesomeAssertions;
using QuickER.Tests.TestSupport.Engines;

namespace QuickER.Tests.Provider;

/// <summary>方言の DDL の表（<see cref="DialectDdlRows"/>）を流すテスト</summary>
public sealed class DialectDdlTableTests(ITestOutputHelper output)
{
    /// <summary>(行の Id, 方言) の組</summary>
    public static TheoryData<string, string> Cases => DialectDdlRows.Cases();

    [Theory(
        DisplayName = "[方言の DDL] 行の期待どおりの DDL・同期スクリプトになる（カルチャ・英語・扱いの表の自動の照合つき）"
    )]
    [MemberData(nameof(Cases))]
    public void Row_MatchesExpectation(string id, string dialect)
    {
        var row = DialectDdlRows.Table[id];
        var problems = DialectDdlRunner.Check(row.Input, dialect);

        problems
            .Should()
            .BeEmpty(
                $"行 {id}（{row.Description}）の {dialect}：\n  " + string.Join("\n  ", problems)
            );
    }

    [Fact(
        DisplayName = "[方言の DDL] 網羅しない組は一覧のとおり（方言・要素・観点・経路の 2 つ組）"
    )]
    public void Coverage_MatchesDeclaredGaps()
    {
        var counter = new CoverageCounter(DialectDdlDimensions.Table);

        foreach (var row in DialectDdlRows.Table)
        {
            foreach (var dialect in row.Input.Expected.Keys)
            {
                DialectDdlDimensions.Observe(counter, row.Input, dialect);
            }
        }

        var report = counter.Report();
        report.WriteSummary(output);
        report.AssertGaps(DialectDdlDimensions.Gaps, DialectDdlDimensions.Scope, output: output);
    }
}
