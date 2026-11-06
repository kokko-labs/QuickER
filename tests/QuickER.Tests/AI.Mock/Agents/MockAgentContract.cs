using AwesomeAssertions;
using QuickER.AI.Mock;

namespace QuickER.Tests.AI.Mock.Agents;

/// <summary>契約の行を流す 1 ケースの文脈（アダプタ・組・取り消し）</summary>
/// <param name="Adapter">アダプタ（期待の組み方と性質の宣言）</param>
/// <param name="Rig">組</param>
/// <param name="CancellationToken">テストの取り消し</param>
internal sealed record MockAgentContext(
    MockAgentAdapter Adapter,
    MockAgentRig Rig,
    CancellationToken CancellationToken
)
{
    /// <summary>実の実行器</summary>
    public IMockProjectAgent Agent => Rig.Agent;

    /// <summary>台本で実行して結果を返す（テストの取り消しを渡す）</summary>
    /// <param name="script">台本</param>
    public Task<MockProjectAgentOutcome> RunAsync(MockAgentScript script) =>
        Rig.RunAsync(script, CancellationToken);

    /// <summary>結果を 1 つの値として表明する</summary>
    /// <param name="outcome">結果</param>
    /// <param name="success">期待する成否</param>
    /// <param name="error">期待するエラーの文</param>
    /// <param name="notLoggedIn">期待する未ログインの印</param>
    public static void ShouldBe(
        MockProjectAgentOutcome outcome,
        bool success,
        string? error,
        bool notLoggedIn
    ) => outcome.Should().Be(new MockProjectAgentOutcome(success, error, notLoggedIn));
}

/// <summary>契約の行の入力（観点・前提の性質・組の前提・本体）</summary>
/// <param name="Contract">観点（outcome / login / progress / cancel / interrupt など）</param>
/// <param name="Requires">前提にする性質（持たない実行器では流さない）</param>
/// <param name="Options">組の前提</param>
/// <param name="Body">流して表明する本体</param>
internal sealed record MockAgentScenario(
    string Contract,
    IReadOnlyList<MockAgentCapability> Requires,
    MockAgentRigOptions Options,
    Func<MockAgentContext, Task> Body
)
{
    /// <summary>このアダプタで流すか（前提の性質をすべて持つ）</summary>
    /// <param name="adapter">アダプタ</param>
    public bool AppliesTo(MockAgentAdapter adapter) => Requires.All(adapter.Has);
}
