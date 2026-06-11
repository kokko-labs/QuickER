using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Text.Json;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ERDesigner.Services;

namespace ERDesigner.ViewModels;

/// <summary>Codex App Server の接続設定・認証・対話チャットを扱うダイアログ用 ViewModel</summary>
/// <remarks>JSON-RPC クライアントの通知イベントを購読し、ストリーミング応答や承認フローを UI へ仲介する</remarks>
public partial class CodexAppServerDialogViewModel : ObservableObject
{
    /// <summary>OpenAI プロバイダー名（認証要否判定に用いる）</summary>
    private const string OpenAiProviderName = "openai";

    /// <summary>initialize ハンドシェイクで通知するクライアント名</summary>
    private const string ClientName = "erdesigner";

    /// <summary>クライアントタイトル</summary>
    private const string ClientTitle = "ERDesigner";

    /// <summary>クライアントバージョン</summary>
    private const string ClientVersion = "1.0.0";

    /// <summary>承認ポリシー「常に承認不要」</summary>
    private const string ApprovalPolicyNever = "never";

    /// <summary>Codex App Server との JSON-RPC クライアント</summary>
    private readonly ICodexAppServerClient _client;

    /// <summary>接続設定の永続化ストア</summary>
    private readonly CodexAppServerSettingsStore _settingsStore;

    /// <summary>API キー保存に用いるストアキー名</summary>
    private readonly string _apiKeyStoreName;

    /// <summary>ツール実行対象のメイン ViewModel（null の場合は ER 図操作ツールを無効化する）</summary>
    private readonly MainViewModel? _mainViewModel;

    /// <summary>初期化処理中かどうか（設定変更の副作用を抑止するためのガード）</summary>
    private bool _isInitializing;
    private string _modelProvider = "openai";
    private string _model = AiModelCatalog.DefaultOpenAiModel;
    private string _apiKey = string.Empty;
    private bool _saveApiKey = true;
    private bool _isBusy;
    private string _statusMessage = string.Empty;
    private bool _isStarted;
    private bool _isInitialAutoConnectInProgress;
    private bool _requiresOpenAiAuth = true;
    private CodexAuthMode _authMode;
    private string _accountSummary = "未接続";

    /// <summary>ブラウザで URL を開く処理（テスト時に差し替え可能）</summary>
    internal Action<string> OpenBrowser { get; set; } = url => Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });

    // チャット関連フィールド（進行中のスレッド・ターン・組み立て中メッセージを保持する）
    private string? _currentThreadId;
    private string? _currentTurnId;
    private bool _isTurnInProgress;
    private string _userInput = string.Empty;
    private CodexChatMessage? _currentAssistantMessage;
    private CodexChatMessage? _currentToolCallMessage;
    private CodexConfigToml _configToml = new();

    /// <summary>チャットメッセージ一覧</summary>
    public ObservableCollection<CodexChatMessage> Messages { get; } = new();

    /// <summary>モデルプロバイダーの候補一覧（openai 固定 + config.toml のプロバイダー名）</summary>
    public ObservableCollection<string> ModelProviderCandidates { get; } = new();

    /// <summary>現在のプロバイダーに対応するモデル名の候補一覧</summary>
    public ObservableCollection<string> ModelCandidates { get; } = new();

    /// <summary>使用するモデルプロバイダー（例: ollama-launch, openai）</summary>
    public string ModelProvider
    {
        get => _modelProvider;
        set
        {
            if (SetProperty(ref _modelProvider, value))
            {
                NotifyProviderStateChanged();
                RefreshModelCandidates();
                EnsureSelectedModelIsCandidate();
            }
        }
    }

    /// <summary>現在のプロバイダーが openai かどうか（openai のみ認証が必要）</summary>
    public bool IsOpenAiProvider => string.IsNullOrWhiteSpace(ModelProvider) || ModelProvider.Trim().Equals(OpenAiProviderName, StringComparison.OrdinalIgnoreCase);

    /// <summary>認証セクションを表示するかどうか（openai プロバイダー選択時のみ）</summary>
    public bool ShowAuthSection => IsOpenAiProvider;

    /// <summary>openai 以外のプロバイダーで「認証不要」案内を表示するかどうか</summary>
    public bool ShowNonOpenAiMessage => !IsOpenAiProvider;

    /// <summary>使用するモデル名（例: gemma4:31b-cloud）</summary>
    public string Model
    {
        get => _model;
        set => SetProperty(ref _model, value);
    }

    /// <summary>API キー（変更時に保存設定に従って永続化する）</summary>
    public string ApiKey
    {
        get => _apiKey;
        set
        {
            if (SetProperty(ref _apiKey, value))
            {
                PersistApiKey();
            }
        }
    }

    /// <summary>API キーを保存するかどうか</summary>
    public bool SaveApiKey
    {
        get => _saveApiKey;
        set
        {
            if (SetProperty(ref _saveApiKey, value))
            {
                PersistApiKey();
            }
        }
    }

    /// <summary>処理中かどうか</summary>
    public bool IsBusy
    {
        get => _isBusy;
        set => SetProperty(ref _isBusy, value);
    }

    /// <summary>状態メッセージ</summary>
    public string StatusMessage
    {
        get => _statusMessage;
        set => SetProperty(ref _statusMessage, value);
    }

    /// <summary>Codex App Server へ接続済みかどうか</summary>
    public bool IsStarted
    {
        get => _isStarted;
        set
        {
            if (SetProperty(ref _isStarted, value))
            {
                NotifyAuthenticationStateChanged();
            }
        }
    }

    /// <summary>OpenAI 認証が必要かどうか（サーバーが返すフラグ、ログインパネル表示やスレッド開始可否の判定に用いる）</summary>
    public bool RequiresOpenAiAuth
    {
        get => _requiresOpenAiAuth;
        set
        {
            if (SetProperty(ref _requiresOpenAiAuth, value))
            {
                NotifyAuthenticationStateChanged();
            }
        }
    }

    /// <summary>現在の認証モード</summary>
    public CodexAuthMode AuthMode
    {
        get => _authMode;
        set
        {
            if (SetProperty(ref _authMode, value))
            {
                OnPropertyChanged(nameof(ShowApiKeyInput));
                OnPropertyChanged(nameof(IsLoggedIn));
                NotifyAuthenticationStateChanged();
            }
        }
    }

    /// <summary>アカウント概要の表示文言</summary>
    public string AccountSummary
    {
        get => _accountSummary;
        set => SetProperty(ref _accountSummary, value);
    }

    /// <summary>ターンが実行中かどうか（送信・中断コマンドの可否に連動する）</summary>
    public bool IsTurnInProgress
    {
        get => _isTurnInProgress;
        set
        {
            if (SetProperty(ref _isTurnInProgress, value))
            {
                OnPropertyChanged(nameof(CanSendMessage));
                SendMessageCommand.NotifyCanExecuteChanged();
                InterruptTurnCommand.NotifyCanExecuteChanged();
            }
        }
    }

    /// <summary>ユーザーが入力中のメッセージテキスト</summary>
    public string UserInput
    {
        get => _userInput;
        set
        {
            if (SetProperty(ref _userInput, value))
            {
                SendMessageCommand.NotifyCanExecuteChanged();
            }
        }
    }

    /// <summary>会話スレッドが開始済みかどうか</summary>
    public bool HasThread => _currentThreadId is not null;

    /// <summary>メッセージを送信できるかどうか（openai で認証必要時はログイン済みの場合のみ可）</summary>
    public bool CanSendMessage => IsStarted && HasThread && !IsTurnInProgress && !string.IsNullOrWhiteSpace(UserInput) && (!IsOpenAiProvider || !RequiresOpenAiAuth || IsLoggedIn);

    /// <summary>ダイアログを閉じる際に呼ぶアクション</summary>
    public Action<bool>? CloseAction { get; set; }

    /// <summary>API キー入力欄を表示するかどうか（ChatGPT ログイン時は非表示）</summary>
    public bool ShowApiKeyInput => AuthMode != CodexAuthMode.ChatGpt;

    /// <summary>ログアウト可能かどうか（接続済みかつログイン済み、または認証不要の場合のみ）</summary>
    public bool CanLogout => IsStarted && (IsLoggedIn || !RequiresOpenAiAuth);

    /// <summary>ログイン済みかどうか（ログインパネルの表示制御に用いる）</summary>
    public bool IsLoggedIn => AuthMode != CodexAuthMode.None;

    /// <summary>ログインパネルを表示するかどうか（openai かつ認証必要かつ未ログイン時のみ表示）</summary>
    public bool ShowLoginPanel => !IsInitialAutoConnectInProgress && IsOpenAiProvider && RequiresOpenAiAuth && !IsLoggedIn;

    /// <summary>初回自動接続中かどうか（接続結果が出るまでログインパネルを表示しない）</summary>
    public bool IsInitialAutoConnectInProgress
    {
        get => _isInitialAutoConnectInProgress;
        private set
        {
            if (SetProperty(ref _isInitialAutoConnectInProgress, value))
            {
                OnPropertyChanged(nameof(ShowLoginPanel));
            }
        }
    }

    /// <summary>MainViewModel を伴わずに ViewModel を生成する</summary>
    public CodexAppServerDialogViewModel(ICodexAppServerClient? client = null, CodexAppServerSettingsStore? settingsStore = null)
        : this(client, settingsStore, "CodexAppServerApiKey", null) { }

    /// <summary>API キー保存名を指定して ViewModel を生成する（テスト用）</summary>
    public CodexAppServerDialogViewModel(ICodexAppServerClient? client, CodexAppServerSettingsStore? settingsStore, string apiKeyStoreName)
        : this(client, settingsStore, apiKeyStoreName, null) { }

    /// <summary>クライアント・設定ストア・API キー保存名・MainViewModel を指定して ViewModel を生成し、通知イベントを購読する</summary>
    public CodexAppServerDialogViewModel(ICodexAppServerClient? client, CodexAppServerSettingsStore? settingsStore, string apiKeyStoreName, MainViewModel? mainViewModel)
    {
        _client = client ?? new CodexAppServerClient();
        _settingsStore = settingsStore ?? new CodexAppServerSettingsStore();
        _apiKeyStoreName = apiKeyStoreName;
        _mainViewModel = mainViewModel;
        _client.LoginCompleted += OnLoginCompleted;
        _client.AccountUpdated += OnAccountUpdated;
        _client.AgentMessageDeltaReceived += OnAgentMessageDelta;
        _client.TurnCompleted += OnTurnCompleted;
        _client.DynamicToolCallReceived += OnDynamicToolCallReceived;
        _client.ApprovalRequested += OnApprovalRequested;
        _client.NotificationReceived += OnNotificationReceived;
        LoadSettings();
    }

    /// <summary>ダイアログ表示時に設定と API キーを読み込み、自動接続してアカウント状態を復元する</summary>
    public async Task InitializeAsync()
    {
        try
        {
            _isInitializing = true;
            LoadSettings();
            ApiKey = ApiKeyStore.Load(_apiKeyStoreName);
            _isInitializing = false;

            // 起動時に自動接続し、保存済みトークン（~/.codex/auth.json）を使って自動ログインを試みる
            // 接続に失敗しても例外をユーザーへ伝搬させず、StatusMessage に表示する
            await ConnectAsync();
        }
        finally
        {
            _isInitializing = false;
            IsInitialAutoConnectInProgress = false;
        }
    }

    /// <summary>画面表示直後の自動接続中に、ログインパネルの一時的なちらつき表示を抑止する</summary>
    public void BeginInitialAutoConnect()
    {
        IsInitialAutoConnectInProgress = true;
    }

    /// <summary>Codex App Server を起動・接続し、アカウント状態と状態メッセージを更新する</summary>
    [RelayCommand]
    private async Task ConnectAsync()
    {
        IsBusy = true;
        StatusMessage = "Codex App Server に接続中...";

        try
        {
            SaveSettings();
            await _client.StartAsync(BuildSettings(), ClientName, ClientTitle, ClientVersion);
            IsStarted = _client.IsStarted;
            await RefreshAccountStateAsync();

            // 保存済みトークンで自動ログインできた場合はその旨を表示する
            // openai 以外のプロバイダー、またはサーバーが認証不要と返した場合もログイン不要として正常扱いする
            StatusMessage = BuildConnectedStatusMessage();
        }
        catch (Exception ex)
        {
            StatusMessage = $"接続に失敗しました: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>現在のアカウント状態を再取得して反映する（未起動時は未接続状態へリセットする）</summary>
    [RelayCommand]
    private async Task RefreshAccountStateAsync()
    {
        try
        {
            if (!_client.IsStarted)
            {
                IsStarted = false;
                AuthMode = CodexAuthMode.None;
                AccountSummary = "未接続";
                RequiresOpenAiAuth = true;
                return;
            }

            // refreshToken: true を渡すことで、~/.codex に保存済みのトークンを自動再利用・更新する
            var account = await _client.ReadAccountAsync(refreshToken: true);
            ApplyAccountState(account);
        }
        catch (Exception ex)
        {
            StatusMessage = $"アカウント状態の取得に失敗しました: {ex.Message}";
        }
    }

    /// <summary>入力された API キーでログイン要求を送信する（結果は account/updated 通知で反映する）</summary>
    [RelayCommand]
    private async Task LoginWithApiKeyAsync()
    {
        if (string.IsNullOrWhiteSpace(ApiKey))
        {
            StatusMessage = "API キーを入力してください。";
            return;
        }

        if (!await EnsureStartedAsync())
        {
            return;
        }

        IsBusy = true;
        StatusMessage = "API キーでログイン中...";

        try
        {
            await _client.LoginWithApiKeyAsync(ApiKey);
            PersistApiKey();
            // 状態の反映は account/updated 通知 (OnAccountUpdated) で行うため、ここでは送信済みメッセージのみ設定する
            StatusMessage = "ログイン要求を送信しました。";
        }
        catch (Exception ex)
        {
            StatusMessage = $"ログインに失敗しました: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>ChatGPT ブラウザログインを開始し、取得した認証 URL をブラウザで開く</summary>
    [RelayCommand]
    private async Task StartChatGptLoginAsync()
    {
        if (!await EnsureStartedAsync())
        {
            return;
        }

        IsBusy = true;
        StatusMessage = "ChatGPT ログイン URL を取得中...";

        try
        {
            var result = await _client.StartChatGptLoginAsync();

            if (string.IsNullOrWhiteSpace(result.AuthUrl))
            {
                StatusMessage = "ChatGPT ログイン URL を取得できませんでした。";
                return;
            }

            // ブラウザログインは URL をブラウザで開くだけ。デバイスコードパネルは表示しない
            StatusMessage = "ブラウザで ChatGPT ログインを完了してください。";

            try
            {
                OpenBrowser(result.AuthUrl);
            }
            catch (Exception ex)
            {
                StatusMessage = $"ブラウザを開けませんでした: {ex.Message}";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"ChatGPT ログイン開始に失敗しました: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>現在のアカウントからログアウトする</summary>
    [RelayCommand]
    private async Task LogoutAsync()
    {
        if (!_client.IsStarted)
        {
            return;
        }

        IsBusy = true;
        StatusMessage = "ログアウト中...";

        try
        {
            await _client.LogoutAsync();

            // account/updated 通知には RequiresOpenAiAuth フィールドが存在しないため、
            // サーバー応答に依存せず ViewModel 側で直接確定する。
            // ログアウト = 次回ログインが必要な状態に戻るため RequiresOpenAiAuth=true にリセットする。
            RequiresOpenAiAuth = true;
            AccountSummary = "未ログイン";
            StatusMessage = "ログアウトしました。";
        }
        catch (Exception ex)
        {
            StatusMessage = $"ログアウトに失敗しました: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>新しい会話スレッドを開始し、チャット表示を初期化する</summary>
    [RelayCommand(CanExecute = nameof(CanStartNewThread))]
    private async Task StartNewThreadAsync()
    {
        // openai プロバイダー以外は Codex App Server への接続不要（直接 EnsureStartedAsync を実行）
        if (!await EnsureStartedAsync())
        {
            return;
        }

        IsBusy = true;
        StatusMessage = "会話を開始中...";

        try
        {
            var thread = await _client.StartThreadAsync(BuildThreadStartOptions());
            _currentThreadId = thread.Id;
            OnPropertyChanged(nameof(HasThread));
            SendMessageCommand.NotifyCanExecuteChanged();

            Messages.Clear();
            AddSystemMessage("会話を開始しました。ER 図について話しかけてください。");
            StatusMessage = "会話を開始しました。";
        }
        catch (Exception ex)
        {
            StatusMessage = $"会話の開始に失敗しました: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>会話スレッドを開始できるかどうか（openai で認証必要時はログイン済みの場合のみ可）</summary>
    public bool CanStartNewThread => !IsOpenAiProvider || !RequiresOpenAiAuth || IsLoggedIn;

    /// <summary>ユーザー入力を新しいターンとして送信する</summary>
    [RelayCommand(CanExecute = nameof(CanSendMessage))]
    private async Task SendMessageAsync()
    {
        if (string.IsNullOrWhiteSpace(UserInput) || _currentThreadId is null)
        {
            return;
        }

        var prompt = UserInput.Trim();
        UserInput = string.Empty;

        Messages.Add(new CodexChatMessage { Role = CodexChatMessageRole.User, Content = prompt });
        IsTurnInProgress = true;
        _currentAssistantMessage = null;
        StatusMessage = "Codex が処理中です...";

        try
        {
            var turn = await _client.StartTurnAsync(_currentThreadId, prompt);
            _currentTurnId = turn.Id;
        }
        catch (Exception ex)
        {
            IsTurnInProgress = false;
            StatusMessage = $"メッセージ送信に失敗しました: {ex.Message}";
        }
    }

    /// <summary>実行中のターンを中断する</summary>
    [RelayCommand(CanExecute = nameof(IsTurnInProgress))]
    private async Task InterruptTurnAsync()
    {
        if (_currentThreadId is null || _currentTurnId is null)
        {
            return;
        }

        try
        {
            await _client.InterruptTurnAsync(_currentThreadId, _currentTurnId);
            StatusMessage = "処理を中断しました。";
        }
        catch (Exception ex)
        {
            StatusMessage = $"処理の中断に失敗しました: {ex.Message}";
        }
    }

    /// <summary>設定を保存してダイアログを閉じる</summary>
    [RelayCommand]
    private void Close()
    {
        SaveSettings();
        CloseAction?.Invoke(true);
    }

    /// <summary>未接続なら接続を試み、起動済みかどうかを返す</summary>
    private async Task<bool> EnsureStartedAsync()
    {
        if (_client.IsStarted)
        {
            return true;
        }

        await ConnectAsync();
        return _client.IsStarted;
    }

    /// <summary>保存済み設定と config.toml の候補からプロバイダー・モデルを初期化する</summary>
    private void LoadSettings()
    {
        var settings = _settingsStore.Load();

        // config.toml から候補を読み込んでプロバイダーリストを構築する
        LoadModelCandidatesFromConfigToml();

        // 既定プロバイダーは openai とする（保存済み設定を優先）
        ModelProvider = NormalizeSetting(settings.ModelProvider, OpenAiProviderName);
        Model = ResolveInitialModel(settings.Model);
        EnsureSelectedModelIsCandidate();
    }

    /// <summary>プロバイダー変更に伴い、認証関連の表示状態プロパティをまとめて再通知する</summary>
    private void NotifyProviderStateChanged()
    {
        OnPropertyChanged(nameof(IsOpenAiProvider));
        OnPropertyChanged(nameof(ShowAuthSection));
        OnPropertyChanged(nameof(ShowNonOpenAiMessage));
        OnPropertyChanged(nameof(ShowLoginPanel));
        NotifyAuthenticationStateChanged();
    }

    /// <summary>認証状態に依存するコマンド可否・表示プロパティをまとめて再通知する</summary>
    private void NotifyAuthenticationStateChanged()
    {
        OnPropertyChanged(nameof(CanLogout));
        OnPropertyChanged(nameof(CanSendMessage));
        OnPropertyChanged(nameof(CanStartNewThread));
        OnPropertyChanged(nameof(ShowLoginPanel));
        StartNewThreadCommand.NotifyCanExecuteChanged();
        SendMessageCommand.NotifyCanExecuteChanged();
    }

    /// <summary>ログイン要否を踏まえた接続完了時のステータス文言を組み立てる</summary>
    private string BuildConnectedStatusMessage()
    {
        return CanUseWithoutLogin() ? $"接続しました。{AccountSummary}" : "接続しました。ログインしてください。";
    }

    /// <summary>ログインなしで利用可能か（ログイン済み・非 openai・認証不要のいずれか）を判定する</summary>
    private bool CanUseWithoutLogin() => IsLoggedIn || !IsOpenAiProvider || !RequiresOpenAiAuth;

    /// <summary>取得したアカウント情報を ViewModel の認証状態へ反映する</summary>
    private void ApplyAccountState(CodexAccountInfo account)
    {
        IsStarted = true;
        RequiresOpenAiAuth = account.RequiresOpenAiAuth;

        // account/read が account:null を返す場合は、account/updated 通知で反映済みの AuthMode を維持する
        if (account.AuthMode != CodexAuthMode.None)
        {
            AuthMode = account.AuthMode;
            AccountSummary = BuildAccountSummary(account);
            return;
        }

        if (AuthMode == CodexAuthMode.None)
        {
            AccountSummary = BuildAccountSummary(account);
        }
    }

    /// <summary>スレッド開始オプションを組み立てる（MainViewModel がある場合のみ ER 図操作ツールを登録する）</summary>
    private CodexThreadStartOptions BuildThreadStartOptions() =>
        new()
        {
            Cwd = Environment.CurrentDirectory,
            ApprovalPolicy = ApprovalPolicyNever,
            ModelProvider = NormalizeOptionalText(ModelProvider),
            Model = NormalizeOptionalText(Model),
            DynamicTools = _mainViewModel is not null ? ErDiagramDynamicTools.GetDefinitions() : null,
        };

    /// <summary>保存済みモデルを優先し、無ければプロバイダー既定モデルを初期値として返す</summary>
    private string ResolveInitialModel(string? storedModel)
    {
        if (!string.IsNullOrWhiteSpace(storedModel))
        {
            return storedModel;
        }

        return IsOpenAiProvider ? AiModelCatalog.DefaultOpenAiModel : _configToml.Model;
    }

    /// <summary>選択中モデルが候補に含まれない場合に、妥当な候補へ補正する</summary>
    private void EnsureSelectedModelIsCandidate()
    {
        if (IsOpenAiProvider && (string.IsNullOrWhiteSpace(Model) || !ModelCandidates.Contains(Model)))
        {
            Model = AiModelCatalog.DefaultOpenAiModel;
            return;
        }

        if (!IsOpenAiProvider && ModelCandidates.Count > 0 && !ModelCandidates.Contains(Model))
        {
            Model = ModelCandidates[0];
        }
    }

    /// <summary>空値を既定値へ正規化する</summary>
    private static string NormalizeSetting(string? value, string fallback) => string.IsNullOrWhiteSpace(value) ? fallback : value;

    /// <summary>空白文字列を null に、それ以外はトリムして返す（任意指定項目の正規化）</summary>
    private static string? NormalizeOptionalText(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>config.toml を読み込んでプロバイダー候補とモデル候補を更新する</summary>
    private void LoadModelCandidatesFromConfigToml()
    {
        _configToml = CodexConfigTomlReader.Read();

        // プロバイダー候補: openai は必ず先頭に配置 + config.toml の model_providers
        ModelProviderCandidates.Clear();
        ModelProviderCandidates.Add(OpenAiProviderName);

        foreach (var name in _configToml.ProviderNames)
        {
            if (!ModelProviderCandidates.Contains(name, StringComparer.OrdinalIgnoreCase))
            {
                ModelProviderCandidates.Add(name);
            }
        }

        RefreshModelCandidates();
    }

    /// <summary>現在のプロバイダーに応じてモデル候補一覧を更新する</summary>
    private void RefreshModelCandidates()
    {
        ModelCandidates.Clear();

        if (IsOpenAiProvider)
        {
            // openai の場合は固定候補を表示する
            foreach (var m in AiModelCatalog.OpenAiModels)
            {
                ModelCandidates.Add(m);
            }
        }
        else
        {
            // その他のプロバイダーは config.toml のプロバイダー別モデルを表示する
            var currentProvider = ModelProvider.Trim();

            if (_configToml.ProviderModels.TryGetValue(currentProvider, out var models))
            {
                foreach (var m in models)
                {
                    ModelCandidates.Add(m);
                }
            }
            else if (!string.IsNullOrWhiteSpace(_configToml.Model))
            {
                // プロバイダー別エントリがなければ config.toml のデフォルトモデルを候補にする
                ModelCandidates.Add(_configToml.Model);
            }
        }
    }

    /// <summary>設定を保存する（ウィンドウの非表示化時など外部から呼び出すための公開メソッド）</summary>
    public void SaveSettingsPublic() => SaveSettings();

    /// <summary>現在のプロバイダー・モデル設定を永続化する</summary>
    private void SaveSettings()
    {
        _settingsStore.Save(BuildSettings());
    }

    /// <summary>現在の入力から保存用の設定オブジェクトを組み立てる</summary>
    private CodexAppServerSettings BuildSettings() => new() { ModelProvider = ModelProvider?.Trim() ?? string.Empty, Model = Model?.Trim() ?? string.Empty };

    /// <summary>保存設定に従い API キーを永続化する（初期化中は副作用を抑止する）</summary>
    private void PersistApiKey()
    {
        if (_isInitializing)
        {
            return;
        }

        ApiKeyStore.Save(_apiKeyStoreName, SaveApiKey ? ApiKey : string.Empty);
    }

    /// <summary>システムメッセージをチャットへ追加する（UI スレッド外からの呼び出しにも対応する）</summary>
    private void AddSystemMessage(string text)
    {
        var message = new CodexChatMessage { Role = CodexChatMessageRole.System, Content = text };

        if (Application.Current?.Dispatcher is { } dispatcher && !dispatcher.CheckAccess())
        {
            dispatcher.Invoke(() => Messages.Add(message));
        }
        else
        {
            Messages.Add(message);
        }
    }

    /// <summary>指定処理を UI スレッドで実行する（既に UI スレッドなら即時実行する）</summary>
    private static void RunOnUiThread(Action action)
    {
        var dispatcher = Application.Current?.Dispatcher;

        if (dispatcher is null || dispatcher.CheckAccess())
        {
            action();
            return;
        }

        _ = dispatcher.InvokeAsync(action);
    }

    /// <summary>非同期処理を UI スレッドで実行する（既に UI スレッドなら即時実行する）</summary>
    private static void RunOnUiThreadAsync(Func<Task> action)
    {
        var dispatcher = Application.Current?.Dispatcher;

        if (dispatcher is null || dispatcher.CheckAccess())
        {
            _ = action();
            return;
        }

        _ = dispatcher.InvokeAsync(action);
    }

    /// <summary>ログイン完了通知を UI スレッドで処理する</summary>
    private void OnLoginCompleted(object? sender, CodexLoginCompletedNotification e)
    {
        RunOnUiThread(() => ApplyLoginCompleted(e));
    }

    /// <summary>ログイン完了結果をステータスへ反映する（状態は account/updated に委ねる）</summary>
    private void ApplyLoginCompleted(CodexLoginCompletedNotification e)
    {
        // account/updated 通知が AuthMode を更新するため、ここでは状態再取得は行わない
        StatusMessage = e.Success ? "ログインしました。" : $"ログインに失敗しました: {e.Error}";
    }

    /// <summary>アカウント更新通知を UI スレッドで処理する</summary>
    private void OnAccountUpdated(object? sender, CodexAccountUpdatedNotification e)
    {
        RunOnUiThread(() => ApplyAccountUpdated(e));
    }

    /// <summary>アカウント更新通知を認証モードと概要表示へ反映する</summary>
    private void ApplyAccountUpdated(CodexAccountUpdatedNotification e)
    {
        AuthMode = e.AuthMode;
        // account/updated 通知には RequiresOpenAiAuth が含まれないため、
        // AuthMode=None は「認証不要 or 接続済み」として扱い、プロバイダー種別で表示を分ける
        AccountSummary = BuildAccountSummary(e.AuthMode, e.PlanType, email: null, showNotLoggedInWhenUnauthenticated: false, isOpenAiProvider: IsOpenAiProvider);
    }

    /// <summary>エージェントメッセージ差分通知を UI スレッドで処理する</summary>
    private void OnAgentMessageDelta(object? sender, CodexAgentMessageDeltaNotification e)
    {
        // InvokeAsync でノンブロッキングにディスパッチし、同期ブロックによるデッドロックを防ぐ
        RunOnUiThread(() => ApplyDelta(e.Delta));
    }

    /// <summary>ストリーミング差分を組み立て中のアシスタントメッセージへ追記する</summary>
    private void ApplyDelta(string delta)
    {
        if (_currentAssistantMessage is null)
        {
            _currentAssistantMessage = new CodexChatMessage { Role = CodexChatMessageRole.Assistant, Content = delta };
            Messages.Add(_currentAssistantMessage);
        }
        else
        {
            // INotifyPropertyChanged 経由で UI へ直接通知するため、コレクションの置き換えは不要
            _currentAssistantMessage.Content += delta;
        }
    }

    /// <summary>ターン完了通知を UI スレッドで処理する</summary>
    private void OnTurnCompleted(object? sender, CodexTurnCompletedNotification e)
    {
        RunOnUiThread(() => ApplyTurnCompleted(e));
    }

    /// <summary>ターン完了に伴い進行状態を解除し、結果に応じたステータスを表示する</summary>
    private void ApplyTurnCompleted(CodexTurnCompletedNotification e)
    {
        IsTurnInProgress = false;
        _currentAssistantMessage = null;
        CollapseToolCallMessages();
        _currentToolCallMessage = null;

        if (e.Turn.Status == "interrupted")
        {
            StatusMessage = "処理が中断されました。";
        }
        else if (e.Turn.Status == "failed" && !string.IsNullOrWhiteSpace(e.Turn.Error))
        {
            // ターン失敗時はエラーメッセージをシステムメッセージとステータスバーに表示する
            AddSystemMessage($"エラー: {e.Turn.Error}");
            StatusMessage = $"エラーが発生しました: {e.Turn.Error}";
        }
        else
        {
            StatusMessage = "応答が完了しました。";
        }
    }

    /// <summary>ターン完了後に全 ToolCall メッセージを折り畳む</summary>
    private void CollapseToolCallMessages()
    {
        foreach (var message in Messages)
        {
            if (message.Role == CodexChatMessageRole.ToolCall)
            {
                message.IsExpanded = false;
            }
        }
    }

    /// <summary>dynamicTool 呼び出し要求を UI スレッドで実行し、結果を返送する</summary>
    private void OnDynamicToolCallReceived(object? sender, CodexDynamicToolCallRequest e)
    {
        RunOnUiThreadAsync(async () => await ExecuteDynamicToolCallAsync(e));
    }

    /// <summary>dynamicTool を実行し、その結果を JSON-RPC レスポンスとしてサーバーへ返す</summary>
    private async Task ExecuteDynamicToolCallAsync(CodexDynamicToolCallRequest request)
    {
        var (resultText, success) = ExecuteDynamicTool(request);

        try
        {
            await _client.RespondToDynamicToolCallAsync(request.RequestId, resultText, success);
        }
        catch (Exception ex)
        {
            StatusMessage = $"ツールレスポンスの送信に失敗しました: {ex.Message}";
        }
    }

    /// <summary>ER 図操作ツールを実行し、結果をチャットへ表示してキャンバスを更新する</summary>
    private (string ResultText, bool Success) ExecuteDynamicTool(CodexDynamicToolCallRequest request)
    {
        if (_mainViewModel is null)
        {
            return ($"MainViewModel が利用できないため '{request.Tool}' を実行できませんでした。", false);
        }

        var (resultText, success) = ErDiagramDynamicTools.Execute(request.Tool, request.Arguments, _mainViewModel);
        AddOrAppendToolCallMessage(request.Tool, resultText);

        // ツール呼び出し後の次のアシスタントメッセージは新しい吹き出しで表示する
        _currentAssistantMessage = null;
        _mainViewModel.RefreshCanvasSize();
        return (resultText, success);
    }

    /// <summary>ツール呼び出し内容を ToolCall メッセージへ追加または追記する（実行中は展開状態）</summary>
    private void AddOrAppendToolCallMessage(string toolName, string resultText)
    {
        var toolCallText = $"[{toolName}] {resultText}";

        if (_currentToolCallMessage is null)
        {
            _currentToolCallMessage = new CodexChatMessage
            {
                Role = CodexChatMessageRole.ToolCall,
                Content = toolCallText,
                IsExpanded = true,
            };
            Messages.Add(_currentToolCallMessage);
            return;
        }

        _currentToolCallMessage.Content += "\n" + toolCallText;
    }

    /// <summary>承認要求を UI スレッドで処理する</summary>
    private void OnApprovalRequested(object? sender, CodexApprovalRequest e)
    {
        // approvalPolicy="never" を設定しているが、念のため自動承認で応答する
        RunOnUiThreadAsync(async () => await RespondToApprovalAsync(e));
    }

    /// <summary>承認要求に対して自動承認のレスポンスを返す</summary>
    private async Task RespondToApprovalAsync(CodexApprovalRequest request)
    {
        try
        {
            await _client.RespondToApprovalAsync(request.RequestId, "accept");
        }
        catch (Exception ex)
        {
            StatusMessage = $"承認レスポンスの送信に失敗しました: {ex.Message}";
        }
    }

    /// <summary>汎用通知を受信し、error 通知のみ UI スレッドでエラー表示へ振り分ける</summary>
    private void OnNotificationReceived(object? sender, CodexJsonRpcNotification e)
    {
        if (e.Method != "error")
        {
            return;
        }

        RunOnUiThread(() => ApplyTurnError(e));
    }

    /// <summary>error 通知から error.message を取り出し、システムメッセージとステータスへ表示する</summary>
    private void ApplyTurnError(CodexJsonRpcNotification e)
    {
        var message = "不明なエラーが発生しました。";

        if (
            e.Params is System.Text.Json.JsonElement paramsElement
            && paramsElement.TryGetProperty("error", out var errorElement)
            && errorElement.TryGetProperty("message", out var msgElement)
        )
        {
            message = msgElement.GetString() ?? message;
        }

        if (_isTurnInProgress)
        {
            AddSystemMessage($"エラー: {message}");
        }

        StatusMessage = $"エラーが発生しました: {message}";
    }

    /// <summary>アカウント情報から表示用の概要文言を組み立てる</summary>
    private string BuildAccountSummary(CodexAccountInfo account)
    {
        return BuildAccountSummary(
            account.AuthMode,
            account.PlanType,
            account.Email,
            showNotLoggedInWhenUnauthenticated: account.RequiresOpenAiAuth,
            isOpenAiProvider: IsOpenAiProvider
        );
    }

    /// <summary>認証モード・プラン・メール・プロバイダー種別から概要文言を組み立てる</summary>
    /// <param name="showNotLoggedInWhenUnauthenticated">未認証時に「未ログイン」と表示するかどうか</param>
    private static string BuildAccountSummary(CodexAuthMode authMode, string? planType, string? email, bool showNotLoggedInWhenUnauthenticated, bool isOpenAiProvider = false)
    {
        return authMode switch
        {
            CodexAuthMode.ApiKey => "API キーでログイン済み",
            CodexAuthMode.ChatGpt => string.IsNullOrWhiteSpace(email)
                ? string.IsNullOrWhiteSpace(planType)
                    ? "ChatGPT でログイン済み"
                    : $"ChatGPT でログイン済み ({planType})"
                : string.IsNullOrWhiteSpace(planType)
                    ? $"{email} でログイン済み"
                    : $"{email} / {planType}",
            // openai プロバイダーでサーバーが認証不要と返した場合は「接続済み」、非 OpenAI プロバイダーは「ログイン不要」
            _ => showNotLoggedInWhenUnauthenticated ? "未ログイン"
            : isOpenAiProvider ? "接続済み"
            : "ログイン不要",
        };
    }
}
