using System.IO;
using AwesomeAssertions;
using QuickER.AI.Mock;
using QuickER.Tests.TestSupport;

namespace QuickER.Tests.AI.Mock;

/// <summary>
/// <see cref="MockPreviewPanel.IsNavigationAllowed"/>（プレビューの遷移許可判定・純粋関数）と
/// <see cref="MockPreviewPanel.IsResourceAllowed"/>（サブリソース許可判定・純粋関数）を検証するテストクラス。
/// 遷移は許可フォルダ直下の <c>*.html</c>（#fragment を含む同一ファイル）のみ許可し、フォルダ外・<c>..</c>・http(s)・
/// サブフォルダ・非 HTML は拒否する。リソースは共有 CSS・画像を通すため拡張子を問わず許可フォルダ内なら許可し、
/// それ以外は既定拒否する。あわせて、パネルの状態（初期化を 1 回にまとめること・iframe の遷移で現在ファイルを
/// 変えないこと）を、初期化の差し替え口で WebView2 の実ランタイムを使わずに検証する。
/// </summary>
public class MockPreviewPanelTests
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "QuickERMockRoot");

    /// <summary>許可フォルダ直下の *.html は許可される</summary>
    [Fact(DisplayName = "許可フォルダ直下の *.html は許可")]
    public void FileDirectlyUnderFolder_IsAllowed()
    {
        var target = new Uri(Path.Combine(Root, "OrderList.html"));

        MockPreviewPanel.IsNavigationAllowed(target, Root).Should().BeTrue();
    }

    /// <summary>#fragment 付き（同一ファイル）の遷移は許可される</summary>
    [Fact(DisplayName = "#fragment 付き同一ファイルは許可")]
    public void FragmentNavigation_IsAllowed()
    {
        var baseUri = new Uri(Path.Combine(Root, "OrderList.html"));
        var target = new Uri(baseUri.AbsoluteUri + "#section");

        MockPreviewPanel.IsNavigationAllowed(target, Root).Should().BeTrue();
    }

    /// <summary>サブフォルダ配下（直下でない）は拒否される</summary>
    [Fact(DisplayName = "サブフォルダ配下は拒否")]
    public void FileInSubFolder_IsRejected()
    {
        var target = new Uri(Path.Combine(Root, "sub", "OrderList.html"));

        MockPreviewPanel.IsNavigationAllowed(target, Root).Should().BeFalse();
    }

    /// <summary>フォルダ外（親フォルダ）は拒否される</summary>
    [Fact(DisplayName = "フォルダ外は拒否")]
    public void FileOutsideFolder_IsRejected()
    {
        var parent = Directory.GetParent(Root)!.FullName;
        var target = new Uri(Path.Combine(parent, "Secret.html"));

        MockPreviewPanel.IsNavigationAllowed(target, Root).Should().BeFalse();
    }

    /// <summary><c>..</c> でフォルダ外へ抜ける遷移は拒否される</summary>
    [Fact(DisplayName = ".. でフォルダ外へ抜ける遷移は拒否")]
    public void ParentTraversal_IsRejected()
    {
        var baseUri = new Uri(Path.Combine(Root, "index.html"));
        var target = new Uri(baseUri, "../Secret.html");

        MockPreviewPanel.IsNavigationAllowed(target, Root).Should().BeFalse();
    }

    /// <summary>http(s) など file 以外は拒否される</summary>
    [Theory(DisplayName = "http(s) など file 以外は拒否")]
    [InlineData("https://example.com/OrderList.html")]
    [InlineData("http://example.com/x.html")]
    public void NonFileScheme_IsRejected(string url)
    {
        MockPreviewPanel.IsNavigationAllowed(new Uri(url), Root).Should().BeFalse();
    }

    /// <summary>非 HTML ファイルは拒否される</summary>
    [Fact(DisplayName = "非 HTML ファイルは拒否")]
    public void NonHtmlFile_IsRejected()
    {
        var target = new Uri(Path.Combine(Root, "data.txt"));

        MockPreviewPanel.IsNavigationAllowed(target, Root).Should().BeFalse();
    }

    /// <summary>許可フォルダが空なら拒否される</summary>
    [Fact(DisplayName = "許可フォルダが空なら拒否")]
    public void EmptyAllowedRootFolder_IsRejected()
    {
        var target = new Uri(Path.Combine(Root, "OrderList.html"));

        MockPreviewPanel.IsNavigationAllowed(target, string.Empty).Should().BeFalse();
        MockPreviewPanel.IsNavigationAllowed(target, null).Should().BeFalse();
    }

    /// <summary>許可フォルダ内のファイルは拡張子を問わず許可される（共有 CSS・画像を通すため）</summary>
    [Theory(DisplayName = "リソース: 許可フォルダ内は拡張子を問わず許可")]
    [InlineData("style.css")]
    [InlineData("OrderList.html")]
    [InlineData("logo.png")]
    [InlineData("app.js")]
    public void Resource_FileInFolder_IsAllowed(string fileName)
    {
        var target = new Uri(Path.Combine(Root, fileName));

        MockPreviewPanel.IsResourceAllowed(target, Root).Should().BeTrue();
    }

    /// <summary>file 以外のスキーム（http/https/ws）は拒否される</summary>
    [Theory(DisplayName = "リソース: file 以外のスキームは拒否")]
    [InlineData("https://example.com/x.png")]
    [InlineData("http://example.com/app.js")]
    [InlineData("ws://example.com/socket")]
    [InlineData("wss://example.com/socket")]
    public void Resource_NonFileScheme_IsRejected(string url)
    {
        MockPreviewPanel.IsResourceAllowed(new Uri(url), Root).Should().BeFalse();
    }

    /// <summary>フォルダ外・サブフォルダのファイルは拒否される</summary>
    [Fact(DisplayName = "リソース: フォルダ外・サブフォルダは拒否")]
    public void Resource_OutsideFolder_IsRejected()
    {
        var parent = Directory.GetParent(Root)!.FullName;

        MockPreviewPanel
            .IsResourceAllowed(new Uri(Path.Combine(parent, "secret.css")), Root)
            .Should()
            .BeFalse();

        MockPreviewPanel
            .IsResourceAllowed(new Uri(Path.Combine(Root, "sub", "style.css")), Root)
            .Should()
            .BeFalse();
    }

    /// <summary><c>..</c> でフォルダ外を指すリソースは拒否される</summary>
    [Fact(DisplayName = "リソース: .. でフォルダ外を指すファイルは拒否")]
    public void Resource_ParentTraversal_IsRejected()
    {
        var baseUri = new Uri(Path.Combine(Root, "index.html"));
        var target = new Uri(baseUri, "../secret.css");

        MockPreviewPanel.IsResourceAllowed(target, Root).Should().BeFalse();
    }

    /// <summary>許可フォルダ未設定なら既定拒否される</summary>
    [Fact(DisplayName = "リソース: 許可フォルダ未設定なら既定拒否")]
    public void Resource_EmptyAllowedRootFolder_IsRejected()
    {
        var target = new Uri(Path.Combine(Root, "style.css"));

        MockPreviewPanel.IsResourceAllowed(target, string.Empty).Should().BeFalse();
        MockPreviewPanel.IsResourceAllowed(target, null).Should().BeFalse();
        MockPreviewPanel.IsResourceAllowed(null, Root).Should().BeFalse();
    }

    // --- パネルの状態（STA・実ランタイムを使わない） --------------------------

    /// <summary>
    /// 初期化を完了させない差し替えでパネルを作り、許可フォルダを設定した状態にする
    /// （<see cref="MockPreviewPanel.Navigate"/> は初期化前なら保留して初期化を始めるだけ）。
    /// </summary>
    private static (MockPreviewPanel Panel, Func<int> InitializeCount) CreatePendingPanel(Uri first)
    {
        WpfApplicationTestSupport.EnsureApplicationResources();
        var panel = WpfApplicationTestSupport.LoadXamlComponent(() => new MockPreviewPanel());

        var count = 0;
        var never = new TaskCompletionSource();
        panel.CoreWebViewInitializer = () =>
        {
            count++;
            return never.Task;
        };

        panel.Navigate(first, Root);

        return (panel, () => count);
    }

    /// <summary>
    /// Loaded より前の Navigate と Loaded の両方から初期化を求められても、コアの初期化（＝ハンドラと
    /// フィルタの登録）は 1 回だけ走ることを検証する
    /// </summary>
    [Fact(DisplayName = "初期化を重ねて求めても WebView2 の初期化は 1 回だけ走る")]
    public void EnsureWebView_RunsInitializationOnce()
    {
        WpfApplicationTestSupport.RunSta(() =>
        {
            var (panel, initializeCount) = CreatePendingPanel(
                new Uri(Path.Combine(Root, "A.html"))
            );

            var first = panel.EnsureWebViewAsync();
            var second = panel.EnsureWebViewAsync();

            initializeCount().Should().Be(1);
            second.Should().BeSameAs(first);
        });
    }

    /// <summary>
    /// iframe の許可された遷移では現在ファイルと通知が変わらず、最上位フレームの遷移では変わることを検証する
    /// </summary>
    [Fact(
        DisplayName = "iframe の遷移は現在ファイルを変えず、最上位フレームの遷移だけが追跡される"
    )]
    public void FrameNavigation_DoesNotTrackCurrentFile()
    {
        WpfApplicationTestSupport.RunSta(() =>
        {
            var (panel, _) = CreatePendingPanel(new Uri(Path.Combine(Root, "A.html")));
            var changes = new List<string>();
            panel.CurrentFileChanged += (_, file) => changes.Add(file);

            var frameTarget = new Uri(Path.Combine(Root, "B.html")).AbsoluteUri;

            // iframe：許可（キャンセルしない）が、現在ファイルは変えない
            panel.HandleNavigationStarting(frameTarget, isTopLevel: false).Should().BeFalse();
            changes.Should().BeEmpty();
            panel.CurrentSource.Should().BeNull();

            // iframe でもフォルダ外は遮断する
            panel
                .HandleNavigationStarting("https://example.com/", isTopLevel: false)
                .Should()
                .BeTrue();

            // 最上位フレーム：許可し、現在ファイルを追跡する
            panel.HandleNavigationStarting(frameTarget, isTopLevel: true).Should().BeFalse();
            changes.Should().Equal("B.html");
            panel.CurrentSource!.AbsoluteUri.Should().Be(frameTarget);
        });
    }
}
