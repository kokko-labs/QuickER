using QuickER.Tests.TestSupport.Engines;

namespace QuickER.Tests.AI.UI;

/// <summary>
/// 接続設定の操作の列の表（初期状態 × 手の列 → 状態・設定 JSON・API キーの保存・通知）と、どの手の後にも掛ける不変条件
/// </summary>
public sealed class ConnectionSettingsTests
{
    /// <summary>行の Id</summary>
    public static TheoryData<string> Ids() => ConnectionSettingsRows.Table.Ids();

    [Theory(DisplayName = "接続設定の表：手の列の後の状態と保存が期待どおりで、不変条件を破らない")]
    [MemberData(nameof(Ids))]
    public void Row_Holds(string id)
    {
        var row = ConnectionSettingsRows.Table[id];
        using var harness = new ConnectionSettingsHarness();

        row.Input.Body(harness);
        harness.AssertInvariants();
    }

    [Fact(DisplayName = "網羅：接続設定の面 × 手の種類")]
    public void Coverage_MatchesGaps()
    {
        var counter = new CoverageCounter(ConnectionSettingsDimensions.Table);

        foreach (var row in ConnectionSettingsRows.Table)
        {
            foreach (var (area, op) in row.Input.Touches)
            {
                ConnectionSettingsDimensions.Observe(counter, area, op);
            }
        }

        counter
            .Report()
            .AssertGaps(ConnectionSettingsDimensions.Gaps, ConnectionSettingsDimensions.Scope);
    }
}
