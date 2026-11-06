using QuickER.Tests.TestSupport.Engines;

namespace QuickER.Tests.Gui.Services.Lifecycle;

/// <summary>終了の最後の処理の総当たり（<see cref="PostExitExhaustiveRows"/>）を流す</summary>
public sealed class PostExitExhaustiveTests
{
    /// <summary>行の Id</summary>
    public static TheoryData<string> RowIds => PostExitExhaustiveRows.Table.Ids();

    /// <summary>1 組を流す</summary>
    [Theory(DisplayName = "終了の最後の処理の総当たり: 組")]
    [MemberData(nameof(RowIds))]
    public void Row_Holds(string id) =>
        PostExitExhaustiveRows.Run(PostExitExhaustiveRows.Table[id].Input);

    /// <summary>全組を流して網羅を数え、一覧と照合する</summary>
    [Fact(DisplayName = "終了の最後の処理の総当たり: 網羅は一覧と一致する")]
    public void Coverage_MatchesGaps()
    {
        var counter = new CoverageCounter(PostExitExhaustiveDimensions.Table);

        foreach (var row in PostExitExhaustiveRows.Table)
        {
            if (PostExitExhaustiveRows.Run(row.Input))
            {
                PostExitExhaustiveDimensions.Observe(counter, row.Input);
            }
        }

        var report = counter.Report();
        report.WriteSummary();
        report.AssertGaps(PostExitExhaustiveDimensions.Gaps, PostExitExhaustiveDimensions.Scope);
    }
}
