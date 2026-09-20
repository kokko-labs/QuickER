using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using AwesomeAssertions;
using QuickER.ViewModels;
using static QuickER.Tests.TestSupport.WpfApplicationTestSupport;

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
        RunInIsolatedWindow(AssertArrangeGroup);
    }

    /// <summary>表示済みウィンドウ上で整列グループの開閉とコマンド束縛を検証する</summary>
    private static void AssertArrangeGroup(MainViewModel vm, MainWindow window)
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
}
