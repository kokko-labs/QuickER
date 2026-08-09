using System.Text.Json;
using AwesomeAssertions;
using QuickER.AI;
using QuickER.AI.Chat;

namespace QuickER.Tests.AI;

/// <summary><see cref="CodexChatEngine"/> が Codex 通知を共通イベントへ変換することを検証するテストクラス</summary>
public class CodexChatEngineTests
{
    private sealed class SyncUiDispatcher : IUiDispatcher
    {
        public T Invoke<T>(Func<T> func) => func();
    }

    // RecordingToolHost は共有版（QuickER.Tests.AI.RecordingToolHost）を使用する

    private static CodexDynamicToolCallRequest BuildToolCall(string tool, string argsJson)
    {
        using var doc = JsonDocument.Parse(argsJson);

        return new CodexDynamicToolCallRequest
        {
            RequestId = 1,
            ThreadId = "thr_test",
            TurnId = "turn_test",
            CallId = "call_1",
            Tool = tool,
            Arguments = doc.RootElement.Clone(),
        };
    }

    /// <summary>エージェント差分通知が共通の delta イベントへ変換されることを検証する</summary>
    [Fact(DisplayName = "Codex の差分通知は AssistantDelta へ変換される")]
    public void AgentMessageDelta_IsForwardedAsAssistantDelta()
    {
        var client = new FakeCodexAppServerClient();
        var engine = new CodexChatEngine(
            client,
            new RecordingToolHost(),
            new SyncUiDispatcher(),
            ErDesignProfile.ErDesign
        );
        var deltas = new List<string>();
        engine.AssistantDeltaReceived += (_, d) => deltas.Add(d);

        client.RaiseAgentMessageDelta("こんに");
        client.RaiseAgentMessageDelta("ちは");

        deltas.Should().Equal("こんに", "ちは");
    }

    /// <summary>ツール呼び出し通知でツールが実行され、活動通知と JSON-RPC 応答が行われることを検証する</summary>
    [Fact(DisplayName = "Codex のツール呼び出しはツール実行・活動通知・応答返送を行う")]
    public void DynamicToolCall_ExecutesAndResponds()
    {
        var client = new FakeCodexAppServerClient();
        var host = new RecordingToolHost();
        var engine = new CodexChatEngine(
            client,
            host,
            new SyncUiDispatcher(),
            ErDesignProfile.ErDesign
        );
        var activities = new List<ErChatToolActivity>();
        engine.ToolActivityReceived += (_, a) => activities.Add(a);

        client.RaiseDynamicToolCall(BuildToolCall("add_entity", "{\"table_name\":\"Book\"}"));

        host.Calls.Should().ContainSingle();
        host.Calls[0].Tool.Should().Be("add_entity");
        activities.Should().ContainSingle().Which.ToolName.Should().Be("add_entity");
        client.RespondToolCount.Should().Be(1);
    }

    /// <summary>
    /// commandExecution / fileChange の承認要求は decision:"decline" で拒否され、その旨が活動として
    /// 可視化されることを検証する。ER 図の操作は dynamicTools 経路のため、自動承認する必要はない。
    /// </summary>
    [Theory(
        DisplayName = "Codex の decision 形の承認要求は decline で拒否され活動として通知される"
    )]
    [InlineData("item/commandExecution/requestApproval", "commandExecution")]
    [InlineData("item/fileChange/requestApproval", "fileChange")]
    public void ApprovalRequest_IsDeclinedAndReported(string method, string expectedLabel)
    {
        var client = new FakeCodexAppServerClient();
        var activities = RaiseApprovalRequest(client, method);

        client.ApprovalDecisions.Should().Equal("decline");
        client.ApprovalResultJson.Should().BeEmpty();
        AssertDeclinedActivity(activities, expectedLabel);
    }

    /// <summary>
    /// permissions の承認要求は decision フィールドを持たない応答形（権限プロファイルが必須）のため、
    /// 空プロファイル＝何も付与しない応答で拒否することを検証する。
    /// </summary>
    /// <remarks>decision で応答するとスキーマ違反になる（Codex 0.146.0 のスキーマで確認）</remarks>
    [Fact(DisplayName = "Codex の permissions 承認要求は空の権限プロファイルで拒否される")]
    public void PermissionsApprovalRequest_IsDeclinedWithEmptyProfile()
    {
        var client = new FakeCodexAppServerClient();
        var activities = RaiseApprovalRequest(client, "item/permissions/requestApproval");

        client.ApprovalDecisions.Should().BeEmpty();
        client.ApprovalResultJson.Should().Equal("""{"permissions":{},"scope":"turn"}""");
        AssertDeclinedActivity(activities, "permissions");
    }

    /// <summary>承認要求を 1 件発火し、通知された活動の一覧を返す</summary>
    private static List<ErChatToolActivity> RaiseApprovalRequest(
        FakeCodexAppServerClient client,
        string method
    )
    {
        var engine = new CodexChatEngine(
            client,
            new RecordingToolHost(),
            new SyncUiDispatcher(),
            ErDesignProfile.ErDesign
        );
        var activities = new List<ErChatToolActivity>();
        engine.ToolActivityReceived += (_, a) => activities.Add(a);

        client.RaiseApprovalRequested(
            new CodexApprovalRequest
            {
                RequestId = 7,
                Method = method,
                ThreadId = "thr_test",
                TurnId = "turn_test",
                ItemId = "item_1",
            }
        );

        return activities;
    }

    /// <summary>拒否が 1 件だけ活動として通知されたことを検証する</summary>
    private static void AssertDeclinedActivity(
        List<ErChatToolActivity> activities,
        string expectedLabel
    )
    {
        var activity = activities.Should().ContainSingle().Which;
        activity.ToolName.Should().Be(expectedLabel);
        activity.Success.Should().BeFalse();
        activity.Result.Should().Be(QuickER.AI.Resources.Strings.Codex_ApprovalDeclined);
    }

    /// <summary>ターン完了通知が成否に応じた共通イベントへ変換されることを検証する</summary>
    [Theory(DisplayName = "Codex のターン完了は成否に応じた結果へ変換される")]
    [InlineData("completed", true)]
    [InlineData("interrupted", false)]
    [InlineData("failed", false)]
    public void TurnCompleted_IsTranslatedByStatus(string status, bool expectedSuccess)
    {
        var client = new FakeCodexAppServerClient();
        var engine = new CodexChatEngine(
            client,
            new RecordingToolHost(),
            new SyncUiDispatcher(),
            ErDesignProfile.ErDesign
        );
        ErChatTurnResult? result = null;
        engine.TurnCompleted += (_, r) => result = r;

        client.RaiseTurnCompleted(status, status == "failed" ? "boom" : null);

        result.Should().NotBeNull();
        result!.Value.Success.Should().Be(expectedSuccess);
    }

    /// <summary>非 openai プロバイダーでは接続のみで送信可能（認証不要）になることを検証する</summary>
    [Fact(DisplayName = "非 openai プロバイダーは認証不要で IsReady になる")]
    public async Task IsReady_NonOpenAiProvider_RequiresNoAuth()
    {
        var client = new FakeCodexAppServerClient();
        var engine = new CodexChatEngine(
            client,
            new RecordingToolHost(),
            new SyncUiDispatcher(),
            ErDesignProfile.ErDesign
        )
        {
            ModelProvider = "ollama-launch",
        };

        await engine.InitializeAsync(TestContext.Current.CancellationToken);

        engine.IsReady.Should().BeTrue();
    }

    /// <summary>
    /// codex CLI が未検出のとき、プロセス起動を試みず未検出状態（赤・インストール案内）になることを検証する。
    /// </summary>
    [Fact(DisplayName = "codex 未検出なら起動せず未検出状態になる")]
    public async Task Connect_CliMissing_DoesNotStartAndReportsNotFound()
    {
        var client = new FakeCodexAppServerClient { IsCliAvailable = false };
        var engine = new CodexChatEngine(
            client,
            new RecordingToolHost(),
            new SyncUiDispatcher(),
            ErDesignProfile.ErDesign
        );
        CodexAuthState? state = null;
        engine.AuthStateChanged += (_, s) => state = s;

        await engine.InitializeAsync(TestContext.Current.CancellationToken);

        client.StartCount.Should().Be(0, "未検出ならプロセス起動を試みない");
        engine.IsCliMissing.Should().BeTrue();
        engine.IsStarted.Should().BeFalse();
        engine.IsReady.Should().BeFalse();
        engine.AccountSummary.Should().Be(QuickER.AI.Resources.Strings.Codex_Status_NotFound);
        engine.Guidance.Should().Be(QuickER.AI.Resources.Strings.Codex_Guidance_Install);
        state.Should().NotBeNull();
        state!.Value.IsCliMissing.Should().BeTrue();
        state.Value.Guidance.Should().Be(QuickER.AI.Resources.Strings.Codex_Guidance_Install);
    }

    /// <summary>検出済みで起動に失敗した場合は、未検出ではなく接続失敗として理由が案内されることを検証する</summary>
    [Fact(DisplayName = "検出済みで起動失敗なら接続失敗の理由を案内する")]
    public async Task Connect_StartFails_ReportsConnectFailure()
    {
        var client = new FakeCodexAppServerClient
        {
            StartException = new InvalidOperationException("起動できません"),
        };
        var engine = new CodexChatEngine(
            client,
            new RecordingToolHost(),
            new SyncUiDispatcher(),
            ErDesignProfile.ErDesign
        );
        var statuses = new List<string>();
        engine.StatusChanged += (_, m) => statuses.Add(m);

        await engine.InitializeAsync(TestContext.Current.CancellationToken);

        client.StartCount.Should().Be(1);
        engine.IsCliMissing.Should().BeFalse();
        engine.IsStarted.Should().BeFalse();
        engine.Guidance.Should().Contain("起動できません");
        statuses.Should().ContainSingle().Which.Should().Contain("起動できません");
    }

    /// <summary>
    /// ChatGPT ログイン済み（メール・プランあり）なら、概要が「ログイン済み（メール / プラン）」形式
    /// （Copilot 接続タブの概要と同形）になり、ログイン済みの常時案内が立つことを検証する。
    /// </summary>
    [Fact(
        DisplayName = "ChatGPT ログイン済みなら概要は「ログイン済み（メール / プラン）」形式になる"
    )]
    public async Task Connect_ChatGptLoggedIn_UsesUnifiedSummaryAndGuidance()
    {
        var client = new FakeCodexAppServerClient
        {
            NextAccountInfo = new CodexAccountInfo
            {
                RequiresOpenAiAuth = true,
                AuthMode = CodexAuthMode.ChatGpt,
                Email = "user@example.com",
                PlanType = "plus",
            },
        };
        var engine = new CodexChatEngine(
            client,
            new RecordingToolHost(),
            new SyncUiDispatcher(),
            ErDesignProfile.ErDesign
        );

        await engine.InitializeAsync(TestContext.Current.CancellationToken);

        engine
            .AccountSummary.Should()
            .Be(
                string.Format(
                    QuickER.AI.Resources.Strings.Codex_Account_EmailLoggedIn,
                    "user@example.com / plus"
                )
            );
        engine.Guidance.Should().Be(QuickER.AI.Resources.Strings.Codex_Guidance_LoggedIn);
    }

    /// <summary>未検出から検出可能へ変わったら、「再確認」で未検出表示が解除され接続されることを検証する</summary>
    [Fact(DisplayName = "未検出から復帰したら再確認で接続できる")]
    public async Task Refresh_AfterCliBecomesAvailable_Connects()
    {
        var client = new FakeCodexAppServerClient { IsCliAvailable = false };
        var engine = new CodexChatEngine(
            client,
            new RecordingToolHost(),
            new SyncUiDispatcher(),
            ErDesignProfile.ErDesign
        )
        {
            ModelProvider = "ollama-launch",
        };

        await engine.InitializeAsync(TestContext.Current.CancellationToken);
        client.IsCliAvailable = true;
        await engine.RefreshAccountStateAsync(TestContext.Current.CancellationToken);

        client.StartCount.Should().Be(1);
        engine.IsCliMissing.Should().BeFalse();
        engine.IsStarted.Should().BeTrue();
        engine.Guidance.Should().BeEmpty();
    }

    /// <summary>「再確認」は未接続のまま諦めず、接続からやり直すことを検証する</summary>
    [Fact(DisplayName = "再確認は未接続なら接続を試行する")]
    public async Task Refresh_WhenNotStarted_AttemptsConnect()
    {
        var client = new FakeCodexAppServerClient();
        var engine = new CodexChatEngine(
            client,
            new RecordingToolHost(),
            new SyncUiDispatcher(),
            ErDesignProfile.ErDesign
        )
        {
            ModelProvider = "ollama-launch",
        };

        await engine.RefreshAccountStateAsync(TestContext.Current.CancellationToken);

        client.StartCount.Should().Be(1, "未接続なら接続からやり直す");
        engine.IsStarted.Should().BeTrue();
        engine.IsReady.Should().BeTrue();
    }

    /// <summary>Codex は添付非対応（AttachmentSupport=None）であることを検証する</summary>
    [Fact(DisplayName = "Codex の AttachmentSupport は None")]
    public void AttachmentSupport_IsNone()
    {
        var engine = new CodexChatEngine(
            new FakeCodexAppServerClient(),
            new RecordingToolHost(),
            new SyncUiDispatcher(),
            ErDesignProfile.ErDesign
        );

        engine.AttachmentSupport.Should().Be(AttachmentSupport.None);
    }

    /// <summary>添付付き送信は防御的に NotSupportedException で弾かれることを検証する</summary>
    [Fact(DisplayName = "添付付き送信は NotSupportedException")]
    public async Task SendAsync_WithAttachments_Throws()
    {
        var engine = new CodexChatEngine(
            new FakeCodexAppServerClient(),
            new RecordingToolHost(),
            new SyncUiDispatcher(),
            ErDesignProfile.ErDesign
        );

        byte[] pngData = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
        var attachment = new ChatAttachment(
            "a.png",
            ChatAttachmentKind.Image,
            "image/png",
            pngData
        );

        var act = () =>
            engine.SendAsync("やあ", [attachment], TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<NotSupportedException>();
    }
}
