using QuickER.Tests.TestSupport.Engines;

namespace QuickER.Tests.Db.UI.ConnectionDialog;

/// <summary>
/// 接続ダイアログの操作の列の表（ストアの状態 × 手の列 → 入力欄・一覧・確認・確定・ストアの中身）と、
/// どの手の後にも掛ける不変条件
/// </summary>
public sealed class ConnectionDialogTests
{
    /// <summary>行の Id</summary>
    public static TheoryData<string> Ids() => ConnectionDialogRows.Table.Ids();

    [Theory(DisplayName = "接続ダイアログの表：手の列の後の状態が期待どおりで、不変条件を破らない")]
    [MemberData(nameof(Ids))]
    public async Task Row_Holds(string id)
    {
        var row = ConnectionDialogRows.Table[id];
        using var harness = new ConnectionDialogHarness();

        await row.Input.Body(harness);
        await harness.FinishAsync();
    }

    [Fact(DisplayName = "網羅：接続ダイアログの状態 × 手の種類")]
    public void Coverage_MatchesGaps()
    {
        var counter = new CoverageCounter(ConnectionDialogDimensions.Table);

        foreach (var row in ConnectionDialogRows.Table)
        {
            foreach (var (state, op) in row.Input.Touches)
            {
                ConnectionDialogDimensions.Observe(counter, state, op);
            }
        }

        counter
            .Report()
            .AssertGaps(ConnectionDialogDimensions.Gaps, ConnectionDialogDimensions.Scope);
    }
}
