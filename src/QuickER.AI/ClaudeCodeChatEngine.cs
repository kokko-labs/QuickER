using System.IO;
using System.Text.Json;
using QuickER.AI.Resources;

namespace QuickER.AI;

/// <summary>
/// ローカルの Claude Code CLI をヘッドレスで駆動する <see cref="IErChatEngine"/>。
/// ER ツールはプロセス内 <see cref="ErDiagramMcpServer"/>（HTTP/SSE MCP）で公開し、Claude Code から呼ばせる。
/// 認証はユーザーの claude 設定をそのまま使い、未ログイン時は案内する。
/// </summary>
public sealed class ClaudeCodeChatEngine : IErChatEngine
{
    private readonly IClaudeCodeClient _client;
    private readonly IErDiagramToolHost? _toolHost;
    private readonly IUiDispatcher _dispatcher;
    private readonly ErChatProfile _profile;

    /// <summary>許可するツール指定（プロファイルの MCP サーバー名配下のすべて）</summary>
    private string AllowedTool => "mcp__" + _profile.McpServerName;

    private ErDiagramMcpServer? _mcpServer;
    private string _workingDirectory = string.Empty;
    private string _mcpConfigPath = string.Empty;
    private string? _sessionId;
    private bool _initialized;
    private CancellationTokenSource? _turnCts;

    /// <summary>この会話で一度でも添付を使ったか（以降のターンも Read 許可を維持するためのフラグ）</summary>
    private bool _attachmentsUsedInConversation;

    /// <summary>添付書き出し先サブフォルダ名（作業ディレクトリ配下）</summary>
    private const string AttachmentsSubfolder = "attachments";

    /// <summary>添付使用時に追加で許可するツール（ファイル読取）</summary>
    private const string ReadTool = "Read";

    // 案内文は表示言語（OS カルチャ）依存のため const ではなく resx から解決する static readonly にする
    private static readonly string PendingGuidance = Strings.ClaudeCode_Guidance_Pending;
    private static readonly string InstallGuidance = Strings.ClaudeCode_Guidance_Install;
    private static readonly string LoggedInGuidance = Strings.ClaudeCode_Guidance_LoggedIn;
    private static readonly string NotLoggedInGuidance = Strings.ClaudeCode_Guidance_NotLoggedIn;
    private static readonly string InconclusiveGuidance = Strings.ClaudeCode_Guidance_Inconclusive;

    /// <summary>使用するモデルエイリアス（空なら Claude Code 既定）</summary>
    public string Model { get; set; } = string.Empty;

    /// <summary>状態サマリー（UI 表示用）</summary>
    public string StatusSummary { get; private set; } = Strings.ClaudeCode_Status_Unconfirmed;

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
    /// <param name="client">Claude Code クライアント</param>
    /// <param name="toolHost">ER 図操作ツールの実行ホスト（null ならツール無効）</param>
    /// <param name="dispatcher">UI スレッドへのマーシャリング</param>
    /// <param name="profile">用途プロファイル（システムプロンプト・ツール・MCP サーバー名。合成ルートが明示的に指定する）</param>
    public ClaudeCodeChatEngine(
        IClaudeCodeClient client,
        IErDiagramToolHost? toolHost,
        IUiDispatcher dispatcher,
        ErChatProfile profile
    )
    {
        _client = client;
        _toolHost = toolHost;
        _dispatcher = dispatcher;
        _profile = profile;
    }

    /// <inheritdoc />
    public bool IsReady => _initialized && _client.IsAvailable();

    /// <inheritdoc />
    /// <remarks>Claude Code は Read でファイルを読み返せるため、テキスト・バイナリを含む全種別に対応する。</remarks>
    public AttachmentSupport AttachmentSupport =>
        AttachmentSupport.Images
        | AttachmentSupport.Pdf
        | AttachmentSupport.Text
        | AttachmentSupport.Binary;

    /// <inheritdoc />
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (!_client.IsAvailable())
        {
            UpdateStatus(
                Strings.ClaudeCode_Status_NotFound,
                ConnectionHealth.NeedsAction,
                InstallGuidance
            );
            _initialized = false;
            return;
        }

        if (_mcpServer is null && _toolHost is not null)
        {
            _workingDirectory = CreateWorkingDirectory();
            _mcpServer = new ErDiagramMcpServer(ExecuteTool, _profile.Tools);
            await _mcpServer.StartAsync(cancellationToken).ConfigureAwait(false);
            _mcpConfigPath = WriteMcpConfig(_mcpServer);
        }
        else if (_workingDirectory.Length == 0)
        {
            _workingDirectory = CreateWorkingDirectory();
        }

        _initialized = true;

        // 検出はできたがログインは未確認（プローブは明示操作時のみ）。緑を偽装せず灰にする。
        UpdateStatus(
            Strings.ClaudeCode_Status_Unconfirmed,
            ConnectionHealth.Pending,
            PendingGuidance
        );
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
                Strings.ClaudeCode_Status_NotFound,
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
                UpdateStatus(
                    Strings.ClaudeCode_Status_LoggedIn,
                    ConnectionHealth.Ready,
                    LoggedInGuidance
                );
                break;
            case ClaudeLoginProbeResult.NotLoggedIn:
                UpdateStatus(
                    Strings.ClaudeCode_Status_NotLoggedIn,
                    ConnectionHealth.NeedsAction,
                    NotLoggedInGuidance
                );
                break;
            default:
                UpdateStatus(
                    Strings.ClaudeCode_Status_Inconclusive,
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
        // 新しい会話では添付使用状態もリセットする（前会話の Read 許可を持ち越さない）
        _attachmentsUsedInConversation = false;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task SendAsync(string prompt, CancellationToken cancellationToken = default) =>
        SendAsync(prompt, Array.Empty<ChatAttachment>(), cancellationToken);

    /// <inheritdoc />
    public async Task SendAsync(
        string prompt,
        IReadOnlyList<ChatAttachment> attachments,
        CancellationToken cancellationToken = default
    )
    {
        if (!_initialized)
        {
            await InitializeAsync(cancellationToken).ConfigureAwait(false);
        }

        _turnCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var token = _turnCts.Token;
        StatusChanged?.Invoke(this, Strings.ClaudeCode_Processing);

        // 添付があれば作業フォルダ配下へ書き出し、プロンプト末尾に絶対パス一覧を付記する。
        // 一度でも添付を使った会話は以降のターンも Read 許可を維持する（_attachmentsUsedInConversation）。
        var effectivePrompt = prompt;

        if (attachments is { Count: > 0 })
        {
            var paths = WriteAttachments(attachments);
            var hasBinary = attachments.Any(a => a.Kind == ChatAttachmentKind.Binary);
            effectivePrompt = AppendAttachmentPaths(prompt, paths, hasBinary);
            _attachmentsUsedInConversation = true;
        }

        // 添付を使った会話では Read を追加許可する（MCP ツールと併記）。従来の添付なし会話は MCP のみで不変。
        var additionalTools = _attachmentsUsedInConversation
            ? new[] { ReadTool }
            : Array.Empty<string>();

        var options = new ClaudeCodeLaunchOptions(
            Model,
            _toolHost is not null ? _profile.BuildSystemPrompt() : string.Empty,
            _mcpConfigPath,
            AllowedTool,
            _workingDirectory
        )
        {
            AdditionalAllowedTools = additionalTools,
        };

        try
        {
            var outcome = await _client
                .RunTurnAsync(
                    effectivePrompt,
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
                UpdateStatus(
                    Strings.ClaudeCode_Status_NotLoggedIn,
                    ConnectionHealth.NeedsAction,
                    NotLoggedInGuidance
                );
                TurnCompleted?.Invoke(
                    this,
                    new ErChatTurnResult(false, BuildNotLoggedInMessage(outcome.Error))
                );
            }
            else if (outcome.Success)
            {
                // ターンが通った＝ログイン済み → 緑に反映
                UpdateStatus(
                    Strings.ClaudeCode_Status_LoggedIn,
                    ConnectionHealth.Ready,
                    LoggedInGuidance
                );
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

    /// <summary>未ログイン失敗としてチャットへ表示する文言を組み立てる</summary>
    /// <param name="reportedError">
    /// claude CLI が報告した原因（例 <c>Failed to authenticate: OAuth session expired and could not be refreshed</c>）。
    /// 報告が無ければ null または空。
    /// </param>
    /// <returns>
    /// 再認証を促す案内文（UI 言語に追従）に、原因を <c>(...)</c> で括って続けた 1 行。
    /// 原因が空なら案内文だけを返す。原因は CLI が返した英語のまま載せる（翻訳・要約はしない）。
    /// </returns>
    /// <remarks>
    /// 原因を併記するのは、対処がどちらも <c>/login</c> である「一度もログインしていない」と
    /// 「保存済みの資格情報が失効した」を、利用者が自分の状況と照合して区別できるようにするため。
    /// </remarks>
    internal static string BuildNotLoggedInMessage(string? reportedError) =>
        string.IsNullOrWhiteSpace(reportedError)
            ? Strings.ClaudeCode_TurnNotLoggedIn
            : $"{Strings.ClaudeCode_TurnNotLoggedIn} ({reportedError})";

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
        // ツール結果は AI へ返る機械向け文言のため英語で固定する
        var (result, success) = _toolHost is null
            ? ("Tools are not available.", false)
            : _dispatcher.Invoke(() => _toolHost.Execute(toolName, argumentsJson));

        ToolActivityReceived?.Invoke(this, new ErChatToolActivity(toolName, result, success));
        return (result, success);
    }

    /// <summary>
    /// 添付を作業ディレクトリ配下の <c>attachments/</c> へ書き出し、書き出した絶対パス一覧を返す。
    /// ファイル名衝突は連番（<c>name (2).ext</c>）で回避する。
    /// </summary>
    private List<string> WriteAttachments(IReadOnlyList<ChatAttachment> attachments)
    {
        var directory = Path.Combine(_workingDirectory, AttachmentsSubfolder);
        Directory.CreateDirectory(directory);

        var paths = new List<string>();

        foreach (var attachment in attachments)
        {
            var path = ResolveUniquePath(directory, attachment.FileName);
            File.WriteAllBytes(path, attachment.Data);
            paths.Add(path);
        }

        return paths;
    }

    /// <summary>指定フォルダ内で衝突しないパスを解決する（衝突時は <c>name (2).ext</c> 形式で連番を付す）</summary>
    private static string ResolveUniquePath(string directory, string fileName)
    {
        var candidate = Path.Combine(directory, fileName);

        if (!File.Exists(candidate))
        {
            return candidate;
        }

        var baseName = Path.GetFileNameWithoutExtension(fileName);
        var extension = Path.GetExtension(fileName);

        for (var index = 2; ; index++)
        {
            candidate = Path.Combine(directory, $"{baseName} ({index}){extension}");

            if (!File.Exists(candidate))
            {
                return candidate;
            }
        }
    }

    /// <summary>
    /// プロンプト末尾に、添付ファイルの絶対パス一覧（Read ツールで読む案内付き）を付記する。
    /// バイナリ（Read で読めない可能性がある形式）を含むときは、代替案をユーザーへ伝える一文を加える。
    /// </summary>
    internal static string AppendAttachmentPaths(
        string prompt,
        IReadOnlyList<string> paths,
        bool hasBinary = false
    )
    {
        if (paths.Count == 0)
        {
            return prompt;
        }

        var list = string.Join("\n", paths.Select(path => $"- {path}"));
        // ヘッドレス実行の CLI へ渡す機械向け指示のため、UI 言語に依らず英語で固定する
        var appended = $"{prompt}\n\nAttached files (read them with the Read tool):\n{list}";

        if (hasBinary)
        {
            appended +=
                "\n\nNote: if any file is in a format the Read tool cannot open, tell the user so and suggest an alternative.";
        }

        return appended;
    }

    /// <summary>MCP サーバーの URL・トークンから mcp-config ファイルを書き出し、そのパスを返す</summary>
    private string WriteMcpConfig(ErDiagramMcpServer server)
    {
        var config = new
        {
            mcpServers = new Dictionary<string, object>
            {
                [_profile.McpServerName] = new
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
