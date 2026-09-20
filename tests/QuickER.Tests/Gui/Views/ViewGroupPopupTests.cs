using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using AwesomeAssertions;
using QuickER.ViewModels;
using static QuickER.Tests.TestSupport.WpfApplicationTestSupport;

namespace QuickER.Tests.Gui.Views;

/// <summary>
/// ツールバーの表示グループ（トグル＋直下ポップアップ）の配線を検証するテストクラス。
/// </summary>
/// <remarks>
/// <para>
/// トグルとポップアップ開閉の同期・5 トグルの束縛先はいずれも XAML 配線（ElementName 束縛）で、
/// VM テストでは守れない（束縛先を間違えても WPF は無言で何も起きない）。
/// </para>
/// <para>
/// 整列グループ（<see cref="ArrangeGroupPopupTests"/>）との差は「項目クリックで閉じない」こと。
/// 一発実行のコマンドと違い、こちらは状態のトグルなので、続けて切り替えられる必要がある。
/// </para>
/// </remarks>
public class ViewGroupPopupTests
{
    /// <summary>トグルとポップアップの開閉同期・5 トグルの束縛・項目クリックで閉じないことを検証する</summary>
    [Fact(DisplayName = "表示グループ: トグルで開閉・5 トグル束縛・項目クリックでは閉じない")]
    public void ViewGroup_TogglePopupAndItemWiring()
    {
        RunInIsolatedWindow(AssertViewGroup);
    }

    /// <summary>表示済みウィンドウ上で表示グループの開閉とトグルの束縛を検証する</summary>
    private static void AssertViewGroup(MainViewModel vm, MainWindow window)
    {
        var groupToggle = (ToggleButton)window.FindName("ViewGroupToggle")!;
        var popup = (Popup)window.FindName("ViewPopup")!;

        // 初期状態は閉じている
        popup.IsOpen.Should().BeFalse();

        // トグル ON → ポップアップが開く（ElementName 束縛の配線）
        groupToggle.IsChecked = true;
        DoEvents();
        popup.IsOpen.Should().BeTrue();

        var panel = (StackPanel)((Border)popup.Child!).Child!;
        var toggles = panel.Children.OfType<ToggleButton>().ToList();

        // 並び順はツールボックス → プロパティパネル → 説明表示 → NULL 表示 → 簡易表示
        toggles.Should().HaveCount(5);
        toggles[0].IsChecked.Should().Be(vm.IsToolboxVisible);
        toggles[1].IsChecked.Should().Be(vm.IsPropertyPanelVisible);
        toggles[2].IsChecked.Should().Be(vm.ShowColumnDescriptionsInDiagram);
        toggles[3].IsChecked.Should().Be(vm.ShowNullabilityInDiagram);
        toggles[4].IsChecked.Should().Be(vm.IsCompactViewInDiagram);

        // 各トグルが対応する VM プロパティへ双方向に束縛されている
        AssertTwoWay(toggles[0], () => vm.IsToolboxVisible, value => vm.IsToolboxVisible = value);
        AssertTwoWay(
            toggles[1],
            () => vm.IsPropertyPanelVisible,
            value => vm.IsPropertyPanelVisible = value
        );
        AssertTwoWay(
            toggles[2],
            () => vm.ShowColumnDescriptionsInDiagram,
            value => vm.ShowColumnDescriptionsInDiagram = value
        );
        AssertTwoWay(
            toggles[3],
            () => vm.ShowNullabilityInDiagram,
            value => vm.ShowNullabilityInDiagram = value
        );
        AssertTwoWay(
            toggles[4],
            () => vm.IsCompactViewInDiagram,
            value => vm.IsCompactViewInDiagram = value
        );

        // 項目クリックでは閉じない（状態トグルを続けて切り替えられる）
        toggles[0].RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, toggles[0]));
        DoEvents();
        popup.IsOpen.Should().BeTrue("状態のトグルは続けて切り替えられる必要がある");
        groupToggle.IsChecked.Should().Be(true);

        // トグル OFF で閉じる（双方向束縛）
        groupToggle.IsChecked = false;
        DoEvents();
        popup.IsOpen.Should().BeFalse();
    }

    /// <summary>トグルと VM プロパティが双方向に束縛されていることを、両方向へ動かして確かめる</summary>
    private static void AssertTwoWay(ToggleButton toggle, Func<bool> read, Action<bool> write)
    {
        var original = read();

        // VM → UI
        write(!original);
        DoEvents();
        toggle.IsChecked.Should().Be(!original);

        // UI → VM
        toggle.IsChecked = original;
        DoEvents();
        read().Should().Be(original);
    }
}
