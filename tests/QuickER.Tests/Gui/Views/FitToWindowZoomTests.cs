using AwesomeAssertions;
using QuickER.Model;
using QuickER.Services;
using QuickER.ViewModels;
using static QuickER.Tests.TestSupport.WpfApplicationTestSupport;

namespace QuickER.Tests.Gui.Views;

/// <summary>
/// 図がビューポートに収まらないときの表示倍率を、実ウィンドウで検証するテストクラス。
/// </summary>
/// <remarks>
/// <para>
/// fit の要求は経路に依らず同じ <see cref="MainViewModel.FitToWindowRequested"/> で View へ届き、
/// 倍率の下限だけが違う。どちらの下限を渡すかはコードビハインドの 1 行なので、VM 単体テストでは守れない。
/// </para>
/// <para>
/// 線引きは「利用者が現在の図を並べ直した（明示の「全体を表示」・整列）＝全体を見せる」対
/// 「図が新しく現れた（開く・取込・DB 取込・AI 生成）＝まず読める大きさ（等倍）で出す」。
/// 区別を落とすと「全体を表示」が効かなくなるか、図を開いた直後に縮んで文字が読めなくなるかの
/// どちらかが静かに起きる（倍率は画面を見ないと分からず、例外にもならない）。
/// </para>
/// <para>
/// ウィンドウ幅を狭めすぎないこと。左右のパネルは固定幅（200 + スプリッタ 5 + 560）を占めるため、
/// 幅がそれを下回るとキャンバス列が 0 幅になり、fit は「ビューポートなし」として等倍を返す
/// （＝何も検証していないのに緑になる）。
/// </para>
/// </remarks>
public class FitToWindowZoomTests
{
    /// <summary>図が現れた経路は等倍・並べ直した経路は縮小、の非対称を検証する</summary>
    [Fact(DisplayName = "収まらない図: 取込直後は 100%・整列と全体表示は縮む")]
    public void FitToWindow_WhenContentOverflows_ShrinksOnlyForRearrangePaths()
    {
        RunInIsolatedWindow(
            (vm, window) =>
            {
                // キャンバス列が残る幅にしたうえで、高さを抑えて「収まらない」状況を作る
                window.Width = 1200;
                window.Height = 500;
                window.UpdateLayout();
                DoEvents();

                // 図が新しく現れる経路（DB 取込・AI 生成と同じ入口）。全新規なので全体自動整列＋fit が走る
                var diagram = new ErDiagram();

                for (var i = 0; i < 40; i++)
                {
                    diagram.Entities.Add(new Entity { TableName = $"T{i}" });
                }

                vm.ReplaceDiagramFromModule(diagram);
                DoEvents();

                vm.ZoomLevel.Should().Be(1.0, "図が現れた経路は読める大きさを優先する");

                // 利用者が並べ直した経路は、結果を確かめられるよう全体が収まるまで縮む
                vm.AutoLayoutGridCommand.Execute(null);
                DoEvents();

                vm.ZoomLevel.Should().BeLessThan(1.0, "整列後は全体を見せる");

                vm.ZoomLevel = 1.0;
                vm.FitToWindowCommand.Execute(null);
                DoEvents();

                vm.ZoomLevel.Should()
                    .Be(
                        ViewportCalculator.MinZoom,
                        "この大きさでは下限（手動ズームと同じ 50%）まで縮む"
                    );
            }
        );
    }
}
