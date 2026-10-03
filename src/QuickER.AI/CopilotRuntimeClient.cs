using System.IO;
using System.Text.Json;
using GitHub.Copilot;
using GitHub.Copilot.Rpc;
using Microsoft.Extensions.AI;
using QuickER.AI.Resources;
using QuickER.Mcp;

namespace QuickER.AI;

/// <summary>
/// GitHub.Copilot.SDK を用いた <see cref="ICopilotRuntimeClient"/> の実装。
/// ユーザーがインストール済みの copilot 実行ファイルを PATH から検出して stdio 接続で子プロセス起動し、
/// セッションイベント（差分・ツール要求・アイドル・エラー）を SDK 非依存のイベントへ翻訳する。
/// </summary>
/// <remarks>
/// <para>
/// 認証はユーザーの CLI ログイン状態をそのまま使う（<c>UseLoggedInUser</c>）。GitHubToken は扱わない。
/// </para>
/// <para>
/// ツールは「AIFunction を伴わない宣言」（<see cref="CopilotToolDeclaration"/>）として渡す。SDK は
/// 呼び出せる実体が無い宣言を自動実行せず <c>external_tool.requested</c> イベントとして client 側へ委ねるため、
/// Codex の dynamicTools と同じ「要求を受けて自前で実行し結果を返す」手動解決の形になる。
/// </para>
/// </remarks>
public sealed class CopilotRuntimeClient : ICopilotRuntimeClient
{
    /// <summary>Copilot へ名乗るクライアント名</summary>
    private const string ClientName = "QuickER";

    /// <summary>許可外の要求を拒否するときに Copilot へ返す理由（AI へ渡る機械向け文言のため英語で固定する）</summary>
    private const string DeclineFeedback =
        "Declined. This session is limited to the ER diagram editing tools provided by the host application.";

    /// <summary>作業フォルダ外の要求を拒否するときに Copilot へ返す理由（同じく英語で固定する）</summary>
    private const string OutsideWorkspaceDeclineFeedback =
        "Declined. This session may only read, write and run commands inside its working directory.";

    private readonly List<IDisposable> _subscriptions = new();

    /// <summary>差分を受け取り済みのアシスタントメッセージ ID（完了イベントでの二重出力を防ぐ）</summary>
    private readonly HashSet<string> _streamedMessageIds = new(StringComparer.Ordinal);

    /// <summary>現在のセッションで許可しているツール名（許可要求の照合に使う）</summary>
    private HashSet<string> _allowedToolNames = new(StringComparer.Ordinal);

    /// <summary>
    /// 組込みツール許可セッションで自動承認する範囲の基準フォルダ（正規化済み絶対パス。空なら承認しない）。
    /// </summary>
    private string _approvalRoot = string.Empty;

    /// <summary>copilot の実行ファイルのパスを返す処理（見つからなければ null）</summary>
    private readonly Func<string?> _resolveExecutablePath;

    private CopilotClient? _client;
    private CopilotSession? _session;
    private string _workingDirectory = string.Empty;

    /// <inheritdoc />
    public bool IsStarted => _client is not null;

    /// <inheritdoc />
    public bool HasSession => _session is not null;

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

    /// <summary>PATH 上の copilot を使うクライアントを作る</summary>
    public CopilotRuntimeClient()
        : this(CopilotCliLocator.ResolveExecutablePath) { }

    /// <summary>実行ファイルの解決を差し替えたクライアントを作る（テストが起動の失敗を起こすため）</summary>
    /// <param name="resolveExecutablePath">copilot の実行ファイルのパスを返す処理（見つからなければ null）</param>
    internal CopilotRuntimeClient(Func<string?> resolveExecutablePath)
    {
        _resolveExecutablePath = resolveExecutablePath;
    }

    /// <inheritdoc />
    public bool IsAvailable() => _resolveExecutablePath() is not null;

    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_client is not null)
        {
            return;
        }

        var executable = CopilotCliLocator.EnsureSafeToLaunch(
            _resolveExecutablePath()
                ?? throw new InvalidOperationException(Strings.Copilot_CliNotFound)
        );

        var workingDirectory = BackendWorkingDirectory.Create("copilot");
        CopilotClient? client = null;

        try
        {
            client = new CopilotClient(
                new CopilotClientOptions
                {
                    // SDK 同梱 CLI ではなくユーザーがインストールした copilot を明示的に起動する
                    Connection = RuntimeConnection.ForStdio(executable),
                    // ユーザーの CLI ログイン状態（OAuth トークン / gh CLI 認証）をそのまま使う
                    UseLoggedInUser = true,
                    // アプリのカレントディレクトリを触らせないよう、無害な一時フォルダに閉じ込める
                    WorkingDirectory = workingDirectory,
                }
            );
            await client.StartAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            // 起動に失敗した接続の一時フォルダは残さない（再試行のたびに新しいフォルダを作るため、
            // 消さないと %TEMP% に溜まる）
            if (client is not null)
            {
                try
                {
                    await client.DisposeAsync().ConfigureAwait(false);
                }
                catch (Exception)
                {
                    // 起動の失敗を伝えるほうを優先する
                }
            }

            BackendWorkingDirectory.TryDelete(workingDirectory);
            throw;
        }

        _workingDirectory = workingDirectory;
        _client = client;
    }

    /// <inheritdoc />
    public async Task<CopilotAuthInfo> GetAuthStatusAsync(
        CancellationToken cancellationToken = default
    )
    {
        var client = RequireClient();
        var status = await InvokeAsync(() => client.GetAuthStatusAsync(cancellationToken))
            .ConfigureAwait(false);

        return new CopilotAuthInfo(
            status.IsAuthenticated,
            status.Login ?? string.Empty,
            status.AuthType ?? string.Empty,
            status.StatusMessage ?? string.Empty
        );
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> ListModelsAsync(
        CancellationToken cancellationToken = default
    )
    {
        var client = RequireClient();
        var models = await InvokeAsync(() => client.ListModelsAsync(cancellationToken))
            .ConfigureAwait(false);

        return models
            .Select(model => model.Id)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    /// <inheritdoc />
    public async Task StartSessionAsync(
        CopilotSessionOptions options,
        CancellationToken cancellationToken = default
    )
    {
        var client = RequireClient();
        await DisposeSessionAsync().ConfigureAwait(false);

        var toolNames = options
            .Tools.Select(tool => tool.Name)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .ToList();
        _allowedToolNames = new HashSet<string>(toolNames, StringComparer.Ordinal);

        // セッションの作業フォルダ。未指定なら接続時に用意した無害な一時フォルダに閉じ込める
        var sessionDirectory = string.IsNullOrWhiteSpace(options.WorkingDirectory)
            ? _workingDirectory
            : options.WorkingDirectory.Trim();
        // 組込みツールを許可するときだけ、自動承認の基準フォルダを持つ（それ以外は空＝何も承認しない）
        _approvalRoot = options.AllowWorkspaceTools
            ? CopilotWorkspacePermissionPolicy.NormalizeRoot(sessionDirectory)
            : string.Empty;

        var config = new SessionConfig
        {
            ClientName = ClientName,
            // 空文字は「CLI 既定モデルに任せる」の意味なので、指定なし（null）として渡す
            Model = NormalizeOptionalText(options.Model),
            // 差分イベント（AssistantMessageDeltaEvent）で逐次表示する
            Streaming = true,
            WorkingDirectory = sessionDirectory,
            // 組込みツール許可時は作業フォルダ配下だけを承認し、それ以外の要求（ER 図編集に不要な
            // ファイル編集・シェル等）は自動拒否する
            OnPermissionRequest = options.AllowWorkspaceTools
                ? HandleWorkspacePermissionRequestAsync
                : HandlePermissionRequestAsync,
        };

        if (toolNames.Count > 0)
        {
            config.Tools = options
                .Tools.Select(tool => (AIFunctionDeclaration)new CopilotToolDeclaration(tool))
                .ToList();
        }

        // 絞らないときは代入しない（SDK の既定のまま＝全ツール）
        if (ResolveAvailableTools(options.AllowWorkspaceTools, toolNames) is { } availableTools)
        {
            config.AvailableTools = availableTools;
        }

        if (!string.IsNullOrWhiteSpace(options.Instructions))
        {
            config.SystemMessage = new SystemMessageConfig
            {
                Mode = SystemMessageMode.Append,
                Content = options.Instructions,
            };
        }

        var session = await InvokeAsync(() => client.CreateSessionAsync(config, cancellationToken))
            .ConfigureAwait(false);
        _session = session;
        _streamedMessageIds.Clear();
        Subscribe(session);
    }

    /// <inheritdoc />
    public async Task SendAsync(
        string prompt,
        IReadOnlyList<ChatAttachment> attachments,
        CancellationToken cancellationToken = default
    )
    {
        var session = RequireSession();

        var message = new MessageOptions { Prompt = prompt };

        if (attachments is { Count: > 0 })
        {
            message.Attachments = attachments.Select(CreateAttachment).ToList();
        }

        await InvokeAsync(() => session.SendAsync(message, cancellationToken))
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task AbortAsync(CancellationToken cancellationToken = default) =>
        _session is { } session
            ? InvokeAsync(() => session.AbortAsync(cancellationToken))
            : Task.CompletedTask;

    /// <inheritdoc />
    public async Task RespondToToolCallAsync(
        string requestId,
        string result,
        bool success,
        CancellationToken cancellationToken = default
    )
    {
        var session = RequireSession();

        // 成功は result、失敗は error として返す（失敗の内容もモデルに見せて次の手を選ばせる）
        await InvokeAsync(() =>
                session.Rpc.Tools.HandlePendingToolCallAsync(
                    requestId,
                    success ? result : null,
                    success ? null : result,
                    cancellationToken
                )
            )
            .ConfigureAwait(false);
    }

    /// <summary>SDK を呼び、接続が切れていたら接続を捨ててから例外を投げ直す（値を返す呼び出し）</summary>
    /// <remarks>
    /// SDK は copilot ランタイムの終了を知らせるイベントも状態も公開していないため、呼び出しの失敗で知る。
    /// 接続を捨てると <see cref="IsStarted"/> と <see cref="HasSession"/> が false に戻り、エンジンの次の接続で
    /// 起動し直せる。捨てないと <see cref="IsStarted"/> が true のまま残り、以後の呼び出しがすべて同じ理由で失敗し続ける
    /// </remarks>
    internal async Task<T> InvokeAsync<T>(Func<Task<T>> call)
    {
        try
        {
            return await call().ConfigureAwait(false);
        }
        catch (IOException ex) when (IsConnectionLost(ex))
        {
            await ResetConnectionAsync().ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>SDK を呼び、接続が切れていたら接続を捨ててから例外を投げ直す（値を返さない呼び出し）</summary>
    internal async Task InvokeAsync(Func<Task> call)
    {
        try
        {
            await call().ConfigureAwait(false);
        }
        catch (IOException ex) when (IsConnectionLost(ex))
        {
            await ResetConnectionAsync().ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>SDK の呼び出しの失敗が、copilot ランタイムとの接続が切れたことを表すか</summary>
    /// <remarks>
    /// 実機（GitHub.Copilot.SDK 1.0.14・copilot.exe）でランタイムを止めて確かめた形だけに絞る。呼び出し中に止まると
    /// <c>Communication error with Copilot CLI</c>（内側は SDK 内部の接続断の例外）、止まった後の呼び出しはパイプが
    /// 閉じた旨のメッセージで、どちらも型がちょうど <see cref="IOException"/> になる。派生型（ファイルの不在など）や、
    /// ランタイムが返した通常のエラー応答では接続を捨てない（会話の文脈を失うため）
    /// </remarks>
    /// <param name="exception">SDK の呼び出しが投げた例外</param>
    internal static bool IsConnectionLost(Exception exception) =>
        exception.GetType() == typeof(IOException);

    /// <summary>セッションで使わせるツールの許可リストを決める（null＝絞らない）</summary>
    /// <remarks>
    /// 組込みツールを許可しないセッションで宣言ツールがあるときだけ、宣言ツールに絞る（Copilot 組込みツール・
    /// GitHub MCP を使わせない）。組込みツールを許可するセッション（<see cref="CopilotSessionOptions.AllowWorkspaceTools"/>）
    /// で絞ると、宣言ツールを渡しただけで組込みのファイル編集・シェル実行が使えなくなる
    /// </remarks>
    /// <param name="allowWorkspaceTools">組込みツールを許可するセッションか</param>
    /// <param name="toolNames">宣言したツールの名前</param>
    internal static List<string>? ResolveAvailableTools(
        bool allowWorkspaceTools,
        List<string> toolNames
    ) => !allowWorkspaceTools && toolNames.Count > 0 ? toolNames : null;

    /// <summary>チャット添付を Copilot の blob 添付へ変換する（base64 をそのまま渡す＝一時ファイルを作らない）</summary>
    private static Attachment CreateAttachment(ChatAttachment attachment) =>
        new AttachmentBlob
        {
            Data = attachment.ToBase64(),
            MimeType = attachment.MediaType,
            DisplayName = attachment.FileName,
            ByteLength = attachment.Data.LongLength,
        };

    /// <summary>セッションイベントを購読し、SDK 非依存のイベントへ翻訳する</summary>
    private void Subscribe(CopilotSession session)
    {
        _subscriptions.Add(session.On<AssistantMessageDeltaEvent>(OnAssistantDelta));
        _subscriptions.Add(session.On<AssistantMessageEvent>(OnAssistantMessage));
        _subscriptions.Add(session.On<ExternalToolRequestedEvent>(OnExternalToolRequested));
        _subscriptions.Add(session.On<ToolExecutionStartEvent>(OnToolExecutionStart));
        _subscriptions.Add(session.On<SessionIdleEvent>(OnSessionIdle));
        _subscriptions.Add(session.On<SessionErrorEvent>(OnSessionError));
    }

    /// <summary>ストリーミング差分をそのまま流す</summary>
    private void OnAssistantDelta(AssistantMessageDeltaEvent e)
    {
        var delta = e.Data?.DeltaContent;

        if (string.IsNullOrEmpty(delta))
        {
            return;
        }

        if (e.Data?.MessageId is { Length: > 0 } messageId)
        {
            lock (_streamedMessageIds)
            {
                _streamedMessageIds.Add(messageId);
            }
        }

        AssistantDeltaReceived?.Invoke(this, delta);
    }

    /// <summary>
    /// 完成したアシスタントメッセージを流す（差分を 1 つも受け取らなかったメッセージに限る）。
    /// Streaming=true でも差分が来ない経路があり得るため、取りこぼしの保険として置く。
    /// </summary>
    private void OnAssistantMessage(AssistantMessageEvent e)
    {
        var content = e.Data?.Content;

        if (string.IsNullOrEmpty(content))
        {
            return;
        }

        var messageId = e.Data?.MessageId;

        if (messageId is { Length: > 0 })
        {
            lock (_streamedMessageIds)
            {
                if (!_streamedMessageIds.Add(messageId))
                {
                    // 差分で既に出力済み
                    return;
                }
            }
        }

        AssistantDeltaReceived?.Invoke(this, content);
    }

    /// <summary>手動解決のツール呼び出し要求を翻訳して通知する</summary>
    private void OnExternalToolRequested(ExternalToolRequestedEvent e)
    {
        var data = e.Data;

        if (data?.RequestId is not { Length: > 0 } requestId)
        {
            return;
        }

        var argumentsJson = data.Arguments?.GetRawText() ?? "{}";
        ToolCallRequested?.Invoke(
            this,
            new CopilotToolCallRequest(requestId, data.ToolName ?? string.Empty, argumentsJson)
        );
    }

    /// <summary>組込みツールの実行開始を通知する（進捗表示用。ツール名が取れないイベントは無視する）</summary>
    private void OnToolExecutionStart(ToolExecutionStartEvent e)
    {
        if (e.Data?.ToolName is { Length: > 0 } toolName)
        {
            ToolExecutionStarted?.Invoke(this, toolName);
        }
    }

    /// <summary>アイドル復帰（＝ターン完了）を通知する</summary>
    private void OnSessionIdle(SessionIdleEvent e) =>
        SessionIdle?.Invoke(this, e.Data?.Aborted == true);

    /// <summary>セッションエラーを通知する</summary>
    private void OnSessionError(SessionErrorEvent e) =>
        SessionErrorReceived?.Invoke(
            this,
            string.IsNullOrWhiteSpace(e.Data?.Message)
                ? Strings.Copilot_UnknownError
                : e.Data.Message
        );

    /// <summary>
    /// 許可要求を処理する。ER 設計ツールだけを許可し、それ以外（ファイル編集・シェル実行・MCP 等）は拒否する。
    /// </summary>
    /// <remarks>
    /// 許可リスト（<c>AvailableTools</c>）で組込みツールは既に締め出しているが、ハンドラを与えないと
    /// 許可要求がイベントとして保留されターンが止まるため、拒否側の既定として必ず与える
    /// （Codex の「承認要求は安全側で拒否」と同じ方針）。
    /// </remarks>
    // GHCP001: SDK が PermissionDecision を試験的 API として印付けしているが、許可要求への応答は
    // この型でしか表現できない（ハンドラを与えないと要求が保留されターンが止まる）。使用箇所を
    // このメソッドへ閉じ込めたうえで局所的に抑止する。
#pragma warning disable GHCP001
    private Task<PermissionDecision> HandlePermissionRequestAsync(
        PermissionRequest request,
        PermissionInvocation invocation
    )
    {
        if (IsDeclaredTool(request, _allowedToolNames))
        {
            return Task.FromResult(PermissionDecision.ApproveOnce());
        }

        PermissionDeclined?.Invoke(this, DescribePermissionRequest(request));
        return Task.FromResult(PermissionDecision.Reject(DeclineFeedback));
    }

    /// <summary>
    /// 組込みツール許可セッションの許可要求を処理する。作業フォルダ配下のファイル読み書きと
    /// コマンド実行だけを承認し、それ以外（URL 取得・MCP・拡張・メモリ・フォルダ外のパス）は拒否する。
    /// </summary>
    /// <remarks>
    /// Codex の <c>sandbox=workspace-write</c> ／ Claude Code の <c>--permission-mode acceptEdits</c> に
    /// 相当する権限を、SDK が表現できる範囲で最も狭く与える。サンドボックス外実行の要求
    /// （<c>RequestSandboxBypass</c>）は、対象パスが配下でも承認しない。宣言したツールの呼び出しは
    /// 通常のセッションと同じく承認する（拒否すると、宣言したツールそのものが使えない）。
    /// </remarks>
    private Task<PermissionDecision> HandleWorkspacePermissionRequestAsync(
        PermissionRequest request,
        PermissionInvocation invocation
    )
    {
        if (IsApprovedInWorkspaceSession(request, _allowedToolNames, _approvalRoot))
        {
            return Task.FromResult(PermissionDecision.ApproveOnce());
        }

        PermissionDeclined?.Invoke(this, DescribePermissionRequest(request));
        return Task.FromResult(PermissionDecision.Reject(OutsideWorkspaceDeclineFeedback));
    }
#pragma warning restore GHCP001

    /// <summary>許可要求が、このセッションで宣言したツールの呼び出しか</summary>
    /// <param name="request">Copilot から届いた許可要求</param>
    /// <param name="declaredToolNames">セッションで宣言したツールの名前</param>
    internal static bool IsDeclaredTool(
        PermissionRequest request,
        IReadOnlySet<string> declaredToolNames
    ) =>
        request is PermissionRequestCustomTool { ToolName: { Length: > 0 } toolName }
        && declaredToolNames.Contains(toolName);

    /// <summary>組込みツール許可セッションで、許可要求を承認するか</summary>
    /// <remarks>宣言したツールの呼び出しか、作業フォルダ配下に収まる要求（判定は <see cref="CopilotWorkspacePermissionPolicy"/>）を承認する</remarks>
    /// <param name="request">Copilot から届いた許可要求</param>
    /// <param name="declaredToolNames">セッションで宣言したツールの名前</param>
    /// <param name="approvalRoot">自動承認の基準フォルダ（正規化済み絶対パス）</param>
    internal static bool IsApprovedInWorkspaceSession(
        PermissionRequest request,
        IReadOnlySet<string> declaredToolNames,
        string approvalRoot
    ) =>
        IsDeclaredTool(request, declaredToolNames)
        || CopilotWorkspacePermissionPolicy.IsWithinWorkspace(request, approvalRoot);

    /// <summary>拒否した許可要求の表示名を組み立てる（種別＋ツール名／コマンド文。判別できなければ種別のみ）</summary>
    /// <remarks>
    /// シェル要求はツール名を持たないため、代わりにコマンド文を添える（何を拒否したのかが分からないと、
    /// ヘッドレス実行が途中で止まった理由をログから追えない）。長いコマンド文は末尾を切り詰める。
    /// </remarks>
    internal static string DescribePermissionRequest(PermissionRequest request)
    {
        var kind = string.IsNullOrWhiteSpace(request.Kind) ? "permission" : request.Kind;

        var detail = request switch
        {
            PermissionRequestCustomTool custom => custom.ToolName,
            PermissionRequestMcp mcp => mcp.ToolName,
            PermissionRequestHook hook => hook.ToolName,
            PermissionRequestShell shell => Truncate(shell.FullCommandText),
            _ => null,
        };

        return string.IsNullOrWhiteSpace(detail) ? kind : $"{kind}: {detail}";
    }

    /// <summary>拒否ログへ載せるコマンド文の最大長</summary>
    private const int DeclinedCommandTextLimit = 200;

    /// <summary>コマンド文を 1 行へ畳んで切り詰める（ログ 1 行の体裁を保つ）</summary>
    private static string? Truncate(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var folded = text.ReplaceLineEndings(" ").Trim();

        return folded.Length <= DeclinedCommandTextLimit
            ? folded
            : folded[..DeclinedCommandTextLimit] + "…";
    }

    /// <summary>接続済みのクライアントを取り出す（未接続なら例外）</summary>
    private CopilotClient RequireClient() =>
        _client ?? throw new InvalidOperationException(Strings.Copilot_NotConnected);

    /// <summary>生成済みのセッションを取り出す（未生成なら例外）</summary>
    private CopilotSession RequireSession() =>
        _session ?? throw new InvalidOperationException(Strings.Copilot_NoSession);

    /// <summary>空白を null へ正規化する</summary>
    private static string? NormalizeOptionalText(string? text) =>
        string.IsNullOrWhiteSpace(text) ? null : text.Trim();

    /// <summary>購読を解除してセッションを破棄する</summary>
    private async Task DisposeSessionAsync()
    {
        foreach (var subscription in _subscriptions)
        {
            subscription.Dispose();
        }

        _subscriptions.Clear();

        if (_session is not null)
        {
            var session = _session;
            _session = null;

            try
            {
                await session.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception)
            {
                // 既に切断されている場合などの後始末失敗は無視する
            }
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync() => await ResetConnectionAsync().ConfigureAwait(false);

    /// <summary>
    /// セッションと接続を捨てて未接続へ戻す（copilot ランタイムを止め、一時作業フォルダを消す）。
    /// 破棄と、接続が切れたときの後始末が共有する（後者のあとは <see cref="StartAsync"/> で接続し直せる）
    /// </summary>
    private async Task ResetConnectionAsync()
    {
        await DisposeSessionAsync().ConfigureAwait(false);

        if (_client is not null)
        {
            var client = _client;
            _client = null;

            try
            {
                await client.StopAsync().ConfigureAwait(false);
            }
            catch (Exception)
            {
                // 正常停止に失敗したら強制停止で子プロセスを残さない
                try
                {
                    await client.ForceStopAsync().ConfigureAwait(false);
                }
                catch (Exception)
                {
                    // 強制停止も失敗した場合は Dispose に委ねる
                }
            }

            await client.DisposeAsync().ConfigureAwait(false);
        }

        BackendWorkingDirectory.TryDelete(_workingDirectory);
        _workingDirectory = string.Empty;
    }
}

/// <summary>
/// 中立なツール定義（<see cref="ToolDefinition"/>）を Copilot SDK のツール宣言へ写すアダプタ。
/// </summary>
/// <remarks>
/// <see cref="AIFunction"/> を継承せず <see cref="AIFunctionDeclaration"/> のままにしているのが要点で、
/// SDK は「呼び出せる実体を伴わない宣言」を自動実行せず client 側の手動解決へ回す。
/// <c>skip_permission</c> を立てて、自前で実行する ER 設計ツールに毎回の許可プロンプトを出させない。
/// </remarks>
internal sealed class CopilotToolDeclaration : AIFunctionDeclaration
{
    /// <summary>許可プロンプトを省略させる SDK 既定のメタデータキー</summary>
    private const string SkipPermissionKey = "skip_permission";

    private readonly ToolDefinition _definition;
    private readonly JsonElement _schema;
    private readonly IReadOnlyDictionary<string, object?> _additionalProperties;

    /// <summary>ツール定義から宣言を生成する</summary>
    /// <param name="definition">中立なツール定義</param>
    public CopilotToolDeclaration(ToolDefinition definition)
    {
        _definition = definition;
        _schema = JsonSerializer.SerializeToElement(definition.InputSchema);
        _additionalProperties = new Dictionary<string, object?> { [SkipPermissionKey] = true };
    }

    /// <inheritdoc />
    public override string Name => _definition.Name;

    /// <inheritdoc />
    public override string Description => _definition.Description;

    /// <inheritdoc />
    public override JsonElement JsonSchema => _schema;

    /// <inheritdoc />
    public override IReadOnlyDictionary<string, object?> AdditionalProperties =>
        _additionalProperties;
}
