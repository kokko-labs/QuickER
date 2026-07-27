using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using FluentAssertions;
using QuickER.Tests.TestSupport;
using QuickER.ViewModels;
using Xunit;

namespace QuickER.Tests.Gui.Views;

/// <summary>
/// ツールバーの整列グループ（トグル＋直下ポップアップ）の配線を検証するテストクラス。
/// トグルとポップアップ開閉の同期・ポップアップ内 4 ボタンのコマンド束縛・項目クリックでの
/// クローズは XAML 配線（ElementName 束縛＋コードビハインド）のため、VM テストでは守れず
/// 実ウィンドウの Show を要する（lessons.md の「入力イベント配線はヘッドレスで検証できない」）。
/// </summary>
public class ArrangeGroupPopupTests
{
    /// <summary>トグルとポップアップの開閉同期・4 ボタンのコマンド束縛・項目クリックでのクローズを検証する</summary>
    [Fact(DisplayName = "整列グループ: トグルで開閉・4 コマンド束縛・項目クリックで閉じる")]
    public void ArrangeGroup_TogglePopupAndItemWiring()
    {
        Exception? captured = null;

        var thread = new Thread(() =>
        {
            try
            {
                WpfApplicationTestSupport.EnsureApplicationResources();

                var vm = new MainViewModel();
                var window = new MainWindow(vm)
                {
                    // 画面外・非アクティブで表示する（開発者のデスクトップを妨げない。lessons.md の先例）
                    WindowStartupLocation = WindowStartupLocation.Manual,
                    Left = -4000,
                    Top = -4000,
                    ShowActivated = false,
                };

                window.Show();
                window.UpdateLayout();
                DoEvents();

                try
                {
                    var toggle = (ToggleButton)window.FindName("ArrangeGroupToggle")!;
                    var popup = (Popup)window.FindName("ArrangePopup")!;

                    // 初期状態は閉じている
                    popup.IsOpen.Should().BeFalse();

                    // トグル ON → ポップアップが開く（ElementName 束縛の配線）
                    toggle.IsChecked = true;
                    DoEvents();
                    popup.IsOpen.Should().BeTrue();

                    // ポップアップ内の 4 ボタンが期待どおりのコマンドへ束縛されている
                    var panel = (StackPanel)((Border)popup.Child!).Child!;
                    var buttons = panel.Children.OfType<Button>().ToList();

                    buttons.Should().HaveCount(4);
                    buttons[0].Command.Should().BeSameAs(vm.AutoLayoutGridCommand);
                    buttons[1].Command.Should().BeSameAs(vm.AutoLayoutTreeCommand);
                    buttons[2].Command.Should().BeSameAs(vm.AutoLayoutForceCommand);
                    buttons[3].Command.Should().BeSameAs(vm.AutoFitEntityWidthsCommand);

                    // 項目クリック（Click イベント）でポップアップが閉じ、トグルも戻る
                    buttons[0].RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, buttons[0]));
                    DoEvents();
                    popup.IsOpen.Should().BeFalse();
                    toggle.IsChecked.Should().BeFalse();

                    // 再度開いてトグル OFF でも閉じる（双方向束縛）
                    toggle.IsChecked = true;
                    DoEvents();
                    popup.IsOpen.Should().BeTrue();
                    toggle.IsChecked = false;
                    DoEvents();
                    popup.IsOpen.Should().BeFalse();
                }
                finally
                {
                    window.Close();
                    DoEvents();
                }
            }
            catch (Exception ex)
            {
                captured = ex;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();
        thread.Join();

        captured.Should().BeNull();
    }

    /// <summary>保留中のディスパッチャ処理（レイアウト・束縛反映）を流し切る</summary>
    private static void DoEvents()
    {
        var frame = new System.Windows.Threading.DispatcherFrame();
        System.Windows.Threading.Dispatcher.CurrentDispatcher.BeginInvoke(
            System.Windows.Threading.DispatcherPriority.ApplicationIdle,
            new Action(() => frame.Continue = false)
        );
        System.Windows.Threading.Dispatcher.PushFrame(frame);
    }
}
