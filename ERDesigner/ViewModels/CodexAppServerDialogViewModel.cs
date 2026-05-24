using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Text.Json;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ERDesigner.Services;

namespace ERDesigner.ViewModels;

/// <summary>Codex App Server の接続設定・認証・対話チャットを扱うダイアログ用 ViewModel です。</summary>
public partial class CodexAppServerDialogViewModel : ObservableObject
{
    private readonly ICodexAppServerClient _client;
    private readonly CodexAppServerSettingsStore _settingsStore;
    private readonly string _apiKeyStoreName;
    private readonly MainViewModel? _mainViewModel;
    private bool _isInitializing;
    private string _modelProvider = "openai";
    private string _model = AiModelCatalog.DefaultOpenAiModel;
    private string _apiKey = string.Empty;
    private bool _saveApiKey = true;
    private bool _isBusy;
    private string _statusMessage = string.Empty;
    private bool _isStarted;
    private bool _requiresOpenAiAuth = true;
    private CodexAuthMode _authMode;
    private string _accountSummary = "未接続";
    private string _deviceCodeVerificationUrl = string.Empty;
    private string _deviceCodeUserCode = string.Empty;
    private bool _hasPendingDeviceCode;

    /// <summary>ブラウザで URL を開く処理です（テスト時に差し替え可能）。</summary>
    internal Action<string> OpenBrowser { get; set; } = url => Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });

    // チャット関連フィールド
    private string? _currentThreadId;
    private string? _currentTurnId;
    private bool _isTurnInProgress;
    private string _userInput = string.Empty;
    private CodexChatMessage? _currentAssistantMessage;
    private CodexConfigToml _configToml = new();

    /// <summary>チャットメッセージ一覧です。</summary>
    public ObservableCollection<CodexChatMessage> Messages { get; } = new();

    /// <summary>モデルプロバイダーの候補リストです（openai 固定 + config.toml のプロバイダー名）。</summary>
    public ObservableCollection<string> ModelProviderCandidates { get; } = new();

    /// <summary>現在のプロバイダーに対応するモデル名の候補リストです。</summary>
    public ObservableCollection<string> ModelCandidates { get; } = new();

    /// <summary>使用するモデルプロバイダーです（例: ollama-launch, openai）。</summary>
    public string ModelProvider
    {
        get => _modelProvider;
        set
        {
            if (SetProperty(ref _modelProvider, value))
            {
                OnPropertyChanged(nameof(IsOpenAiProvider));
                OnPropertyChanged(nameof(ShowAuthSection));
                OnPropertyChanged(nameof(ShowNonOpenAiMessage));
                // プロバイダー変更時にモデル候補を更新する
                RefreshModelCandidates();

                if (IsOpenAiProvider)
                {
                    if (string.IsNullOrWhiteSpace(Model) || !ModelCandidates.Contains(Model))
                    {
                        Model = AiModelCatalog.DefaultOpenAiModel;
                    }
                }
                else if (ModelCandidates.Count > 0 && !ModelCandidates.Contains(Model))
                {
                    Model = ModelCandidates[0];
                }
            }
        }
    }

    /// <summary>現在のプロバイダーが openai かどうかです。openai のみ認証が必要です。</summary>
    public bool IsOpenAiProvider => string.IsNullOrWhiteSpace(ModelProvider) || ModelProvider.Trim().Equals("openai", StringComparison.OrdinalIgnoreCase);

    /// <summary>認証セクションを表示するかどうかです（openai プロバイダー選択時のみ）。</summary>
    public bool ShowAuthSection => IsOpenAiProvider;

    /// <summary>openai 以外のプロバイダーで「認証不要」案内を表示するかどうかです。</summary>
    public bool ShowNonOpenAiMessage => !IsOpenAiProvider;

    /// <summary>使用するモデル名です（例: gemma4:31b-cloud）。</summary>
    public string Model
    {
        get => _model;
        set => SetProperty(ref _model, value);
    }

    /// <summary>API キーです。</summary>
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

    /// <summary>API キーを保存するかどうかです。</summary>
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

    /// <summary>処理中かどうかです。</summary>
    public bool IsBusy
    {
        get => _isBusy;
        set => SetProperty(ref _isBusy, value);
    }

    /// <summary>状態メッセージです。</summary>
    public string StatusMessage
    {
        get => _statusMessage;
        set => SetProperty(ref _statusMessage, value);
    }

    /// <summary>Codex App Server に接続済みかどうかです。</summary>
    public bool IsStarted
    {
        get => _isStarted;
        set
        {
            if (SetProperty(ref _isStarted, value))
            {
                OnPropertyChanged(nameof(CanSendMessage));
                OnPropertyChanged(nameof(CanStartNewThread));
                SendMessageCommand.NotifyCanExecuteChanged();
                StartNewThreadCommand.NotifyCanExecuteChanged();
            }
        }
    }

    /// <summary>OpenAI 認証が必要かどうかです。</summary>
    public bool RequiresOpenAiAuth
    {
        get => _requiresOpenAiAuth;
        set => SetProperty(ref _requiresOpenAiAuth, value);
    }

    /// <summary>現在の認証モードです。</summary>
    public CodexAuthMode AuthMode
    {
        get => _authMode;
        set
        {
            if (SetProperty(ref _authMode, value))
            {
                OnPropertyChanged(nameof(ShowApiKeyInput));
                OnPropertyChanged(nameof(CanLogout));
            }
        }
    }

    /// <summary>アカウント概要の表示文言です。</summary>
    public string AccountSummary
    {
        get => _accountSummary;
        set => SetProperty(ref _accountSummary, value);
    }

    /// <summary>確認用 URL です。</summary>
    public string DeviceCodeVerificationUrl
    {
        get => _deviceCodeVerificationUrl;
        set
        {
            if (SetProperty(ref _deviceCodeVerificationUrl, value))
            {
                OnPropertyChanged(nameof(HasPendingBrowserLink));
            }
        }
    }

    /// <summary>デバイスコードのユーザーコードです。</summary>
    public string DeviceCodeUserCode
    {
        get => _deviceCodeUserCode;
        set => SetProperty(ref _deviceCodeUserCode, value);
    }

    /// <summary>デバイスコード認証の案内を表示するかどうかです。</summary>
    public bool HasPendingDeviceCode
    {
        get => _hasPendingDeviceCode;
        set => SetProperty(ref _hasPendingDeviceCode, value);
    }

    /// <summary>ターンが実行中かどうかです。</summary>
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

    /// <summary>ユーザーが入力中のテキストです。</summary>
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

    /// <summary>スレッドが開始済みかどうかです。</summary>
    public bool HasThread => _currentThreadId is not null;

    /// <summary>メッセージを送信できるかどうかです。</summary>
    public bool CanSendMessage => IsStarted && HasThread && !IsTurnInProgress && !string.IsNullOrWhiteSpace(UserInput);

    /// <summary>ダイアログを閉じるためのアクションです。</summary>
    public Action<bool>? CloseAction { get; set; }

    /// <summary>API キー入力欄を表示するかどうかです。</summary>
    public bool ShowApiKeyInput => AuthMode != CodexAuthMode.ChatGpt;

    /// <summary>ログアウト可能かどうかです。</summary>
    public bool CanLogout => AuthMode != CodexAuthMode.None;

    /// <summary>ChatGPT プランを表示するかどうかです。</summary>
    public bool HasPendingBrowserLink => !string.IsNullOrWhiteSpace(DeviceCodeVerificationUrl);

    /// <summary>新しい ViewModel を生成します（MainViewModel なし）。</summary>
    public CodexAppServerDialogViewModel(ICodexAppServerClient? client = null, CodexAppServerSettingsStore? settingsStore = null)
        : this(client, settingsStore, "CodexAppServerApiKey", null) { }

    /// <summary>テスト用に API キー保存名も指定して新しい ViewModel を生成します。</summary>
    public CodexAppServerDialogViewModel(ICodexAppServerClient? client, CodexAppServerSettingsStore? settingsStore, string apiKeyStoreName)
        : this(client, settingsStore, apiKeyStoreName, null) { }

    /// <summary>MainViewModel を受け取って新しい ViewModel を生成します。</summary>
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

    /// <summary>ダイアログ表示時に設定を読み込み、必要なら接続を試みます。</summary>
    public async Task InitializeAsync()
    {
        _isInitializing = true;
        LoadSettings();
        ApiKey = ApiKeyStore.Load(_apiKeyStoreName);
        _isInitializing = false;
        await RefreshAccountStateAsync();
    }

    /// <summary>Codex App Server を起動して接続状態を更新します。</summary>
    [RelayCommand]
    private async Task ConnectAsync()
    {
        IsBusy = true;
        StatusMessage = "Codex App Server に接続中...";

        try
        {
            SaveSettings();
            await _client.StartAsync(BuildSettings(), "erdesigner", "ERDesigner", "1.0.0");
            IsStarted = _client.IsStarted;
            await RefreshAccountStateAsync();
            StatusMessage = "Codex App Server に接続しました。";
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

    /// <summary>現在のアカウント状態を再取得します。</summary>
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

            var account = await _client.ReadAccountAsync(refreshToken: false);
            IsStarted = true;
            RequiresOpenAiAuth = account.RequiresOpenAiAuth;
            AuthMode = account.AuthMode;
            AccountSummary = BuildAccountSummary(account);
        }
        catch (Exception ex)
        {
            StatusMessage = $"アカウント状態の取得に失敗しました: {ex.Message}";
        }
    }

    /// <summary>API キーでログインします。</summary>
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
            StatusMessage = "API キーのログイン要求を送信しました。";

            // ログイン完了後に最新のアカウント状態を反映する
            await RefreshAccountStateAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = $"API キーログインに失敗しました: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>ChatGPT ブラウザログインを開始します。</summary>
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

    /// <summary>ChatGPT デバイスコードログインを開始します。</summary>
    [RelayCommand]
    private async Task StartDeviceCodeLoginAsync()
    {
        if (!await EnsureStartedAsync())
        {
            return;
        }

        IsBusy = true;
        StatusMessage = "デバイスコードを取得中...";

        try
        {
            var result = await _client.StartChatGptDeviceCodeLoginAsync();
            DeviceCodeVerificationUrl = result.VerificationUrl ?? string.Empty;
            DeviceCodeUserCode = result.UserCode ?? string.Empty;
            HasPendingDeviceCode = !string.IsNullOrWhiteSpace(DeviceCodeVerificationUrl) && !string.IsNullOrWhiteSpace(DeviceCodeUserCode);
            StatusMessage = HasPendingDeviceCode ? "確認 URL とユーザーコードを使ってログインを完了してください。" : "デバイスコードを取得できませんでした。";
        }
        catch (Exception ex)
        {
            StatusMessage = $"デバイスコードログイン開始に失敗しました: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>ブラウザ認証 URL を既定ブラウザで開きます。</summary>
    [RelayCommand]
    private void OpenVerificationUrl()
    {
        if (string.IsNullOrWhiteSpace(DeviceCodeVerificationUrl))
        {
            return;
        }

        try
        {
            OpenBrowser(DeviceCodeVerificationUrl);
        }
        catch (Exception ex)
        {
            StatusMessage = $"ブラウザを開けませんでした: {ex.Message}";
        }
    }

    /// <summary>ログアウトします。</summary>
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
            DeviceCodeVerificationUrl = string.Empty;
            DeviceCodeUserCode = string.Empty;
            HasPendingDeviceCode = false;
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

    /// <summary>新しい会話スレッドを開始します。</summary>
    [RelayCommand(CanExecute = nameof(CanStartNewThread))]
    private async Task StartNewThreadAsync()
    {
        // openai プロバイダー以外は Codex App Server への接続不要（直接 EnsureStartedAsync を実行）
        if (!await EnsureStartedAsync())
        {
            return;
        }

        IsBusy = true;
        StatusMessage = "新しい会話スレッドを開始中...";

        try
        {
            var options = new CodexThreadStartOptions
            {
                Cwd = Environment.CurrentDirectory,
                ApprovalPolicy = "never",
                ModelProvider = string.IsNullOrWhiteSpace(ModelProvider) ? null : ModelProvider.Trim(),
                Model = string.IsNullOrWhiteSpace(Model) ? null : Model.Trim(),
                DynamicTools = _mainViewModel is not null ? ErDiagramDynamicTools.GetDefinitions() : null,
            };

            var thread = await _client.StartThreadAsync(options);
            _currentThreadId = thread.Id;
            OnPropertyChanged(nameof(HasThread));
            SendMessageCommand.NotifyCanExecuteChanged();

            Messages.Clear();
            AddSystemMessage("新しい会話スレッドを開始しました。ER 図について話しかけてください。");
            StatusMessage = "スレッドを開始しました。";
        }
        catch (Exception ex)
        {
            StatusMessage = $"スレッド開始に失敗しました: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>会話スレッドを開始できるかどうかです。</summary>
    public bool CanStartNewThread => IsStarted || !IsOpenAiProvider;

    /// <summary>ユーザーの入力をターンとして送信します。</summary>
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

    /// <summary>実行中のターンを中断します。</summary>
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
            StatusMessage = "ターンを中断しました。";
        }
        catch (Exception ex)
        {
            StatusMessage = $"中断に失敗しました: {ex.Message}";
        }
    }

    /// <summary>ダイアログを閉じます。</summary>
    [RelayCommand]
    private void Close()
    {
        SaveSettings();
        CloseAction?.Invoke(true);
    }

    private async Task<bool> EnsureStartedAsync()
    {
        if (_client.IsStarted)
        {
            return true;
        }

        await ConnectAsync();
        return _client.IsStarted;
    }

    private void LoadSettings()
    {
        var settings = _settingsStore.Load();

        // config.toml から候補を読み込んでプロバイダーリストを構築する
        LoadModelCandidatesFromConfigToml();

        // 既定プロバイダーは openai とする（保存済み設定を優先）
        ModelProvider = string.IsNullOrEmpty(settings.ModelProvider) ? "openai" : settings.ModelProvider;
        Model = string.IsNullOrEmpty(settings.Model)
            ? IsOpenAiProvider
                ? AiModelCatalog.DefaultOpenAiModel
                : _configToml.Model
            : settings.Model;

        if (IsOpenAiProvider && string.IsNullOrWhiteSpace(Model))
        {
            Model = AiModelCatalog.DefaultOpenAiModel;
        }

        if (ModelCandidates.Count > 0 && !ModelCandidates.Contains(Model))
        {
            Model = ModelCandidates[0];
        }
    }

    /// <summary>config.toml を読み込んでプロバイダーおよびモデル候補を更新します。</summary>
    private void LoadModelCandidatesFromConfigToml()
    {
        _configToml = CodexConfigTomlReader.Read();

        // プロバイダー候補: openai は必ず先頭に配置 + config.toml の model_providers
        ModelProviderCandidates.Clear();
        ModelProviderCandidates.Add("openai");

        foreach (var name in _configToml.ProviderNames)
        {
            if (!ModelProviderCandidates.Contains(name, StringComparer.OrdinalIgnoreCase))
            {
                ModelProviderCandidates.Add(name);
            }
        }

        RefreshModelCandidates();
    }

    /// <summary>現在のプロバイダーに応じてモデル候補を更新します。</summary>
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

    /// <summary>設定を保存します（ウィンドウが隠れる際などに外部から呼び出せます）。</summary>
    public void SaveSettingsPublic() => SaveSettings();

    private void SaveSettings()
    {
        _settingsStore.Save(BuildSettings());
    }

    private CodexAppServerSettings BuildSettings() => new() { ModelProvider = ModelProvider?.Trim() ?? string.Empty, Model = Model?.Trim() ?? string.Empty };

    private void PersistApiKey()
    {
        if (_isInitializing)
        {
            return;
        }

        ApiKeyStore.Save(_apiKeyStoreName, SaveApiKey ? ApiKey : string.Empty);
    }

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

    private void OnLoginCompleted(object? sender, CodexLoginCompletedNotification e)
    {
        var dispatcher = Application.Current?.Dispatcher;

        if (dispatcher is null || dispatcher.CheckAccess())
        {
            ApplyLoginCompleted(e);
        }
        else
        {
            _ = dispatcher.InvokeAsync(() => ApplyLoginCompleted(e));
        }
    }

    private void ApplyLoginCompleted(CodexLoginCompletedNotification e)
    {
        StatusMessage = e.Success ? "ログインが完了しました。" : $"ログインに失敗しました: {e.Error}";
        _ = RefreshAccountStateAsync();
    }

    private void OnAccountUpdated(object? sender, CodexAccountUpdatedNotification e)
    {
        var dispatcher = Application.Current?.Dispatcher;

        if (dispatcher is null || dispatcher.CheckAccess())
        {
            ApplyAccountUpdated(e);
        }
        else
        {
            _ = dispatcher.InvokeAsync(() => ApplyAccountUpdated(e));
        }
    }

    private void ApplyAccountUpdated(CodexAccountUpdatedNotification e)
    {
        AuthMode = e.AuthMode;
        AccountSummary = e.AuthMode switch
        {
            CodexAuthMode.ApiKey => "API キーでログイン済み",
            CodexAuthMode.ChatGpt => string.IsNullOrWhiteSpace(e.PlanType) ? "ChatGPT でログイン済み" : $"ChatGPT でログイン済み ({e.PlanType})",
            _ => RequiresOpenAiAuth ? "未ログイン" : "OpenAI 認証不要",
        };
    }

    private void OnAgentMessageDelta(object? sender, CodexAgentMessageDeltaNotification e)
    {
        // InvokeAsync でノンブロッキングにディスパッチする（Invoke の同期ブロックによるデッドロックを防ぐ）
        var dispatcher = Application.Current?.Dispatcher;

        if (dispatcher is null || dispatcher.CheckAccess())
        {
            ApplyDelta(e.Delta);
        }
        else
        {
            _ = dispatcher.InvokeAsync(() => ApplyDelta(e.Delta));
        }
    }

    private void ApplyDelta(string delta)
    {
        if (_currentAssistantMessage is null)
        {
            _currentAssistantMessage = new CodexChatMessage { Role = CodexChatMessageRole.Assistant, Content = delta };
            Messages.Add(_currentAssistantMessage);
        }
        else
        {
            // INotifyPropertyChanged 経由で UI に直接通知する（Messages の置き換え不要）
            _currentAssistantMessage.Content += delta;
        }
    }

    private void OnTurnCompleted(object? sender, CodexTurnCompletedNotification e)
    {
        var dispatcher = Application.Current?.Dispatcher;

        if (dispatcher is null || dispatcher.CheckAccess())
        {
            ApplyTurnCompleted(e);
        }
        else
        {
            _ = dispatcher.InvokeAsync(() => ApplyTurnCompleted(e));
        }
    }

    private void ApplyTurnCompleted(CodexTurnCompletedNotification e)
    {
        IsTurnInProgress = false;
        _currentAssistantMessage = null;

        if (e.Turn.Status == "interrupted")
        {
            StatusMessage = "ターンが中断されました。";
        }
        else if (e.Turn.Status == "failed" && !string.IsNullOrWhiteSpace(e.Turn.Error))
        {
            // ターン失敗時はエラーメッセージをシステムメッセージとステータスバーに表示する
            AddSystemMessage($"エラー: {e.Turn.Error}");
            StatusMessage = $"エラーが発生しました: {e.Turn.Error}";
        }
        else
        {
            StatusMessage = "完了しました。";
        }
    }

    private void OnDynamicToolCallReceived(object? sender, CodexDynamicToolCallRequest e)
    {
        // dynamicTool を実行してレスポンスを返す（UI スレッドで実行）
        Application.Current?.Dispatcher.Invoke(async () =>
        {
            string resultText;
            bool success;

            if (_mainViewModel is not null)
            {
                (resultText, success) = ErDiagramDynamicTools.Execute(e.Tool, e.Arguments, _mainViewModel);
                AddSystemMessage($"[ツール: {e.Tool}] {resultText}");
                _mainViewModel.RefreshCanvasSize();
            }
            else
            {
                resultText = $"MainViewModel が利用できないため '{e.Tool}' を実行できませんでした。";
                success = false;
            }

            try
            {
                await _client.RespondToDynamicToolCallAsync(e.RequestId, resultText, success);
            }
            catch (Exception ex)
            {
                StatusMessage = $"ツールレスポンスの送信に失敗しました: {ex.Message}";
            }
        });
    }

    private void OnApprovalRequested(object? sender, CodexApprovalRequest e)
    {
        // approvalPolicy="never" を設定しているが、念のため auto-accept する
        Application.Current?.Dispatcher.Invoke(async () =>
        {
            try
            {
                await _client.RespondToApprovalAsync(e.RequestId, "accept");
            }
            catch (Exception ex)
            {
                StatusMessage = $"承認レスポンスの送信に失敗しました: {ex.Message}";
            }
        });
    }

    private void OnNotificationReceived(object? sender, CodexJsonRpcNotification e)
    {
        // "error" 通知はターン処理中のエラーを示すため UI スレッドでメッセージを表示する
        if (e.Method != "error")
        {
            return;
        }

        var dispatcher = Application.Current?.Dispatcher;

        if (dispatcher is null || dispatcher.CheckAccess())
        {
            ApplyTurnError(e);
        }
        else
        {
            _ = dispatcher.InvokeAsync(() => ApplyTurnError(e));
        }
    }

    private void ApplyTurnError(CodexJsonRpcNotification e)
    {
        // params.error.message を取得してシステムメッセージとステータスに表示する
        var message = "不明なエラーが発生しました。";

        if (e.Params is System.Text.Json.JsonElement paramsElement &&
            paramsElement.TryGetProperty("error", out var errorElement) &&
            errorElement.TryGetProperty("message", out var msgElement))
        {
            message = msgElement.GetString() ?? message;
        }

        if (_isTurnInProgress)
        {
            AddSystemMessage($"エラー: {message}");
        }

        StatusMessage = $"エラーが発生しました: {message}";
    }

    private static string BuildAccountSummary(CodexAccountInfo account)
    {
        return account.AuthMode switch
        {
            CodexAuthMode.ApiKey => "API キーでログイン済み",
            CodexAuthMode.ChatGpt => string.IsNullOrWhiteSpace(account.Email)
                ? string.IsNullOrWhiteSpace(account.PlanType)
                    ? "ChatGPT でログイン済み"
                    : $"ChatGPT でログイン済み ({account.PlanType})"
                : string.IsNullOrWhiteSpace(account.PlanType)
                    ? $"{account.Email} でログイン済み"
                    : $"{account.Email} / {account.PlanType}",
            _ => account.RequiresOpenAiAuth ? "未ログイン" : "OpenAI 認証不要",
        };
    }
}
