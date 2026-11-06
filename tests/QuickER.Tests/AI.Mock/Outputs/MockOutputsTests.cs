using QuickER.Tests.TestSupport.Engines;

namespace QuickER.Tests.AI.Mock.Outputs;

/// <summary>モックフォルダの出力の表を流す</summary>
/// <remarks>
/// 各行は入力を当てはまる対象へ流して表明する。単一 HTML と設計書を出す行には、文脈（<see cref="MockOutputsContext"/>）が
/// 決定的であること・外部参照・改行の照合を掛ける。
/// </remarks>
public sealed class MockOutputsTests
{
    /// <summary>行の Id</summary>
    public static TheoryData<string> Ids() => MockOutputsRows.Table.Ids();

    [Theory(DisplayName = "出力の行：入力を当てはまる対象へ流すと行の期待どおり")]
    [MemberData(nameof(Ids))]
    public void Row_Holds(string id)
    {
        var scenario = MockOutputsRows.Table[id].Input;
        using var context = new MockOutputsContext();

        scenario.Body(context);
    }

    [Fact(DisplayName = "網羅：対象 × 観点（理由つきで外した組だけが欠ける）")]
    public void Coverage_MatchesGaps()
    {
        var counter = new CoverageCounter(MockOutputsDimensions.Table);

        foreach (var row in MockOutputsRows.Table)
        {
            MockOutputsDimensions.Observe(counter, row.Input);
        }

        counter.Report().AssertGaps(MockOutputsDimensions.Gaps, MockOutputsDimensions.Scope);
    }
}
