using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using AwesomeAssertions;
using QuickER.Services;
using QuickER.Tests.TestSupport;
using QuickER.ViewModels;
using Xunit;

namespace QuickER.Tests.Gui.Views;

/// <summary>
/// ツールバーの整列グループ（トグル＋直下ポップアップ）の配線を検証するテストクラス。
/// トグルとポップアップ開閉の同期・ポップアップ内 4 ボタンのコマンド束縛・項目クリックでの
/// クローズは XAML 配線（ElementName 束縛＋コードビハインド）のため、VM テストでは守れず
/// 実ウィンドウの Show を要する（入力イベントの配線はヘッドレスでは検証できない）。
/// </summary>
public class ArrangeGroupPopupTests
{
    /// <summary>トグルとポップアップの開閉同期・4 ボタンのコマンド束縛・項目クリックでのクローズを検証する</summary>
    [Fact(DisplayName = "整列グループ: トグルで開閉・4 コマンド束縛・項目クリックで閉じる")]
    public void ArrangeGroup_TogglePopupAndItemWiring()
    {
        Exception? captured = null;

        // MainWindow ctor の Initialize() が実 %APPDATA% の自動保存を復元し、Close の AutoSave が
        // 書き戻すため、永続化先を一時フォルダへ隔離する（実ユーザーデータの読み書きを断つ）
        var folder = Path.Combine(
            Path.GetTempPath(),
            "quicker-arrange-" + Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(folder);

        try
        {
            RunArrangeGroupScenario(folder, ref captured);
        }
        finally
        {
            try
            {
                Directory.Delete(folder, recursive: true);
            }
            catch
            {
                // 後始末の失敗はテスト結果に影響させない
            }
        }

        captured.Should().BeNull();
    }

    /// <summary>STA スレッド上で実ウィンドウを表示し、整列グループの配線を検証する本体</summary>
    private static void RunArrangeGroupScenario(string folder, ref Exception? captured)
    {
        Exception? threadCaptured = null;

        var thread = new Thread(() =>
        {
            try
            {
                WpfApplicationTestSupport.EnsureApplicationResources();

                var vm = new MainViewModel();
                vm.UsePersistenceForTests(
                    new GuiAppSettingsStore(folder),
                    Path.Combine(folder, "last_diagram.json")
                );
                var window = new MainWindow(vm)
                {
                    // 画面外・非アクティブで表示する（開発者のデスクトップを妨げない）
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
                threadCaptured = ex;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();
        thread.Join();

        captured = threadCaptured;
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
