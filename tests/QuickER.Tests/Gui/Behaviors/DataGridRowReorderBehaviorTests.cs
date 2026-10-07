using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using AwesomeAssertions;
using QuickER.Behaviors;
using QuickER.Model;
using QuickER.ViewModels;
using static QuickER.Tests.TestSupport.WpfApplicationTestSupport;

namespace QuickER.Tests.Gui.Behaviors;

/// <summary>
/// <see cref="DataGridRowReorderBehavior"/> が、行の並べ替えのドラッグ元を押下の起点で決めることを検証するテストクラス。
/// </summary>
/// <remarks>
/// <para>
/// 起点が行ヘッダーでなければ並べ替えを始めない（セル編集と競合させない）。移動中の位置で判定すると、
/// セルで押してから行ヘッダーの上へ動かしただけで並べ替えが始まる。
/// </para>
/// <para>
/// 実際のドラッグ（<c>DragDrop.DoDragDrop</c>）はモーダルなのでテストでは開始できない。押下で記録した
/// ドラッグ元（移動ではこれだけを見る）を確かめる。押下はセル・行ヘッダーの中の要素から
/// <c>PreviewMouseDown</c> を発火させて流す（押下元がその要素になり、トンネルの途中で WPF が DataGrid に
/// <c>PreviewMouseLeftButtonDown</c> を発火させる。後者は直接ルートのイベントなので直接は発火させない）。
/// </para>
/// </remarks>
public class DataGridRowReorderBehaviorTests
{
    /// <summary>カラム 2 行の DataGrid を画面外のウィンドウへ表示し、ビヘイビアを有効にする</summary>
    private static (
        Window Window,
        DataGrid Grid,
        ObservableCollection<ColumnViewModel> Columns
    ) ShowGrid()
    {
        var columns = new ObservableCollection<ColumnViewModel>
        {
            new(new Column { Name = "Id", DataType = "int" }),
            new(new Column { Name = "Name", DataType = "nvarchar(50)" }),
        };
        var grid = new DataGrid
        {
            ItemsSource = columns,
            AutoGenerateColumns = false,
            HeadersVisibility = DataGridHeadersVisibility.All,
            RowHeaderWidth = 20,
        };
        grid.Columns.Add(
            new DataGridTextColumn { Binding = new Binding(nameof(ColumnViewModel.Name)) }
        );
        DataGridRowReorderBehavior.SetIsEnabled(grid, true);
        var window = new Window
        {
            Content = grid,
            Width = 300,
            Height = 200,
            ShowInTaskbar = false,
            ShowActivated = false,
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = -10000,
            Top = -10000,
        };
        window.Show();
        DoEvents();
        return (window, grid, columns);
    }

    /// <summary>視覚ツリーを下へたどり、最初に見つかった <typeparamref name="T"/> を返す</summary>
    private static T FindDescendant<T>(DependencyObject root)
        where T : DependencyObject =>
        FindDescendantOrNull<T>(root)
        ?? throw new InvalidOperationException($"{typeof(T).Name} が見つからない");

    /// <summary><see cref="FindDescendant{T}"/> の見つからなければ null を返す版</summary>
    private static T? FindDescendantOrNull<T>(DependencyObject root)
        where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);

            if (child is T typed)
            {
                return typed;
            }

            if (FindDescendantOrNull<T>(child) is { } found)
            {
                return found;
            }
        }

        return null;
    }

    /// <summary>指定要素の上で左ボタンを押す（押下元はその要素になる）</summary>
    private static void Press(UIElement element) =>
        element.RaiseEvent(
            new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
            {
                RoutedEvent = Mouse.PreviewMouseDownEvent,
            }
        );

    [Fact(
        DisplayName = "行ヘッダーで押すとその行のカラムをドラッグ元にし、セルで押すとドラッグ元を持たない"
    )]
    public void Press_RecordsSourceOnlyFromRowHeader()
    {
        RunSta(() =>
        {
            var (window, grid, columns) = ShowGrid();

            try
            {
                var secondRow = (DataGridRow)grid.ItemContainerGenerator.ContainerFromIndex(1);

                Press(FindDescendant<DataGridRowHeader>(secondRow));
                DataGridRowReorderBehavior
                    .GetDragSourceColumnForTests(grid)
                    .Should()
                    .BeSameAs(columns[1]);

                // セルで押した後は、移動中に行ヘッダーの上へ来ても並べ替えを始めない
                Press(FindDescendant<DataGridCell>(secondRow));
                DataGridRowReorderBehavior.GetDragSourceColumnForTests(grid).Should().BeNull();
            }
            finally
            {
                window.Close();
            }
        });
    }
}
