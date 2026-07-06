using System.Windows;
using System.Windows.Controls;
using Microsoft.Web.WebView2.Core;

namespace QuickER.Views;

/// <summary>
/// 生成されたモック HTML を WebView2 で表示するプレビューパネル。
/// </summary>
/// <remarks>
/// セキュリティのため、自ファイル以外への遷移・新規ウィンドウ・DevTools を遮断する
/// （ページ内アンカー遷移 <c>#fragment</c> は許可）。WebView2 ランタイム未導入時は
/// 初期化失敗を捕捉し、案内文へフォールバックしてアプリ全体は落とさない。
/// </remarks>
public partial class MockPreviewPanel : UserControl
{
    /// <summary>WebView2 の初期化が完了したか</summary>
    private bool _webViewReady;

    /// <summary>初期化失敗などでプレビューを表示できない状態か</summary>
    private bool _unavailable;

    /// <summary>初期化完了前に Navigate を要求された場合の保留 URI</summary>
    private Uri? _pendingNavigation;

    /// <summary>現在表示中（許可済み）のプレビューファイル URI</summary>
    private Uri? _currentSource;

    /// <summary>プレビューパネルを生成する</summary>
    public MockPreviewPanel()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    /// <summary>初回表示時に WebView2 を初期化する（ランタイム未導入時は案内へフォールバック）</summary>
    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoaded;
        await EnsureWebViewAsync();
    }

    /// <summary>WebView2 のコアを初期化し、セキュリティ設定とイベントハンドラを構成する</summary>
    private async Task EnsureWebViewAsync()
    {
        if (_webViewReady || _unavailable)
        {
            return;
        }

        try
        {
            await WebView.EnsureCoreWebView2Async();
        }
        catch (Exception)
        {
            // ランタイム未導入・初期化失敗時は案内へフォールバックする（アプリは落とさない）
            ShowUnavailable();
            return;
        }

        var core = WebView.CoreWebView2;
        core.Settings.AreDevToolsEnabled = false;
        core.Settings.AreDefaultContextMenusEnabled = true;
        core.Settings.IsStatusBarEnabled = false;

        // 自ファイル以外（http/https 等の外部遷移）を遮断する
        core.NavigationStarting += OnNavigationStarting;
        // iframe 内の遷移も同じ方針で遮断する（外部リソースの埋め込み読込を防ぐ）
        core.FrameNavigationStarting += OnNavigationStarting;
        // 新規ウィンドウ要求（target=_blank・window.open）を遮断する
        core.NewWindowRequested += OnNewWindowRequested;

        _webViewReady = true;

        // 初期化前に要求されていた Navigate があれば反映する
        if (_pendingNavigation is { } pending)
        {
            _pendingNavigation = null;
            NavigateCore(pending);
        }
    }

    /// <summary>プレビューする HTML ファイルの URI を表示する（初期化前なら保留し、完了後に反映する）</summary>
    /// <param name="fileUri">表示する <c>file:///</c> URI</param>
    public void Navigate(Uri fileUri)
    {
        if (_unavailable)
        {
            return;
        }

        if (!_webViewReady)
        {
            _pendingNavigation = fileUri;
            _ = EnsureWebViewAsync();
            return;
        }

        NavigateCore(fileUri);
    }

    /// <summary>WebView2 を実際に指定 URI へ遷移させ、空表示から切り替える</summary>
    private void NavigateCore(Uri fileUri)
    {
        _currentSource = fileUri;
        EmptyPlaceholder.Visibility = Visibility.Collapsed;
        WebView.Visibility = Visibility.Visible;
        WebView.CoreWebView2.Navigate(fileUri.AbsoluteUri);
    }

    /// <summary>
    /// 自ファイル（現在のプレビューファイル）またはページ内アンカー遷移のみを許可し、
    /// 外部（http/https 等）への遷移はキャンセルする。
    /// </summary>
    private void OnNavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
    {
        if (_currentSource is null)
        {
            return;
        }

        if (!Uri.TryCreate(e.Uri, UriKind.Absolute, out var target))
        {
            e.Cancel = true;
            return;
        }

        // 自ファイルへの遷移（初回ロード・ページ内 #fragment を含む）は許可する。
        // それ以外（外部サイト等）はキャンセルして遮断する。
        var isSameFile =
            target.IsFile
            && string.Equals(
                target.LocalPath,
                _currentSource.LocalPath,
                StringComparison.OrdinalIgnoreCase
            );

        if (!isSameFile)
        {
            e.Cancel = true;
        }
    }

    /// <summary>新規ウィンドウ要求を常にキャンセルする（別ウィンドウでの外部遷移を防ぐ）</summary>
    private void OnNewWindowRequested(object? sender, CoreWebView2NewWindowRequestedEventArgs e)
    {
        e.Handled = true;
    }

    /// <summary>プレビュー不可の案内を表示する（ランタイム未導入時など）</summary>
    private void ShowUnavailable()
    {
        _unavailable = true;
        WebView.Visibility = Visibility.Collapsed;
        EmptyPlaceholder.Visibility = Visibility.Collapsed;
        UnavailablePanel.Visibility = Visibility.Visible;
    }

    /// <summary>プレビューを表示できる状態か（テスト・呼び出し側の判定用）</summary>
    public bool IsPreviewAvailable => !_unavailable;
}
