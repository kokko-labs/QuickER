using FluentAssertions;
using QuickER.AI;
using QuickER.Services.Chat;

namespace QuickER.Tests.Services.Chat;

/// <summary><see cref="ChatTurnEngine"/> のツール呼び出しループ・ストリーミング・完了通知を検証するテストクラス</summary>
public class ChatTurnEngineTests
{
    /// <summary>UI スレッドへのマーシャリングを同期実行で代替するテスト用ディスパッチャ</summary>
    private sealed class SyncUiDispatcher : IUiDispatcher
    {
        public T Invoke<T>(Func<T> func) => func();
    }

    /// <summary>スクリプト化したアシスタント応答を順に返すフェイクドライバ</summary>
    private sealed class ScriptedTurnDriver : IChatTurnDriver
    {
        private readonly Queue<ChatAssistantTurn> _turns;

        public ScriptedTurnDriver(IEnumerable<ChatAssistantTurn> turns) =>
            _turns = new Queue<ChatAssistantTurn>(turns);

        /// <summary>各ターン実行時点の履歴件数を記録する</summary>
        public List<int> HistoryCountsAtCall { get; } = new();

        public Task<ChatAssistantTurn> RunAsync(
            IReadOnlyList<ChatHistoryItem> history,
            Action<string> onTextDelta,
            CancellationToken cancellationToken
        )
        {
            HistoryCountsAtCall.Add(history.Count);
            var turn = _turns.Dequeue();

            if (!string.IsNullOrEmpty(turn.Text))
            {
                onTextDelta(turn.Text);
            }

            return Task.FromResult(turn);
        }
    }

    /// <summary>ツール呼び出しを記録し、定型結果を返すフェイクホスト</summary>
    private sealed class RecordingToolHost : IErDiagramToolHost
    {
        public List<(string Tool, string Args)> Calls { get; } = new();

        public (string Result, bool Success) Execute(string toolName, string argumentsJson)
        {
            Calls.Add((toolName, argumentsJson));
            return ($"{toolName} 実行済み", true);
        }
    }

    private static ChatTurnEngine CreateEngine(
        ScriptedTurnDriver driver,
        RecordingToolHost host,
        bool isReady = true
    ) => new(driver, host, new SyncUiDispatcher(), () => isReady);

    /// <summary>ツール呼び出しの無いターンが、ストリーミングと成功完了で終わることを検証する</summary>
    [Fact(DisplayName = "ツール無しターンは delta を流し成功完了する")]
    public async Task SendAsync_NoToolCalls_StreamsAndCompletes()
    {
        var driver = new ScriptedTurnDriver([new ChatAssistantTurn("こんにちは", [])]);
        var host = new RecordingToolHost();
        var engine = CreateEngine(driver, host);

        var deltas = new List<string>();
        ErChatTurnResult? completed = null;
        engine.AssistantDeltaReceived += (_, d) => deltas.Add(d);
        engine.TurnCompleted += (_, r) => completed = r;

        await engine.StartConversationAsync(TestContext.Current.CancellationToken);
        await engine.SendAsync("やあ", TestContext.Current.CancellationToken);

        deltas.Should().ContainSingle().Which.Should().Be("こんにちは");
        host.Calls.Should().BeEmpty();
        completed.Should().NotBeNull();
        completed!.Value.Success.Should().BeTrue();
    }

    /// <summary>ツール要求ターン→ツール実行→完了ターンのループが正しく回ることを検証する</summary>
    [Fact(DisplayName = "ツール要求ターンはツールを実行し結果を履歴へ積んで継続する")]
    public async Task SendAsync_WithToolCall_ExecutesToolThenCompletes()
    {
        var driver = new ScriptedTurnDriver([
            new ChatAssistantTurn(
                string.Empty,
                [new ChatToolCallRequest("call_1", "add_entity", "{\"table_name\":\"Book\"}")]
            ),
            new ChatAssistantTurn("テーブルを追加しました", []),
        ]);
        var host = new RecordingToolHost();
        var engine = CreateEngine(driver, host);

        var activities = new List<ErChatToolActivity>();
        ErChatTurnResult? completed = null;
        engine.ToolActivityReceived += (_, a) => activities.Add(a);
        engine.TurnCompleted += (_, r) => completed = r;

        await engine.StartConversationAsync(TestContext.Current.CancellationToken);
        await engine.SendAsync("本のテーブルを作って", TestContext.Current.CancellationToken);

        host.Calls.Should().ContainSingle();
        host.Calls[0].Tool.Should().Be("add_entity");
        host.Calls[0].Args.Should().Contain("Book");
        activities.Should().ContainSingle();
        activities[0].ToolName.Should().Be("add_entity");
        activities[0].Success.Should().BeTrue();
        completed!.Value.Success.Should().BeTrue();

        // 2 回目のドライバ呼び出し時点では、user＋assistant(tool)＋tool 結果が履歴へ積まれている
        driver.HistoryCountsAtCall.Should().HaveCount(2);
        driver.HistoryCountsAtCall[1].Should().BeGreaterThan(driver.HistoryCountsAtCall[0]);
    }

    /// <summary>会話開始で履歴がシステムプロンプトのみにリセットされることを検証する</summary>
    [Fact(DisplayName = "StartConversation はシステムプロンプトで履歴を初期化する")]
    public async Task StartConversation_InitializesHistoryWithSystemPrompt()
    {
        var driver = new ScriptedTurnDriver([new ChatAssistantTurn("ok", [])]);
        var engine = CreateEngine(driver, new RecordingToolHost());

        await engine.StartConversationAsync(TestContext.Current.CancellationToken);
        await engine.SendAsync("hi", TestContext.Current.CancellationToken);

        // 1 回目の呼び出し時点の履歴 = system + user の 2 件
        driver.HistoryCountsAtCall[0].Should().Be(2);
    }
}
