using QuickER.AI;

namespace QuickER.Tests.AI.Mock;

/// <summary>
/// 送信内容を記録し、スクリプトされたツール呼び出しをツールホストへ橋渡しするフェイクエンジン。
/// 実際の LLM は呼ばず、テストが仕込んだツール呼び出しだけを再生する。
/// </summary>
internal sealed class FakeChatEngine : IErChatEngine
{
    private readonly IErDiagramToolHost _toolHost;

    public FakeChatEngine(IErDiagramToolHost toolHost) => _toolHost = toolHost;

    public List<string> SentPrompts { get; } = new();

    /// <summary>各 SendAsync で渡された添付を記録する（透過検証用）</summary>
    public List<IReadOnlyList<ChatAttachment>> SentAttachments { get; } = new();

    /// <summary>次の SendAsync で再生するツール呼び出し（ツール名・引数 JSON）</summary>
    public (string Tool, string Args)? ScriptedToolCall { get; set; }

    /// <summary>
    /// SendAsync ごとに 1 バッチずつ再生するツール呼び出し列（固定パイプライン検証用）。
    /// 各 SendAsync でキューから 1 バッチを取り出し、その中のツール呼び出しを順に実行する
    /// （空になったターンはツール呼び出しなし＝emit なしターンを再現する）。
    /// </summary>
    public Queue<IReadOnlyList<(string Tool, string Args)>> ScriptedTurns { get; } = new();

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

    public Task SendAsync(string prompt, CancellationToken cancellationToken = default) =>
        SendAsync(prompt, Array.Empty<ChatAttachment>(), cancellationToken);

    public Task SendAsync(
        string prompt,
        IReadOnlyList<ChatAttachment> attachments,
        CancellationToken cancellationToken = default
    )
    {
        SentPrompts.Add(prompt);
        SentAttachments.Add(attachments);

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

        // 固定パイプライン検証: このターン分のバッチを取り出し、含まれるツール呼び出しを順に実行する
        if (ScriptedTurns.Count > 0)
        {
            foreach (var (tool, args) in ScriptedTurns.Dequeue())
            {
                var result = _toolHost.Execute(tool, args);
                LastToolResult = result;
                ToolActivityReceived?.Invoke(
                    this,
                    new ErChatToolActivity(tool, result.Result, result.Success)
                );
            }
        }

        TurnCompleted?.Invoke(this, new ErChatTurnResult(true, null));
        return Task.CompletedTask;
    }

    public Task InterruptAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
