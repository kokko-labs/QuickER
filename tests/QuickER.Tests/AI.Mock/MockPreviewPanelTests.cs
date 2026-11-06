using System.IO;
using AwesomeAssertions;
using QuickER.AI.Mock;
using QuickER.Tests.TestSupport;

namespace QuickER.Tests.AI.Mock;

/// <summary>
/// <see cref="MockPreviewPanel"/> のパネルの状態（初期化を 1 回にまとめること・iframe の遷移で現在ファイルを
/// 変えないこと）を、初期化の差し替え口で WebView2 の実ランタイムを使わずに検証するテストクラス。
/// </summary>
/// <remarks>
/// 遷移・サブリソースの許可の判定の行は OS との境界のエンジンのパスの入口の表（<c>Platform/OsBoundary/PathEntranceRows*.cs</c>）へ移した。
/// ここに残すのは初期化と iframe の遷移の状態。
/// </remarks>
public class MockPreviewPanelTests
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "QuickERMockRoot");

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
