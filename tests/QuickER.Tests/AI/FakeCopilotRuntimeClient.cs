using System.IO;
using GitHub.Copilot;
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

    /// <summary>接続（<see cref="StartAsync"/> の成功）の回数</summary>
    public int StartCallCount { get; private set; }

    /// <summary>セッション生成回数</summary>
    public int StartSessionCallCount { get; private set; }

    /// <summary>送信された (プロンプト, 添付件数) の記録</summary>
    public List<(string Prompt, int AttachmentCount)> Sends { get; } = new();

    /// <summary>
    /// 送信のたびにアイドル復帰（＝ターン完了）を自動発火するか。
    /// 複数ターンを跨ぐシナリオ（自動続行ナッジ）で、テスト側が発火の順番を組まずに済むようにする。
    /// </summary>
    public bool AutoIdleAfterSend { get; set; }

    /// <summary>
    /// 送信の中で、アイドル復帰の自動発火の後に走らせる処理（SDK のスレッドが送信の途中で
    /// セッションエラーを流す・送信自体が例外で終わる、といった順序の模擬に使う）
    /// </summary>
    public Action? DuringSend { get; set; }

    /// <summary>返送したツール結果の記録</summary>
    public List<(string RequestId, string Result, bool Success)> ToolResponses { get; } = new();

    /// <summary>RespondToToolCallAsync が投げる例外（非 null なら応答送信の失敗を模擬する）</summary>
    public Exception? RespondToolException { get; set; }

    /// <summary>中断要求の回数</summary>
    public int AbortCallCount { get; private set; }

    /// <summary>破棄済みか</summary>
    public bool Disposed { get; private set; }

    /// <summary>DisposeAsync が呼ばれた回数（アプリ終了時のエンジン破棄の検証用）</summary>
    public int DisposeCount { get; private set; }

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

        StartCallCount++;

        // 起動し直したランタイムとの接続は生きている
        _failPingsWithConnectionLost = false;

        if (StartGate is null)
        {
            IsStarted = true;
            return Task.CompletedTask;
        }

        return WaitStartAsync(StartGate);
    }

    /// <summary>非 null なら StartAsync をこのタスクの完了まで待たせる（起動が終わるまで IsStarted は偽）</summary>
    public Task? StartGate { get; set; }

    /// <summary>起動の完了を模擬して待つ</summary>
    private async Task WaitStartAsync(Task gate)
    {
        await gate;
        IsStarted = true;
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

        if (StartSessionGate is not null)
        {
            return WaitSessionAsync(StartSessionGate);
        }

        HasSession = true;
        return Task.CompletedTask;
    }

    /// <summary>非 null なら StartSessionAsync（会話の開始）をこのタスクの完了まで待たせる</summary>
    public Task? StartSessionGate { get; set; }

    /// <summary>会話の開始の完了を模擬して待つ</summary>
    private async Task WaitSessionAsync(Task gate)
    {
        await gate;
        HasSession = true;
    }

    /// <inheritdoc />
    public Task SendAsync(
        string prompt,
        IReadOnlyList<ChatAttachment> attachments,
        CancellationToken cancellationToken = default
    )
    {
        if (_failNextSendWithConnectionLost)
        {
            _failNextSendWithConnectionLost = false;
            SimulateConnectionLost();
            return Task.FromException(CreateConnectionLostException());
        }

        if (SendError is not null)
        {
            return Fail(SendError);
        }

        Sends.Add((prompt, attachments.Count));

        if (AutoIdleAfterSend)
        {
            SessionIdle?.Invoke(this, false);
        }

        DuringSend?.Invoke();

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task AbortAsync(CancellationToken cancellationToken = default)
    {
        AbortCallCount++;

        if (_failNextAbortWithConnectionLost)
        {
            _failNextAbortWithConnectionLost = false;
            SimulateConnectionLost();
            return Task.FromException(CreateConnectionLostException());
        }

        return AbortError is not null ? Fail(AbortError) : Task.CompletedTask;
    }

    /// <summary>
    /// <see cref="AbortAsync"/> が投げる例外（null なら成功）。実装と同じく
    /// <see cref="CopilotRuntimeClient.IsConnectionLost"/> が接続断と判定する例外なら接続を捨てる
    /// </summary>
    public Exception? AbortError { get; set; }

    /// <summary>
    /// <see cref="SendAsync(string, IReadOnlyList{ChatAttachment}, CancellationToken)"/> が投げる例外（null なら成功）。
    /// 実装と同じく <see cref="CopilotRuntimeClient.IsConnectionLost"/> が接続断と判定する例外なら接続を捨てる。
    /// 送信は届かないので <see cref="Sends"/> には積まない
    /// </summary>
    public Exception? SendError { get; set; }

    /// <inheritdoc />
    public Task PingAsync(CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _pingCallCount);

        if (_failPingsWithConnectionLost)
        {
            SimulateConnectionLost();
            return Task.FromException(CreateConnectionLostException());
        }

        return PingError is not null ? Fail(PingError) : Task.CompletedTask;
    }

    /// <summary><see cref="PingAsync"/> が呼ばれた回数</summary>
    public int PingCallCount => Volatile.Read(ref _pingCallCount);

    /// <summary><see cref="PingCallCount"/> の数</summary>
    private int _pingCallCount;

    /// <summary>
    /// <see cref="PingAsync"/> が投げる例外（null なら成功）。実装と同じく
    /// <see cref="CopilotRuntimeClient.IsConnectionLost"/> が接続断と判定する例外なら接続を捨てる
    /// </summary>
    public Exception? PingError { get; set; }

    /// <summary>以後の <see cref="PingAsync"/> を、接続が切れた失敗にする（応答の途中でランタイムが止まった状態を模す）</summary>
    /// <remarks>実装と同じく、接続とセッションを捨ててから型がちょうど <see cref="IOException"/> の例外を投げる</remarks>
    public void FailPingsWithConnectionLost() => _failPingsWithConnectionLost = true;

    /// <summary><see cref="FailPingsWithConnectionLost"/> の印</summary>
    private volatile bool _failPingsWithConnectionLost;

    /// <summary>次の中断 1 回を、接続が切れた失敗にする</summary>
    /// <remarks>実装と同じく、接続とセッションを捨ててから型がちょうど <see cref="IOException"/> の例外を投げる</remarks>
    public void FailNextAbortWithConnectionLost() => _failNextAbortWithConnectionLost = true;

    /// <summary><see cref="FailNextAbortWithConnectionLost"/> の印</summary>
    private bool _failNextAbortWithConnectionLost;

    /// <summary>実装と同じく、接続断と判定される例外なら接続を捨ててから、例外で終わるタスクを返す</summary>
    /// <param name="error">投げる例外</param>
    private Task Fail(Exception error)
    {
        if (CopilotRuntimeClient.IsConnectionLost(error))
        {
            SimulateConnectionLost();
        }

        return Task.FromException(error);
    }

    /// <summary>実装が接続断で投げるのと同じ形の例外を作る</summary>
    private static IOException CreateConnectionLostException() => ConnectionLostError();

    /// <summary>
    /// SDK（GitHub.Copilot.SDK 1.0.26）が接続断で投げる形の例外を作る（型がちょうど <see cref="IOException"/> で、
    /// 内側が SDK 内部の接続断の例外）
    /// </summary>
    public static IOException ConnectionLostError()
    {
        var inner = (Exception)
            Activator.CreateInstance(
                CopilotRuntimeClient.ConnectionLostExceptionType!,
                nonPublic: true
            )!;
        return new IOException("Communication error with Copilot CLI: " + inner.Message, inner);
    }

    /// <summary>
    /// SDK（GitHub.Copilot.SDK 1.0.26）が、ランタイムが返した通常のエラー応答で投げる形の例外を作る
    /// （型がちょうど <see cref="IOException"/> で、内側が <see cref="RemoteRpcException"/>）
    /// </summary>
    /// <param name="message">ランタイムが返したエラーの文言</param>
    public static IOException ErrorResponse(string message)
    {
        var inner = new RemoteRpcException(message, -32603);
        return new IOException("Communication error with Copilot CLI: " + inner.Message, inner);
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

        if (RespondToolException is not null)
        {
            throw RespondToolException;
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// copilot ランタイムとの接続が切れたことを模す（実装は接続断の失敗を受けるとセッションごと接続を捨てる）
    /// </summary>
    public void SimulateConnectionLost()
    {
        IsStarted = false;
        HasSession = false;
    }

    /// <summary>次の送信 1 回を、接続が切れた失敗にする</summary>
    /// <remarks>
    /// 実装と同じく、接続とセッションを捨ててから（<see cref="IsStarted"/> と <see cref="HasSession"/> を false にしてから）
    /// 型がちょうど <see cref="IOException"/> の例外を投げる。送信は届かないので <see cref="Sends"/> には積まない
    /// </remarks>
    public void FailNextSendWithConnectionLost() => _failNextSendWithConnectionLost = true;

    /// <summary><see cref="FailNextSendWithConnectionLost"/> の印</summary>
    private bool _failNextSendWithConnectionLost;

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
        DisposeCount++;
        IsStarted = false;
        HasSession = false;
        return ValueTask.CompletedTask;
    }
}
