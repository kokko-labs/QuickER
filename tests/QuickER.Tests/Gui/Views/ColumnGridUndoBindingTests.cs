using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using AwesomeAssertions;
using QuickER.Model;
using QuickER.Tests.Gui.UndoRedo;
using QuickER.ViewModels;
using static QuickER.Tests.TestSupport.WpfApplicationTestSupport;

namespace QuickER.Tests.Gui.Views;

/// <summary>
/// 列一覧の NULL のチェックボックスを切り替えると、実ウィンドウの束縛を通って ViewModel へ届き、
/// 履歴に 1 件積まれ、Undo 1 回で元へ戻り、未保存になることを検証するテストクラス。
/// </summary>
/// <remarks>
/// <para>
/// ViewModel 単体のテスト（<see cref="TrackedPropertyUndoExhaustiveTests"/>）では、XAML の束縛の
/// <c>Mode</c>・<c>UpdateSourceTrigger</c>・束縛先の名前の誤りを捕まえられない。WPF は解決できない束縛を無言で捨てるので、
/// 実ウィンドウを表示して、チェックボックスを切り替えるところから確かめる。
/// </para>
/// <para>
/// チェックボックスの <see cref="ToggleButton.IsChecked"/> をコードから切り替えるだけで、キーボードフォーカスも
/// キー入力も使わない（ウィンドウは画面外・非アクティブで表示する）ので、アクティブ化に左右されず、
/// <see cref="TestSupport.WindowActivationTestCollection"/> には入れない。
/// </para>
/// </remarks>
public class ColumnGridUndoBindingTests
{
    [Fact(
        DisplayName = "列一覧の NULL のチェックボックスを切り替えると、履歴が 1 件積まれ、Undo 1 回で戻り、未保存になる"
    )]
    public void NullableCheckBox_Toggle_IsOneUndoEntry() =>
        UndoKnownFailures.Run(
            nameof(NullableCheckBox_Toggle_IsOneUndoEntry),
            () =>
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
                                            new Column
                                            {
                                                Name = "Id",
                                                DataType = "int",
                                                IsPrimaryKey = true,
                                                IsNullable = false,
                                            },
                                            new Column
                                            {
                                                Name = "Memo",
                                                DataType = "nvarchar(50)",
                                                IsNullable = true,
                                            },
                                        },
                                    },
                                },
                            }
                        );
                        vm.SelectedEntity = vm.Entities[0];
                        window.UpdateLayout();
                        DoEvents();

                        var memo = vm.SelectedEntity.Columns[1];
                        var generation = vm.UndoRedo.ChangeGeneration;

                        var checkBox = FindNullableCheckBox(window, memo);
                        checkBox.IsChecked.Should().BeTrue("束縛が現在の値を表示している");

                        checkBox.IsChecked = false;
                        DoEvents();

                        memo.IsNullable.Should()
                            .BeFalse("チェックボックスの操作が ViewModel の IsNullable へ届く");
                        vm.UndoRedo.CanUndo.Should().BeTrue("切り替えが履歴に積まれる");
                        vm.UndoRedo.ChangeGeneration.Should().BeGreaterThan(generation);

                        vm.UndoCommand.Execute(null);
                        DoEvents();

                        memo.IsNullable.Should().BeTrue("Undo 1 回で元に戻る");
                        checkBox.IsChecked.Should().BeTrue("画面のチェックボックスも元に戻る");
                        vm.UndoRedo.CanUndo.Should().BeFalse("履歴は 1 件だけ");
                    }
                )
        );

    /// <summary>列一覧から、指定した列の NULL のチェックボックスを取り出す</summary>
    private static CheckBox FindNullableCheckBox(MainWindow window, ColumnViewModel column)
    {
        var dataGrid = (DataGrid)window.FindName("ColumnsDataGrid")!;
        var nullColumn = dataGrid.Columns.Single(c => c.Header is "NULL");

        // 行のセルが実体化されるまでレイアウトを回す
        dataGrid.UpdateLayout();
        DoEvents();
        dataGrid.ScrollIntoView(column);
        dataGrid.UpdateLayout();
        DoEvents();

        var content = nullColumn.GetCellContent(column);
        content.Should().NotBeNull("列一覧の NULL 列のセルが実体化されている");

        return FindDescendant<CheckBox>(content!)
            ?? throw new InvalidOperationException("NULL 列のセルにチェックボックスが無い");
    }

    /// <summary>ビジュアルツリーを辿り、最初に見つかった指定型の子孫を返す（自分自身を含む）</summary>
    private static T? FindDescendant<T>(DependencyObject root)
        where T : DependencyObject
    {
        if (root is T self)
        {
            return self;
        }

        var count = VisualTreeHelper.GetChildrenCount(root);

        for (var i = 0; i < count; i++)
        {
            var found = FindDescendant<T>(VisualTreeHelper.GetChild(root, i));

            if (found is not null)
            {
                return found;
            }
        }

        return null;
    }
}
