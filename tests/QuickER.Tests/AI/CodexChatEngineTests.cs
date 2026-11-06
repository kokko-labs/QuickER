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

    /// <summary>
    /// commandExecution / fileChange の承認要求は decision:"decline" で拒否され、その旨が活動として
    /// 可視化されることを検証する。ER 図の操作は dynamicTools 経路のため、自動承認する必要はない。
    /// </summary>
    [Theory(
        DisplayName = "Codex の decision 形の承認要求は decline で拒否され活動として通知される"
    )]
    [InlineData("item/commandExecution/requestApproval", "commandExecution")]
    [InlineData("item/fileChange/requestApproval", "fileChange")]
    public async Task ApprovalRequest_IsDeclinedAndReported(string method, string expectedLabel)
    {
        var client = new FakeCodexAppServerClient();
        var activities = await RaiseApprovalRequest(client, method);

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
    public async Task PermissionsApprovalRequest_IsDeclinedWithEmptyProfile()
    {
        var client = new FakeCodexAppServerClient();
        var activities = await RaiseApprovalRequest(client, "item/permissions/requestApproval");

        client.ApprovalDecisions.Should().BeEmpty();
        client.ApprovalResultJson.Should().Equal("""{"permissions":{},"scope":"turn"}""");
        AssertDeclinedActivity(activities, "permissions");
    }

    /// <summary>承認要求を 1 件発火し、通知された活動の一覧を返す</summary>
    private static async Task<List<ErChatToolActivity>> RaiseApprovalRequest(
        FakeCodexAppServerClient client,
        string method
    )
    {
        await using var engine = new CodexChatEngine(
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

    /// <summary>非 openai プロバイダーでは接続のみで送信可能（認証不要）になることを検証する</summary>
    [Fact(DisplayName = "非 openai プロバイダーは認証不要で IsReady になる")]
    public async Task IsReady_NonOpenAiProvider_RequiresNoAuth()
    {
        var client = new FakeCodexAppServerClient();
        await using var engine = new CodexChatEngine(
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
        await using var engine = new CodexChatEngine(
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
        await using var engine = new CodexChatEngine(
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
        await using var engine = new CodexChatEngine(
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

    /// <summary>
    /// ログイン済みの状態から、CLI 側でログアウトされた後の「再確認」で未ログインが返ったら、
    /// 認証モード・概要・常時案内がすべて未ログインへ戻ることを検証する
    /// （直前の状態がログイン済みでも、未ログインを取りこぼさない）。
    /// </summary>
    [Fact(DisplayName = "ログイン済みから再確認で未ログインが返ったら未ログイン表示へ戻る")]
    public async Task Refresh_AfterExternalLogout_ShowsNotLoggedIn()
    {
        var client = new FakeCodexAppServerClient
        {
            NextAccountInfo = new CodexAccountInfo
            {
                RequiresOpenAiAuth = true,
                AuthMode = CodexAuthMode.ChatGpt,
                Email = "user@example.com",
            },
        };
        await using var engine = new CodexChatEngine(
            client,
            new RecordingToolHost(),
            new SyncUiDispatcher(),
            ErDesignProfile.ErDesign
        );
        CodexAuthState? state = null;
        engine.AuthStateChanged += (_, s) => state = s;

        await engine.InitializeAsync(TestContext.Current.CancellationToken);
        engine.AuthMode.Should().Be(CodexAuthMode.ChatGpt);

        client.NextAccountInfo = new CodexAccountInfo
        {
            RequiresOpenAiAuth = true,
            AuthMode = CodexAuthMode.None,
        };
        await engine.RefreshAccountStateAsync(TestContext.Current.CancellationToken);

        engine.AuthMode.Should().Be(CodexAuthMode.None);
        engine.IsLoggedIn.Should().BeFalse();
        engine.AccountSummary.Should().Be(QuickER.AI.Resources.Strings.Codex_NotLoggedIn);
        engine.Guidance.Should().BeEmpty();
        state!.Value.AuthMode.Should().Be(CodexAuthMode.None);
    }

    /// <summary>
    /// アプリ内のログアウトが成功したら、account/updated の通知が来なくても
    /// エンジン自身が認証モードを未ログインへ戻すことを検証する。
    /// </summary>
    [Fact(DisplayName = "ログアウトは通知を待たずに認証モードを未ログインへ戻す")]
    public async Task Logout_ResetsAuthModeWithoutNotification()
    {
        var client = new FakeCodexAppServerClient
        {
            RaiseAccountUpdatedOnLogout = false,
            NextAccountInfo = new CodexAccountInfo
            {
                RequiresOpenAiAuth = true,
                AuthMode = CodexAuthMode.ApiKey,
            },
        };
        await using var engine = new CodexChatEngine(
            client,
            new RecordingToolHost(),
            new SyncUiDispatcher(),
            ErDesignProfile.ErDesign
        );
        CodexAuthState? state = null;
        engine.AuthStateChanged += (_, s) => state = s;

        await engine.InitializeAsync(TestContext.Current.CancellationToken);
        await engine.LogoutAsync(TestContext.Current.CancellationToken);

        engine.AuthMode.Should().Be(CodexAuthMode.None);
        engine.IsLoggedIn.Should().BeFalse();
        state!.Value.AuthMode.Should().Be(CodexAuthMode.None);
    }

    /// <summary>未検出から検出可能へ変わったら、「再確認」で未検出表示が解除され接続されることを検証する</summary>
    [Fact(DisplayName = "未検出から復帰したら再確認で接続できる")]
    public async Task Refresh_AfterCliBecomesAvailable_Connects()
    {
        var client = new FakeCodexAppServerClient { IsCliAvailable = false };
        await using var engine = new CodexChatEngine(
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
        await using var engine = new CodexChatEngine(
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
        await using var engine = new CodexChatEngine(
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
        await using var engine = new CodexChatEngine(
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
        await using var engine = new CodexChatEngine(
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

    /// <summary>引数を持たないツール要求でも、応答を必ず返すことを検証する</summary>
    /// <remarks>
    /// <c>arguments</c> を持たない要求では <c>Arguments</c> が <c>default</c>（Undefined）になり、
    /// <c>GetRawText()</c> が投げる。取り出しが例外の受け止めの外にあると、そこで抜けて
    /// <b>応答を送らないまま</b>終わり、AI がツール結果を待ち続けてターンが詰まる。
    /// </remarks>
    [Fact(DisplayName = "引数を持たないツール要求でも応答を返す")]
    public async Task DynamicToolCall_WithoutArguments_StillResponds()
    {
        var client = new FakeCodexAppServerClient();
        await using var engine = new CodexChatEngine(
            client,
            new RecordingToolHost(),
            new SyncUiDispatcher(),
            ErDesignProfile.ErDesign
        );

        await engine.StartConversationAsync(TestContext.Current.CancellationToken);

        // arguments を持たない要求（CodexAppServerClient の解析は Arguments = default にする）
        client.RaiseDynamicToolCall(
            new CodexDynamicToolCallRequest
            {
                RequestId = 1,
                ThreadId = "thr_test",
                TurnId = "turn_test",
                CallId = "call_1",
                Tool = "add_entity",
            }
        );

        client.RespondToolCount.Should().Be(1, "応答を送らないと AI が待ち続ける");
        client.LastToolSuccess.Should().BeFalse();
        client.LastToolResult.Should().StartWith("The tool 'add_entity' threw an exception");
    }

    /// <summary>完了したターンへは中断を送らないことを検証する</summary>
    /// <remarks>
    /// <c>_currentTurnId</c> は正常完了では消えないため、実行中かどうかを見ないと「完了した直後に
    /// 中断を押す」だけで完了済みのターンへ interrupt が飛ぶ。それがエラーなら App Server を止め、
    /// 会話の文脈を失って次の送信が新しい会話になる（完了済みターンへの interrupt を Codex が
    /// エラーにするかは未確認だが、どちらであってもこの防御は安全側に働く）。
    /// </remarks>
    [Fact(DisplayName = "完了したターンへは中断を送らない")]
    public async Task InterruptAsync_AfterTurnCompleted_DoesNotSendInterrupt()
    {
        var client = new FakeCodexAppServerClient();
        await using var engine = new CodexChatEngine(
            client,
            new RecordingToolHost(),
            new SyncUiDispatcher(),
            ErDesignProfile.ErDesign
        );

        await engine.StartConversationAsync(TestContext.Current.CancellationToken);
        await engine.SendAsync("やあ", TestContext.Current.CancellationToken);
        client.RaiseTurnCompleted("completed");

        await engine.InterruptAsync(TestContext.Current.CancellationToken);

        client.InterruptTurnCount.Should().Be(0, "実行中のターンが無ければ送らない");
        client.StopCount.Should().Be(0, "止める理由が無い");
    }

    /// <summary>新しいターンの開始中に中断が来ても、前のターンの Id へ送らないことを検証する</summary>
    /// <remarks>
    /// 実行中フラグを立ててから <c>StartTurnAsync</c> が新しい Id を返すまでの間、Id は前のターン
    /// （完了済み）のままになる。この隙間で中断が来ると、実行中フラグを見る防御だけでは通り抜けて
    /// しまうので、開始時に Id を消しておく。中断は効かないが会話は壊れない。
    /// </remarks>
    [Fact(DisplayName = "ターン開始中の中断は、前のターンの Id へ送らない")]
    public async Task InterruptAsync_WhileTurnIsStarting_DoesNotSendToPreviousTurn()
    {
        var client = new FakeCodexAppServerClient();
        await using var engine = new CodexChatEngine(
            client,
            new RecordingToolHost(),
            new SyncUiDispatcher(),
            ErDesignProfile.ErDesign
        );

        await engine.StartConversationAsync(TestContext.Current.CancellationToken);
        await engine.SendAsync("1 回目", TestContext.Current.CancellationToken);
        client.RaiseTurnCompleted("completed");

        // 2 回目の送信を「開始中」で止め、その最中に中断を掛ける
        var gate = new TaskCompletionSource();
        client.StartTurnGate = gate.Task;
        var sending = engine.SendAsync("2 回目", TestContext.Current.CancellationToken);

        // 「開始中」へ確実に到達してから中断する（最初の await で戻っただけの状態と区別する）
        await client.StartTurnEntered.Task.WaitAsync(
            TimeSpan.FromSeconds(10),
            TestContext.Current.CancellationToken
        );

        await engine.InterruptAsync(TestContext.Current.CancellationToken);

        client.InterruptTurnCount.Should().Be(0, "前のターンの Id へ送らない");
        client.StopCount.Should().Be(0);

        gate.SetResult();
        await sending;
    }

    /// <summary>中断の送信中にターンが完了したら、その失敗で強制停止しないことを検証する</summary>
    /// <remarks>
    /// 終わったターンへの中断が失敗しても壊すものは無い。ここで App Server を止めると、
    /// 正常に終わった会話の文脈を中断の失敗を理由に捨てることになる。
    /// </remarks>
    [Fact(DisplayName = "中断の送信中にターンが完了したら強制停止しない")]
    public async Task InterruptAsync_WhenTurnCompletesDuringRequest_DoesNotForceStop()
    {
        var client = new FakeCodexAppServerClient
        {
            InterruptException = new TimeoutException("応答がありません"),
        };
        await using var engine = new CodexChatEngine(
            client,
            new RecordingToolHost(),
            new SyncUiDispatcher(),
            ErDesignProfile.ErDesign
        );
        var statuses = new List<string>();

        await engine.StartConversationAsync(TestContext.Current.CancellationToken);
        await engine.SendAsync("やあ", TestContext.Current.CancellationToken);
        engine.StatusChanged += (_, m) => statuses.Add(m);

        // 中断要求を処理している最中にターンが完了する状況を作る
        client.OnInterruptRequested = () => client.RaiseTurnCompleted("completed");

        await engine.InterruptAsync(TestContext.Current.CancellationToken);

        client.InterruptTurnCount.Should().Be(1, "実行中だったので送ってはいる");
        client.StopCount.Should().Be(0, "完了済みなら失敗しても止めない");
        statuses.Should().BeEmpty("終わったターンの中断失敗を知らせても意味が無い");
    }

    /// <summary>
    /// 中断の応答を待つ間に次のターンが始まっていたら、その中断の失敗で強制停止しないことを検証する
    /// </summary>
    /// <remarks>
    /// 中断要求の応答待ち（実クライアントは 30 秒でタイムアウト）の間にターン 1 が完了し、
    /// ターン 2 が始まると、実行中フラグはターン 2 のものになる。フラグだけを見て止めると、
    /// 中断を頼んでいないターン 2 と会話の文脈を捨てる。照合するのは中断を送ったターンの Id。
    /// </remarks>
    [Fact(DisplayName = "中断の応答待ちの間に次のターンが始まっていたら強制停止しない")]
    public async Task InterruptAsync_WhenNextTurnStartsDuringRequest_DoesNotForceStop()
    {
        var gate = new TaskCompletionSource();
        var client = new FakeCodexAppServerClient
        {
            InterruptException = new TimeoutException("応答がありません"),
            InterruptGate = gate.Task,
        };
        await using var engine = new CodexChatEngine(
            client,
            new RecordingToolHost(),
            new SyncUiDispatcher(),
            ErDesignProfile.ErDesign
        );
        var results = new List<ErChatTurnResult>();
        var statuses = new List<string>();
        engine.TurnCompleted += (_, r) => results.Add(r);
        engine.StatusChanged += (_, m) => statuses.Add(m);

        await engine.StartConversationAsync(TestContext.Current.CancellationToken);
        await engine.SendAsync("1 回目", TestContext.Current.CancellationToken);

        // ターン 1 への中断を応答待ちで止める
        var interrupting = engine.InterruptAsync(TestContext.Current.CancellationToken);
        await client.InterruptEntered.Task.WaitAsync(
            TimeSpan.FromSeconds(10),
            TestContext.Current.CancellationToken
        );

        // 応答待ちの間にターン 1 が完了し、ターン 2 が始まる
        client.RaiseTurnCompleted("completed");
        client.NextTurnId = "turn_2";
        await engine.SendAsync("2 回目", TestContext.Current.CancellationToken);

        // ターン 1 への中断は失敗で返る
        gate.SetResult();
        await interrupting;

        client.LastInterruptTurnId.Should().Be("turn_test", "中断を送ったのはターン 1");
        client.StopCount.Should().Be(0, "ターン 2 は中断を頼まれていない");
        statuses
            .Should()
            .NotContain(
                string.Format(
                    QuickER.AI.Resources.Strings.Codex_InterruptFailed,
                    "応答がありません"
                )
            );

        // ターン 2 は実行中のまま＝完了通知がそのまま届く
        client.RaiseTurnCompleted("completed");
        results.Should().HaveCount(2);
        results.Should().OnlyContain(result => result.Success);
        client.StartThreadCount.Should().Be(1, "会話の文脈は保たれている");
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
        await using var engine = new CodexChatEngine(
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
        await using var engine = new CodexChatEngine(
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
        await using var engine = new CodexChatEngine(
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

    /// <summary>ターン中の error 通知は、要約に通知の文を詳細として添えた失敗で完了することを検証する</summary>
    [Fact(DisplayName = "ターン中の error 通知は要約＋詳細の失敗として完了する")]
    public async Task ErrorNotification_DuringTurn_IsSummaryWithDetail()
    {
        var client = new FakeCodexAppServerClient();
        await using var engine = new CodexChatEngine(
            client,
            new RecordingToolHost(),
            new SyncUiDispatcher(),
            ErDesignProfile.ErDesign
        );
        var results = new List<ErChatTurnResult>();
        engine.TurnCompleted += (_, r) => results.Add(r);

        await engine.SendAsync("やあ", TestContext.Current.CancellationToken);
        client.RaiseNotification(
            new CodexJsonRpcNotification
            {
                Method = "error",
                Params = JsonSerializer.SerializeToElement(
                    new { error = new { message = "stream disconnected" } }
                ),
            }
        );

        results.Should().ContainSingle();
        results[0].Success.Should().BeFalse();
        results[0]
            .Error.Should()
            .Be(
                QuickER.AI.Resources.Strings.Codex_TurnFailed
                    + Environment.NewLine
                    + QuickER.AI.Resources.Strings.Ai_DetailLabel
                    + "stream disconnected"
            );
    }

    /// <summary>
    /// 接続が実行中のあいだに重ねて呼んでも、App Server の起動は 1 回だけで、アカウント状態は
    /// ハンドシェイクの完了後にしか読まないことを検証する（タブの素早い往復・タブ切替と「再確認」の重なり）。
    /// </summary>
    [Fact(
        DisplayName = "接続中に重ねて接続しても起動は 1 回でハンドシェイク前にアカウントを読まない"
    )]
    public async Task Connect_WhileConnecting_SharesTheConnection()
    {
        var gate = new TaskCompletionSource();
        var client = new FakeCodexAppServerClient { StartGate = gate.Task };
        await using var engine = new CodexChatEngine(
            client,
            new RecordingToolHost(),
            new SyncUiDispatcher(),
            ErDesignProfile.ErDesign
        );

        var first = engine.ConnectAsync(TestContext.Current.CancellationToken);
        var second = engine.InitializeAsync(TestContext.Current.CancellationToken);

        client.StartCount.Should().Be(1);
        client.ReadAccountBeforeHandshakeCount.Should().Be(0);

        gate.SetResult();
        await first;
        await second;

        client.StartCount.Should().Be(1);
        client.ReadAccountCount.Should().Be(1);
        client.ReadAccountBeforeHandshakeCount.Should().Be(0);
    }
}
