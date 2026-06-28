using System.Text.Json;
using FluentAssertions;
using QuickER.AI;
using QuickER.Services;
using QuickER.Services.Chat;

namespace QuickER.Tests.Services.Chat;

/// <summary><see cref="CodexChatEngine"/> が Codex 通知を共通イベントへ変換することを検証するテストクラス</summary>
public class CodexChatEngineTests
{
    private sealed class SyncUiDispatcher : IUiDispatcher
    {
        public T Invoke<T>(Func<T> func) => func();
    }

    private sealed class RecordingToolHost : IErDiagramToolHost
    {
        public List<(string Tool, string Args)> Calls { get; } = new();

        public (string Result, bool Success) Execute(string toolName, string argumentsJson)
        {
            Calls.Add((toolName, argumentsJson));
            return ($"{toolName} 実行済み", true);
        }
    }

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
        var engine = new CodexChatEngine(client, new RecordingToolHost(), new SyncUiDispatcher());
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
        var engine = new CodexChatEngine(client, host, new SyncUiDispatcher());
        var activities = new List<ErChatToolActivity>();
        engine.ToolActivityReceived += (_, a) => activities.Add(a);

        client.RaiseDynamicToolCall(BuildToolCall("add_entity", "{\"table_name\":\"Book\"}"));

        host.Calls.Should().ContainSingle();
        host.Calls[0].Tool.Should().Be("add_entity");
        activities.Should().ContainSingle().Which.ToolName.Should().Be("add_entity");
        client.RespondToolCount.Should().Be(1);
    }

    /// <summary>ターン完了通知が成否に応じた共通イベントへ変換されることを検証する</summary>
    [Theory(DisplayName = "Codex のターン完了は成否に応じた結果へ変換される")]
    [InlineData("completed", true)]
    [InlineData("interrupted", false)]
    [InlineData("failed", false)]
    public void TurnCompleted_IsTranslatedByStatus(string status, bool expectedSuccess)
    {
        var client = new FakeCodexAppServerClient();
        var engine = new CodexChatEngine(client, new RecordingToolHost(), new SyncUiDispatcher());
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
        var engine = new CodexChatEngine(client, new RecordingToolHost(), new SyncUiDispatcher())
        {
            ModelProvider = "ollama-launch",
        };

        await engine.InitializeAsync();

        engine.IsReady.Should().BeTrue();
    }
}
