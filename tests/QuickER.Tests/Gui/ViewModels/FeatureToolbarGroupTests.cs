using System.Windows.Input;
using AwesomeAssertions;
using QuickER.Extensibility;
using QuickER.ViewModels;

namespace QuickER.Tests.Gui.ViewModels;

/// <summary>
/// ツールバーの折返し単位（<see cref="MainViewModel.FeatureToolbarItemGroups"/>）が、
/// いつ組み立てられるかを固定するテストクラス。
/// </summary>
/// <remarks>
/// グループ分割は <see cref="MainViewModel.FeatureToolbarItems"/> を設定したときに 1 回だけ走り、
/// ビューは分割済みのグループへ束縛する。したがって
/// <see cref="FeatureToolbarItem.BeginsGroup"/> を実行中に変えても折返しは変わらない。
/// <see cref="FeatureToolbarItem"/> の XmlDoc はそう書いてあり、ここでそれを固定する
/// （実行時追随を入れるならこのテストが赤になる＝仕様変更として明示的に扱える）。
/// </remarks>
public class FeatureToolbarGroupTests
{
    /// <summary>何もしないコマンド（ボタン記述子の組み立て用）</summary>
    private sealed class NoopCommand : ICommand
    {
        public event EventHandler? CanExecuteChanged
        {
            add { }
            remove { }
        }

        public bool CanExecute(object? parameter) => true;

        public void Execute(object? parameter) { }
    }

    private static FeatureToolbarItem CreateItem(string label, bool beginsGroup) =>
        new("x", label, null, new NoopCommand(), beginsGroup);

    [Fact(DisplayName = "折返しのグループは BeginsGroup の直前で分割される")]
    public void Groups_AreSplitBeforeBeginsGroup()
    {
        var vm = new MainViewModel
        {
            FeatureToolbarItems =
            [
                CreateItem("a", false),
                CreateItem("b", false),
                CreateItem("c", beginsGroup: true),
            ],
        };

        vm.FeatureToolbarItemGroups.Should().HaveCount(2);
        vm.FeatureToolbarItemGroups[0].Should().HaveCount(2);
        vm.FeatureToolbarItemGroups[1].Should().HaveCount(1);
    }

    /// <summary>設定後に BeginsGroup を変えても折返しが変わらないことを検証する</summary>
    /// <remarks>
    /// これは不具合ではなく仕様（XmlDoc が「起動時の組み立てでのみ反映」と述べている）。
    /// 実行中に効くのは <see cref="FeatureToolbarItem.Tooltip"/> のほうで、そちらはビューが直接束縛する。
    /// </remarks>
    [Fact(DisplayName = "設定後に BeginsGroup を変えても折返しは変わらない")]
    public void Groups_DoNotFollowLaterBeginsGroupChanges()
    {
        var second = CreateItem("b", beginsGroup: false);
        var vm = new MainViewModel { FeatureToolbarItems = [CreateItem("a", false), second] };

        vm.FeatureToolbarItemGroups.Should().ContainSingle();

        second.BeginsGroup = true;

        vm.FeatureToolbarItemGroups.Should()
            .ContainSingle("折返しの組み立ては FeatureToolbarItems の設定時にだけ走る");
        vm.FeatureToolbarItemGroups[0].Should().HaveCount(2);
    }

    /// <summary>ツールチップは実行中に切り替わることを検証する（BeginsGroup との非対称を固定する）</summary>
    [Fact(DisplayName = "ツールチップは実行中に切り替わり、通知も出る")]
    public void Tooltip_ChangesAtRuntime()
    {
        var item = CreateItem("a", false);
        var changed = new List<string?>();
        item.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        item.Tooltip = "hello";

        item.Tooltip.Should().Be("hello");
        changed.Should().Contain(nameof(FeatureToolbarItem.Tooltip));
    }
}
