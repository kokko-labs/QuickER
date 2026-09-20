using System.Windows;
using System.Windows.Controls;
using AwesomeAssertions;
using QuickER.Behaviors;
using static QuickER.Tests.TestSupport.WpfApplicationTestSupport;

namespace QuickER.Tests.Gui.Behaviors;

/// <summary>
/// 列を幅ごと畳む添付ビヘイビア <see cref="CollapsibleColumnBehavior"/> を、実 WPF の
/// レイアウト計算（Measure / Arrange）を回して検証するテストクラス。
/// </summary>
/// <remarks>
/// <para>
/// 「幅 0 になること」は列の宣言幅・最小幅・兄弟列との配分が絡むため、実際にレイアウトを
/// 回さないと確かめられない（プロパティ値だけを見ると <see cref="ColumnDefinition.MinWidth"/> の
/// 取りこぼしを見逃す）。
/// </para>
/// <para>
/// とくに重要なのは「ドラッグで変えた幅が戻ってくる」こと。<see cref="GridSplitter"/> は
/// <see cref="ColumnDefinition.Width"/> へ直接書き込むため、復元を宣言値で行う実装だと
/// ユーザーが調整した幅が隠すたびに失われる。
/// </para>
/// </remarks>
public class CollapsibleColumnBehaviorTests
{
    /// <summary>MainWindow.xaml と同じ 4 列構成（左パネル・キャンバス・スプリッタ・右パネル）を組む</summary>
    private static Grid BuildFourColumnGrid()
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(200) });
        grid.ColumnDefinitions.Add(
            new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }
        );
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(5) });
        grid.ColumnDefinitions.Add(
            new ColumnDefinition { Width = new GridLength(560), MinWidth = 300 }
        );

        return grid;
    }

    /// <summary>指定サイズでレイアウトを確定させる</summary>
    private static void Layout(Grid grid)
    {
        grid.Measure(new Size(1880, 1000));
        grid.Arrange(new Rect(0, 0, 1880, 1000));
        grid.UpdateLayout();
    }

    [Fact(DisplayName = "列を畳むと実幅が 0 になり、戻すと元の幅へ復帰する")]
    public void Collapse_ThenExpand_RestoresDeclaredWidth()
    {
        RunSta(() =>
        {
            var grid = BuildFourColumnGrid();
            var toolbox = grid.ColumnDefinitions[0];
            Layout(grid);

            toolbox.ActualWidth.Should().Be(200);

            CollapsibleColumnBehavior.SetIsExpanded(toolbox, false);
            Layout(grid);

            toolbox.ActualWidth.Should().Be(0);

            CollapsibleColumnBehavior.SetIsExpanded(toolbox, true);
            Layout(grid);

            toolbox.ActualWidth.Should().Be(200);
        });
    }

    [Fact(DisplayName = "最小幅を持つ列でも 0 まで畳める")]
    public void Collapse_ColumnWithMinWidth_ShrinksToZero()
    {
        RunSta(() =>
        {
            var grid = BuildFourColumnGrid();
            var propertyPanel = grid.ColumnDefinitions[3];
            Layout(grid);

            propertyPanel.ActualWidth.Should().Be(560);

            CollapsibleColumnBehavior.SetIsExpanded(propertyPanel, false);
            Layout(grid);

            // MinWidth=300 を退避せず残すと、ここが 300 のままになる
            propertyPanel.ActualWidth.Should().Be(0);

            CollapsibleColumnBehavior.SetIsExpanded(propertyPanel, true);
            Layout(grid);

            propertyPanel.ActualWidth.Should().Be(560);
            propertyPanel.MinWidth.Should().Be(300);
        });
    }

    [Fact(DisplayName = "スプリッタで変えた幅は、畳んで戻しても保たれる")]
    public void Collapse_AfterSplitterDrag_RestoresDraggedWidth()
    {
        RunSta(() =>
        {
            var grid = BuildFourColumnGrid();
            var propertyPanel = grid.ColumnDefinitions[3];
            Layout(grid);

            // GridSplitter のドラッグは ColumnDefinition.Width へ直接書き込む
            propertyPanel.Width = new GridLength(420);
            Layout(grid);

            CollapsibleColumnBehavior.SetIsExpanded(propertyPanel, false);
            Layout(grid);
            CollapsibleColumnBehavior.SetIsExpanded(propertyPanel, true);
            Layout(grid);

            // 宣言値の 560 へ戻す実装だと、ユーザーの調整が隠すたびに失われる
            propertyPanel.ActualWidth.Should().Be(420);
        });
    }

    [Fact(DisplayName = "一度も畳んでいない列へ展開を指示しても宣言幅は変わらない")]
    public void Expand_WithoutCollapse_LeavesDeclaredWidth()
    {
        RunSta(() =>
        {
            var grid = BuildFourColumnGrid();
            var toolbox = grid.ColumnDefinitions[0];
            Layout(grid);

            CollapsibleColumnBehavior.SetIsExpanded(toolbox, true);
            Layout(grid);

            toolbox.ActualWidth.Should().Be(200);
        });
    }

    [Fact(DisplayName = "列以外へ付けても例外にならない（無視される）")]
    public void SetIsExpanded_OnNonColumnDefinition_IsIgnored()
    {
        RunSta(() =>
        {
            var border = new Border();

            var act = () => CollapsibleColumnBehavior.SetIsExpanded(border, false);

            act.Should().NotThrow();
        });
    }
}
