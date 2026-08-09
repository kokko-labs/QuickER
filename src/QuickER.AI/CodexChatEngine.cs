using System.Text.Json;
using QuickER.AI.Resources;

namespace QuickER.AI;

/// <summary>Codex の接続・アカウント認証状態のスナップショット（UI 表示用）</summary>
/// <param name="IsStarted">App Server へ接続済みか</param>
/// <param name="RequiresOpenAiAuth">OpenAI 認証が必要か</param>
/// <param name="AuthMode">認証モード</param>
/// <param name="AccountSummary">アカウント概要文言</param>
/// <param name="IsCliMissing">codex CLI が PATH で見つからないか（真なら接続は試行されていない）</param>
/// <param name="Guidance">状態依存の案内文（未検出＝インストール案内・接続失敗＝理由。正常時は空）</param>
public readonly record struct CodexAuthState(
    bool IsStarted,
    bool RequiresOpenAiAuth,
    CodexAuthMode AuthMode,
    string AccountSummary,
    bool IsCliMissing = false,
    string Guidance = ""
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

    /// <summary>commandExecution / fileChange 承認への拒否決定（ターンは継続する。cancel はターン中断）</summary>
    private const string ApprovalDecisionDecline = "decline";

    /// <summary>権限昇格の承認要求（item/permissions/requestApproval）の種別名</summary>
    private const string PermissionsApprovalKind = "permissions";

    /// <summary>権限付与のスコープ（turn＝このターン限り）</summary>
    private const string PermissionsScopeTurn = "turn";

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
    public string AccountSummary { get; private set; } = Strings.Codex_NotConnected;

    /// <summary>codex CLI が PATH で見つからないか（真なら接続は試行していない）</summary>
    public bool IsCliMissing { get; private set; }

    /// <summary>
    /// 下段に表示する状態依存の案内文（未検出＝インストール案内・接続失敗＝その理由。正常時は空）。
    /// Claude Code 側の <see cref="ClaudeCodeChatEngine.Guidance"/> と同じ役割。
    /// </summary>
    public string Guidance { get; private set; } = string.Empty;

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
    /// <remarks>
    /// codex CLI が PATH に無いときはプロセス起動を試みず、未検出（赤・インストール案内）として返す。
    /// 起動を試みると Win32Exception になり「接続に失敗しました」という的外れな理由しか出せないため。
    /// </remarks>
    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        if (!EnsureCliAvailable())
        {
            RaiseAuthStateChanged();
            return;
        }

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
            Guidance = string.Empty;
        }
        catch (Exception ex)
        {
            // 検出はできたが起動に失敗した場合は、従来の通知に加えて案内文にも理由を残す
            Guidance = string.Format(Strings.Codex_ConnectFailed, ex.Message);
            StatusChanged?.Invoke(this, Guidance);
        }

        IsStarted = _client.IsStarted;

        if (IsStarted)
        {
            await ReadAccountStateAsync(cancellationToken).ConfigureAwait(false);
        }

        RaiseAuthStateChanged();
    }

    /// <summary>アカウント状態を再取得して反映する（未接続ならまず接続からやり直す）</summary>
    /// <remarks>
    /// 「再確認」ボタンの入口。未接続のまま未接続表示へリセットするだけでは復帰手段が無いため、
    /// 先に <see cref="ConnectAsync"/> を試みる（未検出ならその中で案内へ落ちる）。
    /// </remarks>
    public async Task RefreshAccountStateAsync(CancellationToken cancellationToken = default)
    {
        if (!_client.IsStarted)
        {
            await ConnectAsync(cancellationToken).ConfigureAwait(false);
            return;
        }

        await ReadAccountStateAsync(cancellationToken).ConfigureAwait(false);
        RaiseAuthStateChanged();
    }

    /// <summary>
    /// codex CLI の存在を確認し、未検出なら未検出状態（赤・インストール案内）へ落として false を返す。
    /// 検出できていれば、直前の未検出表示を解除して true を返す。
    /// </summary>
    private bool EnsureCliAvailable()
    {
        if (_client.IsAvailable())
        {
            if (IsCliMissing)
            {
                // 未検出から復帰したので、案内文とサマリーを未接続の初期状態へ戻す
                IsCliMissing = false;
                Guidance = string.Empty;
                AccountSummary = Strings.Codex_NotConnected;
            }

            return true;
        }

        IsCliMissing = true;
        IsStarted = false;
        AuthMode = CodexAuthMode.None;
        RequiresOpenAiAuth = true;
        AccountSummary = Strings.Codex_Status_NotFound;
        Guidance = Strings.Codex_Guidance_Install;
        return false;
    }

    /// <summary>アカウント状態を取得して反映する（接続済み前提・状態通知は呼び出し側の責務）</summary>
    private async Task ReadAccountStateAsync(CancellationToken cancellationToken)
    {
        try
        {
            var account = await _client
                .ReadAccountAsync(refreshToken: true, cancellationToken)
                .ConfigureAwait(false);
            ApplyAccountState(account);
        }
        catch (Exception ex)
        {
            StatusChanged?.Invoke(
                this,
                string.Format(Strings.Codex_AccountStateFailed, ex.Message)
            );
        }
    }

    /// <summary>ChatGPT ブラウザログインを開始し、認証 URL を返す（URL を開く処理は呼び出し側）</summary>
    public async Task<string?> StartChatGptLoginAsync(CancellationToken cancellationToken = default)
    {
        if (!await EnsureStartedAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        StatusChanged?.Invoke(this, Strings.Codex_ChatGptLoginUrlFetching);

        try
        {
            var result = await _client
                .StartChatGptLoginAsync(cancellationToken)
                .ConfigureAwait(false);

            if (string.IsNullOrWhiteSpace(result.AuthUrl))
            {
                StatusChanged?.Invoke(this, Strings.Codex_ChatGptLoginUrlFailed);
                return null;
            }

            StatusChanged?.Invoke(this, Strings.Codex_ChatGptCompleteInBrowser);
            return result.AuthUrl;
        }
        catch (Exception ex)
        {
            StatusChanged?.Invoke(
                this,
                string.Format(Strings.Codex_ChatGptLoginStartFailed, ex.Message)
            );
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

        StatusChanged?.Invoke(this, Strings.Codex_LoggingOut);

        try
        {
            await _client.LogoutAsync(cancellationToken).ConfigureAwait(false);
            RequiresOpenAiAuth = true;
            AccountSummary = Strings.Codex_NotLoggedIn;
            // ログイン済みの常時案内を残さない（ログインパネル側が案内する）
            Guidance = string.Empty;
            StatusChanged?.Invoke(this, Strings.Codex_LoggedOut);
        }
        catch (Exception ex)
        {
            StatusChanged?.Invoke(this, string.Format(Strings.Codex_LogoutFailed, ex.Message));
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

        StatusChanged?.Invoke(this, Strings.Codex_StartingConversation);

        try
        {
            var thread = await _client
                .StartThreadAsync(BuildThreadStartOptions(), cancellationToken)
                .ConfigureAwait(false);
            _currentThreadId = thread.Id;
            StatusChanged?.Invoke(this, Strings.Codex_StartedConversation);
        }
        catch (Exception ex)
        {
            StatusChanged?.Invoke(
                this,
                string.Format(Strings.Codex_StartConversationFailed, ex.Message)
            );
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
                new ErChatTurnResult(false, Strings.Codex_CouldNotStartConversation)
            );
            return;
        }

        _turnInProgress = true;
        StatusChanged?.Invoke(this, Strings.Codex_Processing);

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
            throw new NotSupportedException(Strings.Codex_AttachmentsNotSupported);
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
            StatusChanged?.Invoke(this, string.Format(Strings.Codex_InterruptFailed, ex.Message));
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
            // ログイン済みの常時案内（Claude Code / Copilot 接続タブと同じ「相乗り」の説明。
            // Codex のログイン状態も実体は ~/.codex で CLI と共有される）
            Guidance = Strings.Codex_Guidance_LoggedIn;
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
            // 未ログインへ変わった場合は、直前のログイン済み案内を残さない（ログインパネル側が案内する）
            Guidance = string.Empty;
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
            CodexAuthMode.ApiKey => Strings.Codex_Account_ApiKey,
            CodexAuthMode.ChatGpt => string.IsNullOrWhiteSpace(email)
                ? string.IsNullOrWhiteSpace(planType)
                    ? Strings.Codex_Account_ChatGpt
                    : string.Format(Strings.Codex_Account_ChatGptWithPlan, planType)
                : string.IsNullOrWhiteSpace(planType)
                    ? string.Format(Strings.Codex_Account_EmailLoggedIn, email)
                    // 「ログイン済み（メール / プラン）」＝Copilot 接続タブの概要と同じ形式に揃える
                    : string.Format(Strings.Codex_Account_EmailLoggedIn, $"{email} / {planType}"),
            _ => showNotLoggedInWhenUnauthenticated ? Strings.Codex_NotLoggedIn
            : isOpenAiProvider ? Strings.Codex_Account_Connected
            : Strings.Codex_Account_NoLoginRequired,
        };

    /// <summary>空白を null へ正規化する</summary>
    private static string? NormalizeOptionalText(string? text) =>
        string.IsNullOrWhiteSpace(text) ? null : text.Trim();

    /// <summary>認証状態スナップショットを通知する</summary>
    private void RaiseAuthStateChanged() =>
        AuthStateChanged?.Invoke(
            this,
            new CodexAuthState(
                IsStarted,
                RequiresOpenAiAuth,
                AuthMode,
                AccountSummary,
                IsCliMissing,
                Guidance
            )
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
            // ツール結果は AI へ返る機械向け文言のため英語で固定する
            resultText = $"Could not run '{request.Tool}' because the tool host is not available.";
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
            StatusChanged?.Invoke(
                this,
                string.Format(Strings.Codex_ToolResponseSendFailed, ex.Message)
            );
        }
    }

    /// <summary>承認要求を拒否で応答する（approvalPolicy=never の保険）</summary>
    /// <remarks>
    /// チャット経路は approvalPolicy=never かつ ER 図の操作は dynamicTools（別チャネル）で行うため、
    /// ここへ承認要求が届くのは「ER 図編集に不要な Codex ネイティブ操作」（コマンド実行・ファイル変更・
    /// 権限昇格）に限られる。作業フォルダはアプリ自身のカレントディレクトリなので、無音の自動承認は危険。
    /// 拒否したうえで <see cref="ToolActivityReceived"/> により会話へ記録し、ユーザーに見える形で残す
    /// </remarks>
    private void OnApprovalRequested(object? sender, CodexApprovalRequest e)
    {
        _ = RespondToApprovalAsync(e);
    }

    /// <summary>承認要求へ拒否相当の応答を返し、拒否したことを活動として通知する</summary>
    /// <remarks>
    /// 応答の形は承認種別ごとに異なる（Codex 0.146.0 の <c>generate-json-schema</c> で検証済み）。
    /// commandExecution / fileChange は <c>decision: "decline"</c>（ユーザーが拒否・ターンは継続）で応答するが、
    /// permissions（<c>PermissionsRequestApprovalResponse</c>）は decision を持たず
    /// 「付与する権限プロファイル」が必須のため、空プロファイル（＝何も付与しない＝実質拒否）を返す
    /// </remarks>
    private async Task RespondToApprovalAsync(CodexApprovalRequest request)
    {
        var kind = DescribeApprovalKind(request.Method);

        ToolActivityReceived?.Invoke(
            this,
            new ErChatToolActivity(kind, Strings.Codex_ApprovalDeclined, false)
        );

        try
        {
            var respond =
                kind == PermissionsApprovalKind
                    ? _client.RespondToApprovalAsync(request.RequestId, BuildNoPermissionsResult())
                    : _client.RespondToApprovalAsync(request.RequestId, ApprovalDecisionDecline);

            await respond.ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            StatusChanged?.Invoke(
                this,
                string.Format(Strings.Codex_ApprovalResponseSendFailed, ex.Message)
            );
        }
    }

    /// <summary>permissions 承認要求へ返す「何も付与しない」応答ペイロードを組み立てる</summary>
    /// <remarks>空の権限プロファイルを turn スコープで返す（＝このターン限りで何の権限も与えない）</remarks>
    private static object BuildNoPermissionsResult() =>
        new Dictionary<string, object?>
        {
            ["permissions"] = new Dictionary<string, object?>(),
            ["scope"] = PermissionsScopeTurn,
        };

    /// <summary>承認要求のメソッド名（item/{種別}/requestApproval）から種別部分を表示名として取り出す</summary>
    /// <remarks>想定外の形式ならメソッド名をそのまま返す（欠落させず原文を見せる）</remarks>
    private static string DescribeApprovalKind(string method)
    {
        var segments = method.Split('/');
        return segments.Length >= 3 ? segments[^2] : method;
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
            e.Success
                ? Strings.Codex_LoginSucceeded
                : string.Format(Strings.Codex_LoginFailed, e.Error)
        );
    }

    /// <summary>error 通知を抽出してステータス・ターン失敗へ反映する</summary>
    private void OnNotificationReceived(object? sender, CodexJsonRpcNotification e)
    {
        if (e.Method != "error")
        {
            return;
        }

        var message = Strings.Codex_UnknownError;

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
            StatusChanged?.Invoke(this, string.Format(Strings.Codex_ErrorOccurred, message));
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
