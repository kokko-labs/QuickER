using QuickER.Tests.TestSupport.Engines;

namespace QuickER.Tests.Platform.OsBoundary;

/// <summary>パスの入口の表（<see cref="PathEntranceRows"/>）を流す</summary>
public sealed class PathEntranceTests
{
    /// <summary>行の Id</summary>
    public static TheoryData<string> RowIds => PathEntranceRows.Table.Ids();

    /// <summary>行を流す（結果の種類の照合と、元のテストの表明）</summary>
    [Theory(DisplayName = "OS との境界: パスの入口の表の行")]
    [MemberData(nameof(RowIds))]
    public void Row_Holds(string id) => PathEntranceRunner.Run(PathEntranceRows.Table[id].Input);

    /// <summary>全行を流して網羅を数え、網羅しない組の一覧と照合する</summary>
    [Fact(DisplayName = "OS との境界: パスの入口の表の網羅は一覧と一致する")]
    public void Coverage_MatchesGaps()
    {
        var counter = new CoverageCounter(PathEntranceDimensions.Table);

        foreach (var row in PathEntranceRows.Table)
        {
            PathEntranceRunner.Run(row.Input);
            PathEntranceDimensions.Observe(counter, row.Input);
        }

        var report = counter.Report();
        report.WriteSummary();
        report.AssertGaps(PathEntranceDimensions.Gaps, PathEntranceDimensions.Scope);
    }
}
