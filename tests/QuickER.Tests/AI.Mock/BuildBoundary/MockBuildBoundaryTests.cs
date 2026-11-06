using QuickER.Tests.TestSupport.Engines;

namespace QuickER.Tests.AI.Mock.BuildBoundary;

/// <summary>モックプロジェクトの最終ビルドの境界の表を流す</summary>
/// <remarks>
/// ランナー経由の行には、全行に掛かる照合（<see cref="MockBuildBoundaryContext.RunAsync"/>）が掛かる。
/// 行が作った ACL・ハンドル・reparse point・リンクは、行が失敗しても文脈の破棄で戻してから消す。
/// </remarks>
public sealed class MockBuildBoundaryTests
{
    /// <summary>行の Id</summary>
    public static TheoryData<string> Ids() => MockBuildBoundaryRows.Table.Ids();

    [Theory(
        DisplayName = "境界の行：最終ビルドの前の検知・削除・探索・上書きの走査が行の宣言どおり"
    )]
    [MemberData(nameof(Ids))]
    public async Task Row_Holds(string id)
    {
        var scenario = MockBuildBoundaryRows.Table[id].Input;
        using var context = new MockBuildBoundaryContext(TestContext.Current.CancellationToken);

        await scenario.Body(context);
    }

    [Fact(DisplayName = "網羅：入口 × 種別（理由つきで外した組だけが欠ける）")]
    public void Coverage_MatchesGaps()
    {
        var counter = new CoverageCounter(MockBuildBoundaryDimensions.Table);

        foreach (var row in MockBuildBoundaryRows.Table)
        {
            MockBuildBoundaryDimensions.Observe(counter, row.Input);
        }

        counter
            .Report()
            .AssertGaps(MockBuildBoundaryDimensions.Gaps, MockBuildBoundaryDimensions.Scope);
    }
}
