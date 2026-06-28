using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using QuickER.AI;
using QuickER.Services;
using QuickER.Services.Chat;

namespace QuickER.ViewModels;

/// <summary>AI チャット（API キー接続 / Codex 接続 / Claude Code 接続の 3 エンジン）を扱う統合ダイアログ用 ViewModel</summary>
/// <remarks>チャット UI・メッセージ・送信/中断・自動整列を共通化し、エンジン固有部分のみ <see cref="IErChatEngine"/> で差し替える</remarks>
public partial class AiChatDialogViewModel : ObservableObject
{
    /// <summary>OpenAI API キーの保存名</summary>
    private const string OpenAiApiKeyStoreName = "OpenAiApiKey";

    /// <summary>Anthropic (Claude) API キーの保存名</summary>
    private const string ClaudeApiKeyStoreName = "ClaudeApiKey";

    private const string OpenAiProviderName = "openai";

    private readonly MainViewModel? _mainViewModel;
    private readonly IUiDispatcher _dispatcher;
    private readonly CodexAppServerSettingsStore _codexSettingsStore;
    private readonly ClaudeCodeSettingsStore _claudeCodeSettingsStore;

    private readonly ChatTurnEngine _apiKeyEngine;
    private readonly CodexChatEngine? _codexEngine;
    private readonly ClaudeCodeChatEngine _claudeCodeEngine;
    private IErChatEngine _engine;

    private bool _isInitializing;
    private bool _conversationStarted;
    private bool _diagramWasEmptyAtTurnStart;
    private ErChatMessage? _currentAssistantMessage;
    private ErChatMessage? _currentToolCallMessage;
    private CodexConfigToml _configToml = new();

    /// <summary>ブラウザで URL を開く処理（テスト時に差し替え可能）</summary>
    internal Action<string> OpenBrowser { get; set; } =
        url => Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });

    /// <summary>チャットメッセージ一覧</summary>
    public ObservableCollection<ErChatMessage> Messages { get; } = new();

    /// <summary>ダイアログを閉じる際に呼ぶアクション</summary>
    public Action<bool>? CloseAction { get; set; }

    // ── 共通のチャット状態 ──

    [ObservableProperty]
    private ErChatBackendKind _selectedBackend = ErChatBackendKind.ApiKey;

    [ObservableProperty]
    private string _userInput = string.Empty;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private bool _isBusy;

    private bool _isTurnInProgress;

    /// <summary>ターンが実行中か（送信・中断コマンドの可否に連動）</summary>
    public bool IsTurnInProgress
    {
        get => _isTurnInProgress;
        set
        {
            if (SetProperty(ref _isTurnInProgress, value))
            {
                SendMessageCommand.NotifyCanExecuteChanged();
                InterruptCommand.NotifyCanExecuteChanged();
            }
        }
    }

    /// <summary>API キー接続タブが選択されているか</summary>
    public bool IsApiKeyBackend => SelectedBackend == ErChatBackendKind.ApiKey;

    /// <summary>Codex 接続タブが選択されているか</summary>
    public bool IsCodexBackend => SelectedBackend == ErChatBackendKind.Codex;

    /// <summary>Claude Code 接続タブが選択されているか</summary>
    public bool IsClaudeCodeBackend => SelectedBackend == ErChatBackendKind.ClaudeCode;

    /// <summary>新しい会話を開始できるか（接続・認証が整っているか）</summary>
    public bool CanStartConversation => _engine.IsReady;

    /// <summary>メッセージを送信できるか</summary>
    public bool CanSendMessage =>
        _conversationStarted
        && _engine.IsReady
        && !IsTurnInProgress
        && !string.IsNullOrWhiteSpace(UserInput);

    /// <summary>クリア確認が必要な実質的な会話があるか（ユーザー／アシスタントの発言が 1 件以上）</summary>
    public bool HasConversation =>
        Messages.Any(m => m.Role is ErChatMessageRole.User or ErChatMessageRole.Assistant);

    // ── API キー接続タブ ──

    [ObservableProperty]
    private AiProvider _apiProvider = AiProvider.OpenAI;

    [ObservableProperty]
    private string _apiModel = AiModelCatalog.DefaultOpenAiModel;

    [ObservableProperty]
    private string _apiKey = string.Empty;

    [ObservableProperty]
    private bool _saveApiKey = true;

    [ObservableProperty]
    private string _endpointOverride = string.Empty;

    /// <summary>API キー接続で利用可能なプロバイダー一覧</summary>
    public IReadOnlyList<AiProvider> ApiProviders { get; } =
    [AiProvider.OpenAI, AiProvider.Claude, AiProvider.Ollama];

    /// <summary>現在の API プロバイダーに応じたモデル候補</summary>
    public IReadOnlyList<string> ApiModelCandidates =>
        ApiProvider switch
        {
            AiProvider.Ollama => AiModelCatalog.OllamaModels,
            AiProvider.Claude => AiModelCatalog.ClaudeModels,
            _ => AiModelCatalog.OpenAiModels,
        };

    /// <summary>API キー欄を表示するか（API キーが必要な OpenAI / Claude 選択時のみ）</summary>
    public bool ShowApiKey => ApiProvider is AiProvider.OpenAI or AiProvider.Claude;

    /// <summary>エンドポイント欄を表示するか（Ollama 選択時のみ）</summary>
    public bool ShowEndpoint => ApiProvider == AiProvider.Ollama;

    // ── Codex 接続タブ ──

    /// <summary>Codex モデルプロバイダー候補（openai + config.toml）</summary>
    public ObservableCollection<string> CodexModelProviderCandidates { get; } = new();

    /// <summary>Codex モデル候補</summary>
    public ObservableCollection<string> CodexModelCandidates { get; } = new();

    [ObservableProperty]
    private string _codexModelProvider = OpenAiProviderName;

    [ObservableProperty]
    private string _codexModel = AiModelCatalog.DefaultOpenAiModel;

    [ObservableProperty]
    private string _codexAccountSummary = "未接続";

    private CodexAuthState _codexAuth = new(false, true, CodexAuthMode.None, "未接続");

    /// <summary>Codex 接続・認証状態の解決中か（解決までログインパネルのちらつきを抑止する）</summary>
    private bool _codexConnecting;

    /// <summary>Codex 認証セクションを表示するか（openai プロバイダー時のみ）</summary>
    public bool ShowCodexAuthSection => _codexEngine?.IsOpenAiProvider ?? false;

    /// <summary>Codex ログインパネルを表示するか（openai・認証必要・未ログイン、かつ接続解決済みのときのみ）</summary>
    public bool ShowCodexLoginPanel =>
        !_codexConnecting
        && (_codexEngine?.IsOpenAiProvider ?? false)
        && _codexAuth.RequiresOpenAiAuth
        && _codexAuth.AuthMode == CodexAuthMode.None;

    /// <summary>Codex ログアウト可能か</summary>
    public bool CanCodexLogout =>
        _codexAuth.IsStarted
        && (_codexAuth.AuthMode != CodexAuthMode.None || !_codexAuth.RequiresOpenAiAuth);

    /// <summary>Codex の状態ドット健全度（接続中/未開始=灰・未ログイン=赤・他=緑）</summary>
    public ConnectionHealth CodexStatusLevel
    {
        get
        {
            if (_codexConnecting || !_codexAuth.IsStarted)
            {
                return ConnectionHealth.Pending;
            }

            if (
                (_codexEngine?.IsOpenAiProvider ?? false)
                && _codexAuth.RequiresOpenAiAuth
                && _codexAuth.AuthMode == CodexAuthMode.None
            )
            {
                return ConnectionHealth.NeedsAction;
            }

            return ConnectionHealth.Ready;
        }
    }

    /// <summary>Codex ログアウトボタンを表示するか（openai かつログイン済みのときのみ）</summary>
    public bool ShowCodexLogout =>
        (_codexEngine?.IsOpenAiProvider ?? false) && _codexAuth.AuthMode != CodexAuthMode.None;

    // ── Claude Code 接続タブ ──

    [ObservableProperty]
    private string _claudeCodeModel = AiModelCatalog.DefaultClaudeCodeModel;

    [ObservableProperty]
    private string _claudeCodeStatusSummary = "未確認";

    [ObservableProperty]
    private ConnectionHealth _claudeCodeStatusLevel = ConnectionHealth.Pending;

    [ObservableProperty]
    private string _claudeCodeGuidance = "「再確認」を押すとログイン状態を確認できます。";

    /// <summary>Claude Code のモデル候補（エイリアス）</summary>
    public IReadOnlyList<string> ClaudeCodeModelCandidates { get; } =
        AiModelCatalog.ClaudeCodeModels;

    /// <summary>本番構成（実クライアント・WPF ディスパッチャ）で生成する</summary>
    public AiChatDialogViewModel(MainViewModel? mainViewModel)
        : this(
            mainViewModel,
            new WpfUiDispatcher(),
            settingsStore: null,
            codexClient: null,
            claudeCodeClient: null
        ) { }

    /// <summary>依存を注入して生成する（テスト用）</summary>
    public AiChatDialogViewModel(
        MainViewModel? mainViewModel,
        IUiDispatcher dispatcher,
        CodexAppServerSettingsStore? settingsStore,
        ICodexAppServerClient? codexClient,
        IClaudeCodeClient? claudeCodeClient = null
    )
    {
        _mainViewModel = mainViewModel;
        _dispatcher = dispatcher;
        _codexSettingsStore = settingsStore ?? new CodexAppServerSettingsStore();
        _claudeCodeSettingsStore = new ClaudeCodeSettingsStore();

        IErDiagramToolHost? toolHost = mainViewModel is not null
            ? new ErDiagramToolHost(mainViewModel)
            : null;

        _apiKeyEngine = new ChatTurnEngine(
            new ProviderRoutingTurnDriver(
                () => ApiProvider,
                new OpenAiTurnDriver(BuildOpenAiConnection),
                new AnthropicChatTurnDriver(BuildAnthropicConnection)
            ),
            toolHost ?? new NullToolHost(),
            dispatcher,
            () => ApiProvider == AiProvider.Ollama || !string.IsNullOrWhiteSpace(ApiKey)
        );

        var client = codexClient ?? new CodexAppServerClient();
        _codexEngine = new CodexChatEngine(client, toolHost, dispatcher);
        _codexEngine.AuthStateChanged += OnCodexAuthStateChanged;

        _claudeCodeEngine = new ClaudeCodeChatEngine(
            claudeCodeClient ?? new ClaudeCodeProcessClient(),
            toolHost,
            dispatcher
        );
        _claudeCodeEngine.StatusSummaryChanged += OnClaudeCodeStatusSummaryChanged;

        _engine = _apiKeyEngine;
        SubscribeEngine(_engine);
        LoadSettings();
    }

    /// <summary>ダイアログ表示時に設定・API キーを読み込み、Codex タブなら自動接続する</summary>
    public async Task InitializeAsync()
    {
        _isInitializing = true;
        LoadSettings();
        ApiKey = CurrentApiKeyStoreName is { } slot ? ApiKeyStore.Load(slot) : string.Empty;
        _isInitializing = false;

        if (IsCodexBackend)
        {
            await EnsureCodexConnectedAsync().ConfigureAwait(true);
        }
        else if (IsClaudeCodeBackend)
        {
            await EnsureClaudeCodeInitializedAsync().ConfigureAwait(true);
        }
    }

    /// <summary>新しい会話を開始する</summary>
    [RelayCommand(CanExecute = nameof(CanStartConversation))]
    private async Task StartConversationAsync()
    {
        IsBusy = true;

        try
        {
            await _engine.StartConversationAsync().ConfigureAwait(true);
            _conversationStarted = true;
            Messages.Clear();
            AddSystemMessage("会話を開始しました。ER 図について話しかけてください。");
            SendMessageCommand.NotifyCanExecuteChanged();
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>現在の会話表示と進行状態をクリアする（タブ切替時の確認後に呼ぶ）</summary>
    public void ClearConversation()
    {
        Messages.Clear();
        _conversationStarted = false;
        _currentAssistantMessage = null;
        _currentToolCallMessage = null;
        SendMessageCommand.NotifyCanExecuteChanged();
    }

    /// <summary>ユーザー入力を 1 ターンとして送信する</summary>
    [RelayCommand(CanExecute = nameof(CanSendMessage))]
    private async Task SendMessageAsync()
    {
        if (string.IsNullOrWhiteSpace(UserInput))
        {
            return;
        }

        var prompt = UserInput.Trim();
        UserInput = string.Empty;
        Messages.Add(new ErChatMessage { Role = ErChatMessageRole.User, Content = prompt });

        IsTurnInProgress = true;
        _currentAssistantMessage = null;
        _currentToolCallMessage = null;
        _diagramWasEmptyAtTurnStart =
            _mainViewModel is not null && _mainViewModel.Entities.Count == 0;

        try
        {
            await _engine.SendAsync(prompt).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            IsTurnInProgress = false;
            StatusMessage = $"送信に失敗しました: {ex.Message}";
        }
    }

    /// <summary>実行中のターンを中断する</summary>
    [RelayCommand(CanExecute = nameof(IsTurnInProgress))]
    private async Task InterruptAsync()
    {
        await _engine.InterruptAsync().ConfigureAwait(true);
    }

    // ── Codex 認証コマンド ──

    /// <summary>Codex の ChatGPT ブラウザログインを開始する</summary>
    [RelayCommand]
    private async Task CodexStartChatGptLoginAsync()
    {
        if (_codexEngine is null)
        {
            return;
        }

        var url = await _codexEngine.StartChatGptLoginAsync().ConfigureAwait(true);

        if (!string.IsNullOrWhiteSpace(url))
        {
            try
            {
                OpenBrowser(url);
            }
            catch (Exception ex)
            {
                StatusMessage = $"ブラウザを開けませんでした: {ex.Message}";
            }
        }
    }

    /// <summary>Codex からログアウトする</summary>
    [RelayCommand]
    private async Task CodexLogoutAsync()
    {
        if (_codexEngine is not null)
        {
            await _codexEngine.LogoutAsync().ConfigureAwait(true);
        }
    }

    /// <summary>Codex のアカウント状態を取り直す（「再確認」）</summary>
    [RelayCommand]
    private async Task CodexRefreshAsync()
    {
        if (_codexEngine is not null)
        {
            await _codexEngine.RefreshAccountStateAsync().ConfigureAwait(true);
        }
    }

    /// <summary>Claude のログイン状態を軽量プローブで取り直す（「再確認」）</summary>
    [RelayCommand]
    private async Task ClaudeCodeRefreshAsync()
    {
        await _claudeCodeEngine.RefreshAsync().ConfigureAwait(true);
    }

    /// <summary>設定を保存する（ウィンドウ非表示化時などに外部から呼ぶ）</summary>
    public void SaveSettings()
    {
        _codexSettingsStore.Save(
            new CodexAppServerSettings
            {
                ModelProvider = CodexModelProvider?.Trim() ?? string.Empty,
                Model = CodexModel?.Trim() ?? string.Empty,
            }
        );

        _claudeCodeSettingsStore.Save(
            new ClaudeCodeSettings { Model = ClaudeCodeModel?.Trim() ?? string.Empty }
        );
    }

    /// <summary>OpenAI 接続設定を現在の入力から組み立てる</summary>
    private OpenAiChatConnection BuildOpenAiConnection() =>
        new(
            ApiProvider,
            ApiKey,
            ApiModel,
            string.IsNullOrWhiteSpace(EndpointOverride) ? null : EndpointOverride
        );

    /// <summary>Anthropic (Claude) 接続設定を現在の入力から組み立てる</summary>
    private AnthropicChatConnection BuildAnthropicConnection() => new(ApiKey, ApiModel);

    /// <summary>現在のプロバイダーに対応する API キー保存名（API キー不要のプロバイダーは null）</summary>
    private string? CurrentApiKeyStoreName =>
        ApiProvider switch
        {
            AiProvider.OpenAI => OpenAiApiKeyStoreName,
            AiProvider.Claude => ClaudeApiKeyStoreName,
            _ => null,
        };

    /// <summary>保存済み設定と config.toml の候補を読み込む</summary>
    private void LoadSettings()
    {
        var settings = _codexSettingsStore.Load();
        LoadCodexModelCandidates();
        CodexModelProvider = string.IsNullOrWhiteSpace(settings.ModelProvider)
            ? OpenAiProviderName
            : settings.ModelProvider;
        CodexModel = string.IsNullOrWhiteSpace(settings.Model)
            ? AiModelCatalog.DefaultOpenAiModel
            : settings.Model;

        ClaudeCodeModel = _claudeCodeSettingsStore.Load().Model;
    }

    /// <summary>config.toml から Codex のプロバイダー・モデル候補を読み込む</summary>
    private void LoadCodexModelCandidates()
    {
        _configToml = CodexConfigTomlReader.Read();
        CodexModelProviderCandidates.Clear();
        CodexModelProviderCandidates.Add(OpenAiProviderName);

        foreach (var name in _configToml.ProviderNames)
        {
            if (!CodexModelProviderCandidates.Contains(name, StringComparer.OrdinalIgnoreCase))
            {
                CodexModelProviderCandidates.Add(name);
            }
        }

        RefreshCodexModelCandidates();
    }

    /// <summary>現在の Codex プロバイダーに応じてモデル候補を更新する</summary>
    private void RefreshCodexModelCandidates()
    {
        CodexModelCandidates.Clear();
        var isOpenAi =
            string.IsNullOrWhiteSpace(CodexModelProvider)
            || CodexModelProvider
                .Trim()
                .Equals(OpenAiProviderName, StringComparison.OrdinalIgnoreCase);

        if (isOpenAi)
        {
            foreach (var m in AiModelCatalog.OpenAiModels)
            {
                CodexModelCandidates.Add(m);
            }
        }
        else if (_configToml.ProviderModels.TryGetValue(CodexModelProvider.Trim(), out var models))
        {
            foreach (var m in models)
            {
                CodexModelCandidates.Add(m);
            }
        }
        else if (!string.IsNullOrWhiteSpace(_configToml.Model))
        {
            CodexModelCandidates.Add(_configToml.Model);
        }
    }

    /// <summary>Codex 接続が未確立なら接続を試みる（解決中はログインパネルのちらつきを抑止する）</summary>
    private async Task EnsureCodexConnectedAsync()
    {
        if (_codexEngine is null)
        {
            return;
        }

        _codexEngine.ModelProvider = CodexModelProvider;
        _codexEngine.Model = CodexModel;
        SetCodexConnecting(true);

        try
        {
            await _codexEngine.InitializeAsync().ConfigureAwait(true);
        }
        finally
        {
            SetCodexConnecting(false);
        }
    }

    /// <summary>Codex 接続解決中フラグを更新し、ログインパネル表示を再評価する</summary>
    private void SetCodexConnecting(bool value)
    {
        _codexConnecting = value;
        OnPropertyChanged(nameof(ShowCodexLoginPanel));
    }

    /// <summary>Claude Code エンジンを初期化し、状態サマリー・可否を反映する</summary>
    private async Task EnsureClaudeCodeInitializedAsync()
    {
        _claudeCodeEngine.Model = ClaudeCodeModel;
        await _claudeCodeEngine.InitializeAsync().ConfigureAwait(true);
        ClaudeCodeStatusSummary = _claudeCodeEngine.StatusSummary;
        ClaudeCodeStatusLevel = _claudeCodeEngine.StatusLevel;
        ClaudeCodeGuidance = _claudeCodeEngine.Guidance;
        NotifyReadinessChanged();
    }

    // ── 設定変更フック ──

    partial void OnSelectedBackendChanged(ErChatBackendKind value)
    {
        UnsubscribeEngine(_engine);
        _engine = value switch
        {
            ErChatBackendKind.Codex when _codexEngine is not null => _codexEngine,
            ErChatBackendKind.ClaudeCode => _claudeCodeEngine,
            _ => _apiKeyEngine,
        };
        SubscribeEngine(_engine);

        _conversationStarted = false;
        OnPropertyChanged(nameof(IsApiKeyBackend));
        OnPropertyChanged(nameof(IsCodexBackend));
        OnPropertyChanged(nameof(IsClaudeCodeBackend));
        NotifyReadinessChanged();

        if (value == ErChatBackendKind.Codex)
        {
            _ = EnsureCodexConnectedAsync();
        }
        else if (value == ErChatBackendKind.ClaudeCode)
        {
            _ = EnsureClaudeCodeInitializedAsync();
        }
    }

    partial void OnApiProviderChanged(AiProvider value)
    {
        OnPropertyChanged(nameof(ApiModelCandidates));
        OnPropertyChanged(nameof(ShowApiKey));
        OnPropertyChanged(nameof(ShowEndpoint));
        ApiModel = ApiModelCandidates[0];

        if (value == AiProvider.Ollama && string.IsNullOrWhiteSpace(EndpointOverride))
        {
            EndpointOverride = "http://localhost:11434/v1";
        }

        // プロバイダーごとに別の API キーを保持するため、切替時に保存済みキーを読み直す
        // （読み込みで OnApiKeyChanged が走っても上書き保存しないよう _isInitializing で抑止する）
        var wasInitializing = _isInitializing;
        _isInitializing = true;
        ApiKey = CurrentApiKeyStoreName is { } slot ? ApiKeyStore.Load(slot) : string.Empty;
        _isInitializing = wasInitializing;

        NotifyReadinessChanged();
    }

    partial void OnApiKeyChanged(string value)
    {
        PersistApiKey();
        NotifyReadinessChanged();
    }

    partial void OnSaveApiKeyChanged(bool value) => PersistApiKey();

    partial void OnUserInputChanged(string value) => SendMessageCommand.NotifyCanExecuteChanged();

    partial void OnCodexModelProviderChanged(string value)
    {
        RefreshCodexModelCandidates();

        if (_codexEngine is not null)
        {
            _codexEngine.ModelProvider = value;
        }

        OnPropertyChanged(nameof(ShowCodexAuthSection));
        OnPropertyChanged(nameof(ShowCodexLoginPanel));
        NotifyReadinessChanged();
    }

    partial void OnCodexModelChanged(string value)
    {
        if (_codexEngine is not null)
        {
            _codexEngine.Model = value;
        }
    }

    partial void OnClaudeCodeModelChanged(string value) => _claudeCodeEngine.Model = value;

    /// <summary>保存設定に従い、現在のプロバイダーの API キーを永続化する（キー不要のプロバイダーは何もしない）</summary>
    private void PersistApiKey()
    {
        if (_isInitializing)
        {
            return;
        }

        if (CurrentApiKeyStoreName is { } slot)
        {
            ApiKeyStore.Save(slot, SaveApiKey ? ApiKey : string.Empty);
        }
    }

    // ── エンジンイベント ──

    /// <summary>アクティブエンジンのイベントを購読する</summary>
    private void SubscribeEngine(IErChatEngine engine)
    {
        engine.AssistantDeltaReceived += OnAssistantDelta;
        engine.ToolActivityReceived += OnToolActivity;
        engine.TurnCompleted += OnTurnCompleted;
        engine.StatusChanged += OnEngineStatus;
    }

    /// <summary>エンジンのイベント購読を解除する</summary>
    private void UnsubscribeEngine(IErChatEngine engine)
    {
        engine.AssistantDeltaReceived -= OnAssistantDelta;
        engine.ToolActivityReceived -= OnToolActivity;
        engine.TurnCompleted -= OnTurnCompleted;
        engine.StatusChanged -= OnEngineStatus;
    }

    private void OnAssistantDelta(object? sender, string delta) => RunOnUi(() => ApplyDelta(delta));

    private void OnToolActivity(object? sender, ErChatToolActivity activity) =>
        RunOnUi(() => ApplyToolActivity(activity));

    private void OnTurnCompleted(object? sender, ErChatTurnResult result) =>
        RunOnUi(() => ApplyTurnCompleted(result));

    private void OnEngineStatus(object? sender, string message) =>
        RunOnUi(() => StatusMessage = message);

    private void OnCodexAuthStateChanged(object? sender, CodexAuthState state) =>
        RunOnUi(() => ApplyCodexAuthState(state));

    private void OnClaudeCodeStatusSummaryChanged(object? sender, EventArgs e) =>
        RunOnUi(() =>
        {
            ClaudeCodeStatusSummary = _claudeCodeEngine.StatusSummary;
            ClaudeCodeStatusLevel = _claudeCodeEngine.StatusLevel;
            ClaudeCodeGuidance = _claudeCodeEngine.Guidance;
            NotifyReadinessChanged();
        });

    /// <summary>ストリーミング差分を組み立て中のアシスタント吹き出しへ追記する</summary>
    private void ApplyDelta(string delta)
    {
        if (_currentAssistantMessage is null)
        {
            _currentAssistantMessage = new ErChatMessage
            {
                Role = ErChatMessageRole.Assistant,
                Content = delta,
            };
            Messages.Add(_currentAssistantMessage);
        }
        else
        {
            _currentAssistantMessage.Content += delta;
        }
    }

    /// <summary>ツール実行活動を ToolCall 吹き出しへ追加し、次のアシスタント発話を新吹き出しにする</summary>
    private void ApplyToolActivity(ErChatToolActivity activity)
    {
        var text = $"[{activity.ToolName}] {activity.Result}";

        if (_currentToolCallMessage is null)
        {
            _currentToolCallMessage = new ErChatMessage
            {
                Role = ErChatMessageRole.ToolCall,
                Content = text,
                IsExpanded = true,
            };
            Messages.Add(_currentToolCallMessage);
        }
        else
        {
            _currentToolCallMessage.Content += "\n" + text;
        }

        _currentAssistantMessage = null;
    }

    /// <summary>ターン完了に伴い進行状態を解除し、結果に応じてステータス・自動整列を行う</summary>
    private void ApplyTurnCompleted(ErChatTurnResult result)
    {
        IsTurnInProgress = false;
        _currentAssistantMessage = null;
        _currentToolCallMessage = null;
        CollapseToolCallMessages();

        if (result.Success)
        {
            ArrangeNewDiagramIfCreated();
            StatusMessage = "応答が完了しました。";
        }
        else if (!string.IsNullOrWhiteSpace(result.Error))
        {
            AddSystemMessage($"エラー: {result.Error}");
            StatusMessage = $"エラーが発生しました: {result.Error}";
        }
        else
        {
            StatusMessage = "処理が中断されました。";
        }
    }

    /// <summary>Codex 認証状態を UI バインド用プロパティへ反映する</summary>
    private void ApplyCodexAuthState(CodexAuthState state)
    {
        _codexAuth = state;
        CodexAccountSummary = state.AccountSummary;
        OnPropertyChanged(nameof(ShowCodexAuthSection));
        OnPropertyChanged(nameof(ShowCodexLoginPanel));
        OnPropertyChanged(nameof(CanCodexLogout));
        OnPropertyChanged(nameof(ShowCodexLogout));
        OnPropertyChanged(nameof(CodexStatusLevel));
        NotifyReadinessChanged();
    }

    /// <summary>空の ER 図から始まったターンでエンティティが生成された場合のみ自動整列する</summary>
    private void ArrangeNewDiagramIfCreated()
    {
        if (
            _diagramWasEmptyAtTurnStart
            && _mainViewModel is not null
            && _mainViewModel.Entities.Count > 0
        )
        {
            _mainViewModel.AutoArrangeNewDiagram();
        }
    }

    /// <summary>全 ToolCall 吹き出しを折り畳む</summary>
    private void CollapseToolCallMessages()
    {
        foreach (var message in Messages)
        {
            if (message.Role == ErChatMessageRole.ToolCall)
            {
                message.IsExpanded = false;
            }
        }
    }

    /// <summary>送信・会話開始の可否変更をまとめて通知する</summary>
    private void NotifyReadinessChanged()
    {
        OnPropertyChanged(nameof(CanStartConversation));
        OnPropertyChanged(nameof(CanSendMessage));
        StartConversationCommand.NotifyCanExecuteChanged();
        SendMessageCommand.NotifyCanExecuteChanged();
    }

    /// <summary>システムメッセージをチャットへ追加する</summary>
    private void AddSystemMessage(string text) =>
        Messages.Add(new ErChatMessage { Role = ErChatMessageRole.System, Content = text });

    /// <summary>処理を UI スレッドで実行する</summary>
    private void RunOnUi(Action action)
    {
        var dispatcher = Application.Current?.Dispatcher;

        if (dispatcher is null || dispatcher.CheckAccess())
        {
            action();
            return;
        }

        _ = dispatcher.InvokeAsync(action);
    }

    /// <summary>ツール無効時に使うダミーホスト（MainViewModel 不在時）</summary>
    private sealed class NullToolHost : IErDiagramToolHost
    {
        public (string Result, bool Success) Execute(string toolName, string argumentsJson) =>
            ("ツールは利用できません。", false);
    }
}
