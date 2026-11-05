using QuickER.Tests.TestSupport;
using QuickER.Tests.TestSupport.Engines;

namespace QuickER.Tests.Mcp.Tools;

/// <summary>ER 図のツールの行（<see cref="ToolScenarioRows"/>）を両ホストで流す</summary>
public sealed class ToolScenarioTests
{
    /// <summary>行の Id × ホスト（<see cref="ToolHostKind"/> の全値）</summary>
    public static TheoryData<string, ToolHostKind> Cases
    {
        get
        {
            var data = new TheoryData<string, ToolHostKind>();

            foreach (var row in ToolScenarioRows.Table)
            {
                foreach (var host in Enum.GetValues<ToolHostKind>())
                {
                    data.Add(row.Id, host);
                }
            }

            return data;
        }
    }

    [Theory(DisplayName = "ER 図のツールの行は、両ホストで期待どおりになる")]
    [MemberData(nameof(Cases))]
    public void Row_Matches(string id, ToolHostKind host) =>
        ToolScenarioRunner.Run(ToolScenarioRows.Table[id], host);

    /// <summary>名前付きクエリの共有コアの期待（<see cref="ToolExpect.Core"/>）を持つ行の Id</summary>
    public static TheoryData<string> CoreCases =>
        new(
            ToolScenarioRows
                .Table.Where(row => row.Input.Calls.Any(call => call.CoreChecks.Count > 0))
                .Select(row => row.Id)
        );

    [Theory(DisplayName = "名前付きクエリの行は、共有コアの構造の結果が期待どおりになる")]
    [MemberData(nameof(CoreCases))]
    public void Row_MatchesCore(string id) => QueryToolScenarioCore.Run(ToolScenarioRows.Table[id]);

    [Fact(DisplayName = "ER 図のツールの行は、ツール × ホスト × 結果の組を宣言どおりに網羅する")]
    public void Rows_CoverToolDimensions()
    {
        var counter = new CoverageCounter(ToolScenarioDimensions.Table);
        ToolScenarioDimensions.ObserveDeclarations(counter);

        var report = counter.Report();
        report.WriteSummary();
        report.WriteGapUsage(ToolScenarioDimensions.Gaps, ToolScenarioDimensions.Scope);
        report.AssertGaps(ToolScenarioDimensions.Gaps, ToolScenarioDimensions.Scope);
    }
}
