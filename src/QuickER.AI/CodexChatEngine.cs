using System.Text.Json;

namespace QuickER.AI;

/// <summary>Codex のアカウント認証状態のスナップショット（UI 表示用）</summary>
/// <param name="IsStarted">App Server へ接続済みか</param>
/// <param name="RequiresOpenAiAuth">OpenAI 認証が必要か</param>
/// <param name="AuthMode">認証モード</param>
/// <param name="AccountSummary">アカウント概要文言</param>
public readonly record struct CodexAuthState(
    bool IsStarted,
    bool RequiresOpenAiAuth,
    CodexAuthMode AuthMode,
    string AccountSummary
);

/// <summary>
/// Codex App Server を <see cref="IErChatEngine"/> として扱うエンジン。
/// JSON-RPC クライアントの通知（delta / tool call / turn 完了）を共通イベントへ変換し、
/// Codex 固有の接続・認証（ChatGPT / API キー）も公開する。
/// </summary>
public sealed class CodexChatEngine : IErChatEngine
{
    private const string OpenAiProviderName = "openai";
    private const string ClientName = "erdesigner";
    private const string ClientTitle = "QuickER";
    private const string ClientVersion = "1.0.0";
    private const string ApprovalPolicyNever = "never";

    private readonly ICodexAppServerClient _client;
    private readonly IErDiagramToolHost? _toolHost;
    private readonly IUiDispatcher _dispatcher;
    private readonly ErChatProfile _profile;

    private string? _currentThreadId;
    private string? _currentTurnId;
    private bool _turnInProgress;

    /// <summary>使用するモデルプロバイダー（例: openai, ollama-launch）</summary>
    public string ModelProvider { get; set; } = OpenAiProviderName;

    /// <summary>使用するモデル名（空なら Codex 既定）</summary>
    public string Model { get; set; } = string.Empty;

    /// <summary>App Server へ接続済みか</summary>
    public bool IsStarted { get; private set; }

    /// <summary>OpenAI 認証が必要か（サーバーが返すフラグ）</summary>
    public bool RequiresOpenAiAuth { get; private set; } = true;

    /// <summary>現在の認証モード</summary>
    public CodexAuthMode AuthMode { get; private set; }

    /// <summary>アカウント概要の表示文言</summary>
    public string AccountSummary { get; private set; } = "未接続";

    /// <summary>現在のプロバイダーが openai か（openai のみ認証が必要）</summary>
    public bool IsOpenAiProvider =>
        string.IsNullOrWhiteSpace(ModelProvider)
        || ModelProvider.Trim().Equals(OpenAiProviderName, StringComparison.OrdinalIgnoreCase);

    /// <summary>ログイン済みか</summary>
    public bool IsLoggedIn => AuthMode != CodexAuthMode.None;

    /// <summary>認証状態が変化したときに発火する（UI 反映用）</summary>
    public event EventHandler<CodexAuthState>? AuthStateChanged;

    /// <inheritdoc />
    public event EventHandler<string>? AssistantDeltaReceived;

    /// <inheritdoc />
    public event EventHandler<ErChatToolActivity>? ToolActivityReceived;

    /// <inheritdoc />
    public event EventHandler<ErChatTurnResult>? TurnCompleted;

    /// <inheritdoc />
    public event EventHandler<string>? StatusChanged;

    /// <summary>クライアント・ツールホスト・ディスパッチャを指定して生成し、通知イベントを購読する</summary>
    /// <param name="client">Codex App Server クライアント</param>
    /// <param name="toolHost">ER 図操作ツールの実行ホスト（null ならツール無効）</param>
    /// <param name="dispatcher">UI スレッドへのマーシャリング</param>
    /// <param name="profile">用途プロファイル（ツール定義・developer instructions。合成ルートが明示的に指定する）</param>
    public CodexChatEngine(
        ICodexAppServerClient client,
        IErDiagramToolHost? toolHost,
        IUiDispatcher dispatcher,
        ErChatProfile profile
    )
    {
        _client = client;
        _toolHost = toolHost;
        _dispatcher = dispatcher;
        _profile = profile;
        _client.AgentMessageDeltaReceived += OnAgentMessageDelta;
        _client.TurnCompleted += OnTurnCompleted;
        _client.DynamicToolCallReceived += OnDynamicToolCallReceived;
        _client.ApprovalRequested += OnApprovalRequested;
        _client.AccountUpdated += OnAccountUpdated;
        _client.LoginCompleted += OnLoginCompleted;
        _client.NotificationReceived += OnNotificationReceived;
    }

    /// <inheritdoc />
    public bool IsReady => IsStarted && (!IsOpenAiProvider || !RequiresOpenAiAuth || IsLoggedIn);

    /// <inheritdoc />
    /// <remarks>Codex は添付プロトコルが未対応のため添付不可（UI 側で無効化・防御的にガードもする）。</remarks>
    public AttachmentSupport AttachmentSupport => AttachmentSupport.None;

    /// <inheritdoc />
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await ConnectAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>App Server を起動・接続し、アカウント状態を復元する</summary>
    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await _client
                .StartAsync(
                    BuildSettings(),
                    ClientName,
                    ClientTitle,
                    ClientVersion,
                    cancellationToken
                )
                .ConfigureAwait(false);
            IsStarted = _client.IsStarted;
            await RefreshAccountStateAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            StatusChanged?.Invoke(this, $"接続に失敗しました: {ex.Message}");
        }

        RaiseAuthStateChanged();
    }

    /// <summary>アカウント状態を再取得して反映する</summary>
    public async Task RefreshAccountStateAsync(CancellationToken cancellationToken = default)
    {
        if (!_client.IsStarted)
        {
            IsStarted = false;
            AuthMode = CodexAuthMode.None;
            AccountSummary = "未接続";
            RequiresOpenAiAuth = true;
            RaiseAuthStateChanged();
            return;
        }

        try
        {
            var account = await _client
                .ReadAccountAsync(refreshToken: true, cancellationToken)
                .ConfigureAwait(false);
            ApplyAccountState(account);
        }
        catch (Exception ex)
        {
            StatusChanged?.Invoke(this, $"アカウント状態の取得に失敗しました: {ex.Message}");
        }

        RaiseAuthStateChanged();
    }

    /// <summary>ChatGPT ブラウザログインを開始し、認証 URL を返す（URL を開く処理は呼び出し側）</summary>
    public async Task<string?> StartChatGptLoginAsync(CancellationToken cancellationToken = default)
    {
        if (!await EnsureStartedAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        StatusChanged?.Invoke(this, "ChatGPT ログイン URL を取得中...");

        try
        {
            var result = await _client
                .StartChatGptLoginAsync(cancellationToken)
                .ConfigureAwait(false);

            if (string.IsNullOrWhiteSpace(result.AuthUrl))
            {
                StatusChanged?.Invoke(this, "ChatGPT ログイン URL を取得できませんでした。");
                return null;
            }

            StatusChanged?.Invoke(this, "ブラウザで ChatGPT ログインを完了してください。");
            return result.AuthUrl;
        }
        catch (Exception ex)
        {
            StatusChanged?.Invoke(this, $"ChatGPT ログイン開始に失敗しました: {ex.Message}");
            return null;
        }
    }

    /// <summary>現在のアカウントからログアウトする</summary>
    public async Task LogoutAsync(CancellationToken cancellationToken = default)
    {
        if (!_client.IsStarted)
        {
            return;
        }

        StatusChanged?.Invoke(this, "ログアウト中...");

        try
        {
            await _client.LogoutAsync(cancellationToken).ConfigureAwait(false);
            RequiresOpenAiAuth = true;
            AccountSummary = "未ログイン";
            StatusChanged?.Invoke(this, "ログアウトしました。");
        }
        catch (Exception ex)
        {
            StatusChanged?.Invoke(this, $"ログアウトに失敗しました: {ex.Message}");
        }

        RaiseAuthStateChanged();
    }

    /// <inheritdoc />
    public async Task StartConversationAsync(CancellationToken cancellationToken = default)
    {
        if (!await EnsureStartedAsync(cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        StatusChanged?.Invoke(this, "会話を開始中...");

        try
        {
            var thread = await _client
                .StartThreadAsync(BuildThreadStartOptions(), cancellationToken)
                .ConfigureAwait(false);
            _currentThreadId = thread.Id;
            StatusChanged?.Invoke(this, "会話を開始しました。");
        }
        catch (Exception ex)
        {
            StatusChanged?.Invoke(this, $"会話の開始に失敗しました: {ex.Message}");
        }
    }

    /// <inheritdoc />
    public async Task SendAsync(string prompt, CancellationToken cancellationToken = default)
    {
        if (_currentThreadId is null)
        {
            await StartConversationAsync(cancellationToken).ConfigureAwait(false);
        }

        if (_currentThreadId is null)
        {
            TurnCompleted?.Invoke(
                this,
                new ErChatTurnResult(false, "会話を開始できませんでした。")
            );
            return;
        }

        _turnInProgress = true;
        StatusChanged?.Invoke(this, "Codex が処理中です...");

        try
        {
            var turn = await _client
                .StartTurnAsync(_currentThreadId, prompt, cancellationToken)
                .ConfigureAwait(false);
            _currentTurnId = turn.Id;
        }
        catch (Exception ex)
        {
            _turnInProgress = false;
            TurnCompleted?.Invoke(this, new ErChatTurnResult(false, ex.Message));
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// Codex は添付非対応。UI 側で添付操作を無効化する前提だが、万一添付付きで呼ばれたら
    /// 分かる例外で弾く（無言で添付を落として送るとユーザーの意図とずれるため）。
    /// </remarks>
    public Task SendAsync(
        string prompt,
        IReadOnlyList<ChatAttachment> attachments,
        CancellationToken cancellationToken = default
    )
    {
        if (attachments is { Count: > 0 })
        {
            throw new NotSupportedException("Codex 接続は添付に対応していません。");
        }

        return SendAsync(prompt, cancellationToken);
    }

    /// <inheritdoc />
    public async Task InterruptAsync(CancellationToken cancellationToken = default)
    {
        if (_currentThreadId is null || _currentTurnId is null)
        {
            return;
        }

        try
        {
            await _client
                .InterruptTurnAsync(_currentThreadId, _currentTurnId, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            StatusChanged?.Invoke(this, $"処理の中断に失敗しました: {ex.Message}");
        }
    }

    /// <summary>未接続なら接続を試み、起動済みかどうかを返す</summary>
    private async Task<bool> EnsureStartedAsync(CancellationToken cancellationToken)
    {
        if (_client.IsStarted)
        {
            return true;
        }

        await ConnectAsync(cancellationToken).ConfigureAwait(false);
        return _client.IsStarted;
    }

    /// <summary>スレッド開始オプションを組み立てる（ツールホストがある場合のみツールと設計ルールを登録）</summary>
    private CodexThreadStartOptions BuildThreadStartOptions() =>
        new()
        {
            Cwd = Environment.CurrentDirectory,
            ApprovalPolicy = ApprovalPolicyNever,
            ModelProvider = NormalizeOptionalText(ModelProvider),
            Model = NormalizeOptionalText(Model),
            DynamicTools = _toolHost is not null ? _profile.Tools : null,
            DeveloperInstructions = _toolHost is not null
                ? _profile.BuildCodexDeveloperInstructions()
                : null,
        };

    /// <summary>現在のプロバイダー・モデルから保存用設定を組み立てる</summary>
    private CodexAppServerSettings BuildSettings() =>
        new()
        {
            ModelProvider = ModelProvider?.Trim() ?? string.Empty,
            Model = Model?.Trim() ?? string.Empty,
        };

    /// <summary>取得したアカウント情報を認証状態へ反映する</summary>
    private void ApplyAccountState(CodexAccountInfo account)
    {
        IsStarted = true;
        RequiresOpenAiAuth = account.RequiresOpenAiAuth;

        if (account.AuthMode != CodexAuthMode.None)
        {
            AuthMode = account.AuthMode;
            AccountSummary = BuildAccountSummary(
                account.AuthMode,
                account.PlanType,
                account.Email,
                account.RequiresOpenAiAuth,
                IsOpenAiProvider
            );
            return;
        }

        if (AuthMode == CodexAuthMode.None)
        {
            AccountSummary = BuildAccountSummary(
                account.AuthMode,
                account.PlanType,
                account.Email,
                account.RequiresOpenAiAuth,
                IsOpenAiProvider
            );
        }
    }

    /// <summary>認証モード・プラン・メール・プロバイダー種別から概要文言を組み立てる</summary>
    private static string BuildAccountSummary(
        CodexAuthMode authMode,
        string? planType,
        string? email,
        bool showNotLoggedInWhenUnauthenticated,
        bool isOpenAiProvider
    ) =>
        authMode switch
        {
            CodexAuthMode.ApiKey => "API キーでログイン済み",
            CodexAuthMode.ChatGpt => string.IsNullOrWhiteSpace(email)
                ? string.IsNullOrWhiteSpace(planType)
                    ? "ChatGPT でログイン済み"
                    : $"ChatGPT でログイン済み ({planType})"
                : string.IsNullOrWhiteSpace(planType)
                    ? $"{email} でログイン済み"
                    : $"{email} / {planType}",
            _ => showNotLoggedInWhenUnauthenticated ? "未ログイン"
            : isOpenAiProvider ? "接続済み"
            : "ログイン不要",
        };

    /// <summary>空白を null へ正規化する</summary>
    private static string? NormalizeOptionalText(string? text) =>
        string.IsNullOrWhiteSpace(text) ? null : text.Trim();

    /// <summary>認証状態スナップショットを通知する</summary>
    private void RaiseAuthStateChanged() =>
        AuthStateChanged?.Invoke(
            this,
            new CodexAuthState(IsStarted, RequiresOpenAiAuth, AuthMode, AccountSummary)
        );

    /// <summary>ストリーミング差分を UI スレッドで共通イベントへ変換する</summary>
    private void OnAgentMessageDelta(object? sender, CodexAgentMessageDeltaNotification e) =>
        AssistantDeltaReceived?.Invoke(this, e.Delta);

    /// <summary>ターン完了通知を共通イベントへ変換する</summary>
    private void OnTurnCompleted(object? sender, CodexTurnCompletedNotification e)
    {
        _turnInProgress = false;

        if (e.Turn.Status == "interrupted")
        {
            TurnCompleted?.Invoke(this, new ErChatTurnResult(false, null));
        }
        else if (e.Turn.Status == "failed" && !string.IsNullOrWhiteSpace(e.Turn.Error))
        {
            TurnCompleted?.Invoke(this, new ErChatTurnResult(false, e.Turn.Error));
        }
        else
        {
            TurnCompleted?.Invoke(this, new ErChatTurnResult(true, null));
        }
    }

    /// <summary>dynamicTool 呼び出しを UI スレッドで実行し、結果を返送する</summary>
    private void OnDynamicToolCallReceived(object? sender, CodexDynamicToolCallRequest e)
    {
        _ = ExecuteAndRespondAsync(e);
    }

    /// <summary>ツールを実行し、活動を通知しつつ JSON-RPC レスポンスを返す</summary>
    private async Task ExecuteAndRespondAsync(CodexDynamicToolCallRequest request)
    {
        string resultText;
        bool success;

        if (_toolHost is null)
        {
            resultText =
                $"ツールホストが利用できないため '{request.Tool}' を実行できませんでした。";
            success = false;
        }
        else
        {
            var argumentsJson = request.Arguments.GetRawText();
            (resultText, success) = _dispatcher.Invoke(() =>
                _toolHost.Execute(request.Tool, argumentsJson)
            );
        }

        ToolActivityReceived?.Invoke(
            this,
            new ErChatToolActivity(request.Tool, resultText, success)
        );

        try
        {
            await _client
                .RespondToDynamicToolCallAsync(request.RequestId, resultText, success)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            StatusChanged?.Invoke(this, $"ツールレスポンスの送信に失敗しました: {ex.Message}");
        }
    }

    /// <summary>承認要求に自動承認で応答する（approvalPolicy=never の保険）</summary>
    private void OnApprovalRequested(object? sender, CodexApprovalRequest e)
    {
        _ = RespondToApprovalAsync(e);
    }

    /// <summary>承認要求へ accept を返す</summary>
    private async Task RespondToApprovalAsync(CodexApprovalRequest request)
    {
        try
        {
            await _client.RespondToApprovalAsync(request.RequestId, "accept").ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            StatusChanged?.Invoke(this, $"承認レスポンスの送信に失敗しました: {ex.Message}");
        }
    }

    /// <summary>アカウント更新通知を認証状態へ反映する</summary>
    private void OnAccountUpdated(object? sender, CodexAccountUpdatedNotification e)
    {
        AuthMode = e.AuthMode;
        AccountSummary = BuildAccountSummary(
            e.AuthMode,
            e.PlanType,
            email: null,
            showNotLoggedInWhenUnauthenticated: false,
            isOpenAiProvider: IsOpenAiProvider
        );
        RaiseAuthStateChanged();
    }

    /// <summary>ログイン完了通知をステータスへ反映する（状態は account/updated に委ねる）</summary>
    private void OnLoginCompleted(object? sender, CodexLoginCompletedNotification e)
    {
        StatusChanged?.Invoke(
            this,
            e.Success ? "ログインしました。" : $"ログインに失敗しました: {e.Error}"
        );
    }

    /// <summary>error 通知を抽出してステータス・ターン失敗へ反映する</summary>
    private void OnNotificationReceived(object? sender, CodexJsonRpcNotification e)
    {
        if (e.Method != "error")
        {
            return;
        }

        var message = "不明なエラーが発生しました。";

        if (
            e.Params is JsonElement paramsElement
            && paramsElement.TryGetProperty("error", out var errorElement)
            && errorElement.TryGetProperty("message", out var msgElement)
        )
        {
            message = msgElement.GetString() ?? message;
        }

        if (_turnInProgress)
        {
            _turnInProgress = false;
            TurnCompleted?.Invoke(this, new ErChatTurnResult(false, message));
        }
        else
        {
            StatusChanged?.Invoke(this, $"エラーが発生しました: {message}");
        }
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        _client.AgentMessageDeltaReceived -= OnAgentMessageDelta;
        _client.TurnCompleted -= OnTurnCompleted;
        _client.DynamicToolCallReceived -= OnDynamicToolCallReceived;
        _client.ApprovalRequested -= OnApprovalRequested;
        _client.AccountUpdated -= OnAccountUpdated;
        _client.LoginCompleted -= OnLoginCompleted;
        _client.NotificationReceived -= OnNotificationReceived;
        return _client.DisposeAsync();
    }
}
