using System.Windows;
using System.Windows.Controls;
using CommunityToolkit.Mvvm.Input;
using QuickER.AI;
using QuickER.Services.Chat;
using QuickER.ViewModels;

namespace QuickER.Views;

/// <summary>
/// AI モック生成ダイアログ（左チャット／右 HTML プレビュー）のコードビハインド。
/// </summary>
/// <remarks>
/// 接続方式ごとの認証状態プローブ（Codex / Claude）はここで軽量エンジンを保持して確認し、
/// 結果を ViewModel へ反映する。生成に使うエンジンは ViewModel が生成開始時に別途構築する。
/// HtmlUpdated 通知を受けて一時ファイルへ書き出し、WebView2 プレビューへ Navigate する。
/// </remarks>
public partial class MockGenerationDialog : Window
{
    /// <summary>このウィンドウの ViewModel</summary>
    public MockGenerationDialogViewModel ViewModel { get; }

    /// <summary>プレビュー用一時ファイルストア</summary>
    private readonly Services.MockPreviewFileStore _previewStore = new();

    /// <summary>Codex 認証状態のプローブ用エンジン（ツールホスト無し）</summary>
    private readonly CodexChatEngine? _codexProbe;

    /// <summary>Claude Code ログイン状態のプローブ用エンジン（ツールホスト無し）</summary>
    private readonly ClaudeCodeChatEngine _claudeCodeProbe;

    /// <summary>アプリ終了などで強制クローズ中かどうか</summary>
    private bool _isForceClosing;

    /// <summary>「再確認」（Codex）コマンド</summary>
    public IAsyncRelayCommand CodexRefreshCommand { get; }

    /// <summary>「再確認」（Claude）コマンド</summary>
    public IAsyncRelayCommand ClaudeCodeRefreshCommand { get; }

    /// <summary>注入された ViewModel を結び付けてウィンドウを生成する</summary>
    public MockGenerationDialog(MockGenerationDialogViewModel viewModel)
    {
        InitializeComponent();
        ViewModel = viewModel;
        DataContext = ViewModel;

        var dispatcher = new WpfUiDispatcher();
        _codexProbe = new CodexChatEngine(new CodexAppServerClient(), toolHost: null, dispatcher);
        _codexProbe.AuthStateChanged += OnCodexAuthStateChanged;
        _claudeCodeProbe = new ClaudeCodeChatEngine(
            new ClaudeCodeProcessClient(),
            toolHost: null,
            dispatcher
        );
        _claudeCodeProbe.StatusSummaryChanged += OnClaudeCodeStatusChanged;

        CodexRefreshCommand = new AsyncRelayCommand(RefreshCodexAsync);
        ClaudeCodeRefreshCommand = new AsyncRelayCommand(RefreshClaudeCodeAsync);

        ViewModel.HtmlUpdated += OnHtmlUpdated;
        ViewModel.Messages.CollectionChanged += (_, _) => ScrollToBottom();
        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
        Loaded += OnLoaded;
        Closing += OnWindowClosing;
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

    /// <summary>初回表示時に ViewModel を初期化する</summary>
    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoaded;
        ViewModel.Initialize();
    }

    /// <summary>×ボタンでは閉じず、設定を保存して非表示にし状態を維持する（シングルトン動作）</summary>
    private void OnWindowClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_isForceClosing)
        {
            return;
        }

        e.Cancel = true;
        ViewModel.SaveSettings();
        Hide();
    }

    /// <summary>アプリ終了時などにウィンドウを実際に閉じる</summary>
    public void ForceClose()
    {
        _isForceClosing = true;
        ViewModel.SaveSettings();
        Close();
    }

    /// <summary>HTML 更新を受けて一時ファイルへ書き出し、プレビューへ反映する</summary>
    private void OnHtmlUpdated(object? sender, MockHtmlUpdate update)
    {
        try
        {
            var uri = _previewStore.Write(update.Html);
            Preview.Navigate(uri);
        }
        catch (Exception ex)
        {
            ViewModel.StatusMessage = $"プレビューを更新できませんでした: {ex.Message}";
        }
    }

    /// <summary>チャット表示を最下部へスクロールする</summary>
    private void ScrollToBottom()
    {
        Dispatcher.InvokeAsync(
            () => ChatScrollViewer?.ScrollToEnd(),
            System.Windows.Threading.DispatcherPriority.Background
        );
    }

    // ── 接続タブの切り替え・状態プローブ ──

    /// <summary>確定済み（切り替え済み）の接続タブのインデックス</summary>
    private int _committedBackendIndex;

    /// <summary>タブ選択を接続方式へ変換して ViewModel に反映し、必要ならプローブする</summary>
    private async void BackendTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!ReferenceEquals(e.OriginalSource, BackendTabs))
        {
            return;
        }

        var newIndex = BackendTabs.SelectedIndex;

        if (newIndex == _committedBackendIndex)
        {
            return;
        }

        _committedBackendIndex = newIndex;
        var backend = newIndex switch
        {
            1 => ErChatBackendKind.Codex,
            2 => ErChatBackendKind.ClaudeCode,
            _ => ErChatBackendKind.ApiKey,
        };
        ViewModel.SelectedBackend = backend;

        if (backend == ErChatBackendKind.Codex)
        {
            await EnsureCodexConnectedAsync();
        }
        else if (backend == ErChatBackendKind.ClaudeCode)
        {
            await EnsureClaudeCodeInitializedAsync();
        }
    }

    /// <summary>Codex 接続を確立し、認証状態を ViewModel へ反映する</summary>
    private async Task EnsureCodexConnectedAsync()
    {
        if (_codexProbe is null)
        {
            return;
        }

        _codexProbe.ModelProvider = ViewModel.CodexModelProvider;
        _codexProbe.Model = ViewModel.CodexModel;
        await _codexProbe.InitializeAsync();
    }

    /// <summary>Codex アカウント状態を取り直す（「再確認」）</summary>
    private async Task RefreshCodexAsync()
    {
        if (_codexProbe is not null)
        {
            await _codexProbe.RefreshAccountStateAsync();
        }
    }

    /// <summary>Codex 認証状態の変化を ViewModel へ反映する</summary>
    private void OnCodexAuthStateChanged(object? sender, CodexAuthState state)
    {
        if (_codexProbe is null)
        {
            return;
        }

        var ready = _codexProbe.IsReady;
        var level =
            ready ? ConnectionHealth.Ready
            : state.IsStarted ? ConnectionHealth.NeedsAction
            : ConnectionHealth.Pending;
        Dispatcher.Invoke(() => ViewModel.ApplyCodexReadiness(ready, state.AccountSummary, level));
    }

    /// <summary>Claude Code を初期化し、状態を ViewModel へ反映する</summary>
    private async Task EnsureClaudeCodeInitializedAsync()
    {
        _claudeCodeProbe.Model = ViewModel.ClaudeCodeModel;
        await _claudeCodeProbe.InitializeAsync();
        ApplyClaudeCodeState();

        // Claude Code タブへ切り替えたら、第2ステップ（WPF モック生成）の有効条件も取り直す
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
}
