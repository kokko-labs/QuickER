using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
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
using QuickER.Platform;

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
/// 接続方式（API キー / Codex / Claude / Copilot）の選択・接続状態は <see cref="AiChatDialogViewModel"/> と
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
    private readonly Func<ErChatProfile, IErDiagramToolHost, IErChatEngine> _copilotEngineFactory;

    /// <summary>接続方式タブ（API キー / Codex / Claude Code / Copilot）の状態と永続化を束ねる共通 VM 部品</summary>
    public ChatConnectionSettingsViewModel Connection { get; }

    /// <summary>現在の生成セッション（会話開始前は null）</summary>
    private MockFolderDesignSession? _session;

    /// <summary>現在のセッションが抱えるエンジン（モデルの変更を流すために控える。セッションと同時に消える）</summary>
    private IErChatEngine? _sessionEngine;

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

    /// <summary>フォルダをエクスプローラで開く処理（テスト時に差し替え可能）</summary>
    internal Action<string> OpenFolder { get; set; } = ExternalLauncher.OpenFolder;

    /// <summary>チャットメッセージ一覧</summary>
    public ObservableCollection<ErChatMessage> Messages { get; } = new();

    /// <summary>回答の中のリンクを開く処理（Markdown 表示の吹き出しが使う。開けなかったときはエラーで知らせる）</summary>
    public IChatLinkOpener LinkOpener { get; }

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

    private string _mockFolder = string.Empty;

    /// <summary>モックフォルダのフルパス（会話開始前に指定する動線。開けば即サイドバー・プレビューへ反映）</summary>
    /// <remarks>
    /// ターン実行中の変更は拒む（値を変えず、理由をステータスへ出す）。変更は会話のリセット＝セッションの切り離しを伴い、
    /// 切り離したセッションのターン完了は届かないため、通すと <see cref="IsTurnInProgress"/> が戻らずダイアログが固まる。
    /// 画面からは入力欄と参照ボタンを <see cref="CanEditMockFolder"/> で無効化しているので、ここは二重化の防御。
    /// </remarks>
    public string MockFolder
    {
        get => _mockFolder;
        set
        {
            if (string.Equals(_mockFolder, value, StringComparison.Ordinal))
            {
                return;
            }

            if (IsTurnInProgress)
            {
                StatusMessage = Strings.Mock_MockFolderLockedDuringTurn;
                // 束縛している入力欄に打ち込んだ文字が残らないよう、元の値を読み直させる
                // （WPF の束縛は値を書き込んだ後にソースを読み直すが、それに頼らず通知で確実にする）
                OnPropertyChanged();
                return;
            }

            // 通知の順序は [ObservableProperty] の生成コードと同じ（変更前通知→代入→フック→変更通知）
            OnPropertyChanging();
            _mockFolder = value;
            OnMockFolderChanged(value);
            OnPropertyChanged();
        }
    }

    /// <summary>モックフォルダの指定を変えられるか（ターン実行中は不可＝<see cref="MockFolder"/> の説明を参照）</summary>
    public bool CanEditMockFolder => !IsTurnInProgress;

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
                OnPropertyChanged(nameof(CanEditMockFolder));
                // ターン実行中は接続方式タブも押せなくする
                OnPropertyChanged(nameof(CanChangeBackend));
                BrowseMockFolderCommand.NotifyCanExecuteChanged();
                OnPropertyChanged(nameof(CanClear));
                ClearCommand.NotifyCanExecuteChanged();
                // ターン実行中は添付操作も禁止する
                Attachments.IsTurnInProgress = value;
                NotifyMockGenChanged();

                // ターンが終わったら（完了・失敗・中断・送信の例外のどれでも）回答を Markdown 表示へ切り替える
                if (!value)
                {
                    ErChatMessage.EndStreaming(Messages);
                }
            }
        }
    }

    /// <summary>現在選択中のバックエンドが送信可能な状態か（接続・認証が整っているか）</summary>
    public bool IsBackendReady =>
        Connection.SelectedBackend switch
        {
            ErChatBackendKind.Codex => _codexReady,
            ErChatBackendKind.ClaudeCode => _claudeCodeReady,
            ErChatBackendKind.Copilot => _copilotReady,
            _ => Connection.IsApiKeyConnectionReady,
        };

    /// <summary>図が空（エンティティ 0）か（会話開始の可否に使う）</summary>
    public bool IsDiagramEmpty => _diagramSource.IsEmpty;

    /// <summary>
    /// 新しい会話を開始できるか（接続 OK・図が空でない・ターン非実行中・モックプロジェクト生成の非実行中・
    /// モックフォルダ指定あり・そのフォルダが会話に使える＝モックフォルダでない既存フォルダや破損でない）。
    /// </summary>
    public bool CanStartConversation =>
        IsBackendReady
        && !IsDiagramEmpty
        && !IsTurnInProgress
        && !IsMockGenInProgress
        && !string.IsNullOrWhiteSpace(MockFolder)
        && !_folderBlocked;

    /// <summary>
    /// メッセージを送信できるか（会話開始済み・接続 OK・入力あり・ターン非実行中・モックプロジェクト生成の非実行中）
    /// </summary>
    /// <remarks>
    /// モックプロジェクトの生成はモックフォルダを出力先へ写すため、その間のターンがモックフォルダへ書き込むと
    /// 写した内容と食い違い得る。生成の開始がターンの実行中を拒むのと対にして、生成中は会話を止める。
    /// </remarks>
    public bool CanSendMessage =>
        _conversationStarted
        && IsBackendReady
        && !IsTurnInProgress
        && !IsMockGenInProgress
        && !string.IsNullOrWhiteSpace(UserInput);

    /// <summary>会話（開始・送信）を止めている理由（止めていなければ null＝ツールチップを出さない）</summary>
    /// <remarks>
    /// 表示するのは利用者が待つしかない理由（モックプロジェクトの生成中）だけで、
    /// 接続や入力の不足のように画面の他の場所が既に示している理由は載せない。
    /// </remarks>
    public string? ConversationDisabledReason =>
        IsMockGenInProgress ? Strings.Mock_ConversationBlockedDuringMockGen : null;

    /// <summary>入力欄を編集できるか（ターン実行中は禁止）</summary>
    public bool CanEditInput => !IsTurnInProgress;

    /// <summary>接続方式を切り替えられるか（タブの有効・無効に束縛する）。ターン実行中は切り替えられない。</summary>
    /// <remarks>
    /// 条件は <see cref="TryChangeBackend"/> が断る条件と一致させる（そちらは二重の守りとして残す）。
    /// モックプロジェクトの生成中は切り替えてよい（生成は開始時の接続方式を控えて使うので影響しない）。
    /// </remarks>
    public bool CanChangeBackend => !IsTurnInProgress;

    /// <summary>接続方式タブの選択（タブと TwoWay で束縛し、確定を <see cref="TryChangeBackend"/> へ委ねる）</summary>
    public BackendTabSelection BackendSelection { get; }

    /// <summary>モックフォルダに画面が 1 つ以上あるか（単一 HTML 出力・第2ステップの前提）</summary>
    public bool HasScreens => _store is not null && Screens.Count > 0;

    /// <summary>単一 HTML を出力できるか（モックフォルダに画面が 1 つ以上あるか）</summary>
    public bool CanExportBundle => HasScreens;

    // ── 第2ステップ: モックプロジェクト生成（Claude Code / Codex / Copilot / API キー） ──

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

    /// <summary>生成するプロジェクト名（既定値は固定文字列 <see cref="DefaultProjectName"/>）</summary>
    [ObservableProperty]
    private string _projectName = DefaultProjectName;

    /// <summary>実装に対する追加指示（任意・複数行。選択中バックエンドへ渡すプロンプト末尾に連結される）</summary>
    [ObservableProperty]
    private string _mockGenInstructions = string.Empty;

    /// <summary>モックプロジェクト生成の進捗ログ（追記式・自動スクロール表示）</summary>
    /// <remarks>
    /// 追記は <see cref="AppendMockGenLog"/> のバッファへ溜め、まとめて反映する（差分ごとに通知しない）。
    /// 生成の完了時には必ず全量を反映するため、完了後に読む値は追記した内容の連結と一致する。
    /// </remarks>
    [ObservableProperty]
    private string _mockGenLog = string.Empty;

    /// <summary>進捗ログの反映を遅らせる間隔（この間に届いた差分は 1 回の変更通知にまとめる）</summary>
    private static readonly TimeSpan MockGenLogFlushInterval = TimeSpan.FromMilliseconds(200);

    /// <summary>進捗ログのバッファと予約状態を守るロック（差分は生成器のスレッドから届く）</summary>
    private readonly object _mockGenLogLock = new();

    /// <summary>未反映分を含む進捗ログの全量</summary>
    private readonly StringBuilder _mockGenLogBuffer = new();

    /// <summary>反映の予約が既に積まれているか（積まれていれば追記だけして新たに予約しない）</summary>
    private bool _isMockGenLogFlushScheduled;

    /// <summary>進捗ログの反映を後で実行するよう予約する seam（テストでは手動で実行する）</summary>
    private readonly Action<Action> _scheduleMockGenLogFlush;

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
                // 会話の開始・送信の可否も変わる（NotifyReadinessChanged は第2ステップの通知も含む）
                OnPropertyChanged(nameof(ConversationDisabledReason));
                NotifyReadinessChanged();
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
    /// 出力フォルダあり／プロジェクト名が妥当・非実行中）。4 バックエンド（Claude Code / Codex / Copilot / API キー）すべて可。
    /// </summary>
    public bool CanGenerateMockProject =>
        HasScreens
        && IsSelectedBackendReadyForMockGen
        && IsDotnetAvailable
        && !string.IsNullOrWhiteSpace(OutputFolder)
        && MockProjectScaffoldService.IsValidProjectName(ProjectName)
        && !IsMockGenInProgress
        && !IsTurnInProgress;

    /// <summary>選択中バックエンドが API キー方式か（一発生成の注記表示に使う）</summary>
    public bool IsApiKeyMockGenBackend => Connection.SelectedBackend == ErChatBackendKind.ApiKey;

    /// <summary>
    /// 選択中バックエンドがモックプロジェクト生成の実行条件（CLI 検出／認証／API キー）を満たすか。
    /// Claude Code＝claude CLI 検出・Codex＝認証プローブ結果（<see cref="ApplyCodexReadiness"/>）・
    /// Copilot＝認証プローブ結果（<see cref="ApplyCopilotReadiness"/>）・
    /// API キー＝キー入力あり（ローカル LLM はキー任意）。
    /// </summary>
    private bool IsSelectedBackendReadyForMockGen =>
        Connection.SelectedBackend switch
        {
            ErChatBackendKind.ClaudeCode => IsClaudeCliAvailable,
            ErChatBackendKind.Codex => _codexReady,
            ErChatBackendKind.Copilot => _copilotReady,
            _ => Connection.IsApiKeyConnectionReady,
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

            if (IsTurnInProgress)
            {
                return Strings.Mock_DisabledReason_TurnInProgress;
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

            if (Connection.SelectedBackend == ErChatBackendKind.Copilot && !_copilotReady)
            {
                return Strings.Mock_DisabledReason_CopilotNotReady;
            }

            if (
                Connection.SelectedBackend == ErChatBackendKind.ApiKey
                && !Connection.IsApiKeyConnectionReady
            )
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

            if (MockProjectScaffoldService.CheckProjectName(ProjectName) is { } violation)
            {
                return string.Format(
                    Strings.Mock_DisabledReason_ProjectNameInvalid,
                    PathSegments.Describe(violation, CultureInfo.CurrentUICulture)
                );
            }

            return string.Empty;
        }
    }

    // ── Codex 認証状態（子の状態タブとは別に、認証解決はダイアログ側プローブの責務） ──

    [ObservableProperty]
    private string _codexAccountSummary = Strings.Mock_CodexNotConnected;

    [ObservableProperty]
    private ConnectionHealth _codexStatusLevel = ConnectionHealth.Pending;

    /// <summary>Codex の状態依存の案内文（未検出＝インストール案内・接続失敗＝理由。正常時は空）</summary>
    [ObservableProperty]
    private string _codexGuidance = string.Empty;

    /// <summary>Codex の案内文を表示するか（空なら行ごと隠す）</summary>
    public bool ShowCodexGuidance => !string.IsNullOrWhiteSpace(CodexGuidance);

    private bool _codexReady;

    private bool _claudeCodeReady;

    private bool _copilotReady;

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
            copilotEngineFactory: null,
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
    /// <param name="copilotEngineFactory">GitHub Copilot エンジンのファクトリ</param>
    /// <param name="mockProjectGenerator">モックプロジェクト生成器（省略時は図の供給元のプロバイダから構築）</param>
    /// <param name="dialogService">確認・通知ダイアログ（省略時は MessageBox 実装）</param>
    /// <param name="apiKeyLoader">
    /// API キー読込 seam（省略時は <see cref="ApiKeyStore.Load(string)"/>）。<see cref="Connection"/> へ透過する
    /// </param>
    /// <param name="apiKeySaver">
    /// API キー保存 seam（省略時は <see cref="ApiKeyStore.Save(string, string)"/>）。<see cref="Connection"/> へ透過する
    /// </param>
    /// <param name="scheduleLogFlush">
    /// 進捗ログの反映を予約する seam（省略時は <see cref="MockGenLogFlushInterval"/> 後にスレッドプールで実行する）。
    /// 渡された処理はどのスレッドから呼んでもよい（UI スレッドへの移送は処理側が行う）
    /// </param>
    public MockGenerationDialogViewModel(
        IMockDiagramSource diagramSource,
        IUiDispatcher dispatcher,
        IFileDialogService? files,
        AiSettingsStore? settingsStore,
        Func<ErChatProfile, IErDiagramToolHost, IErChatEngine>? apiKeyEngineFactory,
        Func<ErChatProfile, IErDiagramToolHost, IErChatEngine>? codexEngineFactory,
        Func<ErChatProfile, IErDiagramToolHost, IErChatEngine>? claudeCodeEngineFactory,
        Func<ErChatProfile, IErDiagramToolHost, IErChatEngine>? copilotEngineFactory = null,
        IMockProjectGenerator? mockProjectGenerator = null,
        ChatAttachmentFactory.ImageShrinker? imageShrinker = null,
        IDialogService? dialogService = null,
        Func<string, SavedApiKey?>? apiKeyLoader = null,
        Action<string, string>? apiKeySaver = null,
        Action<Action>? scheduleLogFlush = null
    )
    {
        _diagramSource = diagramSource;
        _dispatcher = dispatcher;
        _scheduleMockGenLogFlush =
            scheduleLogFlush
            ?? (
                flush =>
                    _ = Task.Delay(MockGenLogFlushInterval)
                        .ContinueWith(
                            _ => flush(),
                            CancellationToken.None,
                            TaskContinuationOptions.None,
                            TaskScheduler.Default
                        )
            );
        _files = files ?? new WpfFileDialogService();
        _dialogs = dialogService ?? new MessageBoxDialogService();
        LinkOpener = new ShellChatLinkOpener(_dialogs, Strings.Mock_WindowTitle);

        // 添付部品は本番では WPF の画像縮小を差し込む（テストでは注入された縮小・null）
        Attachments = new AttachmentListViewModel(
            reportStatus: message => StatusMessage = message,
            shrinker: imageShrinker ?? WpfImageShrinker.Shrink
        );
        // 接続方式タブの状態部品。エンジンのファクトリ既定ラムダより前に用意し、get-only プロパティを
        // ラムダから参照させる（PropertyChanged 購読と LoadSettings は下記の ctor 順序に従い後段で行う）。
        Connection = new ChatConnectionSettingsViewModel(
            AiDialogKind.MockGeneration,
            settingsStore,
            apiKeyLoader: apiKeyLoader,
            apiKeySaver: apiKeySaver
        );

        _apiKeyEngineFactory =
            apiKeyEngineFactory
            ?? (
                (profile, toolHost) => Connection.CreateApiKeyEngine(profile, toolHost, _dispatcher)
            );

        // API キー方式のモックプロジェクト生成は、チャットと同じ API キーエンジンを使う。生成時点の Connection 状態
        // （モデル・キー・エンドポイント）を閉包へ閉じ込めるため、確定済みの _apiKeyEngineFactory を渡す
        // （実行は生成時＝Connection ロード後）。
        _mockProjectGenerator =
            mockProjectGenerator
            ?? new MockProjectGenerator(diagramSource.Providers, _apiKeyEngineFactory);
        // 生成ターゲット一覧を取り込み、既定を先頭（Blazor Web App）にする（第 2 ステップの ComboBox が選ばせる）
        MockProjectTargets = _mockProjectGenerator.Targets;
        _selectedMockProjectTarget = MockProjectTargets[0];
        // CLI 3 方式のモデル・プロバイダーは生成後に ApplyConnectionModel が入れる（会話中の変更も同じ口で流す）
        _codexEngineFactory =
            codexEngineFactory
            ?? (
                (profile, toolHost) =>
                    new CodexChatEngine(new CodexAppServerClient(), toolHost, _dispatcher, profile)
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
            );
        _copilotEngineFactory =
            copilotEngineFactory
            ?? (
                (profile, toolHost) =>
                    new CopilotChatEngine(
                        new CopilotRuntimeClient(),
                        toolHost,
                        _dispatcher,
                        profile
                    )
            );

        // ctor 順序厳守: Connection 生成 → PropertyChanged 購読 → Connection.LoadSettings
        // （購読前にロードするとモデル同期・可否再評価が漏れる）
        Connection.PropertyChanged += OnConnectionPropertyChanged;
        Connection.ModelHistoryRemoveFailed += (_, message) => StatusMessage = message;
        // タブへの中継は、上の会話リセットなどの後に走るよう、ここで購読させる
        BackendSelection = new BackendTabSelection(Connection, TryChangeBackend);
        Connection.LoadSettings();

        RefreshAttachmentSupport();
    }

    /// <summary>
    /// 選択中バックエンドに応じて添付部品の対応範囲を再評価する（エンジン生成前でも判定できるよう、
    /// エンジンと同じ正本 <see cref="AttachmentSupportResolver.ForBackend"/> の規則で解決する）。
    /// </summary>
    private void RefreshAttachmentSupport() =>
        Attachments.Support = AttachmentSupportResolver.ForBackend(
            Connection.SelectedBackend,
            Connection.ApiProvider
        );

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

    /// <summary>ダイアログ表示時に API キーを読み込み、前回使った接続方式へ切り替える</summary>
    /// <remarks>
    /// 設定・候補の読込は ctor で <see cref="ChatConnectionSettingsViewModel.LoadSettings"/> 済み。
    /// 接続方式の切り替えはここ（表示時）で行う＝接続確認は外部プロセスを起動するため、VM の生成時には走らせない。
    /// 表示時はターンが無いので、そのまま切り替える。タブは <see cref="BackendSelection"/> の束縛で追従する。
    /// </remarks>
    public void Initialize()
    {
        // 現在のプロバイダーの保存済み API キーを読み直す（子側で _isInitializing 抑止）
        Connection.Initialize();
        NotifyReadinessChanged();

        // 第2ステップ（モックプロジェクト生成）の有効条件（claude CLI・dotnet SDK 検出）を非同期に確認する
        _ = RefreshMockGenAvailabilityAsync();

        Connection.SelectedBackend = Connection.InitialBackend;
    }

    /// <summary>
    /// 図の状態（空かどうか）に依存する可否を評価し直す（ダイアログが前面に戻ったときに呼ぶ）。
    /// </summary>
    /// <remarks>
    /// 図の供給元は変更を通知しないため、メイン画面で図を編集して戻ってきた時点で読み直す。
    /// </remarks>
    public void RefreshDiagramState() => NotifyReadinessChanged();

    // ── モックフォルダ ──

    /// <summary>モックフォルダを選択する</summary>
    [RelayCommand(CanExecute = nameof(CanEditMockFolder))]
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
    private void OnMockFolderChanged(string value)
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

        if (!Path.IsPathFullyQualified(folder))
        {
            // 相対パスはアプリの作業フォルダ基準で解決され、思わぬ場所にモックフォルダを作る
            // （プレビューのファイル URI も組めない）。フルパスを求めて会話の開始を止める
            _folderBlocked = true;
            StatusMessage = Strings.Mock_MockFolderNotFullyQualified;
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

    /// <summary>
    /// マニフェスト宣言の画面群からサイドバー項目を再構築する。選択中の画面がまだあれば同じファイル名で選び直し、
    /// プレビューは要求しない（表示を変えるかどうかは呼び出し側が決める）。
    /// </summary>
    /// <remarks>
    /// 画面一覧に束縛した ListBox は <see cref="ObservableCollection{T}.Clear"/> で選択を null へ押し戻す。
    /// 選び直さないと、表示していない画面の削除・保存だけで選択が外れて先頭の画面へ飛ぶ。
    /// 項目は値で等しくなる record なので、同じ値の旧インスタンスが残っていても新しいインスタンスへ差し替える
    /// （一覧に実在するインスタンスを選択に持つ）。
    /// </remarks>
    private void RebuildScreens()
    {
        var selectedFile = SelectedScreen?.File;
        var wasSuppressed = _suppressPreviewRequest;
        _suppressPreviewRequest = true;

        try
        {
            Screens.Clear();

            if (_store is not null)
            {
                foreach (var screen in _store.Manifest.Screens)
                {
                    Screens.Add(
                        new MockScreenListItem(screen.File, screen.Name, screen.Description)
                    );
                }
            }

            var reselected = selectedFile is null ? null : FindScreen(selectedFile);

            if (!ReferenceEquals(SelectedScreen, reselected))
            {
                SelectedScreen = null;
                SelectedScreen = reselected;
            }
        }
        finally
        {
            _suppressPreviewRequest = wasSuppressed;
        }

        OnPropertyChanged(nameof(HasScreens));
        OnPropertyChanged(nameof(CanExportBundle));
        ExportBundleCommand.NotifyCanExecuteChanged();
        ExportDesignDocCommand.NotifyCanExecuteChanged();
        NotifyMockGenChanged();
    }

    /// <summary>指定ファイルの画面項目を返す（無ければ null。ファイル名は大文字小文字を区別しない）</summary>
    private MockScreenListItem? FindScreen(string file) =>
        Screens.FirstOrDefault(s =>
            string.Equals(s.File, file, StringComparison.OrdinalIgnoreCase)
        );

    /// <summary>
    /// 指定ファイルの画面をサイドバーで選択し、プレビューへ表示し直す（既に選択中でも、内容が変わっているため再要求する）
    /// </summary>
    private void ShowScreen(string file)
    {
        var item = FindScreen(file);

        if (item is null)
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

        RaisePreview(item.File);
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
        var item = FindScreen(file);

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
        IErChatEngine? engine = null;
        var session = new MockFolderDesignSession(
            toolHost => engine = factory(MockDesignProfile.FolderMockDesign, toolHost),
            _store
        );
        AttachSession(session);
        // 切り離し（AttachSession 内の DetachSession）が旧エンジンの参照を消した後に控える
        _sessionEngine = engine;
        ApplyConnectionModel();

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
                // 初回送信は新規（スキーマ）／再開（画面一覧＋差異）のいずれかで会話を開始する。
                // 済みの印は送信が戻ってから立てる（失敗したら次の送信も初回としてやり直す＝
                // 先に立てると、初期化されていないエンジンへフィードバックとして送ってしまう）
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

                _firstMessageSent = true;
            }
            else
            {
                // 2 回目以降は修正フィードバックとして送る（添付を同梱）
                await _session.SendFeedbackAsync(message, attachments).ConfigureAwait(true);
            }

            // 添付はここでは消さない。エンジンの多くは失敗を例外でなく TurnCompleted(false) で返すため、
            // 送信の戻りで消すと、送れなかった添付まで消える。消すのはターンの成功時（ApplyTurnCompleted）
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

        string html;

        try
        {
            // Open 検証が入口の防波堤だが、エクスポート実行時の読み取り失敗（画面 HTML の実体が
            // 差し替えられている等）も同じ経路で拾い、未処理例外でアプリを落とさない。
            html = MockBundleExporter.Export(_store);
        }
        catch (Exception ex)
        {
            StatusMessage = string.Format(Strings.Mock_HtmlSaveFailedFormat, ex.Message);
            return;
        }

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
            AtomicFile.WriteAllText(picked.Path, html, TextEncodings.Utf8NoBomLenient);
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

        var path = Path.Combine(_store.Folder, MockDesignDocExporter.FileName);

        try
        {
            // Open 検証が入口の防波堤だが、エクスポート実行時の読み取り失敗（画面 HTML の実体が
            // 差し替えられている等）も同じ経路で拾い、未処理例外でアプリを落とさない
            // （RegenerateDesignDocIfPresent と同じ形）。
            var markdown = MockDesignDocExporter.Export(_store);
            AtomicFile.WriteAllText(path, markdown, TextEncodings.Utf8NoBomLenient);
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
            AtomicFile.WriteAllText(path, markdown, TextEncodings.Utf8NoBomLenient);
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
        ClearMockGenLog();
        MockGenCompleted = false;
        MockGenSucceeded = false;
        _lastGeneratedOutputDirectory = null;

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

    /// <summary>スキャフォールド＋選択バックエンド（Claude Code / Codex / Copilot / API キー）でモックプロジェクトを生成する</summary>
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
        var diagram = _diagramSource.GetDiagram();
        var projectName = ProjectName.Trim();

        // 出力フォルダに既存のファイル・フォルダがあれば、何が上書きされるかを一覧で見せて確認する。
        // キャンセルなら出力フォルダには一切書き込まない（スキャフォールドへ入らない）。
        var scan = MockProjectOverwriteScanner.Scan(outputFolder, projectName);

        if (
            scan.RequiresConfirmation
            && !_dialogs.ConfirmWarningDetails(
                Strings.Mock_OverwriteConfirm_Message,
                string.Join(Environment.NewLine, scan.ExistingPaths),
                Strings.Mock_WindowTitle
            )
        )
        {
            return;
        }

        // 選択バックエンド別にモデル・プロバイダーを選ぶ（Codex のみプロバイダーを渡す。
        // API キーはエンジンファクトリがモデル・キーを閉じ込めるため、ここで渡す値はログ表示用）
        var backend = Connection.SelectedBackend;
        var model = backend switch
        {
            ErChatBackendKind.Codex => Connection.CodexModel,
            ErChatBackendKind.ClaudeCode => Connection.ClaudeCodeModel,
            ErChatBackendKind.Copilot => Connection.CopilotModel,
            _ => Connection.ApiModel,
        };
        var modelProvider =
            backend == ErChatBackendKind.Codex ? Connection.CodexModelProvider : string.Empty;

        ClearMockGenLog();
        MockGenCompleted = false;
        MockGenSucceeded = false;
        _lastGeneratedOutputDirectory = null;
        IsMockGenInProgress = true;
        StatusMessage = Strings.Mock_GeneratingProject;

        // 上書きの案内はログのクリアより後に出す（先に出すとこの直後のクリアで消えて一度も見えない）
        if (scan.RequiresConfirmation)
        {
            AppendMockGenLog(Strings.Mock_OutputFolderNotEmpty);
        }

        _mockGenCts = new CancellationTokenSource();

        MockProjectGenerationResult? result = null;

        try
        {
            result = await _mockProjectGenerator
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
                    AppendMockGenLog,
                    ConfirmVerificationBuild,
                    _mockGenCts.Token
                )
                .ConfigureAwait(true);

            MockGenSucceeded = result.Success;
            _lastGeneratedOutputDirectory = result.OutputDirectory;
            StatusMessage = result.Message;
        }
        catch (Exception ex)
        {
            MockGenSucceeded = false;
            AppendMockGenLog(string.Format(Strings.Mock_GenerationErrorLogFormat, ex.Message));
            StatusMessage = string.Format(Strings.Mock_GenerationFailedFormat, ex.Message);
        }
        finally
        {
            // 間引いて未反映の差分を、完了状態へ移る前に全量反映する（完了ダイアログの時点でログが欠けない）
            FlushMockGenLog();
            _mockGenCts?.Dispose();
            _mockGenCts = null;
            IsMockGenInProgress = false;
            MockGenCompleted = true;
        }

        // 完了を明示する（ステータスバーだけでは見落としやすいため）。モーダルは実行中フラグを
        // 落としてから出す＝OK を待つ間も画面は完了状態で、ダイアログがウィンドウの背面へ
        // 回り込んでも「生成を実行中」の表示とボタンの無効化が残らない。
        // 例外で終わった場合（result が null）とユーザー自身の中断ではダイアログを出さない。
        if (result is null || result.Interrupted)
        {
            return;
        }

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
        else if (result.BuildDeclined)
        {
            // 検証ビルドを実行しなかったのは利用者自身の選択で、失敗が起きたわけではない
            // （Error アイコンは「すでに発生した失敗の報告」専用＝IDialogService の規約）。
            // 成果物は残っているので、未検証であることと確認先だけを案内する。
            _dialogs.ShowInformation(
                string.Format(
                    Strings.Mock_GenResultNotVerifiedBodyFormat,
                    result.Message,
                    string.IsNullOrWhiteSpace(result.LogPath)
                        ? result.OutputDirectory
                        : result.LogPath
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

    /// <summary>
    /// 最終ビルド（検証ビルド）を実行してよいかを、変更されたファイルの一覧つきで確認する。
    /// </summary>
    /// <param name="changedPaths">UI 層のソース・静的資産以外で追加・変更されたファイルの相対パス</param>
    /// <remarks>
    /// 最終ビルドは AI の CLI のサンドボックスの外・ユーザー権限で走るため、AI が書いたビルド設定を
    /// そのまま実行し得る。判断材料（何が書かれたか）を全件見せる必要があるので、件数を畳まない
    /// <see cref="IDialogService.ConfirmWarningDetails"/> を使う。
    /// <para>
    /// 呼び出しはランナーのスレッド（UI スレッドではない）から来るため、
    /// <see cref="IUiDispatcher.Invoke{T}"/> で UI スレッドへ移して待つ。
    /// </para>
    /// </remarks>
    private bool ConfirmVerificationBuild(IReadOnlyList<string> changedPaths) =>
        _dispatcher.Invoke(() =>
            _dialogs.ConfirmWarningDetails(
                Strings.Mock_BuildConfirm_Message,
                string.Join(Environment.NewLine, changedPaths),
                Strings.Mock_WindowTitle
            )
        );

    /// <summary>モックプロジェクト生成の中断起点</summary>
    private CancellationTokenSource? _mockGenCts;

    /// <summary>
    /// 直近の生成が成果物を書き出したフォルダ（「フォルダを開く」が開く先。生成の開始と画面の初期化で破棄する）
    /// </summary>
    /// <remarks>
    /// 出力フォルダ欄は生成の後も編集できるため、欄の現在値を開くと、生成していない場所を開いてしまう。
    /// </remarks>
    private string? _lastGeneratedOutputDirectory;

    /// <summary>実行中のモックプロジェクト生成を中断する</summary>
    [RelayCommand(CanExecute = nameof(IsMockGenInProgress))]
    private async Task InterruptMockGenAsync()
    {
        _mockGenCts?.Cancel();
        await _mockProjectGenerator.InterruptAsync().ConfigureAwait(true);
    }

    /// <summary>
    /// アプリ終了などの同期的な終了経路から、実行中の処理をベストエフォートで打ち切る。
    /// </summary>
    /// <remarks>
    /// ウィンドウを閉じるだけでは、モックプロジェクト生成が起動した claude / codex / copilot / dotnet の
    /// 子プロセスが孤児として残る。終了経路は同期のため完了は待たず（kill 相当までは求めない）、
    /// 中断要求を出すところまでを行う。
    /// </remarks>
    public void RequestInterrupt()
    {
        if (IsMockGenInProgress)
        {
            try
            {
                _mockGenCts?.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // 生成が同時に終わって破棄済みなら何もしない
            }

            Forget(_mockProjectGenerator.InterruptAsync());
        }

        if (IsTurnInProgress && _session is not null)
        {
            Forget(_session.InterruptAsync());
        }
    }

    /// <summary>
    /// アプリ終了などの同期的な終了経路から、会話中のセッションが抱えるエンジンを破棄する。
    /// </summary>
    /// <param name="timeout">
    /// 破棄の完了を待つ上限。超過しても終了経路は続行する（停止はベストエフォート）
    /// </param>
    /// <remarks>
    /// <see cref="RequestInterrupt"/> が止められるのは実行中のターンだけで、ターンを実行していない
    /// 常駐プロセス（codex app-server・copilot ランタイム）は残る。
    /// それらを確実に止める経路はエンジンの破棄だけなので、ウィンドウを閉じる前にここを通す。
    /// 待ちに上限を設けるのは、破棄がハングしたときにアプリの終了を長く止めないため。
    /// </remarks>
    public void ShutdownEngines(TimeSpan timeout)
    {
        var session = _session;
        // 会話を切り替えた直後に閉じた場合、旧セッションの破棄はまだ走っている可能性がある。
        // 待たずに終了すると、その常駐子プロセス（codex app-server 等）が孤児になる
        var detached = _detachedDisposal;

        if (session is null && detached is null)
        {
            return;
        }

        // 破棄そのものは必ずスレッドプールで走らせる。終了経路は UI スレッドで、ここで完了を待つため、
        // 破棄の途中に UI スレッドへのマーシャリングがあると待ちがデッドロックする
        var shutdown = Task.Run(async () =>
        {
            if (session is not null)
            {
                try
                {
                    await session.DisposeAsync().ConfigureAwait(false);
                }
                catch (Exception)
                {
                    // 終了経路のため、破棄の失敗は報告先が無い
                }
            }

            if (detached is not null)
            {
                try
                {
                    await detached.ConfigureAwait(false);
                }
                catch (Exception)
                {
                    // 切り離し済みセッションの破棄の失敗も同様
                }
            }
        });

        try
        {
            // 現在のセッションと切り離し済みの破棄を合わせて 1 つの上限で待つ
            // （ハングした破棄でアプリの終了を長く止めない）
            shutdown.Wait(timeout);
        }
        catch (Exception)
        {
            // 破棄の失敗・待ちの打ち切りで終了処理を妨げない
        }
    }

    /// <summary>完了を待たない中断要求の後始末（失敗しても終了処理を妨げない）</summary>
    private static void Forget(Task task) =>
        task.ContinueWith(
            static completed => _ = completed.Exception,
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted,
            TaskScheduler.Default
        );

    /// <summary>直近の生成が書き出したフォルダをエクスプローラで開く（欄の現在値ではない）</summary>
    [RelayCommand(CanExecute = nameof(ShowOpenFolder))]
    private void OpenOutputFolder()
    {
        var folder = _lastGeneratedOutputDirectory;

        if (folder is not null && Directory.Exists(folder))
        {
            OpenFolder(folder);
        }
    }

    /// <summary>進捗ログへ追記する（反映はまとめて行う）</summary>
    /// <remarks>
    /// 差分ごとに <see cref="MockGenLog"/> を連結し直すと、長時間の生成で文字列の再確保が O(n²) になり、
    /// 変更通知（とビューの自動スクロール）も差分の数だけ走る。そこでバッファへ追記し、未反映の予約が
    /// 無いときだけ反映を 1 件予約する＝予約が実行されるまでに届いた差分は 1 回の通知にまとまる。
    /// 生成器のスレッドから直接呼ばれる（呼び出し元を UI スレッドで待たせない）。
    /// </remarks>
    private void AppendMockGenLog(string text)
    {
        lock (_mockGenLogLock)
        {
            _mockGenLogBuffer.Append(text);

            if (_isMockGenLogFlushScheduled)
            {
                return;
            }

            _isMockGenLogFlushScheduled = true;
        }

        _scheduleMockGenLogFlush(() => RunOnUi(FlushMockGenLog));
    }

    /// <summary>バッファの全量を <see cref="MockGenLog"/> へ反映する（UI スレッドで呼ぶ）</summary>
    /// <remarks>
    /// 予約済みフラグを下ろしてから反映するので、以後の追記は新しい予約を積む。後から実行された古い予約は
    /// 同じ全量を読むだけで、値が変わらなければ変更通知も出ない。
    /// </remarks>
    private void FlushMockGenLog()
    {
        string text;

        lock (_mockGenLogLock)
        {
            text = _mockGenLogBuffer.ToString();
            _isMockGenLogFlushScheduled = false;
        }

        MockGenLog = text;
    }

    /// <summary>進捗ログを空にする（バッファと表示の両方）</summary>
    private void ClearMockGenLog()
    {
        lock (_mockGenLogLock)
        {
            _mockGenLogBuffer.Clear();
        }

        MockGenLog = string.Empty;
    }

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
            ErChatBackendKind.Copilot => _copilotEngineFactory,
            _ => _apiKeyEngineFactory,
        };

    /// <summary>
    /// 接続タブのモデル（Codex はプロバイダーも）を会話のエンジンへ入れる（会話の開始時と、接続タブの変更時に呼ぶ）。
    /// </summary>
    /// <remarks>
    /// 効き始めはエンジンごとに違い、AI チャットと同じ＝Claude Code は次のターンから、
    /// Codex と Copilot は次のスレッド（セッション）の開始から。API キー方式は接続設定を毎ターン読むので対象外。
    /// </remarks>
    private void ApplyConnectionModel()
    {
        switch (_sessionEngine)
        {
            case CodexChatEngine codex:
                codex.ModelProvider = Connection.CodexModelProvider;
                codex.Model = Connection.CodexModel;
                break;

            case ClaudeCodeChatEngine claudeCode:
                claudeCode.Model = Connection.ClaudeCodeModel;
                break;

            case CopilotChatEngine copilot:
                copilot.Model = Connection.CopilotModel;
                break;
        }
    }

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

    /// <summary>
    /// 現在のセッションのイベント購読を解除し、セッション（が抱えるエンジン）を破棄する
    /// （新セッションへの差し替え・会話リセット時に呼ぶ）。
    /// </summary>
    /// <remarks>
    /// 破棄しないと、会話を切り替えるたびに旧エンジン（CLI バックエンドなら codex app-server 等の
    /// 常駐子プロセスを含む）がアプリ終了まで残り続ける。切替経路は UI の同期処理のため完了は
    /// 待たず（破棄は子プロセスの停止を含む）、破棄は丸ごとスレッドプールで走らせる
    /// （非 async の DisposeAsync 実装が同期区間で投げる例外も Task へ閉じ込め、
    /// <see cref="Forget"/> が観測して握り潰す＝<see cref="ShutdownEngines"/> と同じ形）。
    /// 購読解除を破棄より先に行い、破棄が誘発するイベント（中断のターン完了等）を UI へ流さない。
    /// </remarks>
    private void DetachSession()
    {
        if (_session is null)
        {
            return;
        }

        var session = _session;
        session.AssistantDeltaReceived -= OnAssistantDelta;
        session.ScreenSaved -= OnScreenSaved;
        session.ScreenRemoved -= OnScreenRemoved;
        session.StylesheetSaved -= OnStylesheetSaved;
        session.TurnCompleted -= OnTurnCompleted;
        session.StatusChanged -= OnStatus;
        _session = null;
        _sessionEngine = null;

        var disposal = Task.Run(() => session.DisposeAsync().AsTask());

        // 上書きせず連結する。A→B→C と続けて切り替えて閉じると、上書きでは A の破棄を
        // ShutdownEngines が待てず、その常駐子プロセス（codex app-server 等）が孤児になる
        _detachedDisposal = _detachedDisposal is { IsCompleted: false } pending
            ? Task.WhenAll(pending, disposal)
            : disposal;

        // 連結後のタスクにも掛ける。Task.WhenAll の例外は、ShutdownEngines が呼ばれなければ
        // 誰も観測せず TaskScheduler.UnobservedTaskException（App が購読している）へ回る
        Forget(_detachedDisposal);
    }

    /// <summary>
    /// 切り離したセッションの破棄タスク（未完了のものを連結して持ち、終了時にまとめて待つ）
    /// </summary>
    /// <remarks>
    /// 参照を捨てると、会話の切替直後にアプリを閉じたとき旧セッションの破棄を待てない
    /// （<see cref="ShutdownEngines"/> は現在のセッションしか見られない）。切替は UI の同期処理なので
    /// ここでは完了を待たず、終了時にだけ上限つきで待ち合わせる。
    /// <b>上書きではなく連結する</b>＝続けて切り替えると、上書きでは古い破棄を待てなくなる。
    /// </remarks>
    private Task? _detachedDisposal;

    /// <summary>
    /// 切り離したセッションの破棄タスク（テストが完了を待つための口。未切替なら <c>null</c>）
    /// </summary>
    /// <remarks>
    /// 破棄はスレッドプールで走るため、到達を短い上限つきのポーリングで待つと、全スイートの
    /// 並列実行でプールが飽和したときに上限内へ入らず落ちる（実測: 飽和状態では自明な
    /// <c>Task.Run</c> ですら 2 秒以内に開始しない）。テストはこのタスクを待つ。
    /// </remarks>
    internal Task? DetachedDisposalForTests => _detachedDisposal;

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
                IsStreaming = true,
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
        // 保存された画面を選択してプレビューを再要求する（同名の再保存で選択が変わらなくても内容は新しい）
        ShowScreen(e.File);

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

        // 再構築は選択中の画面がまだあれば選び直す＝表示していない画面の削除では選択もプレビューも動かない
        RebuildScreens();

        // 表示していた画面が消えた（または何も選んでいなかった）ら先頭画面（なければ空表示）へ
        if (SelectedScreen is null)
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
            // 送った添付はここで消す（失敗・中断では残し、送り直せるようにする）。
            // ターン実行中は添付を足せないので、いま残っているのは送った添付だけ
            Attachments.Clear();
            StatusMessage = Strings.Mock_ResponseCompleted;

            // 成功したターンで使ったモデルを MRU 履歴へ記録する（ローカル LLM / Codex のガードは子 VM 側）。
            // 記録は設定ファイルの書き込みを伴う。失敗してもターンの成功は変わらないので、ここで受け止める
            // （漏らすと同期の Dispatcher.Invoke を通ってエンジンへ戻り、成功したターンが失敗として二度通知される）
            try
            {
                Connection.RecordSuccessfulModel();
            }
            catch (Exception ex)
            {
                StatusMessage = string.Format(
                    Strings.Mock_ModelHistorySaveFailedFormat,
                    ex.Message
                );
            }
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

    /// <summary>設定を保存する（ウィンドウ非表示化時などに外部から呼ぶ）</summary>
    /// <remarks>接続タブの状態保存は子（<see cref="ChatConnectionSettingsViewModel.SaveSettings"/>）へ委譲する。</remarks>
    public void SaveSettings() => Connection.SaveSettings();

    /// <summary>
    /// × でウィンドウを閉じるときに、設定を保存してからウィンドウを隠す。
    /// 保存に失敗してもウィンドウは必ず隠し、隠した後でモーダルのエラーで知らせる（理由はステータスにも残す）。
    /// </summary>
    /// <remarks>
    /// 保存はディスクフル・権限などで落ちうる。例外を Closing から漏らすと、閉じる操作が取り消されたまま
    /// 隠れもしないウィンドウが残る。隠したウィンドウのステータスは開き直すまで見えないため、失敗はモーダルで知らせる
    /// （隠した後に出すので、オーナーは残っているウィンドウになる）。アプリ終了時の経路（ForceClose）は
    /// <see cref="SaveSettings"/> を直接呼び、失敗は終了の記録へ残す。AI チャットの同名メソッドと同じ形。
    /// </remarks>
    /// <param name="hide">ウィンドウを隠す処理</param>
    public void SaveSettingsAndHide(Action hide)
    {
        Exception? failure = null;

        try
        {
            SaveSettings();
        }
        catch (Exception ex)
        {
            failure = ex;
        }

        hide();

        if (failure is not null)
        {
            var message = string.Format(Strings.Mock_SettingsSaveFailedFormat, failure.Message);
            StatusMessage = message;
            _dialogs.ShowError(message, Strings.Mock_WindowTitle);
        }
    }

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
            case nameof(ChatConnectionSettingsViewModel.CodexModel):
            case nameof(ChatConnectionSettingsViewModel.ClaudeCodeModel):
            case nameof(ChatConnectionSettingsViewModel.CopilotModel):
                // 会話中のエンジンへモデルの変更を流す（会話の開始前は開始時に入る）
                ApplyConnectionModel();
                break;
        }
    }

    /// <summary>
    /// 接続タブの切り替えを試みる（タブの選択＝<see cref="BackendSelection"/> が確定のために呼ぶ）。切り替えたら true、拒んだら false を返す。
    /// </summary>
    /// <param name="newBackend">切り替え先の接続方式</param>
    /// <remarks>
    /// ターン実行中は拒む（理由をステータスへ出す）。切り替えは会話のリセット＝セッションの切り離しを伴い、
    /// 切り離したセッションのターン完了は届かないため、通すと <see cref="IsTurnInProgress"/> が戻らずダイアログが固まる。
    /// 画面からは <see cref="CanChangeBackend"/> でタブ自体を押せなくしているので、ここでの拒否は二重の守り
    /// （キーボード・UI オートメーション・プログラムからの経路はタブの無効化を通らないことがある）。
    /// 拒んだときは <see cref="BackendSelection"/> がタブを元の接続方式へ戻す。
    /// </remarks>
    public bool TryChangeBackend(ErChatBackendKind newBackend)
    {
        if (newBackend == Connection.SelectedBackend)
        {
            return true;
        }

        if (IsTurnInProgress)
        {
            StatusMessage = Strings.Mock_SwitchBackendBlockedDuringTurn;
            return false;
        }

        Connection.SelectedBackend = newBackend;
        return true;
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

    partial void OnCodexGuidanceChanged(string value) =>
        OnPropertyChanged(nameof(ShowCodexGuidance));

    partial void OnOutputFolderChanged(string value) => NotifyMockGenChanged();

    partial void OnProjectNameChanged(string value) => NotifyMockGenChanged();

    partial void OnIsClaudeCliAvailableChanged(bool value) => NotifyMockGenChanged();

    partial void OnIsDotnetAvailableChanged(bool value) => NotifyMockGenChanged();

    /// <summary>Codex 接続状態を外部（ダイアログのコードビハインド）から反映する</summary>
    /// <param name="ready">送信可能な状態か</param>
    /// <param name="summary">状態サマリー（未接続・未ログイン・未検出など）</param>
    /// <param name="level">状態ドットの健全度</param>
    /// <param name="guidance">状態依存の案内文（未検出＝インストール案内・接続失敗＝理由。正常時は空）</param>
    public void ApplyCodexReadiness(
        bool ready,
        string summary,
        ConnectionHealth level,
        string guidance = ""
    )
    {
        _codexReady = ready;
        CodexAccountSummary = summary;
        CodexStatusLevel = level;
        CodexGuidance = guidance;
        NotifyReadinessChanged();
    }

    /// <summary>
    /// Codex プローブのステータス通知（接続失敗・アカウント取得失敗など）を反映する。
    /// プローブエンジンは会話用エンジンではないため、通知経路をここで明示的にステータスバーへ繋ぐ。
    /// </summary>
    /// <param name="message">通知メッセージ</param>
    public void ApplyCodexStatusMessage(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        StatusMessage = message;
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

    /// <summary>GitHub Copilot 接続状態を外部から反映する</summary>
    /// <param name="ready">送信可能な状態か（接続済みかつログイン済み）</param>
    /// <param name="summary">状態サマリー（未検出・未ログイン・ログイン済みなど）</param>
    /// <param name="level">状態ドットの健全度</param>
    /// <param name="guidance">状態依存の案内文</param>
    /// <param name="availableModels">
    /// 接続後に実行時列挙したモデル ID の一覧。Copilot は静的カタログを持たないため、
    /// これを接続タブのモデル候補（削除不可の上段）へ流し込む。
    /// </param>
    public void ApplyCopilotReadiness(
        bool ready,
        string summary,
        ConnectionHealth level,
        string guidance,
        IReadOnlyList<string> availableModels
    )
    {
        _copilotReady = ready;
        Connection.CopilotStatusSummary = summary;
        Connection.CopilotStatusLevel = level;
        Connection.CopilotGuidance = guidance;
        Connection.CopilotAvailableModels = availableModels;
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
