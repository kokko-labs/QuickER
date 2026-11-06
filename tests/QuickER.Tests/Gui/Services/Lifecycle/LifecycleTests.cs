using QuickER.Tests.TestSupport.Engines;

namespace QuickER.Tests.Gui.Services.Lifecycle;

/// <summary>アプリの一生の表（<see cref="LifecycleRows"/>）を流す</summary>
public sealed class LifecycleTests
{
    /// <summary>行の Id</summary>
    public static TheoryData<string> RowIds => LifecycleRows.Table.Ids();

    /// <summary>行を流す（入口が例外を外へ出さないことの照合と、元のテストの表明）</summary>
    [Theory(DisplayName = "アプリの一生: 表の行")]
    [MemberData(nameof(RowIds))]
    public async Task Row_Holds(string id) =>
        await LifecycleRunner.RunAsync(LifecycleRows.Table[id].Input);

    /// <summary>全行を流して網羅を数え、網羅しない組の一覧と照合する</summary>
    [Fact(DisplayName = "アプリの一生: 表の網羅は一覧と一致する")]
    public async Task Coverage_MatchesGaps()
    {
        var counter = new CoverageCounter(LifecycleDimensions.Table);

        foreach (var row in LifecycleRows.Table)
        {
            if (await LifecycleRunner.RunAsync(row.Input))
            {
                LifecycleDimensions.Observe(counter, row.Input);
            }
        }

        var report = counter.Report();
        report.WriteSummary();
        report.AssertGaps(LifecycleDimensions.Gaps, LifecycleDimensions.Scope);
    }
}
