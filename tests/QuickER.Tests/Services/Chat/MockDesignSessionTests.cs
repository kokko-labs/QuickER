using FluentAssertions;
using QuickER.AI;
using QuickER.Model;

namespace QuickER.Tests.Services.Chat;

/// <summary>
/// <see cref="MockDesignSession"/> のツール処理（save_mock_html）・HTML 確定通知・
/// フィードバック送信・初回プロンプト組み立てを検証するテストクラス。
/// </summary>
public class MockDesignSessionTests
{
    /// <summary>
    /// 送信内容を記録し、スクリプトされたツール呼び出しをツールホストへ橋渡しするフェイクエンジン。
    /// 実際の LLM は呼ばず、テストが仕込んだツール呼び出しだけを再生する。
    /// </summary>
    private sealed class FakeChatEngine : IErChatEngine
    {
        private readonly IErDiagramToolHost _toolHost;

        public FakeChatEngine(IErDiagramToolHost toolHost) => _toolHost = toolHost;

        public List<string> SentPrompts { get; } = new();

        /// <summary>次の SendAsync で再生するツール呼び出し（ツール名・引数 JSON）</summary>
        public (string Tool, string Args)? ScriptedToolCall { get; set; }

        /// <summary>直近のツール実行結果（テストからの検証用）</summary>
        public (string Result, bool Success)? LastToolResult { get; private set; }

        public event EventHandler<string>? AssistantDeltaReceived;
        public event EventHandler<ErChatToolActivity>? ToolActivityReceived;
        public event EventHandler<ErChatTurnResult>? TurnCompleted;
        public event EventHandler<string>? StatusChanged;

        public bool IsReady => true;

        public Task InitializeAsync(CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task StartConversationAsync(CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task SendAsync(string prompt, CancellationToken cancellationToken = default)
        {
            SentPrompts.Add(prompt);

            // 実エンジンと同様に、ステータス通知と応答断片を 1 つ流す（イベント転送の検証も兼ねる）
            StatusChanged?.Invoke(this, "生成中...");
            AssistantDeltaReceived?.Invoke(this, "了解しました。");

            if (ScriptedToolCall is { } call)
            {
                var result = _toolHost.Execute(call.Tool, call.Args);
                LastToolResult = result;
                ToolActivityReceived?.Invoke(
                    this,
                    new ErChatToolActivity(call.Tool, result.Result, result.Success)
                );
                ScriptedToolCall = null;
            }

            TurnCompleted?.Invoke(this, new ErChatTurnResult(true, null));
            return Task.CompletedTask;
        }

        public Task InterruptAsync(CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private const string ValidHtml =
        "<!DOCTYPE html><html lang=\"ja\"><head><style>body{}</style></head>"
        + "<body><h1>顧客一覧</h1></body></html>";

    /// <summary>save_mock_html 呼び出しで CurrentHtml が更新され HtmlUpdated が発火することを検証する</summary>
    [Fact(DisplayName = "save_mock_html で CurrentHtml 更新・HtmlUpdated 発火")]
    public async Task Execute_SaveMockHtml_UpdatesCurrentHtmlAndRaisesEvent()
    {
        MockDesignSession session = null!;
        var engine = new FakeChatEngine(new LazyToolHost(() => session));
        session = new MockDesignSession(engine);

        var args =
            $"{{\"html\":{System.Text.Json.JsonSerializer.Serialize(ValidHtml)},\"revision_note\":\"初版\"}}";
        engine.ScriptedToolCall = (MockDesignTools.SaveMockHtmlToolName, args);

        MockHtmlUpdate? update = null;
        session.HtmlUpdated += (_, u) => update = u;

        await session.StartAsync(new ErDiagram(), null, TestContext.Current.CancellationToken);

        session.CurrentHtml.Should().Be(ValidHtml);
        update.Should().NotBeNull();
        update!.Value.Html.Should().Be(ValidHtml);
        update.Value.RevisionNote.Should().Be("初版");
        engine.LastToolResult!.Value.Success.Should().BeTrue();
    }

    /// <summary>空文字 HTML は拒否され CurrentHtml が更新されないことを検証する</summary>
    [Fact(DisplayName = "空文字 HTML は拒否され CurrentHtml は更新されない")]
    public void Execute_EmptyHtml_IsRejected()
    {
        MockDesignSession session = null!;
        var engine = new FakeChatEngine(new LazyToolHost(() => session));
        session = new MockDesignSession(engine);

        var (result, success) = session.Execute(
            MockDesignTools.SaveMockHtmlToolName,
            "{\"html\":\"\"}"
        );

        success.Should().BeFalse();
        result.Should().Contain("空");
        session.CurrentHtml.Should().BeNull();
    }

    /// <summary>&lt;html&gt; を含まない断片は不完全として拒否されることを検証する</summary>
    [Fact(DisplayName = "html タグを含まない断片は拒否される")]
    public void Execute_HtmlWithoutHtmlTag_IsRejected()
    {
        var session = new MockDesignSession(new FakeChatEngine(new NullToolHost()));

        var (_, success) = session.Execute(
            MockDesignTools.SaveMockHtmlToolName,
            "{\"html\":\"<div>部分</div>\"}"
        );

        success.Should().BeFalse();
        session.CurrentHtml.Should().BeNull();
    }

    /// <summary>初回プロンプトにスキーマ記述とユーザー補足指示が含まれることを検証する</summary>
    [Fact(DisplayName = "初回プロンプトにスキーマと補足指示が含まれる")]
    public async Task StartAsync_SendsPromptWithSchemaAndInstructions()
    {
        MockDesignSession session = null!;
        var engine = new FakeChatEngine(new LazyToolHost(() => session));
        session = new MockDesignSession(engine);

        var diagram = new ErDiagram
        {
            Entities =
            {
                new Entity { TableName = "Customer", Description = "顧客" },
            },
        };

        await session.StartAsync(
            diagram,
            "モダンな配色にして",
            TestContext.Current.CancellationToken
        );

        engine.SentPrompts.Should().ContainSingle();
        engine.SentPrompts[0].Should().Contain("Customer");
        engine.SentPrompts[0].Should().Contain("モダンな配色にして");
    }

    /// <summary>
    /// エンジンファクトリ受け取りコンストラクタが、ツールホストをセッション自身へ解決して
    /// エンジンを生成し、save_mock_html を正しく処理することを検証する（相互依存の解消）。
    /// </summary>
    [Fact(DisplayName = "ファクトリ構築でセッション自身がツールホストになる")]
    public async Task FactoryConstructor_WiresSessionAsToolHost()
    {
        FakeChatEngine? captured = null;

        // ファクトリには本セッションへ解決されるツールホストが渡される。
        // ここではそのツールホストをフェイクエンジンへ流し込み、後段のツール呼び出しで検証する。
        var session = new MockDesignSession(toolHost => captured = new FakeChatEngine(toolHost));

        captured.Should().NotBeNull();

        var args =
            $"{{\"html\":{System.Text.Json.JsonSerializer.Serialize(ValidHtml)},\"revision_note\":\"初版\"}}";
        captured!.ScriptedToolCall = (MockDesignTools.SaveMockHtmlToolName, args);

        MockHtmlUpdate? update = null;
        session.HtmlUpdated += (_, u) => update = u;

        await session.StartAsync(new ErDiagram(), null, TestContext.Current.CancellationToken);

        // ツールホストがセッション自身へ解決され、CurrentHtml が更新される
        session.CurrentHtml.Should().Be(ValidHtml);
        update.Should().NotBeNull();
        update!.Value.RevisionNote.Should().Be("初版");
    }

    /// <summary>フィードバックターンがそのままエンジンへ送信されることを検証する</summary>
    [Fact(DisplayName = "フィードバックはエンジンへ送信される")]
    public async Task SendFeedbackAsync_ForwardsToEngine()
    {
        var engine = new FakeChatEngine(new NullToolHost());
        var session = new MockDesignSession(engine);

        var deltas = new List<string>();
        ErChatTurnResult? completed = null;
        session.AssistantDeltaReceived += (_, d) => deltas.Add(d);
        session.TurnCompleted += (_, r) => completed = r;

        await session.SendFeedbackAsync("列を減らして", TestContext.Current.CancellationToken);

        engine.SentPrompts.Should().ContainSingle().Which.Should().Be("列を減らして");
        // エンジンのイベントがセッション経由で転送されることも確認する
        deltas.Should().Contain("了解しました。");
        completed!.Value.Success.Should().BeTrue();
    }

    /// <summary>セッション確定後に遅延解決するツールホスト（フェイクエンジンにセッションを渡す循環を解く）</summary>
    private sealed class LazyToolHost : IErDiagramToolHost
    {
        private readonly Func<IErDiagramToolHost> _resolve;

        public LazyToolHost(Func<IErDiagramToolHost> resolve) => _resolve = resolve;

        public (string Result, bool Success) Execute(string toolName, string argumentsJson) =>
            _resolve().Execute(toolName, argumentsJson);
    }

    /// <summary>何もしないツールホスト（ツール呼び出しが発生しないケース用）</summary>
    private sealed class NullToolHost : IErDiagramToolHost
    {
        public (string Result, bool Success) Execute(string toolName, string argumentsJson) =>
            ("noop", true);
    }
}
