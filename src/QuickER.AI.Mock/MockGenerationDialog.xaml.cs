using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using QuickER.AI;
using QuickER.AI.Mock.Resources;
using QuickER.AI.UI;
using QuickER.Gui.Abstractions;

namespace QuickER.AI.Mock;

/// <summary>
/// AI モック生成ダイアログ（左チャット／右 HTML プレビュー）のコードビハインド。
/// </summary>
/// <remarks>
/// 接続方式ごとの認証状態プローブ（Codex / Claude / Copilot）はここで軽量エンジンを保持して確認し、
/// 結果を ViewModel へ反映する。生成に使うエンジンは ViewModel が生成開始時に別途構築する。
/// ViewModel のプレビュー要求（現在画面のファイルパス）を受けて、モックフォルダ内の実ファイルを
/// 直接 WebView2 プレビューへ Navigate する（一時ファイルは使わない）。
/// </remarks>
public partial class MockGenerationDialog : Window
{
    /// <summary>このウィンドウの ViewModel</summary>
    public MockGenerationDialogViewModel ViewModel { get; }

    /// <summary>Codex 認証状態のプローブ用エンジン（ツールホスト無し）</summary>
    private readonly CodexChatEngine _codexProbe;

    /// <summary>Claude Code ログイン状態のプローブ用エンジン（ツールホスト無し）</summary>
    private readonly ClaudeCodeChatEngine _claudeCodeProbe;

    /// <summary>GitHub Copilot ログイン状態・モデル一覧のプローブ用エンジン（ツールホスト無し）</summary>
    private readonly CopilotChatEngine _copilotProbe;

    /// <summary>アプリ終了などで強制クローズ中かどうか</summary>
    private bool _isForceClosing;

    /// <summary>終了の連鎖で受け止めた失敗の記録先</summary>
    private readonly IShutdownFailureReporter _shutdownFailureReporter;

    /// <summary>「再確認」（Codex）コマンド</summary>
    public IAsyncRelayCommand CodexRefreshCommand { get; }

    /// <summary>「再確認」（Claude）コマンド</summary>
    public IAsyncRelayCommand ClaudeCodeRefreshCommand { get; }

    /// <summary>「再確認」（Copilot）コマンド</summary>
    public IAsyncRelayCommand CopilotRefreshCommand { get; }

    /// <summary>注入された ViewModel を結び付けてウィンドウを生成する</summary>
    /// <param name="viewModel">このウィンドウの ViewModel</param>
    /// <param name="shutdownFailureReporter">
    /// <see cref="ForceClose"/> の各段で受け止めた失敗の記録先（ランチャーが DI から渡す）
    /// </param>
    public MockGenerationDialog(
        MockGenerationDialogViewModel viewModel,
        IShutdownFailureReporter shutdownFailureReporter
    )
        : this(
            viewModel,
            shutdownFailureReporter,
            new CodexAppServerClient(),
            new ClaudeCodeProcessClient(),
            new CopilotRuntimeClient()
        ) { }

    /// <summary>プローブ用エンジンのクライアントを差し替えてウィンドウを生成する（テスト用）</summary>
    /// <param name="viewModel">このウィンドウの ViewModel</param>
    /// <param name="shutdownFailureReporter"><see cref="ForceClose"/> の各段で受け止めた失敗の記録先</param>
    /// <param name="codexClient">Codex プローブのクライアント</param>
    /// <param name="claudeCodeClient">Claude Code プローブのクライアント</param>
    /// <param name="copilotClient">GitHub Copilot プローブのクライアント</param>
    internal MockGenerationDialog(
        MockGenerationDialogViewModel viewModel,
        IShutdownFailureReporter shutdownFailureReporter,
        ICodexAppServerClient codexClient,
        IClaudeCodeClient claudeCodeClient,
        ICopilotRuntimeClient copilotClient
    )
    {
        ArgumentNullException.ThrowIfNull(shutdownFailureReporter);

        InitializeComponent();
        ViewModel = viewModel;
        _shutdownFailureReporter = shutdownFailureReporter;
        DataContext = ViewModel;

        var dispatcher = new WpfUiDispatcher();
        _codexProbe = new CodexChatEngine(
            codexClient,
            toolHost: null,
            dispatcher,
            MockDesignProfile.FolderMockDesign
        );
        _codexProbe.AuthStateChanged += OnCodexAuthStateChanged;
        // 接続失敗・アカウント取得失敗の理由はステータス通知にしか出ないため、こちらも購読して VM へ流す
        _codexProbe.StatusChanged += OnCodexStatusChanged;
        _claudeCodeProbe = new ClaudeCodeChatEngine(
            claudeCodeClient,
            toolHost: null,
            dispatcher,
            MockDesignProfile.FolderMockDesign
        );
        _claudeCodeProbe.StatusSummaryChanged += OnClaudeCodeStatusChanged;
        _copilotProbe = new CopilotChatEngine(
            copilotClient,
            toolHost: null,
            dispatcher,
            MockDesignProfile.FolderMockDesign
        );
        _copilotProbe.StatusSummaryChanged += OnCopilotStatusChanged;
        // モデル一覧は接続後に実行時列挙されるため、更新通知も拾って接続タブの候補へ流す
        _copilotProbe.AvailableModelsChanged += OnCopilotStatusChanged;

        // 「再確認」も失敗をステータスへ畳む（AsyncRelayCommand は例外をディスパッチャへ投げ直す）
        CodexRefreshCommand = new AsyncRelayCommand(() => RunProbeAsync(RefreshCodexAsync));
        ClaudeCodeRefreshCommand = new AsyncRelayCommand(() =>
            RunProbeAsync(RefreshClaudeCodeAsync)
        );
        CopilotRefreshCommand = new AsyncRelayCommand(() => RunProbeAsync(RefreshCopilotAsync));

        // ViewModel のプレビュー要求／クリア要求と、プレビュー内リンク遷移をサイドバー選択へ橋渡しする
        ViewModel.PreviewRequested += OnPreviewRequested;
        ViewModel.PreviewClearRequested += OnPreviewClearRequested;
        Preview.CurrentFileChanged += OnPreviewCurrentFileChanged;
        ViewModel.Messages.CollectionChanged += (_, _) => ScrollToBottom();
        // ストリーミングで吹き出しが伸びたときも、末尾付近を見ていれば追従する（読み返し中は動かさない）
        ChatScrollFollower.Attach(ChatScrollViewer);
        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
        Loaded += OnLoaded;
        Closing += OnWindowClosing;
        // 図の変更は通知されないため、前面に戻るたびに図に依存する可否を読み直す（再表示・メイン画面からの戻り）
        Activated += (_, _) => ViewModel.RefreshDiagramState();
    }

    /// <summary>進捗ログの更新に追従して自動スクロールする</summary>
    private void OnViewModelPropertyChanged(
        object? sender,
        System.ComponentModel.PropertyChangedEventArgs e
    )
    {
        if (e.PropertyName == nameof(MockGenerationDialogViewModel.MockGenLog))
        {
            Dispatcher.InvokeAsync(
                () => MockGenLogScroll?.ScrollToEnd(),
                System.Windows.Threading.DispatcherPriority.Background
            );
        }
    }

    /// <summary>初回表示時に ViewModel を初期化し、前回使った接続タブを復元する</summary>
    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoaded;
        ViewModel.Initialize();
        ApplyInitialBackendTab();
    }

    /// <summary>
    /// 前回使った接続タブを復元する。SelectedIndex の変更で通常のタブ切替処理
    /// （<see cref="BackendTabs_SelectionChanged"/>）が走り、切替経路を一本に保つ。
    /// </summary>
    private void ApplyInitialBackendTab()
    {
        var index = ViewModel.Connection.InitialBackend switch
        {
            ErChatBackendKind.Codex => 1,
            ErChatBackendKind.ClaudeCode => 2,
            ErChatBackendKind.Copilot => 3,
            _ => 0,
        };

        if (index != BackendTabs.SelectedIndex)
        {
            BackendTabs.SelectedIndex = index;
        }
    }

    /// <summary>
    /// ×ボタンでは閉じず、設定を保存して非表示にし状態を維持する（シングルトン動作）。
    /// 保存に失敗しても非表示にし、失敗はモーダルで知らせる。
    /// </summary>
    private void OnWindowClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_isForceClosing)
        {
            return;
        }

        e.Cancel = true;
        ViewModel.SaveSettingsAndHide(Hide);
    }

    /// <summary>
    /// アプリ終了時にエンジンの破棄を待つ上限。破棄は子プロセスの停止とハンドル解放までで通常 1 秒未満だが、
    /// ハングしたときにアプリの終了を長く止めないため上限を設ける。
    /// </summary>
    private static readonly TimeSpan EngineShutdownTimeout = TimeSpan.FromSeconds(3);

    /// <summary>アプリ終了時などにウィンドウを実際に閉じる</summary>
    /// <remarks>
    /// <para>
    /// 閉じる前に、(1) 実行中の処理（モックプロジェクト生成・会話ターン）を打ち切り
    /// (2) 会話中のエンジンを破棄し (3) 接続状態の確認に使うプローブ用エンジン 3 つも破棄して、
    /// 常駐する子プロセス（codex app-server・copilot ランタイム）まで止める。
    /// 中断だけでは、処理を実行していない常駐プロセスが残るため、すべてを通す。
    /// (2) と (3) はそれぞれ <see cref="EngineShutdownTimeout"/> を上限に待つので、閉じるまでの待ちは最大でその 2 倍になる。
    /// </para>
    /// <para>
    /// 各段は <see cref="ShutdownSteps.Run"/> で独立して受け止める。とくに設定保存は
    /// ディスクフル・権限などで落ちうるが、そこで連鎖が止まるとエンジンの破棄へ届かず
    /// 常駐プロセスが孤児として残る。<see cref="Window.Close"/> 自体の失敗も同じ理由で受け止める。
    /// </para>
    /// </remarks>
    public void ForceClose()
    {
        _isForceClosing = true;
        ShutdownSteps.Run(
            _shutdownFailureReporter,
            "MockGeneration.RequestInterrupt",
            ViewModel.RequestInterrupt
        );
        ShutdownSteps.Run(
            _shutdownFailureReporter,
            "MockGeneration.SaveSettings",
            ViewModel.SaveSettings
        );
        ShutdownSteps.Run(
            _shutdownFailureReporter,
            "MockGeneration.ShutdownEngines",
            () => ViewModel.ShutdownEngines(EngineShutdownTimeout)
        );
        ShutdownSteps.Run(
            _shutdownFailureReporter,
            "MockGeneration.DisposeProbes",
            () =>
                EngineShutdown.DisposeAll(
                    EngineShutdownTimeout,
                    _codexProbe,
                    _claudeCodeProbe,
                    _copilotProbe
                )
        );
        ShutdownSteps.Run(_shutdownFailureReporter, "MockGeneration.Close", Close);
    }

    /// <summary>プレビュー要求を受けて、モックフォルダ内の実ファイルをプレビューへ Navigate する</summary>
    private void OnPreviewRequested(object? sender, MockPreviewRequest request)
    {
        try
        {
            Preview.Navigate(ToPreviewUri(request.FilePath), request.Folder);
        }
        catch (Exception ex)
        {
            ViewModel.StatusMessage = string.Format(
                Strings.Mock_PreviewUpdateFailedFormat,
                ex.Message
            );
        }
    }

    /// <summary>画面ファイルのフルパスからプレビューへ渡すファイル URI を作る</summary>
    /// <remarks>
    /// 暗黙のファイルパスとして解釈させる（<c>#</c>・<c>?</c>・<c>%</c> はパスの文字としてエスケープされ、
    /// フラグメントやクエリにならない）。<see cref="UriBuilder"/> で組むと <c>%20</c> のような並びを
    /// 復号してしまい、別のファイルを指す。モックフォルダは ViewModel がフルパスに限っている。
    /// </remarks>
    /// <param name="path">画面ファイルのフルパス</param>
    internal static Uri ToPreviewUri(string path) => new(path, UriKind.Absolute);

    /// <summary>プレビューのクリア要求を受けて、空表示へ戻す</summary>
    private void OnPreviewClearRequested(object? sender, EventArgs e) => Preview.ShowEmpty();

    /// <summary>プレビュー内リンクで表示画面が変わったら、サイドバーの選択を追従させる</summary>
    private void OnPreviewCurrentFileChanged(object? sender, string file) =>
        ViewModel.SyncSelectionFromPreview(file);

    /// <summary>チャット表示を最下部へスクロールする</summary>
    private void ScrollToBottom()
    {
        Dispatcher.InvokeAsync(
            () => ChatScrollViewer?.ScrollToEnd(),
            System.Windows.Threading.DispatcherPriority.Background
        );
    }

    // ── モックフォルダ欄の確定 ──

    /// <summary>
    /// モックフォルダ欄の入力を、遅延（<c>Delay</c>）を待たずに ViewModel へ確定する。
    /// </summary>
    /// <remarks>
    /// 欄は入力が止まってから確定する（キー入力ごとにフォルダを走査して会話をやり直さない）。
    /// ただし利用者が欄を離れた時点の値で動くべき操作（「新しい会話」など）が、確定前の古いフォルダで
    /// 動かないよう、フォーカスが外れたときと Enter で即座に確定する。ボタンはマウスを押した時点で
    /// フォーカスを取るので、クリックの実行可否の判定より前にこの確定が走る。
    /// </remarks>
    private void CommitMockFolderInput() =>
        BindingOperations.GetBindingExpression(MockFolderBox, TextBox.TextProperty)?.UpdateSource();

    /// <summary>モックフォルダ欄からフォーカスが外れたら確定する</summary>
    private void MockFolderBox_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e) =>
        CommitMockFolderInput();

    /// <summary>モックフォルダ欄で Enter を押したら確定する</summary>
    private void MockFolderBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            CommitMockFolderInput();
        }
    }

    /// <summary>API キー欄から離れたら、入力したキーを保存する（打鍵ごとには保存しない）</summary>
    private void ApiKeyBox_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e) =>
        ViewModel.Connection.CommitApiKey();

    // ── 添付: ボタン・Ctrl+V・ドラッグ&ドロップ ──

    /// <summary>クリップボタン押下でファイル選択を開き、選択ファイルを添付へ取り込む</summary>
    private void Attachments_AttachRequested(object sender, RoutedEventArgs e) =>
        ViewModel.PickAndAddAttachments();

    /// <summary>
    /// 入力欄フォーカス時の Ctrl+V を捕捉し、クリップボードに画像があればチップ化する。
    /// 画像が無ければ何もせず（テキスト貼付の既定動作を妨げない）。
    /// </summary>
    private void UserInputBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.V || (Keyboard.Modifiers & ModifierKeys.Control) != ModifierKeys.Control)
        {
            return;
        }

        if (!ViewModel.Attachments.IsEnabled)
        {
            return;
        }

        var png = ClipboardImageAttachmentReader.TryGetClipboardPng();

        if (png is null)
        {
            return;
        }

        ViewModel.Attachments.AddClipboardImage(png, DateTime.Now);
        e.Handled = true;
    }

    /// <summary>
    /// チャット領域上のファイルドラッグをトンネル段で先取りして受け入れる。
    /// バブリング段だとメッセージバブル（コピー可能な TextBox）の組み込みドラッグ処理に
    /// 飲み込まれ、バブルの上へのドロップが効かなくなる。テキスト等のドラッグは既定動作に任せる。
    /// </summary>
    private void ChatArea_PreviewDragOver(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            return;
        }

        e.Effects = ViewModel.Attachments.IsEnabled ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    /// <summary>ドロップされたファイルを添付へ取り込む（非対応種別は VM がステータス通知する）</summary>
    private void ChatArea_PreviewDrop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            return;
        }

        if (e.Data.GetData(DataFormats.FileDrop) is string[] paths && paths.Length > 0)
        {
            ViewModel.AddDroppedFiles(paths);
        }

        e.Handled = true;
    }

    // ── 接続タブの切り替え・状態プローブ ──

    /// <summary>確定済み（切り替え済み）の接続タブのインデックス</summary>
    private int _committedBackendIndex;

    /// <summary>タブ選択の復帰中か（復帰による再入を無視するためのガード）</summary>
    private bool _revertingBackendTab;

    /// <summary>
    /// タブ選択を接続方式へ変換し、切替可否を ViewModel に委ねる（ターン実行中は拒まれる）。
    /// 切り替えたら確定インデックスを更新して必要ならプローブし、拒まれたら元のタブへ戻す。
    /// </summary>
    private async void BackendTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // 子 Selector（ComboBox 等）のバブリングや、復帰による再入は無視する
        if (!ReferenceEquals(e.OriginalSource, BackendTabs) || _revertingBackendTab)
        {
            return;
        }

        var newIndex = BackendTabs.SelectedIndex;

        if (newIndex == _committedBackendIndex)
        {
            return;
        }

        var backend = newIndex switch
        {
            1 => ErChatBackendKind.Codex,
            2 => ErChatBackendKind.ClaudeCode,
            3 => ErChatBackendKind.Copilot,
            _ => ErChatBackendKind.ApiKey,
        };

        if (!ViewModel.TryChangeBackend(backend))
        {
            RevertBackendSelection();
            return;
        }

        _committedBackendIndex = newIndex;

        if (backend == ErChatBackendKind.Codex)
        {
            await RunProbeAsync(EnsureCodexConnectedAsync);
        }
        else if (backend == ErChatBackendKind.ClaudeCode)
        {
            await RunProbeAsync(EnsureClaudeCodeInitializedAsync);
        }
        else if (backend == ErChatBackendKind.Copilot)
        {
            await RunProbeAsync(EnsureCopilotInitializedAsync);
        }
    }

    /// <summary>
    /// 確定済みタブへ選択を戻す。<see cref="Selector.SelectionChanged"/> 処理中の同期的な選択戻しは
    /// TabControl の選択状態を不整合にし、以降のクリックで再発火するため、イベント完了後にディスパッチャ経由で戻し、
    /// 戻し中の再入はガードで無視する（AI チャットのダイアログと同じ形）。
    /// </summary>
    private void RevertBackendSelection()
    {
        Dispatcher.BeginInvoke(
            new Action(() =>
            {
                _revertingBackendTab = true;
                try
                {
                    BackendTabs.SelectedIndex = _committedBackendIndex;
                }
                finally
                {
                    _revertingBackendTab = false;
                }
            })
        );
    }

    /// <summary>
    /// 接続状態の確認（プローブ）を実行し、失敗はステータスへ出す（例外をウィンドウの外へ漏らさない）。
    /// </summary>
    /// <remarks>
    /// 呼び出し元はタブ切替の <c>async void</c> ハンドラと「再確認」のコマンドで、どちらも例外を
    /// ディスパッチャへ投げ直す＝作業フォルダの作成失敗など 1 回の確認の失敗でアプリが落ちる。
    /// </remarks>
    /// <param name="probe">実行する確認</param>
    private async Task RunProbeAsync(Func<Task> probe)
    {
        try
        {
            await probe();
        }
        catch (Exception ex)
        {
            ViewModel.StatusMessage = string.Format(
                Strings.Mock_ConnectionCheckFailedFormat,
                ex.Message
            );
        }
    }

    /// <summary>Codex 接続を確立し、認証状態を ViewModel へ反映する</summary>
    private async Task EnsureCodexConnectedAsync()
    {
        _codexProbe.ModelProvider = ViewModel.Connection.CodexModelProvider;
        _codexProbe.Model = ViewModel.Connection.CodexModel;
        await _codexProbe.InitializeAsync();
    }

    /// <summary>Codex アカウント状態を取り直す（「再確認」）</summary>
    private async Task RefreshCodexAsync()
    {
        await _codexProbe.RefreshAccountStateAsync();
    }

    /// <summary>Codex 認証状態の変化を ViewModel へ反映する</summary>
    private void OnCodexAuthStateChanged(object? sender, CodexAuthState state)
    {
        var ready = _codexProbe.IsReady;
        // CLI 未検出はユーザー操作（インストール）が要るため、未接続の灰ではなく赤で出す
        var level =
            ready ? ConnectionHealth.Ready
            : state.IsCliMissing || state.IsStarted ? ConnectionHealth.NeedsAction
            : ConnectionHealth.Pending;
        Dispatcher.Invoke(() =>
            ViewModel.ApplyCodexReadiness(ready, state.AccountSummary, level, state.Guidance)
        );
    }

    /// <summary>Codex プローブのステータス通知（接続失敗など）を ViewModel へ反映する</summary>
    private void OnCodexStatusChanged(object? sender, string message) =>
        Dispatcher.Invoke(() => ViewModel.ApplyCodexStatusMessage(message));

    /// <summary>Claude Code を初期化し、状態を ViewModel へ反映する</summary>
    private async Task EnsureClaudeCodeInitializedAsync()
    {
        _claudeCodeProbe.Model = ViewModel.Connection.ClaudeCodeModel;
        await _claudeCodeProbe.InitializeAsync();
        ApplyClaudeCodeState();

        // Claude Code タブへ切り替えたら、第2ステップ（モックプロジェクト生成）の有効条件も取り直す
        await ViewModel.RefreshMockGenAvailabilityAsync();
    }

    /// <summary>Claude Code ログイン状態を取り直す（「再確認」）</summary>
    private async Task RefreshClaudeCodeAsync()
    {
        await _claudeCodeProbe.RefreshAsync();
    }

    /// <summary>Claude Code 状態変化を ViewModel へ反映する</summary>
    private void OnClaudeCodeStatusChanged(object? sender, EventArgs e) =>
        Dispatcher.Invoke(ApplyClaudeCodeState);

    /// <summary>Claude Code の現在状態を ViewModel へ反映する</summary>
    private void ApplyClaudeCodeState() =>
        ViewModel.ApplyClaudeCodeReadiness(
            _claudeCodeProbe.IsReady,
            _claudeCodeProbe.StatusSummary,
            _claudeCodeProbe.StatusLevel,
            _claudeCodeProbe.Guidance
        );

    /// <summary>GitHub Copilot を初期化（接続）し、状態とモデル一覧を ViewModel へ反映する</summary>
    private async Task EnsureCopilotInitializedAsync()
    {
        _copilotProbe.Model = ViewModel.Connection.CopilotModel;
        await _copilotProbe.InitializeAsync();
        ApplyCopilotState();
    }

    /// <summary>GitHub Copilot のログイン状態・モデル一覧を取り直す（「再確認」）</summary>
    private async Task RefreshCopilotAsync()
    {
        await _copilotProbe.RefreshAsync();
        ApplyCopilotState();
    }

    /// <summary>GitHub Copilot 状態変化を ViewModel へ反映する</summary>
    private void OnCopilotStatusChanged(object? sender, EventArgs e) =>
        Dispatcher.Invoke(ApplyCopilotState);

    /// <summary>GitHub Copilot の現在状態を ViewModel へ反映する</summary>
    private void ApplyCopilotState() =>
        ViewModel.ApplyCopilotReadiness(
            _copilotProbe.IsReady,
            _copilotProbe.StatusSummary,
            _copilotProbe.StatusLevel,
            _copilotProbe.Guidance,
            _copilotProbe.AvailableModels
        );
}
