using AwesomeAssertions;
using QuickER.Tests.TestSupport.Engines;

namespace QuickER.Tests.Provider;

/// <summary>方言の識別子の表（<see cref="DialectIdentifierRows"/>）を流すテスト</summary>
public sealed class DialectIdentifierTableTests(ITestOutputHelper output)
{
    /// <summary>(行の Id, 方言) の組</summary>
    public static TheoryData<string, string> Cases => DialectIdentifierRows.Cases();

    [Theory(DisplayName = "[方言の識別子] 行の期待どおりにクォート・分割・エスケープされる")]
    [MemberData(nameof(Cases))]
    public void Row_MatchesExpectation(string id, string dialect)
    {
        var row = DialectIdentifierRows.Table[id];
        var input = row.Input.Expected[dialect].Input;
        var problems = DialectIdentifierRunner.Check(row.Input, dialect);

        problems
            .Should()
            .BeEmpty(
                $"行 {id}（{row.Description}）の {dialect} で 入力 {DialectIdentifierRow.Show(input)}：\n  "
                    + string.Join("\n  ", problems)
            );
    }

    [Fact(
        DisplayName = "[方言の識別子] 網羅しない組は一覧のとおり（方言 × 関数・関数 × 入力の形）"
    )]
    public void Coverage_MatchesDeclaredGaps()
    {
        var counter = new CoverageCounter(DialectIdentifierDimensions.Table);

        foreach (var row in DialectIdentifierRows.Table)
        {
            foreach (var dialect in row.Input.Expected.Keys)
            {
                DialectIdentifierDimensions.Observe(counter, row.Input, dialect);
            }
        }

        var report = counter.Report();
        report.WriteSummary(output);
        report.AssertGaps(
            DialectIdentifierDimensions.Gaps,
            DialectIdentifierDimensions.Scope,
            output: output
        );
    }
}
