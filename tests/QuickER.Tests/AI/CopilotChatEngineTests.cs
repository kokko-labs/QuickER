using AwesomeAssertions;
using QuickER.AI;
using QuickER.AI.Chat;
using QuickER.AI.Resources;

namespace QuickER.Tests.AI;

/// <summary>
/// <see cref="CopilotChatEngine"/> の状態遷移（未検出／未ログイン／ログイン済み）と、
/// クライアント通知の共通イベントへの変換（差分・ツール要求・完了・中断・エラー）を検証するテストクラス。
/// 実 CLI は起動せず <see cref="FakeCopilotRuntimeClient"/> のシームで検証する。
/// </summary>
public class CopilotChatEngineTests
{
    /// <summary>UI スレッドへのマーシャリングを同期実行で代替するテスト用ディスパッチャ</summary>
    private sealed class SyncUiDispatcher : IUiDispatcher
    {
        public T Invoke<T>(Func<T> func) => func();
    }

    private static CopilotChatEngine CreateEngine(
        FakeCopilotRuntimeClient client,
        IErDiagramToolHost? toolHost = null
    ) => new(client, toolHost, new SyncUiDispatcher(), ErDesignProfile.ErDesign);

    /// <summary>接続後にログイン済み・モデル一覧取得済みとなり送信可能になることを検証する</summary>
    [Fact(DisplayName = "接続でログイン済み・モデル列挙・Ready になる")]
    public async Task Initialize_LoggedIn_BecomesReadyWithModels()
    {
        var client = new FakeCopilotRuntimeClient();
        var engine = CreateEngine(client);

        await engine.InitializeAsync(TestContext.Current.CancellationToken);

        engine.IsReady.Should().BeTrue();
        engine.StatusLevel.Should().Be(ConnectionHealth.Ready);
        engine.StatusSummary.Should().Contain("octocat");
        engine.AvailableModels.Should().Equal("gpt-5", "claude-sonnet-4.5");

        await engine.DisposeAsync();
    }

    /// <summary>未ログインなら赤＋ /login 案内になり、モデル列挙もしないことを検証する</summary>
    [Fact(DisplayName = "未ログインは NeedsAction＋/login 案内でモデルは空")]
    public async Task Initialize_NotLoggedIn_NeedsActionWithoutModels()
    {
        var client = new FakeCopilotRuntimeClient
        {
            AuthInfo = new CopilotAuthInfo(false, string.Empty, string.Empty, string.Empty),
        };
        var engine = CreateEngine(client);

        await engine.InitializeAsync(TestContext.Current.CancellationToken);

        engine.IsReady.Should().BeFalse();
        engine.StatusLevel.Should().Be(ConnectionHealth.NeedsAction);
        engine.StatusSummary.Should().Be(Strings.Copilot_Status_NotLoggedIn);
        engine.Guidance.Should().Contain("/login");
        engine.AvailableModels.Should().BeEmpty();

        await engine.DisposeAsync();
    }

    /// <summary>認証状態を取得できないときは「確認できない」状態（赤）になることを検証する</summary>
    [Fact(DisplayName = "認証状態の取得失敗は Inconclusive")]
    public async Task Initialize_AuthQueryFails_BecomesInconclusive()
    {
        var client = new FakeCopilotRuntimeClient
        {
            AuthError = new InvalidOperationException("no auth"),
        };
        var engine = CreateEngine(client);

        await engine.InitializeAsync(TestContext.Current.CancellationToken);

        engine.IsReady.Should().BeFalse();
        engine.StatusLevel.Should().Be(ConnectionHealth.NeedsAction);
        engine.StatusSummary.Should().Be(Strings.Copilot_Status_Inconclusive);

        await engine.DisposeAsync();
    }

    /// <summary>ツールホストがあればセッションへ ER 設計ツールと設計ルールが渡ることを検証する</summary>
    [Fact(DisplayName = "会話開始で ER 設計ツールと設計ルールを渡す")]
    public async Task StartConversation_PassesToolsAndInstructions()
    {
        var client = new FakeCopilotRuntimeClient();
        var engine = CreateEngine(client, new RecordingToolHost());
        engine.Model = "gpt-5";

        await engine.InitializeAsync(TestContext.Current.CancellationToken);
        await engine.StartConversationAsync(TestContext.Current.CancellationToken);

        var options = client.LastSessionOptions;
        options.Should().NotBeNull();
        options!.Model.Should().Be("gpt-5");
        options.Tools.Should().NotBeEmpty();
        options.Instructions.Should().NotBeEmpty();

        await engine.DisposeAsync();
    }

    /// <summary>
    /// チャットのセッションが「作業フォルダなし・組込みツール不許可」のままであることを検証する。
    /// </summary>
    /// <remarks>
    /// モックプロジェクト生成のためにシーム（<see cref="CopilotSessionOptions"/>）へ足した
    /// 作業フォルダ・組込みツール許可が、チャット経路の既定値を動かしていないことを固定する退行防止。
    /// </remarks>
    [Theory(DisplayName = "チャットのセッションは作業フォルダなし・組込みツール不許可")]
    [InlineData(true)]
    [InlineData(false)]
    public async Task StartConversation_KeepsWorkspaceToolsDisabled(bool withToolHost)
    {
        var client = new FakeCopilotRuntimeClient();
        var engine = CreateEngine(client, withToolHost ? new RecordingToolHost() : null);

        await engine.InitializeAsync(TestContext.Current.CancellationToken);
        await engine.StartConversationAsync(TestContext.Current.CancellationToken);

        client.LastSessionOptions!.WorkingDirectory.Should().BeEmpty();
        client.LastSessionOptions!.AllowWorkspaceTools.Should().BeFalse();

        await engine.DisposeAsync();
    }

    /// <summary>ツールホストが無ければツール・設計ルールを渡さない（＝ツール無効）ことを検証する</summary>
    [Fact(DisplayName = "ツールホスト無しならツールを渡さない")]
    public async Task StartConversation_WithoutToolHost_PassesNoTools()
    {
        var client = new FakeCopilotRuntimeClient();
        var engine = CreateEngine(client);

        await engine.InitializeAsync(TestContext.Current.CancellationToken);
        await engine.StartConversationAsync(TestContext.Current.CancellationToken);

        client.LastSessionOptions!.Tools.Should().BeEmpty();
        client.LastSessionOptions!.Instructions.Should().BeEmpty();

        await engine.DisposeAsync();
    }

    /// <summary>
    /// 送信の途中で接続が切れて（クライアントが接続を捨てて）ターンが失敗しても、送信できる状態を保つことを検証する。
    /// 接続し直すのは次の送信なので、ここで送信できなくすると接続し直す経路に届かない。
    /// </summary>
    [Fact(DisplayName = "送信中に接続が切れて失敗したターンの後も IsReady は true のまま")]
    public async Task SendAsync_ConnectionLostDuringSend_StaysReady()
    {
        var client = new FakeCopilotRuntimeClient();
        var engine = CreateEngine(client);
        ErChatTurnResult? completed = null;
        engine.TurnCompleted += (_, r) => completed = r;

        await engine.InitializeAsync(TestContext.Current.CancellationToken);
        await engine.StartConversationAsync(TestContext.Current.CancellationToken);
        client.FailNextSendWithConnectionLost();
        await engine.SendAsync("やあ", TestContext.Current.CancellationToken);

        completed.Should().NotBeNull();
        completed!.Value.Success.Should().BeFalse();
        completed!.Value.Error.Should().StartWith(Strings.Copilot_Disconnected);
        client.IsStarted.Should().BeFalse("接続が切れた失敗でクライアントは接続を捨てる");
        engine.IsReady.Should().BeTrue();

        await engine.DisposeAsync();
    }

    /// <summary>接続が切れた後の送信で起動し直せなかったら、送信できない状態（未接続）へ落ちることを検証する</summary>
    [Fact(
        DisplayName = "接続が切れた後の接続し直しで起動に失敗したら IsReady は false で NeedsAction"
    )]
    public async Task SendAsync_ReconnectStartFails_IsNotReady()
    {
        var client = new FakeCopilotRuntimeClient();
        var engine = CreateEngine(client);

        await engine.InitializeAsync(TestContext.Current.CancellationToken);
        await engine.StartConversationAsync(TestContext.Current.CancellationToken);
        client.FailNextSendWithConnectionLost();
        await engine.SendAsync("1 回目", TestContext.Current.CancellationToken);
        engine.IsReady.Should().BeTrue();

        client.StartError = new InvalidOperationException("restart fails");
        await engine.SendAsync("2 回目", TestContext.Current.CancellationToken);

        engine.IsReady.Should().BeFalse();
        engine.StatusLevel.Should().Be(ConnectionHealth.NeedsAction);
        engine.StatusSummary.Should().Be(Strings.Copilot_Status_NotConnected);

        await engine.DisposeAsync();
    }

    /// <summary>接続が切れた後の送信で copilot CLI が見つからなくなっていたら、送信できない状態へ落ちることを検証する</summary>
    [Fact(DisplayName = "接続が切れた後の接続し直しで CLI が見つからなければ IsReady は false")]
    public async Task SendAsync_ReconnectCliMissing_IsNotReady()
    {
        var client = new FakeCopilotRuntimeClient();
        var engine = CreateEngine(client);

        await engine.InitializeAsync(TestContext.Current.CancellationToken);
        await engine.StartConversationAsync(TestContext.Current.CancellationToken);
        client.FailNextSendWithConnectionLost();
        await engine.SendAsync("1 回目", TestContext.Current.CancellationToken);
        engine.IsReady.Should().BeTrue();

        client.Available = false;
        await engine.SendAsync("2 回目", TestContext.Current.CancellationToken);

        engine.IsReady.Should().BeFalse();
        engine.IsCliMissing.Should().BeTrue();
        engine.StatusSummary.Should().Be(Strings.Copilot_Status_NotFound);

        await engine.DisposeAsync();
    }

    /// <summary>接続の見張りの間隔を指定してエンジンを作る（見張りのテストは短く、それ以外は見張りが走らない長さにする）</summary>
    private static CopilotChatEngine CreateEngine(
        FakeCopilotRuntimeClient client,
        TimeSpan connectionCheckInterval
    ) =>
        new(
            client,
            null,
            new SyncUiDispatcher(),
            ErDesignProfile.ErDesign,
            connectionCheckInterval
        );

    /// <summary>見張りのテストで使う短い間隔</summary>
    private static readonly TimeSpan ShortCheckInterval = TimeSpan.FromMilliseconds(10);

    /// <summary>完了の通知を待つ上限（全件実行の負荷でも十分に長くし、時間そのものは表明しない）</summary>
    private static readonly TimeSpan CompletionWaitLimit = TimeSpan.FromSeconds(30);

    /// <summary>次の <see cref="CopilotChatEngine.TurnCompleted"/> を受け取る（届いた回数も数える）</summary>
    private sealed class TurnCompletionProbe
    {
        private readonly TaskCompletionSource<ErChatTurnResult> _first = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        private int _count;

        public TurnCompletionProbe(CopilotChatEngine engine) =>
            engine.TurnCompleted += (_, result) =>
            {
                Interlocked.Increment(ref _count);
                _first.TrySetResult(result);
            };

        /// <summary>届いた回数</summary>
        public int Count => Volatile.Read(ref _count);

        /// <summary>最初の完了の通知を待つ（上限までに届かなければ失敗）</summary>
        public Task<ErChatTurnResult> WaitAsync() =>
            _first.Task.WaitAsync(CompletionWaitLimit, TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// 応答の途中で copilot ランタイムが止まる（以後のイベントが来ない）と、ターンの実行中の見張りが接続断を見つけて
    /// ターンを失敗で終えることを検証する。接続し直すのは次の送信なので、送信できる状態は保つ。
    /// </summary>
    [Fact(
        DisplayName = "応答の途中で接続が切れると、見張りがターンを失敗で終え、IsReady は true のまま"
    )]
    public async Task ConnectionWatch_ConnectionLostMidStream_CompletesTurnAsFailure()
    {
        var client = new FakeCopilotRuntimeClient();
        var engine = CreateEngine(client, ShortCheckInterval);
        var probe = new TurnCompletionProbe(engine);

        await engine.InitializeAsync(TestContext.Current.CancellationToken);
        await engine.StartConversationAsync(TestContext.Current.CancellationToken);
        await engine.SendAsync("長い応答を", TestContext.Current.CancellationToken);
        client.RaiseDelta("途中まで");
        client.FailPingsWithConnectionLost();

        var result = await probe.WaitAsync();

        result.Success.Should().BeFalse();
        result.Error.Should().StartWith(Strings.Copilot_Disconnected);
        result.Error.Should().Contain("The JSON-RPC connection was lost.");
        client.IsStarted.Should().BeFalse("接続が切れた失敗でクライアントは接続を捨てる");
        engine.IsReady.Should().BeTrue("次の送信で接続し直す");

        await engine.DisposeAsync();
    }

    /// <summary>
    /// 見張りの Ping が失敗しても、接続が生きている限りターンを終えずに次の回へ進むことを検証する。
    /// 通常のエラー応答（SDK が IOException に包む場合を含む）では接続も捨てない。
    /// </summary>
    /// <param name="isErrorResponse">true ならランタイムの通常のエラー応答、false なら接続が生きたままの一時的な例外</param>
    [Theory(
        DisplayName = "見張りの Ping が失敗しても接続が生きていれば、ターンを終えず次の回へ進む（一時的な例外・通常のエラー応答）"
    )]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ConnectionWatch_PingFails_KeepsTurnAndLiveConnection(bool isErrorResponse)
    {
        var client = new FakeCopilotRuntimeClient
        {
            PingError = isErrorResponse
                ? FakeCopilotRuntimeClient.ErrorResponse("ping not allowed")
                : new InvalidOperationException("transient"),
        };
        var engine = CreateEngine(client, ShortCheckInterval);
        var probe = new TurnCompletionProbe(engine);

        await engine.InitializeAsync(TestContext.Current.CancellationToken);
        await engine.StartConversationAsync(TestContext.Current.CancellationToken);
        await engine.SendAsync("やあ", TestContext.Current.CancellationToken);

        SpinWait
            .SpinUntil(() => client.PingCallCount >= 3, CompletionWaitLimit)
            .Should()
            .BeTrue("見張りは失敗の後も次の回へ進む");
        probe.Count.Should().Be(0);
        client.IsStarted.Should().BeTrue("接続が生きている失敗では接続を捨てない");

        client.RaiseIdle();
        (await probe.WaitAsync()).Success.Should().BeTrue();

        await engine.DisposeAsync();
    }

    /// <summary>
    /// 見張りがターンの終わり（アイドル）とエンジンの破棄で止まること、見張りが終えたターンへ後からアイドルが来ても
    /// 完了の通知が 1 回だけであることを検証する。
    /// </summary>
    [Fact(DisplayName = "見張りはターンの終わりと破棄で止まり、完了の通知は 1 回だけ")]
    public async Task ConnectionWatch_StopsWithTurnAndDispose_AndCompletesOnce()
    {
        var client = new FakeCopilotRuntimeClient();
        var engine = CreateEngine(client, ShortCheckInterval);

        await engine.InitializeAsync(TestContext.Current.CancellationToken);
        await engine.StartConversationAsync(TestContext.Current.CancellationToken);

        // アイドルで終わったターンの見張りは止まる
        await engine.SendAsync("1 回目", TestContext.Current.CancellationToken);
        var firstWatch = engine.ConnectionWatchTask;
        client.RaiseIdle();
        await firstWatch.WaitAsync(CompletionWaitLimit, TestContext.Current.CancellationToken);

        // 見張りが接続断で終えたターンへ、後からアイドルが来ても 2 回目の通知は出ない
        var probe = new TurnCompletionProbe(engine);
        await engine.SendAsync("2 回目", TestContext.Current.CancellationToken);
        client.FailPingsWithConnectionLost();
        await probe.WaitAsync();
        client.RaiseIdle();
        probe.Count.Should().Be(1);

        // 実行中のターンの見張りは、エンジンの破棄で止まる
        await engine.SendAsync("3 回目", TestContext.Current.CancellationToken);
        var lastWatch = engine.ConnectionWatchTask;
        var pingsAtSend = client.PingCallCount;
        SpinWait
            .SpinUntil(() => client.PingCallCount >= pingsAtSend + 2, CompletionWaitLimit)
            .Should()
            .BeTrue("Ping が成功する間は、見張りは次の回へ進む");
        probe.Count.Should().Be(1, "Ping が成功する間はターンを終えない");
        lastWatch.IsCompleted.Should().BeFalse("ターンの実行中は見張っている");
        await engine.DisposeAsync();
        lastWatch.IsCompleted.Should().BeTrue("破棄は見張りの終わりを待つ");
    }

    /// <summary>
    /// 中断がランタイムとの接続断で失敗したら、ランタイムがターンを終えることはもう無いので、ターンを失敗で終えることを検証する。
    /// 接続が生きたままの中断の失敗では、従来どおり状態文だけでターンは終えない（ランタイムが後でアイドルを返す）。
    /// </summary>
    [Fact(DisplayName = "中断が接続断で失敗したらターンを失敗で終え、接続が生きていれば終えない")]
    public async Task InterruptAsync_ConnectionLost_CompletesTurnAsFailure()
    {
        var client = new FakeCopilotRuntimeClient
        {
            AbortError = new InvalidOperationException("runtime busy"),
        };
        var engine = CreateEngine(client, Timeout.InfiniteTimeSpan);
        var probe = new TurnCompletionProbe(engine);

        await engine.InitializeAsync(TestContext.Current.CancellationToken);
        await engine.StartConversationAsync(TestContext.Current.CancellationToken);
        await engine.SendAsync("長い応答を", TestContext.Current.CancellationToken);

        // 接続が生きたままの失敗：ターンは終えない
        await engine.InterruptAsync(TestContext.Current.CancellationToken);
        probe.Count.Should().Be(0);

        // 接続断の失敗：ターンを失敗で終える
        client.FailNextAbortWithConnectionLost();
        await engine.InterruptAsync(TestContext.Current.CancellationToken);

        probe.Count.Should().Be(1);
        var result = await probe.WaitAsync();
        result.Success.Should().BeFalse();
        result.Error.Should().StartWith(Strings.Copilot_Disconnected);
        result.Error.Should().Contain("The JSON-RPC connection was lost.");
        engine.IsReady.Should().BeTrue("次の送信で接続し直す");

        await engine.DisposeAsync();
    }

    /// <summary>ターン失敗の表示文言（要約＋改行＋詳細: 原因）を組み立てる</summary>
    private static string ExpectedTurnFailed(string detail) =>
        Strings.Copilot_TurnFailed + Environment.NewLine + Strings.Ai_DetailLabel + detail;

    /// <summary>
    /// 送信にランタイムが通常のエラー応答を返したら（SDK が IOException に包んでも）、接続を捨てずに通常の失敗の文言で
    /// 終え、次の送信は同じ会話のまま続くことを検証する
    /// </summary>
    [Fact(DisplayName = "送信が通常のエラー応答で失敗しても接続を捨てず、次の送信は同じ会話で続く")]
    public async Task SendFailure_ErrorResponse_KeepsConnectionAndConversation()
    {
        var client = new FakeCopilotRuntimeClient();
        var engine = CreateEngine(client);

        var completions = new List<ErChatTurnResult>();
        engine.TurnCompleted += (_, r) => completions.Add(r);

        await engine.InitializeAsync(TestContext.Current.CancellationToken);
        await engine.StartConversationAsync(TestContext.Current.CancellationToken);
        var error = FakeCopilotRuntimeClient.ErrorResponse("model not available");
        client.SendError = error;
        await engine.SendAsync("1 回目", TestContext.Current.CancellationToken);

        completions.Should().ContainSingle();
        completions[0].Error.Should().Be(ExpectedTurnFailed(error.Message));
        client.IsStarted.Should().BeTrue("通常のエラー応答では接続を捨てない");

        client.SendError = null;
        client.AutoIdleAfterSend = true;
        await engine.SendAsync("2 回目", TestContext.Current.CancellationToken);

        client.StartSessionCallCount.Should().Be(1, "会話を開き直さない＝文脈を保つ");
        client.Sends.Select(send => send.Prompt).Should().Equal("2 回目");
        completions.Should().HaveCount(2);
        completions[1].Success.Should().BeTrue();

        await engine.DisposeAsync();
    }

    /// <summary>
    /// 送信の途中でセッションエラーがターンを完了させた後に、送信自体が例外で終わっても、
    /// TurnCompleted が 1 回しか届かないことを検証する（送信失敗の経路が実行中フラグを見ずに
    /// 通知すると、同じターンの失敗が 2 回届く）。
    /// </summary>
    [Fact(DisplayName = "送信中のエラーの後に送信が失敗しても完了通知は 1 回だけ")]
    public async Task SendFailure_AfterSessionError_CompletesOnce()
    {
        var client = new FakeCopilotRuntimeClient();
        var engine = CreateEngine(client);

        var completions = new List<ErChatTurnResult>();
        engine.TurnCompleted += (_, r) => completions.Add(r);

        await engine.InitializeAsync(TestContext.Current.CancellationToken);
        await engine.StartConversationAsync(TestContext.Current.CancellationToken);
        client.DuringSend = () =>
        {
            client.RaiseError("rate limited");
            throw new InvalidOperationException("send failed");
        };
        await engine.SendAsync("やあ", TestContext.Current.CancellationToken);

        completions.Should().ContainSingle();
        completions[0].Success.Should().BeFalse();
        completions[0].Error.Should().Be(ExpectedTurnFailed("rate limited"));

        await engine.DisposeAsync();
    }

    /// <summary>拒否した許可要求が会話へ活動として記録されることを検証する</summary>
    [Fact(DisplayName = "拒否した許可要求は活動として記録する")]
    public async Task PermissionDeclined_IsReportedAsActivity()
    {
        var client = new FakeCopilotRuntimeClient();
        var engine = CreateEngine(client);

        var activities = new List<ErChatToolActivity>();
        engine.ToolActivityReceived += (_, a) => activities.Add(a);

        await engine.InitializeAsync(TestContext.Current.CancellationToken);
        client.RaisePermissionDeclined("shell: rm");

        activities.Should().ContainSingle();
        activities[0].ToolName.Should().Be("shell: rm");
        activities[0].Result.Should().Be(Strings.Copilot_PermissionDeclined);
        activities[0].Success.Should().BeFalse();

        await engine.DisposeAsync();
    }

    /// <summary>中断要求がクライアントへ伝わることを検証する</summary>
    [Fact(DisplayName = "Interrupt はクライアントへ中断を伝える")]
    public async Task Interrupt_DelegatesToClient()
    {
        var client = new FakeCopilotRuntimeClient();
        var engine = CreateEngine(client);

        await engine.InitializeAsync(TestContext.Current.CancellationToken);
        await engine.StartConversationAsync(TestContext.Current.CancellationToken);
        await engine.InterruptAsync(TestContext.Current.CancellationToken);

        client.AbortCallCount.Should().Be(1);

        await engine.DisposeAsync();
    }

    /// <summary>会話未開始なら中断要求を出さない（不要な RPC を投げない）ことを検証する</summary>
    [Fact(DisplayName = "会話未開始の Interrupt は何もしない")]
    public async Task Interrupt_WithoutSession_DoesNothing()
    {
        var client = new FakeCopilotRuntimeClient();
        var engine = CreateEngine(client);

        await engine.InterruptAsync(TestContext.Current.CancellationToken);

        client.AbortCallCount.Should().Be(0);

        await engine.DisposeAsync();
    }

    /// <summary>会話開始に失敗したら送信は失敗完了になることを検証する</summary>
    [Fact(DisplayName = "会話開始に失敗した送信は失敗完了する")]
    public async Task SendAsync_WhenSessionCannotStart_FailsTurn()
    {
        var client = new FakeCopilotRuntimeClient
        {
            StartSessionError = new InvalidOperationException("no session"),
        };
        var engine = CreateEngine(client);

        ErChatTurnResult? completed = null;
        engine.TurnCompleted += (_, r) => completed = r;

        await engine.InitializeAsync(TestContext.Current.CancellationToken);
        await engine.SendAsync("やあ", TestContext.Current.CancellationToken);

        client.Sends.Should().BeEmpty();
        completed!.Value.Success.Should().BeFalse();
        completed!.Value.Error.Should().Be(Strings.Copilot_CouldNotStartConversation);

        await engine.DisposeAsync();
    }
}
