using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using AwesomeAssertions;
using QuickER.Model;
using QuickER.ViewModels;
using static QuickER.Tests.TestSupport.WpfApplicationTestSupport;

namespace QuickER.Tests.Gui.Views;

/// <summary>
/// プロパティパネルの列一覧（ColumnsDataGrid）で Delete を押したときの振る舞いを、実ウィンドウで検証するテストクラス。
/// </summary>
/// <remarks>
/// <para>
/// 列一覧の Delete は、行の削除ボタンと同じ列の削除のコマンドを通る（Undo でき・未保存になり・その列を含む一意制約も外れる）。
/// DataGrid の既定の行の削除（CanUserDeleteRows）は正本のコレクションから列を直接取り除き、履歴にも未保存にも残らない。
/// 列を選んでいないときも窓全体の Delete（テーブルの削除）へは倒れず、セルの編集中の Delete は文字の削除のまま。
/// </para>
/// <para>
/// キーは <see cref="InputManager.ProcessInput"/> でフォーカスのある要素へ流す（DataGrid の入力束縛・窓の入力束縛・
/// 編集中の TextBox の処理が製品と同じ順に試される）。フォーカスに結果が左右されるので並列に走らせない
/// （<see cref="TestSupport.WindowActivationTestCollection"/>）。
/// </para>
/// </remarks>
[Collection(TestSupport.WindowActivationTestCollection.Name)]
public class ColumnGridDeleteKeyTests
{
    /// <summary>
    /// 親（Parents.Id）と子（Orders.Id＝主キー・Orders.ParentId＝リレーションの子列・Orders.Memo＝一意制約の構成列）を置き、
    /// Orders を選ぶ
    /// </summary>
    private static EntityViewModel ShowOrders(MainViewModel vm, MainWindow window)
    {
        var parentId = new Column
        {
            Name = "Id",
            DataType = "int",
            IsPrimaryKey = true,
            IsNullable = false,
        };
        var parents = new Entity { TableName = "Parents", Columns = { parentId } };

        var orderId = new Column
        {
            Name = "Id",
            DataType = "int",
            IsPrimaryKey = true,
            IsNullable = false,
        };
        var orderParentId = new Column
        {
            Name = "ParentId",
            DataType = "int",
            IsNullable = true,
            IsForeignKey = true,
        };
        var memo = new Column
        {
            Name = "Memo",
            DataType = "nvarchar(50)",
            IsNullable = true,
        };
        var orders = new Entity
        {
            TableName = "Orders",
            Columns = { orderId, orderParentId, memo },
        };
        orders.UniqueConstraints.Add(
            new UniqueConstraint { Name = "UQ_Orders_Memo", ColumnIds = { memo.Id } }
        );

        vm.ReplaceDiagramFromModule(
            new ErDiagram
            {
                Entities = { parents, orders },
                Relationships =
                {
                    new Relationship
                    {
                        SourceEntityId = parents.Id,
                        TargetEntityId = orders.Id,
                        Type = RelationshipType.OneToMany,
                        ColumnPairs =
                        {
                            new RelationshipColumnPair
                            {
                                SourceColumnId = parentId.Id,
                                TargetColumnId = orderParentId.Id,
                            },
                        },
                    },
                },
            }
        );
        vm.UndoRedo.Clear();

        var ordersVm = vm.Entities.Single(e => e.TableName == "Orders");
        vm.SelectedEntity = ordersVm;
        window.UpdateLayout();
        DoEvents();

        return ordersVm;
    }

    /// <summary>図全体の保存内容（意味モデル＋レイアウト）を文字列にする</summary>
    private static string Snapshot(MainViewModel vm) => JsonSerializer.Serialize(vm.ToDocument());

    /// <summary>列一覧を取り出し、行のセルが実体化されるまでレイアウトを回す</summary>
    private static DataGrid ColumnsGrid(MainWindow window)
    {
        var dataGrid = (DataGrid)window.FindName("ColumnsDataGrid")!;
        dataGrid.UpdateLayout();
        DoEvents();

        return dataGrid;
    }

    /// <summary>指定した列の名前のセルを取り出す</summary>
    private static DataGridCell NameCell(DataGrid dataGrid, ColumnViewModel column)
    {
        dataGrid.ScrollIntoView(column);
        dataGrid.UpdateLayout();
        DoEvents();

        var nameColumn = dataGrid.Columns.OfType<DataGridTextColumn>().First();
        var content = nameColumn.GetCellContent(column);
        content.Should().NotBeNull("名前のセルが実体化されている");

        return FindAncestor<DataGridCell>(content!)
            ?? throw new InvalidOperationException("名前のセルが見つからない");
    }

    /// <summary>キーの押下をフォーカスのある要素へ流す（PreviewKeyDown → KeyDown の順に届く）</summary>
    private static void PressKey(Window window, Key key)
    {
        var source = PresentationSource.FromVisual(window)!;
        InputManager.Current.ProcessInput(
            new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, key)
            {
                RoutedEvent = Keyboard.PreviewKeyDownEvent,
            }
        );
        InputManager.Current.ProcessInput(
            new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, key)
            {
                RoutedEvent = Keyboard.PreviewKeyUpEvent,
            }
        );
        DoEvents();
    }

    [Theory(
        DisplayName = "列一覧で列を選んで Delete を押すと、行の削除ボタンと同じ結果になり、Undo 1 回で戻り、未保存になる"
    )]
    [InlineData("Memo")]
    [InlineData("Id")]
    [InlineData("ParentId")]
    public void DeleteKey_OnSelectedColumn_MatchesRowDeleteButton(string columnName) =>
        RunInIsolatedWindow(
            (vm, window) =>
            {
                var orders = ShowOrders(vm, window);
                var target = orders.Columns.Single(c => c.Name == columnName);
                var before = Snapshot(vm);
                var dataGrid = ColumnsGrid(window);

                vm.SelectedColumn = target;
                DoEvents();
                dataGrid.SelectedItem.Should().BeSameAs(target);

                var cell = NameCell(dataGrid, target);
                cell.Focus();
                DoEvents();
                Keyboard.FocusedElement.Should().BeSameAs(cell, "名前のセルにフォーカスがある");

                PressKey(window, Key.Delete);

                vm.Entities.Should().HaveCount(2, "テーブルは消えない");
                orders.Columns.Should().NotContain(target, "Delete で列が消える");
                vm.UndoRedo.CanUndo.Should().BeTrue("列の削除が履歴に積まれる");
                vm.IsDirty.Should().BeTrue("列の削除で未保存になる");
                var byKey = Snapshot(vm);

                vm.UndoCommand.Execute(null);
                DoEvents();

                Snapshot(vm).Should().Be(before, "Undo 1 回で図全体が元に戻る");
                vm.UndoRedo.CanUndo.Should().BeFalse("履歴は 1 件だけ");

                // 同じ列を行の削除ボタン（RemoveColumnCommand）で消した結果と一致する
                vm.RemoveColumnCommand.Execute(target);
                DoEvents();

                Snapshot(vm).Should().Be(byKey, "列一覧の Delete は行の削除ボタンと同じ結果になる");
            }
        );

    [Fact(
        DisplayName = "列一覧にフォーカスがあり列を選んでいないときの Delete は、何も消さない（テーブルの削除へ倒れない）"
    )]
    public void DeleteKey_WithoutSelectedColumn_DeletesNothing() =>
        RunInIsolatedWindow(
            (vm, window) =>
            {
                var orders = ShowOrders(vm, window);
                var before = Snapshot(vm);
                var dataGrid = ColumnsGrid(window);

                vm.SelectedColumn = null;
                DoEvents();
                dataGrid.SelectedItem.Should().BeNull();

                dataGrid.Focus();
                DoEvents();
                Keyboard.FocusedElement.Should().BeSameAs(dataGrid, "列一覧にフォーカスがある");

                PressKey(window, Key.Delete);

                vm.Entities.Should().HaveCount(2, "窓全体の Delete（テーブルの削除）へ倒れない");
                vm.Entities.Should().Contain(orders);
                orders.Columns.Should().HaveCount(3, "列も消えない");
                vm.UndoRedo.CanUndo.Should().BeFalse("履歴は積まれない");
                Snapshot(vm).Should().Be(before);
            }
        );

    [Fact(DisplayName = "列一覧の名前のセルを編集中の Delete は、文字を消すだけで列を消さない")]
    public void DeleteKey_WhileEditingNameCell_DeletesCharacter() =>
        RunInIsolatedWindow(
            (vm, window) =>
            {
                var orders = ShowOrders(vm, window);
                var memo = orders.Columns.Single(c => c.Name == "Memo");
                var dataGrid = ColumnsGrid(window);

                vm.SelectedColumn = memo;
                DoEvents();

                var cell = NameCell(dataGrid, memo);
                cell.Focus();
                dataGrid.BeginEdit();
                DoEvents();

                var editor = FindDescendant<TextBox>(cell);
                editor.Should().NotBeNull("名前のセルが編集中になっている");
                editor!.Focus();
                editor.CaretIndex = 0;
                DoEvents();
                Keyboard.FocusedElement.Should().BeSameAs(editor);

                PressKey(window, Key.Delete);

                editor.Text.Should().Be("emo", "編集中の Delete は先頭の文字を消す");
                orders.Columns.Should().Contain(memo, "列は消えない");
                vm.Entities.Should().HaveCount(2);
            }
        );

    [Fact(DisplayName = "列一覧の型のセルを編集中の Delete は、文字を消すだけで列を消さない")]
    public void DeleteKey_WhileEditingDataTypeCell_DeletesCharacter() =>
        RunInIsolatedWindow(
            (vm, window) =>
            {
                var orders = ShowOrders(vm, window);
                var memo = orders.Columns.Single(c => c.Name == "Memo");
                var dataGrid = ColumnsGrid(window);

                vm.SelectedColumn = memo;
                DoEvents();

                var typeColumn = dataGrid
                    .Columns.OfType<DataGridTemplateColumn>()
                    .Single(c => c.CellEditingTemplate is not null);
                dataGrid.ScrollIntoView(memo);
                dataGrid.UpdateLayout();
                DoEvents();
                var cell = FindAncestor<DataGridCell>(typeColumn.GetCellContent(memo)!)!;
                cell.Focus();
                dataGrid.BeginEdit();
                DoEvents();

                var editor = FindDescendant<TextBox>(cell);
                editor.Should().NotBeNull("型のセルが編集中になり、入力欄がある");
                editor!.Focus();
                editor.CaretIndex = 0;
                DoEvents();
                Keyboard.FocusedElement.Should().BeSameAs(editor);
                var text = editor.Text;

                PressKey(window, Key.Delete);

                editor.Text.Should().Be(text[1..], "編集中の Delete は先頭の文字を消す");
                orders.Columns.Should().Contain(memo, "列は消えない");
                vm.Entities.Should().HaveCount(2);
            }
        );

    /// <summary>ビジュアルツリーを親へ辿り、最初に見つかった指定型の祖先を返す（自分自身を含む）</summary>
    private static T? FindAncestor<T>(DependencyObject start)
        where T : DependencyObject
    {
        var current = start;

        while (current is not null)
        {
            if (current is T found)
            {
                return found;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return null;
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
