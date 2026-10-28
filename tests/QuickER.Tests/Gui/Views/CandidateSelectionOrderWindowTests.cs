using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using AwesomeAssertions;
using QuickER.Model;
using QuickER.Tests.TestSupport;
using QuickER.Tests.TestSupport.Engines;
using QuickER.ViewModels;
using static QuickER.Tests.TestSupport.WpfApplicationTestSupport;

namespace QuickER.Tests.Gui.Views;

/// <summary>
/// 列ペアの行・構成列の行の先頭を外したとき（残りの行の選択が繰り上がるとき）に、画面のコンボボックスが
/// 正本と同じ列を示し、選択の書き戻しで正本や履歴が変わらないこと、
/// 言語メニューのチェックがクリックで正本から外れないことを、実ウィンドウで固定するテストクラス。
/// </summary>
/// <remarks>
/// <para>
/// コンボボックスは、選択中の項目が ItemsSource に無いと選択を落とす（<c>null</c> を書き戻す）か、
/// 新しい選択を受け付けずに古い表示のまま残る。どちらも VM の段では起きないので、
/// 画面外（Left/Top=-4000）・非アクティブで表示した実ウィンドウ上で確かめる。順序そのものは
/// <c>CandidateRowReconcileTests</c> が VM の段で数える。
/// </para>
/// <para>既知の失敗は台帳（<see cref="Known"/>）に載せる。突き合わせの規則は <see cref="KnownCaseLedger"/>。</para>
/// </remarks>
public class CandidateSelectionOrderWindowTests
{
    /// <summary>既知の失敗（ケース ID → 期待する失敗の理由の断片）</summary>
    private static readonly KnownCaseLedger Known = new("CandidateSelectionOrderWindowTests", []);

    [Fact(
        DisplayName = "外部キーの先頭の行を外すと、画面の残りの行は繰り上がった列を示し、履歴は 1 回だけ進む"
    )]
    public void ForeignKeyFirstRowRemoval_ComboBoxesShowShiftedPair()
    {
        Known.Run("fk-first-row", () => RunInIsolatedWindow(AssertForeignKeyFirstRowRemoval));
    }

    /// <summary>表示済みウィンドウ上で、複合外部キーの先頭の行を外したときのコンボボックスを確かめる</summary>
    private static void AssertForeignKeyFirstRowRemoval(MainViewModel vm, MainWindow window)
    {
        vm.ReplaceDiagramFromModule(CreateCompositeForeignKeyDiagram());
        var relationship = vm.Relationships.Should().ContainSingle().Subject;
        vm.OnRelationshipClicked(relationship);
        window.UpdateLayout();
        DoEvents();

        var items = (ItemsControl)window.FindName("RelationshipColumnPairItems")!;
        items.Items.Count.Should().Be(2);

        var generation = vm.UndoRedo.ChangeGeneration;
        vm.RemoveRelationshipColumnPairCommand.Execute(relationship.ColumnPairRows[0]);
        window.UpdateLayout();
        DoEvents();

        relationship
            .ColumnPairs.Select(pair =>
                (
                    relationship.Source.Columns.Single(c => c.Id == pair.SourceColumnId).Name,
                    relationship.Target.Columns.Single(c => c.Id == pair.TargetColumnId).Name
                )
            )
            .Should()
            .Equal(("Code", "ParentCode"));
        items.Items.Count.Should().Be(1);
        RowComboBoxNames(items, 0).Should().Equal("Code", "ParentCode");
        vm.UndoRedo.ChangeGeneration.Should().Be(generation + 1, "行の削除だけが履歴に入る");

        vm.UndoCommand.Execute(null);
        window.UpdateLayout();
        DoEvents();

        relationship.ColumnPairs.Should().HaveCount(2);
        RowComboBoxNames(items, 0).Should().Equal("Id", "ParentId");
        RowComboBoxNames(items, 1).Should().Equal("Code", "ParentCode");
        vm.UndoRedo.ChangeGeneration.Should().Be(generation + 2);
    }

    [Fact(
        DisplayName = "一意制約の先頭の行を外すと、画面の残りの行は繰り上がった列を示し、履歴は 1 回だけ進む"
    )]
    public void UniqueFirstMemberRemoval_ComboBoxShowsShiftedColumn()
    {
        Known.Run("uq-first-row", () => RunInIsolatedWindow(AssertUniqueFirstMemberRemoval));
    }

    /// <summary>表示済みウィンドウ上で、一意制約の先頭の構成列を外したときのコンボボックスを確かめる</summary>
    private static void AssertUniqueFirstMemberRemoval(MainViewModel vm, MainWindow window)
    {
        var a = new Column { Name = "A", DataType = "int" };
        var b = new Column { Name = "B", DataType = "int" };
        vm.ReplaceDiagramFromModule(
            new ErDiagram
            {
                Entities =
                {
                    new Entity
                    {
                        TableName = "Item",
                        Columns =
                        {
                            new Column
                            {
                                Name = "Id",
                                DataType = "int",
                                IsPrimaryKey = true,
                            },
                            a,
                            b,
                        },
                        UniqueConstraints = { new UniqueConstraint { ColumnIds = [a.Id, b.Id] } },
                    },
                },
            }
        );
        var entity = vm.Entities.Single();
        vm.OnEntityClicked(entity);
        window.UpdateLayout();
        DoEvents();

        var constraint = entity.UniqueConstraints.Single();
        var cards = (ItemsControl)window.FindName("UniqueConstraintsItems")!;
        var card = (FrameworkElement)cards.ItemContainerGenerator.ContainerFromIndex(0)!;
        card.UpdateLayout();

        var generation = vm.UndoRedo.ChangeGeneration;
        vm.RemoveUniqueConstraintMemberCommand.Execute(constraint.Members[0]);
        window.UpdateLayout();
        DoEvents();

        constraint
            .ColumnIds.Select(id => entity.Columns.Single(c => c.Id == id).Name)
            .Should()
            .Equal("B");
        ComboBoxNames(card).Should().Equal("B");
        vm.UndoRedo.ChangeGeneration.Should().Be(generation + 1, "行の削除だけが履歴に入る");

        vm.UndoCommand.Execute(null);
        window.UpdateLayout();
        DoEvents();

        ComboBoxNames(card).Should().Equal("A", "B");
        vm.UndoRedo.ChangeGeneration.Should().Be(generation + 2);
    }

    [Fact(DisplayName = "言語メニューで今の言語をもう一度選んでも、チェックは外れない")]
    public void LanguageMenu_ReselectingCurrentLanguage_KeepsCheck()
    {
        Known.Run("language-menu-reselect", () => RunInIsolatedWindow(AssertLanguageMenuReselect));
    }

    /// <summary>表示済みウィンドウ上で、チェックの付いた言語の項目をクリックしたあとのチェックを確かめる</summary>
    private static void AssertLanguageMenuReselect(MainViewModel vm, MainWindow window)
    {
        var button = (Button)window.FindName("SettingsButton")!;
        var menu = button.ContextMenu!;
        menu.PlacementTarget = button;
        menu.IsOpen = true;
        DoEvents();

        try
        {
            var languageItems = ((MenuItem)menu.Items[0]!).Items.Cast<MenuItem>().ToList();
            var current = languageItems.Single(item => item.IsChecked);
            var other = languageItems.Single(item => !ReferenceEquals(item, current));

            // クリックと同じ処理（チェックの切り替え・Click・コマンドの実行）を通す
            typeof(MenuItem)
                .GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(current, null);
            DoEvents();

            current.IsChecked.Should().BeTrue("今の言語を選び直してもチェックは外れない");
            other.IsChecked.Should().BeFalse();

            // 選んでいない側はクリックすると保存と再起動の確認に進むので、ここでは押せない。
            // チェックを自分で切り替えない（チェックは束縛だけが決める）ことを両方の項目で確かめる
            languageItems
                .Should()
                .OnlyContain(
                    item => !item.IsCheckable,
                    "言語の項目はクリックでチェックを切り替えない"
                );
        }
        finally
        {
            menu.IsOpen = false;
            DoEvents();
        }
    }

    /// <summary>
    /// 親（Id＝主キー・Code＝一意制約）と子（ChildId・ParentId・ParentCode）を、
    /// (Id → ParentId)・(Code → ParentCode) の複合外部キーで結んだ図を作る
    /// </summary>
    private static ErDiagram CreateCompositeForeignKeyDiagram()
    {
        var id = new Column
        {
            Name = "Id",
            DataType = "int",
            IsPrimaryKey = true,
        };
        var code = new Column { Name = "Code", DataType = "nvarchar(20)" };
        var parentId = new Column { Name = "ParentId", DataType = "int" };
        var parentCode = new Column { Name = "ParentCode", DataType = "nvarchar(20)" };
        var parent = new Entity
        {
            TableName = "Parent",
            Columns = { id, code },
            UniqueConstraints = { new UniqueConstraint { ColumnIds = [code.Id] } },
        };
        var child = new Entity
        {
            TableName = "Child",
            Columns =
            {
                new Column
                {
                    Name = "ChildId",
                    DataType = "int",
                    IsPrimaryKey = true,
                },
                parentId,
                parentCode,
            },
        };

        return new ErDiagram
        {
            Entities = { parent, child },
            Relationships =
            {
                new Relationship
                {
                    SourceEntityId = parent.Id,
                    TargetEntityId = child.Id,
                    ColumnPairs =
                    {
                        new RelationshipColumnPair(id.Id, parentId.Id),
                        new RelationshipColumnPair(code.Id, parentCode.Id),
                    },
                },
            },
        };
    }

    /// <summary>列ペアの行 1 つのコンボボックスが示す列名（親列・子列の順）</summary>
    private static List<string?> RowComboBoxNames(ItemsControl items, int index)
    {
        var container = (FrameworkElement)items.ItemContainerGenerator.ContainerFromIndex(index)!;
        container.UpdateLayout();

        return ComboBoxNames(container);
    }

    /// <summary>要素の下にあるコンボボックスが示す列名（並び順）</summary>
    private static List<string?> ComboBoxNames(DependencyObject root) =>
        FindVisualChildren<ComboBox>(root)
            .Select(comboBox => (comboBox.SelectedItem as ColumnViewModel)?.Name)
            .ToList();

    /// <summary>ビジュアルツリーを深さ優先で辿り、指定型の子要素を列挙する</summary>
    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject root)
        where T : DependencyObject
    {
        var count = System.Windows.Media.VisualTreeHelper.GetChildrenCount(root);

        for (var i = 0; i < count; i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(root, i);

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
