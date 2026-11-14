using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using AwesomeAssertions;
using QuickER.Behaviors;
using static QuickER.Tests.TestSupport.WpfApplicationTestSupport;

namespace QuickER.Tests.Gui.Behaviors;

/// <summary>
/// <see cref="CanvasViewportBehavior"/> の Space の横取りのうち、入力の列のエンジン（<see cref="CanvasInputTests"/>）が
/// 流さない形を、読み口を差し替えない実際の入力で検証するテストクラス。
/// </summary>
/// <remarks>
/// <para>
/// 残す理由：(1) 読み口（<c>BehaviorInput</c>）を差し替えずに流すパンの実際の入力の経路の守り（エンジンは差し替えて流す）。
/// (2) キーボードで来たキャンバス（MainWindow のタブ順にキャンバスが入らないので、エンジンの窓では作れない）。
/// (3) 無効化（IsEnabled=false）したウィンドウの購読の後始末（製品は無効化しないので、エンジンの手に無い）。
/// Space の横取りの線引きのほかの形（キーボードで移ったボタン類は押せる・マウスで来た・戻った直後はパン・列一覧の
/// PK のチェックボックス）はエンジンの決まった形の行と網羅の行が流す。
/// </para>
/// <para>
/// キーは <see cref="InputManager.ProcessInput"/> で流す（ウィンドウのトンネル → フォーカス要素の処理 →
/// Tab のフォーカス移動まで、製品と同じ経路を通る）。マウスの押下は要素へ PreviewMouseDown を発火させてから
/// フォーカスを移す（WPF がクリックで行う順序と同じ。擬似のマウス入力は実際のカーソル位置へ届くため使えない）。
/// </para>
/// <para>
/// ボタンは Space の押下でマウスをキャプチャするため、押下から解放までの間にメッセージを処理させると、
/// その間のマウスやアクティブの変化で押下が解け得る。Space の押下と解放はメッセージを処理させずに続けて流す。
/// </para>
/// <para>
/// ボタン・チェックボックスは Space の解放でマウスのボタンをスレッドのキーの状態の表から読み、押されていれば Click も
/// 切り替えもしない。利用者の実際のボタンを混ぜないよう、ウィンドウごとに <see cref="TestSupport.PhysicalMouseButtons.Isolate"/> の
/// スコープを持ち、Space の押下の前は押されている間待ってから表を「離れている」に固定し、ほかの入力の前は固定だけする
/// （押下と解放の間にメッセージを処理させない）。
/// </para>
/// <para>
/// 判断が <c>Deactivated</c> に左右されるので、ほかのテストのウィンドウにアクティブを奪われないよう
/// 並列に走らせない（<see cref="TestSupport.WindowActivationTestCollection"/>）。
/// </para>
/// </remarks>
[Collection(TestSupport.WindowActivationTestCollection.Name)]
public class CanvasViewportBehaviorSpaceKeyTests
{
    /// <summary>キャンバス（ScrollViewer）・ボタン・チェックボックスを並べたウィンドウ</summary>
    private sealed record TestWindow(
        Window Window,
        ScrollViewer Canvas,
        Button Button,
        CheckBox CheckBox,
        TestSupport.PhysicalMouseButtons.Isolation Mouse
    )
    {
        /// <summary>ボタンの Click の回数</summary>
        public int Clicks { get; set; }

        /// <summary>ウィンドウを閉じ、キーの状態の表のマウスのボタンを表示する前の値へ戻す</summary>
        public void Close()
        {
            try
            {
                Window.Close();
            }
            finally
            {
                Mouse.Dispose();
            }
        }
    }

    /// <summary>キャンバス・ボタン・チェックボックスの順に並べたウィンドウを表示し、ビヘイビアを有効にする</summary>
    private static TestWindow ShowWindow(bool enableBehavior = true)
    {
        var canvas = new ScrollViewer
        {
            Content = new Grid { Width = 50, Height = 50 },
            Height = 60,
        };
        var button = new Button { Content = "Button" };
        var checkBox = new CheckBox { Content = "CheckBox" };
        var panel = new StackPanel();
        panel.Children.Add(canvas);
        panel.Children.Add(button);
        panel.Children.Add(checkBox);
        var window = new Window
        {
            Content = panel,
            Width = 300,
            Height = 300,
            ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = -10000,
            Top = -10000,
        };
        var testWindow = new TestWindow(
            window,
            canvas,
            button,
            checkBox,
            TestSupport.PhysicalMouseButtons.Isolate()
        );
        button.Click += (_, _) => testWindow.Clicks++;

        // まれに全件実行でだけ落ちるので、失敗した瞬間の状態の記録へイベントを残す（観測だけで結果は変えない）
        TestSupport.GuiFailureDiagnostics.Track(window);

        // 添付プロパティは表示より前に立てる（ウィンドウへの購読は ScrollViewer の Loaded で張られる）
        CanvasViewportBehavior.SetIsEnabled(canvas, enableBehavior);
        window.Show();
        DoEvents();

        return testWindow;
    }

    /// <summary>キーの押下か解放を、入力の経路（InputManager）へ流す</summary>
    /// <remarks>
    /// Space の押下の前には、実際のマウスのボタンが離されるまで待ってからキーの状態の表のボタンを「離れている」に固定する
    /// （押されたままだと WPF のボタンは Space の解放で Click しない＝<see cref="TestSupport.PhysicalMouseButtons"/>）。
    /// ほかのキー（Space の解放を含む）の前は固定だけする（押下と解放の間にメッセージを処理させない）。パンを見るテストの
    /// Space も同じ経路を通す（離されていれば待たずに進むので、結果は変わらない）
    /// </remarks>
    private static void SendKey(TestWindow w, Key key, bool down)
    {
        if (key == Key.Space && down)
        {
            w.Mouse.WaitAndPin();
        }
        else
        {
            w.Mouse.Pin();
        }

        var source = PresentationSource.FromVisual(w.Window)!;
        InputManager.Current.ProcessInput(
            new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, key)
            {
                RoutedEvent = down ? Keyboard.PreviewKeyDownEvent : Keyboard.PreviewKeyUpEvent,
            }
        );
    }

    /// <summary>Tab を押して離す（キーボードでフォーカスを次へ移す）</summary>
    private static void PressTab(TestWindow w)
    {
        SendKey(w, Key.Tab, down: true);
        SendKey(w, Key.Tab, down: false);
    }

    /// <summary>要素をマウスでクリックしたときの順序（ウィンドウのトンネル → フォーカス移動）を再現する</summary>
    private static void ClickToFocus(TestWindow w, UIElement element)
    {
        w.Mouse.Pin();
        element.RaiseEvent(
            new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
            {
                RoutedEvent = Mouse.PreviewMouseDownEvent,
            }
        );
        element.Focus();
    }

    [Fact(
        DisplayName = "キャンバスにフォーカスがあるときは、キーボードで来ていても Space はパンになる"
    )]
    public void SpaceOnKeyboardFocusedCanvas_Pans()
    {
        RunSta(() =>
        {
            var w = ShowWindow();

            try
            {
                // 直前の入力がキーボード（Tab）のまま、フォーカスがキャンバスへ移る
                ClickToFocus(w, w.CheckBox);
                PressTab(w);
                w.Canvas.Focus();
                Keyboard.FocusedElement.Should().BeSameAs(w.Canvas);

                SendKey(w, Key.Space, down: true);
                CanvasViewportBehavior.IsPanActive.Should().BeTrue();
                SendKey(w, Key.Space, down: false);
            }
            finally
            {
                w.Close();
                DoEvents();
            }
        });
    }

    /// <summary>
    /// 無効化したウィンドウへ張っていたマウス押下・フォーカス移動の購読が外れ、
    /// 別のウィンドウの記録を書き換えないことを検証する
    /// </summary>
    /// <remarks>
    /// 外し忘れがあると、もう使っていないウィンドウの入力が今のキャンバスの「キーボードで来たか」を書き換え、
    /// Space の押下とパンが入れ替わる（キーボードで移ったボタンがパンになる／マウスで来たボタンが押される）
    /// </remarks>
    [Fact(
        DisplayName = "無効化したウィンドウのマウス押下・フォーカス移動の購読は外れ、今の記録を書き換えない"
    )]
    public void Disabling_RemovesWindowMouseAndFocusHandlers()
    {
        RunSta(() =>
        {
            var disabled = ShowWindow();
            CanvasViewportBehavior.SetIsEnabled(disabled.Canvas, false);
            DoEvents();
            var w = ShowWindow();

            try
            {
                // マウス押下の購読: 外れていないと、無効化したウィンドウの押下が「直前の入力」を書き換える
                ClickToFocus(w, w.Canvas);
                PressTab(w);
                disabled.Window.RaiseEvent(
                    new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
                    {
                        RoutedEvent = Mouse.PreviewMouseDownEvent,
                    }
                );
                w.CheckBox.Focus();
                SendKey(w, Key.Space, down: true);
                SendKey(w, Key.Space, down: false);
                w.CheckBox.IsChecked.Should()
                    .BeTrue("直前の入力は Tab のまま＝キーボードで移ったフォーカスとして押せる");

                // フォーカス移動の購読: 外れていないと、無効化したウィンドウのフォーカス移動が記録を書き換える。
                // マウスでクリックしたボタンの上で、フォーカスを動かさないキー（Shift）だけを押した状態を作る
                // （直前の入力はキーボード・キーボードで来た記録は無い）。ここで記録を作り直されると押下に化ける
                ClickToFocus(w, w.Button);
                SendKey(w, Key.LeftShift, down: true);
                SendKey(w, Key.LeftShift, down: false);
                Keyboard.FocusedElement.Should().BeSameAs(w.Button);
                disabled.Window.RaiseEvent(
                    new KeyboardFocusChangedEventArgs(
                        Keyboard.PrimaryDevice,
                        0,
                        null,
                        disabled.Button
                    )
                    {
                        RoutedEvent = Keyboard.PreviewGotKeyboardFocusEvent,
                    }
                );
                SendKey(w, Key.Space, down: true);
                CanvasViewportBehavior
                    .IsPanActive.Should()
                    .BeTrue("ボタンへはマウスで来たまま＝パン");
                SendKey(w, Key.Space, down: false);
                w.Clicks.Should().Be(0);
            }
            finally
            {
                w.Close();
                disabled.Close();
                DoEvents();
            }
        });
    }
}
