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

    /// <summary>copilot 未検出時は接続を試みず未検出（赤・インストール案内）になることを検証する</summary>
    [Fact(DisplayName = "copilot 未検出は接続せず NeedsAction＋インストール案内")]
    public async Task Initialize_WhenUnavailable_DoesNotStart()
    {
        var client = new FakeCopilotRuntimeClient { Available = false };
        var engine = CreateEngine(client);

        await engine.InitializeAsync(TestContext.Current.CancellationToken);

        client.IsStarted.Should().BeFalse();
        engine.IsReady.Should().BeFalse();
        engine.IsCliMissing.Should().BeTrue();
        engine.StatusLevel.Should().Be(ConnectionHealth.NeedsAction);
        engine.StatusSummary.Should().Be(Strings.Copilot_Status_NotFound);
        engine.Guidance.Should().Be(Strings.Copilot_Guidance_Install);

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

    /// <summary>未検出から検出可能へ復帰したら、再確認で未検出表示が解除されることを検証する</summary>
    [Fact(DisplayName = "再確認: 未検出から復帰すると Ready になる")]
    public async Task Refresh_AfterCliBecomesAvailable_Recovers()
    {
        var client = new FakeCopilotRuntimeClient { Available = false };
        var engine = CreateEngine(client);

        await engine.InitializeAsync(TestContext.Current.CancellationToken);
        engine.IsCliMissing.Should().BeTrue();

        client.Available = true;
        await engine.RefreshAsync(TestContext.Current.CancellationToken);

        engine.IsCliMissing.Should().BeFalse();
        engine.StatusLevel.Should().Be(ConnectionHealth.Ready);

        await engine.DisposeAsync();
    }

    /// <summary>接続に失敗したら理由付きの案内へ落ちることを検証する</summary>
    [Fact(DisplayName = "接続失敗は理由を案内に残す")]
    public async Task Initialize_StartFails_KeepsReasonInGuidance()
    {
        var client = new FakeCopilotRuntimeClient
        {
            StartError = new InvalidOperationException("boom"),
        };
        var engine = CreateEngine(client);

        await engine.InitializeAsync(TestContext.Current.CancellationToken);

        engine.IsReady.Should().BeFalse();
        engine.StatusLevel.Should().Be(ConnectionHealth.NeedsAction);
        engine.Guidance.Should().Contain("boom");

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

    /// <summary>会話未開始のまま送信すると、セッションを自動生成してから送ることを検証する</summary>
    [Fact(DisplayName = "会話未開始の送信はセッションを自動生成する")]
    public async Task SendAsync_WithoutConversation_StartsSession()
    {
        var client = new FakeCopilotRuntimeClient();
        var engine = CreateEngine(client);

        await engine.InitializeAsync(TestContext.Current.CancellationToken);
        await engine.SendAsync("やあ", TestContext.Current.CancellationToken);

        client.StartSessionCallCount.Should().Be(1);
        client.Sends.Should().ContainSingle().Which.Prompt.Should().Be("やあ");

        await engine.DisposeAsync();
    }

    /// <summary>差分がそのまま流れ、アイドル復帰で成功完了することを検証する</summary>
    [Fact(DisplayName = "差分を流しアイドルで成功完了する")]
    public async Task SendAsync_StreamsAndCompletesOnIdle()
    {
        var client = new FakeCopilotRuntimeClient();
        var engine = CreateEngine(client);

        var deltas = new List<string>();
        ErChatTurnResult? completed = null;
        engine.AssistantDeltaReceived += (_, d) => deltas.Add(d);
        engine.TurnCompleted += (_, r) => completed = r;

        await engine.InitializeAsync(TestContext.Current.CancellationToken);
        await engine.StartConversationAsync(TestContext.Current.CancellationToken);
        await engine.SendAsync("やあ", TestContext.Current.CancellationToken);

        client.RaiseDelta("こん");
        client.RaiseDelta("にちは");
        completed.Should().BeNull("アイドル前は完了していないはず");

        client.RaiseIdle();

        deltas.Should().Equal("こん", "にちは");
        completed!.Value.Success.Should().BeTrue();

        await engine.DisposeAsync();
    }

    /// <summary>ターン外のアイドル通知は完了として扱わない（二重完了を防ぐ）ことを検証する</summary>
    [Fact(DisplayName = "ターン外のアイドルは完了通知しない")]
    public async Task SessionIdle_OutsideTurn_IsIgnored()
    {
        var client = new FakeCopilotRuntimeClient();
        var engine = CreateEngine(client);

        var completions = new List<ErChatTurnResult>();
        engine.TurnCompleted += (_, r) => completions.Add(r);

        await engine.InitializeAsync(TestContext.Current.CancellationToken);
        await engine.StartConversationAsync(TestContext.Current.CancellationToken);
        await engine.SendAsync("やあ", TestContext.Current.CancellationToken);

        client.RaiseIdle();
        client.RaiseIdle();

        completions.Should().ContainSingle();

        await engine.DisposeAsync();
    }

    /// <summary>中断されたアイドルはエラー文言なしの失敗として完了することを検証する</summary>
    [Fact(DisplayName = "中断はエラーなしの失敗として完了する")]
    public async Task SessionIdle_Aborted_CompletesAsSilentFailure()
    {
        var client = new FakeCopilotRuntimeClient();
        var engine = CreateEngine(client);

        ErChatTurnResult? completed = null;
        engine.TurnCompleted += (_, r) => completed = r;

        await engine.InitializeAsync(TestContext.Current.CancellationToken);
        await engine.StartConversationAsync(TestContext.Current.CancellationToken);
        await engine.SendAsync("やあ", TestContext.Current.CancellationToken);

        client.RaiseIdle(aborted: true);

        completed!.Value.Success.Should().BeFalse();
        completed!.Value.Error.Should().BeNull();

        await engine.DisposeAsync();
    }

    /// <summary>ターン中のエラーはその場で失敗完了し、後続のアイドルで二重完了しないことを検証する</summary>
    [Fact(DisplayName = "ターン中のエラーは失敗完了し二重完了しない")]
    public async Task SessionError_DuringTurn_FailsOnce()
    {
        var client = new FakeCopilotRuntimeClient();
        var engine = CreateEngine(client);

        var completions = new List<ErChatTurnResult>();
        engine.TurnCompleted += (_, r) => completions.Add(r);

        await engine.InitializeAsync(TestContext.Current.CancellationToken);
        await engine.StartConversationAsync(TestContext.Current.CancellationToken);
        await engine.SendAsync("やあ", TestContext.Current.CancellationToken);

        client.RaiseError("rate limited");
        client.RaiseIdle();

        completions.Should().ContainSingle();
        completions[0].Success.Should().BeFalse();
        completions[0].Error.Should().Be("rate limited");

        await engine.DisposeAsync();
    }

    /// <summary>ツール呼び出し要求をツールホストで実行し、結果を返送・活動通知することを検証する</summary>
    [Fact(DisplayName = "ツール要求は実行して結果を返送する")]
    public async Task ToolCallRequested_ExecutesAndResponds()
    {
        var client = new FakeCopilotRuntimeClient();
        var toolHost = new RecordingToolHost();
        var engine = CreateEngine(client, toolHost);

        var activities = new List<ErChatToolActivity>();
        engine.ToolActivityReceived += (_, a) => activities.Add(a);

        await engine.InitializeAsync(TestContext.Current.CancellationToken);
        await engine.StartConversationAsync(TestContext.Current.CancellationToken);

        client.RaiseToolCall("req-1", "add_entity", "{\"name\":\"Customer\"}");

        toolHost.Calls.Should().ContainSingle();
        toolHost.Calls[0].Tool.Should().Be("add_entity");
        toolHost.Calls[0].Args.Should().Be("{\"name\":\"Customer\"}");

        activities.Should().ContainSingle();
        activities[0].ToolName.Should().Be("add_entity");
        activities[0].Success.Should().BeTrue();

        client.ToolResponses.Should().ContainSingle();
        client.ToolResponses[0].RequestId.Should().Be("req-1");
        client.ToolResponses[0].Success.Should().BeTrue();

        await engine.DisposeAsync();
    }

    /// <summary>ツールホストが無い状態でのツール要求は失敗として返送されることを検証する</summary>
    [Fact(DisplayName = "ツールホスト無しのツール要求は失敗を返送する")]
    public async Task ToolCallRequested_WithoutToolHost_RespondsFailure()
    {
        var client = new FakeCopilotRuntimeClient();
        var engine = CreateEngine(client);

        await engine.InitializeAsync(TestContext.Current.CancellationToken);
        await engine.StartConversationAsync(TestContext.Current.CancellationToken);

        client.RaiseToolCall("req-1", "add_entity", "{}");

        client.ToolResponses.Should().ContainSingle();
        client.ToolResponses[0].Success.Should().BeFalse();

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

    /// <summary>画像添付は同梱して送信できることを検証する</summary>
    [Fact(DisplayName = "画像添付は送信できる")]
    public async Task SendAsync_ImageAttachment_IsPassedThrough()
    {
        var client = new FakeCopilotRuntimeClient();
        var engine = CreateEngine(client);

        await engine.InitializeAsync(TestContext.Current.CancellationToken);
        await engine.StartConversationAsync(TestContext.Current.CancellationToken);

        var attachment = new ChatAttachment("a.png", ChatAttachmentKind.Image, "image/png", [1, 2]);
        await engine.SendAsync("これは？", [attachment], TestContext.Current.CancellationToken);

        client.Sends.Should().ContainSingle().Which.AttachmentCount.Should().Be(1);

        await engine.DisposeAsync();
    }

    /// <summary>画像以外の添付は分かる例外で弾かれることを検証する（無言で落とさない）</summary>
    [Fact(DisplayName = "画像以外の添付は NotSupportedException")]
    public async Task SendAsync_NonImageAttachment_Throws()
    {
        var client = new FakeCopilotRuntimeClient();
        var engine = CreateEngine(client);

        await engine.InitializeAsync(TestContext.Current.CancellationToken);

        var attachment = new ChatAttachment(
            "a.pdf",
            ChatAttachmentKind.Pdf,
            "application/pdf",
            [1, 2]
        );

        await Assert.ThrowsAsync<NotSupportedException>(() =>
            engine.SendAsync("これは？", [attachment], TestContext.Current.CancellationToken)
        );

        client.Sends.Should().BeEmpty();

        await engine.DisposeAsync();
    }

    /// <summary>添付範囲が画像のみであることを検証する</summary>
    [Fact(DisplayName = "添付範囲は画像のみ")]
    public async Task AttachmentSupport_IsImagesOnly()
    {
        var client = new FakeCopilotRuntimeClient();
        var engine = CreateEngine(client);

        engine.AttachmentSupport.Should().Be(AttachmentSupport.Images);

        await engine.DisposeAsync();
    }

    /// <summary>破棄でクライアントも破棄されることを検証する（子プロセスを残さない）</summary>
    [Fact(DisplayName = "Dispose はクライアントを破棄する")]
    public async Task DisposeAsync_DisposesClient()
    {
        var client = new FakeCopilotRuntimeClient();
        var engine = CreateEngine(client);

        await engine.InitializeAsync(TestContext.Current.CancellationToken);
        await engine.DisposeAsync();

        client.Disposed.Should().BeTrue();

        // 破棄後はイベント購読も外れており、遅れて届いた通知で完了イベントが飛ばない
        var completions = new List<ErChatTurnResult>();
        engine.TurnCompleted += (_, r) => completions.Add(r);
        client.RaiseIdle();
        completions.Should().BeEmpty();
    }
}
