using AwesomeAssertions;
using QuickER.Tests.TestSupport.Engines;

namespace QuickER.Tests.AI.ChatEngines;

/// <summary>API キー接続のドライバの表（同じ発話とツールの往復を全ドライバへ流し、送る形の射影を照合する）</summary>
public sealed class ChatDriverTests
{
    /// <summary>行 × ドライバ</summary>
    public static TheoryData<string, string> Cases()
    {
        var data = new TheoryData<string, string>();

        foreach (var (id, driver) in ChatDriverDimensions.CaseList())
        {
            data.Add(id, driver);
        }

        return data;
    }

    [Theory(DisplayName = "ドライバの表：同じ発話をドライバごとの送る形へ変える")]
    [MemberData(nameof(Cases))]
    public async Task Row_Matches(string id, string driver)
    {
        var row = ChatDriverRows.Table[id];
        var actual = await row.Input.Project(
            ChatDriverAxis.Axis.Get(driver),
            TestContext.Current.CancellationToken
        );
        var expected = row.Input.Expected[driver];

        ChatDriverRows
            .Matches(actual, expected)
            .Should()
            .BeTrue(
                $"{id} × {driver}\n  期待: {string.Join(" | ", expected)}\n  実際: {string.Join(" | ", actual)}"
            );
    }

    [Fact(DisplayName = "ドライバの表：どの行もアダプタを持つ全ドライバの期待を持つ")]
    public void Rows_DeclareEveryDriver()
    {
        foreach (var row in ChatDriverRows.Table)
        {
            row.Input.Expected.Keys.Should()
                .BeEquivalentTo(
                    ChatDriverAxis.Axis.Adapters.Select(adapter => adapter.AxisValue),
                    row.Id
                );
        }
    }

    [Fact(DisplayName = "網羅：ドライバ × 送る形")]
    public void Coverage_IsComplete()
    {
        var counter = new CoverageCounter(ChatDriverDimensions.Table);

        foreach (var (id, driver) in ChatDriverDimensions.CaseList())
        {
            ChatDriverDimensions.Observe(counter, id, driver);
        }

        counter.Report().AssertGaps(CoverageGap.Set(EngineIds.Ai, []), ChatDriverDimensions.Scope);
    }

    [Fact(
        DisplayName = "ドライバの軸：正本（IChatTurnDriver の具象）ごとにアダプタか理由つきの除外がちょうど 1 つ"
    )]
    public void Axis_CoversAuthoritativeValues()
    {
        ChatDriverAxis
            .Axis.Adapters.Select(adapter => adapter.AxisValue)
            .Concat(ChatDriverAxis.Axis.Excluded.Keys)
            .Should()
            .BeEquivalentTo(ChatDriverAxis.Names);
    }
}
