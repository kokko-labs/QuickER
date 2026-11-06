using QuickER.Tests.TestSupport.Engines;

namespace QuickER.Tests.AI.Mock.Folder;

/// <summary>モックフォルダの保存単位の表を流す</summary>
/// <remarks>
/// 各行の本体を組（<see cref="MockFolderHarness"/>）の手で流したあと、組がどの手の後にも見張った不変条件をまとめて表明する。
/// </remarks>
public sealed class MockFolderTests
{
    /// <summary>行の Id</summary>
    public static TheoryData<string> Ids() => MockFolderRows.Table.Ids();

    [Theory(DisplayName = "保存単位の行：手の列が行の期待どおりで、どの手の後も不変条件が成り立つ")]
    [MemberData(nameof(Ids))]
    public async Task Row_Holds(string id)
    {
        var scenario = MockFolderRows.Table[id].Input;
        using var harness = new MockFolderHarness();

        await scenario.Body(harness);

        harness.AssertInvariants();
    }

    [Fact(DisplayName = "網羅：主な手の入口 × 結果（理由つきで外した組だけが欠ける）")]
    public void Coverage_MatchesGaps()
    {
        var counter = new CoverageCounter(MockFolderDimensions.Table);

        foreach (var row in MockFolderRows.Table)
        {
            MockFolderDimensions.Observe(counter, row.Input);
        }

        counter.Report().AssertGaps(MockFolderDimensions.Gaps, MockFolderDimensions.Scope);
    }
}
