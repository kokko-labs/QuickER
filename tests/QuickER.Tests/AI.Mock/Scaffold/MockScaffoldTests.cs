using QuickER.Tests.TestSupport.Engines;

namespace QuickER.Tests.AI.Mock.Scaffold;

/// <summary>モックプロジェクトのスキャフォールドの表を流す</summary>
public sealed class MockScaffoldTests
{
    /// <summary>行の Id</summary>
    public static TheoryData<string> Ids() => MockScaffoldRows.Table.Ids();

    [Theory(
        DisplayName = "スキャフォールドの行：行のターゲット・方言・出力フォルダの状態で宣言どおりに書く・断る"
    )]
    [MemberData(nameof(Ids))]
    public async Task Row_Holds(string id)
    {
        var scenario = MockScaffoldRows.Table[id].Input;
        using var context = new MockScaffoldContext();

        await scenario.Body(context);
    }

    [Fact(
        DisplayName = "網羅：ターゲット × 方言 × 出力フォルダの状態（理由つきで外した組だけが欠ける）"
    )]
    public void Coverage_MatchesGaps()
    {
        var counter = new CoverageCounter(MockScaffoldDimensions.Table);

        foreach (var row in MockScaffoldRows.Table)
        {
            MockScaffoldDimensions.Observe(counter, row.Input);
        }

        counter.Report().AssertGaps(MockScaffoldDimensions.Gaps, MockScaffoldDimensions.Scope);
    }
}
