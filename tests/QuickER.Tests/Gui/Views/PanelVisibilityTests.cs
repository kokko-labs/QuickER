using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
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

    /// <summary>全画面の切替で「表示」グループのポップアップが閉じることを検証する。</summary>
    /// <remarks>
    /// ポップアップは開いた時点の画面座標に留まりウィンドウへ追従しない（実機で確認）。閉じないと、
    /// ツールバーから全画面にした直後にポップアップだけが画面の真ん中へ取り残される。
    /// 他の 5 トグルは連続操作のため開いたままにするので、ここだけ意図的に非対称。
    /// </remarks>
    [Fact(DisplayName = "全画面表示: 切り替えると表示グループのポップアップは閉じる")]
    public void FullScreen_ClosesViewGroupPopup()
    {
        RunInIsolatedWindow(
            (vm, window) =>
            {
                var groupToggle = (ToggleButton)window.FindName("ViewGroupToggle")!;
                var popup = (Popup)window.FindName("ViewPopup")!;

                groupToggle.IsChecked = true;
                DoEvents();
                popup.IsOpen.Should().BeTrue();

                vm.IsFullScreen = true;
                DoEvents();

                popup.IsOpen.Should().BeFalse("ウィンドウの形が変わるので開いたままにしない");

                vm.IsFullScreen = false;
                DoEvents();
            }
        );
    }

    /// <summary>
    /// 全画面へ入るときに「いったん通常サイズを経由してから最大化する」順序そのものを固定する。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 最大化のまま枠だけ外すとタスクバーが前面に残る（実測＝クライアント領域が作業領域どまりの
    /// 2562x1394 になり、経由ありの 2560x1440 と食い違う）。ところが<b>最終状態はどちらも
    /// <c>None</c> / <c>Maximized</c> で同じ</b>なので、状態だけを見る表明では経由の有無を見分けられない。
    /// </para>
    /// <para>
    /// <see cref="Window.StateChanged"/> は状態の代入に対して同期的に発火するため、その並びを
    /// 表明すれば順序を固定できる。経由を省いた実装ではこのイベントが 1 度も起きない。
    /// 画面の解像度に依存しないので CI でも成立する。
    /// </para>
    /// </remarks>
    [Fact(DisplayName = "全画面表示: 最大化からでも通常サイズを経由してから最大化する")]
    public void FullScreen_FromMaximized_GoesThroughNormalState()
    {
        RunInIsolatedWindow(
            (vm, window) =>
            {
                var originalStyle = window.WindowStyle;
                var states = new List<(WindowState State, WindowStyle Style)>();
                window.StateChanged += (_, _) =>
                    states.Add((window.WindowState, window.WindowStyle));

                window.WindowState = WindowState.Maximized;
                DoEvents();
                states.Clear();

                vm.IsFullScreen = true;
                DoEvents();

                // 最大化のまま枠だけ外す実装だと StateChanged は 1 度も起きない
                states
                    .Should()
                    .Equal(
                        (WindowState.Normal, originalStyle),
                        (WindowState.Maximized, WindowStyle.None)
                    );
            }
        );
    }

    /// <summary>最小化しても全画面表示は解除されず、復帰すると全画面へ戻ることを検証する。</summary>
    /// <remarks>
    /// 外部要因での解除（<see cref="FullScreen_WhenRestoredExternally_ExitsAndRestoresChrome"/>）の条件を
    /// 「最大化でない」で書くと最小化まで拾ってしまい、Win+D で一度デスクトップを見て戻っただけで
    /// 全画面が解ける。最小化の意味は「いったん引っ込める」で、掴んで動かせないウィンドウも残らない。
    /// </remarks>
    [Fact(DisplayName = "全画面表示: 最小化では解除されず、復帰しても全画面のまま")]
    public void FullScreen_WhenMinimized_StaysFullScreen()
    {
        RunInIsolatedWindow(
            (vm, window) =>
            {
                vm.IsFullScreen = true;
                DoEvents();

                window.WindowState = WindowState.Minimized;
                DoEvents();

                vm.IsFullScreen.Should().BeTrue("最小化は「最大化をやめる」操作ではない");
                window.WindowStyle.Should().Be(WindowStyle.None);

                // 最小化からの復帰は最小化前の状態へ戻る（SW_RESTORE の意味論）。
                // 二重起動時の Program.ActivateMainWindow は Normal を代入するので、その形で確かめる
                // （ネイティブ側の復帰が先に解決するため StateChanged は Maximized で届き、
                // ハンドラは Normal を一度も観測しない＝全画面が解けない）
                window.WindowState = WindowState.Normal;
                DoEvents();

                vm.IsFullScreen.Should().BeTrue();
                window.WindowStyle.Should().Be(WindowStyle.None);
                window.WindowState.Should().Be(WindowState.Maximized);

                vm.IsFullScreen = false;
                DoEvents();
            }
        );
    }

    /// <summary>
    /// 全画面中に外部要因で最大化が解けたら、枠を戻して全画面もやめることを検証する。
    /// </summary>
    /// <remarks>
    /// Win+Down やシステムメニューの「元のサイズに戻す」は <c>SC_RESTORE</c> を送るため全画面中でも
    /// 最大化が解ける。放置すると枠が無いまま通常サイズ＝タイトルバーが無く掴んで動かせないウィンドウが
    /// 残る。状態は利用者の操作結果（通常サイズ）を尊重し、戻すのは枠だけにする。
    /// </remarks>
    [Fact(DisplayName = "全画面表示: 外部要因で最大化が解けたら枠を戻して全画面もやめる")]
    public void FullScreen_WhenRestoredExternally_ExitsAndRestoresChrome()
    {
        RunInIsolatedWindow(
            (vm, window) =>
            {
                var originalStyle = window.WindowStyle;

                vm.IsFullScreen = true;
                DoEvents();
                window.WindowStyle.Should().Be(WindowStyle.None);

                // SC_RESTORE 相当（Win+Down・システムメニュー）
                window.WindowState = WindowState.Normal;
                DoEvents();

                vm.IsFullScreen.Should().BeFalse("枠なしのまま通常サイズで取り残さない");
                window.WindowStyle.Should().Be(originalStyle);
                window.WindowState.Should().Be(WindowState.Normal, "最大化をやめる操作を覆さない");
            }
        );
    }

    /// <summary>全画面表示の切替が、ウィンドウの枠と状態へ届き、解除で元へ戻ることを検証する。</summary>
    /// <remarks>
    /// <para>
    /// 全画面は ViewModel からは触れないウィンドウの枠の話で、コードビハインドが
    /// <c>WindowStyle</c> / <c>WindowState</c> を当てる。束縛や配線ではないぶん、
    /// 適用そのものが抜け落ちても VM テストでは緑のままになる。
    /// </para>
    /// <para>
    /// すでに最大化されている状態から枠だけ外すとタスクバーが前面に残るため、実装は通常サイズを
    /// 経由してから最大化する。最大化で入った場合に解除後も最大化へ戻ることを併せて固定する。
    /// </para>
    /// </remarks>
    [Fact(DisplayName = "全画面表示: 枠を外して最大化し、解除で元の枠と状態へ戻る")]
    public void FullScreen_TogglesWindowChrome()
    {
        RunInIsolatedWindow(
            (vm, window) =>
            {
                var originalStyle = window.WindowStyle;
                window.WindowState.Should().Be(WindowState.Normal);

                vm.IsFullScreen = true;
                DoEvents();

                window.WindowStyle.Should().Be(WindowStyle.None);
                window.WindowState.Should().Be(WindowState.Maximized);

                vm.IsFullScreen = false;
                DoEvents();

                window.WindowStyle.Should().Be(originalStyle);
                window.WindowState.Should().Be(WindowState.Normal);

                // 最大化した状態から入ったときは、解除で最大化へ戻る
                window.WindowState = WindowState.Maximized;
                DoEvents();

                vm.IsFullScreen = true;
                DoEvents();
                window.WindowStyle.Should().Be(WindowStyle.None);
                window.WindowState.Should().Be(WindowState.Maximized);

                vm.IsFullScreen = false;
                DoEvents();
                window.WindowStyle.Should().Be(originalStyle);
                window.WindowState.Should().Be(WindowState.Maximized);
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
