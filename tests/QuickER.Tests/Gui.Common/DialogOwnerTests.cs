using System.IO;
using System.Text.RegularExpressions;
using AwesomeAssertions;
using QuickER.Tests.Resources;

namespace QuickER.Tests.Gui.Common;

/// <summary>
/// モーダルの親ウィンドウの解決を、2 つのダイアログサービスが共有していることを固定するテストクラス。
/// </summary>
/// <remarks>
/// <para>
/// オーナーを与えないモーダルは、モードレスで開いた機能ウィンドウ（AI モック生成など）の背面へ
/// 回り込んで見えなくなり、呼び出し元は応答待ちのまま止まる。ファイル選択も同じで、
/// 解決を 2 箇所に分けて持つと片方だけ直し忘れて同じ症状が戻る。
/// </para>
/// <para>
/// <b>解決そのものの挙動はここでは検証できない。</b>
/// <c>DialogOwner.Resolve</c> が読む <c>Application.Current.Windows</c> と <c>MainWindow</c> は
/// Application を作ったスレッドの持ち物で、テストは STA スレッドを使い捨てにするため、
/// 別スレッドから触ると例外になる（本番は UI スレッド 1 本なので起こらない）。
/// Z 順も実行時の挙動でテストからは守れない。したがって守れるのは
/// <b>「両者が同じ解決を通し、オーナーなしの表示が残っていないこと」</b>だけになる。
/// </para>
/// </remarks>
public class DialogOwnerTests
{
    /// <summary><c>src/QuickER.Gui.Common</c> 配下のファイルを読む</summary>
    private static string ReadSource(string fileName) =>
        File.ReadAllText(
            Path.Combine(
                NeutralResxFiles.FindRepositoryRoot(),
                "src",
                "QuickER.Gui.Common",
                fileName
            )
        );

    [Fact(DisplayName = "ファイル選択は共有のオーナー解決を通し、オーナーなしの表示を残さない")]
    public void FileDialogService_GoesThroughTheSharedOwnerResolution()
    {
        var source = ReadSource("WpfFileDialogService.cs");

        source.Should().Contain("DialogOwner.Resolve()");
        Regex
            .Matches(source, @"dialog\.ShowDialog\(\)\s*==")
            .Should()
            .BeEmpty("オーナーなしの表示が残っていないこと");
    }

    [Fact(DisplayName = "メッセージボックスも同じ解決を通す（自前の複製を持たない）")]
    public void MessageBoxDialogService_GoesThroughTheSharedOwnerResolution()
    {
        var source = ReadSource("MessageBoxDialogService.cs");

        source.Should().Contain("DialogOwner.Resolve()");
        source
            .Should()
            .NotContain(
                "private static Window? ResolveOwner",
                "解決を 2 箇所に分けて持つと片方だけ直し忘れる"
            );
    }
}
