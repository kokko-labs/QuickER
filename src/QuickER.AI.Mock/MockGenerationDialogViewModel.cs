using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using QuickER.AI;
using QuickER.AI.Mock.Resources;
using QuickER.AI.UI;
using QuickER.Gui.Abstractions;
using QuickER.Gui.Common;
using QuickER.Model;

namespace QuickER.AI.Mock;

/// <summary>サイドバーに並べる 1 画面の項目（マニフェストの画面宣言のスナップショット）</summary>
/// <param name="File">画面ファイル名（フォルダ直下・<c>.html</c>）</param>
/// <param name="Name">画面の表示名</param>
/// <param name="Description">画面の役割説明</param>
public sealed record MockScreenListItem(string File, string Name, string Description)
{
    /// <summary>ListBox のツールチップに出す「ファイル名 + 説明」</summary>
    public string Tooltip =>
        string.IsNullOrWhiteSpace(Description) ? File : $"{File}\n{Description}";
}

/// <summary>プレビュー要求（表示する画面の実ファイルパスと、遷移を許可するルートフォルダ）</summary>
/// <param name="FilePath">表示する画面 HTML のフルパス</param>
/// <param name="Folder">画面間リンク遷移を許可するモックフォルダのフルパス</param>
public sealed record MockPreviewRequest(string FilePath, string Folder);

/// <summary>
/// AI モック生成ダイアログ（左チャット／右「画面一覧サイドバー＋プレビュー」）の ViewModel。
/// </summary>
/// <remarks>
/// 接続方式（API キー / Codex / Claude）の選択・接続状態は <see cref="AiChatDialogViewModel"/> と
/// 同じ構造を踏襲する。会話は「モックフォルダ」（<see cref="MockFolderStore"/>）に対して行い、画面ごとの HTML と
/// 共有 style.css を <see cref="MockFolderDesignSession"/> のツール（save_screen / save_stylesheet /
/// get_screen / remove_screen）で作成・更新する。プレビューはフォルダ内の実ファイルを直接表示し、
/// 単一 HTML 出力は <see cref="MockBundleExporter"/> で結合して書き出す。
/// </remarks>
public partial class MockGenerationDialogViewModel : ObservableObject
{
    private readonly IMockDiagramSource _diagramSource;
    private readonly IUiDispatcher _dispatcher;
    private readonly IFileDialogService _files;
    private readonly IDialogService _dialogs;
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
    private MockFolderDesignSession? _session;

    /// <summary>
    /// 現在のモックフォルダのストア（フォルダを開いた／作成したときに設定される）。
    /// 会話開始前でもサイドバー・プレビュー・単一 HTML 出力・第2ステップの土台になる。
    /// </summary>
    private MockFolderStore? _store;

    /// <summary>
    /// 現在のフォルダが「モックフォルダではない既存フォルダ」（HTML 等はあるが mock.json なし）か、
    /// または破損／新フォーマットで開けなかったか。true の間は会話開始を抑止する。
    /// </summary>
    private bool _folderBlocked;

    /// <summary>会話が開始済みか（「新しい会話」で true、バックエンド切替・フォルダ変更でリセット）</summary>
    private bool _conversationStarted;

    /// <summary>この会話で初回送信を済ませたか（初回=StartNew/Resume／2 回目以降=SendFeedback の分岐に使う）</summary>
    private bool _firstMessageSent;

    /// <summary>現在の会話が「再開」フローか（既存モックフォルダを開いて始めたか）</summary>
    private bool _resumeMode;

    /// <summary>プレビュー要求の再入抑止（プレビュー内リンク遷移→選択同期→再ナビゲートのループを断つ）</summary>
    private bool _suppressPreviewRequest;

    /// <summary>ブラウザで URL を開く処理（テスト時に差し替え可能）</summary>
    internal Action<string> OpenBrowser { get; set; } =
        url => Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });

    /// <summary>フォルダをエクスプローラで開く処理（テスト時に差し替え可能）</summary>
    internal Action<string> OpenFolder { get; set; } =
        path => Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });

    /// <summary>チャットメッセージ一覧</summary>
    public ObservableCollection<ErChatMessage> Messages { get; } = new();

    /// <summary>画面一覧サイドバーの項目（マニフェストから再構築する）</summary>
    public ObservableCollection<MockScreenListItem> Screens { get; } = new();

    /// <summary>プレビューへ「この画面をこのフォルダの許可のもとで表示せよ」と要求する（ダイアログが受けて Navigate）</summary>
    public event EventHandler<MockPreviewRequest>? PreviewRequested;

    /// <summary>プレビューを空表示へ戻すよう要求する（フォルダ未指定・全画面削除時）</summary>
    public event EventHandler? PreviewClearRequested;

    // ── 共通のチャット状態 ──

    [ObservableProperty]
    private string _userInput = string.Empty;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    /// <summary>モックフォルダのフルパス（会話開始前に指定する動線。開けば即サイドバー・プレビューへ反映）</summary>
    [ObservableProperty]
    private string _mockFolder = string.Empty;

    /// <summary>サイドバーで選択中の画面（選択でプレビューへ遷移する）</summary>
    [ObservableProperty]
    private MockScreenListItem? _selectedScreen;

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
                OnPropertyChanged(nameof(CanClear));
                ClearCommand.NotifyCanExecuteChanged();
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
            _ => IsApiKeyConnectionReady,
        };

    /// <summary>API キー接続が送信可能か（Ollama はキー不要・それ以外はキー入力が必須）</summary>
    private bool IsApiKeyConnectionReady =>
        Connection.ApiProvider == AiProvider.Ollama
        || !string.IsNullOrWhiteSpace(Connection.ApiKey);

    /// <summary>図が空（エンティティ 0）か（会話開始の可否に使う）</summary>
    public bool IsDiagramEmpty => _diagramSource.IsEmpty;

    /// <summary>
    /// 新しい会話を開始できるか（接続 OK・図が空でない・ターン非実行中・モックフォルダ指定あり・
    /// そのフォルダが会話に使える＝モックフォルダでない既存フォルダや破損でない）。
    /// </summary>
    public bool CanStartConversation =>
        IsBackendReady
        && !IsDiagramEmpty
        && !IsTurnInProgress
        && !string.IsNullOrWhiteSpace(MockFolder)
        && !_folderBlocked;

    /// <summary>メッセージを送信できるか（会話開始済み・接続 OK・入力あり・ターン非実行中）</summary>
    public bool CanSendMessage =>
        _conversationStarted
        && IsBackendReady
        && !IsTurnInProgress
        && !string.IsNullOrWhiteSpace(UserInput);

    /// <summary>入力欄を編集できるか（ターン実行中は禁止）</summary>
    public bool CanEditInput => !IsTurnInProgress;

    /// <summary>モックフォルダに画面が 1 つ以上あるか（単一 HTML 出力・第2ステップの前提）</summary>
    public bool HasScreens => _store is not null && Screens.Count > 0;

    /// <summary>単一 HTML を出力できるか（モックフォルダに画面が 1 つ以上あるか）</summary>
    public bool CanExportBundle => HasScreens;

    // ── 第2ステップ: モックプロジェクト生成（Claude Code / Codex / API キー） ──

    /// <summary>選択可能な生成ターゲット一覧（WPF / Blazor。生成器が公開する Targets）</summary>
    public IReadOnlyList<MockProjectTarget> MockProjectTargets { get; }

    /// <summary>選択中の生成ターゲット（既定は先頭＝Blazor Web App）</summary>
    [ObservableProperty]
    private MockProjectTarget _selectedMockProjectTarget;

    /// <summary>生成先の出力フォルダ</summary>
    [ObservableProperty]
    private string _outputFolder = string.Empty;

    /// <summary>プロジェクト名の既定値</summary>
    private const string DefaultProjectName = "MockApp";

    /// <summary>生成するプロジェクト名（既定は図名由来の PascalCase）</summary>
    [ObservableProperty]
    private string _projectName = DefaultProjectName;

    /// <summary>実装に対する追加指示（任意・複数行。選択中バックエンドへ渡すプロンプト末尾に連結される）</summary>
    [ObservableProperty]
    private string _mockGenInstructions = string.Empty;

    /// <summary>モックプロジェクト生成の進捗ログ（追記式・自動スクロール表示）</summary>
    [ObservableProperty]
    private string _mockGenLog = string.Empty;

    /// <summary>claude CLI が検出済みか（第2ステップの有効条件）</summary>
    [ObservableProperty]
    private bool _isClaudeCliAvailable;

    /// <summary>dotnet SDK が検出済みか（第2ステップの有効条件）</summary>
    [ObservableProperty]
    private bool _isDotnetAvailable;

    private bool _isMockGenInProgress;

    /// <summary>モックプロジェクト生成が実行中か（生成・中断ボタンの可否に連動）</summary>
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

    /// <summary>直近のモックプロジェクト生成が完了したか（成功・失敗を問わず。フォルダを開く／ログ案内の表示制御）</summary>
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

    /// <summary>直近のモックプロジェクト生成が成功したか（フォルダを開くボタンの表示制御）</summary>
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
    /// モックプロジェクト生成を開始できるか（画面あり・選択バックエンドが ready・dotnet SDK 検出・
    /// 出力フォルダ／プロジェクト名あり・非実行中）。3 バックエンド（Claude Code / Codex / API キー）すべて可。
    /// </summary>
    public bool CanGenerateMockProject =>
        HasScreens
        && IsSelectedBackendReadyForMockGen
        && IsDotnetAvailable
        && !string.IsNullOrWhiteSpace(OutputFolder)
        && !string.IsNullOrWhiteSpace(ProjectName)
        && !IsMockGenInProgress
        && !IsTurnInProgress;

    /// <summary>選択中バックエンドが API キー方式か（一発生成の注記表示に使う）</summary>
    public bool IsApiKeyMockGenBackend => Connection.SelectedBackend == ErChatBackendKind.ApiKey;

    /// <summary>
    /// 選択中バックエンドがモックプロジェクト生成の実行条件（CLI 検出／認証／API キー）を満たすか。
    /// Claude Code＝claude CLI 検出・Codex＝認証プローブ結果（<see cref="ApplyCodexReadiness"/>）・
    /// API キー＝キー入力あり（Ollama はキー不要）。
    /// </summary>
    private bool IsSelectedBackendReadyForMockGen =>
        Connection.SelectedBackend switch
        {
            ErChatBackendKind.ClaudeCode => IsClaudeCliAvailable,
            ErChatBackendKind.Codex => _codexReady,
            _ => IsApiKeyConnectionReady,
        };

    /// <summary>モックプロジェクト生成が無効な場合の理由（ツールチップ／案内文用）</summary>
    public string MockGenDisabledReason
    {
        get
        {
            if (IsMockGenInProgress)
            {
                return Strings.Mock_DisabledReason_InProgress;
            }

            if (!HasScreens)
            {
                return Strings.Mock_DisabledReason_NoScreens;
            }

            if (Connection.SelectedBackend == ErChatBackendKind.ClaudeCode && !IsClaudeCliAvailable)
            {
                return Strings.Mock_DisabledReason_ClaudeCli;
            }

            if (Connection.SelectedBackend == ErChatBackendKind.Codex && !_codexReady)
            {
                return Strings.Mock_DisabledReason_CodexNotReady;
            }

            if (Connection.SelectedBackend == ErChatBackendKind.ApiKey && !IsApiKeyConnectionReady)
            {
                return Strings.Mock_DisabledReason_ApiKeyNotReady;
            }

            if (!IsDotnetAvailable)
            {
                return Strings.Mock_DisabledReason_Dotnet;
            }

            if (string.IsNullOrWhiteSpace(OutputFolder))
            {
                return Strings.Mock_DisabledReason_OutputFolder;
            }

            if (string.IsNullOrWhiteSpace(ProjectName))
            {
                return Strings.Mock_DisabledReason_ProjectName;
            }

            return string.Empty;
        }
    }

    // ── Codex 認証状態（子の状態タブとは別に、認証解決はダイアログ側プローブの責務） ──

    [ObservableProperty]
    private string _codexAccountSummary = Strings.Mock_CodexNotConnected;

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
            settingsStore: null,
            apiKeyEngineFactory: null,
            codexEngineFactory: null,
            claudeCodeEngineFactory: null,
            mockProjectGenerator: null
        ) { }

    /// <summary>依存を注入して生成する（テスト用）</summary>
    /// <param name="diagramSource">生成対象の ER 図の供給元</param>
    /// <param name="dispatcher">UI スレッドへのマーシャリング</param>
    /// <param name="files">フォルダ選択・HTML 出力ダイアログの供給元</param>
    /// <param name="settingsStore">AI 設定ストア（Codex / Claude Code / UI 状態 / モデル履歴を集約）</param>
    /// <param name="apiKeyEngineFactory">API キーエンジンのファクトリ（プロファイル・ツールホスト受け取り）</param>
    /// <param name="codexEngineFactory">Codex エンジンのファクトリ</param>
    /// <param name="claudeCodeEngineFactory">Claude Code エンジンのファクトリ</param>
    /// <param name="mockProjectGenerator">モックプロジェクト生成器（省略時は図の供給元のプロバイダから構築）</param>
    /// <param name="dialogService">確認・通知ダイアログ（省略時は MessageBox 実装）</param>
    public MockGenerationDialogViewModel(
        IMockDiagramSource diagramSource,
        IUiDispatcher dispatcher,
        IFileDialogService? files,
        AiSettingsStore? settingsStore,
        Func<ErChatProfile, IErDiagramToolHost, IErChatEngine>? apiKeyEngineFactory,
        Func<ErChatProfile, IErDiagramToolHost, IErChatEngine>? codexEngineFactory,
        Func<ErChatProfile, IErDiagramToolHost, IErChatEngine>? claudeCodeEngineFactory,
        IMockProjectGenerator? mockProjectGenerator = null,
        ChatAttachmentFactory.ImageShrinker? imageShrinker = null,
        IDialogService? dialogService = null
    )
    {
        _diagramSource = diagramSource;
        _dispatcher = dispatcher;
        _files = files ?? new WpfFileDialogService();
        _dialogs = dialogService ?? new MessageBoxDialogService();

        // 添付部品は本番では WPF の画像縮小を差し込む（テストでは注入された縮小・null）
        Attachments = new AttachmentListViewModel(
            reportStatus: message => StatusMessage = message,
            shrinker: imageShrinker ?? WpfImageShrinker.Shrink
        );
        // 接続方式タブの状態部品。エンジンのファクトリ既定ラムダより前に用意し、get-only プロパティを
        // ラムダから参照させる（PropertyChanged 購読と LoadSettings は下記の ctor 順序に従い後段で行う）。
        Connection = new ChatConnectionSettingsViewModel(
            AiDialogKind.MockGeneration,
            settingsStore
        );

        _apiKeyEngineFactory =
            apiKeyEngineFactory ?? ((profile, toolHost) => BuildApiKeyEngine(profile, toolHost));

        // API キー方式のモックプロジェクト生成は、チャットと同じ API キーエンジンを使う。生成時点の Connection 状態
        // （モデル・キー・エンドポイント）を閉包へ閉じ込めるため、確定済みの _apiKeyEngineFactory を渡す
        // （実行は生成時＝Connection ロード後）。
        _mockProjectGenerator =
            mockProjectGenerator
            ?? new MockProjectGenerator(diagramSource.Providers, _apiKeyEngineFactory);
        // 生成ターゲット一覧を取り込み、既定を先頭（Blazor Web App）にする（第 2 ステップの ComboBox が選ばせる）
        MockProjectTargets = _mockProjectGenerator.Targets;
        _selectedMockProjectTarget = MockProjectTargets[0];
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

        // 第2ステップ（モックプロジェクト生成）の有効条件（claude CLI・dotnet SDK 検出）を非同期に確認する
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

    // ── モックフォルダ ──

    /// <summary>モックフォルダを選択する</summary>
    [RelayCommand]
    private void BrowseMockFolder()
    {
        var picked = _files.PickFolder(Strings.Mock_PickMockFolderTitle, MockFolder);

        if (!string.IsNullOrWhiteSpace(picked))
        {
            MockFolder = picked;
        }
    }

    /// <summary>
    /// モックフォルダの指定が変わったら、会話をリセットし、フォルダの種別を判定して
    /// サイドバー・プレビューへ即反映する（既存モックフォルダなら開いて眺められる）。
    /// </summary>
    partial void OnMockFolderChanged(string value)
    {
        // フォルダを変えたら会話・状態は仕切り直す
        ResetConversation();
        _store = null;
        _folderBlocked = false;
        Screens.Clear();
        SelectedScreen = null;
        PreviewClearRequested?.Invoke(this, EventArgs.Empty);

        var folder = value?.Trim() ?? string.Empty;

        if (string.IsNullOrEmpty(folder))
        {
            NotifyFolderStateChanged();
            return;
        }

        if (MockFolderStore.IsMockFolder(folder))
        {
            // 既存モックフォルダ: 開いて即サイドバー・プレビューへ反映する
            try
            {
                _store = MockFolderStore.Open(folder);
            }
            catch (Exception ex)
            {
                // 破損／新フォーマットはメッセージをそのまま出し、会話開始を抑止する
                _store = null;
                _folderBlocked = true;
                StatusMessage = string.Format(Strings.Mock_MockFolderOpenFailedFormat, ex.Message);
                NotifyFolderStateChanged();
                return;
            }

            RebuildScreens();

            if (Screens.Count > 0)
            {
                // 先頭画面を選択するとプレビュー要求が飛ぶ
                SelectedScreen = Screens[0];
            }
        }
        else if (Directory.Exists(folder) && Directory.EnumerateFileSystemEntries(folder).Any())
        {
            // HTML 等はあるが mock.json が無い既存フォルダ: モックフォルダではないので開始を抑止する
            _folderBlocked = true;
            StatusMessage = Strings.Mock_NotAMockFolder;
        }

        // 空フォルダ・未存在フォルダは新規として扱う（会話開始時に CreateNew する）

        NotifyFolderStateChanged();
    }

    /// <summary>フォルダ種別が変わったときの可否・派生表示をまとめて通知する</summary>
    private void NotifyFolderStateChanged()
    {
        OnPropertyChanged(nameof(HasScreens));
        OnPropertyChanged(nameof(CanExportBundle));
        ExportBundleCommand.NotifyCanExecuteChanged();
        ExportDesignDocCommand.NotifyCanExecuteChanged();
        NotifyReadinessChanged();
        NotifyMockGenChanged();
    }

    /// <summary>マニフェスト宣言の画面群からサイドバー項目を再構築する</summary>
    private void RebuildScreens()
    {
        Screens.Clear();

        if (_store is not null)
        {
            foreach (var screen in _store.Manifest.Screens)
            {
                Screens.Add(new MockScreenListItem(screen.File, screen.Name, screen.Description));
            }
        }

        OnPropertyChanged(nameof(HasScreens));
        OnPropertyChanged(nameof(CanExportBundle));
        ExportBundleCommand.NotifyCanExecuteChanged();
        ExportDesignDocCommand.NotifyCanExecuteChanged();
        NotifyMockGenChanged();
    }

    /// <summary>指定ファイルの画面をサイドバーで選択する（見つかればプレビュー要求が飛ぶ）</summary>
    private void SelectScreenByFile(string file)
    {
        var item = Screens.FirstOrDefault(s =>
            string.Equals(s.File, file, StringComparison.OrdinalIgnoreCase)
        );

        if (item is not null)
        {
            SelectedScreen = item;
        }
    }

    /// <summary>プレビューへ「この画面を表示せよ」と要求する（許可フォルダ付き）</summary>
    private void RaisePreview(string file)
    {
        if (_store is null || string.IsNullOrWhiteSpace(file))
        {
            return;
        }

        var path = Path.Combine(_store.Folder, file);
        PreviewRequested?.Invoke(this, new MockPreviewRequest(path, _store.Folder));
    }

    /// <summary>サイドバーの選択が変わったらプレビューへ反映する（再入時は抑止する）</summary>
    partial void OnSelectedScreenChanged(MockScreenListItem? value)
    {
        if (_suppressPreviewRequest)
        {
            return;
        }

        if (value is not null)
        {
            RaisePreview(value.File);
        }
    }

    /// <summary>
    /// プレビュー内リンクで別画面へ遷移したとき、サイドバーの選択を追従させる（プレビュー再要求は起こさない）。
    /// </summary>
    /// <param name="file">プレビューが現在表示している画面ファイル名</param>
    public void SyncSelectionFromPreview(string file)
    {
        var item = Screens.FirstOrDefault(s =>
            string.Equals(s.File, file, StringComparison.OrdinalIgnoreCase)
        );

        if (item is null || ReferenceEquals(item, SelectedScreen))
        {
            return;
        }

        _suppressPreviewRequest = true;

        try
        {
            SelectedScreen = item;
        }
        finally
        {
            _suppressPreviewRequest = false;
        }
    }

    /// <summary>新しい会話を開始する（フォルダを確定・新セッション用意・案内表示）</summary>
    /// <remarks>
    /// 既存モックフォルダを開いていれば再開フロー、空／未存在フォルダなら新規作成フローで始める。
    /// 画面・サイドバー・プレビューはフォルダのライブ状態に紐づくため、会話をリセットしても維持される。
    /// </remarks>
    [RelayCommand(CanExecute = nameof(CanStartConversation))]
    private void StartConversation()
    {
        // 会話開始時にフォルダをストアとして確定する
        if (_store is null)
        {
            // 空／未存在フォルダ: 新規モックフォルダとして作成する
            var folder = MockFolder.Trim();

            try
            {
                _store = MockFolderStore.CreateNew(
                    folder,
                    DeriveTitle(folder),
                    sourceSchema: string.Empty
                );
            }
            catch (Exception ex)
            {
                StatusMessage = string.Format(Strings.Mock_MockFolderOpenFailedFormat, ex.Message);
                return;
            }

            _resumeMode = false;
            RebuildScreens();
        }
        else
        {
            // 既存モックフォルダ: 再開フロー
            _resumeMode = true;
        }

        // 選択中バックエンドのエンジンを、モックフォルダ方式プロファイル注入・セッション自身をツールホストにして生成する。
        // エンジン⇔セッションの相互依存は MockFolderDesignSession のファクトリコンストラクタが解く。
        var factory = SelectedFactory();
        var session = new MockFolderDesignSession(
            toolHost => factory(MockDesignProfile.FolderMockDesign, toolHost),
            _store
        );
        AttachSession(session);

        Messages.Clear();
        _currentAssistantMessage = null;
        _conversationStarted = true;
        _firstMessageSent = false;
        AddSystemMessage(Strings.Mock_ConversationStarted);
        SendMessageCommand.NotifyCanExecuteChanged();
    }

    /// <summary>フォルダ名からモックの表題を導く（末尾区切りを除いたフォルダ名・空なら "Mock"）</summary>
    private static string DeriveTitle(string folder)
    {
        var trimmed = folder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var name = Path.GetFileName(trimmed);

        return string.IsNullOrWhiteSpace(name) ? "Mock" : name;
    }

    /// <summary>ユーザー入力を 1 ターンとして送信する（初回はスキーマ／再開情報、2 回目以降はフィードバック）</summary>
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
                // 初回送信は新規（スキーマ）／再開（画面一覧＋差異）のいずれかで会話を開始する
                _firstMessageSent = true;
                var diagram = _diagramSource.GetDiagram();

                if (_resumeMode)
                {
                    await _session
                        .StartResumeAsync(diagram, message, attachments)
                        .ConfigureAwait(true);
                }
                else
                {
                    await _session
                        .StartNewAsync(diagram, message, attachments)
                        .ConfigureAwait(true);
                }
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
            StatusMessage = string.Format(Strings.Mock_SendFailedFormat, ex.Message);
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

    /// <summary>モックフォルダの内容を単一 HTML へ結合してファイルへ書き出す</summary>
    [RelayCommand(CanExecute = nameof(CanExportBundle))]
    private void ExportBundle()
    {
        if (_store is null || Screens.Count == 0)
        {
            return;
        }

        var html = MockBundleExporter.Export(_store);

        var picked = _files.PickSaveFile(
            Strings.Mock_HtmlFileFilter,
            ".html",
            initialFileName: "mock.html"
        );

        if (picked is null)
        {
            return;
        }

        try
        {
            File.WriteAllText(
                picked.Path,
                html,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)
            );
            StatusMessage = Strings.Mock_HtmlSaved;
            // 保存先パス付きの完了メッセージを明示する（ステータスバーだけでは見落としやすいため）
            _dialogs.ShowInformation(
                string.Format(Strings.Mock_HtmlSavedMessageFormat, picked.Path),
                Strings.Mock_WindowTitle
            );
        }
        catch (Exception ex)
        {
            StatusMessage = string.Format(Strings.Mock_HtmlSaveFailedFormat, ex.Message);
        }
    }

    /// <summary>
    /// モックフォルダの内容から画面設計書（README.md）を決定的に生成し、フォルダ直下へ書き出す（初回オプトイン）。
    /// </summary>
    /// <remarks>
    /// 生成は <see cref="MockDesignDocExporter"/>（AI 不使用・決定的）。一度書き出せば以降は画面の保存・削除に
    /// <see cref="RegenerateDesignDocIfPresent"/> が自動追従する。BOM なし UTF-8・改行はエクスポータ既定（LF）。
    /// </remarks>
    [RelayCommand(CanExecute = nameof(CanExportBundle))]
    private void ExportDesignDoc()
    {
        if (_store is null || Screens.Count == 0)
        {
            return;
        }

        var markdown = MockDesignDocExporter.Export(_store);
        var path = Path.Combine(_store.Folder, MockDesignDocExporter.FileName);

        try
        {
            File.WriteAllText(
                path,
                markdown,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)
            );
            StatusMessage = Strings.Mock_DesignDocSaved;
            // 保存先パス付きの完了メッセージを明示する（ステータスバーだけでは見落としやすいため）
            _dialogs.ShowInformation(
                string.Format(Strings.Mock_DesignDocSavedMessageFormat, path),
                Strings.Mock_WindowTitle
            );
        }
        catch (Exception ex)
        {
            StatusMessage = string.Format(Strings.Mock_DesignDocSaveFailedFormat, ex.Message);
        }
    }

    /// <summary>
    /// モックフォルダ直下に設計書（README.md）が既にあれば、現在の内容で無音再生成して上書きする。
    /// </summary>
    /// <remarks>
    /// 設計書出力ボタンで一度書き出したフォルダにだけ追従する（README.md が無いフォルダには何も書かない＝オプトイン維持）。
    /// 画面の保存・削除で呼ぶ（共有 CSS 保存・スキーマ更新は設計書の内容に影響しないため呼ばない）。
    /// 成功時は無音（ステータス・ダイアログを出さない）・書き込み失敗のみステータスへ通知する。
    /// </remarks>
    private void RegenerateDesignDocIfPresent()
    {
        if (_store is null)
        {
            return;
        }

        var path = Path.Combine(_store.Folder, MockDesignDocExporter.FileName);

        if (!File.Exists(path))
        {
            return;
        }

        try
        {
            var markdown = MockDesignDocExporter.Export(_store);
            File.WriteAllText(
                path,
                markdown,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)
            );
        }
        catch (Exception ex)
        {
            StatusMessage = string.Format(Strings.Mock_DesignDocSaveFailedFormat, ex.Message);
        }
    }

    /// <summary>画面全体を初期状態へ戻せるか（モックフォルダ未選択・ターン／モックプロジェクト生成の実行中は不可）</summary>
    public bool CanClear =>
        !string.IsNullOrWhiteSpace(MockFolder) && !IsTurnInProgress && !IsMockGenInProgress;

    /// <summary>
    /// 画面全体を初期状態へ戻す（確認後）。会話・入力・添付・モックフォルダの選択・第 2 ステップの入力と
    /// 結果表示をクリアする。ディスク上のモックフォルダには触れない。
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanClear))]
    private void Clear()
    {
        if (!_dialogs.Confirm(Strings.Mock_ClearConfirm, Strings.Mock_WindowTitle))
        {
            return;
        }

        // 会話・入力・添付
        ResetConversation();
        UserInput = string.Empty;
        Attachments.Clear();

        // モックフォルダの選択解除（OnMockFolderChanged がストア・サイドバー・プレビューも初期化する。
        // 既に空なら関連状態も初期状態のため変更通知は不要）
        MockFolder = string.Empty;

        // 第 2 ステップの入力と結果表示
        SelectedMockProjectTarget = MockProjectTargets[0];
        OutputFolder = string.Empty;
        ProjectName = DefaultProjectName;
        MockGenInstructions = string.Empty;
        MockGenLog = string.Empty;
        MockGenCompleted = false;
        MockGenSucceeded = false;

        StatusMessage = string.Empty;
    }

    // ── 第2ステップ: モックプロジェクト生成 ──

    /// <summary>出力フォルダを選択する</summary>
    [RelayCommand]
    private void BrowseOutputFolder()
    {
        var picked = _files.PickFolder(Strings.Mock_PickOutputFolderTitle, OutputFolder);

        if (!string.IsNullOrWhiteSpace(picked))
        {
            OutputFolder = picked;
        }
    }

    /// <summary>スキャフォールド＋選択バックエンド（Claude Code / Codex / API キー）でモックプロジェクトを生成する</summary>
    [RelayCommand(CanExecute = nameof(CanGenerateMockProject))]
    private async Task GenerateMockProjectAsync()
    {
        if (_store is null || Screens.Count == 0)
        {
            return;
        }

        // デザイン仕様はモックフォルダそのものを同梱する（スキャフォールドが design/mock/ へコピーする）
        var mockFolder = _store.Folder;
        var instructions = string.IsNullOrWhiteSpace(MockGenInstructions)
            ? null
            : MockGenInstructions.Trim();

        var outputFolder = OutputFolder.Trim();

        // 非空フォルダのときは上書きの確認案内をログへ出す（破壊的削除はしない。既存ファイルは温存）
        if (
            Directory.Exists(outputFolder)
            && Directory.EnumerateFileSystemEntries(outputFolder).Any()
        )
        {
            AppendMockGenLog(Strings.Mock_OutputFolderNotEmpty);
        }

        var diagram = _diagramSource.GetDiagram();
        var projectName = ProjectName.Trim();

        // 選択バックエンド別にモデル・プロバイダーを選ぶ（Codex のみプロバイダーを渡す。
        // API キーはエンジンファクトリがモデル・キーを閉じ込めるため、ここで渡す値はログ表示用）
        var backend = Connection.SelectedBackend;
        var model = backend switch
        {
            ErChatBackendKind.Codex => Connection.CodexModel,
            ErChatBackendKind.ClaudeCode => Connection.ClaudeCodeModel,
            _ => Connection.ApiModel,
        };
        var modelProvider =
            backend == ErChatBackendKind.Codex ? Connection.CodexModelProvider : string.Empty;

        MockGenLog = string.Empty;
        MockGenCompleted = false;
        MockGenSucceeded = false;
        IsMockGenInProgress = true;
        StatusMessage = Strings.Mock_GeneratingProject;

        _mockGenCts = new CancellationTokenSource();

        try
        {
            var result = await _mockProjectGenerator
                .GenerateAsync(
                    diagram,
                    mockFolder,
                    instructions,
                    outputFolder,
                    projectName,
                    // 第 2 ステップの ComboBox で選択中の生成ターゲット（WPF / Blazor）
                    SelectedMockProjectTarget,
                    backend,
                    model,
                    modelProvider,
                    delta => RunOnUi(() => AppendMockGenLog(delta)),
                    _mockGenCts.Token
                )
                .ConfigureAwait(true);

            MockGenSucceeded = result.Success;
            StatusMessage = result.Message;

            // 完了を明示する（ステータスバーだけでは見落としやすいため）。
            // ただしユーザー自身の中断による終了時はダイアログを出さない。
            if (!result.Interrupted)
            {
                if (result.Success)
                {
                    _dialogs.ShowInformation(
                        string.Format(
                            Strings.Mock_GenResultSuccessBodyFormat,
                            result.Message,
                            result.OutputDirectory
                        ),
                        Strings.Mock_WindowTitle
                    );
                }
                else
                {
                    // 失敗はログパス（無ければ出力フォルダ）を添えて詳細確認へ誘導する
                    _dialogs.ShowError(
                        string.Format(
                            Strings.Mock_GenResultFailureBodyFormat,
                            result.Message,
                            string.IsNullOrWhiteSpace(result.LogPath)
                                ? result.OutputDirectory
                                : result.LogPath
                        ),
                        Strings.Mock_WindowTitle
                    );
                }
            }
        }
        catch (Exception ex)
        {
            MockGenSucceeded = false;
            AppendMockGenLog(string.Format(Strings.Mock_GenerationErrorLogFormat, ex.Message));
            StatusMessage = string.Format(Strings.Mock_GenerationFailedFormat, ex.Message);
        }
        finally
        {
            _mockGenCts?.Dispose();
            _mockGenCts = null;
            IsMockGenInProgress = false;
            MockGenCompleted = true;
        }
    }

    /// <summary>モックプロジェクト生成の中断起点</summary>
    private CancellationTokenSource? _mockGenCts;

    /// <summary>実行中のモックプロジェクト生成を中断する</summary>
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
        IsClaudeCliAvailable = _mockProjectGenerator.IsAgentAvailable(ErChatBackendKind.ClaudeCode);

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
        OnPropertyChanged(nameof(IsApiKeyMockGenBackend));
        OnPropertyChanged(nameof(CanEditMockGenInput));
        OnPropertyChanged(nameof(CanClear));
        GenerateMockProjectCommand.NotifyCanExecuteChanged();
        InterruptMockGenCommand.NotifyCanExecuteChanged();
        ClearCommand.NotifyCanExecuteChanged();
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
    private void AttachSession(MockFolderDesignSession session)
    {
        DetachSession();

        _session = session;
        session.AssistantDeltaReceived += OnAssistantDelta;
        session.ScreenSaved += OnScreenSaved;
        session.ScreenRemoved += OnScreenRemoved;
        session.StylesheetSaved += OnStylesheetSaved;
        session.TurnCompleted += OnTurnCompleted;
        session.StatusChanged += OnStatus;
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
        _session.ScreenSaved -= OnScreenSaved;
        _session.ScreenRemoved -= OnScreenRemoved;
        _session.StylesheetSaved -= OnStylesheetSaved;
        _session.TurnCompleted -= OnTurnCompleted;
        _session.StatusChanged -= OnStatus;
        _session = null;
    }

    /// <summary>組み立て中のアシスタント吹き出し（差分追記先）</summary>
    private ErChatMessage? _currentAssistantMessage;

    private void OnAssistantDelta(object? sender, string delta) => RunOnUi(() => ApplyDelta(delta));

    private void OnScreenSaved(object? sender, MockScreenSavedEventArgs e) =>
        RunOnUi(() => ApplyScreenSaved(e));

    private void OnScreenRemoved(object? sender, string file) =>
        RunOnUi(() => ApplyScreenRemoved(file));

    private void OnStylesheetSaved(object? sender, MockStylesheetSavedEventArgs e) =>
        RunOnUi(() => ApplyStylesheetSaved(e));

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

    /// <summary>画面保存で、サイドバー再読込＋その画面をプレビュー表示し、変更点・警告をチャットへ通知する</summary>
    private void ApplyScreenSaved(MockScreenSavedEventArgs e)
    {
        // 次のアシスタント発話は新しい吹き出しにする
        _currentAssistantMessage = null;

        RebuildScreens();
        // 保存された画面を選択するとプレビュー要求が飛ぶ（同名再保存でも新インスタンスなので反映される）
        SelectScreenByFile(e.File);

        // 設計書（README.md）を出力済みなら、画面変更に無音で追従して再生成する
        RegenerateDesignDocIfPresent();

        AddSystemMessage(BuildScreenSavedNote(e.RevisionNote, e.Warnings));
    }

    /// <summary>画面保存のシステムメッセージ（変更点＋警告）を組み立てる</summary>
    private static string BuildScreenSavedNote(string revisionNote, IReadOnlyList<string> warnings)
    {
        var note = string.IsNullOrWhiteSpace(revisionNote)
            ? Strings.Mock_MockUpdated
            : string.Format(Strings.Mock_UpdateNoteFormat, revisionNote);

        if (warnings.Count > 0)
        {
            note += "\n" + Strings.Mock_WarningsHeading + " " + string.Join("; ", warnings);
        }

        return note;
    }

    /// <summary>画面削除で、サイドバー再読込・表示中画面が消えたら先頭画面（なければ空）へ切り替える</summary>
    private void ApplyScreenRemoved(string file)
    {
        _currentAssistantMessage = null;

        var wasShowingRemoved =
            SelectedScreen is not null
            && string.Equals(SelectedScreen.File, file, StringComparison.OrdinalIgnoreCase);

        RebuildScreens();

        // 削除で選択が実体を失ったら先頭画面（なければ空表示）へ
        if (wasShowingRemoved || SelectedScreen is null || !Screens.Contains(SelectedScreen))
        {
            if (Screens.Count > 0)
            {
                SelectedScreen = Screens[0];
            }
            else
            {
                SelectedScreen = null;
                PreviewClearRequested?.Invoke(this, EventArgs.Empty);
            }
        }

        // 設計書（README.md）を出力済みなら、画面削除に無音で追従して再生成する
        RegenerateDesignDocIfPresent();

        AddSystemMessage(string.Format(Strings.Mock_ScreenRemovedResult, file));
    }

    /// <summary>共有スタイルシート保存で、表示中の画面をリロードする（CSS 変更を反映）</summary>
    private void ApplyStylesheetSaved(MockStylesheetSavedEventArgs e)
    {
        _currentAssistantMessage = null;

        // 表示中の画面を同一ファイルとして再要求する（プレビューは Reload で最新化する）
        if (SelectedScreen is not null)
        {
            RaisePreview(SelectedScreen.File);
        }

        AddSystemMessage(BuildScreenSavedNote(e.RevisionNote, e.Warnings));
    }

    /// <summary>ターン完了で進行状態を解除し、結果に応じてステータスを更新する</summary>
    private void ApplyTurnCompleted(ErChatTurnResult result)
    {
        IsTurnInProgress = false;
        _currentAssistantMessage = null;
        NotifyReadinessChanged();

        if (result.Success)
        {
            // 成功したターンで使ったモデルを MRU 履歴へ記録する（Ollama / Codex のガードは子 VM 側）
            Connection.RecordSuccessfulModel();
            StatusMessage = Strings.Mock_ResponseCompleted;
        }
        else if (!string.IsNullOrWhiteSpace(result.Error))
        {
            AddSystemMessage(string.Format(Strings.Mock_ErrorSystemFormat, result.Error));
            StatusMessage = string.Format(Strings.Mock_ErrorStatusFormat, result.Error);
        }
        else
        {
            StatusMessage = Strings.Mock_Interrupted;
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
                // モックフォルダ・サイドバー・プレビューは維持し、単一 HTML 出力・第2ステップの有効性は失わない。
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

    /// <summary>会話を未開始状態へ戻す（セッション購読解除・履歴クリア・可否更新）。フォルダ・画面・プレビューは保持する</summary>
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

    /// <summary>会話開始・送信・出力の可否変更をまとめて通知する</summary>
    private void NotifyReadinessChanged()
    {
        OnPropertyChanged(nameof(IsBackendReady));
        OnPropertyChanged(nameof(IsDiagramEmpty));
        OnPropertyChanged(nameof(CanStartConversation));
        OnPropertyChanged(nameof(CanSendMessage));
        OnPropertyChanged(nameof(CanExportBundle));
        StartConversationCommand.NotifyCanExecuteChanged();
        SendMessageCommand.NotifyCanExecuteChanged();
        ExportBundleCommand.NotifyCanExecuteChanged();
        ExportDesignDocCommand.NotifyCanExecuteChanged();
        // 第2ステップの可否は画面の有無・接続状態にも依存するため合わせて更新する
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
