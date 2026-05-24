using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ERDesigner.Services;

/// <summary>Codex App Server との通信を扱うクライアントの抽象です。</summary>
public interface ICodexAppServerClient : IAsyncDisposable
{
    /// <summary>汎用通知を受信したときに発火します。</summary>
    event EventHandler<CodexJsonRpcNotification>? NotificationReceived;

    /// <summary>ログイン完了通知を受信したときに発火します。</summary>
    event EventHandler<CodexLoginCompletedNotification>? LoginCompleted;

    /// <summary>アカウント更新通知を受信したときに発火します。</summary>
    event EventHandler<CodexAccountUpdatedNotification>? AccountUpdated;

    /// <summary>スレッド開始通知を受信したときに発火します。</summary>
    event EventHandler<CodexThreadStartedNotification>? ThreadStarted;

    /// <summary>ターン開始通知を受信したときに発火します。</summary>
    event EventHandler<CodexTurnStartedNotification>? TurnStarted;

    /// <summary>エージェントメッセージ差分通知を受信したときに発火します。</summary>
    event EventHandler<CodexAgentMessageDeltaNotification>? AgentMessageDeltaReceived;

    /// <summary>ターン完了通知を受信したときに発火します。</summary>
    event EventHandler<CodexTurnCompletedNotification>? TurnCompleted;

    /// <summary>Codex App Server が起動済みかどうかです。</summary>
    bool IsStarted { get; }

    /// <summary>Codex App Server を起動して初期化します。</summary>
    Task StartAsync(CodexAppServerSettings settings, string clientName, string clientTitle, string clientVersion, CancellationToken cancellationToken = default);

    /// <summary>現在のアカウント状態を取得します。</summary>
    Task<CodexAccountInfo> ReadAccountAsync(bool refreshToken, CancellationToken cancellationToken = default);

    /// <summary>API キーでログインします。</summary>
    Task<CodexLoginStartResult> LoginWithApiKeyAsync(string apiKey, CancellationToken cancellationToken = default);

    /// <summary>ChatGPT ブラウザログインを開始します。</summary>
    Task<CodexLoginStartResult> StartChatGptLoginAsync(CancellationToken cancellationToken = default);

    /// <summary>ChatGPT デバイスコードログインを開始します。</summary>
    Task<CodexLoginStartResult> StartChatGptDeviceCodeLoginAsync(CancellationToken cancellationToken = default);

    /// <summary>ログアウトします。</summary>
    Task LogoutAsync(CancellationToken cancellationToken = default);

    /// <summary>新しい会話スレッドを開始します。</summary>
    Task<CodexThreadInfo> StartThreadAsync(CodexThreadStartOptions options, CancellationToken cancellationToken = default);

    /// <summary>指定スレッドで新しいターンを開始します。</summary>
    Task<CodexTurnInfo> StartTurnAsync(string threadId, string prompt, CancellationToken cancellationToken = default);

    /// <summary>実行中のターンを中断します。</summary>
    Task InterruptTurnAsync(string threadId, string turnId, CancellationToken cancellationToken = default);

    /// <summary>dynamicTool 呼び出しに対してレスポンスを返します。</summary>
    Task RespondToDynamicToolCallAsync(int requestId, string resultText, bool success, CancellationToken cancellationToken = default);

    /// <summary>承認リクエストに対して accept で応答します。</summary>
    Task RespondToApprovalAsync(int requestId, string decision, CancellationToken cancellationToken = default);

    /// <summary>dynamicTool 呼び出しを受信したときに発火します。</summary>
    event EventHandler<CodexDynamicToolCallRequest>? DynamicToolCallReceived;

    /// <summary>item/started 通知を受信したときに発火します。</summary>
    event EventHandler<CodexItemStartedNotification>? ItemStarted;

    /// <summary>item/completed 通知を受信したときに発火します。</summary>
    event EventHandler<CodexItemCompletedNotification>? ItemCompleted;

    /// <summary>承認リクエストを受信したときに発火します。</summary>
    event EventHandler<CodexApprovalRequest>? ApprovalRequested;
}

/// <summary>Codex App Server を stdio で起動して JSON-RPC を送受信するクライアントです。</summary>
public sealed class CodexAppServerClient : ICodexAppServerClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };
    private const string GetAccountMethod = "account/read";
    private const string LoginAccountMethod = "account/login/start";
    private const string LogoutAccountMethod = "account/logout";

    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly ConcurrentDictionary<int, TaskCompletionSource<JsonElement?>> _pendingRequests = new();
    private Process? _process;
    private StreamWriter? _stdin;
    private Task? _readerTask;
    private CancellationTokenSource? _readerCts;
    private int _nextRequestId;
    private readonly ConcurrentQueue<string> _stderrLines = new();

    /// <inheritdoc />
    public event EventHandler<CodexJsonRpcNotification>? NotificationReceived;

    /// <inheritdoc />
    public event EventHandler<CodexLoginCompletedNotification>? LoginCompleted;

    /// <inheritdoc />
    public event EventHandler<CodexAccountUpdatedNotification>? AccountUpdated;

    /// <inheritdoc />
    public event EventHandler<CodexThreadStartedNotification>? ThreadStarted;

    /// <inheritdoc />
    public event EventHandler<CodexTurnStartedNotification>? TurnStarted;

    /// <inheritdoc />
    public event EventHandler<CodexAgentMessageDeltaNotification>? AgentMessageDeltaReceived;

    /// <inheritdoc />
    public event EventHandler<CodexTurnCompletedNotification>? TurnCompleted;

    /// <inheritdoc />
    public event EventHandler<CodexDynamicToolCallRequest>? DynamicToolCallReceived;

    /// <inheritdoc />
    public event EventHandler<CodexItemStartedNotification>? ItemStarted;

    /// <inheritdoc />
    public event EventHandler<CodexItemCompletedNotification>? ItemCompleted;

    /// <inheritdoc />
    public event EventHandler<CodexApprovalRequest>? ApprovalRequested;

    /// <inheritdoc />
    public bool IsStarted => _process is { HasExited: false } && _stdin is not null;

    /// <inheritdoc />
    public async Task StartAsync(CodexAppServerSettings settings, string clientName, string clientTitle, string clientVersion, CancellationToken cancellationToken = default)
    {
        if (IsStarted)
        {
            return;
        }

        var executablePath = "codex";
        var appServerArguments = BuildArguments(string.Empty);
        var (fileName, arguments) = ResolveStartInfo(executablePath, appServerArguments);

        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            WorkingDirectory = Environment.CurrentDirectory,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            // BOM なし UTF-8 を使用する（BOM 付きだとサーバー側のJSONパースが失敗するため）
            StandardInputEncoding = new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            StandardOutputEncoding = new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            StandardErrorEncoding = new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
        };

        var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };

        if (!process.Start())
        {
            throw new InvalidOperationException("Codex App Server を起動できませんでした。");
        }

        _process = process;
        _stdin = process.StandardInput;
        _stdin.NewLine = "\n";
        _stdin.AutoFlush = true;
        _readerCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _readerTask = Task.Run(() => ReadLoopAsync(process.StandardOutput, _readerCts.Token), CancellationToken.None);

        // stderr を非同期で読み捨てる（バッファフルによるデッドロックを防ぐ）
        _ = Task.Run(
            async () =>
            {
                try
                {
                    while (!_readerCts.IsCancellationRequested)
                    {
                        var line = await process.StandardError.ReadLineAsync().ConfigureAwait(false);

                        if (line is null)
                        {
                            break;
                        }

                        _stderrLines.Enqueue(line);

                        while (_stderrLines.Count > 20 && _stderrLines.TryDequeue(out _)) { }
                    }
                }
                catch
                {
                    // 終了時の例外は無視する
                }
            },
            CancellationToken.None
        );

        // initialize/initialized ハンドシェイクが失敗した場合はプロセスをクリーンアップし、
        // IsStarted が false に戻るようにする（次回の EnsureStartedAsync で再接続できるようにする）
        try
        {
            await SendRequestAsync(
                "initialize",
                new
                {
                    clientInfo = new
                    {
                        name = clientName,
                        title = clientTitle,
                        version = clientVersion,
                    },
                    capabilities = new { experimentalApi = true },
                },
                cancellationToken
            );

            await SendNotificationAsync("initialized", null, cancellationToken);
        }
        catch
        {
            // ハンドシェイク失敗時はプロセスを終了してフィールドをリセットする
            await DisposeAsync();
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<CodexAccountInfo> ReadAccountAsync(bool refreshToken, CancellationToken cancellationToken = default)
    {
        var result = await SendRequestAsync(GetAccountMethod, new { refreshToken }, cancellationToken);
        return ParseAccountInfo(result);
    }

    /// <inheritdoc />
    public Task<CodexLoginStartResult> LoginWithApiKeyAsync(string apiKey, CancellationToken cancellationToken = default)
    {
        return StartLoginAsync(new { type = "apiKey", apiKey }, CodexLoginType.ApiKey, cancellationToken);
    }

    /// <inheritdoc />
    public Task<CodexLoginStartResult> StartChatGptLoginAsync(CancellationToken cancellationToken = default)
    {
        return StartLoginAsync(new { type = "chatgpt" }, CodexLoginType.ChatGpt, cancellationToken);
    }

    /// <inheritdoc />
    public Task<CodexLoginStartResult> StartChatGptDeviceCodeLoginAsync(CancellationToken cancellationToken = default)
    {
        return StartLoginAsync(new { type = "chatgptDeviceCode" }, CodexLoginType.ChatGptDeviceCode, cancellationToken);
    }

    /// <inheritdoc />
    public async Task LogoutAsync(CancellationToken cancellationToken = default)
    {
        await SendRequestAsync(LogoutAccountMethod, null, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<CodexThreadInfo> StartThreadAsync(CodexThreadStartOptions options, CancellationToken cancellationToken = default)
    {
        var parameters = new Dictionary<string, object?>();

        if (!string.IsNullOrWhiteSpace(options.Cwd))
        {
            parameters["cwd"] = options.Cwd;
        }

        if (!string.IsNullOrWhiteSpace(options.ModelProvider))
        {
            parameters["modelProvider"] = options.ModelProvider;
        }

        if (!string.IsNullOrWhiteSpace(options.Model))
        {
            parameters["model"] = options.Model;
        }

        if (!string.IsNullOrWhiteSpace(options.ApprovalPolicy))
        {
            parameters["approvalPolicy"] = options.ApprovalPolicy;
        }

        if (!string.IsNullOrWhiteSpace(options.Sandbox))
        {
            parameters["sandbox"] = options.Sandbox;
        }

        if (options.DynamicTools is { Count: > 0 })
        {
            parameters["dynamicTools"] = options
                .DynamicTools.Select(tool => new Dictionary<string, object?>
                {
                    ["name"] = tool.Name,
                    ["description"] = tool.Description,
                    ["deferLoading"] = tool.DeferLoading,
                    ["inputSchema"] = tool.InputSchema,
                })
                .ToArray();
        }

        var result = await SendRequestAsync("thread/start", parameters, cancellationToken);
        return ParseThreadStartResult(result);
    }

    /// <inheritdoc />
    public async Task<CodexTurnInfo> StartTurnAsync(string threadId, string prompt, CancellationToken cancellationToken = default)
    {
        var result = await SendRequestAsync("turn/start", new { threadId, input = new object[] { new CodexTextInputItem { Text = prompt } } }, cancellationToken);

        return ParseTurnStartResult(result);
    }

    /// <inheritdoc />
    public async Task InterruptTurnAsync(string threadId, string turnId, CancellationToken cancellationToken = default)
    {
        await SendRequestAsync("turn/interrupt", new { threadId, turnId }, cancellationToken);
    }

    /// <inheritdoc />
    public async Task RespondToDynamicToolCallAsync(int requestId, string resultText, bool success, CancellationToken cancellationToken = default)
    {
        EnsureStarted();
        var response = new Dictionary<string, object?>
        {
            ["id"] = requestId,
            ["result"] = new { contentItems = new object[] { new { type = "inputText", text = resultText } }, success },
        };
        await SendMessageAsync(response, cancellationToken);
    }

    /// <inheritdoc />
    public async Task RespondToApprovalAsync(int requestId, string decision, CancellationToken cancellationToken = default)
    {
        EnsureStarted();
        var response = new Dictionary<string, object?> { ["id"] = requestId, ["result"] = new { decision } };
        await SendMessageAsync(response, cancellationToken);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        _readerCts?.Cancel();

        if (_stdin is not null)
        {
            await _stdin.DisposeAsync();
            _stdin = null;
        }

        if (_readerTask is not null)
        {
            try
            {
                await _readerTask;
            }
            catch
            {
                // 終了時の読み取り例外は破棄します。
            }
        }

        if (_process is not null)
        {
            try
            {
                if (!_process.HasExited)
                {
                    _process.Kill(entireProcessTree: true);
                }
            }
            catch
            {
                // 終了済みなどの例外は破棄します。
            }

            _process.Dispose();
            _process = null;
        }

        _readerCts?.Dispose();
        _readerCts = null;
        _writeLock.Dispose();
    }

    private async Task<CodexLoginStartResult> StartLoginAsync(object parameters, CodexLoginType fallbackType, CancellationToken cancellationToken)
    {
        var result = await SendRequestAsync(LoginAccountMethod, parameters, cancellationToken);
        return ParseLoginStartResult(result, fallbackType);
    }

    private async Task<JsonElement?> SendRequestAsync(string method, object? parameters, CancellationToken cancellationToken)
    {
        EnsureStarted();

        var requestId = Interlocked.Increment(ref _nextRequestId);
        var completionSource = new TaskCompletionSource<JsonElement?>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pendingRequests[requestId] = completionSource;

        // リクエストタイムアウト（30 秒）+ 呼び出し元キャンセルを合成する
        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

        using var cancellationRegistration = linkedCts.Token.Register(() =>
        {
            if (_pendingRequests.TryRemove(requestId, out var pending))
            {
                if (timeoutCts.IsCancellationRequested)
                {
                    pending.TrySetException(
                        new TimeoutException($"Codex App Server からのレスポンスがタイムアウトしました (method: {method})。{BuildRecentStandardErrorSuffix()}")
                    );
                }
                else
                {
                    pending.TrySetCanceled(cancellationToken);
                }
            }
        });

        await SendMessageAsync(
            new Dictionary<string, object?>
            {
                ["jsonrpc"] = "2.0",
                ["method"] = method,
                ["id"] = requestId,
                ["params"] = parameters,
            },
            cancellationToken
        );
        return await completionSource.Task.ConfigureAwait(false);
    }

    private async Task SendNotificationAsync(string method, object? parameters, CancellationToken cancellationToken)
    {
        EnsureStarted();
        await SendMessageAsync(
            new Dictionary<string, object?>
            {
                ["jsonrpc"] = "2.0",
                ["method"] = method,
                ["params"] = parameters,
            },
            cancellationToken
        );
    }

    private async Task SendMessageAsync(object payload, CancellationToken cancellationToken)
    {
        EnsureStarted();
        var json = JsonSerializer.Serialize(payload, JsonOptions);
        await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            await _stdin!.WriteLineAsync(json).ConfigureAwait(false);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private async Task ReadLoopAsync(StreamReader stdout, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var line = await stdout.ReadLineAsync(cancellationToken).ConfigureAwait(false);

            if (line is null)
            {
                break;
            }

            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            try
            {
                using var document = JsonDocument.Parse(line);
                var root = document.RootElement;

                if (root.TryGetProperty("id", out var idElement) && !root.TryGetProperty("method", out _))
                {
                    HandleResponse(root, idElement);
                    continue;
                }

                if (root.TryGetProperty("method", out var methodElement))
                {
                    var method = methodElement.GetString() ?? string.Empty;
                    var parameters = root.TryGetProperty("params", out var paramsElement) ? paramsElement.Clone() : (JsonElement?)null;

                    // id を持つ（サーバーからのリクエスト）場合は dynamicTool 呼び出しや承認として扱う
                    if (root.TryGetProperty("id", out var serverReqIdElement) && TryGetRequestId(serverReqIdElement, out var serverRequestId))
                    {
                        HandleServerRequest(method, serverRequestId, parameters);
                    }
                    else
                    {
                        HandleNotification(method, parameters);
                    }
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // 1 行のパース・処理に失敗してもループを継続する
                Debug.WriteLine($"[CodexAppServerClient] ReadLoop 処理エラー: {ex.Message}");
            }
        }

        foreach (var pending in _pendingRequests.ToArray())
        {
            if (_pendingRequests.TryRemove(pending.Key, out var request))
            {
                request.TrySetException(new InvalidOperationException("Codex App Server との接続が切断されました。"));
            }
        }
    }

    private void HandleResponse(JsonElement root, JsonElement idElement)
    {
        if (!TryGetRequestId(idElement, out var requestId))
        {
            return;
        }

        if (!_pendingRequests.TryRemove(requestId, out var pending))
        {
            return;
        }

        if (root.TryGetProperty("error", out var errorElement))
        {
            pending.TrySetException(new InvalidOperationException(BuildErrorMessage(errorElement)));
            return;
        }

        var result = root.TryGetProperty("result", out var resultElement) ? resultElement.Clone() : (JsonElement?)null;
        pending.TrySetResult(result);
    }

    private void HandleServerRequest(string method, int requestId, JsonElement? parameters)
    {
        switch (method)
        {
            case "item/tool/call":
                DynamicToolCallReceived?.Invoke(this, ParseDynamicToolCallRequest(requestId, parameters));
                break;

            case "item/commandExecution/requestApproval":
            case "item/fileChange/requestApproval":
            case "item/permissions/requestApproval":
                ApprovalRequested?.Invoke(this, ParseApprovalRequest(requestId, method, parameters));
                break;
        }
    }

    private void HandleNotification(string method, JsonElement? parameters)
    {
        NotificationReceived?.Invoke(this, new CodexJsonRpcNotification { Method = method, Params = parameters });

        switch (method)
        {
            case "account/login/completed":
                LoginCompleted?.Invoke(this, ParseLoginCompletedNotification(parameters));
                break;

            case "account/updated":
                AccountUpdated?.Invoke(this, ParseAccountUpdatedNotification(parameters));
                break;

            case "thread/started":
                ThreadStarted?.Invoke(this, ParseThreadStartedNotification(parameters));
                break;

            case "turn/started":
                TurnStarted?.Invoke(this, ParseTurnStartedNotification(parameters));
                break;

            case "item/agentMessage/delta":
                AgentMessageDeltaReceived?.Invoke(this, ParseAgentMessageDeltaNotification(parameters));
                break;

            case "item/started":
                ItemStarted?.Invoke(this, ParseItemStartedNotification(parameters));
                break;

            case "item/completed":
                ItemCompleted?.Invoke(this, ParseItemCompletedNotification(parameters));
                break;

            case "turn/completed":
                TurnCompleted?.Invoke(this, ParseTurnCompletedNotification(parameters));
                break;
        }
    }

    private static CodexThreadInfo ParseThreadStartResult(JsonElement? result)
    {
        if (result is not JsonElement element || !element.TryGetProperty("thread", out var threadElement))
        {
            throw new InvalidOperationException("thread/start の応答に thread が含まれていません。");
        }

        return ParseThreadInfo(threadElement);
    }

    private static CodexTurnInfo ParseTurnStartResult(JsonElement? result)
    {
        if (result is not JsonElement element || !element.TryGetProperty("turn", out var turnElement))
        {
            throw new InvalidOperationException("turn/start の応答に turn が含まれていません。");
        }

        return ParseTurnInfo(turnElement);
    }

    private static CodexAccountInfo ParseAccountInfo(JsonElement? result)
    {
        if (result is not JsonElement element)
        {
            return new CodexAccountInfo();
        }

        var requiresOpenAiAuth = element.TryGetProperty("requiresOpenaiAuth", out var requiresElement) && requiresElement.ValueKind == JsonValueKind.True;

        if (!element.TryGetProperty("account", out var accountElement) || accountElement.ValueKind == JsonValueKind.Null)
        {
            return new CodexAccountInfo { RequiresOpenAiAuth = requiresOpenAiAuth, AuthMode = CodexAuthMode.None };
        }

        var accountType = accountElement.TryGetProperty("type", out var typeElement) ? typeElement.GetString() : null;
        return new CodexAccountInfo
        {
            RequiresOpenAiAuth = requiresOpenAiAuth,
            AuthMode = ParseAuthMode(accountType),
            Email = accountElement.TryGetProperty("email", out var emailElement) ? emailElement.GetString() : null,
            PlanType = accountElement.TryGetProperty("planType", out var planElement) ? planElement.GetString() : null,
        };
    }

    private static CodexThreadStartedNotification ParseThreadStartedNotification(JsonElement? parameters)
    {
        if (parameters is not JsonElement element || !element.TryGetProperty("thread", out var threadElement))
        {
            throw new InvalidOperationException("thread/started 通知に thread が含まれていません。");
        }

        return new CodexThreadStartedNotification { Thread = ParseThreadInfo(threadElement) };
    }

    private static CodexTurnStartedNotification ParseTurnStartedNotification(JsonElement? parameters)
    {
        if (parameters is not JsonElement element)
        {
            return new CodexTurnStartedNotification
            {
                ThreadId = string.Empty,
                Turn = new CodexTurnInfo { Id = string.Empty, Status = "inProgress" },
            };
        }

        var threadId =
            element.TryGetProperty("threadId", out var threadIdElement) ? threadIdElement.GetString() ?? string.Empty
            : element.TryGetProperty("thread_id", out var legacyThreadIdElement) ? legacyThreadIdElement.GetString() ?? string.Empty
            : string.Empty;

        if (!element.TryGetProperty("turn", out var turnElement))
        {
            return new CodexTurnStartedNotification
            {
                ThreadId = threadId,
                Turn = new CodexTurnInfo { Id = string.Empty, Status = "inProgress" },
            };
        }

        return new CodexTurnStartedNotification { ThreadId = threadId, Turn = ParseTurnInfo(turnElement) };
    }

    private static CodexAgentMessageDeltaNotification ParseAgentMessageDeltaNotification(JsonElement? parameters)
    {
        if (parameters is not JsonElement element)
        {
            return new CodexAgentMessageDeltaNotification();
        }

        return new CodexAgentMessageDeltaNotification
        {
            ThreadId = element.TryGetProperty("threadId", out var threadIdElement) ? threadIdElement.GetString() : null,
            TurnId = element.TryGetProperty("turnId", out var turnIdElement) ? turnIdElement.GetString() : null,
            Delta = element.TryGetProperty("delta", out var deltaElement) ? deltaElement.GetString() ?? string.Empty : string.Empty,
        };
    }

    private static CodexTurnCompletedNotification ParseTurnCompletedNotification(JsonElement? parameters)
    {
        if (parameters is not JsonElement element)
        {
            return new CodexTurnCompletedNotification
            {
                ThreadId = string.Empty,
                Turn = new CodexTurnInfo { Id = string.Empty, Status = "completed" },
            };
        }

        var threadId =
            element.TryGetProperty("threadId", out var threadIdElement) ? threadIdElement.GetString() ?? string.Empty
            : element.TryGetProperty("thread_id", out var legacyThreadIdElement) ? legacyThreadIdElement.GetString() ?? string.Empty
            : string.Empty;

        if (!element.TryGetProperty("turn", out var turnElement))
        {
            return new CodexTurnCompletedNotification
            {
                ThreadId = threadId,
                Turn = new CodexTurnInfo { Id = string.Empty, Status = "completed" },
            };
        }

        return new CodexTurnCompletedNotification { ThreadId = threadId, Turn = ParseTurnInfo(turnElement) };
    }

    private static CodexItemStartedNotification ParseItemStartedNotification(JsonElement? parameters)
    {
        if (parameters is not JsonElement element)
        {
            return new CodexItemStartedNotification();
        }

        var itemId = default(string?);
        var itemType = default(string?);

        if (element.TryGetProperty("item", out var itemElement))
        {
            itemId = itemElement.TryGetProperty("id", out var idEl) ? idEl.GetString() : null;
            itemType = itemElement.TryGetProperty("type", out var typeEl) ? typeEl.GetString() : null;
        }

        return new CodexItemStartedNotification
        {
            ThreadId = element.TryGetProperty("threadId", out var threadIdEl) ? threadIdEl.GetString() : null,
            TurnId = element.TryGetProperty("turnId", out var turnIdEl) ? turnIdEl.GetString() : null,
            ItemId = itemId,
            ItemType = itemType,
        };
    }

    private static CodexItemCompletedNotification ParseItemCompletedNotification(JsonElement? parameters)
    {
        if (parameters is not JsonElement element)
        {
            return new CodexItemCompletedNotification();
        }

        var itemId = default(string?);
        var itemType = default(string?);

        if (element.TryGetProperty("item", out var itemElement))
        {
            itemId = itemElement.TryGetProperty("id", out var idEl) ? idEl.GetString() : null;
            itemType = itemElement.TryGetProperty("type", out var typeEl) ? typeEl.GetString() : null;
        }

        return new CodexItemCompletedNotification
        {
            ThreadId = element.TryGetProperty("threadId", out var threadIdEl) ? threadIdEl.GetString() : null,
            TurnId = element.TryGetProperty("turnId", out var turnIdEl) ? turnIdEl.GetString() : null,
            ItemId = itemId,
            ItemType = itemType,
        };
    }

    private static CodexDynamicToolCallRequest ParseDynamicToolCallRequest(int requestId, JsonElement? parameters)
    {
        if (parameters is not JsonElement element)
        {
            throw new InvalidOperationException("item/tool/call リクエストを解釈できませんでした。");
        }

        return new CodexDynamicToolCallRequest
        {
            RequestId = requestId,
            ThreadId = element.TryGetProperty("threadId", out var threadIdEl) ? threadIdEl.GetString() ?? string.Empty : string.Empty,
            TurnId = element.TryGetProperty("turnId", out var turnIdEl) ? turnIdEl.GetString() ?? string.Empty : string.Empty,
            CallId = element.TryGetProperty("callId", out var callIdEl) ? callIdEl.GetString() ?? string.Empty : string.Empty,
            Tool = element.TryGetProperty("tool", out var toolEl) ? toolEl.GetString() ?? string.Empty : string.Empty,
            Arguments = element.TryGetProperty("arguments", out var argsEl) ? argsEl.Clone() : default,
        };
    }

    private static CodexApprovalRequest ParseApprovalRequest(int requestId, string method, JsonElement? parameters)
    {
        var element = parameters is JsonElement el ? el : default;
        return new CodexApprovalRequest
        {
            RequestId = requestId,
            Method = method,
            ThreadId = element.ValueKind != JsonValueKind.Undefined && element.TryGetProperty("threadId", out var threadIdEl) ? threadIdEl.GetString() : null,
            TurnId = element.ValueKind != JsonValueKind.Undefined && element.TryGetProperty("turnId", out var turnIdEl) ? turnIdEl.GetString() : null,
            ItemId = element.ValueKind != JsonValueKind.Undefined && element.TryGetProperty("itemId", out var itemIdEl) ? itemIdEl.GetString() : null,
        };
    }

    private static CodexThreadInfo ParseThreadInfo(JsonElement threadElement)
    {
        return new CodexThreadInfo
        {
            Id = threadElement.TryGetProperty("id", out var idElement) ? idElement.GetString() ?? string.Empty : string.Empty,
            Preview = threadElement.TryGetProperty("preview", out var previewElement) ? previewElement.GetString() ?? string.Empty : string.Empty,
            ModelProvider = threadElement.TryGetProperty("modelProvider", out var providerElement) ? providerElement.GetString() : null,
            Ephemeral = threadElement.TryGetProperty("ephemeral", out var ephemeralElement) && ephemeralElement.ValueKind == JsonValueKind.True,
        };
    }

    private static CodexTurnInfo ParseTurnInfo(JsonElement turnElement)
    {
        // ターンのエラーメッセージを取得する（turn.error.message）
        string? errorMessage = null;

        if (turnElement.TryGetProperty("error", out var errorElement) && errorElement.ValueKind == JsonValueKind.Object)
        {
            errorMessage = errorElement.TryGetProperty("message", out var msgElement) ? msgElement.GetString() : null;
        }

        return new CodexTurnInfo
        {
            Id = turnElement.TryGetProperty("id", out var idElement) ? idElement.GetString() ?? string.Empty : string.Empty,
            Status = turnElement.TryGetProperty("status", out var statusElement) ? statusElement.GetString() ?? string.Empty : string.Empty,
            Error = errorMessage,
        };
    }

    private static CodexLoginStartResult ParseLoginStartResult(JsonElement? result, CodexLoginType fallbackType)
    {
        if (result is not JsonElement element)
        {
            return new CodexLoginStartResult { Type = fallbackType };
        }

        var rawType = element.TryGetProperty("type", out var typeElement) ? typeElement.GetString() : null;
        return new CodexLoginStartResult
        {
            Type = ParseLoginType(rawType, fallbackType),
            LoginId = element.TryGetProperty("loginId", out var loginIdElement) ? loginIdElement.GetString() : null,
            AuthUrl = element.TryGetProperty("authUrl", out var authUrlElement) ? authUrlElement.GetString() : null,
            VerificationUrl = element.TryGetProperty("verificationUrl", out var verificationElement) ? verificationElement.GetString() : null,
            UserCode = element.TryGetProperty("userCode", out var userCodeElement) ? userCodeElement.GetString() : null,
        };
    }

    private static CodexLoginCompletedNotification ParseLoginCompletedNotification(JsonElement? parameters)
    {
        if (parameters is not JsonElement element)
        {
            return new CodexLoginCompletedNotification { Success = false, Error = "ログイン完了通知を解釈できませんでした。" };
        }

        return new CodexLoginCompletedNotification
        {
            LoginId = element.TryGetProperty("loginId", out var loginIdElement) ? loginIdElement.GetString() : null,
            Success = element.TryGetProperty("success", out var successElement) && successElement.ValueKind == JsonValueKind.True,
            Error = element.TryGetProperty("error", out var errorElement) && errorElement.ValueKind != JsonValueKind.Null ? errorElement.GetString() : null,
        };
    }

    private static CodexAccountUpdatedNotification ParseAccountUpdatedNotification(JsonElement? parameters)
    {
        if (parameters is not JsonElement element)
        {
            return new CodexAccountUpdatedNotification { AuthMode = CodexAuthMode.None };
        }

        return new CodexAccountUpdatedNotification
        {
            AuthMode = ParseAuthMode(element.TryGetProperty("authMode", out var modeElement) ? modeElement.GetString() : null),
            PlanType = element.TryGetProperty("planType", out var planElement) && planElement.ValueKind != JsonValueKind.Null ? planElement.GetString() : null,
        };
    }

    private static CodexAuthMode ParseAuthMode(string? rawMode)
    {
        // 大文字小文字を無視して比較する（サーバーが "apiKey" / "apikey" 等を返す場合に対応）
        return rawMode?.ToLowerInvariant() switch
        {
            "apikey" => CodexAuthMode.ApiKey,
            "chatgpt" => CodexAuthMode.ChatGpt,
            _ => CodexAuthMode.None,
        };
    }

    private static CodexLoginType ParseLoginType(string? rawType, CodexLoginType fallbackType)
    {
        return rawType switch
        {
            "apiKey" => CodexLoginType.ApiKey,
            "chatgpt" => CodexLoginType.ChatGpt,
            "chatgptDeviceCode" => CodexLoginType.ChatGptDeviceCode,
            _ => fallbackType,
        };
    }

    private static bool TryGetRequestId(JsonElement idElement, out int requestId)
    {
        switch (idElement.ValueKind)
        {
            case JsonValueKind.Number:
                return idElement.TryGetInt32(out requestId);
            case JsonValueKind.String:
                return int.TryParse(idElement.GetString(), out requestId);
            default:
                requestId = default;
                return false;
        }
    }

    private static string BuildArguments(string additionalArguments)
    {
        var suffix = string.IsNullOrWhiteSpace(additionalArguments) ? string.Empty : $" {additionalArguments.Trim()}";
        return $"app-server --listen stdio://{suffix}".Trim();
    }

    /// <summary>
    /// codex コマンドが .cmd / .bat の場合は cmd.exe /c でラップして起動します。
    /// これにより UseShellExecute = false でも stdin/stdout リダイレクトが機能します。
    /// </summary>
    private static (string fileName, string arguments) ResolveStartInfo(string executablePath, string appServerArguments)
    {
        // フルパス指定 or PATH 解決で .cmd/.bat を探す
        string resolvedPath = executablePath;

        if (!Path.IsPathRooted(executablePath))
        {
            // PATH から検索して .cmd/.bat かどうか判定する
            foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(Path.PathSeparator))
            {
                foreach (var ext in new[] { ".cmd", ".bat", ".exe", string.Empty })
                {
                    var candidate = Path.Combine(dir, executablePath + ext);

                    if (File.Exists(candidate))
                    {
                        resolvedPath = candidate;
                        break;
                    }
                }

                if (resolvedPath != executablePath)
                {
                    break;
                }
            }
        }

        var extension = Path.GetExtension(resolvedPath).ToLowerInvariant();

        if (extension == ".cmd" || extension == ".bat")
        {
            // cmd.exe /c を使って .cmd/.bat を起動し stdin/stdout リダイレクトを有効にする
            return ("cmd.exe", $"/c \"{resolvedPath}\" {appServerArguments}");
        }

        return (executablePath, appServerArguments);
    }

    private static string BuildErrorMessage(JsonElement errorElement)
    {
        var codeText = errorElement.TryGetProperty("code", out var codeElement) ? codeElement.ToString() : "unknown";
        var messageText = errorElement.TryGetProperty("message", out var messageElement) ? messageElement.GetString() : "Codex App Server エラー";
        return $"{messageText} (code: {codeText})";
    }

    /// <summary>直近の標準エラー出力をユーザー向け補足文として返します。</summary>
    private string BuildRecentStandardErrorSuffix()
    {
        if (_stderrLines.IsEmpty)
        {
            return string.Empty;
        }

        var lines = _stderrLines.ToArray();
        return $" stderr: {string.Join(" | ", lines)}";
    }

    private void EnsureStarted()
    {
        if (!IsStarted)
        {
            throw new InvalidOperationException("Codex App Server はまだ起動していません。");
        }
    }
}
