using CommunityToolkit.Mvvm.Input;
using FluentAssertions;
using QuickER.Extensibility;
using QuickER.ViewModels;

namespace QuickER.Tests.Gui.ViewModels;

/// <summary>
/// フィーチャーモジュールのツールバーボタンをグループ区切り（BeginsGroup）単位へ分割する
/// <see cref="MainViewModel.SplitToolbarGroups"/> と、その公開プロパティ連動を検証するテストクラス。
/// グループはツールバー WrapPanel の折返し単位になる（くくりを崩さない折返し）。
/// </summary>
public class MainViewModelToolbarGroupsTests
{
    /// <summary>指定した BeginsGroup を持つテスト用のツールバーボタンを作る</summary>
    private static FeatureToolbarItem Item(string label, bool beginsGroup = false) =>
        new(
            icon: "★",
            label: label,
            tooltip: null,
            command: new RelayCommand(() => { }),
            beginsGroup
        );

    /// <summary>実構成（DB×2・AI×2・コード生成系×3）の分割が 3 くくりになることを検証する</summary>
    [Fact(DisplayName = "SplitToolbarGroups: BeginsGroup 境界で 3 グループへ分割される")]
    public void SplitToolbarGroups_SplitsAtGroupBoundaries()
    {
        // 全体先頭は App が BeginsGroup=false へ矯正済みの形を模す
        var items = new[]
        {
            Item("DB取込"),
            Item("DB同期"),
            Item("AIチャット", beginsGroup: true),
            Item("AIモック"),
            Item("コード生成", beginsGroup: true),
            Item("コード取込"),
            Item("クエリ定義"),
        };

        var groups = MainViewModel.SplitToolbarGroups(items);

        groups.Should().HaveCount(3);
        groups[0].Select(item => item.Label).Should().Equal("DB取込", "DB同期");
        groups[1].Select(item => item.Label).Should().Equal("AIチャット", "AIモック");
        groups[2]
            .Select(item => item.Label)
            .Should()
            .Equal("コード生成", "コード取込", "クエリ定義");
    }

    /// <summary>先頭要素が BeginsGroup=true でも空グループを作らないことを検証する</summary>
    [Fact(DisplayName = "SplitToolbarGroups: 先頭が BeginsGroup=true でも空グループを作らない")]
    public void SplitToolbarGroups_FirstItemWithBeginsGroup_DoesNotCreateEmptyGroup()
    {
        var items = new[] { Item("A", beginsGroup: true), Item("B") };

        var groups = MainViewModel.SplitToolbarGroups(items);

        groups.Should().HaveCount(1);
        groups[0].Select(item => item.Label).Should().Equal("A", "B");
    }

    /// <summary>空のボタン列は空のグループ列になることを検証する</summary>
    [Fact(DisplayName = "SplitToolbarGroups: 空列は空のグループ列になる")]
    public void SplitToolbarGroups_Empty_YieldsNoGroups()
    {
        MainViewModel.SplitToolbarGroups([]).Should().BeEmpty();
    }

    /// <summary>FeatureToolbarItems の設定でグループが再計算され、変更通知も発火することを検証する</summary>
    [Fact(DisplayName = "FeatureToolbarItems 設定時に FeatureToolbarItemGroups が連動更新される")]
    public void SettingFeatureToolbarItems_UpdatesGroupsAndRaisesPropertyChanged()
    {
        var vm = new MainViewModel();
        var raised = new List<string?>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        vm.FeatureToolbarItems = new[] { Item("A"), Item("B", beginsGroup: true) };

        vm.FeatureToolbarItemGroups.Should().HaveCount(2);
        raised.Should().Contain(nameof(MainViewModel.FeatureToolbarItemGroups));
    }
}
