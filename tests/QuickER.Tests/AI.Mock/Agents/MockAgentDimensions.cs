using QuickER.Tests.TestSupport.Engines;

namespace QuickER.Tests.AI.Mock.Agents;

/// <summary>モックプロジェクト生成の実行器の網の次元表・網羅しない組の一覧・台帳の行</summary>
/// <remarks>
/// 1 ケース＝（実行器, 契約の行）。網羅すべき組は実行器 × 行の全部で、前提の性質を持たない実行器の組を Accepted に、
/// 製品の不具合で直す区切りが決まっている組を Pending に載せる。どちらも手で書いた静的な一覧で、宣言から導かない。
/// </remarks>
internal static class MockAgentDimensions
{
    /// <summary>網羅しない組の照合の範囲</summary>
    public const string Scope = "mock-agents";

    /// <summary>実行器</summary>
    public const string Agent = "Agent";

    /// <summary>契約の行</summary>
    public const string Behavior = "Behavior";

    private const string ApiKey = "ApiKeyMockProjectAgent";
    private const string ClaudeCode = "ClaudeCodeMockProjectAgent";
    private const string Codex = "CodexMockProjectAgent";
    private const string Copilot = "CopilotMockProjectAgent";

    /// <summary>次元表</summary>
    public static DimensionTable Table =>
        new(
            EngineIds.Ai,
            [
                new Factor(
                    Agent,
                    MockAgentAxis.Authoritative.Select(name => new Level(name)).ToList(),
                    "モックプロジェクト生成の実行器（IMockProjectAgent の実装）"
                ),
                new Factor(
                    Behavior,
                    MockAgentContractRows.Table.Select(row => new Level(row.Id)).ToList(),
                    "契約の行"
                ),
            ]
        );

    /// <summary>
    /// 製品の不具合で流さない組（実行器・行・見つけた区切り・直す区切り・理由）。網羅しない組の一覧の Pending と、
    /// 流すケースから外す組の両方の正本（アダプタの宣言とは関係しない）
    /// </summary>
    /// <remarks>
    /// 直したら（直す区切りで）この一覧から消し、その実行器の期待を行へ足す。
    /// いまは空（次の不具合の置き場として、流すケースと網羅しない組への配線を残す）。
    /// </remarks>
    public static IReadOnlyList<(
        string Agent,
        string RowId,
        string Finding,
        string FixedIn,
        string Reason
    )> PendingCases { get; } = [];

    /// <summary>網羅しない組の一覧（前提の性質を持たない実行器 × 行と、製品の不具合で流さない組を手で書いた静的な一覧）</summary>
    /// <remarks>
    /// 流すケース（<see cref="CaseList"/>）は行の前提とアダプタの宣言から決まるが、Accepted の項目はそこから導かない。
    /// 宣言を 1 つ変えて流れる組が増えても減っても、一覧との照合（<c>Coverage_MatchesGaps</c>）で赤になる。
    /// </remarks>
    public static CoverageGapSet Gaps =>
        CoverageGap.Set(
            EngineIds.Ai,
            [
                Missing(
                    ApiKey,
                    "outcome-disconnect-while-waiting-fails-immediately",
                    MockAgentCapability.ReportsDisconnect
                ),
                Missing(
                    ClaudeCode,
                    "outcome-disconnect-while-waiting-fails-immediately",
                    MockAgentCapability.ReportsDisconnect
                ),
                Missing(
                    Copilot,
                    "outcome-disconnect-while-waiting-fails-immediately",
                    MockAgentCapability.ReportsDisconnect
                ),
                Missing(
                    ApiKey,
                    "login-not-logged-in-is-reported",
                    MockAgentCapability.DetectsNotLoggedIn
                ),
                Missing(
                    ApiKey,
                    "login-probe-not-logged-in-starts-no-session",
                    MockAgentCapability.ProbesLoginBeforeSession
                ),
                Missing(
                    ClaudeCode,
                    "login-probe-not-logged-in-starts-no-session",
                    MockAgentCapability.ProbesLoginBeforeSession
                ),
                Missing(
                    ApiKey,
                    "progress-tool-activity-is-forwarded",
                    MockAgentCapability.ForwardsToolActivityToProgress
                ),
                Missing(
                    ClaudeCode,
                    "progress-tool-activity-is-forwarded",
                    MockAgentCapability.ForwardsToolActivityToProgress
                ),
                Missing(
                    ApiKey,
                    "progress-declined-permission-is-forwarded",
                    MockAgentCapability.ReportsDeclinedPermission
                ),
                Missing(
                    ClaudeCode,
                    "progress-declined-permission-is-forwarded",
                    MockAgentCapability.ReportsDeclinedPermission
                ),
                Missing(
                    Codex,
                    "progress-declined-permission-is-forwarded",
                    MockAgentCapability.ReportsDeclinedPermission
                ),
                Missing(
                    ApiKey,
                    "interrupt-during-turn-completes-as-canceled",
                    MockAgentCapability.InterruptCompletesViaEvent
                ),
                Missing(
                    ClaudeCode,
                    "interrupt-during-turn-completes-as-canceled",
                    MockAgentCapability.InterruptCompletesViaEvent
                ),
                Missing(
                    ApiKey,
                    "nudge-once-without-ui-file-wpf",
                    MockAgentCapability.SendsContinuationNudge
                ),
                Missing(
                    ClaudeCode,
                    "nudge-once-without-ui-file-wpf",
                    MockAgentCapability.SendsContinuationNudge
                ),
                Missing(
                    ApiKey,
                    "nudge-none-with-ui-file-wpf",
                    MockAgentCapability.SendsContinuationNudge
                ),
                Missing(
                    ClaudeCode,
                    "nudge-none-with-ui-file-wpf",
                    MockAgentCapability.SendsContinuationNudge
                ),
                Missing(
                    ApiKey,
                    "nudge-once-without-ui-file-blazor",
                    MockAgentCapability.SendsContinuationNudge
                ),
                Missing(
                    ClaudeCode,
                    "nudge-once-without-ui-file-blazor",
                    MockAgentCapability.SendsContinuationNudge
                ),
                Missing(
                    ApiKey,
                    "nudge-none-with-ui-file-blazor",
                    MockAgentCapability.SendsContinuationNudge
                ),
                Missing(
                    ClaudeCode,
                    "nudge-none-with-ui-file-blazor",
                    MockAgentCapability.SendsContinuationNudge
                ),
                Missing(
                    ClaudeCode,
                    "lifetime-backend-disposed-after-run",
                    MockAgentCapability.DisposesBackendPerRun
                ),
                Missing(
                    ApiKey,
                    "prompt-shared-builder-states-headless-rules-wpf",
                    MockAgentCapability.SharesAgentPromptBuilder
                ),
                Missing(
                    ApiKey,
                    "prompt-shared-builder-states-headless-rules-blazor",
                    MockAgentCapability.SharesAgentPromptBuilder
                ),
                Missing(
                    ClaudeCode,
                    "prompt-emit-tool-paths-follow-target-blazor",
                    MockAgentCapability.SubmitsFilesThroughTool
                ),
                Missing(
                    Codex,
                    "prompt-emit-tool-paths-follow-target-blazor",
                    MockAgentCapability.SubmitsFilesThroughTool
                ),
                Missing(
                    Copilot,
                    "prompt-emit-tool-paths-follow-target-blazor",
                    MockAgentCapability.SubmitsFilesThroughTool
                ),
                .. PendingCases.Select(pending =>
                    CoverageGap.Tuple(
                        Scope,
                        pending.Finding,
                        pending.FixedIn,
                        pending.Reason,
                        (Agent, pending.Agent),
                        (Behavior, pending.RowId)
                    )
                ),
            ]
        );

    /// <summary>性質を持たない実行器 × 行の項目（理由はアダプタの宣言の出典）</summary>
    /// <param name="agent">実行器</param>
    /// <param name="rowId">行の Id</param>
    /// <param name="capability">実行器が持たない、行の前提の性質</param>
    private static CoverageGapEntry Missing(
        string agent,
        string rowId,
        MockAgentCapability capability
    ) =>
        CoverageGap.AcceptedTuple(
            Scope,
            "P3-11b",
            $"{capability} を持たない：{MockAgentAxis.Axis.Get(agent).Declarations[capability].Evidence}",
            (Agent, agent),
            (Behavior, rowId)
        );

    /// <summary>行 × 実行器（流すケース）</summary>
    public static TheoryData<string, string> Cases()
    {
        var data = new TheoryData<string, string>();

        foreach (var (id, agent) in CaseList())
        {
            data.Add(id, agent);
        }

        return data;
    }

    /// <summary>行 × 前提の性質を持つ実行器から、製品の不具合で流さない組（<see cref="PendingCases"/>）を除いた並び</summary>
    public static IReadOnlyList<(string Id, string Agent)> CaseList() =>
        (
            from row in MockAgentContractRows.Table
            from adapter in MockAgentAxis.Axis.Adapters
            where row.Input.AppliesTo(adapter)
            where
                !PendingCases.Any(pending =>
                    pending.Agent == adapter.AxisValue && pending.RowId == row.Id
                )
            select (row.Id, adapter.AxisValue)
        ).ToList();

    /// <summary>流すケースを数える</summary>
    public static void Observe(CoverageCounter counter, string rowId, string agent) =>
        counter.Observe(
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [Agent] = agent,
                [Behavior] = rowId,
            }
        );

    /// <summary>台帳の行</summary>
    public static EngineDescriptor Descriptor =>
        new(
            EngineIds.Ai,
            "MockAgents",
            "モックプロジェクト生成の実行器の網（実行器 4 種 × 契約の行）",
            Table,
            MockAgentContractRows.Table.Count,
            [
                "tests/QuickER.Tests/AI.Mock/Agents/ApiKeyMockAgentAdapter.cs",
                "tests/QuickER.Tests/AI.Mock/Agents/ApiKeyMockMaterials.cs",
                "tests/QuickER.Tests/AI.Mock/Agents/ClaudeCodeMockAgentAdapter.cs",
                "tests/QuickER.Tests/AI.Mock/Agents/CodexMockAgentAdapter.cs",
                "tests/QuickER.Tests/AI.Mock/Agents/CopilotMockAgentAdapter.cs",
                "tests/QuickER.Tests/AI.Mock/Agents/MockAgentAdapter.cs",
                "tests/QuickER.Tests/AI.Mock/Agents/MockAgentContract.cs",
                "tests/QuickER.Tests/AI.Mock/Agents/MockAgentContractRows.cs",
                "tests/QuickER.Tests/AI.Mock/Agents/MockAgentContractTests.cs",
                "tests/QuickER.Tests/AI.Mock/Agents/MockAgentDimensions.cs",
                "tests/QuickER.Tests/AI.Mock/Agents/MockAgentRig.cs",
                "tests/QuickER.Tests/AI.Mock/Agents/MockAgentScript.cs",
                "tests/QuickER.Tests/AI.Mock/Agents/QueueBuildRunner.cs",
            ],
            [
                "src/QuickER.AI.Mock/ApiKeyMockProjectAgent.cs",
                "src/QuickER.AI.Mock/ClaudeCodeMockProjectAgent.cs",
                "src/QuickER.AI.Mock/CodexMockProjectAgent.cs",
                "src/QuickER.AI.Mock/CopilotMockProjectAgent.cs",
            ]
        );
}
