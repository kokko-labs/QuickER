using QuickER.AI;
using QuickER.Tests.TestSupport.Engines;

namespace QuickER.Tests.AI.ChatEngines;

/// <summary>チャットエンジンの契約の網の次元表・網羅しない組の一覧・台帳の行</summary>
/// <remarks>
/// 1 ケース＝（エンジン, 契約の行）。網羅すべき組はエンジン × 行の全部で、前提の性質を持たないエンジンの組だけを
/// 網羅しない組の Accepted に載せる（理由はアダプタの宣言の出典。製品の事実だけを持たない宣言にする）。
/// </remarks>
internal static class ChatEngineDimensions
{
    /// <summary>網羅しない組の照合の範囲</summary>
    public const string Scope = "chat-engines";

    /// <summary>エンジン</summary>
    public const string Engine = "Engine";

    /// <summary>契約の行</summary>
    public const string Behavior = "Behavior";

    /// <summary>次元表</summary>
    public static DimensionTable Table =>
        new(
            EngineIds.Ai,
            [
                new Factor(
                    Engine,
                    Enum.GetValues<ErChatBackendKind>()
                        .Select(kind => new Level(kind.ToString(), AxisMember.Of(kind)))
                        .ToList(),
                    "チャットエンジン（接続方式）"
                ),
                new Factor(
                    Behavior,
                    ChatEngineContractRows.Table.Select(row => new Level(row.Id)).ToList(),
                    "契約の行"
                ),
            ]
        );

    /// <summary>網羅しない組の一覧（前提の性質を持たないエンジン × 行を手で書いた静的な一覧。理由はアダプタの宣言の出典）</summary>
    /// <remarks>
    /// 流すケース（<see cref="CaseList"/>）は行の前提とアダプタの宣言から決まるが、この一覧はそこから導かない。
    /// 宣言を 1 つ変えて流れる組が増えても減っても、一覧との照合（<c>Coverage_MatchesGaps</c>）で赤になる
    /// （導くと、宣言の書き換えで行が黙って流れなくなり、一覧も同時に増えて緑のまま残る）。
    /// </remarks>
    public static CoverageGapSet Gaps =>
        CoverageGap.Set(
            EngineIds.Ai,
            [
                Missing(
                    ErChatBackendKind.ApiKey,
                    "turn-stray-completion-after-turn-is-dropped",
                    ChatEngineCapability.AsyncCompletion
                ),
                Missing(
                    ErChatBackendKind.ClaudeCode,
                    "turn-stray-completion-after-turn-is-dropped",
                    ChatEngineCapability.AsyncCompletion
                ),
                Missing(
                    ErChatBackendKind.ApiKey,
                    "turn-stray-completion-before-any-turn-is-dropped",
                    ChatEngineCapability.AsyncCompletion
                ),
                Missing(
                    ErChatBackendKind.ClaudeCode,
                    "turn-stray-completion-before-any-turn-is-dropped",
                    ChatEngineCapability.AsyncCompletion
                ),
                Missing(
                    ErChatBackendKind.ApiKey,
                    "tool-without-host-is-failure-result",
                    ChatEngineCapability.OptionalToolHost
                ),
                Missing(
                    ErChatBackendKind.ApiKey,
                    "tool-response-send-failure-is-reported-as-status",
                    ChatEngineCapability.ToolResponseChannel
                ),
                Missing(
                    ErChatBackendKind.ClaudeCode,
                    "tool-response-send-failure-is-reported-as-status",
                    ChatEngineCapability.ToolResponseChannel
                ),
                Missing(
                    ErChatBackendKind.Codex,
                    "cancel-user-interrupt-then-backend-throws-is-silent-failure",
                    ChatEngineCapability.TurnRunsInsideSend
                ),
                Missing(
                    ErChatBackendKind.Copilot,
                    "cancel-user-interrupt-then-backend-throws-is-silent-failure",
                    ChatEngineCapability.TurnRunsInsideSend
                ),
                Missing(
                    ErChatBackendKind.ApiKey,
                    "failure-reported-is-summary-with-detail",
                    ChatEngineCapability.ReportedFailure
                ),
                Missing(
                    ErChatBackendKind.ApiKey,
                    "failure-reported-without-detail-is-summary-only",
                    ChatEngineCapability.ReportedFailure
                ),
                .. MissingAll(
                    "attachment-outside-support-is-refused",
                    ChatEngineCapability.RefusesSomeAttachmentKind,
                    ErChatBackendKind.ClaudeCode
                ),
                .. MissingAll(
                    "attachment-inside-support-reaches-backend",
                    ChatEngineCapability.AcceptsAttachments,
                    ErChatBackendKind.Codex
                ),
                .. MissingAll(
                    "dispose-releases-backend",
                    ChatEngineCapability.OwnsBackend,
                    ErChatBackendKind.ApiKey
                ),
                .. MissingAll(
                    "dispose-stops-completion-notifications",
                    ChatEngineCapability.AsyncCompletion,
                    ErChatBackendKind.ApiKey,
                    ErChatBackendKind.ClaudeCode
                ),
                .. MissingAll(
                    "dispose-deletes-working-directory",
                    ChatEngineCapability.CreatesWorkingDirectory,
                    ErChatBackendKind.ApiKey,
                    ErChatBackendKind.Copilot
                ),
                .. MissingAll(
                    "ready-cli-missing-is-not-ready-with-install-guidance",
                    ChatEngineCapability.CliLifecycle,
                    ErChatBackendKind.ApiKey
                ),
                .. MissingAll(
                    "ready-runtime-start-failure-keeps-reason-in-guidance",
                    ChatEngineCapability.LaunchesRuntime,
                    ErChatBackendKind.ApiKey,
                    ErChatBackendKind.ClaudeCode
                ),
                .. MissingAll(
                    "ready-recheck-after-cli-becomes-available-connects",
                    ChatEngineCapability.LaunchesRuntime,
                    ErChatBackendKind.ApiKey,
                    ErChatBackendKind.ClaudeCode
                ),
                .. MissingAll(
                    "ready-concurrent-connects-start-runtime-once",
                    ChatEngineCapability.LaunchesRuntime,
                    ErChatBackendKind.ApiKey,
                    ErChatBackendKind.ClaudeCode
                ),
                .. MissingAll(
                    "ready-connect-after-failed-start-retries",
                    ChatEngineCapability.LaunchesRuntime,
                    ErChatBackendKind.ApiKey,
                    ErChatBackendKind.ClaudeCode
                ),
                .. MissingAll(
                    "conversation-reopens-after-connection-lost",
                    ChatEngineCapability.ConnectionCanDrop,
                    ErChatBackendKind.ApiKey,
                    ErChatBackendKind.ClaudeCode
                ),
            ]
        );

    /// <summary>1 つの行で、同じ性質を持たない複数のエンジンの項目</summary>
    /// <param name="rowId">行の Id</param>
    /// <param name="capability">エンジンが持たない、行の前提の性質</param>
    /// <param name="engines">性質を持たないエンジン（手で並べる）</param>
    private static IEnumerable<CoverageGapEntry> MissingAll(
        string rowId,
        ChatEngineCapability capability,
        params ErChatBackendKind[] engines
    ) => engines.Select(engine => Missing(engine, rowId, capability));

    /// <summary>性質を持たないエンジン × 行の項目（理由はアダプタの宣言の出典）</summary>
    /// <param name="engine">エンジン</param>
    /// <param name="rowId">行の Id</param>
    /// <param name="capability">エンジンが持たない、行の前提の性質</param>
    private static CoverageGapEntry Missing(
        ErChatBackendKind engine,
        string rowId,
        ChatEngineCapability capability
    ) =>
        CoverageGap.AcceptedTuple(
            Scope,
            "P3-11a",
            $"{capability} を持たない：{ChatEngineAxis.Axis.Get(engine.ToString()).Declarations[capability].Evidence}",
            (Engine, engine.ToString()),
            (Behavior, rowId)
        );

    /// <summary>行 × 前提の性質を持つエンジン（流すケース）</summary>
    public static TheoryData<string, string> Cases()
    {
        var data = new TheoryData<string, string>();

        foreach (var (id, engine) in CaseList())
        {
            data.Add(id, engine);
        }

        return data;
    }

    /// <summary>行 × 前提の性質を持つエンジン（行の Id とエンジンの組の並び）</summary>
    public static IReadOnlyList<(string Id, string Engine)> CaseList() =>
        (
            from row in ChatEngineContractRows.Table
            from adapter in ChatEngineAxis.Axis.Adapters
            where row.Input.AppliesTo(adapter)
            select (row.Id, adapter.AxisValue)
        ).ToList();

    /// <summary>流すケースを数える</summary>
    public static void Observe(CoverageCounter counter, string rowId, string engine) =>
        counter.Observe(
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [Engine] = engine,
                [Behavior] = rowId,
            }
        );

    /// <summary>台帳の行</summary>
    public static EngineDescriptor Descriptor =>
        new(
            EngineIds.Ai,
            "ChatEngines",
            "チャットエンジンの契約の網（エンジン 4 種 × 契約の行）",
            Table,
            ChatEngineContractRows.Table.Count,
            [
                "tests/QuickER.Tests/AI/ChatEngines/ApiKeyChatEngineAdapter.cs",
                "tests/QuickER.Tests/AI/ChatEngines/ChatEngineAdapter.cs",
                "tests/QuickER.Tests/AI/ChatEngines/ChatEngineContract.cs",
                "tests/QuickER.Tests/AI/ChatEngines/ChatEngineContractRows.cs",
                "tests/QuickER.Tests/AI/ChatEngines/ChatEngineContractRows.Attachment.cs",
                "tests/QuickER.Tests/AI/ChatEngines/ChatEngineContractRows.Cancel.cs",
                "tests/QuickER.Tests/AI/ChatEngines/ChatEngineContractRows.Conversation.cs",
                "tests/QuickER.Tests/AI/ChatEngines/ChatEngineContractRows.Dispose.cs",
                "tests/QuickER.Tests/AI/ChatEngines/ChatEngineContractRows.Failure.cs",
                "tests/QuickER.Tests/AI/ChatEngines/ChatEngineContractRows.Ready.cs",
                "tests/QuickER.Tests/AI/ChatEngines/ChatEngineContractRows.Tool.cs",
                "tests/QuickER.Tests/AI/ChatEngines/ChatEngineContractRows.Turn.cs",
                "tests/QuickER.Tests/AI/ChatEngines/ChatEngineContractTests.cs",
                "tests/QuickER.Tests/AI/ChatEngines/ChatEngineDimensions.cs",
                "tests/QuickER.Tests/AI/ChatEngines/ChatEngineRig.cs",
                "tests/QuickER.Tests/AI/ChatEngines/ChatTurnScript.cs",
                "tests/QuickER.Tests/AI/ChatEngines/ClaudeCodeChatEngineAdapter.cs",
                "tests/QuickER.Tests/AI/ChatEngines/CodexChatEngineAdapter.cs",
                "tests/QuickER.Tests/AI/ChatEngines/CopilotChatEngineAdapter.cs",
            ],
            ["src/QuickER.AI"]
        );
}
