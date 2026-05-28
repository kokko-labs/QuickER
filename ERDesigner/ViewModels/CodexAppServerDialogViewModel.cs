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
    private const string OpenAiProviderName = "openai";
    private const string ClientName = "erdesigner";
    private const string ClientTitle = "ERDesigner";
    private const string ClientVersion = "1.0.0";
    private const string ApprovalPolicyNever = "never";

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

    /// <summary>ブラウザで URL を開く処理です（テスト時に差し替え可能）。</summary>
    internal Action<string> OpenBrowser { get; set; } = url => Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });

    // チャット関連フィールド
    private string? _currentThreadId;
    private string? _currentTurnId;
    private bool _isTurnInProgress;
    private string _userInput = string.Empty;
    private CodexChatMessage? _currentAssistantMessage;
    private CodexChatMessage? _currentToolCallMessage;
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
                NotifyProviderStateChanged();
                RefreshModelCandidates();
                EnsureSelectedModelIsCandidate();
            }
        }
    }

    /// <summary>現在のプロバイダーが openai かどうかです。openai のみ認証が必要です。</summary>
    public bool IsOpenAiProvider => string.IsNullOrWhiteSpace(ModelProvider) || ModelProvider.Trim().Equals(OpenAiProviderName, StringComparison.OrdinalIgnoreCase);

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
                NotifyAuthenticationStateChanged();
            }
        }
    }

    /// <summary>OpenAI 認証が必要かどうかです（サーバーから返るフラグ。ログインパネル表示やスレッド開始可否の判定に使用）。</summary>
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

    /// <summary>現在の認証モードです。</summary>
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

    /// <summary>アカウント概要の表示文言です。</summary>
    public string AccountSummary
    {
        get => _accountSummary;
        set => SetProperty(ref _accountSummary, value);
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

    /// <summary>メッセージを送信できるかどうかです。openai かつ認証必要な場合はログイン済みの場合のみ送信できます。</summary>
    public bool CanSendMessage => IsStarted && HasThread && !IsTurnInProgress && !string.IsNullOrWhiteSpace(UserInput) && (!IsOpenAiProvider || !RequiresOpenAiAuth || IsLoggedIn);

    /// <summary>ダイアログを閉じるためのアクションです。</summary>
    public Action<bool>? CloseAction { get; set; }

    /// <summary>API キー入力欄を表示するかどうかです。</summary>
    public bool ShowApiKeyInput => AuthMode != CodexAuthMode.ChatGpt;

    /// <summary>ログアウト可能かどうかです（接続済みかつ「ログイン済み」または「認証不要」の場合のみ）。</summary>
    public bool CanLogout => IsStarted && (IsLoggedIn || !RequiresOpenAiAuth);

    /// <summary>ログイン済みかどうかです（ログインパネルの表示制御に使用）。</summary>
    public bool IsLoggedIn => AuthMode != CodexAuthMode.None;

    /// <summary>ログインパネルを表示するかどうかです（openai プロバイダーかつサーバーが認証必要と返したかつ未ログイン時のみ表示）。</summary>
    public bool ShowLoginPanel => IsOpenAiProvider && RequiresOpenAiAuth && !IsLoggedIn;

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

    /// <summary>ダイアログ表示時に設定を読み込み、Codex App Server へ自動接続してアカウント状態を復元します。</summary>
    public async Task InitializeAsync()
    {
        _isInitializing = true;
        LoadSettings();
        ApiKey = ApiKeyStore.Load(_apiKeyStoreName);
        _isInitializing = false;

        // 起動時に自動接続し、保存済みトークン（~/.codex/auth.json）を使って自動ログインを試みる
        // 接続に失敗しても例外をユーザーへ伝搬させず、StatusMessage に表示する
        await ConnectAsync();
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

            // refreshToken: true を渡すことで、~/.codex に保存済みのトークンを自動再利用・更新する
            var account = await _client.ReadAccountAsync(refreshToken: true);
            ApplyAccountState(account);
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
            // 状態の反映は account/updated 通知 (OnAccountUpdated) で行うため、ここでは要求送信メッセージのみ設定する
            StatusMessage = "API キーのログイン要求を送信しました。";
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
            var thread = await _client.StartThreadAsync(BuildThreadStartOptions());
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

    /// <summary>会話スレッドを開始できるかどうかです。openai かつ認証必要な場合はログイン済みの場合のみ開始できます。</summary>
    public bool CanStartNewThread => !IsOpenAiProvider || !RequiresOpenAiAuth || IsLoggedIn;

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
        ModelProvider = NormalizeSetting(settings.ModelProvider, OpenAiProviderName);
        Model = ResolveInitialModel(settings.Model);
        EnsureSelectedModelIsCandidate();
    }

    private void NotifyProviderStateChanged()
    {
        OnPropertyChanged(nameof(IsOpenAiProvider));
        OnPropertyChanged(nameof(ShowAuthSection));
        OnPropertyChanged(nameof(ShowNonOpenAiMessage));
        OnPropertyChanged(nameof(ShowLoginPanel));
        NotifyAuthenticationStateChanged();
    }

    private void NotifyAuthenticationStateChanged()
    {
        OnPropertyChanged(nameof(CanLogout));
        OnPropertyChanged(nameof(CanSendMessage));
        OnPropertyChanged(nameof(CanStartNewThread));
        OnPropertyChanged(nameof(ShowLoginPanel));
        StartNewThreadCommand.NotifyCanExecuteChanged();
        SendMessageCommand.NotifyCanExecuteChanged();
    }

    private string BuildConnectedStatusMessage()
    {
        return CanUseWithoutLogin() ? $"接続しました。{AccountSummary}" : "Codex App Server に接続しました。ログインしてください。";
    }

    private bool CanUseWithoutLogin() => IsLoggedIn || !IsOpenAiProvider || !RequiresOpenAiAuth;

    private void ApplyAccountState(CodexAccountInfo account)
    {
        IsStarted = true;
        RequiresOpenAiAuth = account.RequiresOpenAiAuth;

        // account/read が account:null を返す場合は、account/updated 通知で反映済みの AuthMode を維持します。
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

    private CodexThreadStartOptions BuildThreadStartOptions() =>
        new()
        {
            Cwd = Environment.CurrentDirectory,
            ApprovalPolicy = ApprovalPolicyNever,
            ModelProvider = NormalizeOptionalText(ModelProvider),
            Model = NormalizeOptionalText(Model),
            DynamicTools = _mainViewModel is not null ? ErDiagramDynamicTools.GetDefinitions() : null,
        };

    private string ResolveInitialModel(string? storedModel)
    {
        if (!string.IsNullOrWhiteSpace(storedModel))
        {
            return storedModel;
        }

        return IsOpenAiProvider ? AiModelCatalog.DefaultOpenAiModel : _configToml.Model;
    }

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

    private static string NormalizeSetting(string? value, string fallback) => string.IsNullOrWhiteSpace(value) ? fallback : value;

    private static string? NormalizeOptionalText(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>config.toml を読み込んでプロバイダーおよびモデル候補を更新します。</summary>
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

    private void OnLoginCompleted(object? sender, CodexLoginCompletedNotification e)
    {
        RunOnUiThread(() => ApplyLoginCompleted(e));
    }

    private void ApplyLoginCompleted(CodexLoginCompletedNotification e)
    {
        // account/updated 通知が AuthMode を更新するため、ここでは状態再取得は行わない
        StatusMessage = e.Success ? "ログインが完了しました。" : $"ログインに失敗しました: {e.Error}";
    }

    private void OnAccountUpdated(object? sender, CodexAccountUpdatedNotification e)
    {
        RunOnUiThread(() => ApplyAccountUpdated(e));
    }

    private void ApplyAccountUpdated(CodexAccountUpdatedNotification e)
    {
        AuthMode = e.AuthMode;
        AccountSummary = e.AuthMode switch
        {
            CodexAuthMode.ApiKey => "API キーでログイン済み",
            CodexAuthMode.ChatGpt => string.IsNullOrWhiteSpace(e.PlanType) ? "ChatGPT でログイン済み" : $"ChatGPT でログイン済み ({e.PlanType})",
            // openai プロバイダーは未ログイン、それ以外は認証不要
            _ => IsOpenAiProvider ? "未ログイン" : "OpenAI 認証不要",
        };
    }

    private void OnAgentMessageDelta(object? sender, CodexAgentMessageDeltaNotification e)
    {
        // InvokeAsync でノンブロッキングにディスパッチする（Invoke の同期ブロックによるデッドロックを防ぐ）
        RunOnUiThread(() => ApplyDelta(e.Delta));
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
        RunOnUiThread(() => ApplyTurnCompleted(e));
    }

    private void ApplyTurnCompleted(CodexTurnCompletedNotification e)
    {
        IsTurnInProgress = false;
        _currentAssistantMessage = null;
        CollapseToolCallMessages();
        _currentToolCallMessage = null;

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

    private void CollapseToolCallMessages()
    {
        // ターン完了後は全 ToolCall メッセージを折り畳みます。
        foreach (var message in Messages)
        {
            if (message.Role == CodexChatMessageRole.ToolCall)
            {
                message.IsExpanded = false;
            }
        }
    }

    private void OnDynamicToolCallReceived(object? sender, CodexDynamicToolCallRequest e)
    {
        // dynamicTool を実行してレスポンスを返す（UI スレッドで実行）
        RunOnUiThreadAsync(async () => await ExecuteDynamicToolCallAsync(e));
    }

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

    private void AddOrAppendToolCallMessage(string toolName, string resultText)
    {
        // ツール呼び出し内容を ToolCall メッセージとして追加または追記する（作業中は展開状態）
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

    private void OnApprovalRequested(object? sender, CodexApprovalRequest e)
    {
        // approvalPolicy="never" を設定しているが、念のため auto-accept する
        RunOnUiThreadAsync(async () => await RespondToApprovalAsync(e));
    }

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

    private void OnNotificationReceived(object? sender, CodexJsonRpcNotification e)
    {
        // "error" 通知はターン処理中のエラーを示すため UI スレッドでメッセージを表示する
        if (e.Method != "error")
        {
            return;
        }

        RunOnUiThread(() => ApplyTurnError(e));
    }

    private void ApplyTurnError(CodexJsonRpcNotification e)
    {
        // params.error.message を取得してシステムメッセージとステータスに表示する
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
