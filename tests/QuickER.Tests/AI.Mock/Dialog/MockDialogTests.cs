using QuickER.Tests.TestSupport.Engines;

namespace QuickER.Tests.AI.Mock.Dialog;

/// <summary>
/// モック生成のダイアログの操作の列の表（状態 × 手の列 → 可否・メッセージ・画面一覧・出力・生成・破棄）と、
/// どの手の後にも掛ける不変条件
/// </summary>
public sealed class MockDialogTests
{
    /// <summary>行の Id</summary>
    public static TheoryData<string> Ids() => MockDialogRows.Table.Ids();

    [Theory(
        DisplayName = "モック生成のダイアログの表：手の列の後の状態が期待どおりで、不変条件を破らない"
    )]
    [MemberData(nameof(Ids))]
    public async Task Row_Holds(string id)
    {
        var row = MockDialogRows.Table[id];
        using var harness = new MockDialogHarness();

        await row.Input.Body(harness);
        await harness.FinishAsync();
    }

    [Fact(DisplayName = "網羅：ダイアログの状態 × 手の種類")]
    public void Coverage_MatchesGaps()
    {
        var counter = new CoverageCounter(MockDialogDimensions.Table);

        foreach (var row in MockDialogRows.Table)
        {
            foreach (var (state, op) in row.Input.Touches)
            {
                MockDialogDimensions.Observe(counter, state, op);
            }
        }

        counter.Report().AssertGaps(MockDialogDimensions.Gaps, MockDialogDimensions.Scope);
    }
}
