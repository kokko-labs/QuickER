using QuickER.Tests.TestSupport.Engines;

namespace QuickER.Tests.Platform.OsBoundary;

/// <summary>起動の表（<see cref="ProcessLaunchRows"/>）を流す</summary>
public sealed class ProcessLaunchTests
{
    /// <summary>行の Id</summary>
    public static TheoryData<string> RowIds => ProcessLaunchRows.Table.Ids();

    /// <summary>行を流す（結果の種類の照合と、元のテストの表明）</summary>
    [Theory(DisplayName = "OS との境界: 起動の表の行")]
    [MemberData(nameof(RowIds))]
    public void Row_Holds(string id) => ProcessLaunchRunner.Run(ProcessLaunchRows.Table[id].Input);

    /// <summary>全行を流して網羅を数え、網羅しない組の一覧と照合する</summary>
    [Fact(DisplayName = "OS との境界: 起動の表の網羅は一覧と一致する")]
    public void Coverage_MatchesGaps()
    {
        var counter = new CoverageCounter(ProcessLaunchDimensions.Table);

        foreach (var row in ProcessLaunchRows.Table)
        {
            if (ProcessLaunchRunner.Run(row.Input))
            {
                ProcessLaunchDimensions.Observe(counter, row.Input);
            }
        }

        var report = counter.Report();
        report.WriteSummary();
        report.AssertGaps(ProcessLaunchDimensions.Gaps, ProcessLaunchDimensions.Scope);
    }
}
