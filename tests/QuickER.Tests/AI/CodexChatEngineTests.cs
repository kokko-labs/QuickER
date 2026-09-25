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
    /// ツールホストが例外を投げても、失敗のツール結果として応答されターンが詰まらないことを検証する
    /// （素通しすると fire-and-forget のタスク内で例外が消え、AI 側は結果を待ち続ける）。
    /// </summary>
    [Fact(DisplayName = "Codex のツール実行の例外は失敗結果として応答されターンが詰まらない")]
    public void DynamicToolCall_ToolHostThrows_RespondsWithFailureResult()
    {
        var client = new FakeCodexAppServerClient();
        var host = new ThrowingToolHost { Exception = new InvalidOperationException("boom") };
        var engine = new CodexChatEngine(
            client,
            host,
            new SyncUiDispatcher(),
            ErDesignProfile.ErDesign
        );
        var activities = new List<ErChatToolActivity>();
        engine.ToolActivityReceived += (_, a) => activities.Add(a);

        client.RaiseDynamicToolCall(BuildToolCall("add_entity", "{\"table_name\":\"Book\"}"));

        activities.Should().ContainSingle();
        activities[0].Success.Should().BeFalse();
        activities[0].Result.Should().Contain("boom");
        client.RespondToolCount.Should().Be(1);
        client.LastToolSuccess.Should().BeFalse();
        client.LastToolResult.Should().Contain("boom");
    }

    /// <summary>ツール応答の送信に失敗した場合、その旨が StatusChanged で可視化されることを検証する</summary>
    [Fact(DisplayName = "Codex のツール応答の送信失敗はステータスへ可視化される")]
    public void DynamicToolCall_RespondFails_ReportsStatus()
    {
        var client = new FakeCodexAppServerClient
        {
            RespondToolException = new InvalidOperationException("接続が切れました"),
        };
        var host = new RecordingToolHost();
        var engine = new CodexChatEngine(
            client,
            host,
            new SyncUiDispatcher(),
            ErDesignProfile.ErDesign
        );
        var statuses = new List<string>();
        engine.StatusChanged += (_, m) => statuses.Add(m);

        client.RaiseDynamicToolCall(BuildToolCall("add_entity", "{\"table_name\":\"Book\"}"));

        statuses
            .Should()
            .ContainSingle()
            .Which.Should()
            .Be(
                string.Format(
                    QuickER.AI.Resources.Strings.Codex_ToolResponseSendFailed,
                    "接続が切れました"
                )
            );
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

    /// <summary>
    /// エンジンが開始したターンの完了通知が、成否に応じた共通イベントへ変換されることを検証する
    /// （エンジンが開始していないターンの完了通知は転送されない＝二重通知防止のゲートが前提）。
    /// </summary>
    [Theory(DisplayName = "Codex のターン完了は成否に応じた結果へ変換される")]
    [InlineData("completed", true)]
    [InlineData("interrupted", false)]
    [InlineData("failed", false)]
    public async Task TurnCompleted_IsTranslatedByStatus(string status, bool expectedSuccess)
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

        // ターンを開始してから完了通知を発火する（実行中でないターンの完了は破棄される）
        await engine.SendAsync("やあ", TestContext.Current.CancellationToken);
        client.RaiseTurnCompleted(status, status == "failed" ? "boom" : null);

        result.Should().NotBeNull();
        result!.Value.Success.Should().Be(expectedSuccess);
    }

    /// <summary>
    /// エンジンが開始していないターンの完了通知（既に完了済み・エンジン外のターン）は
    /// 転送されないことを検証する（強制停止・接続断と turn/completed の競合時の二重通知防止）。
    /// </summary>
    [Fact(DisplayName = "Codex の実行中でないターンの完了通知は転送されない")]
    public void TurnCompleted_WithoutTurnInProgress_IsDropped()
    {
        var client = new FakeCodexAppServerClient();
        var engine = new CodexChatEngine(
            client,
            new RecordingToolHost(),
            new SyncUiDispatcher(),
            ErDesignProfile.ErDesign
        );
        var completions = new List<ErChatTurnResult>();
        engine.TurnCompleted += (_, r) => completions.Add(r);

        client.RaiseTurnCompleted("completed");

        completions.Should().BeEmpty();
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

    /// <summary>
    /// ターン実行中に App Server との接続が切れたら、ターンを失敗完了させ（実行中表示のまま固着させない）、
    /// 次の送信では新しいスレッドを開き直すことを検証する。
    /// </summary>
    /// <remarks>
    /// <c>turn/start</c> の応答後にプロセスが落ちると応答待ちリクエストが無く、turn/completed 通知も
    /// 二度と来ない。接続断の通知が無ければチャットは「実行中」のまま永久に止まる
    /// </remarks>
    [Fact(DisplayName = "ターン実行中の接続断はターンを失敗完了させ次の送信で新スレッドを開く")]
    public async Task Disconnected_DuringTurn_FailsTurnAndStartsNewThread()
    {
        var client = new FakeCodexAppServerClient();
        var engine = new CodexChatEngine(
            client,
            new RecordingToolHost(),
            new SyncUiDispatcher(),
            ErDesignProfile.ErDesign
        );
        var results = new List<ErChatTurnResult>();
        engine.TurnCompleted += (_, r) => results.Add(r);

        await engine.StartConversationAsync(TestContext.Current.CancellationToken);
        await engine.SendAsync("やあ", TestContext.Current.CancellationToken);
        client.RaiseDisconnected();

        results.Should().ContainSingle();
        results[0].Success.Should().BeFalse();
        results[0].Error.Should().Be(QuickER.AI.Resources.Strings.Codex_Disconnected);

        // サーバー内の状態（スレッド・ターン）は失われているため、次の送信はスレッドから開き直す
        await engine.SendAsync("もう一度", TestContext.Current.CancellationToken);
        client.StartThreadCount.Should().Be(2);
    }

    /// <summary>
    /// ターン非実行中の接続断は、ターン完了イベントを出さずステータスで文脈喪失だけを伝えることを検証する。
    /// </summary>
    /// <remarks>黙って新しいスレッドで続けると、会話が続いているように見えたまま文脈だけが消える</remarks>
    [Fact(
        DisplayName = "ターン非実行中の接続断は TurnCompleted を出さずステータスで文脈喪失を伝える"
    )]
    public async Task Disconnected_WithoutTurn_ReportsStatusOnly()
    {
        var client = new FakeCodexAppServerClient();
        var engine = new CodexChatEngine(
            client,
            new RecordingToolHost(),
            new SyncUiDispatcher(),
            ErDesignProfile.ErDesign
        );
        var results = new List<ErChatTurnResult>();
        engine.TurnCompleted += (_, r) => results.Add(r);

        await engine.StartConversationAsync(TestContext.Current.CancellationToken);
        var statuses = new List<string>();
        engine.StatusChanged += (_, m) => statuses.Add(m);
        client.RaiseDisconnected();

        results.Should().BeEmpty();
        statuses
            .Should()
            .ContainSingle()
            .Which.Should()
            .Be(QuickER.AI.Resources.Strings.Codex_Disconnected);
    }

    /// <summary>会話が始まっていない（スレッドが無い）接続断では何も通知しないことを検証する</summary>
    /// <remarks>失われた文脈が無いので、利用者へ伝えることが無い</remarks>
    [Fact(DisplayName = "会話前の接続断は何も通知しない")]
    public async Task Disconnected_BeforeConversation_NotifiesNothing()
    {
        var client = new FakeCodexAppServerClient();
        var engine = new CodexChatEngine(
            client,
            new RecordingToolHost(),
            new SyncUiDispatcher(),
            ErDesignProfile.ErDesign
        );

        await engine.InitializeAsync(TestContext.Current.CancellationToken);
        var results = new List<ErChatTurnResult>();
        var statuses = new List<string>();
        engine.TurnCompleted += (_, r) => results.Add(r);
        engine.StatusChanged += (_, m) => statuses.Add(m);
        client.RaiseDisconnected();

        results.Should().BeEmpty();
        statuses.Should().BeEmpty();
    }

    /// <summary>
    /// 中断要求が失敗（無応答のタイムアウト等）したら App Server を停止し、実行中ターンを失敗完了させる
    /// ことを検証する。停止までしないとターンが実行中のまま残る。
    /// </summary>
    [Fact(DisplayName = "中断要求の失敗はクライアントを停止しターンを失敗完了へ倒す")]
    public async Task InterruptAsync_WhenRequestFails_StopsClientAndFailsTurn()
    {
        var client = new FakeCodexAppServerClient
        {
            InterruptException = new TimeoutException("応答がありません"),
        };
        var engine = new CodexChatEngine(
            client,
            new RecordingToolHost(),
            new SyncUiDispatcher(),
            ErDesignProfile.ErDesign
        );
        var results = new List<ErChatTurnResult>();
        engine.TurnCompleted += (_, r) => results.Add(r);

        await engine.StartConversationAsync(TestContext.Current.CancellationToken);
        await engine.SendAsync("やあ", TestContext.Current.CancellationToken);
        await engine.InterruptAsync(TestContext.Current.CancellationToken);

        client.StopCount.Should().Be(1);
        results.Should().ContainSingle();
        results[0].Success.Should().BeFalse();
        results[0].Error.Should().Be(QuickER.AI.Resources.Strings.Codex_InterruptForcedStop);

        // 停止したのでサーバー内の状態は失われている＝次の送信はスレッドから開き直す
        await engine.SendAsync("もう一度", TestContext.Current.CancellationToken);
        client.StartThreadCount.Should().Be(2);
    }

    /// <summary>強制停止そのものが失敗しても、ターンは必ず失敗完了させることを検証する</summary>
    /// <remarks>停止に失敗した時点でそれ以上の手段が無いため、せめて実行中表示だけは解く</remarks>
    [Fact(DisplayName = "中断失敗時の停止がさらに失敗してもターンは失敗完了する")]
    public async Task InterruptAsync_WhenStopAlsoFails_StillFailsTurn()
    {
        var client = new FakeCodexAppServerClient
        {
            InterruptException = new TimeoutException("応答がありません"),
            StopException = new InvalidOperationException("停止できません"),
        };
        var engine = new CodexChatEngine(
            client,
            new RecordingToolHost(),
            new SyncUiDispatcher(),
            ErDesignProfile.ErDesign
        );
        var results = new List<ErChatTurnResult>();
        engine.TurnCompleted += (_, r) => results.Add(r);

        await engine.StartConversationAsync(TestContext.Current.CancellationToken);
        await engine.SendAsync("やあ", TestContext.Current.CancellationToken);
        await engine.InterruptAsync(TestContext.Current.CancellationToken);

        client.StopCount.Should().Be(1);
        results.Should().ContainSingle();
        results[0].Success.Should().BeFalse();
        results[0].Error.Should().Be(QuickER.AI.Resources.Strings.Codex_InterruptForcedStop);
    }

    /// <summary>接続断が二重に届いてもターン完了は 1 回だけであることを検証する（実行中フラグの回帰固定）</summary>
    [Fact(DisplayName = "接続断の二重通知でも TurnCompleted は 1 回")]
    public async Task Disconnected_RaisedTwice_CompletesTurnOnce()
    {
        var client = new FakeCodexAppServerClient();
        var engine = new CodexChatEngine(
            client,
            new RecordingToolHost(),
            new SyncUiDispatcher(),
            ErDesignProfile.ErDesign
        );
        var results = new List<ErChatTurnResult>();
        engine.TurnCompleted += (_, r) => results.Add(r);

        await engine.StartConversationAsync(TestContext.Current.CancellationToken);
        await engine.SendAsync("やあ", TestContext.Current.CancellationToken);
        client.RaiseDisconnected();
        client.RaiseDisconnected();

        results.Should().ContainSingle();
    }

    /// <summary>
    /// StartTurnAsync が <see cref="OperationCanceledException"/> を投げたら、中断として
    /// Success=false・Error=null（ChatTurnEngine / <see cref="IClaudeCodeClient.RunTurnAsync"/> と
    /// 同じ規約）で完了することを検証する（B4: 誤ってエラー扱いに畳まれていない回帰固定）。
    /// </summary>
    [Fact(DisplayName = "ターン送信の OperationCanceledException は中断として畳まれる")]
    public async Task SendAsync_StartTurnThrowsOperationCanceledException_CompletesAsInterrupted()
    {
        var client = new FakeCodexAppServerClient
        {
            StartTurnException = new OperationCanceledException(),
        };
        var engine = new CodexChatEngine(
            client,
            new RecordingToolHost(),
            new SyncUiDispatcher(),
            ErDesignProfile.ErDesign
        );
        var results = new List<ErChatTurnResult>();
        engine.TurnCompleted += (_, r) => results.Add(r);

        await engine.StartConversationAsync(TestContext.Current.CancellationToken);
        await engine.SendAsync("やあ", TestContext.Current.CancellationToken);

        results.Should().ContainSingle();
        results[0].Success.Should().BeFalse();
        results[0].Error.Should().BeNull();
    }

    /// <summary>対照: 一般例外は従来どおりメッセージ付きの失敗として完了することを検証する</summary>
    [Fact(DisplayName = "ターン送信の一般例外はメッセージ付きの失敗として完了する")]
    public async Task SendAsync_StartTurnThrowsGeneralException_CompletesWithErrorMessage()
    {
        var client = new FakeCodexAppServerClient
        {
            StartTurnException = new InvalidOperationException("boom"),
        };
        var engine = new CodexChatEngine(
            client,
            new RecordingToolHost(),
            new SyncUiDispatcher(),
            ErDesignProfile.ErDesign
        );
        var results = new List<ErChatTurnResult>();
        engine.TurnCompleted += (_, r) => results.Add(r);

        await engine.StartConversationAsync(TestContext.Current.CancellationToken);
        await engine.SendAsync("やあ", TestContext.Current.CancellationToken);

        results.Should().ContainSingle();
        results[0].Success.Should().BeFalse();
        results[0].Error.Should().Be("boom");
    }

    /// <summary>
    /// ToolActivityReceived の購読側が例外を投げても、応答送信は必ず行われることを検証する
    /// （軽微 b: 活動通知は応答送信より先に呼ばれるため、素通しすると AI が結果を待ち続ける）。
    /// </summary>
    [Fact(DisplayName = "活動通知の購読側の例外があっても応答送信は行われる")]
    public void DynamicToolCall_ActivitySubscriberThrows_StillResponds()
    {
        var client = new FakeCodexAppServerClient();
        var host = new RecordingToolHost();
        var engine = new CodexChatEngine(
            client,
            host,
            new SyncUiDispatcher(),
            ErDesignProfile.ErDesign
        );
        engine.ToolActivityReceived += (_, _) => throw new InvalidOperationException("boom");

        client.RaiseDynamicToolCall(BuildToolCall("add_entity", "{\"table_name\":\"Book\"}"));

        client.RespondToolCount.Should().Be(1);
        client.LastToolSuccess.Should().BeTrue();
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
