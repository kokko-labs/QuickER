using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using QuickER.AI;
using QuickER.AI.Chat.Resources;
using QuickER.AI.UI;
using QuickER.Gui.Abstractions;
using QuickER.Gui.Common;

namespace QuickER.AI.Chat;

/// <summary>AI チャット（API キー接続 / Codex 接続 / Claude Code 接続の 3 エンジン）を扱う統合ダイアログ用 ViewModel</summary>
/// <remarks>チャット UI・メッセージ・送信/中断・自動整列を共通化し、エンジン固有部分のみ <see cref="IErChatEngine"/> で差し替える</remarks>
public partial class AiChatDialogViewModel : ObservableObject
{
    private readonly IErDiagramChatHost? _host;
    private readonly IUiDispatcher _dispatcher;
    private readonly IDialogService _dialogs;

    /// <summary>接続方式タブ（API キー / Codex / Claude Code）の状態と永続化を束ねる共通 VM 部品</summary>
    public ChatConnectionSettingsViewModel Connection { get; }

    private readonly ChatTurnEngine _apiKeyEngine;
    private readonly CodexChatEngine? _codexEngine;
    private readonly ClaudeCodeChatEngine _claudeCodeEngine;
    private IErChatEngine _engine;

    private bool _conversationStarted;
    private bool _diagramWasEmptyAtTurnStart;
    private ErChatMessage? _currentAssistantMessage;
    private ErChatMessage? _currentToolCallMessage;

    /// <summary>ブラウザで URL を開く処理（テスト時に差し替え可能）</summary>
    internal Action<string> OpenBrowser { get; set; } =
        url => Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });

    /// <summary>チャットメッセージ一覧</summary>
    public ObservableCollection<ErChatMessage> Messages { get; } = new();

    /// <summary>ダイアログを閉じる際に呼ぶアクション</summary>
    public Action<bool>? CloseAction { get; set; }

    // ── 共通のチャット状態 ──

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
                // ターン実行中は添付操作も禁止する（ボタン・削除の無効化に連動）
                Attachments.IsTurnInProgress = value;
            }
        }
    }

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

    // ── Codex 認証状態（子の状態タブとは別に、認証解決は親エンジンの責務） ──

    [ObservableProperty]
    private string _codexAccountSummary = Strings.Chat_CodexNotConnected;

    private CodexAuthState _codexAuth = new(
        false,
        true,
        CodexAuthMode.None,
        Strings.Chat_CodexNotConnected
    );

    /// <summary>Codex 接続・認証状態の解決中か（解決までログインパネルのちらつきを抑止する）</summary>
    private bool _codexConnecting;

    /// <summary>Codex 認証セクションを表示するか（openai プロバイダー時のみ）</summary>
    public bool ShowCodexAuthSection => _codexEngine?.IsOpenAiProvider ?? false;

    /// <summary>
    /// Codex ログインパネルを表示するか（openai・認証必要・未ログイン、かつ接続解決済みのときのみ）。
    /// codex CLI 未検出のときは表示しない（ログインを促しても解決しないため。案内は
    /// <see cref="CodexGuidance"/> のインストール案内が担う）。
    /// </summary>
    public bool ShowCodexLoginPanel =>
        !_codexConnecting
        && !_codexAuth.IsCliMissing
        && (_codexEngine?.IsOpenAiProvider ?? false)
        && _codexAuth.RequiresOpenAiAuth
        && _codexAuth.AuthMode == CodexAuthMode.None;

    /// <summary>Codex の状態依存の案内文（未検出＝インストール案内・接続失敗＝理由。正常時は空）</summary>
    [ObservableProperty]
    private string _codexGuidance = string.Empty;

    /// <summary>Codex の案内文を表示するか（空なら行ごと隠す）</summary>
    public bool ShowCodexGuidance => !string.IsNullOrWhiteSpace(CodexGuidance);

    /// <summary>Codex ログアウト可能か</summary>
    public bool CanCodexLogout =>
        _codexAuth.IsStarted
        && (_codexAuth.AuthMode != CodexAuthMode.None || !_codexAuth.RequiresOpenAiAuth);

    /// <summary>Codex の状態ドット健全度（CLI 未検出/未ログイン=赤・接続中/未開始=灰・他=緑）</summary>
    public ConnectionHealth CodexStatusLevel
    {
        get
        {
            // CLI 未検出はユーザー操作（インストール）が要るため、接続解決中でも赤で確定させる
            if (_codexAuth.IsCliMissing)
            {
                return ConnectionHealth.NeedsAction;
            }

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

    /// <summary>送信待ち添付を束ねる共通 VM 部品（チップ列・可否・追加/削除）</summary>
    public AttachmentListViewModel Attachments { get; }

    /// <summary>ファイル選択ダイアログの供給元（添付ボタンのファイル選択に使う）</summary>
    private readonly IFileDialogService _files;

    /// <summary>本番構成（実クライアント・WPF ディスパッチャ）で生成する</summary>
    public AiChatDialogViewModel(IErDiagramChatHost? host, IDialogService? dialogService = null)
        : this(
            host,
            new WpfUiDispatcher(),
            settingsStore: null,
            codexClient: null,
            claudeCodeClient: null,
            dialogService: dialogService
        ) { }

    /// <summary>依存を注入して生成する（テスト用）</summary>
    /// <param name="apiKeyLoader">
    /// API キー読込 seam（省略時は <see cref="ApiKeyStore.Load(string)"/>）。<see cref="Connection"/> へ透過する
    /// </param>
    /// <param name="apiKeySaver">
    /// API キー保存 seam（省略時は <see cref="ApiKeyStore.Save(string, string)"/>）。<see cref="Connection"/> へ透過する
    /// </param>
    public AiChatDialogViewModel(
        IErDiagramChatHost? host,
        IUiDispatcher dispatcher,
        AiSettingsStore? settingsStore,
        ICodexAppServerClient? codexClient,
        IClaudeCodeClient? claudeCodeClient = null,
        IDialogService? dialogService = null,
        IFileDialogService? files = null,
        ChatAttachmentFactory.ImageShrinker? imageShrinker = null,
        Func<string, string?>? apiKeyLoader = null,
        Action<string, string>? apiKeySaver = null
    )
    {
        _host = host;
        _dispatcher = dispatcher;
        _dialogs = dialogService ?? new MessageBoxDialogService();
        _files = files ?? new WpfFileDialogService();

        // 添付部品は本番では WPF の画像縮小を差し込む（テストでは注入された縮小・null）
        Attachments = new AttachmentListViewModel(
            reportStatus: message => StatusMessage = message,
            shrinker: imageShrinker ?? WpfImageShrinker.Shrink
        );

        IErDiagramToolHost? toolHost = host?.ToolHost;

        // 接続方式タブの状態部品。エンジン生成前に用意しておき、エンジンの入力ラムダから参照させる
        // （PropertyChanged 購読と LoadSettings は下記の ctor 順序に従い、エンジン確立後に行う）
        Connection = new ChatConnectionSettingsViewModel(
            AiDialogKind.AiChat,
            settingsStore,
            apiKeyLoader: apiKeyLoader,
            apiKeySaver: apiKeySaver
        );

        _apiKeyEngine = new ChatTurnEngine(
            new ProviderRoutingTurnDriver(
                () => Connection.ApiProvider,
                new OpenAiTurnDriver(BuildOpenAiConnection, ErDesignProfile.ErDesign),
                new AnthropicChatTurnDriver(BuildAnthropicConnection, ErDesignProfile.ErDesign)
            ),
            toolHost ?? new NullToolHost(),
            dispatcher,
            () =>
                Connection.ApiProvider == AiProvider.LocalLlm
                || !string.IsNullOrWhiteSpace(Connection.ApiKey),
            ErDesignProfile.ErDesign,
            attachmentSupport: () =>
                AttachmentSupportResolver.ForApiKeyProvider(Connection.ApiProvider)
        );

        var client = codexClient ?? new CodexAppServerClient();
        _codexEngine = new CodexChatEngine(client, toolHost, dispatcher, ErDesignProfile.ErDesign);
        _codexEngine.AuthStateChanged += OnCodexAuthStateChanged;

        _claudeCodeEngine = new ClaudeCodeChatEngine(
            claudeCodeClient ?? new ClaudeCodeProcessClient(),
            toolHost,
            dispatcher,
            ErDesignProfile.ErDesign
        );
        _claudeCodeEngine.StatusSummaryChanged += OnClaudeCodeStatusSummaryChanged;

        _engine = _apiKeyEngine;
        SubscribeEngine(_engine);

        // ctor 順序厳守: エンジン確立 → Connection.PropertyChanged 購読 → Connection.LoadSettings
        // （購読前にロードするとエンジンのモデル同期が漏れる）
        Connection.PropertyChanged += OnConnectionPropertyChanged;
        Connection.LoadSettings();

        RefreshAttachmentSupport();
    }

    /// <summary>添付ボタン押下でファイル選択ダイアログを開き、選択ファイルを添付へ取り込む</summary>
    public void PickAndAddAttachments()
    {
        if (!Attachments.IsEnabled)
        {
            return;
        }

        var paths = _files.PickOpenFiles(Attachments.FileDialogFilter);

        if (paths.Count > 0)
        {
            Attachments.AddFiles(paths);
        }
    }

    /// <summary>ドロップされたファイル群を対応拡張子のみ添付へ取り込む（非対応はステータス通知）</summary>
    /// <param name="paths">ドロップされたファイルパス群</param>
    public void AddDroppedFiles(IReadOnlyList<string> paths) => Attachments.AddFiles(paths);

    /// <summary>現在のエンジンに応じて添付部品の対応範囲を再評価する</summary>
    /// <remarks>API キーエンジンはプロバイダー依存（合成ルールを注入済み）・Codex/Claude Code は固定値を公開する</remarks>
    private void RefreshAttachmentSupport() => Attachments.Support = _engine.AttachmentSupport;

    /// <summary>ダイアログ表示時に API キーを読み込み、Codex タブなら自動接続する</summary>
    /// <remarks>設定・候補の読込は ctor で <see cref="ChatConnectionSettingsViewModel.LoadSettings"/> 済み。</remarks>
    public async Task InitializeAsync()
    {
        // 現在のプロバイダーの保存済み API キーを読み直す（子側で _isInitializing 抑止）
        Connection.Initialize();

        if (Connection.IsCodexBackend)
        {
            await EnsureCodexConnectedAsync().ConfigureAwait(true);
        }
        else if (Connection.IsClaudeCodeBackend)
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
            AddSystemMessage(Strings.Chat_ConversationStarted);
            SendMessageCommand.NotifyCanExecuteChanged();
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// 接続方式を切り替える。会話中はクリア確認を出し、OK の場合は会話をクリアして切り替える。
    /// </summary>
    /// <param name="newBackend">切り替え先の接続方式</param>
    /// <returns>切り替えた（または既に同じ）場合 true、ユーザーがキャンセルした場合 false</returns>
    public bool TryChangeBackend(ErChatBackendKind newBackend)
    {
        if (newBackend == Connection.SelectedBackend)
        {
            return true;
        }

        if (HasConversation)
        {
            if (
                !_dialogs.Confirm(
                    Strings.Chat_SwitchBackendConfirm,
                    Strings.Chat_SwitchBackendConfirmTitle
                )
            )
            {
                return false;
            }

            ClearConversation();
        }

        Connection.SelectedBackend = newBackend;
        return true;
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

        // 送信の直前に添付を取り出し、ユーザー吹き出しへ「📎 name」の要約を載せる
        var attachments = Attachments.BuildAttachments();
        Messages.Add(
            new ErChatMessage
            {
                Role = ErChatMessageRole.User,
                Content = prompt,
                AttachmentSummary = Attachments.BuildSummary(),
            }
        );

        IsTurnInProgress = true;
        _currentAssistantMessage = null;
        _currentToolCallMessage = null;
        _diagramWasEmptyAtTurnStart = _host is not null && _host.IsEmpty;

        try
        {
            await _engine.SendAsync(prompt, attachments).ConfigureAwait(true);
            // 送信できたら添付をクリアする（メッセージ単位のライフサイクル）
            Attachments.Clear();
        }
        catch (Exception ex)
        {
            IsTurnInProgress = false;
            StatusMessage = string.Format(Strings.Chat_SendFailedFormat, ex.Message);
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
                StatusMessage = string.Format(Strings.Chat_BrowserOpenFailedFormat, ex.Message);
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
    /// <remarks>接続タブの状態保存は子（<see cref="ChatConnectionSettingsViewModel.SaveSettings"/>）へ委譲する。</remarks>
    public void SaveSettings() => Connection.SaveSettings();

    /// <summary>OpenAI 接続設定を現在の入力から組み立てる</summary>
    /// <remarks>
    /// エンドポイント上書きは <see cref="ChatConnectionSettingsViewModel.EffectiveEndpointOverride"/> から取る
    /// （ローカル LLM 以外では null＝欄が非表示のまま残った値が OpenAI 接続へ紛れ込まない）。
    /// </remarks>
    internal OpenAiChatConnection BuildOpenAiConnection() =>
        new(
            Connection.ApiProvider,
            Connection.ApiKey,
            Connection.ApiModel,
            Connection.EffectiveEndpointOverride
        );

    /// <summary>Anthropic (Claude) 接続設定を現在の入力から組み立てる</summary>
    private AnthropicChatConnection BuildAnthropicConnection() =>
        new(Connection.ApiKey, Connection.ApiModel);

    /// <summary>Codex 接続が未確立なら接続を試みる（解決中はログインパネルのちらつきを抑止する）</summary>
    private async Task EnsureCodexConnectedAsync()
    {
        if (_codexEngine is null)
        {
            return;
        }

        _codexEngine.ModelProvider = Connection.CodexModelProvider;
        _codexEngine.Model = Connection.CodexModel;
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
        _claudeCodeEngine.Model = Connection.ClaudeCodeModel;
        await _claudeCodeEngine.InitializeAsync().ConfigureAwait(true);
        Connection.ClaudeCodeStatusSummary = _claudeCodeEngine.StatusSummary;
        Connection.ClaudeCodeStatusLevel = _claudeCodeEngine.StatusLevel;
        Connection.ClaudeCodeGuidance = _claudeCodeEngine.Guidance;
        NotifyReadinessChanged();
    }

    // ── 設定変更フック ──

    /// <summary>
    /// 接続方式タブ（子 VM）の変更を購読し、親の責務（エンジン差し替え・モデル同期・readiness 再評価）を反映する。
    /// </summary>
    /// <remarks>
    /// 子は partial フック完了後に PropertyChanged を発火するため、本ハンドラ実行時点で子の内部状態は整合済み。
    /// 子側の候補更新・Is* 通知・API キー永続化は子が済ませており、ここでは親固有の処理のみを行う。
    /// </remarks>
    private void OnConnectionPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(ChatConnectionSettingsViewModel.SelectedBackend):
                UnsubscribeEngine(_engine);
                _engine = Connection.SelectedBackend switch
                {
                    ErChatBackendKind.Codex when _codexEngine is not null => _codexEngine,
                    ErChatBackendKind.ClaudeCode => _claudeCodeEngine,
                    _ => _apiKeyEngine,
                };
                SubscribeEngine(_engine);

                _conversationStarted = false;
                // バックエンド切替で添付範囲を再評価する（非対応になったら添付部品側で Pending をクリア・通知する）
                RefreshAttachmentSupport();
                NotifyReadinessChanged();

                if (Connection.SelectedBackend == ErChatBackendKind.Codex)
                {
                    _ = EnsureCodexConnectedAsync();
                }
                else if (Connection.SelectedBackend == ErChatBackendKind.ClaudeCode)
                {
                    _ = EnsureClaudeCodeInitializedAsync();
                }

                break;

            case nameof(ChatConnectionSettingsViewModel.ApiProvider):
                // API キー接続はプロバイダーで添付範囲が変わる（OpenAI／ローカル LLM=画像・Claude=画像＋PDF）
                if (Connection.IsApiKeyBackend)
                {
                    RefreshAttachmentSupport();
                }

                NotifyReadinessChanged();
                break;

            case nameof(ChatConnectionSettingsViewModel.ApiKey):
                // API キー永続化は子が済ませている。親は readiness 再評価のみ。
                NotifyReadinessChanged();
                break;

            case nameof(ChatConnectionSettingsViewModel.CodexModelProvider):
                if (_codexEngine is not null)
                {
                    _codexEngine.ModelProvider = Connection.CodexModelProvider;
                }

                OnPropertyChanged(nameof(ShowCodexAuthSection));
                OnPropertyChanged(nameof(ShowCodexLoginPanel));
                NotifyReadinessChanged();
                break;

            case nameof(ChatConnectionSettingsViewModel.CodexModel):
                if (_codexEngine is not null)
                {
                    _codexEngine.Model = Connection.CodexModel;
                }

                break;

            case nameof(ChatConnectionSettingsViewModel.ClaudeCodeModel):
                _claudeCodeEngine.Model = Connection.ClaudeCodeModel;
                break;
        }
    }

    partial void OnUserInputChanged(string value) => SendMessageCommand.NotifyCanExecuteChanged();

    partial void OnCodexGuidanceChanged(string value) =>
        OnPropertyChanged(nameof(ShowCodexGuidance));

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
            Connection.ClaudeCodeStatusSummary = _claudeCodeEngine.StatusSummary;
            Connection.ClaudeCodeStatusLevel = _claudeCodeEngine.StatusLevel;
            Connection.ClaudeCodeGuidance = _claudeCodeEngine.Guidance;
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
            // 成功したターンで使ったモデルを MRU 履歴へ記録する（ローカル LLM / Codex のガードは子 VM 側）
            Connection.RecordSuccessfulModel();
            ArrangeNewDiagramIfCreated();
            StatusMessage = Strings.Chat_ResponseCompleted;
        }
        else if (!string.IsNullOrWhiteSpace(result.Error))
        {
            AddSystemMessage(string.Format(Strings.Chat_ErrorSystemFormat, result.Error));
            StatusMessage = string.Format(Strings.Chat_ErrorStatusFormat, result.Error);
        }
        else
        {
            StatusMessage = Strings.Chat_Interrupted;
        }
    }

    /// <summary>Codex 認証状態を UI バインド用プロパティへ反映する</summary>
    private void ApplyCodexAuthState(CodexAuthState state)
    {
        _codexAuth = state;
        CodexAccountSummary = state.AccountSummary;
        CodexGuidance = state.Guidance;
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
        if (_diagramWasEmptyAtTurnStart && _host is not null && !_host.IsEmpty)
        {
            _host.AutoArrangeNewDiagram();
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
    /// <remarks>
    /// <see cref="Application.Current"/> を直接参照せず、注入された <see cref="IUiDispatcher"/> で
    /// マーシャリングする（テストでは同期実行のフェイクに差し替わり、他テストが作った
    /// Application の非ポンプなディスパッチャを拾って処理が実行されない順序依存を防ぐ）。
    /// </remarks>
    private void RunOnUi(Action action) =>
        _dispatcher.Invoke(() =>
        {
            action();
            return true;
        });

    /// <summary>ツール無効時に使うダミーホスト（MainViewModel 不在時）</summary>
    private sealed class NullToolHost : IErDiagramToolHost
    {
        public (string Result, bool Success) Execute(string toolName, string argumentsJson) =>
            (Strings.Chat_ToolsUnavailable, false);
    }
}
