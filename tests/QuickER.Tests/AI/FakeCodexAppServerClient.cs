using System.Text.Json;
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
    public event EventHandler? Disconnected;

    public bool IsStarted { get; private set; }

    /// <summary>codex CLI を検出できたことにするか（false なら未検出＝存在検出のフェイク）</summary>
    public bool IsCliAvailable { get; set; } = true;

    /// <summary>StartAsync が呼ばれた回数（未検出時にプロセス起動を試みないことの検証用）</summary>
    public int StartCount { get; private set; }

    /// <summary>StopAsync が呼ばれた回数（中断失敗時の強制停止の検証用）</summary>
    public int StopCount { get; private set; }

    /// <summary>StopAsync で投げる例外（非 null なら停止そのものの失敗を模擬する）</summary>
    public Exception? StopException { get; set; }

    /// <summary>InterruptTurnAsync で投げる例外（非 null なら中断要求の失敗を模擬する）</summary>
    public Exception? InterruptException { get; set; }

    /// <summary>InterruptTurnAsync の本体に入った時点で実行するフック（要求中のターン完了を模擬する）</summary>
    public Action? OnInterruptRequested { get; set; }

    /// <summary>
    /// 非 null なら InterruptTurnAsync がこのタスクの完了まで待ってから結果を返す（応答待ちの間に
    /// 別のことが起きる状況を作る）
    /// </summary>
    public Task? InterruptGate { get; set; }

    /// <summary>InterruptTurnAsync に入った時点で完了する（応答待ちへ確実に到達したことの合図）</summary>
    public TaskCompletionSource InterruptEntered { get; } = new();

    /// <summary>StartTurnAsync が返すターン Id（ターンごとに変えて、別のターンを区別する）</summary>
    public string NextTurnId { get; set; } = "turn_test";

    /// <summary>非 null なら StartTurnAsync がこのタスクの完了まで待つ（ターン開始中の状態を作る）</summary>
    public Task? StartTurnGate { get; set; }

    /// <summary>StartTurnAsync に入った時点で完了する（待たせた状態へ確実に到達したことの合図）</summary>
    /// <remarks>
    /// <see cref="StartTurnGate"/> だけでは、送信タスクが最初の await で戻った時点と、実際に
    /// 開始処理へ入った時点を区別できない。「開始中」を狙うテストはこれを待ってから操作する。
    /// </remarks>
    public TaskCompletionSource StartTurnEntered { get; } = new();

    /// <summary>StartThreadAsync が呼ばれた回数（接続断後に新しいスレッドを開き直すことの検証用）</summary>
    public int StartThreadCount { get; private set; }

    public CodexAccountInfo NextAccountInfo { get; set; } = new();

    public CodexThreadStartOptions? LastThreadStartOptions { get; private set; }

    public string? LastTurnPrompt { get; private set; }

    /// <summary>StartTurnAsync に渡されたプロンプトを送信順に記録する（多ターンの検証用）</summary>
    public List<string> TurnPrompts { get; } = new();

    /// <summary>StartTurnAsync が呼ばれた回数（ナッジによる追加ターンの検証用）</summary>
    public int StartTurnCount { get; private set; }

    /// <summary>StartTurnAsync で投げる例外（非 null ならターン送信そのものの失敗を模擬する）</summary>
    public Exception? StartTurnException { get; set; }

    /// <summary>
    /// 非空なら StartTurnAsync がターン開始と同時に completed/failed 通知を自動発火する（先頭から 1 件ずつ消費）。
    /// 多ターンをレース無く駆動するためのフック（空ならテストが手動で RaiseTurnCompleted する）。
    /// </summary>
    public Queue<(string Status, string? Error)> AutoTurnCompletions { get; } = new();

    /// <summary>RespondToApprovalAsync に渡された決定を応答順に記録する（承認要求の扱いの検証用）</summary>
    public List<string> ApprovalDecisions { get; } = new();

    /// <summary>
    /// decision 形でない承認応答（permissions など）のペイロードを、送信される JSON 文字列として応答順に記録する。
    /// </summary>
    public List<string> ApprovalResultJson { get; } = new();

    public int RespondToolCount { get; private set; }

    public string? LastToolResult { get; private set; }

    /// <summary>直近の RespondToDynamicToolCallAsync に渡された成否</summary>
    public bool LastToolSuccess { get; private set; }

    /// <summary>RespondToDynamicToolCallAsync が投げる例外（非 null なら応答送信の失敗を模擬する）</summary>
    public Exception? RespondToolException { get; set; }

    public int InterruptTurnCount { get; private set; }

    public string? LastInterruptThreadId { get; private set; }

    public string? LastInterruptTurnId { get; private set; }

    public bool IsAvailable() => IsCliAvailable;

    /// <summary>StartAsync で投げる例外（非 null なら起動失敗を模擬する）</summary>
    public Exception? StartException { get; set; }

    /// <summary>非 null なら StartAsync のハンドシェイクをこのタスクの完了まで待たせる</summary>
    /// <remarks>
    /// 待つ間は実クライアントと同じく <see cref="IsStarted"/> が真（プロセスと stdin はできている）になり、
    /// 重ねて呼ばれた StartAsync は何もせずに戻る（実クライアントの <c>if (IsStarted) return;</c> と同じ）。
    /// </remarks>
    public Task? StartGate { get; set; }

    /// <summary>ReadAccountAsync が呼ばれた回数</summary>
    public int ReadAccountCount { get; private set; }

    /// <summary>ハンドシェイクの完了前（StartGate を待っている間）に ReadAccountAsync が呼ばれた回数</summary>
    public int ReadAccountBeforeHandshakeCount { get; private set; }

    /// <summary>StartGate を待っている StartAsync の数</summary>
    private int _handshakesInProgress;

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

        if (_handshakesInProgress > 0)
        {
            return Task.CompletedTask;
        }

        IsStarted = true;
        return StartGate is null ? Task.CompletedTask : WaitHandshakeAsync(StartGate);
    }

    /// <summary>ハンドシェイクの完了を模擬して待つ</summary>
    private async Task WaitHandshakeAsync(Task gate)
    {
        _handshakesInProgress++;

        try
        {
            await gate;
        }
        finally
        {
            _handshakesInProgress--;
        }
    }

    /// <summary>実クライアントと同じく「再接続できる停止」を模擬する（接続断イベントは出さない）</summary>
    public Task StopAsync()
    {
        StopCount++;

        if (StopException is not null)
        {
            throw StopException;
        }

        IsStarted = false;
        return Task.CompletedTask;
    }

    public Task<CodexAccountInfo> ReadAccountAsync(
        bool refreshToken,
        CancellationToken cancellationToken = default
    )
    {
        ReadAccountCount++;

        if (_handshakesInProgress > 0)
        {
            ReadAccountBeforeHandshakeCount++;
        }

        return Task.FromResult(NextAccountInfo);
    }

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

    /// <summary>StartChatGptLoginAsync が返す認証 URL（scheme 検証の検証用に差し替えられる）</summary>
    public string ChatGptAuthUrl { get; set; } = "https://chatgpt.example/login";

    public Task<CodexLoginStartResult> StartChatGptLoginAsync(
        CancellationToken cancellationToken = default
    ) =>
        Task.FromResult(
            new CodexLoginStartResult { Type = CodexLoginType.ChatGpt, AuthUrl = ChatGptAuthUrl }
        );

    /// <summary>
    /// ログアウト時に account/updated（未ログイン）を流すか（既定 true。false にすると、通知に頼らず
    /// エンジン自身がログアウト後の状態を反映することを検証できる）
    /// </summary>
    public bool RaiseAccountUpdatedOnLogout { get; set; } = true;

    public Task LogoutAsync(CancellationToken cancellationToken = default)
    {
        if (!RaiseAccountUpdatedOnLogout)
        {
            return Task.CompletedTask;
        }

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
        StartThreadCount++;
        return Task.FromResult(new CodexThreadInfo { Id = "thr_test", Preview = string.Empty });
    }

    public async Task<CodexTurnInfo> StartTurnAsync(
        string threadId,
        string prompt,
        CancellationToken cancellationToken = default
    )
    {
        StartTurnEntered.TrySetResult();

        if (StartTurnGate is { } gate)
        {
            await gate.ConfigureAwait(false);
        }

        LastTurnPrompt = prompt;
        TurnPrompts.Add(prompt);
        StartTurnCount++;

        if (StartTurnException is not null)
        {
            throw StartTurnException;
        }

        var info = new CodexTurnInfo { Id = NextTurnId, Status = "inProgress" };

        // スクリプト化された完了があれば、ターン開始と同時に完了通知を自動発火する
        if (AutoTurnCompletions.Count > 0)
        {
            var (status, error) = AutoTurnCompletions.Dequeue();
            RaiseTurnCompleted(status, error);
        }

        return info;
    }

    public async Task InterruptTurnAsync(
        string threadId,
        string turnId,
        CancellationToken cancellationToken = default
    )
    {
        InterruptTurnCount++;
        LastInterruptThreadId = threadId;
        LastInterruptTurnId = turnId;
        OnInterruptRequested?.Invoke();
        InterruptEntered.TrySetResult();

        if (InterruptGate is { } gate)
        {
            await gate.ConfigureAwait(false);
        }

        if (InterruptException is not null)
        {
            throw InterruptException;
        }
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
        LastToolSuccess = success;

        if (RespondToolException is not null)
        {
            throw RespondToolException;
        }

        return Task.CompletedTask;
    }

    public Task RespondToApprovalAsync(
        int requestId,
        string decision,
        CancellationToken cancellationToken = default
    )
    {
        ApprovalDecisions.Add(decision);
        return Task.CompletedTask;
    }

    /// <summary>ペイロード形の応答は、実クライアントと同じ設定で JSON 化して記録する（送信形の検証用）</summary>
    public Task RespondToApprovalAsync(
        int requestId,
        object result,
        CancellationToken cancellationToken = default
    )
    {
        ApprovalResultJson.Add(
            JsonSerializer.Serialize(result, new JsonSerializerOptions(JsonSerializerDefaults.Web))
        );
        return Task.CompletedTask;
    }

    /// <summary>DisposeAsync が呼ばれた回数（アプリ終了時のエンジン破棄の検証用）</summary>
    public int DisposeCount { get; private set; }

    /// <summary>非 null なら DisposeAsync がこのタスクの完了まで戻らない（破棄のハングを模擬する）</summary>
    public Task? DisposeGate { get; set; }

    public async ValueTask DisposeAsync()
    {
        DisposeCount++;

        if (DisposeGate is not null)
        {
            await DisposeGate.ConfigureAwait(false);
        }
    }

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

    /// <summary>予期しない接続断（プロセス死・stdout の EOF）を模擬する</summary>
    public void RaiseDisconnected() => Disconnected?.Invoke(this, EventArgs.Empty);
}
