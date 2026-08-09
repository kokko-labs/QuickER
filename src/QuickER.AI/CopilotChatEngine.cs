using QuickER.AI.Resources;

namespace QuickER.AI;

/// <summary>
/// ローカルの GitHub Copilot CLI を <see cref="IErChatEngine"/> として扱うエンジン。
/// ER 設計ツールだけを公開したセッションを張り、差分・ツール要求・完了を共通イベントへ変換する。
/// </summary>
/// <remarks>
/// <para>
/// 認証はユーザーの CLI ログイン状態をそのまま使う（アプリ側でトークンを預からない）ため、
/// 状態表示は Claude Code 接続と同じ「未検出／未確認／ログイン済み／未ログイン」の 4 状態で扱う。
/// </para>
/// <para>
/// モデルは静的カタログを持たず、接続確立後に <see cref="ICopilotRuntimeClient.ListModelsAsync"/> で
/// 実行時列挙して <see cref="AvailableModels"/> へ載せる。既定は空文字＝CLI 既定モデルに任せる。
/// </para>
/// </remarks>
public sealed class CopilotChatEngine : IErChatEngine
{
    private readonly ICopilotRuntimeClient _client;
    private readonly IErDiagramToolHost? _toolHost;
    private readonly IUiDispatcher _dispatcher;
    private readonly ErChatProfile _profile;

    // 案内文は表示言語（OS カルチャ）依存のため const ではなく resx から解決する static readonly にする
    private static readonly string PendingGuidance = Strings.Copilot_Guidance_Pending;
    private static readonly string InstallGuidance = Strings.Copilot_Guidance_Install;
    private static readonly string LoggedInGuidance = Strings.Copilot_Guidance_LoggedIn;
    private static readonly string NotLoggedInGuidance = Strings.Copilot_Guidance_NotLoggedIn;
    private static readonly string InconclusiveGuidance = Strings.Copilot_Guidance_Inconclusive;

    private bool _sessionStarted;
    private bool _turnInProgress;

    /// <summary>使用するモデル ID（空なら Copilot CLI の既定モデル）</summary>
    public string Model { get; set; } = string.Empty;

    /// <summary>
    /// 接続確立後に実行時列挙した利用可能モデル ID の一覧（未接続・取得失敗時は空）。
    /// 静的カタログを持たないため、UI のモデル候補はこの一覧を使う。
    /// </summary>
    public IReadOnlyList<string> AvailableModels { get; private set; } = [];

    /// <summary>状態サマリー（UI 表示用）</summary>
    public string StatusSummary { get; private set; } = Strings.Copilot_Status_Unconfirmed;

    /// <summary>状態ドットの健全度（緑/灰/赤）</summary>
    public ConnectionHealth StatusLevel { get; private set; } = ConnectionHealth.Pending;

    /// <summary>下段に表示する状態依存の案内文</summary>
    public string Guidance { get; private set; } = PendingGuidance;

    /// <summary>copilot CLI が PATH で見つからないか（真なら接続は試行していない）</summary>
    public bool IsCliMissing { get; private set; }

    /// <summary>Copilot にログイン済みか（認証状態の取得に成功し、かつ認証済みのときのみ真）</summary>
    public bool IsLoggedIn { get; private set; }

    /// <summary>状態サマリー・健全度・案内のいずれかが変化したときに発火する</summary>
    public event EventHandler? StatusSummaryChanged;

    /// <summary><see cref="AvailableModels"/> が更新されたときに発火する</summary>
    public event EventHandler? AvailableModelsChanged;

    /// <inheritdoc />
    public event EventHandler<string>? AssistantDeltaReceived;

    /// <inheritdoc />
    public event EventHandler<ErChatToolActivity>? ToolActivityReceived;

    /// <inheritdoc />
    public event EventHandler<ErChatTurnResult>? TurnCompleted;

    /// <inheritdoc />
    public event EventHandler<string>? StatusChanged;

    /// <summary>クライアント・ツールホスト・ディスパッチャを指定して生成し、通知イベントを購読する</summary>
    /// <param name="client">Copilot ランタイムクライアント</param>
    /// <param name="toolHost">ER 図操作ツールの実行ホスト（null ならツール無効）</param>
    /// <param name="dispatcher">UI スレッドへのマーシャリング</param>
    /// <param name="profile">用途プロファイル（ツール定義・設計ルール。合成ルートが明示的に指定する）</param>
    public CopilotChatEngine(
        ICopilotRuntimeClient client,
        IErDiagramToolHost? toolHost,
        IUiDispatcher dispatcher,
        ErChatProfile profile
    )
    {
        _client = client;
        _toolHost = toolHost;
        _dispatcher = dispatcher;
        _profile = profile;
        _client.AssistantDeltaReceived += OnAssistantDelta;
        _client.ToolCallRequested += OnToolCallRequested;
        _client.SessionIdle += OnSessionIdle;
        _client.SessionErrorReceived += OnSessionError;
        _client.PermissionDeclined += OnPermissionDeclined;
    }

    /// <inheritdoc />
    public bool IsReady => _client.IsStarted && IsLoggedIn;

    /// <inheritdoc />
    /// <remarks>Copilot の添付 API は画像を対象とするため、画像のみ受け付ける。</remarks>
    public AttachmentSupport AttachmentSupport => AttachmentSupport.Images;

    /// <inheritdoc />
    public Task InitializeAsync(CancellationToken cancellationToken = default) =>
        ConnectAsync(cancellationToken);

    /// <summary>copilot を起動・接続し、認証状態とモデル一覧を取り込む</summary>
    /// <remarks>
    /// copilot CLI が PATH に無いときはプロセス起動を試みず未検出（赤・インストール案内）として返す。
    /// 起動を試みても的外れなプロセス起動エラーしか出せないため（Codex 接続と同じ判断）。
    /// </remarks>
    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        if (!EnsureCliAvailable())
        {
            return;
        }

        if (!_client.IsStarted)
        {
            try
            {
                await _client.StartAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                var message = string.Format(Strings.Copilot_ConnectFailed, ex.Message);
                UpdateStatus(
                    Strings.Copilot_Status_NotConnected,
                    ConnectionHealth.NeedsAction,
                    message
                );
                StatusChanged?.Invoke(this, message);
                return;
            }
        }

        await ReadAuthStateAsync(cancellationToken).ConfigureAwait(false);
        await ReadModelsAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>認証状態とモデル一覧を取り直す（「再確認」ボタンの入口）</summary>
    /// <remarks>未接続のまま状態だけ更新しても復帰手段が無いため、先に接続からやり直す。</remarks>
    public Task RefreshAsync(CancellationToken cancellationToken = default) =>
        ConnectAsync(cancellationToken);

    /// <inheritdoc />
    public async Task StartConversationAsync(CancellationToken cancellationToken = default)
    {
        _sessionStarted = false;

        if (!await EnsureStartedAsync(cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        StatusChanged?.Invoke(this, Strings.Copilot_StartingConversation);

        try
        {
            await _client
                .StartSessionAsync(BuildSessionOptions(), cancellationToken)
                .ConfigureAwait(false);
            _sessionStarted = true;
            StatusChanged?.Invoke(this, Strings.Copilot_StartedConversation);
        }
        catch (Exception ex)
        {
            StatusChanged?.Invoke(
                this,
                string.Format(Strings.Copilot_StartConversationFailed, ex.Message)
            );
        }
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
        EnsureAttachmentsSupported(attachments);

        if (!_sessionStarted)
        {
            await StartConversationAsync(cancellationToken).ConfigureAwait(false);
        }

        if (!_sessionStarted)
        {
            TurnCompleted?.Invoke(
                this,
                new ErChatTurnResult(false, Strings.Copilot_CouldNotStartConversation)
            );
            return;
        }

        _turnInProgress = true;
        StatusChanged?.Invoke(this, Strings.Copilot_Processing);

        try
        {
            await _client.SendAsync(prompt, attachments, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _turnInProgress = false;
            TurnCompleted?.Invoke(this, new ErChatTurnResult(false, ex.Message));
        }
    }

    /// <inheritdoc />
    public async Task InterruptAsync(CancellationToken cancellationToken = default)
    {
        if (!_sessionStarted)
        {
            return;
        }

        try
        {
            await _client.AbortAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            StatusChanged?.Invoke(this, string.Format(Strings.Copilot_InterruptFailed, ex.Message));
        }
    }

    /// <summary>
    /// 画像以外の添付を分かる例外で弾く。UI 側で添付操作を制限する前提だが、
    /// 無言で添付を落として送るとユーザーの意図とずれるため防御的にガードする（Codex 接続と同じ流儀）。
    /// </summary>
    private static void EnsureAttachmentsSupported(IReadOnlyList<ChatAttachment> attachments)
    {
        if (attachments is null or { Count: 0 })
        {
            return;
        }

        if (attachments.Any(attachment => attachment.Kind != ChatAttachmentKind.Image))
        {
            throw new NotSupportedException(Strings.Copilot_OnlyImageAttachments);
        }
    }

    /// <summary>
    /// copilot CLI の存在を確認し、未検出なら未検出状態（赤・インストール案内）へ落として false を返す。
    /// 検出できていれば、直前の未検出表示を解除して true を返す。
    /// </summary>
    private bool EnsureCliAvailable()
    {
        if (_client.IsAvailable())
        {
            if (IsCliMissing)
            {
                // 未検出から復帰したので未確認の初期状態へ戻す
                IsCliMissing = false;
                UpdateStatus(
                    Strings.Copilot_Status_Unconfirmed,
                    ConnectionHealth.Pending,
                    PendingGuidance
                );
            }

            return true;
        }

        IsCliMissing = true;
        IsLoggedIn = false;
        UpdateStatus(
            Strings.Copilot_Status_NotFound,
            ConnectionHealth.NeedsAction,
            InstallGuidance
        );
        return false;
    }

    /// <summary>未接続なら接続を試み、送信可能な状態かどうかを返す</summary>
    private async Task<bool> EnsureStartedAsync(CancellationToken cancellationToken)
    {
        if (_client.IsStarted)
        {
            return true;
        }

        await ConnectAsync(cancellationToken).ConfigureAwait(false);
        return _client.IsStarted;
    }

    /// <summary>認証状態を取得して表示状態へ反映する</summary>
    private async Task ReadAuthStateAsync(CancellationToken cancellationToken)
    {
        try
        {
            var auth = await _client.GetAuthStatusAsync(cancellationToken).ConfigureAwait(false);
            IsLoggedIn = auth.IsAuthenticated;

            if (auth.IsAuthenticated)
            {
                UpdateStatus(
                    string.IsNullOrWhiteSpace(auth.Login)
                        ? Strings.Copilot_Status_LoggedIn
                        : string.Format(Strings.Copilot_Status_LoggedInAs, auth.Login),
                    ConnectionHealth.Ready,
                    LoggedInGuidance
                );
            }
            else
            {
                UpdateStatus(
                    Strings.Copilot_Status_NotLoggedIn,
                    ConnectionHealth.NeedsAction,
                    NotLoggedInGuidance
                );
            }
        }
        catch (Exception ex)
        {
            IsLoggedIn = false;
            UpdateStatus(
                Strings.Copilot_Status_Inconclusive,
                ConnectionHealth.NeedsAction,
                InconclusiveGuidance
            );
            StatusChanged?.Invoke(this, string.Format(Strings.Copilot_AuthStateFailed, ex.Message));
        }
    }

    /// <summary>利用可能モデルを実行時列挙して取り込む（未ログインでは問い合わせが失敗するため行わない）</summary>
    private async Task ReadModelsAsync(CancellationToken cancellationToken)
    {
        if (!IsLoggedIn)
        {
            SetAvailableModels([]);
            return;
        }

        try
        {
            var models = await _client.ListModelsAsync(cancellationToken).ConfigureAwait(false);
            SetAvailableModels(models);
        }
        catch (Exception ex)
        {
            SetAvailableModels([]);
            StatusChanged?.Invoke(this, string.Format(Strings.Copilot_ModelListFailed, ex.Message));
        }
    }

    /// <summary>モデル一覧を差し替えて通知する（内容が変わらないときは通知しない）</summary>
    private void SetAvailableModels(IReadOnlyList<string> models)
    {
        if (AvailableModels.SequenceEqual(models, StringComparer.Ordinal))
        {
            return;
        }

        AvailableModels = models;
        AvailableModelsChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>セッション生成オプションを組み立てる（ツールホストがある場合のみツールと設計ルールを登録）</summary>
    private CopilotSessionOptions BuildSessionOptions() =>
        new()
        {
            Model = Model,
            Tools = _toolHost is not null ? _profile.Tools : [],
            Instructions = _toolHost is not null
                ? _profile.BuildCodexDeveloperInstructions()
                : string.Empty,
        };

    /// <summary>状態サマリー・健全度・案内を更新し通知する</summary>
    private void UpdateStatus(string summary, ConnectionHealth level, string guidance)
    {
        StatusSummary = summary;
        StatusLevel = level;
        Guidance = guidance;
        StatusSummaryChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>ストリーミング差分を共通イベントへ変換する</summary>
    private void OnAssistantDelta(object? sender, string delta) =>
        AssistantDeltaReceived?.Invoke(this, delta);

    /// <summary>ツール呼び出し要求を UI スレッドで実行し、結果を返送する</summary>
    private void OnToolCallRequested(object? sender, CopilotToolCallRequest request)
    {
        _ = ExecuteAndRespondAsync(request);
    }

    /// <summary>ツールを実行し、活動を通知しつつ結果を返す</summary>
    private async Task ExecuteAndRespondAsync(CopilotToolCallRequest request)
    {
        string resultText;
        bool success;

        if (_toolHost is null)
        {
            // ツール結果は AI へ返る機械向け文言のため英語で固定する
            resultText =
                $"Could not run '{request.ToolName}' because the tool host is not available.";
            success = false;
        }
        else
        {
            (resultText, success) = _dispatcher.Invoke(() =>
                _toolHost.Execute(request.ToolName, request.ArgumentsJson)
            );
        }

        ToolActivityReceived?.Invoke(
            this,
            new ErChatToolActivity(request.ToolName, resultText, success)
        );

        try
        {
            await _client
                .RespondToToolCallAsync(request.RequestId, resultText, success)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            StatusChanged?.Invoke(
                this,
                string.Format(Strings.Copilot_ToolResponseSendFailed, ex.Message)
            );
        }
    }

    /// <summary>アイドル復帰をターン完了へ変換する（ターン外のアイドルは無視する）</summary>
    private void OnSessionIdle(object? sender, bool aborted)
    {
        if (!_turnInProgress)
        {
            return;
        }

        _turnInProgress = false;
        // 中断はエラーではないため、エラーメッセージなしの失敗として扱う（Codex の interrupted と同じ）
        TurnCompleted?.Invoke(
            this,
            aborted ? new ErChatTurnResult(false, null) : new ErChatTurnResult(true, null)
        );
    }

    /// <summary>
    /// セッションエラーをターン失敗へ変換する。
    /// ターン中はその場で失敗完了させ、以後のアイドル通知は（ターン外として）無視されるため二重完了しない。
    /// </summary>
    private void OnSessionError(object? sender, string message)
    {
        if (_turnInProgress)
        {
            _turnInProgress = false;
            TurnCompleted?.Invoke(this, new ErChatTurnResult(false, message));
        }
        else
        {
            StatusChanged?.Invoke(this, string.Format(Strings.Copilot_ErrorOccurred, message));
        }
    }

    /// <summary>拒否した許可要求を会話へ記録する（ユーザーに見える形で残す）</summary>
    private void OnPermissionDeclined(object? sender, string description) =>
        ToolActivityReceived?.Invoke(
            this,
            new ErChatToolActivity(description, Strings.Copilot_PermissionDeclined, false)
        );

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        _client.AssistantDeltaReceived -= OnAssistantDelta;
        _client.ToolCallRequested -= OnToolCallRequested;
        _client.SessionIdle -= OnSessionIdle;
        _client.SessionErrorReceived -= OnSessionError;
        _client.PermissionDeclined -= OnPermissionDeclined;
        return _client.DisposeAsync();
    }
}
