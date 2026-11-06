using QuickER.Tests.TestSupport.Engines;

namespace QuickER.Tests.Platform.OsBoundary;

/// <summary>シェル許可の表（<see cref="ShellPermissionRows"/>）を流す</summary>
public sealed class ShellPermissionTests
{
    /// <summary>行の Id</summary>
    public static TheoryData<string> RowIds => ShellPermissionRows.Table.Ids();

    /// <summary>行を流す（元のテストの表明）</summary>
    [Theory(DisplayName = "OS との境界: シェル許可の表の行")]
    [MemberData(nameof(RowIds))]
    public void Row_Holds(string id) =>
        ShellPermissionRunner.Run(ShellPermissionRows.Table[id].Input);

    /// <summary>全行の要求から水準を計算して網羅を数え、網羅しない組の一覧と照合する</summary>
    [Fact(DisplayName = "OS との境界: シェル許可の表の網羅は一覧と一致する")]
    public void Coverage_MatchesGaps()
    {
        var counter = new CoverageCounter(ShellPermissionDimensions.Table);

        foreach (var row in ShellPermissionRows.Table)
        {
            ShellPermissionRunner.Run(row.Input);
            ShellPermissionDimensions.Observe(counter, row.Input);
        }

        var report = counter.Report();
        report.WriteSummary();
        report.AssertGaps(ShellPermissionDimensions.Gaps, ShellPermissionDimensions.Scope);
    }
}
