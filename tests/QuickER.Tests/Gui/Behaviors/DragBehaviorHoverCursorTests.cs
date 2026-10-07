using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using AwesomeAssertions;
using QuickER.Behaviors;
using static QuickER.Tests.TestSupport.WpfApplicationTestSupport;

namespace QuickER.Tests.Gui.Behaviors;

/// <summary>
/// <see cref="DragBehavior"/> が、押下前のカードの右端（リサイズのつかみ）でカーソルをリサイズの形にすることを
/// 検証するテストクラス。
/// </summary>
/// <remarks>
/// <para>
/// 擬似のマウスイベントの位置は実際のカーソル位置になるため、マウス位置を引数で受ける
/// <see cref="DragBehavior.HandleMouseMove"/>（<c>MouseMove</c> のハンドラが位置を求めて渡す先）を直接呼ぶ。
/// ドラッグの有無の判定もこの中にあるので、押下前の分岐に到達するかをそのまま確かめられる。
/// </para>
/// <para>
/// 進行中状態は <c>[ThreadStatic]</c> なので、各テストは新しい STA スレッドで走らせる（ほかのテストの
/// ドラッグ状態を持ち越さない）。
/// </para>
/// <para>
/// パン中の確認では、パンの状態をキャンバスのウィンドウへ Space の押下を流して立てる
/// （<see cref="CanvasViewportBehaviorDetachTests"/> と同じ経路）。流したキー入力の扱いはウィンドウの
/// アクティブ状態に左右されるので、並列に走らせない（<see cref="TestSupport.WindowActivationTestCollection"/>）。
/// </para>
/// </remarks>
[Collection(TestSupport.WindowActivationTestCollection.Name)]
public class DragBehaviorHoverCursorTests
{
    /// <summary>カードの幅（右端 8px がつかみ）</summary>
    private const double CardWidth = 200;

    /// <summary>レイアウト済み（ActualWidth が確定した）カードを作る</summary>
    private static Border CreateCard()
    {
        var card = new Border { Width = CardWidth, Height = 50 };
        card.Measure(new Size(CardWidth, 50));
        card.Arrange(new Rect(0, 0, CardWidth, 50));
        return card;
    }

    [Fact(
        DisplayName = "押下前にカードの右端へ乗るとリサイズのカーソルになり、内側へ戻ると既定に戻る"
    )]
    public void HoverOnRightEdge_ShowsResizeCursor()
    {
        RunSta(() =>
        {
            var card = CreateCard();

            DragBehavior.HandleMouseMove(
                card,
                _ => new Point(CardWidth - 3, 10),
                MouseButtonState.Released
            );
            card.Cursor.Should().Be(Cursors.SizeWE);

            DragBehavior.HandleMouseMove(
                card,
                _ => new Point(CardWidth / 2, 10),
                MouseButtonState.Released
            );
            card.Cursor.Should().BeNull();
        });
    }

    [Fact(DisplayName = "つかみの幅より左ではリサイズのカーソルにならない")]
    public void HoverLeftOfGrip_KeepsDefaultCursor()
    {
        RunSta(() =>
        {
            var card = CreateCard();

            DragBehavior.HandleMouseMove(
                card,
                _ => new Point(CardWidth - 9, 10),
                MouseButtonState.Released
            );

            card.Cursor.Should().BeNull();
        });
    }

    [Fact(
        DisplayName = "パン中（Space を押している間）はカードの右端でもリサイズのカーソルにしない"
    )]
    public void HoverOnRightEdge_WhilePanning_KeepsDefaultCursor()
    {
        RunSta(() =>
        {
            var card = CreateCard();
            var canvas = new ScrollViewer
            {
                Content = new Grid { Width = 50, Height = 50 },
            };
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

            // 添付プロパティは表示より前に立てる（Space 監視の登録は ScrollViewer の Loaded で走る）
            CanvasViewportBehavior.SetIsEnabled(canvas, true);
            window.Show();
            DoEvents();

            try
            {
                window.RaiseEvent(
                    new KeyEventArgs(
                        Keyboard.PrimaryDevice,
                        PresentationSource.FromVisual(window)!,
                        0,
                        Key.Space
                    )
                    {
                        RoutedEvent = Keyboard.PreviewKeyDownEvent,
                    }
                );
                DoEvents();
                CanvasViewportBehavior.IsPanActive.Should().BeTrue("パン待機の状態を作る");

                DragBehavior.HandleMouseMove(
                    card,
                    _ => new Point(CardWidth - 3, 10),
                    MouseButtonState.Released
                );

                // カードのカーソルは ScrollViewer に設定したパンのカーソルより優先されるため、出すと
                // パンの手の形を置き換えてしまう
                card.Cursor.Should().BeNull();
            }
            finally
            {
                // パンの状態は [ThreadStatic] の静的状態なので、無効化で捨ててから閉じる
                CanvasViewportBehavior.SetIsEnabled(canvas, false);
                window.Close();
                DoEvents();
            }

            CanvasViewportBehavior.IsPanActive.Should().BeFalse("後続へ持ち越さない");
        });
    }
}
