using AwesomeAssertions;
using QuickER.Tests.TestSupport.Engines;

namespace QuickER.Tests.AI.Mock.Runner;

/// <summary>モックプロジェクト生成のランナーの流れの表を流す</summary>
/// <remarks>
/// 各行の本体を流したあと、返したランの結果を分類して行の宣言した分類と照合する（走らせない・例外で抜ける行は結果を返さない）。
/// </remarks>
public sealed class MockRunnerTests
{
    /// <summary>行の Id</summary>
    public static TheoryData<string> Ids() => MockRunnerRows.Table.Ids();

    [Theory(DisplayName = "流れの行：ランナーが行の入力で宣言どおりの結果に分類される")]
    [MemberData(nameof(Ids))]
    public async Task Row_Holds(string id)
    {
        var scenario = MockRunnerRows.Table[id].Input;
        using var context = new MockRunnerContext(scenario, TestContext.Current.CancellationToken);

        var result = await scenario.Body(context);

        if (scenario.Result is MockRunResult.NotRun or MockRunResult.Thrown)
        {
            result.Should().BeNull("走らせない・例外で抜ける行は結果を返さない");
        }
        else
        {
            result.Should().NotBeNull();
            MockRunnerContext.Classify(result!).Should().Be(scenario.Result);
        }
    }

    [Fact(DisplayName = "網羅：ターゲット × 結果の分類（理由つきで外した組だけが欠ける）")]
    public void Coverage_MatchesGaps()
    {
        var counter = new CoverageCounter(MockRunnerDimensions.Table);

        foreach (var row in MockRunnerRows.Table)
        {
            MockRunnerDimensions.Observe(counter, row.Input);
        }

        counter.Report().AssertGaps(MockRunnerDimensions.Gaps, MockRunnerDimensions.Scope);
    }
}
