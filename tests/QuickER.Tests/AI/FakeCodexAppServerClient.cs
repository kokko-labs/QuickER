using QuickER.AI;
using QuickER.Services;

namespace QuickER.Tests.AI;

/// <summary>通信を伴わず接続・認証・スレッド操作を模擬する <see cref="ICodexAppServerClient"/> のフェイク</summary>
internal sealed class FakeCodexAppServerClient : ICodexAppServerClient
{
    public event EventHandler<CodexJsonRpcNotification>? NotificationReceived;
    public event EventHandler<CodexLoginCompletedNotification>? LoginCompleted;
    public event EventHandler<CodexAccountUpdatedNotification>? AccountUpdated;
    public event EventHandler<CodexThreadStartedNotification>? ThreadStarted;
    public event EventHandler<CodexTurnStartedNotification>? TurnStarted;
    public event EventHandler<CodexAgentMessageDeltaNotification>? AgentMessageDeltaReceived;
    public event EventHandler<CodexTurnCompletedNotification>? TurnCompleted;
    public event EventHandler<CodexDynamicToolCallRequest>? DynamicToolCallReceived;
    public event EventHandler<CodexItemStartedNotification>? ItemStarted;
    public event EventHandler<CodexItemCompletedNotification>? ItemCompleted;
    public event EventHandler<CodexApprovalRequest>? ApprovalRequested;

    public bool IsStarted { get; private set; }

    /// <summary>codex CLI を検出できたことにするか（false なら未検出＝存在検出のフェイク）</summary>
    public bool IsCliAvailable { get; set; } = true;

    /// <summary>StartAsync が呼ばれた回数（未検出時にプロセス起動を試みないことの検証用）</summary>
    public int StartCount { get; private set; }

    public CodexAccountInfo NextAccountInfo { get; set; } = new();

    public CodexThreadStartOptions? LastThreadStartOptions { get; private set; }

    public string? LastTurnPrompt { get; private set; }

    /// <summary>StartTurnAsync に渡されたプロンプトを送信順に記録する（多ターンの検証用）</summary>
    public List<string> TurnPrompts { get; } = new();

    /// <summary>StartTurnAsync が呼ばれた回数（ナッジによる追加ターンの検証用）</summary>
    public int StartTurnCount { get; private set; }

    /// <summary>
    /// 非空なら StartTurnAsync がターン開始と同時に completed/failed 通知を自動発火する（先頭から 1 件ずつ消費）。
    /// 多ターンをレース無く駆動するためのフック（空なら従来どおりテストが手動で RaiseTurnCompleted する）。
    /// </summary>
    public Queue<(string Status, string? Error)> AutoTurnCompletions { get; } = new();

    public int RespondToolCount { get; private set; }

    public string? LastToolResult { get; private set; }

    public int InterruptTurnCount { get; private set; }

    public string? LastInterruptThreadId { get; private set; }

    public string? LastInterruptTurnId { get; private set; }

    public bool IsAvailable() => IsCliAvailable;

    /// <summary>StartAsync で投げる例外（非 null なら起動失敗を模擬する）</summary>
    public Exception? StartException { get; set; }

    public Task StartAsync(
        CodexAppServerSettings settings,
        string clientName,
        string clientTitle,
        string clientVersion,
        CancellationToken cancellationToken = default
    )
    {
        StartCount++;

        if (StartException is not null)
        {
            throw StartException;
        }

        IsStarted = true;
        return Task.CompletedTask;
    }

    public Task<CodexAccountInfo> ReadAccountAsync(
        bool refreshToken,
        CancellationToken cancellationToken = default
    ) => Task.FromResult(NextAccountInfo);

    public Task<CodexLoginStartResult> LoginWithApiKeyAsync(
        string apiKey,
        CancellationToken cancellationToken = default
    )
    {
        AccountUpdated?.Invoke(
            this,
            new CodexAccountUpdatedNotification { AuthMode = CodexAuthMode.ApiKey }
        );
        return Task.FromResult(new CodexLoginStartResult { Type = CodexLoginType.ApiKey });
    }

    public Task<CodexLoginStartResult> StartChatGptLoginAsync(
        CancellationToken cancellationToken = default
    ) =>
        Task.FromResult(
            new CodexLoginStartResult
            {
                Type = CodexLoginType.ChatGpt,
                AuthUrl = "https://chatgpt.example/login",
            }
        );

    public Task LogoutAsync(CancellationToken cancellationToken = default)
    {
        AccountUpdated?.Invoke(
            this,
            new CodexAccountUpdatedNotification { AuthMode = CodexAuthMode.None }
        );
        return Task.CompletedTask;
    }

    public Task<CodexThreadInfo> StartThreadAsync(
        CodexThreadStartOptions options,
        CancellationToken cancellationToken = default
    )
    {
        LastThreadStartOptions = options;
        return Task.FromResult(new CodexThreadInfo { Id = "thr_test", Preview = string.Empty });
    }

    public Task<CodexTurnInfo> StartTurnAsync(
        string threadId,
        string prompt,
        CancellationToken cancellationToken = default
    )
    {
        LastTurnPrompt = prompt;
        TurnPrompts.Add(prompt);
        StartTurnCount++;

        var info = new CodexTurnInfo { Id = "turn_test", Status = "inProgress" };

        // スクリプト化された完了があれば、ターン開始と同時に完了通知を自動発火する
        if (AutoTurnCompletions.Count > 0)
        {
            var (status, error) = AutoTurnCompletions.Dequeue();
            RaiseTurnCompleted(status, error);
        }

        return Task.FromResult(info);
    }

    public Task InterruptTurnAsync(
        string threadId,
        string turnId,
        CancellationToken cancellationToken = default
    )
    {
        InterruptTurnCount++;
        LastInterruptThreadId = threadId;
        LastInterruptTurnId = turnId;
        return Task.CompletedTask;
    }

    public Task RespondToDynamicToolCallAsync(
        int requestId,
        string resultText,
        bool success,
        CancellationToken cancellationToken = default
    )
    {
        RespondToolCount++;
        LastToolResult = resultText;
        return Task.CompletedTask;
    }

    public Task RespondToApprovalAsync(
        int requestId,
        string decision,
        CancellationToken cancellationToken = default
    ) => Task.CompletedTask;

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    // ── テストからイベントを発火させるためのヘルパー ──

    public void RaiseAgentMessageDelta(string delta) =>
        AgentMessageDeltaReceived?.Invoke(
            this,
            new CodexAgentMessageDeltaNotification { Delta = delta }
        );

    public void RaiseTurnCompleted(string status, string? error = null) =>
        TurnCompleted?.Invoke(
            this,
            new CodexTurnCompletedNotification
            {
                ThreadId = "thr_test",
                Turn = new CodexTurnInfo
                {
                    Id = "turn_test",
                    Status = status,
                    Error = error,
                },
            }
        );

    public void RaiseDynamicToolCall(CodexDynamicToolCallRequest request) =>
        DynamicToolCallReceived?.Invoke(this, request);

    public void RaiseNotification(CodexJsonRpcNotification notification) =>
        NotificationReceived?.Invoke(this, notification);

    public void RaiseLoginCompleted(CodexLoginCompletedNotification notification) =>
        LoginCompleted?.Invoke(this, notification);

    public void RaiseThreadStarted(CodexThreadStartedNotification notification) =>
        ThreadStarted?.Invoke(this, notification);

    public void RaiseTurnStarted(CodexTurnStartedNotification notification) =>
        TurnStarted?.Invoke(this, notification);

    public void RaiseItemStarted(CodexItemStartedNotification notification) =>
        ItemStarted?.Invoke(this, notification);

    public void RaiseItemCompleted(CodexItemCompletedNotification notification) =>
        ItemCompleted?.Invoke(this, notification);

    public void RaiseApprovalRequested(CodexApprovalRequest request) =>
        ApprovalRequested?.Invoke(this, request);
}
