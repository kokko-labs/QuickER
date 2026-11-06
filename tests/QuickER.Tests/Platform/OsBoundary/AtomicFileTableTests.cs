using QuickER.Tests.TestSupport.Engines;

namespace QuickER.Tests.Platform.OsBoundary;

/// <summary>原子的な書き込みの表（<see cref="AtomicFileRows"/>）を流す</summary>
public sealed class AtomicFileTableTests
{
    /// <summary>行の Id</summary>
    public static TheoryData<string> RowIds => AtomicFileRows.Table.Ids();

    /// <summary>行を流す（結果の種類の照合と、元のテストの表明と、一時ファイルの残りの照合）</summary>
    [Theory(DisplayName = "OS との境界: 原子的な書き込みの表の行")]
    [MemberData(nameof(RowIds))]
    public void Row_Holds(string id) => AtomicFileRunner.Run(AtomicFileRows.Table[id].Input);

    /// <summary>全行を流して網羅を数え、網羅しない組の一覧と照合する</summary>
    [Fact(DisplayName = "OS との境界: 原子的な書き込みの表の網羅は一覧と一致する")]
    public void Coverage_MatchesGaps()
    {
        var counter = new CoverageCounter(AtomicFileDimensions.Table);

        foreach (var row in AtomicFileRows.Table)
        {
            AtomicFileRunner.Run(row.Input);
            AtomicFileDimensions.Observe(counter, row.Input);
        }

        var report = counter.Report();
        report.WriteSummary();
        report.AssertGaps(AtomicFileDimensions.Gaps, AtomicFileDimensions.Scope);
    }
}
