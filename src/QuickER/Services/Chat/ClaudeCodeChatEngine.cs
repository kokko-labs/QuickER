using System.IO;
using System.Text.Json;

namespace QuickER.Services.Chat;

/// <summary>
/// ローカルの Claude Code CLI をヘッドレスで駆動する <see cref="IErChatEngine"/>。
/// ER ツールはプロセス内 <see cref="ErDiagramMcpServer"/>（HTTP/SSE MCP）で公開し、Claude Code から呼ばせる。
/// 認証はユーザーの claude 設定をそのまま使い、未ログイン時は案内する。
/// </summary>
public sealed class ClaudeCodeChatEngine : IErChatEngine
{
    /// <summary>許可するツール指定（MCP サーバー名配下のすべて）</summary>
    private const string AllowedTool = "mcp__" + ErDiagramMcpServer.ServerName;

    private readonly IClaudeCodeClient _client;
    private readonly IErDiagramToolHost? _toolHost;
    private readonly IUiDispatcher _dispatcher;

    private ErDiagramMcpServer? _mcpServer;
    private string _workingDirectory = string.Empty;
    private string _mcpConfigPath = string.Empty;
    private string? _sessionId;
    private bool _initialized;
    private CancellationTokenSource? _turnCts;

    private const string PendingGuidance = "「再確認」を押すとログイン状態を確認できます。";
    private const string InstallGuidance =
        "Claude Code をインストールし、PATH を通してください。";
    private const string LoggedInGuidance = "ローカルの Claude Code をそのまま使用します。";
    private const string NotLoggedInGuidance =
        "ターミナルで claude を起動し /login で認証後、「再確認」を押してください。";
    private const string InconclusiveGuidance = "しばらくして「再確認」を押してください。";

    /// <summary>使用するモデルエイリアス（空なら Claude Code 既定）</summary>
    public string Model { get; set; } = string.Empty;

    /// <summary>状態サマリー（UI 表示用）</summary>
    public string StatusSummary { get; private set; } = "未確認";

    /// <summary>状態ドットの健全度（緑/灰/赤）</summary>
    public ConnectionHealth StatusLevel { get; private set; } = ConnectionHealth.Pending;

    /// <summary>下段に表示する状態依存の案内文</summary>
    public string Guidance { get; private set; } = PendingGuidance;

    /// <summary>状態サマリー・健全度・案内のいずれかが変化したときに発火する</summary>
    public event EventHandler? StatusSummaryChanged;

    /// <inheritdoc />
    public event EventHandler<string>? AssistantDeltaReceived;

    /// <inheritdoc />
    public event EventHandler<ErChatToolActivity>? ToolActivityReceived;

    /// <inheritdoc />
    public event EventHandler<ErChatTurnResult>? TurnCompleted;

    /// <inheritdoc />
    public event EventHandler<string>? StatusChanged;

    /// <summary>クライアント・ツールホスト・ディスパッチャを指定して生成する</summary>
    public ClaudeCodeChatEngine(
        IClaudeCodeClient client,
        IErDiagramToolHost? toolHost,
        IUiDispatcher dispatcher
    )
    {
        _client = client;
        _toolHost = toolHost;
        _dispatcher = dispatcher;
    }

    /// <inheritdoc />
    public bool IsReady => _initialized && _client.IsAvailable();

    /// <inheritdoc />
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (!_client.IsAvailable())
        {
            UpdateStatus(
                "Claude Code が見つかりません",
                ConnectionHealth.NeedsAction,
                InstallGuidance
            );
            _initialized = false;
            return;
        }

        if (_mcpServer is null && _toolHost is not null)
        {
            _workingDirectory = CreateWorkingDirectory();
            _mcpServer = new ErDiagramMcpServer(ExecuteTool);
            await _mcpServer.StartAsync(cancellationToken).ConfigureAwait(false);
            _mcpConfigPath = WriteMcpConfig(_mcpServer);
        }
        else if (_workingDirectory.Length == 0)
        {
            _workingDirectory = CreateWorkingDirectory();
        }

        _initialized = true;

        // 検出はできたがログインは未確認（プローブは明示操作時のみ）。緑を偽装せず灰にする。
        UpdateStatus("未確認", ConnectionHealth.Pending, PendingGuidance);
    }

    /// <summary>
    /// 軽量ログインプローブで状態を取り直す（「再確認」ボタン）。
    /// 検出不可なら赤、ログイン済みなら緑、未ログインなら赤＋案内に更新する。
    /// </summary>
    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        if (!_client.IsAvailable())
        {
            UpdateStatus(
                "Claude Code が見つかりません",
                ConnectionHealth.NeedsAction,
                InstallGuidance
            );
            return;
        }

        if (!_initialized)
        {
            await InitializeAsync(cancellationToken).ConfigureAwait(false);
        }

        var result = await _client.ProbeLoginAsync(cancellationToken).ConfigureAwait(false);

        switch (result)
        {
            case ClaudeLoginProbeResult.LoggedIn:
                UpdateStatus("ログイン済み", ConnectionHealth.Ready, LoggedInGuidance);
                break;
            case ClaudeLoginProbeResult.NotLoggedIn:
                UpdateStatus("未ログイン", ConnectionHealth.NeedsAction, NotLoggedInGuidance);
                break;
            default:
                UpdateStatus(
                    "ログイン状態を確認できませんでした",
                    ConnectionHealth.NeedsAction,
                    InconclusiveGuidance
                );
                break;
        }
    }

    /// <inheritdoc />
    public Task StartConversationAsync(CancellationToken cancellationToken = default)
    {
        _sessionId = null;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task SendAsync(string prompt, CancellationToken cancellationToken = default)
    {
        if (!_initialized)
        {
            await InitializeAsync(cancellationToken).ConfigureAwait(false);
        }

        _turnCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var token = _turnCts.Token;
        StatusChanged?.Invoke(this, "Claude Code が処理中です...");

        var options = new ClaudeCodeLaunchOptions(
            Model,
            _toolHost is not null ? ErDesignRules.BuildChatSystemPrompt() : string.Empty,
            _mcpConfigPath,
            AllowedTool,
            _workingDirectory
        );

        try
        {
            var outcome = await _client
                .RunTurnAsync(
                    prompt,
                    _sessionId,
                    options,
                    text => AssistantDeltaReceived?.Invoke(this, text),
                    token
                )
                .ConfigureAwait(false);

            if (!string.IsNullOrWhiteSpace(outcome.SessionId))
            {
                _sessionId = outcome.SessionId;
            }

            if (outcome.NotLoggedIn)
            {
                // 実ターンで未ログインが判明 → 赤に反映
                UpdateStatus("未ログイン", ConnectionHealth.NeedsAction, NotLoggedInGuidance);
                TurnCompleted?.Invoke(
                    this,
                    new ErChatTurnResult(
                        false,
                        "Claude Code が未ログインです。ターミナルで `claude` を起動し /login でログインしてください。"
                    )
                );
            }
            else if (outcome.Success)
            {
                // ターンが通った＝ログイン済み → 緑に反映
                UpdateStatus("ログイン済み", ConnectionHealth.Ready, LoggedInGuidance);
                TurnCompleted?.Invoke(this, new ErChatTurnResult(true, null));
            }
            else
            {
                TurnCompleted?.Invoke(this, new ErChatTurnResult(false, outcome.Error));
            }
        }
        catch (Exception ex)
        {
            TurnCompleted?.Invoke(this, new ErChatTurnResult(false, ex.Message));
        }
        finally
        {
            _turnCts?.Dispose();
            _turnCts = null;
        }
    }

    /// <inheritdoc />
    public Task InterruptAsync(CancellationToken cancellationToken = default)
    {
        _client.Interrupt();
        _turnCts?.Cancel();
        return Task.CompletedTask;
    }

    /// <summary>MCP からのツール呼び出しを UI スレッドで実行し、活動を通知して結果を返す</summary>
    private (string Result, bool Success) ExecuteTool(string toolName, string argumentsJson)
    {
        var (result, success) = _toolHost is null
            ? ("ツールは利用できません。", false)
            : _dispatcher.Invoke(() => _toolHost.Execute(toolName, argumentsJson));

        ToolActivityReceived?.Invoke(this, new ErChatToolActivity(toolName, result, success));
        return (result, success);
    }

    /// <summary>MCP サーバーの URL・トークンから mcp-config ファイルを書き出し、そのパスを返す</summary>
    private string WriteMcpConfig(ErDiagramMcpServer server)
    {
        var config = new
        {
            mcpServers = new Dictionary<string, object>
            {
                [ErDiagramMcpServer.ServerName] = new
                {
                    type = "http",
                    url = server.Url,
                    headers = new Dictionary<string, string>
                    {
                        ["Authorization"] = $"Bearer {server.AuthToken}",
                    },
                },
            },
        };

        var path = Path.Combine(_workingDirectory, "mcp-config.json");
        File.WriteAllText(path, JsonSerializer.Serialize(config));
        return path;
    }

    /// <summary>一時作業ディレクトリを作成する（claude の cwd を無害な場所に限定する）</summary>
    private static string CreateWorkingDirectory()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            "QuickER",
            "claude-code",
            Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(path);
        return path;
    }

    /// <summary>状態サマリー・健全度・案内を更新し通知する</summary>
    private void UpdateStatus(string summary, ConnectionHealth level, string guidance)
    {
        StatusSummary = summary;
        StatusLevel = level;
        Guidance = guidance;
        StatusSummaryChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        _turnCts?.Cancel();
        _turnCts?.Dispose();
        _turnCts = null;

        if (_mcpServer is not null)
        {
            await _mcpServer.DisposeAsync().ConfigureAwait(false);
            _mcpServer = null;
        }

        await _client.DisposeAsync().ConfigureAwait(false);

        TryDeleteWorkingDirectory();
    }

    /// <summary>一時作業ディレクトリを削除する（ベストエフォート）</summary>
    private void TryDeleteWorkingDirectory()
    {
        if (_workingDirectory.Length == 0)
        {
            return;
        }

        try
        {
            if (Directory.Exists(_workingDirectory))
            {
                Directory.Delete(_workingDirectory, recursive: true);
            }
        }
        catch (IOException)
        {
            // 使用中などで削除できない場合は無視する
        }
        catch (UnauthorizedAccessException)
        {
            // 権限不足は無視する
        }
    }
}
