using System.IO;
using System.Text.RegularExpressions;
using AwesomeAssertions;
using QuickER.Tests.Resources;

namespace QuickER.Tests.Gui;

/// <summary>
/// アプリの終了条件（<c>ShutdownMode</c>）を固定するテストクラス。
/// </summary>
/// <remarks>
/// <para>
/// AI チャットとモック生成のウィンドウは × で閉じずに <c>Hide</c> するため、終了処理の
/// クローズ段が失敗して閉じ残ると、既定の <c>OnLastWindowClose</c> では
/// <b>見えるウィンドウが 1 つも無いままプロセスが残る</b>（タスクマネージャーでしか終われない）。
/// メインウィンドウが閉じた時点で終了させ、残ったウィンドウは <c>Application.Shutdown</c> に
/// 閉じさせる（このとき各ウィンドウのキャンセルは無視される）。
/// </para>
/// <para>
/// <c>App</c> は WPF の <c>Application</c> 派生で、テストプロセスにはすでに別の
/// <c>Application</c>（<see cref="TestSupport.WpfApplicationTestSupport"/>）が居るため実体化できない。
/// そのため <c>App.xaml</c> の宣言をソース上で固定し、あわせてコード側で別の値へ
/// 上書きしていないことも見張る（宣言だけ正しくても実行時に変えられては意味がない）。
/// </para>
/// </remarks>
public class AppShutdownModeTests
{
    /// <summary><c>src/QuickER.Gui</c> 配下のファイルを読む</summary>
    private static string ReadGuiFile(string fileName) =>
        File.ReadAllText(
            Path.Combine(NeutralResxFiles.FindRepositoryRoot(), "src", "QuickER.Gui", fileName)
        );

    [Fact(DisplayName = "App: メインウィンドウが閉じたらアプリを終了する（閉じ残りで居座らない）")]
    public void App_DeclaresShutdownModeOnMainWindowClose()
    {
        ReadGuiFile("App.xaml").Should().Contain("ShutdownMode=\"OnMainWindowClose\"");
    }

    [Fact(DisplayName = "App: 終了条件をコード側で別の値へ上書きしない")]
    public void App_DoesNotOverrideShutdownModeInCode()
    {
        Regex
            .Matches(ReadGuiFile("App.xaml.cs"), @"ShutdownMode\s*=")
            .Should()
            .BeEmpty("終了条件の正本は App.xaml の宣言 1 箇所");
    }

    /// <summary>メインウィンドウを <c>Application.MainWindow</c> へ明示していることを固定する</summary>
    /// <remarks>
    /// <c>OnMainWindowClose</c> が見るのは <c>Application.MainWindow</c> で、WPF は最初に構築された
    /// <c>Window</c> を自動で入れる。モジュールの初期化中に別の <c>Window</c> 派生（詳細ダイアログ等）が
    /// 先に作られると、そのダイアログを閉じた瞬間にアプリが終わる。明示の代入でこの前提を消す。
    /// </remarks>
    [Fact(DisplayName = "App: メインウィンドウを Application.MainWindow へ明示する")]
    public void App_AssignsMainWindowExplicitly()
    {
        Regex
            .Matches(ReadGuiFile("App.xaml.cs"), @"\bMainWindow\s*=\s*window\s*;")
            .Should()
            .ContainSingle("終了条件は最初に構築された Window という暗黙の前提に乗らない");
    }
}
