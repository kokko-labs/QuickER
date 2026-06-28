using FluentAssertions;
using QuickER.AI;
using QuickER.Services.Chat;

namespace QuickER.Tests.Services.Chat;

/// <summary><see cref="ClaudeCodeChatEngine"/> のターン流れ（ストリーミング・成功/失敗・継続・可否）を検証するテストクラス</summary>
public class ClaudeCodeChatEngineTests
{
    /// <summary>UI スレッドへのマーシャリングを同期実行で代替するテスト用ディスパッチャ</summary>
    private sealed class SyncUiDispatcher : IUiDispatcher
    {
        public T Invoke<T>(Func<T> func) => func();
    }

    /// <summary>スクリプト化した結果を返すフェイク Claude Code クライアント</summary>
    private sealed class FakeClaudeCodeClient : IClaudeCodeClient
    {
        public bool Available { get; set; } = true;
        public string? ScriptedText { get; set; }
        public Queue<ClaudeCodeTurnOutcome> Outcomes { get; } = new();
        public List<string?> ResumeSessionIdsAtCall { get; } = new();
        public bool Interrupted { get; private set; }
        public ClaudeLoginProbeResult ProbeResult { get; set; } = ClaudeLoginProbeResult.LoggedIn;
        public int ProbeCallCount { get; private set; }

        public bool IsAvailable() => Available;

        public Task<ClaudeLoginProbeResult> ProbeLoginAsync(CancellationToken cancellationToken)
        {
            ProbeCallCount++;
            return Task.FromResult(ProbeResult);
        }

        public Task<ClaudeCodeTurnOutcome> RunTurnAsync(
            string prompt,
            string? resumeSessionId,
            ClaudeCodeLaunchOptions options,
            Action<string> onAssistantText,
            CancellationToken cancellationToken
        )
        {
            ResumeSessionIdsAtCall.Add(resumeSessionId);

            if (!string.IsNullOrEmpty(ScriptedText))
            {
                onAssistantText(ScriptedText);
            }

            var outcome =
                Outcomes.Count > 0
                    ? Outcomes.Dequeue()
                    : new ClaudeCodeTurnOutcome(true, null, null, false);
            return Task.FromResult(outcome);
        }

        public void Interrupt() => Interrupted = true;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private static ClaudeCodeChatEngine CreateEngine(FakeClaudeCodeClient client) =>
        new(client, toolHost: null, new SyncUiDispatcher());

    /// <summary>利用可能なら初期化後に送信可能となり、テキストを流して成功完了することを検証する</summary>
    [Fact(DisplayName = "送信はテキストを流し成功完了する")]
    public async Task SendAsync_StreamsAndCompletes()
    {
        var client = new FakeClaudeCodeClient { ScriptedText = "こんにちは" };
        client.Outcomes.Enqueue(new ClaudeCodeTurnOutcome(true, null, "s1", false));
        var engine = CreateEngine(client);

        var deltas = new List<string>();
        ErChatTurnResult? completed = null;
        engine.AssistantDeltaReceived += (_, d) => deltas.Add(d);
        engine.TurnCompleted += (_, r) => completed = r;

        await engine.InitializeAsync();
        engine.IsReady.Should().BeTrue();

        await engine.StartConversationAsync();
        await engine.SendAsync("やあ");

        deltas.Should().ContainSingle().Which.Should().Be("こんにちは");
        completed!.Value.Success.Should().BeTrue();

        await engine.DisposeAsync();
    }

    /// <summary>2 ターン目に前ターンのセッション ID が継続（resume）として渡されることを検証する</summary>
    [Fact(DisplayName = "2 ターン目は前ターンのセッション ID を resume へ渡す")]
    public async Task SendAsync_SecondTurn_PassesResumeSessionId()
    {
        var client = new FakeClaudeCodeClient();
        client.Outcomes.Enqueue(new ClaudeCodeTurnOutcome(true, null, "s1", false));
        client.Outcomes.Enqueue(new ClaudeCodeTurnOutcome(true, null, "s2", false));
        var engine = CreateEngine(client);

        await engine.InitializeAsync();
        await engine.StartConversationAsync();
        await engine.SendAsync("1 回目");
        await engine.SendAsync("2 回目");

        client.ResumeSessionIdsAtCall.Should().Equal(new string?[] { null, "s1" });

        await engine.DisposeAsync();
    }

    /// <summary>未ログイン結果はガイダンス付きの失敗として完了することを検証する</summary>
    [Fact(DisplayName = "未ログインはガイダンス付きで失敗完了する")]
    public async Task SendAsync_NotLoggedIn_FailsWithGuidance()
    {
        var client = new FakeClaudeCodeClient();
        client.Outcomes.Enqueue(new ClaudeCodeTurnOutcome(false, "Not logged in", null, true));
        var engine = CreateEngine(client);

        ErChatTurnResult? completed = null;
        engine.TurnCompleted += (_, r) => completed = r;

        await engine.InitializeAsync();
        await engine.StartConversationAsync();
        await engine.SendAsync("やあ");

        completed!.Value.Success.Should().BeFalse();
        completed!.Value.Error.Should().Contain("ログイン");

        await engine.DisposeAsync();
    }

    /// <summary>claude が見つからない場合は未準備となることを検証する</summary>
    [Fact(DisplayName = "claude 未インストール時は IsReady が false")]
    public async Task Initialize_WhenUnavailable_NotReady()
    {
        var client = new FakeClaudeCodeClient { Available = false };
        var engine = CreateEngine(client);

        await engine.InitializeAsync();

        engine.IsReady.Should().BeFalse();
        engine.StatusSummary.Should().Contain("見つかりません");

        await engine.DisposeAsync();
    }

    /// <summary>中断要求がクライアントへ伝わることを検証する</summary>
    [Fact(DisplayName = "Interrupt はクライアントへ中断を伝える")]
    public async Task Interrupt_DelegatesToClient()
    {
        var client = new FakeClaudeCodeClient();
        var engine = CreateEngine(client);

        await engine.InitializeAsync();
        await engine.InterruptAsync();

        client.Interrupted.Should().BeTrue();

        await engine.DisposeAsync();
    }

    /// <summary>検出済み・未プローブの初期状態は Pending（灰・未確認）であることを検証する</summary>
    [Fact(DisplayName = "初期状態は Pending（未確認）")]
    public async Task Initialize_WhenAvailable_IsPending()
    {
        var client = new FakeClaudeCodeClient();
        var engine = CreateEngine(client);

        await engine.InitializeAsync();

        engine.StatusLevel.Should().Be(ConnectionHealth.Pending);
        engine.StatusSummary.Should().Be("未確認");

        await engine.DisposeAsync();
    }

    /// <summary>再確認でログイン済みなら Ready（緑）になることを検証する</summary>
    [Fact(DisplayName = "再確認: ログイン済みは Ready")]
    public async Task Refresh_LoggedIn_BecomesReady()
    {
        var client = new FakeClaudeCodeClient { ProbeResult = ClaudeLoginProbeResult.LoggedIn };
        var engine = CreateEngine(client);

        await engine.InitializeAsync();
        await engine.RefreshAsync();

        client.ProbeCallCount.Should().Be(1);
        engine.StatusLevel.Should().Be(ConnectionHealth.Ready);

        await engine.DisposeAsync();
    }

    /// <summary>再確認で未ログインなら NeedsAction（赤）＋ /login 案内になることを検証する</summary>
    [Fact(DisplayName = "再確認: 未ログインは NeedsAction＋案内")]
    public async Task Refresh_NotLoggedIn_BecomesNeedsAction()
    {
        var client = new FakeClaudeCodeClient { ProbeResult = ClaudeLoginProbeResult.NotLoggedIn };
        var engine = CreateEngine(client);

        await engine.InitializeAsync();
        await engine.RefreshAsync();

        engine.StatusLevel.Should().Be(ConnectionHealth.NeedsAction);
        engine.Guidance.Should().Contain("/login");

        await engine.DisposeAsync();
    }

    /// <summary>claude 未検出時はプローブせず NeedsAction になることを検証する</summary>
    [Fact(DisplayName = "再確認: 未検出はプローブせず NeedsAction")]
    public async Task Refresh_WhenUnavailable_NeedsActionWithoutProbe()
    {
        var client = new FakeClaudeCodeClient { Available = false };
        var engine = CreateEngine(client);

        await engine.InitializeAsync();
        await engine.RefreshAsync();

        client.ProbeCallCount.Should().Be(0);
        engine.StatusLevel.Should().Be(ConnectionHealth.NeedsAction);

        await engine.DisposeAsync();
    }

    /// <summary>送信成功でログイン済みと判明し Ready（緑）になることを検証する</summary>
    [Fact(DisplayName = "送信成功で Ready になる")]
    public async Task SendAsync_Success_BecomesReady()
    {
        var client = new FakeClaudeCodeClient();
        client.Outcomes.Enqueue(new ClaudeCodeTurnOutcome(true, null, "s1", false));
        var engine = CreateEngine(client);

        await engine.InitializeAsync();
        await engine.StartConversationAsync();
        await engine.SendAsync("やあ");

        engine.StatusLevel.Should().Be(ConnectionHealth.Ready);

        await engine.DisposeAsync();
    }
}
