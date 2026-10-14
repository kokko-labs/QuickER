using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using AwesomeAssertions;
using QuickER.Model;
using QuickER.ViewModels;
using static QuickER.Tests.TestSupport.WpfApplicationTestSupport;

namespace QuickER.Tests.Gui.Views;

/// <summary>
/// プロパティパネルのテーブル名欄が、同じ名前（<see cref="SchemaNames.AreSame"/> の規則）のテーブルがある書き換えを
/// 受け付けず、入力欄へ元の名前を戻してステータスバーで知らせる（変更履歴には残さない）ことを検証するテストクラス。
/// </summary>
/// <remarks>
/// 入力欄の再表示は束縛の挙動（ソースが値を拒否したあとに入力欄が元の値へ戻るか）に依るため、実ウィンドウで確かめる
/// （<see cref="PropertyPanelMemoEditTests"/> と同じ流儀）。確定はキーボードフォーカスの移動に頼るので並列に走らせない。
/// </remarks>
[Collection(TestSupport.WindowActivationTestCollection.Name)]
public class PropertyPanelNameEditTests
{
    /// <summary>Orders と Items の 2 テーブルを置き、Items を選択する</summary>
    private static void SelectItems(MainViewModel vm, MainWindow window)
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
                            new Column { Name = "Id", DataType = "int" },
                        },
                    },
                    new Entity
                    {
                        TableName = "Items",
                        Columns =
                        {
                            new Column { Name = "Id", DataType = "int" },
                        },
                    },
                },
            }
        );
        vm.SelectedEntity = vm.Entities[1];
        window.UpdateLayout();
        DoEvents();
    }

    [Fact(
        DisplayName = "テーブル名欄: 同じ名前（大文字小文字・前後の空白違い）のテーブルがあれば書き換えず、欄を元に戻して通知する"
    )]
    public void TableNameEdit_DuplicateIsRejectedAndRestored()
    {
        RunInIsolatedWindow(
            (vm, window) =>
            {
                SelectItems(vm, window);

                var tableNameBox = FindBoundTextBox(window, "SelectedEntity.TableName");
                var memoBox = FindBoundTextBox(window, "SelectedEntity.Memo");
                tableNameBox.Focus();
                DoEvents();

                tableNameBox.Text = "  orders ";
                memoBox.Focus();
                DoEvents();

                vm.SelectedEntity!.TableName.Should().Be("Items", "重複する名前は受け付けない");
                tableNameBox.Text.Should().Be("Items", "拒否したら入力欄は元の名前へ戻る");
                vm.StatusMessage.Should().Contain("Orders", "どのテーブルと重複したかを知らせる");
                vm.UndoRedo.CanUndo.Should().BeFalse("拒否した書き換えは履歴に残さない");
            }
        );
    }

    [Fact(
        DisplayName = "テーブル名欄: 前後の空白は取り除いた名前で確定し、欄にも取り除いた形が出る（履歴は 1 回）"
    )]
    public void TableNameEdit_SurroundingSpacesAreTrimmed()
    {
        RunInIsolatedWindow(
            (vm, window) =>
            {
                SelectItems(vm, window);

                var tableNameBox = FindBoundTextBox(window, "SelectedEntity.TableName");
                var memoBox = FindBoundTextBox(window, "SelectedEntity.Memo");
                tableNameBox.Focus();
                DoEvents();

                tableNameBox.Text = "  Parts ";
                memoBox.Focus();
                DoEvents();

                vm.SelectedEntity!.TableName.Should().Be("Parts");
                tableNameBox.Text.Should().Be("Parts");

                vm.UndoCommand.Execute(null);
                DoEvents();

                vm.SelectedEntity.TableName.Should().Be("Items");
                vm.UndoRedo.CanUndo.Should().BeFalse();
            }
        );
    }

    /// <summary>束縛パスで入力欄を 1 つに特定する</summary>
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
