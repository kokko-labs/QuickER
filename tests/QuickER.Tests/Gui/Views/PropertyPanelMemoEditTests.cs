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
/// プロパティパネルのメモ欄（複数行の入力欄）が、他の入力欄と同じ確定の仕方をすることを検証するテストクラス。
/// </summary>
/// <remarks>
/// <para>
/// メモ欄だけが <c>UpdateSourceTrigger=PropertyChanged</c> で、1 打鍵ごとに取り消しの履歴が積まれていた
/// （同じプロパティの連続変更をまとめる仕組みは無いため、5 文字打つと取り消しが 5 回必要になる）。
/// 他の入力欄と同じ既定（<c>LostFocus</c>）へそろえ、「打ち終えて欄から離れたら 1 回の変更」にする。
/// </para>
/// <para>
/// 束縛の書き方は XAML にしか現れないため、実ウィンドウを表示して確かめる
/// （<see cref="PanelVisibilityTests"/> と同じ流儀）。
/// </para>
/// </remarks>
public class PropertyPanelMemoEditTests
{
    /// <summary>メモ欄を持つエンティティを 1 つだけ置いた図を作って選択する</summary>
    private static void SelectSingleEntity(MainViewModel vm, MainWindow window)
    {
        vm.ReplaceDiagramFromModule(
            new ErDiagram
            {
                Entities =
                {
                    new Entity
                    {
                        TableName = "Orders",
                        Memo = string.Empty,
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
    }

    /// <summary>打鍵を模して 1 文字ずつ入力欄へ足す（キャレットは末尾）</summary>
    private static void TypeInto(TextBox box, string text)
    {
        foreach (var character in text)
        {
            box.Text += character;
            box.CaretIndex = box.Text.Length;
            DoEvents();
        }
    }

    [Fact(DisplayName = "メモ欄は打鍵ごとに取り消しの履歴を積まない（1 回で全部戻る）")]
    public void MemoEdit_IsUndoneInOneStep()
    {
        RunInIsolatedWindow(
            (vm, window) =>
            {
                SelectSingleEntity(vm, window);

                var memoBox = FindBoundTextBox(window, "SelectedEntity.Memo");
                var tableNameBox = FindBoundTextBox(window, "SelectedEntity.TableName");
                memoBox.Focus();
                DoEvents();

                TypeInto(memoBox, "hello");

                // 打っている間はまだモデルへ入らない（他の入力欄と同じ確定の仕方）
                vm.SelectedEntity!.Memo.Should().BeEmpty();

                // 欄から離れた時点で 1 回の変更として確定する
                tableNameBox.Focus();
                DoEvents();
                vm.SelectedEntity.Memo.Should().Be("hello");

                vm.UndoCommand.Execute(null);
                DoEvents();

                vm.SelectedEntity.Memo.Should()
                    .BeEmpty("5 打鍵は 1 つの変更なので、取り消し 1 回で入力前へ戻る");
                vm.UndoRedo.CanUndo.Should().BeFalse("打鍵ごとの履歴が残っていてはいけない");
            }
        );
    }

    /// <summary>複数行の入力欄として使えること（改行が入力へ残る）を検証する</summary>
    /// <remarks>
    /// 確定の仕方を変えても <c>AcceptsReturn</c> の挙動は変わらないが、
    /// メモは複数行で書く欄なので改行が失われないことまで固定する。
    /// </remarks>
    [Fact(DisplayName = "メモ欄は改行を含む入力をそのまま確定できる")]
    public void MemoEdit_KeepsLineBreaks()
    {
        RunInIsolatedWindow(
            (vm, window) =>
            {
                SelectSingleEntity(vm, window);

                var memoBox = FindBoundTextBox(window, "SelectedEntity.Memo");
                var tableNameBox = FindBoundTextBox(window, "SelectedEntity.TableName");
                memoBox.AcceptsReturn.Should().BeTrue("メモは複数行で書く欄");
                memoBox.Focus();
                DoEvents();

                memoBox.Text = "1 行目" + Environment.NewLine + "2 行目";
                tableNameBox.Focus();
                DoEvents();

                vm.SelectedEntity!.Memo.Should().Be("1 行目" + Environment.NewLine + "2 行目");
            }
        );
    }

    /// <summary>
    /// パネルを畳んだときに、メモ欄の入力途中の値もモデルへ確定することを検証する。
    /// </summary>
    /// <remarks>
    /// 確定を <c>LostFocus</c> にした以上、畳む操作で論理フォーカスを外へ出す配線
    /// （CLAUDE.md の不変条件「パネルを畳むときは論理フォーカスを残さない」）がメモ欄にも
    /// 効いていなければ、打った内容が黙って消える。
    /// </remarks>
    [Fact(DisplayName = "メモ欄の入力中にパネルを畳んでも、入力はモデルへ確定する")]
    public void CollapsingPropertyPanel_CommitsPendingMemoEdit()
    {
        RunInIsolatedWindow(
            (vm, window) =>
            {
                SelectSingleEntity(vm, window);

                var memoBox = FindBoundTextBox(window, "SelectedEntity.Memo");
                memoBox.Focus();
                DoEvents();
                memoBox.IsKeyboardFocused.Should().BeTrue("入力欄にフォーカスがある状態を作る");

                memoBox.Text = "書きかけ";
                DoEvents();
                vm.SelectedEntity!.Memo.Should().BeEmpty();

                vm.IsPropertyPanelVisible = false;
                window.UpdateLayout();
                DoEvents();

                vm.SelectedEntity.Memo.Should()
                    .Be("書きかけ", "畳む操作で入力が捨てられてはいけない");
                BindingOperations
                    .GetBindingExpression(memoBox, TextBox.TextProperty)!
                    .IsDirty.Should()
                    .BeFalse("束縛に未確定の入力が残っていてはいけない");
            }
        );
    }

    /// <summary>指定した束縛パスを持つ <see cref="TextBox"/> を探す</summary>
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
