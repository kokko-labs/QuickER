using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using AwesomeAssertions;
using QuickER.Behaviors;
using static QuickER.Tests.TestSupport.WpfApplicationTestSupport;

namespace QuickER.Tests.Gui.Behaviors;

/// <summary>
/// <see cref="CanvasViewportBehavior"/> の後始末（無効化・画面から外れたとき）を検証するテストクラス。
/// </summary>
/// <remarks>
/// <para>
/// このビヘイビアは ScrollViewer 自身だけでなく<b>所属ウィンドウ</b>へも購読を張る
/// （Space 監視の <c>PreviewKeyDown</c> / <c>PreviewKeyUp</c> と <c>Deactivated</c>）。
/// ScrollViewer 側の購読しか外さないと、キャンバスを無効化・破棄したあともウィンドウのキー入力を
/// 拾い続け、対象 ScrollViewer への静的参照がキャンバスの視覚ツリーを掴んだまま残る。
/// </para>
/// <para>
/// ウィンドウ購読が外れたことは、Space を送っても<b>パン待機状態にならない</b>ことで確かめる
/// （内部状態を覗く口を増やさずに済み、実際に困る症状そのものを見ている）。
/// </para>
/// <para>
/// 進行中状態は <c>[ThreadStatic]</c> なので、並列実行される他クラスとは干渉しない
/// （下の隔離テストが固定する）。同じ状態を書くテストどうしは
/// <c>CanvasInteractionState</c> コレクションで直列化する。
/// </para>
/// </remarks>
[Collection("CanvasInteractionState")]
public class CanvasViewportBehaviorDetachTests
{
    /// <summary>ScrollViewer を 1 つ載せたウィンドウを表示し、ビヘイビアを有効にする</summary>
    private static (Window Window, ScrollViewer ScrollViewer) ShowCanvasWindow()
    {
        var scrollViewer = new ScrollViewer
        {
            Content = new Grid { Width = 50, Height = 50 },
        };
        var window = new Window
        {
            Content = scrollViewer,
            Width = 200,
            Height = 200,
            ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = -10000,
            Top = -10000,
        };

        // 添付プロパティは表示より前に立てる（Space 監視の登録は ScrollViewer の Loaded で走るため、
        // 表示後に立てると登録の機会が無い＝本番の XAML と同じ順序にする）
        CanvasViewportBehavior.SetIsEnabled(scrollViewer, true);
        window.Show();
        DoEvents();

        return (window, scrollViewer);
    }

    /// <summary>ウィンドウへ Space の押下を送る（ビヘイビアの Space 監視が拾う経路）</summary>
    private static void SendSpaceDown(Window window)
    {
        var source = PresentationSource.FromVisual(window);
        source.Should().NotBeNull("表示済みのウィンドウでないとキー入力を模せない");

        window.RaiseEvent(
            new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, Key.Space)
            {
                RoutedEvent = Keyboard.PreviewKeyDownEvent,
            }
        );
        DoEvents();
    }

    [Fact(DisplayName = "無効化するとウィンドウ側の Space 監視も外れる")]
    public void Disabling_RemovesWindowKeyHandlers()
    {
        RunSta(() =>
        {
            var (window, scrollViewer) = ShowCanvasWindow();

            try
            {
                SendSpaceDown(window);
                CanvasViewportBehavior
                    .IsPanActive.Should()
                    .BeTrue("有効な間はウィンドウのキー入力を拾う");

                CanvasViewportBehavior.SetIsEnabled(scrollViewer, false);
                DoEvents();

                CanvasViewportBehavior.IsPanActive.Should().BeFalse("無効化で進行中の状態も捨てる");

                SendSpaceDown(window);
                CanvasViewportBehavior
                    .IsPanActive.Should()
                    .BeFalse("無効化したあとはウィンドウのキー入力を拾わない");
            }
            finally
            {
                window.Close();
                DoEvents();
            }
        });
    }

    /// <summary>画面から外れたときにウィンドウ側の購読が外れることを検証する</summary>
    /// <remarks>
    /// <para>
    /// 添付プロパティは <c>true</c> のままなので、<c>Unloaded</c> を拾えていないと購読が残る。
    /// ウィンドウを閉じる代わりに中身を外すのは、閉じると <c>PresentationSource</c> が消えて
    /// キー入力を模せなくなるため（外す側の経路は同じ <c>Unloaded</c>）。
    /// </para>
    /// <para>
    /// <c>Unloaded</c> を拾えていないと、後始末が走らず 1 回目の Space の状態が残るので、
    /// 中身を外した直後の表明で赤になる。
    /// </para>
    /// </remarks>
    [Fact(DisplayName = "画面から外れるとウィンドウ側の Space 監視も外れる")]
    public void Unloading_RemovesWindowKeyHandlers()
    {
        RunSta(() =>
        {
            var (window, _) = ShowCanvasWindow();

            try
            {
                SendSpaceDown(window);
                CanvasViewportBehavior.IsPanActive.Should().BeTrue();

                window.Content = null;
                DoEvents();

                CanvasViewportBehavior
                    .IsPanActive.Should()
                    .BeFalse("画面から外れたら進行中の状態も捨てる");

                SendSpaceDown(window);
                CanvasViewportBehavior
                    .IsPanActive.Should()
                    .BeFalse("外れたあとはウィンドウのキー入力を拾わない");
            }
            finally
            {
                window.Close();
                DoEvents();
            }
        });
    }

    /// <summary>視覚ツリーから外して戻したあとも、パンが効き続けることを検証する</summary>
    /// <remarks>
    /// <c>Unloaded</c> はウィンドウを閉じるときだけでなく、親の付け替えやテンプレートの再適用でも起きる。
    /// 後始末で捨てた対象を <c>Loaded</c> で戻さないと、添付プロパティは有効なのに対象が null になり、
    /// Space パンがエラーも出さずに効かなくなる（「効かない」ことにしか現れない壊れ方）。
    /// </remarks>
    [Fact(DisplayName = "外して戻したあともパンは効き続ける")]
    public void Reloading_RestoresTheTarget()
    {
        RunSta(() =>
        {
            var (window, scrollViewer) = ShowCanvasWindow();

            try
            {
                window.Content = null;
                DoEvents();
                window.Content = scrollViewer;
                DoEvents();

                SendSpaceDown(window);

                // IsPanActive はウィンドウ側の購読だけで立つため、対象が戻ったかは
                // 「対象にだけ効く」カーソルの変化で見る
                CanvasViewportBehavior.IsPanActive.Should().BeTrue();
                scrollViewer
                    .Cursor.Should()
                    .Be(Cursors.Hand, "外して戻したあとも対象として扱われる");
            }
            finally
            {
                window.Close();
                DoEvents();
            }
        });
    }

    /// <summary>静的状態がスレッドごとに独立することを検証する</summary>
    /// <remarks>
    /// <c>[ThreadStatic]</c> を外すと、並列実行される他スレッドのキャンバス操作と状態を奪い合う
    /// （<c>DragBehavior</c> の同名テストと同じ理由）。
    /// </remarks>
    [Fact(DisplayName = "ビューポートの静的状態はスレッドごとに独立する")]
    public void ViewportState_IsIsolatedPerThread()
    {
        CanvasViewportBehavior.SuppressCenterZoomCorrection = true;

        try
        {
            var sawFlag = true;
            var other = new Thread(() =>
                sawFlag = CanvasViewportBehavior.SuppressCenterZoomCorrection
            );
            other.Start();
            other.Join();

            sawFlag.Should().BeFalse("別スレッドからは見えない");
            CanvasViewportBehavior
                .SuppressCenterZoomCorrection.Should()
                .BeTrue("自スレッドの値は残る");
        }
        finally
        {
            CanvasViewportBehavior.SuppressCenterZoomCorrection = false;
        }
    }
}
