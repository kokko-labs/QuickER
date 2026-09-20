using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using AwesomeAssertions;
using QuickER.Model;
using QuickER.Services;
using QuickER.ViewModels;
using static QuickER.Tests.TestSupport.WpfApplicationTestSupport;

namespace QuickER.Tests.Gui.Views;

/// <summary>
/// 左のツールボックス・右のプロパティパネルの表示トグルが、実ウィンドウの列レイアウトと
/// フォーカスへ正しく効くことを検証するテストクラス。
/// </summary>
/// <remarks>
/// ビヘイビア単体（<see cref="Gui.Behaviors.CollapsibleColumnBehaviorTests"/>）が正しくても、
/// XAML の添付が漏れていれば列は畳まれない。WPF は解決できない束縛を無言で捨てるため、
/// 実ウィンドウを表示して「VM のトグル → 実際に幅 0」まで通す。
/// </remarks>
public class PanelVisibilityTests
{
    /// <summary>ツールボックスの列</summary>
    private const int ToolboxColumn = 0;

    /// <summary>キャンバスの列</summary>
    private const int CanvasColumn = 1;

    /// <summary>プロパティパネルとの境界にある GridSplitter の列</summary>
    private const int SplitterColumn = 2;

    /// <summary>プロパティパネルの列</summary>
    private const int PropertyPanelColumn = 3;

    [Fact(DisplayName = "パネルの表示トグルが列幅と要素の可視性へ届く")]
    public void PanelToggles_CollapseColumns()
    {
        RunInIsolatedWindow(
            (vm, window) =>
            {
                var grid = (Grid)window.FindName("MainContentGrid")!;

                ColumnWidth(grid, ToolboxColumn).Should().Be(200);
                ColumnWidth(grid, PropertyPanelColumn).Should().BeGreaterThan(0);
                var fullCanvasWidth = ColumnWidth(grid, CanvasColumn);

                vm.IsToolboxVisible = false;
                vm.IsPropertyPanelVisible = false;
                window.UpdateLayout();

                ColumnWidth(grid, ToolboxColumn).Should().Be(0);
                ColumnWidth(grid, SplitterColumn).Should().Be(0);
                ColumnWidth(grid, PropertyPanelColumn).Should().Be(0);
                ColumnWidth(grid, CanvasColumn).Should().BeGreaterThan(fullCanvasWidth);

                // 0 幅の列に子が残るとフォーカス・タブ移動が隠れたパネルへ入るため、中身も畳む
                ChildInColumn(grid, ToolboxColumn).Visibility.Should().Be(Visibility.Collapsed);
                ChildInColumn(grid, SplitterColumn).Visibility.Should().Be(Visibility.Collapsed);
                ChildInColumn(grid, PropertyPanelColumn)
                    .Visibility.Should()
                    .Be(Visibility.Collapsed);

                vm.IsToolboxVisible = true;
                vm.IsPropertyPanelVisible = true;
                window.UpdateLayout();

                ColumnWidth(grid, ToolboxColumn).Should().Be(200);
                ColumnWidth(grid, SplitterColumn).Should().Be(5);
                ColumnWidth(grid, PropertyPanelColumn).Should().BeGreaterThan(0);
                ChildInColumn(grid, ToolboxColumn).Visibility.Should().Be(Visibility.Visible);
            }
        );
    }

    [Fact(DisplayName = "前回畳んだ状態は、起動直後のレイアウトへ復元される")]
    public void CollapsedState_IsRestoredOnStartup()
    {
        RunInIsolatedWindow(
            (vm, window) =>
            {
                var grid = (Grid)window.FindName("MainContentGrid")!;

                vm.IsToolboxVisible.Should().BeFalse();
                vm.IsPropertyPanelVisible.Should().BeFalse();

                ColumnWidth(grid, ToolboxColumn).Should().Be(0);
                ColumnWidth(grid, PropertyPanelColumn).Should().Be(0);
            },
            seed: folder =>
                new GuiAppSettingsStore(folder).Save(
                    new GuiAppSettings
                    {
                        Panels = new PanelVisibilitySettings
                        {
                            IsToolboxVisible = false,
                            IsPropertyPanelVisible = false,
                        },
                    }
                )
        );
    }

    /// <summary>
    /// 入力途中（LostFocus 待ち）のままプロパティパネルを畳んでも、入力がモデルへ確定することを検証する。
    /// </summary>
    /// <remarks>
    /// <para>
    /// プロパティパネルの入力欄は 7 本とも <c>UpdateSourceTrigger=LostFocus</c> で、確定はフォーカスが
    /// 外れた瞬間に起きる。ところが <see cref="UIElement.Visibility"/> を Collapsed にしても
    /// 論理フォーカス（<see cref="FocusManager"/> が保持する要素）は入力欄に残るため、
    /// <see cref="UIElement.LostFocus"/> が発火せず束縛が確定しない。
    /// </para>
    /// <para>
    /// 放置すると「パネルには打ち替えた文字列が出ているのにモデルは旧値」という食い違いが残り、
    /// 別のエンティティを選んだ時点で入力が黙って消える（保存されるのは旧値）。
    /// ツールバー経由ではボタンへフォーカスが移って先に確定するため、この経路は F9 / F10 だけで起きる。
    /// </para>
    /// </remarks>
    [Fact(DisplayName = "入力途中でプロパティパネルを畳んでも、入力はモデルへ確定する")]
    public void CollapsingPropertyPanel_CommitsPendingEdit()
    {
        RunInIsolatedWindow(
            (vm, window) =>
            {
                vm.ReplaceDiagramFromModule(
                    new ErDiagram
                    {
                        Entities =
                        {
                            new Entity
                            {
                                TableName = "Original",
                                Columns =
                                {
                                    new Column { Name = "Id", DataType = "int" },
                                },
                            },
                        },
                    }
                );
                vm.SelectedEntity = vm.Entities[0];
                window.UpdateLayout();
                DoEvents();

                var tableNameBox = FindBoundTextBox(window, "SelectedEntity.TableName");
                tableNameBox.Focus();
                DoEvents();
                tableNameBox
                    .IsKeyboardFocused.Should()
                    .BeTrue("入力欄にフォーカスがある状態を作る");

                // 入力中＝LostFocus 待ちで、この時点ではまだモデルへ入らない
                tableNameBox.Text = "Renamed";
                DoEvents();
                vm.SelectedEntity!.TableName.Should().Be("Original");

                vm.IsPropertyPanelVisible = false;
                window.UpdateLayout();
                DoEvents();

                vm.SelectedEntity.TableName.Should()
                    .Be("Renamed", "畳む操作で入力が捨てられてはいけない");
                BindingOperations
                    .GetBindingExpression(tableNameBox, TextBox.TextProperty)!
                    .IsDirty.Should()
                    .BeFalse("束縛が未確定のまま残ると、次の選択で打ち替えた文字列ごと消える");
            }
        );
    }

    /// <summary>
    /// 列グリッドのセル編集中にプロパティパネルを畳んでも、入力がモデルへ確定することを検証する。
    /// </summary>
    /// <remarks>
    /// 単票の入力欄（<see cref="CollapsingPropertyPanel_CommitsPendingEdit"/>）と同じ機構だが、
    /// こちらは <see cref="DataGrid"/> 自身のセル確定も噛むため、経路として別に固定する。
    /// </remarks>
    [Fact(DisplayName = "列グリッドのセル編集中に畳んでも、入力はモデルへ確定する")]
    public void CollapsingPropertyPanel_CommitsPendingCellEdit()
    {
        RunInIsolatedWindow(
            (vm, window) =>
            {
                vm.ReplaceDiagramFromModule(
                    new ErDiagram
                    {
                        Entities =
                        {
                            new Entity
                            {
                                TableName = "Orders",
                                Columns =
                                {
                                    new Column { Name = "OldName", DataType = "int" },
                                },
                            },
                        },
                    }
                );
                vm.SelectedEntity = vm.Entities[0];
                window.UpdateLayout();
                DoEvents();

                var dataGrid = (DataGrid)window.FindName("ColumnsDataGrid")!;
                var nameColumn = dataGrid
                    .Columns.OfType<DataGridTextColumn>()
                    .Single(column =>
                        (column.Binding as Binding)?.Path.Path == nameof(ColumnViewModel.Name)
                    );

                dataGrid.CurrentCell = new DataGridCellInfo(dataGrid.Items[0], nameColumn);
                dataGrid.BeginEdit();
                DoEvents();

                var editor = FindVisualChildren<TextBox>(dataGrid).Single(box => box.IsVisible);
                editor.Focus();
                editor.Text = "NewName";
                DoEvents();
                vm.SelectedEntity!.Columns[0].Name.Should().Be("OldName");

                vm.IsPropertyPanelVisible = false;
                window.UpdateLayout();
                DoEvents();

                vm.SelectedEntity.Columns[0]
                    .Name.Should()
                    .Be("NewName", "セル編集中に畳んでも入力を捨ててはいけない");
            }
        );
    }

    /// <summary>指定列の実幅</summary>
    private static double ColumnWidth(Grid grid, int column) =>
        grid.ColumnDefinitions[column].ActualWidth;

    /// <summary>指定列に置かれた直接の子要素</summary>
    private static UIElement ChildInColumn(Grid grid, int column) =>
        grid.Children.Cast<UIElement>().Single(child => Grid.GetColumn(child) == column);

    /// <summary>指定した束縛パスを Text に持つ TextBox を、ビジュアルツリーから 1 つだけ取り出す</summary>
    private static TextBox FindBoundTextBox(DependencyObject root, string path) =>
        FindVisualChildren<TextBox>(root)
            .Single(box =>
                BindingOperations.GetBinding(box, TextBox.TextProperty)?.Path.Path == path
            );

    /// <summary>ビジュアルツリーを深さ優先で辿り、指定型の子要素を列挙する</summary>
    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject root)
        where T : DependencyObject
    {
        var count = VisualTreeHelper.GetChildrenCount(root);

        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);

            if (child is T typed)
            {
                yield return typed;
            }

            foreach (var descendant in FindVisualChildren<T>(child))
            {
                yield return descendant;
            }
        }
    }
}
