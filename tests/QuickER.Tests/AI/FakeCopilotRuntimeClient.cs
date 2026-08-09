using QuickER.AI;

namespace QuickER.Tests.AI;

/// <summary>
/// <see cref="CopilotChatEngine"/> のテスト用フェイク <see cref="ICopilotRuntimeClient"/>。
/// 接続・認証・モデル列挙の結果をスクリプト化し、イベント（差分・ツール要求・アイドル・エラー）を
/// 任意のタイミングで発火させて、エンジンの状態遷移とイベント変換だけを検証できるようにする。
/// </summary>
internal sealed class FakeCopilotRuntimeClient : ICopilotRuntimeClient
{
    /// <summary>copilot CLI を検出できるか</summary>
    public bool Available { get; set; } = true;

    /// <summary><see cref="StartAsync"/> が投げる例外（null なら成功）</summary>
    public Exception? StartError { get; set; }

    /// <summary><see cref="StartSessionAsync"/> が投げる例外（null なら成功）</summary>
    public Exception? StartSessionError { get; set; }

    /// <summary><see cref="GetAuthStatusAsync"/> が投げる例外（null なら <see cref="AuthInfo"/> を返す）</summary>
    public Exception? AuthError { get; set; }

    /// <summary>返す認証状態</summary>
    public CopilotAuthInfo AuthInfo { get; set; } = new(true, "octocat", "oauth", string.Empty);

    /// <summary>返すモデル ID 一覧</summary>
    public IReadOnlyList<string> Models { get; set; } = ["gpt-5", "claude-sonnet-4.5"];

    /// <summary>渡されたセッション生成オプション（最後の 1 件）</summary>
    public CopilotSessionOptions? LastSessionOptions { get; private set; }

    /// <summary>セッション生成回数</summary>
    public int StartSessionCallCount { get; private set; }

    /// <summary>送信された (プロンプト, 添付件数) の記録</summary>
    public List<(string Prompt, int AttachmentCount)> Sends { get; } = new();

    /// <summary>
    /// 送信のたびにアイドル復帰（＝ターン完了）を自動発火するか。
    /// 複数ターンを跨ぐシナリオ（自動続行ナッジ）で、テスト側が発火の順番を組まずに済むようにする。
    /// </summary>
    public bool AutoIdleAfterSend { get; set; }

    /// <summary>返送したツール結果の記録</summary>
    public List<(string RequestId, string Result, bool Success)> ToolResponses { get; } = new();

    /// <summary>中断要求の回数</summary>
    public int AbortCallCount { get; private set; }

    /// <summary>破棄済みか</summary>
    public bool Disposed { get; private set; }

    /// <inheritdoc />
    public bool IsStarted { get; private set; }

    /// <inheritdoc />
    public bool HasSession { get; private set; }

    /// <inheritdoc />
    public event EventHandler<string>? AssistantDeltaReceived;

    /// <inheritdoc />
    public event EventHandler<CopilotToolCallRequest>? ToolCallRequested;

    /// <inheritdoc />
    public event EventHandler<string>? ToolExecutionStarted;

    /// <inheritdoc />
    public event EventHandler<bool>? SessionIdle;

    /// <inheritdoc />
    public event EventHandler<string>? SessionErrorReceived;

    /// <inheritdoc />
    public event EventHandler<string>? PermissionDeclined;

    /// <inheritdoc />
    public bool IsAvailable() => Available;

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (StartError is not null)
        {
            return Task.FromException(StartError);
        }

        IsStarted = true;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<CopilotAuthInfo> GetAuthStatusAsync(
        CancellationToken cancellationToken = default
    ) =>
        AuthError is not null
            ? Task.FromException<CopilotAuthInfo>(AuthError)
            : Task.FromResult(AuthInfo);

    /// <inheritdoc />
    public Task<IReadOnlyList<string>> ListModelsAsync(
        CancellationToken cancellationToken = default
    ) => Task.FromResult(Models);

    /// <inheritdoc />
    public Task StartSessionAsync(
        CopilotSessionOptions options,
        CancellationToken cancellationToken = default
    )
    {
        StartSessionCallCount++;
        LastSessionOptions = options;

        if (StartSessionError is not null)
        {
            return Task.FromException(StartSessionError);
        }

        HasSession = true;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task SendAsync(
        string prompt,
        IReadOnlyList<ChatAttachment> attachments,
        CancellationToken cancellationToken = default
    )
    {
        Sends.Add((prompt, attachments.Count));

        if (AutoIdleAfterSend)
        {
            SessionIdle?.Invoke(this, false);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task AbortAsync(CancellationToken cancellationToken = default)
    {
        AbortCallCount++;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task RespondToToolCallAsync(
        string requestId,
        string result,
        bool success,
        CancellationToken cancellationToken = default
    )
    {
        ToolResponses.Add((requestId, result, success));
        return Task.CompletedTask;
    }

    /// <summary>アシスタント応答の差分を発火する</summary>
    public void RaiseDelta(string delta) => AssistantDeltaReceived?.Invoke(this, delta);

    /// <summary>ツール呼び出し要求を発火する</summary>
    public void RaiseToolCall(string requestId, string toolName, string argumentsJson) =>
        ToolCallRequested?.Invoke(
            this,
            new CopilotToolCallRequest(requestId, toolName, argumentsJson)
        );

    /// <summary>組込みツールの実行開始を発火する</summary>
    public void RaiseToolExecutionStarted(string toolName) =>
        ToolExecutionStarted?.Invoke(this, toolName);

    /// <summary>アイドル復帰（＝ターン完了）を発火する</summary>
    public void RaiseIdle(bool aborted = false) => SessionIdle?.Invoke(this, aborted);

    /// <summary>セッションエラーを発火する</summary>
    public void RaiseError(string message) => SessionErrorReceived?.Invoke(this, message);

    /// <summary>許可要求の拒否を発火する</summary>
    public void RaisePermissionDeclined(string description) =>
        PermissionDeclined?.Invoke(this, description);

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        Disposed = true;
        IsStarted = false;
        HasSession = false;
        return ValueTask.CompletedTask;
    }
}
