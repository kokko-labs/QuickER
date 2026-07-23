using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Web.WebView2.Core;

namespace QuickER.AI.Mock;

/// <summary>
/// 生成されたモック画面（フォルダ内の実 HTML ファイル）を WebView2 で表示するプレビューパネル。
/// </summary>
/// <remarks>
/// セキュリティのため、許可されたモックフォルダ直下の <c>*.html</c> ファイルへの遷移のみを許可し、
/// それ以外（外部サイト・フォルダ外・新規ウィンドウ・DevTools）は遮断する
/// （ページ内アンカー遷移 <c>#fragment</c> は許可＝同一ファイル扱い）。ページ内リンクで別画面へ
/// 遷移したら <see cref="CurrentFileChanged"/> で通知する。WebView2 ランタイム未導入時は
/// 初期化失敗を捕捉し、案内文へフォールバックしてアプリ全体は落とさない。
/// </remarks>
public partial class MockPreviewPanel : UserControl
{
    /// <summary>WebView2 の初期化が完了したか</summary>
    private bool _webViewReady;

    /// <summary>初期化失敗などでプレビューを表示できない状態か</summary>
    private bool _unavailable;

    /// <summary>初期化完了前に Navigate を要求された場合の保留（URI・許可フォルダ）</summary>
    private (Uri Uri, string Folder)? _pendingNavigation;

    /// <summary>現在表示中（許可済み）のプレビューファイル URI</summary>
    private Uri? _currentSource;

    /// <summary>遷移を許可するルートフォルダ（このフォルダ直下の <c>*.html</c> のみ許可する）</summary>
    private string? _allowedRootFolder;

    /// <summary>現在表示中の画面ファイル名（変化時に <see cref="CurrentFileChanged"/> を発火する）</summary>
    private string? _currentFileName;

    /// <summary>プレビューが表示している画面ファイルが変わったときに発火する（引数はファイル名）</summary>
    public event EventHandler<string>? CurrentFileChanged;

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

        // 許可フォルダ直下の *.html 以外（http/https 等の外部遷移・フォルダ外）を遮断する
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
            _allowedRootFolder = pending.Folder;
            NavigateCore(pending.Uri);
        }
    }

    /// <summary>
    /// プレビューする画面 HTML の URI を、許可ルートフォルダ付きで表示する（初期化前なら保留し、完了後に反映する）。
    /// </summary>
    /// <param name="fileUri">表示する <c>file:///</c> URI</param>
    /// <param name="allowedRootFolder">画面間リンク遷移を許可するルートフォルダ</param>
    public void Navigate(Uri fileUri, string allowedRootFolder)
    {
        if (_unavailable)
        {
            return;
        }

        _allowedRootFolder = allowedRootFolder;

        if (!_webViewReady)
        {
            _pendingNavigation = (fileUri, allowedRootFolder);
            _ = EnsureWebViewAsync();
            return;
        }

        NavigateCore(fileUri);
    }

    /// <summary>プレビューを空表示（プレースホルダ）へ戻す（フォルダ未指定・全画面削除時）</summary>
    public void ShowEmpty()
    {
        if (_unavailable)
        {
            return;
        }

        _currentSource = null;
        _currentFileName = null;
        WebView.Visibility = Visibility.Collapsed;
        EmptyPlaceholder.Visibility = Visibility.Visible;
    }

    /// <summary>
    /// WebView2 を実際に指定 URI へ遷移させる。同一ファイルの再表示（内容更新後）は
    /// <see cref="CoreWebView2.Reload"/> でブラウザキャッシュを避けて最新内容を表示する。
    /// </summary>
    private void NavigateCore(Uri fileUri)
    {
        var sameFile =
            _currentSource is not null
            && string.Equals(
                _currentSource.LocalPath,
                fileUri.LocalPath,
                StringComparison.OrdinalIgnoreCase
            );

        _currentSource = fileUri;
        EmptyPlaceholder.Visibility = Visibility.Collapsed;
        WebView.Visibility = Visibility.Visible;

        if (sameFile)
        {
            WebView.CoreWebView2.Reload();
        }
        else
        {
            WebView.CoreWebView2.Navigate(fileUri.AbsoluteUri);
        }

        SetCurrentFile(Path.GetFileName(fileUri.LocalPath));
    }

    /// <summary>現在ファイル名を更新し、変化していれば <see cref="CurrentFileChanged"/> を発火する</summary>
    private void SetCurrentFile(string? fileName)
    {
        if (string.Equals(_currentFileName, fileName, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _currentFileName = fileName;

        if (!string.IsNullOrEmpty(fileName))
        {
            CurrentFileChanged?.Invoke(this, fileName);
        }
    }

    /// <summary>
    /// 許可フォルダ直下の <c>*.html</c>（<c>#fragment</c> を含む同一ファイル）への遷移のみを許可し、
    /// 外部（http/https 等）・フォルダ外への遷移はキャンセルする。
    /// ページ内リンクで別画面へ遷移したときは現在ファイルを追跡する。
    /// </summary>
    private void OnNavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
    {
        // まだ許可フォルダが未設定（WebView2 初期化直後の about:blank 等）は素通しする
        if (string.IsNullOrEmpty(_allowedRootFolder))
        {
            return;
        }

        if (!Uri.TryCreate(e.Uri, UriKind.Absolute, out var target))
        {
            e.Cancel = true;
            return;
        }

        if (!IsNavigationAllowed(target, _allowedRootFolder))
        {
            e.Cancel = true;
            return;
        }

        // 許可された遷移: ページ内リンクで別ファイルへ移ったら現在ファイルを追跡する
        _currentSource = target;
        SetCurrentFile(Path.GetFileName(target.LocalPath));
    }

    /// <summary>
    /// 遷移先が許可ルートフォルダ直下の <c>*.html</c> ファイルかどうかを判定する（純粋関数・単体テスト用）。
    /// </summary>
    /// <param name="target">遷移先 URI</param>
    /// <param name="allowedRootFolder">許可するルートフォルダ</param>
    /// <returns>許可フォルダ直下の <c>.html</c> ファイル URI なら true（それ以外・フォルダ外・<c>..</c>・http(s) は false）</returns>
    internal static bool IsNavigationAllowed(Uri? target, string? allowedRootFolder)
    {
        if (target is null || !target.IsFile)
        {
            return false;
        }

        if (string.IsNullOrEmpty(allowedRootFolder))
        {
            return false;
        }

        string path;

        try
        {
            path = target.LocalPath;
        }
        catch (Exception)
        {
            return false;
        }

        if (!path.EndsWith(".html", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        try
        {
            // `..` を含む相対経路もフルパス化で正規化され、フォルダ外なら親フォルダが一致しない
            var directory = Path.GetDirectoryName(Path.GetFullPath(path));

            if (directory is null)
            {
                return false;
            }

            var root = Path.GetFullPath(allowedRootFolder);

            return string.Equals(
                Path.TrimEndingDirectorySeparator(directory),
                Path.TrimEndingDirectorySeparator(root),
                StringComparison.OrdinalIgnoreCase
            );
        }
        catch (Exception)
        {
            return false;
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
