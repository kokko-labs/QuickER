using QuickER.Tests.TestSupport.Engines;

namespace QuickER.Tests.AI.Chat;

/// <summary>
/// チャットのダイアログの操作の列の表（初期状態 × 手の列 → 可否・メッセージ・添付・履歴・状態の文・破棄）と、
/// どの手の後にも掛ける不変条件
/// </summary>
public sealed class ChatDialogTests
{
    /// <summary>行の Id</summary>
    public static TheoryData<string> Ids() => ChatDialogRows.Table.Ids();

    [Theory(
        DisplayName = "チャットのダイアログの表：手の列の後の状態が期待どおりで、不変条件を破らない"
    )]
    [MemberData(nameof(Ids))]
    public async Task Row_Holds(string id)
    {
        var row = ChatDialogRows.Table[id];
        using var harness = new ChatDialogHarness();

        await row.Input.Body(harness);
        harness.Finish();
    }

    [Fact(DisplayName = "網羅：ダイアログの状態 × 手の種類")]
    public void Coverage_MatchesGaps()
    {
        var counter = new CoverageCounter(ChatDialogDimensions.Table);

        foreach (var row in ChatDialogRows.Table)
        {
            foreach (var (state, op) in row.Input.Touches)
            {
                ChatDialogDimensions.Observe(counter, state, op);
            }
        }

        counter.Report().AssertGaps(ChatDialogDimensions.Gaps, ChatDialogDimensions.Scope);
    }
}
