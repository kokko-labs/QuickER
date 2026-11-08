using AwesomeAssertions;
using QuickER.Tests.TestSupport.Engines;

namespace QuickER.Tests.AI.UI;

/// <summary>保存した API キーの表（入口 × 暗号ファイルの状態。QuickER.AI.UI の ApiKeyStore と接続設定の VM の保存のオン／オフ）</summary>
/// <remarks>
/// 実 %LOCALAPPDATA% を汚さず（並列テストの IO 競合も避けて）検証するため、保存先フォルダ指定の版でテストごとの一時フォルダへ隔離する。
/// </remarks>
public sealed class ApiKeyStoreTests
{
    /// <summary>行の Id</summary>
    public static TheoryData<string> Ids() => ApiKeyStoreRows.Table.Ids();

    [Theory(
        DisplayName = "保存した API キーの表：無い・読めた・読めないを分け、読めないキーを入口が上書きも削除もしない"
    )]
    [MemberData(nameof(Ids))]
    public void Row_Holds(string id)
    {
        var row = ApiKeyStoreRows.Table[id];
        using var harness = new ApiKeyStoreHarness();
        harness.Prepare(row.Input.State, row.Input.Corrupt);

        var outcome = harness.Run(row.Input.Entrance);

        harness
            .Observed.Single()
            .State.Should()
            .Be(row.Input.State, "行が作った状態が入口の前の実際の状態と同じ");
        row.Input.Expect(outcome, harness);
    }

    [Fact(DisplayName = "網羅：入口 × 暗号ファイルの状態")]
    public void Coverage_MatchesDeclaredGaps()
    {
        var counter = new CoverageCounter(ApiKeyStoreDimensions.Table);

        foreach (var row in ApiKeyStoreRows.Table)
        {
            using var harness = new ApiKeyStoreHarness();
            harness.Prepare(row.Input.State, row.Input.Corrupt);
            harness.Run(row.Input.Entrance);

            foreach (var (entrance, state) in harness.Observed)
            {
                ApiKeyStoreDimensions.Observe(counter, entrance, state);
            }
        }

        counter.Report().AssertGaps(ApiKeyStoreDimensions.Gaps, ApiKeyStoreDimensions.Scope);
    }
}
