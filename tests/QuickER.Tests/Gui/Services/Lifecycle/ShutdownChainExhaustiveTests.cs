using QuickER.Tests.TestSupport.Engines;

namespace QuickER.Tests.Gui.Services.Lifecycle;

/// <summary>終了の連鎖の総当たり（<see cref="ShutdownChainExhaustiveRows"/>）を流す</summary>
public sealed class ShutdownChainExhaustiveTests
{
    /// <summary>行の Id</summary>
    public static TheoryData<string> RowIds => ShutdownChainExhaustiveRows.Table.Ids();

    /// <summary>1 組を流す</summary>
    [Theory(DisplayName = "終了の連鎖の総当たり: 組")]
    [MemberData(nameof(RowIds))]
    public void Row_Holds(string id) =>
        ShutdownChainExhaustiveRows.Run(ShutdownChainExhaustiveRows.Table[id].Input);

    /// <summary>全組を流して網羅を数え、一覧と照合する</summary>
    [Fact(DisplayName = "終了の連鎖の総当たり: 網羅は一覧と一致する")]
    public void Coverage_MatchesGaps()
    {
        var counter = new CoverageCounter(ShutdownChainExhaustiveDimensions.Table);

        foreach (var row in ShutdownChainExhaustiveRows.Table)
        {
            if (ShutdownChainExhaustiveRows.Run(row.Input))
            {
                ShutdownChainExhaustiveDimensions.Observe(counter, row.Input);
            }
        }

        var report = counter.Report();
        report.WriteSummary();
        report.AssertGaps(
            ShutdownChainExhaustiveDimensions.Gaps,
            ShutdownChainExhaustiveDimensions.Scope
        );
    }
}
