using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using AwesomeAssertions;
using QuickER.Model;
using QuickER.Tests.Gui.UndoRedo;
using QuickER.ViewModels;
using static QuickER.Tests.TestSupport.WpfApplicationTestSupport;

namespace QuickER.Tests.Gui.Views;

/// <summary>
/// プロパティパネルで入力の途中（確定待ち）のまま、編集中のテーブルが図から外れる操作（Undo・削除）をしても、
/// 入力が図から外れたテーブルへ書き込まれず、例外も起きないことを検証するテストクラス。
/// </summary>
/// <remarks>
/// <para>
/// 入力欄は <c>UpdateSourceTrigger=LostFocus</c> で、確定はフォーカスが外れた瞬間に起きる。選択を外す処理が
/// 入力欄の確定より先に走ると、確定は外れたテーブルに対して行われる。外れたテーブルは変更追跡の外にあるため、
/// 書き込みは履歴にも未保存にもならず、Redo で同じインスタンスが戻ると記録のない編集ごと現れる。
/// </para>
/// <para>
/// 期待する結果は、確定待ちの入力は捨てられ（外れたテーブルへ書かない）、Redo で戻ったテーブルが
/// 履歴に記録された名前のままであること。フォーカスの移動を使うので並列に走らせない
/// （<see cref="TestSupport.WindowActivationTestCollection"/>）。
/// </para>
/// </remarks>
[Collection(TestSupport.WindowActivationTestCollection.Name)]
public class PendingEditOnDetachTests
{
    /// <summary>1 テーブルの図を読み込んで選択し、テーブル名の入力欄にフォーカスを置いて、確定待ちの入力を作る</summary>
    private static (EntityViewModel Entity, TextBox Box) StartPendingEdit(
        MainViewModel vm,
        MainWindow window,
        bool addThroughCommand
    )
    {
        vm.ReplaceDiagramFromModule(new ErDiagram());

        if (addThroughCommand)
        {
            vm.AddEntityCommand.Execute(null);
        }
        else
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
        }

        window.UpdateLayout();
        DoEvents();

        var entity = vm.SelectedEntity!;
        var box = FindBoundTextBox(window, "SelectedEntity.TableName");
        box.Focus();
        DoEvents();
        box.IsKeyboardFocused.Should().BeTrue("入力欄にフォーカスがある状態を作る");

        // 入力中＝LostFocus 待ちで、この時点ではまだモデルへ入らない
        box.Text = "Typed";
        DoEvents();
        entity.TableName.Should().NotBe("Typed");

        return (entity, box);
    }

    [Fact(
        DisplayName = "入力の途中でテーブルの追加を Undo しても、外れたテーブルへ入力が書き込まれず、Redo で記録どおりの名前に戻る"
    )]
    public void UndoOfAdd_WithPendingEdit_DoesNotWriteToDetachedEntity() =>
        UndoKnownFailures.Run(
            nameof(UndoOfAdd_WithPendingEdit_DoesNotWriteToDetachedEntity),
            () =>
                RunInIsolatedWindow(
                    (vm, window) =>
                    {
                        var (entity, _) = StartPendingEdit(vm, window, addThroughCommand: true);
                        var recorded = entity.TableName;

                        vm.UndoCommand.Execute(null);
                        DoEvents();

                        // 利用者のその後の操作（入力欄からフォーカスが外れる）で、確定待ちの入力が流れ込む機会を作る
                        MoveFocusAway(window);
                        DoEvents();
                        window.UpdateLayout();
                        DoEvents();

                        entity
                            .TableName.Should()
                            .Be(recorded, "図から外れたテーブルへ確定待ちの入力を書き込まない");

                        vm.RedoCommand.Execute(null);
                        DoEvents();

                        vm.Entities.Should().ContainSingle();
                        vm.Entities[0]
                            .TableName.Should()
                            .Be(recorded, "Redo で戻ったテーブルは履歴に記録された名前のまま");
                    }
                )
        );

    [Fact(
        DisplayName = "入力の途中でテーブルを削除しても、例外にならず、削除を Undo で戻したテーブルは記録どおりの名前になる"
    )]
    public void DeleteEntity_WithPendingEdit_DoesNotWriteToDetachedEntity() =>
        UndoKnownFailures.Run(
            nameof(DeleteEntity_WithPendingEdit_DoesNotWriteToDetachedEntity),
            () =>
                RunInIsolatedWindow(
                    (vm, window) =>
                    {
                        var (entity, _) = StartPendingEdit(vm, window, addThroughCommand: false);

                        vm.RemoveSelectedEntityCommand.Execute(null);
                        DoEvents();

                        MoveFocusAway(window);
                        DoEvents();
                        window.UpdateLayout();
                        DoEvents();

                        entity
                            .TableName.Should()
                            .Be("Original", "図から外れたテーブルへ確定待ちの入力を書き込まない");

                        vm.UndoCommand.Execute(null);
                        DoEvents();

                        vm.Entities.Should().ContainSingle();
                        vm.Entities[0]
                            .TableName.Should()
                            .Be("Original", "削除を戻したテーブルは記録どおりの名前");
                    }
                )
        );

    /// <summary>利用者のその後の操作（別の場所へフォーカスが移る）を再現し、確定待ちの入力が流れ込む機会を作る</summary>
    private static void MoveFocusAway(MainWindow window)
    {
        ((UIElement)window.FindName("DiagramScrollViewer")!).Focus();
        DoEvents();
    }

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
