using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using QuickER.AI.Resources;

namespace QuickER.AI;

/// <summary>Codex App Server と JSON-RPC で対話するクライアントの抽象</summary>
public interface ICodexAppServerClient : IAsyncDisposable
{
    /// <summary>JSON-RPC 通知の受信時に種別を問わず発生する汎用イベント</summary>
    /// <remarks>個別イベント（<see cref="ThreadStarted"/> 等）より先に発生する</remarks>
    event EventHandler<CodexJsonRpcNotification>? NotificationReceived;

    /// <summary>ログイン完了通知（account/login/completed）の受信時に発生する</summary>
    event EventHandler<CodexLoginCompletedNotification>? LoginCompleted;

    /// <summary>アカウント更新通知（account/updated）の受信時に発生する</summary>
    event EventHandler<CodexAccountUpdatedNotification>? AccountUpdated;

    /// <summary>スレッド開始通知（thread/started）の受信時に発生する</summary>
    event EventHandler<CodexThreadStartedNotification>? ThreadStarted;

    /// <summary>ターン開始通知（turn/started）の受信時に発生する</summary>
    event EventHandler<CodexTurnStartedNotification>? TurnStarted;

    /// <summary>エージェントメッセージ差分通知（item/agentMessage/delta）の受信時に発生する</summary>
    event EventHandler<CodexAgentMessageDeltaNotification>? AgentMessageDeltaReceived;

    /// <summary>ターン完了通知（turn/completed）の受信時に発生する</summary>
    event EventHandler<CodexTurnCompletedNotification>? TurnCompleted;

    /// <summary>Codex App Server プロセスが起動済みで通信可能かどうかを示す</summary>
    bool IsStarted { get; }

    /// <summary>codex CLI が利用可能か（PATH 解決できるか）</summary>
    /// <remarks>
    /// <see cref="IClaudeCodeClient.IsAvailable"/> と同じ役割。未検出のまま
    /// <see cref="StartAsync"/> を呼ぶと Win32Exception になるため、UI 側は本判定で先に案内する。
    /// </remarks>
    bool IsAvailable();

    /// <summary>Codex App Server プロセスを起動し、initialize ハンドシェイクを完了する</summary>
    /// <remarks>既に起動済みの場合は何もしない</remarks>
    Task StartAsync(
        CodexAppServerSettings settings,
        string clientName,
        string clientTitle,
        string clientVersion,
        CancellationToken cancellationToken = default
    );

    /// <summary>現在のアカウント状態を取得する</summary>
    /// <param name="refreshToken">認証トークンの更新を要求するかどうか</param>
    Task<CodexAccountInfo> ReadAccountAsync(
        bool refreshToken,
        CancellationToken cancellationToken = default
    );

    /// <summary>API キーによるログインを開始する</summary>
    Task<CodexLoginStartResult> LoginWithApiKeyAsync(
        string apiKey,
        CancellationToken cancellationToken = default
    );

    /// <summary>ChatGPT のブラウザログインを開始する</summary>
    /// <returns>ブラウザで開く認証 URL を含むログイン開始結果</returns>
    Task<CodexLoginStartResult> StartChatGptLoginAsync(
        CancellationToken cancellationToken = default
    );

    /// <summary>現在のアカウントからログアウトする</summary>
    Task LogoutAsync(CancellationToken cancellationToken = default);

    /// <summary>新しい会話スレッドを開始する</summary>
    Task<CodexThreadInfo> StartThreadAsync(
        CodexThreadStartOptions options,
        CancellationToken cancellationToken = default
    );

    /// <summary>指定スレッドにプロンプトを送信して新しいターンを開始する</summary>
    Task<CodexTurnInfo> StartTurnAsync(
        string threadId,
        string prompt,
        CancellationToken cancellationToken = default
    );

    /// <summary>実行中のターンを中断する</summary>
    Task InterruptTurnAsync(
        string threadId,
        string turnId,
        CancellationToken cancellationToken = default
    );

    /// <summary>dynamicTool 呼び出し（サーバー発リクエスト）に実行結果を応答する</summary>
    /// <param name="requestId">応答先の JSON-RPC リクエスト ID</param>
    /// <param name="resultText">ツールの実行結果テキスト</param>
    /// <param name="success">ツール実行の成否</param>
    Task RespondToDynamicToolCallAsync(
        int requestId,
        string resultText,
        bool success,
        CancellationToken cancellationToken = default
    );

    /// <summary>承認リクエストに決定を応答する</summary>
    /// <param name="requestId">応答先の JSON-RPC リクエスト ID</param>
    /// <param name="decision">承認決定（accept / decline 等）</param>
    Task RespondToApprovalAsync(
        int requestId,
        string decision,
        CancellationToken cancellationToken = default
    );

    /// <summary>dynamicTool 呼び出しリクエスト（item/tool/call）の受信時に発生する</summary>
    event EventHandler<CodexDynamicToolCallRequest>? DynamicToolCallReceived;

    /// <summary>アイテム開始通知（item/started）の受信時に発生する</summary>
    event EventHandler<CodexItemStartedNotification>? ItemStarted;

    /// <summary>アイテム完了通知（item/completed）の受信時に発生する</summary>
    event EventHandler<CodexItemCompletedNotification>? ItemCompleted;

    /// <summary>承認リクエスト（commandExecution / fileChange / permissions）の受信時に発生する</summary>
    event EventHandler<CodexApprovalRequest>? ApprovalRequested;
}

/// <summary>Codex App Server を子プロセスとして起動し、stdio 経由で JSON-RPC 2.0 メッセージ（1 行 1 JSON）を送受信するクライアント実装</summary>
/// <remarks>
/// プロトコル: stdin へリクエスト・通知を書き込み、stdout を単一の受信ループ（<see cref="ReadLoopAsync"/>）で読み取る。
/// 受信メッセージは「id のみ＝レスポンス」「method のみ＝通知」「id と method の両方＝サーバー発リクエスト」に分類する。
/// スレッド安全性: 送信は <see cref="_writeLock"/> で直列化し、リクエストとレスポンスの対応付けは
/// 連番 ID をキーとする <see cref="_pendingRequests"/> で行うため、複数スレッドから並行して呼び出せる
/// </remarks>
public sealed class CodexAppServerClient : ICodexAppServerClient
{
    /// <summary>JSON-RPC ペイロードのシリアライズ設定（camelCase 変換、null プロパティの出力省略）</summary>
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>アカウント状態取得の JSON-RPC メソッド名</summary>
    private const string GetAccountMethod = "account/read";

    /// <summary>ログイン開始の JSON-RPC メソッド名</summary>
    private const string LoginAccountMethod = "account/login/start";

    /// <summary>ログアウトの JSON-RPC メソッド名</summary>
    private const string LogoutAccountMethod = "account/logout";

    /// <summary>stdin への書き込みを直列化するロック（並行送信で JSON 行が混在するのを防ぐ）</summary>
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    /// <summary>送信済みリクエスト ID と応答待ち <see cref="TaskCompletionSource{TResult}"/> の対応表</summary>
    private readonly ConcurrentDictionary<
        int,
        TaskCompletionSource<JsonElement?>
    > _pendingRequests = new();

    /// <summary>起動した Codex App Server の子プロセス</summary>
    private Process? _process;

    /// <summary>子プロセスの標準入力ライター（JSON-RPC 送信路）</summary>
    private StreamWriter? _stdin;

    /// <summary>stdout を読み続ける受信ループのタスク</summary>
    private Task? _readerTask;

    /// <summary>受信ループを停止させるためのキャンセルソース</summary>
    private CancellationTokenSource? _readerCts;

    /// <summary>JSON-RPC リクエスト ID の採番カウンター（<see cref="Interlocked.Increment(ref int)"/> で加算）</summary>
    private int _nextRequestId;

    /// <summary>診断用に保持する直近の標準エラー出力（最大 20 行）</summary>
    private readonly ConcurrentQueue<string> _stderrLines = new();

    /// <summary>インスタンスが最終破棄済みかどうか（<see cref="DisposeAsync"/> の二重呼び出しを無害化する）</summary>
    private bool _disposed;

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
    /// <remarks>共有ロケーターへ委譲する（結果はキャッシュせず、呼ぶたびに PATH を走査する）。</remarks>
    public bool IsAvailable() => CodexCliLocator.IsAvailable();

    /// <inheritdoc />
    public async Task StartAsync(
        CodexAppServerSettings settings,
        string clientName,
        string clientTitle,
        string clientVersion,
        CancellationToken cancellationToken = default
    )
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

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
            StandardInputEncoding = new System.Text.UTF8Encoding(
                encoderShouldEmitUTF8Identifier: false
            ),
            StandardOutputEncoding = new System.Text.UTF8Encoding(
                encoderShouldEmitUTF8Identifier: false
            ),
            StandardErrorEncoding = new System.Text.UTF8Encoding(
                encoderShouldEmitUTF8Identifier: false
            ),
        };

        var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };

        if (!process.Start())
        {
            throw new InvalidOperationException(Strings.Codex_ServerStartFailed);
        }

        _process = process;
        _stdin = process.StandardInput;
        _stdin.NewLine = "\n";
        _stdin.AutoFlush = true;
        _readerCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _readerTask = Task.Run(
            () => ReadLoopAsync(process.StandardOutput, _readerCts.Token),
            CancellationToken.None
        );

        // stderr を非同期で読み捨てる（バッファフルによるデッドロックを防ぐ）
        _ = Task.Run(
            async () =>
            {
                try
                {
                    while (!_readerCts.IsCancellationRequested)
                    {
                        var line = await process
                            .StandardError.ReadLineAsync()
                            .ConfigureAwait(false);

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
            // ハンドシェイク失敗時はプロセスだけを終了してフィールドをリセットする
            // （インスタンスは破棄しない＝_writeLock を残し、次回の StartAsync で再接続できるようにする）
            await StopProcessAsync();
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<CodexAccountInfo> ReadAccountAsync(
        bool refreshToken,
        CancellationToken cancellationToken = default
    )
    {
        var result = await SendRequestAsync(
            GetAccountMethod,
            new { refreshToken },
            cancellationToken
        );
        return ParseAccountInfo(result);
    }

    /// <inheritdoc />
    public Task<CodexLoginStartResult> LoginWithApiKeyAsync(
        string apiKey,
        CancellationToken cancellationToken = default
    )
    {
        return StartLoginAsync(
            new { type = "apiKey", apiKey },
            CodexLoginType.ApiKey,
            cancellationToken
        );
    }

    /// <inheritdoc />
    public Task<CodexLoginStartResult> StartChatGptLoginAsync(
        CancellationToken cancellationToken = default
    )
    {
        return StartLoginAsync(new { type = "chatgpt" }, CodexLoginType.ChatGpt, cancellationToken);
    }

    /// <inheritdoc />
    public async Task LogoutAsync(CancellationToken cancellationToken = default)
    {
        await SendRequestAsync(LogoutAccountMethod, null, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<CodexThreadInfo> StartThreadAsync(
        CodexThreadStartOptions options,
        CancellationToken cancellationToken = default
    )
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

        if (!string.IsNullOrWhiteSpace(options.DeveloperInstructions))
        {
            parameters["developerInstructions"] = options.DeveloperInstructions;
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
    public async Task<CodexTurnInfo> StartTurnAsync(
        string threadId,
        string prompt,
        CancellationToken cancellationToken = default
    )
    {
        var result = await SendRequestAsync(
            "turn/start",
            new { threadId, input = new object[] { new CodexTextInputItem { Text = prompt } } },
            cancellationToken
        );

        return ParseTurnStartResult(result);
    }

    /// <inheritdoc />
    public async Task InterruptTurnAsync(
        string threadId,
        string turnId,
        CancellationToken cancellationToken = default
    )
    {
        await SendRequestAsync("turn/interrupt", new { threadId, turnId }, cancellationToken);
    }

    /// <inheritdoc />
    public async Task RespondToDynamicToolCallAsync(
        int requestId,
        string resultText,
        bool success,
        CancellationToken cancellationToken = default
    )
    {
        EnsureStarted();
        var response = new Dictionary<string, object?>
        {
            ["id"] = requestId,
            ["result"] = new
            {
                contentItems = new object[] { new { type = "inputText", text = resultText } },
                success,
            },
        };
        await SendMessageAsync(response, cancellationToken);
    }

    /// <inheritdoc />
    public async Task RespondToApprovalAsync(
        int requestId,
        string decision,
        CancellationToken cancellationToken = default
    )
    {
        EnsureStarted();
        var response = new Dictionary<string, object?>
        {
            ["id"] = requestId,
            ["result"] = new { decision },
        };
        await SendMessageAsync(response, cancellationToken);
    }

    /// <inheritdoc />
    /// <remarks>
    /// インスタンスの最終破棄。プロセス停止（<see cref="StopProcessAsync"/>）に加えて
    /// 再利用不能になる <see cref="_writeLock"/> の破棄まで行うため、再接続する可能性がある場面
    /// （ハンドシェイク失敗時など）では呼ばず <see cref="StopProcessAsync"/> を使う。二重呼び出しは無害
    /// </remarks>
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        await StopProcessAsync();
        _writeLock.Dispose();
    }

    /// <summary>子プロセスと受信ループを停止し、再接続できる状態（未起動と同じ状態）へ戻す</summary>
    /// <remarks>
    /// <see cref="_writeLock"/> は破棄しない＝再度 <see cref="StartAsync"/> を呼べば同じインスタンスで再接続できる。
    /// 応答待ちのリクエストは接続断として即座に解消し、タイムアウト（30 秒）まで待たせない
    /// </remarks>
    internal async Task StopProcessAsync()
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
                // 終了時の読み取り例外は破棄する
            }

            _readerTask = null;
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
                // 既に終了している場合などの例外は破棄する
            }

            _process.Dispose();
            _process = null;
        }

        _readerCts?.Dispose();
        _readerCts = null;

        // 受信ループがキャンセル例外で抜けた場合は末尾の解消処理を通らないため、ここでも取りこぼしを解消する
        FailPendingRequests();
    }

    /// <summary>account/login/start リクエストを送信し、ログイン開始結果を解析して返す</summary>
    /// <param name="fallbackType">応答に type が含まれない場合に採用するログイン方式</param>
    private async Task<CodexLoginStartResult> StartLoginAsync(
        object parameters,
        CodexLoginType fallbackType,
        CancellationToken cancellationToken
    )
    {
        var result = await SendRequestAsync(LoginAccountMethod, parameters, cancellationToken);
        return ParseLoginStartResult(result, fallbackType);
    }

    /// <summary>JSON-RPC リクエストを送信し、対応するレスポンスの result を待機して返す</summary>
    /// <returns>レスポンスの result 要素、result が無い場合は null</returns>
    /// <exception cref="TimeoutException">30 秒以内にレスポンスが返らなかった場合</exception>
    /// <exception cref="InvalidOperationException">サーバーがエラーレスポンスを返した、または接続が切断された場合</exception>
    private async Task<JsonElement?> SendRequestAsync(
        string method,
        object? parameters,
        CancellationToken cancellationToken
    )
    {
        EnsureStarted();

        var requestId = Interlocked.Increment(ref _nextRequestId);
        var completionSource = new TaskCompletionSource<JsonElement?>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        _pendingRequests[requestId] = completionSource;

        // リクエストタイムアウト（30 秒）+ 呼び出し元キャンセルを合成する
        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            timeoutCts.Token
        );

        using var cancellationRegistration = linkedCts.Token.Register(() =>
        {
            if (_pendingRequests.TryRemove(requestId, out var pending))
            {
                if (timeoutCts.IsCancellationRequested)
                {
                    pending.TrySetException(
                        new TimeoutException(
                            string.Format(
                                Strings.Codex_ResponseTimeout,
                                method,
                                BuildRecentStandardErrorSuffix()
                            )
                        )
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

    /// <summary>JSON-RPC 通知（id なし、応答を待たないメッセージ）を送信する</summary>
    private async Task SendNotificationAsync(
        string method,
        object? parameters,
        CancellationToken cancellationToken
    )
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

    /// <summary>ペイロードを JSON にシリアライズし、書き込みロック下で stdin へ 1 行として送信する</summary>
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

    /// <summary>stdout を 1 行ずつ読み取り、レスポンス・サーバー発リクエスト・通知に振り分ける受信ループ</summary>
    /// <remarks>
    /// イベントは受信ループのスレッド上で発火するため、購読側で UI スレッドへのディスパッチが必要。
    /// ループ終了時（EOF・キャンセル）は応答待ちの全リクエストを切断エラーで完了させ、待機側のハングを防ぐ
    /// </remarks>
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

                if (
                    root.TryGetProperty("id", out var idElement)
                    && !root.TryGetProperty("method", out _)
                )
                {
                    HandleResponse(root, idElement);
                    continue;
                }

                if (root.TryGetProperty("method", out var methodElement))
                {
                    var method = methodElement.GetString() ?? string.Empty;
                    var parameters = root.TryGetProperty("params", out var paramsElement)
                        ? paramsElement.Clone()
                        : (JsonElement?)null;

                    // id を持つ（サーバーからのリクエスト）場合は dynamicTool 呼び出しや承認として扱う
                    if (
                        root.TryGetProperty("id", out var serverReqIdElement)
                        && TryGetRequestId(serverReqIdElement, out var serverRequestId)
                    )
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

        FailPendingRequests();
    }

    /// <summary>応答待ちの全リクエストを接続断エラーで完了させ、待機側のハングを防ぐ</summary>
    private void FailPendingRequests()
    {
        foreach (var pending in _pendingRequests.ToArray())
        {
            if (_pendingRequests.TryRemove(pending.Key, out var request))
            {
                request.TrySetException(
                    new InvalidOperationException(Strings.Codex_ConnectionClosed)
                );
            }
        }
    }

    /// <summary>レスポンスを応答待ちリクエストへ引き渡し、error なら例外、result なら値として完了させる</summary>
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

        var result = root.TryGetProperty("result", out var resultElement)
            ? resultElement.Clone()
            : (JsonElement?)null;
        pending.TrySetResult(result);
    }

    /// <summary>サーバー発リクエスト（dynamicTool 呼び出し・各種承認要求）を対応するイベントへ振り分ける</summary>
    /// <remarks>応答はイベント購読側が <see cref="RespondToDynamicToolCallAsync"/> / <see cref="RespondToApprovalAsync"/> で返す</remarks>
    private void HandleServerRequest(string method, int requestId, JsonElement? parameters)
    {
        switch (method)
        {
            case "item/tool/call":
                DynamicToolCallReceived?.Invoke(
                    this,
                    ParseDynamicToolCallRequest(requestId, parameters)
                );
                break;

            case "item/commandExecution/requestApproval":
            case "item/fileChange/requestApproval":
            case "item/permissions/requestApproval":
                ApprovalRequested?.Invoke(
                    this,
                    ParseApprovalRequest(requestId, method, parameters)
                );
                break;
        }
    }

    /// <summary>通知を解析し、汎用イベント <see cref="NotificationReceived"/> と各種別イベントへ振り分ける</summary>
    private void HandleNotification(string method, JsonElement? parameters)
    {
        NotificationReceived?.Invoke(
            this,
            new CodexJsonRpcNotification { Method = method, Params = parameters }
        );

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
                AgentMessageDeltaReceived?.Invoke(
                    this,
                    ParseAgentMessageDeltaNotification(parameters)
                );
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

    /// <summary>thread/start レスポンスからスレッド情報を取り出す</summary>
    /// <exception cref="InvalidOperationException">応答に thread 要素が含まれない場合</exception>
    private static CodexThreadInfo ParseThreadStartResult(JsonElement? result)
    {
        if (
            result is not JsonElement element
            || !element.TryGetProperty("thread", out var threadElement)
        )
        {
            throw new InvalidOperationException(Strings.Codex_ThreadStartMissingThread);
        }

        return ParseThreadInfo(threadElement);
    }

    /// <summary>turn/start レスポンスからターン情報を取り出す</summary>
    /// <exception cref="InvalidOperationException">応答に turn 要素が含まれない場合</exception>
    private static CodexTurnInfo ParseTurnStartResult(JsonElement? result)
    {
        if (
            result is not JsonElement element
            || !element.TryGetProperty("turn", out var turnElement)
        )
        {
            throw new InvalidOperationException(Strings.Codex_TurnStartMissingTurn);
        }

        return ParseTurnInfo(turnElement);
    }

    /// <summary>account/read レスポンスをアカウント情報へ変換する</summary>
    /// <remarks>account 要素が null の場合は未ログイン（<see cref="CodexAuthMode.None"/>）として扱う</remarks>
    private static CodexAccountInfo ParseAccountInfo(JsonElement? result)
    {
        if (result is not JsonElement element)
        {
            return new CodexAccountInfo();
        }

        var requiresOpenAiAuth =
            element.TryGetProperty("requiresOpenaiAuth", out var requiresElement)
            && requiresElement.ValueKind == JsonValueKind.True;

        if (
            !element.TryGetProperty("account", out var accountElement)
            || accountElement.ValueKind == JsonValueKind.Null
        )
        {
            return new CodexAccountInfo
            {
                RequiresOpenAiAuth = requiresOpenAiAuth,
                AuthMode = CodexAuthMode.None,
            };
        }

        var accountType = accountElement.TryGetProperty("type", out var typeElement)
            ? typeElement.GetString()
            : null;
        return new CodexAccountInfo
        {
            RequiresOpenAiAuth = requiresOpenAiAuth,
            AuthMode = ParseAuthMode(accountType),
            Email = accountElement.TryGetProperty("email", out var emailElement)
                ? emailElement.GetString()
                : null,
            PlanType = accountElement.TryGetProperty("planType", out var planElement)
                ? planElement.GetString()
                : null,
        };
    }

    /// <summary>thread/started 通知のパラメータを解析する</summary>
    /// <exception cref="InvalidOperationException">通知に thread 要素が含まれない場合</exception>
    private static CodexThreadStartedNotification ParseThreadStartedNotification(
        JsonElement? parameters
    )
    {
        if (
            parameters is not JsonElement element
            || !element.TryGetProperty("thread", out var threadElement)
        )
        {
            throw new InvalidOperationException(Strings.Codex_ThreadStartedMissingThread);
        }

        return new CodexThreadStartedNotification { Thread = ParseThreadInfo(threadElement) };
    }

    /// <summary>turn/started 通知のパラメータを解析する</summary>
    /// <remarks>threadId は camelCase と旧形式（thread_id）の両方に対応し、欠落要素は既定値で補完する</remarks>
    private static CodexTurnStartedNotification ParseTurnStartedNotification(
        JsonElement? parameters
    )
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
            element.TryGetProperty("threadId", out var threadIdElement)
                ? threadIdElement.GetString() ?? string.Empty
            : element.TryGetProperty("thread_id", out var legacyThreadIdElement)
                ? legacyThreadIdElement.GetString() ?? string.Empty
            : string.Empty;

        if (!element.TryGetProperty("turn", out var turnElement))
        {
            return new CodexTurnStartedNotification
            {
                ThreadId = threadId,
                Turn = new CodexTurnInfo { Id = string.Empty, Status = "inProgress" },
            };
        }

        return new CodexTurnStartedNotification
        {
            ThreadId = threadId,
            Turn = ParseTurnInfo(turnElement),
        };
    }

    /// <summary>item/agentMessage/delta 通知のパラメータを解析する</summary>
    private static CodexAgentMessageDeltaNotification ParseAgentMessageDeltaNotification(
        JsonElement? parameters
    )
    {
        if (parameters is not JsonElement element)
        {
            return new CodexAgentMessageDeltaNotification();
        }

        return new CodexAgentMessageDeltaNotification
        {
            ThreadId = element.TryGetProperty("threadId", out var threadIdElement)
                ? threadIdElement.GetString()
                : null,
            TurnId = element.TryGetProperty("turnId", out var turnIdElement)
                ? turnIdElement.GetString()
                : null,
            Delta = element.TryGetProperty("delta", out var deltaElement)
                ? deltaElement.GetString() ?? string.Empty
                : string.Empty,
        };
    }

    /// <summary>turn/completed 通知のパラメータを解析する</summary>
    /// <remarks>threadId は camelCase と旧形式（thread_id）の両方に対応し、欠落要素は既定値で補完する</remarks>
    private static CodexTurnCompletedNotification ParseTurnCompletedNotification(
        JsonElement? parameters
    )
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
            element.TryGetProperty("threadId", out var threadIdElement)
                ? threadIdElement.GetString() ?? string.Empty
            : element.TryGetProperty("thread_id", out var legacyThreadIdElement)
                ? legacyThreadIdElement.GetString() ?? string.Empty
            : string.Empty;

        if (!element.TryGetProperty("turn", out var turnElement))
        {
            return new CodexTurnCompletedNotification
            {
                ThreadId = threadId,
                Turn = new CodexTurnInfo { Id = string.Empty, Status = "completed" },
            };
        }

        return new CodexTurnCompletedNotification
        {
            ThreadId = threadId,
            Turn = ParseTurnInfo(turnElement),
        };
    }

    /// <summary>item/started 通知のパラメータを解析する</summary>
    private static CodexItemStartedNotification ParseItemStartedNotification(
        JsonElement? parameters
    )
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
            itemType = itemElement.TryGetProperty("type", out var typeEl)
                ? typeEl.GetString()
                : null;
        }

        return new CodexItemStartedNotification
        {
            ThreadId = element.TryGetProperty("threadId", out var threadIdEl)
                ? threadIdEl.GetString()
                : null,
            TurnId = element.TryGetProperty("turnId", out var turnIdEl)
                ? turnIdEl.GetString()
                : null,
            ItemId = itemId,
            ItemType = itemType,
        };
    }

    /// <summary>item/completed 通知のパラメータを解析する</summary>
    private static CodexItemCompletedNotification ParseItemCompletedNotification(
        JsonElement? parameters
    )
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
            itemType = itemElement.TryGetProperty("type", out var typeEl)
                ? typeEl.GetString()
                : null;
        }

        return new CodexItemCompletedNotification
        {
            ThreadId = element.TryGetProperty("threadId", out var threadIdEl)
                ? threadIdEl.GetString()
                : null,
            TurnId = element.TryGetProperty("turnId", out var turnIdEl)
                ? turnIdEl.GetString()
                : null,
            ItemId = itemId,
            ItemType = itemType,
        };
    }

    /// <summary>item/tool/call リクエストのパラメータを解析する</summary>
    /// <exception cref="InvalidOperationException">パラメータが存在しない場合</exception>
    private static CodexDynamicToolCallRequest ParseDynamicToolCallRequest(
        int requestId,
        JsonElement? parameters
    )
    {
        if (parameters is not JsonElement element)
        {
            throw new InvalidOperationException(Strings.Codex_ToolCallUninterpretable);
        }

        return new CodexDynamicToolCallRequest
        {
            RequestId = requestId,
            ThreadId = element.TryGetProperty("threadId", out var threadIdEl)
                ? threadIdEl.GetString() ?? string.Empty
                : string.Empty,
            TurnId = element.TryGetProperty("turnId", out var turnIdEl)
                ? turnIdEl.GetString() ?? string.Empty
                : string.Empty,
            CallId = element.TryGetProperty("callId", out var callIdEl)
                ? callIdEl.GetString() ?? string.Empty
                : string.Empty,
            Tool = element.TryGetProperty("tool", out var toolEl)
                ? toolEl.GetString() ?? string.Empty
                : string.Empty,
            Arguments = element.TryGetProperty("arguments", out var argsEl)
                ? argsEl.Clone()
                : default,
        };
    }

    /// <summary>承認リクエスト（requestApproval 系メソッド）のパラメータを解析する</summary>
    private static CodexApprovalRequest ParseApprovalRequest(
        int requestId,
        string method,
        JsonElement? parameters
    )
    {
        var element = parameters is JsonElement el ? el : default;
        return new CodexApprovalRequest
        {
            RequestId = requestId,
            Method = method,
            ThreadId =
                element.ValueKind != JsonValueKind.Undefined
                && element.TryGetProperty("threadId", out var threadIdEl)
                    ? threadIdEl.GetString()
                    : null,
            TurnId =
                element.ValueKind != JsonValueKind.Undefined
                && element.TryGetProperty("turnId", out var turnIdEl)
                    ? turnIdEl.GetString()
                    : null,
            ItemId =
                element.ValueKind != JsonValueKind.Undefined
                && element.TryGetProperty("itemId", out var itemIdEl)
                    ? itemIdEl.GetString()
                    : null,
        };
    }

    /// <summary>thread 要素をスレッド情報へ変換する</summary>
    private static CodexThreadInfo ParseThreadInfo(JsonElement threadElement)
    {
        return new CodexThreadInfo
        {
            Id = threadElement.TryGetProperty("id", out var idElement)
                ? idElement.GetString() ?? string.Empty
                : string.Empty,
            Preview = threadElement.TryGetProperty("preview", out var previewElement)
                ? previewElement.GetString() ?? string.Empty
                : string.Empty,
            ModelProvider = threadElement.TryGetProperty("modelProvider", out var providerElement)
                ? providerElement.GetString()
                : null,
            Ephemeral =
                threadElement.TryGetProperty("ephemeral", out var ephemeralElement)
                && ephemeralElement.ValueKind == JsonValueKind.True,
        };
    }

    /// <summary>turn 要素をターン情報へ変換する</summary>
    private static CodexTurnInfo ParseTurnInfo(JsonElement turnElement)
    {
        // ターン失敗時のエラーメッセージは turn.error.message に格納されている
        string? errorMessage = null;

        if (
            turnElement.TryGetProperty("error", out var errorElement)
            && errorElement.ValueKind == JsonValueKind.Object
        )
        {
            errorMessage = errorElement.TryGetProperty("message", out var msgElement)
                ? msgElement.GetString()
                : null;
        }

        return new CodexTurnInfo
        {
            Id = turnElement.TryGetProperty("id", out var idElement)
                ? idElement.GetString() ?? string.Empty
                : string.Empty,
            Status = turnElement.TryGetProperty("status", out var statusElement)
                ? statusElement.GetString() ?? string.Empty
                : string.Empty,
            Error = errorMessage,
        };
    }

    /// <summary>account/login/start レスポンスをログイン開始結果へ変換する</summary>
    private static CodexLoginStartResult ParseLoginStartResult(
        JsonElement? result,
        CodexLoginType fallbackType
    )
    {
        if (result is not JsonElement element)
        {
            return new CodexLoginStartResult { Type = fallbackType };
        }

        var rawType = element.TryGetProperty("type", out var typeElement)
            ? typeElement.GetString()
            : null;
        return new CodexLoginStartResult
        {
            Type = ParseLoginType(rawType, fallbackType),
            LoginId = element.TryGetProperty("loginId", out var loginIdElement)
                ? loginIdElement.GetString()
                : null,
            AuthUrl = element.TryGetProperty("authUrl", out var authUrlElement)
                ? authUrlElement.GetString()
                : null,
        };
    }

    /// <summary>account/login/completed 通知のパラメータを解析する</summary>
    private static CodexLoginCompletedNotification ParseLoginCompletedNotification(
        JsonElement? parameters
    )
    {
        if (parameters is not JsonElement element)
        {
            return new CodexLoginCompletedNotification
            {
                Success = false,
                Error = Strings.Codex_LoginCompletedUninterpretable,
            };
        }

        return new CodexLoginCompletedNotification
        {
            LoginId = element.TryGetProperty("loginId", out var loginIdElement)
                ? loginIdElement.GetString()
                : null,
            Success =
                element.TryGetProperty("success", out var successElement)
                && successElement.ValueKind == JsonValueKind.True,
            Error =
                element.TryGetProperty("error", out var errorElement)
                && errorElement.ValueKind != JsonValueKind.Null
                    ? errorElement.GetString()
                    : null,
        };
    }

    /// <summary>account/updated 通知のパラメータを解析する</summary>
    private static CodexAccountUpdatedNotification ParseAccountUpdatedNotification(
        JsonElement? parameters
    )
    {
        if (parameters is not JsonElement element)
        {
            return new CodexAccountUpdatedNotification { AuthMode = CodexAuthMode.None };
        }

        return new CodexAccountUpdatedNotification
        {
            AuthMode = ParseAuthMode(
                element.TryGetProperty("authMode", out var modeElement)
                    ? modeElement.GetString()
                    : null
            ),
            PlanType =
                element.TryGetProperty("planType", out var planElement)
                && planElement.ValueKind != JsonValueKind.Null
                    ? planElement.GetString()
                    : null,
        };
    }

    /// <summary>認証モード文字列を <see cref="CodexAuthMode"/> へ変換する</summary>
    /// <remarks>サーバーが "apiKey" / "apikey" 等の表記揺れを返す場合に備え、大文字小文字を無視して比較する</remarks>
    private static CodexAuthMode ParseAuthMode(string? rawMode)
    {
        return rawMode?.ToLowerInvariant() switch
        {
            "apikey" => CodexAuthMode.ApiKey,
            "chatgpt" => CodexAuthMode.ChatGpt,
            _ => CodexAuthMode.None,
        };
    }

    /// <summary>ログイン種別文字列を <see cref="CodexLoginType"/> へ変換する</summary>
    /// <param name="fallbackType">未知の文字列・null の場合に採用するログイン方式</param>
    private static CodexLoginType ParseLoginType(string? rawType, CodexLoginType fallbackType)
    {
        return rawType switch
        {
            "apiKey" => CodexLoginType.ApiKey,
            "chatgpt" => CodexLoginType.ChatGpt,
            _ => fallbackType,
        };
    }

    /// <summary>JSON-RPC の id 要素を int として解釈する（数値・数値文字列の両形式に対応）</summary>
    /// <returns>解釈に成功した場合 true</returns>
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

    /// <summary>codex コマンドに渡す app-server 起動引数（stdio リッスン指定 + 追加引数）を組み立てる</summary>
    private static string BuildArguments(string additionalArguments)
    {
        var suffix = string.IsNullOrWhiteSpace(additionalArguments)
            ? string.Empty
            : $" {additionalArguments.Trim()}";
        return $"app-server --listen stdio://{suffix}".Trim();
    }

    /// <summary>プロセス起動に使う実行ファイル名と引数を決定する</summary>
    /// <remarks>
    /// codex コマンドの実体が .cmd / .bat（npm のシム等）の場合、<c>UseShellExecute = false</c> では直接起動できないため
    /// cmd.exe /c でラップして stdin/stdout のリダイレクトを機能させる
    /// </remarks>
    internal static (string fileName, string arguments) ResolveStartInfo(
        string executablePath,
        string appServerArguments
    )
    {
        string resolvedPath = executablePath;

        if (!Path.IsPathRooted(executablePath))
        {
            // 相対指定の場合は PATH を走査して実体を特定する（拡張子で起動方法を判定するため）
            foreach (
                var dir in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(
                    Path.PathSeparator
                )
            )
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
            // 引用符を含むパス（正規の Windows パスには現れない）は引用の切断＝コマンド挿入につながるため起動前に拒否する
            if (resolvedPath.Contains('"'))
            {
                throw new InvalidOperationException(
                    string.Format(Strings.Codex_PathHasQuote, resolvedPath)
                );
            }

            // 引数側は cmd のメタ文字（引用符・連結・リダイレクト・エスケープ・環境変数展開）が
            // 引用の外で解釈されコマンド挿入につながるため、含まれていたら起動前に拒否する
            var metaIndex = appServerArguments.IndexOfAny(['"', '&', '|', '<', '>', '^', '%']);

            if (metaIndex >= 0)
            {
                throw new InvalidOperationException(
                    string.Format(
                        Strings.Codex_ArgHasCmdMeta,
                        appServerArguments[metaIndex],
                        appServerArguments
                    )
                );
            }

            // バッチファイルは cmd.exe /c 経由で起動しないとリダイレクトが機能しない。
            // /d は AutoRun レジストリコマンドの実行を抑止し、/s は外側の引用符で囲んだ全体を
            // 1 つのコマンド行として扱わせて引用符の解釈を決定的にする
            return ("cmd.exe", $"/d /s /c \"\"{resolvedPath}\" {appServerArguments}\"");
        }

        return (executablePath, appServerArguments);
    }

    /// <summary>JSON-RPC エラーレスポンスの error 要素からユーザー向けエラーメッセージを組み立てる</summary>
    private static string BuildErrorMessage(JsonElement errorElement)
    {
        var codeText = errorElement.TryGetProperty("code", out var codeElement)
            ? codeElement.ToString()
            : "unknown";
        var messageText = errorElement.TryGetProperty("message", out var messageElement)
            ? messageElement.GetString()
            : Strings.Codex_ServerErrorFallback;
        return $"{messageText} (code: {codeText})";
    }

    /// <summary>直近の標準エラー出力をタイムアウトメッセージへの補足文として返す</summary>
    /// <returns>stderr が空の場合は空文字列</returns>
    private string BuildRecentStandardErrorSuffix()
    {
        if (_stderrLines.IsEmpty)
        {
            return string.Empty;
        }

        var lines = _stderrLines.ToArray();
        return $" stderr: {string.Join(" | ", lines)}";
    }

    /// <summary>サーバープロセスが通信可能であることを検証する</summary>
    /// <exception cref="InvalidOperationException">未起動または既に終了している場合</exception>
    private void EnsureStarted()
    {
        if (!IsStarted)
        {
            throw new InvalidOperationException(Strings.Codex_ServerNotStarted);
        }
    }
}
