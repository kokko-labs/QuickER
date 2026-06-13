using ERDesigner.Services;

namespace ERDesigner.Tests.Services.Chat;

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

    public CodexAccountInfo NextAccountInfo { get; set; } = new();

    public CodexThreadStartOptions? LastThreadStartOptions { get; private set; }

    public string? LastTurnPrompt { get; private set; }

    public int RespondToolCount { get; private set; }

    public string? LastToolResult { get; private set; }

    public Task StartAsync(CodexAppServerSettings settings, string clientName, string clientTitle, string clientVersion, CancellationToken cancellationToken = default)
    {
        IsStarted = true;
        return Task.CompletedTask;
    }

    public Task<CodexAccountInfo> ReadAccountAsync(bool refreshToken, CancellationToken cancellationToken = default) => Task.FromResult(NextAccountInfo);

    public Task<CodexLoginStartResult> LoginWithApiKeyAsync(string apiKey, CancellationToken cancellationToken = default)
    {
        AccountUpdated?.Invoke(this, new CodexAccountUpdatedNotification { AuthMode = CodexAuthMode.ApiKey });
        return Task.FromResult(new CodexLoginStartResult { Type = CodexLoginType.ApiKey });
    }

    public Task<CodexLoginStartResult> StartChatGptLoginAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(new CodexLoginStartResult { Type = CodexLoginType.ChatGpt, AuthUrl = "https://chatgpt.example/login" });

    public Task LogoutAsync(CancellationToken cancellationToken = default)
    {
        AccountUpdated?.Invoke(this, new CodexAccountUpdatedNotification { AuthMode = CodexAuthMode.None });
        return Task.CompletedTask;
    }

    public Task<CodexThreadInfo> StartThreadAsync(CodexThreadStartOptions options, CancellationToken cancellationToken = default)
    {
        LastThreadStartOptions = options;
        return Task.FromResult(new CodexThreadInfo { Id = "thr_test", Preview = string.Empty });
    }

    public Task<CodexTurnInfo> StartTurnAsync(string threadId, string prompt, CancellationToken cancellationToken = default)
    {
        LastTurnPrompt = prompt;
        return Task.FromResult(new CodexTurnInfo { Id = "turn_test", Status = "inProgress" });
    }

    public Task InterruptTurnAsync(string threadId, string turnId, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task RespondToDynamicToolCallAsync(int requestId, string resultText, bool success, CancellationToken cancellationToken = default)
    {
        RespondToolCount++;
        LastToolResult = resultText;
        return Task.CompletedTask;
    }

    public Task RespondToApprovalAsync(int requestId, string decision, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    // ── テストからイベントを発火させるためのヘルパー ──

    public void RaiseAgentMessageDelta(string delta) => AgentMessageDeltaReceived?.Invoke(this, new CodexAgentMessageDeltaNotification { Delta = delta });

    public void RaiseTurnCompleted(string status, string? error = null) =>
        TurnCompleted?.Invoke(this, new CodexTurnCompletedNotification { ThreadId = "thr_test", Turn = new CodexTurnInfo { Id = "turn_test", Status = status, Error = error } });

    public void RaiseDynamicToolCall(CodexDynamicToolCallRequest request) => DynamicToolCallReceived?.Invoke(this, request);
}
