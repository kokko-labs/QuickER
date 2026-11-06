using System.IO;
using System.Text.RegularExpressions;
using AwesomeAssertions;
using QuickER.Services;
using QuickER.Tests.Resources;

namespace QuickER.Tests.Gui.Services;

/// <summary>
/// メインウィンドウ終了時のモジュール後始末（<see cref="FeatureModuleShutdown"/>）の配線を検証するテストクラス。
/// </summary>
/// <remarks>
/// 全モジュールへの通知・1 つの失敗が残りを止めない・1 回だけ実行の行は OS との境界のエンジンのアプリの一生の表
/// （<c>Gui/Services/Lifecycle/LifecycleRows.Shutdown.cs</c>）へ移した。ここに残すのは App.xaml.cs の配線のソースの検査（P3-12 の受け持ち）。
/// </remarks>
public class FeatureModuleShutdownTests
{
    /// <summary>
    /// <c>App.xaml.cs</c> の終了処理が、受け止めを持つ <see cref="FeatureModuleShutdown"/> を
    /// 経由していることをソース上で固定する。
    /// </summary>
    /// <remarks>
    /// <c>App</c> は WPF の <c>Application</c> 派生でテストから実体化できないため、ループを切り出した
    /// 共有ヘルパー側を直接検証する。その形が意味を持つのは <c>App</c> が確かにそれを使っているときだけなので、
    /// 素のループ（<c>OnMainWindowClosing</c> の直接呼び出し）へ戻っていないことをここで見張る。
    /// </remarks>
    [Fact(DisplayName = "App の終了処理は FeatureModuleShutdown を経由する")]
    public void App_UsesFeatureModuleShutdown()
    {
        var source = File.ReadAllText(
            Path.Combine(NeutralResxFiles.FindRepositoryRoot(), "src", "QuickER.Gui", "App.xaml.cs")
        );

        source.Should().Contain("new FeatureModuleShutdownOnce(");
        source.Should().Contain("window.Closing += (_, _) => featureModuleShutdown.CloseAll();");
        Regex
            .Matches(source, @"\.OnMainWindowClosing\s*\(")
            .Should()
            .BeEmpty("受け止めを持たない直接呼び出しへ戻っていないこと");
    }
}
