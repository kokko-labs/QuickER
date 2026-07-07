using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using QuickER.AI;
using QuickER.AI.UI;
using QuickER.Gui.Abstractions;
using QuickER.Model;

namespace QuickER.AI.Mock;

/// <summary>
/// AI モック生成ダイアログ（左チャット／右 HTML プレビュー）の ViewModel。
/// </summary>
/// <remarks>
/// 接続方式（API キー / Codex / Claude）の選択・接続状態は <see cref="AiChatDialogViewModel"/> と
/// 同じ構造を踏襲する。会話は「＋新しい会話」で <see cref="MockDesignSession"/> を用意し、初回送信で
/// 現在の ER 図＋要望を、2 回目以降はフィードバックを送信して、
/// 提出された HTML を <see cref="HtmlUpdated"/> で通知する。プレビューへの反映（一時ファイル書き出し）は
/// ダイアログ側が受け取り、WebView2 へ Navigate する。
/// </remarks>
public partial class MockGenerationDialogViewModel : ObservableObject
{
    private readonly IMockDiagramSource _diagramSource;
    private readonly IUiDispatcher _dispatcher;
    private readonly IFileDialogService _files;
    private readonly IMockProjectGenerator _mockProjectGenerator;
    private readonly Func<ErChatProfile, IErDiagramToolHost, IErChatEngine> _apiKeyEngineFactory;
    private readonly Func<ErChatProfile, IErDiagramToolHost, IErChatEngine> _codexEngineFactory;
    private readonly Func<
        ErChatProfile,
        IErDiagramToolHost,
        IErChatEngine
    > _claudeCodeEngineFactory;

    /// <summary>接続方式タブ（API キー / Codex / Claude Code）の状態と永続化を束ねる共通 VM 部品</summary>
    public ChatConnectionSettingsViewModel Connection { get; }

    /// <summary>現在の生成セッション（会話開始前は null）</summary>
    private MockDesignSession? _session;

    /// <summary>会話が開始済みか（「＋新しい会話」で true、バックエンド切替でリセット）</summary>
    private bool _conversationStarted;

    /// <summary>この会話で初回送信を済ませたか（初回=StartAsync／2 回目以降=SendFeedbackAsync の分岐に使う）</summary>
    private bool _firstMessageSent;

    /// <summary>
    /// 直近に確定した（提出された）モック HTML。
    /// セッション破棄（「＋新しい会話」）後もプレビュー・保存・第2ステップの有効性を維持するため、
    /// <see cref="MockDesignSession.CurrentHtml"/> ではなく VM 側で保持する。
    /// </summary>
    private string? _lastHtml;

    /// <summary>ブラウザで URL を開く処理（テスト時に差し替え可能）</summary>
    internal Action<string> OpenBrowser { get; set; } =
        url => Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });

    /// <summary>フォルダをエクスプローラで開く処理（テスト時に差し替え可能）</summary>
    internal Action<string> OpenFolder { get; set; } =
        path => Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });

    /// <summary>チャットメッセージ一覧</summary>
    public ObservableCollection<ErChatMessage> Messages { get; } = new();

    /// <summary>モック HTML が更新されたときにダイアログへ通知する（プレビュー反映用）</summary>
    public event EventHandler<MockHtmlUpdate>? HtmlUpdated;

    // ── 共通のチャット状態 ──

    [ObservableProperty]
    private string _userInput = string.Empty;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    private bool _isTurnInProgress;

    /// <summary>ターンが実行中か（生成開始・フィードバック・中断コマンドの可否に連動）</summary>
    public bool IsTurnInProgress
    {
        get => _isTurnInProgress;
        set
        {
            if (SetProperty(ref _isTurnInProgress, value))
            {
                StartConversationCommand.NotifyCanExecuteChanged();
                SendMessageCommand.NotifyCanExecuteChanged();
                InterruptCommand.NotifyCanExecuteChanged();
                OnPropertyChanged(nameof(CanEditInput));
                // ターン実行中は添付操作も禁止する
                Attachments.IsTurnInProgress = value;
                NotifyMockGenChanged();
            }
        }
    }

    /// <summary>現在選択中のバックエンドが送信可能な状態か（接続・認証が整っているか）</summary>
    public bool IsBackendReady =>
        Connection.SelectedBackend switch
        {
            ErChatBackendKind.Codex => _codexReady,
            ErChatBackendKind.ClaudeCode => _claudeCodeReady,
            _ => Connection.ApiProvider == AiProvider.Ollama
                || !string.IsNullOrWhiteSpace(Connection.ApiKey),
        };

    /// <summary>図が空（エンティティ 0）か（会話開始の可否に使う）</summary>
    public bool IsDiagramEmpty => _diagramSource.IsEmpty;

    /// <summary>新しい会話を開始できるか（接続 OK・図が空でない・ターン非実行中）</summary>
    public bool CanStartConversation => IsBackendReady && !IsDiagramEmpty && !IsTurnInProgress;

    /// <summary>メッセージを送信できるか（会話開始済み・接続 OK・入力あり・ターン非実行中）</summary>
    public bool CanSendMessage =>
        _conversationStarted
        && IsBackendReady
        && !IsTurnInProgress
        && !string.IsNullOrWhiteSpace(UserInput);

    /// <summary>入力欄を編集できるか（ターン実行中は禁止）</summary>
    public bool CanEditInput => !IsTurnInProgress;

    /// <summary>HTML を保存できるか（1 度でもモックが提出されているか）</summary>
    public bool CanSaveHtml => !string.IsNullOrEmpty(_lastHtml);

    // ── 第2ステップ: WPF モックプロジェクト生成（Claude Code 限定） ──

    /// <summary>生成先の出力フォルダ</summary>
    [ObservableProperty]
    private string _outputFolder = string.Empty;

    /// <summary>生成するプロジェクト名（既定は図名由来の PascalCase）</summary>
    [ObservableProperty]
    private string _projectName = "MockApp";

    /// <summary>WPF モック生成の進捗ログ（追記式・自動スクロール表示）</summary>
    [ObservableProperty]
    private string _mockGenLog = string.Empty;

    /// <summary>claude CLI が検出済みか（第2ステップの有効条件）</summary>
    [ObservableProperty]
    private bool _isClaudeCliAvailable;

    /// <summary>dotnet SDK が検出済みか（第2ステップの有効条件）</summary>
    [ObservableProperty]
    private bool _isDotnetAvailable;

    private bool _isMockGenInProgress;

    /// <summary>WPF モック生成が実行中か（生成・中断ボタンの可否に連動）</summary>
    public bool IsMockGenInProgress
    {
        get => _isMockGenInProgress;
        set
        {
            if (SetProperty(ref _isMockGenInProgress, value))
            {
                NotifyMockGenChanged();
            }
        }
    }

    private bool _mockGenCompleted;

    /// <summary>直近の WPF モック生成が完了したか（成功・失敗を問わず。フォルダを開く／ログ案内の表示制御）</summary>
    public bool MockGenCompleted
    {
        get => _mockGenCompleted;
        private set
        {
            if (SetProperty(ref _mockGenCompleted, value))
            {
                OnPropertyChanged(nameof(ShowOpenFolder));
                OpenOutputFolderCommand.NotifyCanExecuteChanged();
            }
        }
    }

    private bool _mockGenSucceeded;

    /// <summary>直近の WPF モック生成が成功したか（フォルダを開くボタンの表示制御）</summary>
    public bool MockGenSucceeded
    {
        get => _mockGenSucceeded;
        private set
        {
            if (SetProperty(ref _mockGenSucceeded, value))
            {
                OnPropertyChanged(nameof(ShowOpenFolder));
            }
        }
    }

    /// <summary>「フォルダを開く」ボタンを表示するか（完了かつ成功で表示）</summary>
    public bool ShowOpenFolder => MockGenCompleted && MockGenSucceeded;

    /// <summary>第2ステップの入力欄（出力フォルダ等）を編集できるか（実行中は不可）</summary>
    public bool CanEditMockGenInput => !IsMockGenInProgress;

    /// <summary>
    /// WPF モック生成を開始できるか（確定 HTML あり・Claude Code バックエンド・claude CLI 検出・
    /// dotnet SDK 検出・出力フォルダ／プロジェクト名あり・非実行中）。
    /// </summary>
    public bool CanGenerateMockProject =>
        CanSaveHtml
        && Connection.SelectedBackend == ErChatBackendKind.ClaudeCode
        && IsClaudeCliAvailable
        && IsDotnetAvailable
        && !string.IsNullOrWhiteSpace(OutputFolder)
        && !string.IsNullOrWhiteSpace(ProjectName)
        && !IsMockGenInProgress
        && !IsTurnInProgress;

    /// <summary>WPF モック生成が無効な場合の理由（ツールチップ／案内文用）</summary>
    public string MockGenDisabledReason
    {
        get
        {
            if (IsMockGenInProgress)
            {
                return "生成を実行中です。";
            }

            if (!CanSaveHtml)
            {
                return "先にモック HTML を確定してください（プレビューに反映された状態が必要です）。";
            }

            if (Connection.SelectedBackend != ErChatBackendKind.ClaudeCode)
            {
                return "WPF モック生成はバックエンドが Claude Code のときのみ利用できます。";
            }

            if (!IsClaudeCliAvailable)
            {
                return "claude CLI が見つかりません。Claude Code をインストールし PATH を通してください。";
            }

            if (!IsDotnetAvailable)
            {
                return ".NET SDK（dotnet）が見つかりません。.NET SDK をインストールしてください。";
            }

            if (string.IsNullOrWhiteSpace(OutputFolder))
            {
                return "出力フォルダを選択してください。";
            }

            if (string.IsNullOrWhiteSpace(ProjectName))
            {
                return "プロジェクト名を入力してください。";
            }

            return string.Empty;
        }
    }

    // ── Codex 認証状態（子の状態タブとは別に、認証解決はダイアログ側プローブの責務） ──

    [ObservableProperty]
    private string _codexAccountSummary = "未接続";

    [ObservableProperty]
    private ConnectionHealth _codexStatusLevel = ConnectionHealth.Pending;

    private bool _codexReady;

    private bool _claudeCodeReady;

    /// <summary>送信待ち添付を束ねる共通 VM 部品（チップ列・可否・追加/削除）</summary>
    public AttachmentListViewModel Attachments { get; }

    /// <summary>本番構成（実クライアント・WPF ディスパッチャ）で生成する</summary>
    public MockGenerationDialogViewModel(IMockDiagramSource diagramSource)
        : this(
            diagramSource,
            new WpfUiDispatcher(),
            files: null,
            codexSettingsStore: null,
            apiKeyEngineFactory: null,
            codexEngineFactory: null,
            claudeCodeEngineFactory: null,
            mockProjectGenerator: null
        ) { }

    /// <summary>依存を注入して生成する（テスト用）</summary>
    /// <param name="diagramSource">生成対象の ER 図の供給元</param>
    /// <param name="dispatcher">UI スレッドへのマーシャリング</param>
    /// <param name="files">HTML 保存ダイアログの供給元</param>
    /// <param name="codexSettingsStore">Codex 設定ストア</param>
    /// <param name="apiKeyEngineFactory">API キーエンジンのファクトリ（プロファイル・ツールホスト受け取り）</param>
    /// <param name="codexEngineFactory">Codex エンジンのファクトリ</param>
    /// <param name="claudeCodeEngineFactory">Claude Code エンジンのファクトリ</param>
    /// <param name="mockProjectGenerator">WPF モックプロジェクト生成器（省略時は図の供給元のプロバイダから構築）</param>
    public MockGenerationDialogViewModel(
        IMockDiagramSource diagramSource,
        IUiDispatcher dispatcher,
        IFileDialogService? files,
        CodexAppServerSettingsStore? codexSettingsStore,
        Func<ErChatProfile, IErDiagramToolHost, IErChatEngine>? apiKeyEngineFactory,
        Func<ErChatProfile, IErDiagramToolHost, IErChatEngine>? codexEngineFactory,
        Func<ErChatProfile, IErDiagramToolHost, IErChatEngine>? claudeCodeEngineFactory,
        IMockProjectGenerator? mockProjectGenerator = null,
        ChatAttachmentFactory.ImageShrinker? imageShrinker = null,
        ChatUiSettingsStore? uiSettingsStore = null
    )
    {
        _diagramSource = diagramSource;
        _dispatcher = dispatcher;
        _files = files ?? new WpfFileDialogService();

        // 添付部品は本番では WPF の画像縮小を差し込む（テストでは注入された縮小・null）
        Attachments = new AttachmentListViewModel(
            reportStatus: message => StatusMessage = message,
            shrinker: imageShrinker ?? WpfImageShrinker.Shrink
        );
        _mockProjectGenerator =
            mockProjectGenerator ?? new MockProjectGenerator(diagramSource.Providers);

        // 接続方式タブの状態部品。エンジンのファクトリ既定ラムダより前に用意し、get-only プロパティを
        // ラムダから参照させる（PropertyChanged 購読と LoadSettings は下記の ctor 順序に従い後段で行う）。
        Connection = new ChatConnectionSettingsViewModel(
            "mock-generation-ui.json",
            codexSettingsStore,
            uiSettingsStore
        );

        _apiKeyEngineFactory =
            apiKeyEngineFactory ?? ((profile, toolHost) => BuildApiKeyEngine(profile, toolHost));
        _codexEngineFactory =
            codexEngineFactory
            ?? (
                (profile, toolHost) =>
                    new CodexChatEngine(new CodexAppServerClient(), toolHost, _dispatcher, profile)
                    {
                        ModelProvider = Connection.CodexModelProvider,
                        Model = Connection.CodexModel,
                    }
            );
        _claudeCodeEngineFactory =
            claudeCodeEngineFactory
            ?? (
                (profile, toolHost) =>
                    new ClaudeCodeChatEngine(
                        new ClaudeCodeProcessClient(),
                        toolHost,
                        _dispatcher,
                        profile
                    )
                    {
                        Model = Connection.ClaudeCodeModel,
                    }
            );

        // ctor 順序厳守: Connection 生成 → PropertyChanged 購読 → Connection.LoadSettings
        // （購読前にロードするとモデル同期・可否再評価が漏れる）
        Connection.PropertyChanged += OnConnectionPropertyChanged;
        Connection.LoadSettings();

        RefreshAttachmentSupport();
    }

    /// <summary>
    /// 選択中バックエンドに応じて添付部品の対応範囲を再評価する。
    /// API キー=プロバイダー依存・Codex=なし・Claude Code=全種別（エンジン生成前でも判定できるよう規則で解決する）。
    /// </summary>
    private void RefreshAttachmentSupport() =>
        Attachments.Support = Connection.SelectedBackend switch
        {
            ErChatBackendKind.ClaudeCode => AttachmentSupport.Images
                | AttachmentSupport.Pdf
                | AttachmentSupport.Text
                | AttachmentSupport.Binary,
            ErChatBackendKind.Codex => AttachmentSupport.None,
            _ => AttachmentSupportResolver.ForApiKeyProvider(Connection.ApiProvider),
        };

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

    /// <summary>ドロップされたファイル群を添付へ取り込む（非対応は VM がステータス通知する）</summary>
    /// <param name="paths">ドロップされたファイルパス群</param>
    public void AddDroppedFiles(IReadOnlyList<string> paths) => Attachments.AddFiles(paths);

    /// <summary>ダイアログ表示時に API キーを読み込む</summary>
    /// <remarks>設定・候補の読込は ctor で <see cref="ChatConnectionSettingsViewModel.LoadSettings"/> 済み。</remarks>
    public void Initialize()
    {
        // 現在のプロバイダーの保存済み API キーを読み直す（子側で _isInitializing 抑止）
        Connection.Initialize();
        NotifyReadinessChanged();

        // 第2ステップ（WPF モック生成）の有効条件（claude CLI・dotnet SDK 検出）を非同期に確認する
        _ = RefreshMockGenAvailabilityAsync();
    }

    /// <summary>本番の API キーエンジン（プロバイダ振り分けドライバ）を組み立てる</summary>
    private ChatTurnEngine BuildApiKeyEngine(ErChatProfile profile, IErDiagramToolHost toolHost) =>
        new(
            new ProviderRoutingTurnDriver(
                () => Connection.ApiProvider,
                new OpenAiTurnDriver(BuildOpenAiConnection, profile),
                new AnthropicChatTurnDriver(BuildAnthropicConnection, profile)
            ),
            toolHost,
            _dispatcher,
            () =>
                Connection.ApiProvider == AiProvider.Ollama
                || !string.IsNullOrWhiteSpace(Connection.ApiKey),
            profile,
            attachmentSupport: () =>
                AttachmentSupportResolver.ForApiKeyProvider(Connection.ApiProvider)
        );

    /// <summary>新しい会話を開始する（履歴クリア・新セッション用意・案内表示）</summary>
    /// <remarks>
    /// 選択中バックエンドで新しい <see cref="MockDesignSession"/> を用意し、旧セッションのイベント購読は解除する。
    /// 確定 HTML（<see cref="_lastHtml"/>）は保持したままにし、プレビュー・保存・第2ステップの有効性を失わない。
    /// </remarks>
    [RelayCommand(CanExecute = nameof(CanStartConversation))]
    private void StartConversation()
    {
        // 選択中バックエンドのエンジンを、モック生成プロファイル注入・セッション自身をツールホストにして生成する。
        // エンジン⇔セッションの相互依存は MockDesignSession のファクトリコンストラクタが解く。
        var factory = SelectedFactory();
        var session = new MockDesignSession(toolHost =>
            factory(MockDesignProfile.MockDesign, toolHost)
        );
        AttachSession(session);

        Messages.Clear();
        _currentAssistantMessage = null;
        _conversationStarted = true;
        _firstMessageSent = false;
        AddSystemMessage(
            "会話を開始しました。モックへの要望を入力して送信してください（例: シンプルな管理画面で）。"
        );
        SendMessageCommand.NotifyCanExecuteChanged();
    }

    /// <summary>ユーザー入力を 1 ターンとして送信する（初回はスキーマ添付＋要望、2 回目以降はフィードバック）</summary>
    [RelayCommand(CanExecute = nameof(CanSendMessage))]
    private async Task SendMessageAsync()
    {
        if (_session is null || string.IsNullOrWhiteSpace(UserInput))
        {
            return;
        }

        var message = UserInput.Trim();
        UserInput = string.Empty;

        // 送信の直前に添付を取り出し、ユーザー吹き出しへ「📎 name」の要約を載せる
        var attachments = Attachments.BuildAttachments();
        Messages.Add(
            new ErChatMessage
            {
                Role = ErChatMessageRole.User,
                Content = message,
                AttachmentSummary = Attachments.BuildSummary(),
            }
        );
        IsTurnInProgress = true;

        try
        {
            if (!_firstMessageSent)
            {
                // 初回送信はスキーマ自動添付＋要望で会話を開始する（添付をデザイン参考として同梱）
                _firstMessageSent = true;
                var diagram = _diagramSource.GetDiagram();
                await _session.StartAsync(diagram, message, attachments).ConfigureAwait(true);
            }
            else
            {
                // 2 回目以降は修正フィードバックとして送る（添付を同梱）
                await _session.SendFeedbackAsync(message, attachments).ConfigureAwait(true);
            }

            // 送信できたら添付をクリアする（メッセージ単位のライフサイクル）
            Attachments.Clear();
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
        if (_session is not null)
        {
            await _session.InterruptAsync().ConfigureAwait(true);
        }
    }

    /// <summary>現在のモック HTML をファイルへ保存する</summary>
    [RelayCommand(CanExecute = nameof(CanSaveHtml))]
    private void SaveHtml()
    {
        var html = _lastHtml;

        if (string.IsNullOrEmpty(html))
        {
            return;
        }

        var picked = _files.PickSaveFile(
            "HTML ファイル (*.html)|*.html",
            ".html",
            initialFileName: "mock.html"
        );

        if (picked is null)
        {
            return;
        }

        try
        {
            System.IO.File.WriteAllText(
                picked.Path,
                html,
                new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false)
            );
            StatusMessage = "HTML を保存しました。";
        }
        catch (Exception ex)
        {
            StatusMessage = $"HTML を保存できませんでした: {ex.Message}";
        }
    }

    // ── 第2ステップ: WPF モックプロジェクト生成 ──

    /// <summary>出力フォルダを選択する</summary>
    [RelayCommand]
    private void BrowseOutputFolder()
    {
        var picked = _files.PickFolder(
            "WPF モックプロジェクトの出力先フォルダを選択",
            OutputFolder
        );

        if (!string.IsNullOrWhiteSpace(picked))
        {
            OutputFolder = picked;
        }
    }

    /// <summary>スキャフォールド＋Claude Code で WPF モックプロジェクトを生成する</summary>
    [RelayCommand(CanExecute = nameof(CanGenerateMockProject))]
    private async Task GenerateMockProjectAsync()
    {
        var html = _lastHtml;

        if (string.IsNullOrEmpty(html))
        {
            return;
        }

        var outputFolder = OutputFolder.Trim();

        // 非空フォルダのときは上書きの確認案内をログへ出す（破壊的削除はしない。既存ファイルは温存）
        if (
            Directory.Exists(outputFolder)
            && Directory.EnumerateFileSystemEntries(outputFolder).Any()
        )
        {
            AppendMockGenLog(
                "※ 出力フォルダは空ではありません。既存ファイルは残したまま生成物を追加します。\n"
            );
        }

        var diagram = _diagramSource.GetDiagram();
        var projectName = ProjectName.Trim();

        MockGenLog = string.Empty;
        MockGenCompleted = false;
        MockGenSucceeded = false;
        IsMockGenInProgress = true;
        StatusMessage = "WPF モックプロジェクトを生成しています...";

        _mockGenCts = new CancellationTokenSource();

        try
        {
            var result = await _mockProjectGenerator
                .GenerateAsync(
                    diagram,
                    html,
                    outputFolder,
                    projectName,
                    Connection.ClaudeCodeModel,
                    delta => RunOnUi(() => AppendMockGenLog(delta)),
                    _mockGenCts.Token
                )
                .ConfigureAwait(true);

            MockGenSucceeded = result.Success;
            StatusMessage = result.Message;
        }
        catch (Exception ex)
        {
            MockGenSucceeded = false;
            AppendMockGenLog($"\n生成中にエラーが発生しました: {ex.Message}\n");
            StatusMessage = $"WPF モック生成に失敗しました: {ex.Message}";
        }
        finally
        {
            _mockGenCts?.Dispose();
            _mockGenCts = null;
            IsMockGenInProgress = false;
            MockGenCompleted = true;
        }
    }

    /// <summary>WPF モック生成の中断起点</summary>
    private CancellationTokenSource? _mockGenCts;

    /// <summary>実行中の WPF モック生成を中断する</summary>
    [RelayCommand(CanExecute = nameof(IsMockGenInProgress))]
    private async Task InterruptMockGenAsync()
    {
        _mockGenCts?.Cancel();
        await _mockProjectGenerator.InterruptAsync().ConfigureAwait(true);
    }

    /// <summary>出力フォルダをエクスプローラで開く</summary>
    [RelayCommand(CanExecute = nameof(ShowOpenFolder))]
    private void OpenOutputFolder()
    {
        var folder = OutputFolder.Trim();

        if (Directory.Exists(folder))
        {
            OpenFolder(folder);
        }
    }

    /// <summary>進捗ログへ追記する</summary>
    private void AppendMockGenLog(string text) => MockGenLog += text;

    /// <summary>claude CLI・dotnet SDK の検出状態を取得して第2ステップの有効条件へ反映する</summary>
    public async Task RefreshMockGenAvailabilityAsync()
    {
        IsClaudeCliAvailable = _mockProjectGenerator.IsClaudeAvailable();

        try
        {
            IsDotnetAvailable = await _mockProjectGenerator
                .IsDotnetAvailableAsync()
                .ConfigureAwait(true);
        }
        catch (Exception)
        {
            IsDotnetAvailable = false;
        }

        NotifyMockGenChanged();
    }

    /// <summary>第2ステップの可否・派生表示・コマンド可否をまとめて通知する</summary>
    private void NotifyMockGenChanged()
    {
        OnPropertyChanged(nameof(CanGenerateMockProject));
        OnPropertyChanged(nameof(MockGenDisabledReason));
        OnPropertyChanged(nameof(CanEditMockGenInput));
        GenerateMockProjectCommand.NotifyCanExecuteChanged();
        InterruptMockGenCommand.NotifyCanExecuteChanged();
    }

    /// <summary>選択中バックエンドのエンジンファクトリ（プロファイル・ツールホスト受け取り）を返す</summary>
    private Func<ErChatProfile, IErDiagramToolHost, IErChatEngine> SelectedFactory() =>
        Connection.SelectedBackend switch
        {
            ErChatBackendKind.Codex => _codexEngineFactory,
            ErChatBackendKind.ClaudeCode => _claudeCodeEngineFactory,
            _ => _apiKeyEngineFactory,
        };

    /// <summary>新しいセッションを結び付け、イベントを購読する（旧セッションは購読解除して破棄する）</summary>
    private void AttachSession(MockDesignSession session)
    {
        DetachSession();

        _session = session;
        session.AssistantDeltaReceived += OnAssistantDelta;
        session.HtmlUpdated += OnHtmlUpdated;
        session.TurnCompleted += OnTurnCompleted;
        session.StatusChanged += OnStatus;
        SaveHtmlCommand.NotifyCanExecuteChanged();
        SendMessageCommand.NotifyCanExecuteChanged();
    }

    /// <summary>現在のセッションのイベント購読を解除する（新セッションへの差し替え・会話リセット時に呼ぶ）</summary>
    private void DetachSession()
    {
        if (_session is null)
        {
            return;
        }

        _session.AssistantDeltaReceived -= OnAssistantDelta;
        _session.HtmlUpdated -= OnHtmlUpdated;
        _session.TurnCompleted -= OnTurnCompleted;
        _session.StatusChanged -= OnStatus;
        _session = null;
    }

    /// <summary>組み立て中のアシスタント吹き出し（差分追記先）</summary>
    private ErChatMessage? _currentAssistantMessage;

    private void OnAssistantDelta(object? sender, string delta) => RunOnUi(() => ApplyDelta(delta));

    private void OnHtmlUpdated(object? sender, MockHtmlUpdate update) =>
        RunOnUi(() => ApplyHtmlUpdated(update));

    private void OnTurnCompleted(object? sender, ErChatTurnResult result) =>
        RunOnUi(() => ApplyTurnCompleted(result));

    private void OnStatus(object? sender, string message) => RunOnUi(() => StatusMessage = message);

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

    /// <summary>提出された HTML をプレビュー通知し、チャットへ更新メモを表示、保存可否を更新する</summary>
    private void ApplyHtmlUpdated(MockHtmlUpdate update)
    {
        // 次のアシスタント発話は新しい吹き出しにする
        _currentAssistantMessage = null;

        // 確定 HTML は VM 側で保持する（「＋新しい会話」でセッションを破棄しても保存・第2ステップを維持するため）
        _lastHtml = update.Html;

        var note = string.IsNullOrWhiteSpace(update.RevisionNote)
            ? "モックを更新しました。"
            : $"更新: {update.RevisionNote}";
        AddSystemMessage(note);

        HtmlUpdated?.Invoke(this, update);
        SaveHtmlCommand.NotifyCanExecuteChanged();
        // 確定 HTML ができたので第2ステップ（WPF モック生成）の可否を更新する
        NotifyMockGenChanged();
    }

    /// <summary>ターン完了で進行状態を解除し、結果に応じてステータスを更新する</summary>
    private void ApplyTurnCompleted(ErChatTurnResult result)
    {
        IsTurnInProgress = false;
        _currentAssistantMessage = null;
        NotifyReadinessChanged();

        if (result.Success)
        {
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

    /// <summary>OpenAI 接続設定を現在の入力から組み立てる</summary>
    private OpenAiChatConnection BuildOpenAiConnection() =>
        new(
            Connection.ApiProvider,
            Connection.ApiKey,
            Connection.ApiModel,
            string.IsNullOrWhiteSpace(Connection.EndpointOverride)
                ? null
                : Connection.EndpointOverride
        );

    /// <summary>Anthropic (Claude) 接続設定を現在の入力から組み立てる</summary>
    private AnthropicChatConnection BuildAnthropicConnection() =>
        new(Connection.ApiKey, Connection.ApiModel);

    /// <summary>設定を保存する（ウィンドウ非表示化時などに外部から呼ぶ）</summary>
    /// <remarks>接続タブの状態保存は子（<see cref="ChatConnectionSettingsViewModel.SaveSettings"/>）へ委譲する。</remarks>
    public void SaveSettings() => Connection.SaveSettings();

    // ── 設定変更フック ──

    /// <summary>
    /// 接続方式タブ（子 VM）の変更を購読し、親の責務（会話リセット・添付範囲再評価・readiness 再評価）を反映する。
    /// </summary>
    /// <remarks>
    /// 子は partial フック完了後に PropertyChanged を発火するため、本ハンドラ実行時点で子の内部状態は整合済み。
    /// 子側の候補更新・Is* 通知・API キー永続化・キー読み直しは子が済ませており、ここでは親固有の処理のみを行う。
    /// </remarks>
    private void OnConnectionPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(ChatConnectionSettingsViewModel.SelectedBackend):
                // バックエンドを切り替えたら会話はリセットする（旧セッションのエンジンは選択前のバックエンドのため）。
                // 確定 HTML（_lastHtml）は保持し、プレビュー・保存・第2ステップの有効性は失わない。
                ResetConversation();
                // バックエンド切替で添付範囲を再評価する（非対応になったら添付部品側で Pending をクリア・通知する）
                RefreshAttachmentSupport();
                NotifyReadinessChanged();
                NotifyMockGenChanged();
                break;

            case nameof(ChatConnectionSettingsViewModel.ApiProvider):
                // API キー接続はプロバイダーで添付範囲が変わる（OpenAI=画像・Claude=画像＋PDF・Ollama=なし）
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
        }
    }

    /// <summary>会話を未開始状態へ戻す（セッション購読解除・履歴クリア・可否更新）。確定 HTML は保持する</summary>
    private void ResetConversation()
    {
        DetachSession();
        _conversationStarted = false;
        _firstMessageSent = false;
        _currentAssistantMessage = null;
        Messages.Clear();
        SendMessageCommand.NotifyCanExecuteChanged();
    }

    partial void OnUserInputChanged(string value) => SendMessageCommand.NotifyCanExecuteChanged();

    partial void OnOutputFolderChanged(string value) => NotifyMockGenChanged();

    partial void OnProjectNameChanged(string value) => NotifyMockGenChanged();

    partial void OnIsClaudeCliAvailableChanged(bool value) => NotifyMockGenChanged();

    partial void OnIsDotnetAvailableChanged(bool value) => NotifyMockGenChanged();

    /// <summary>Codex 接続状態を外部（ダイアログのコードビハインド）から反映する</summary>
    public void ApplyCodexReadiness(bool ready, string summary, ConnectionHealth level)
    {
        _codexReady = ready;
        CodexAccountSummary = summary;
        CodexStatusLevel = level;
        NotifyReadinessChanged();
    }

    /// <summary>Claude Code 接続状態を外部から反映する</summary>
    public void ApplyClaudeCodeReadiness(
        bool ready,
        string summary,
        ConnectionHealth level,
        string guidance
    )
    {
        _claudeCodeReady = ready;
        Connection.ClaudeCodeStatusSummary = summary;
        Connection.ClaudeCodeStatusLevel = level;
        Connection.ClaudeCodeGuidance = guidance;
        NotifyReadinessChanged();
    }

    /// <summary>会話開始・送信・保存の可否変更をまとめて通知する</summary>
    private void NotifyReadinessChanged()
    {
        OnPropertyChanged(nameof(IsBackendReady));
        OnPropertyChanged(nameof(IsDiagramEmpty));
        OnPropertyChanged(nameof(CanStartConversation));
        OnPropertyChanged(nameof(CanSendMessage));
        OnPropertyChanged(nameof(CanSaveHtml));
        StartConversationCommand.NotifyCanExecuteChanged();
        SendMessageCommand.NotifyCanExecuteChanged();
        SaveHtmlCommand.NotifyCanExecuteChanged();
        // 第2ステップの可否は確定 HTML の有無・接続状態にも依存するため合わせて更新する
        NotifyMockGenChanged();
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
}
