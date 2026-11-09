using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using AwesomeAssertions;
using QuickER.Behaviors;
using QuickER.Model;
using QuickER.ViewModels;
using static QuickER.Tests.TestSupport.WpfApplicationTestSupport;

namespace QuickER.Tests.Gui.Behaviors;

/// <summary>
/// <see cref="DragBehavior"/> のうち、入力の列のエンジン（<see cref="CanvasInputTests"/>）が流さない形を扱うテストクラス。
/// </summary>
/// <remarks>
/// 残す理由：(1) 読み口（<c>BehaviorInput</c>）を差し替えずに流すドラッグの実際の入力の経路の守り（エンジンは差し替えて流す）。
/// (2) Undo の管理者が未設定のリサイズの完了（MainWindow は必ず管理者を束縛するので、エンジンの窓では作れない）。
/// 押下・移動・解放・リサイズ・取り消し・ホバーのカーソルはエンジンの決まった形の行と網羅の行が流す。
/// </remarks>
[Collection("CanvasInteractionState")]
public class DragBehaviorTests
{
    /// <summary>添付プロパティ未設定（マネージャ null）でも例外にならないことを検証する</summary>
    [Fact(DisplayName = "マネージャ未設定でも例外にならない")]
    public void MarkWidthChanged_NullManager_DoesNotThrow()
    {
        var act = () => DragBehavior.MarkWidthChanged(null, oldWidth: 200, newWidth: 260);

        act.Should().NotThrow();
    }

    /// <summary>
    /// 読み口を差し替えない実際の入力で、カードの押下がドラッグを始めてキャプチャを取り、実際の左ボタンが離れていれば
    /// キャプチャの取得に伴う WPF の擬似の移動でドラッグを終えることを検証する
    /// </summary>
    /// <remarks>
    /// 差し替えが無いときの読み口は WPF の値（実際のボタンの状態）をそのまま返す。押下で移動・リサイズの状態に入り、
    /// キャプチャを取った瞬間の擬似の移動が「左ボタンは離れている」を読んで終える、が製品の経路そのもの
    /// （読み口が差し替えなしで押されていると返すと、ドラッグが残って赤になる）。
    /// </remarks>
    [Fact(
        DisplayName = "差し替えない実際の入力では、カードの押下はドラッグを始め、実際のボタンが離れていれば終える"
    )]
    public void RealInputPress_StartsDragAndEndsWhenButtonIsUp()
    {
        RunSta(() =>
        {
            TestSupport.PhysicalMouseButtons.WaitUntilReleased();
            var card = new Border
            {
                Width = 100,
                Height = 50,
                DataContext = new EntityViewModel(new Entity { TableName = "A" }),
            };
            DragBehavior.SetIsEnabled(card, true);
            var canvas = new Canvas();
            canvas.Children.Add(card);
            var window = new Window
            {
                Content = canvas,
                Width = 200,
                Height = 200,
                ShowInTaskbar = false,
                ShowActivated = false,
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = -10000,
                Top = -10000,
            };
            var captured = false;
            card.GotMouseCapture += (_, _) => captured = true;

            try
            {
                window.Show();
                DoEvents();

                card.RaiseEvent(
                    new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
                    {
                        RoutedEvent = UIElement.MouseLeftButtonDownEvent,
                    }
                );

                captured.Should().BeTrue("押下でドラッグを始め、カードがマウスをキャプチャする");
                DragBehavior
                    .IsInteractionActiveForTests.Should()
                    .BeFalse("実際の左ボタンは離れているので、キャプチャの擬似の移動で終える");
                card.IsMouseCaptured.Should().BeFalse();
            }
            finally
            {
                DragBehavior.CancelActiveDrag();
                window.Close();
                DoEvents();
            }
        });
    }
}
