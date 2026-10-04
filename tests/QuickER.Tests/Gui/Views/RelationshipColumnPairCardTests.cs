using System.Windows;
using System.Windows.Controls;
using AwesomeAssertions;
using QuickER.Model;
using QuickER.ViewModels;
using Xunit;
using static QuickER.Tests.TestSupport.WpfApplicationTestSupport;

namespace QuickER.Tests.Gui.Views;

/// <summary>
/// プロパティパネルのリレーション「キー列の対応」エディタの XAML 配線（列ペア行の実体化・親列と子列の
/// コンボボックスと ＋ / × ボタンのコマンド束縛と実行・未選択プレースホルダー）を検証するテストクラス。
/// </summary>
/// <remarks>
/// 列ペアの編集は「行の並び＝宣言順、候補は他行の未使用列、両側が揃って確定」という束縛の組み合わせで
/// 成り立っており、VM テストでは束縛そのものを守れない。画面外
/// （Left/Top=-4000）・非アクティブで Show した実ウィンドウ上でコンテナを実体化して検証する。
/// </remarks>
public class RelationshipColumnPairCardTests
{
    /// <summary>列ペア行の実体化・コマンド束縛・両側選択での確定を検証する</summary>
    [Fact(
        DisplayName = "リレーションのキー列エディタ: 列ペア行が実体化し、両側の選択で複合外部キーになる"
    )]
    public void RelationshipColumnPairEditor_RowWiring()
    {
        RunInIsolatedWindow(AssertRelationshipColumnPairEditor);
    }

    /// <summary>表示済みウィンドウ上でキー列エディタの実体化とコマンド配線を検証する</summary>
    private static void AssertRelationshipColumnPairEditor(MainViewModel vm, MainWindow window)
    {
        var code = new Column { Name = "Code", DataType = "nvarchar(20)" };
        vm.ReplaceDiagramFromModule(
            new ErDiagram
            {
                Entities =
                {
                    new Entity
                    {
                        TableName = "Parent",
                        Columns =
                        {
                            new Column
                            {
                                Name = "Id",
                                DataType = "int",
                                IsPrimaryKey = true,
                            },
                            code,
                        },
                        UniqueConstraints = { new UniqueConstraint { ColumnIds = [code.Id] } },
                    },
                    new Entity
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
                            new Column { Name = "ParentId", DataType = "int" },
                            new Column { Name = "ParentCode", DataType = "nvarchar(20)" },
                        },
                    },
                },
            }
        );

        // リレーションを作ると親 PK（Id）と子の ParentId が自動で 1 組にペア化される
        vm.StartAddOneToManyCommand.Execute(null);
        vm.OnEntityClicked(vm.Entities[0]);
        vm.OnEntityClicked(vm.Entities[1]);

        var relationship = vm.Relationships.Should().ContainSingle().Subject;
        vm.OnRelationshipClicked(relationship);
        window.UpdateLayout();
        DoEvents();

        var items = (ItemsControl)window.FindName("RelationshipColumnPairItems")!;
        items.Items.Count.Should().Be(1, "自動ペア化された 1 組が行として出る");

        var firstContainer = (FrameworkElement)items.ItemContainerGenerator.ContainerFromIndex(0)!;
        firstContainer.UpdateLayout();

        var firstRowComboBoxes = FindVisualChildren<ComboBox>(firstContainer).ToList();
        firstRowComboBoxes
            .Should()
            .HaveCount(2, "1 行は親列と子列の 2 つのコンボボックスで構成する");
        firstRowComboBoxes[0]
            .ItemsSource.Should()
            .BeSameAs(relationship.ColumnPairRows[0].AvailableSourceColumns);
        firstRowComboBoxes[1]
            .ItemsSource.Should()
            .BeSameAs(relationship.ColumnPairRows[0].AvailableTargetColumns);
        ((ColumnViewModel)firstRowComboBoxes[0].SelectedItem).Name.Should().Be("Id");
        ((ColumnViewModel)firstRowComboBoxes[1].SelectedItem).Name.Should().Be("ParentId");

        // 「＋」は空スロット追加コマンドへ束縛され、対象のリレーションをパラメーターに持つ
        var addPairButton = FindCommandButton(window, vm.AddRelationshipColumnPairSlotCommand);
        addPairButton.CommandParameter.Should().BeSameAs(relationship);
        addPairButton.IsEnabled.Should().BeTrue("親側に未使用の候補キー列（UNIQUE の Code）がある");

        // クリック時に WPF が行うのと同じ「解決済み束縛の実行」を行う
        // （ButtonBase.OnClick は Command.Execute(CommandParameter) を呼ぶ）
        addPairButton.Command.Execute(addPairButton.CommandParameter);
        window.UpdateLayout();
        DoEvents();

        items.Items.Count.Should().Be(2);
        addPairButton.IsEnabled.Should().BeFalse("空スロットが出ている間は続けて足せない");

        var secondContainer = (FrameworkElement)items.ItemContainerGenerator.ContainerFromIndex(1)!;
        secondContainer.UpdateLayout();

        var secondRowComboBoxes = FindVisualChildren<ComboBox>(secondContainer).ToList();
        secondRowComboBoxes[0].Items.Count.Should().Be(1, "1 行目が使う Id は候補から外れる");
        secondRowComboBoxes[1].Items.Count.Should().Be(2, "1 行目が使う ParentId は候補から外れる");
        secondRowComboBoxes.Should().OnlyContain(comboBox => comboBox.SelectedItem == null);

        // 未選択の間だけ「参照先列」「外部キー列」の案内を重ねて出す
        var placeholders = FindVisualChildren<TextBlock>(secondContainer)
            .Where(text =>
                text.Text == QuickER.Resources.Strings.Property_ReferencedColumn
                || text.Text == QuickER.Resources.Strings.Property_ForeignKeyColumn
            )
            .ToList();
        placeholders.Should().HaveCount(2);
        placeholders.Should().OnlyContain(text => text.Visibility == Visibility.Visible);

        // 一覧から選ぶのと同じ経路（Selector が SetCurrentValue で SelectedItem を更新し、
        //  TwoWay 束縛が VM へ書き戻す）で親列だけを確定させる
        secondRowComboBoxes[0].SelectedIndex = 0;
        window.UpdateLayout();
        DoEvents();

        relationship.ColumnPairs.Should().ContainSingle("片側だけの選択はモデルへ反映しない");
        placeholders
            .Single(text => text.Text == QuickER.Resources.Strings.Property_ReferencedColumn)
            .Visibility.Should()
            .Be(Visibility.Collapsed, "選ばれた側の案内は消える");

        // 子列も選ぶと 2 組目が確定し、複合外部キーになる
        secondRowComboBoxes[1].SelectedIndex = 1;
        window.UpdateLayout();
        DoEvents();

        relationship.ColumnPairs.Should().HaveCount(2);
        relationship
            .ColumnPairRows.Select(row =>
                (row.SelectedSourceColumn!.Name, row.SelectedTargetColumn!.Name)
            )
            .Should()
            .Equal(("Id", "ParentId"), ("Code", "ParentCode"));

        // 行の「×」は行削除コマンドへ束縛され、その行をパラメーターに持つ
        var removeButtons = FindVisualChildren<Button>(window)
            .Where(button =>
                ReferenceEquals(button.Command, vm.RemoveRelationshipColumnPairCommand)
            )
            .ToList();
        removeButtons.Should().HaveCount(2);
        removeButtons[1].CommandParameter.Should().BeSameAs(relationship.ColumnPairRows[1]);

        removeButtons[1].Command.Execute(removeButtons[1].CommandParameter);
        window.UpdateLayout();
        DoEvents();

        relationship.ColumnPairs.Should().ContainSingle();
        items.Items.Count.Should().Be(1);

        // Undo で 2 組目が戻り、行そのものも戻る
        vm.UndoRedo.Undo();
        window.UpdateLayout();
        DoEvents();

        relationship.ColumnPairs.Should().HaveCount(2);
        items.Items.Count.Should().Be(2);
    }

    /// <summary>一意制約の削除を Redo しても、その列を参照する列ペアと画面の選択が残ることを検証する</summary>
    [Fact(
        DisplayName = "リレーションのキー列エディタ: 参照先の一意制約を Redo で消しても落ちず、ペアと選択と履歴が残る"
    )]
    public void RelationshipColumnPairEditor_SurvivesUniqueConstraintRedo()
    {
        RunInIsolatedWindow(AssertSurvivesUniqueConstraintRedo);
    }

    /// <summary>表示済みウィンドウ上で、一意制約の削除の Undo / Redo と列ペアの表示を検証する</summary>
    private static void AssertSurvivesUniqueConstraintRedo(MainViewModel vm, MainWindow window)
    {
        vm.ReplaceDiagramFromModule(CreateCodeReferenceDiagram());

        // 親を選んで制約を消し、続けて親の名前を変える（Redo が 2 段積まれた状態を作るため）
        var parentVm = vm.Entities[0];
        vm.OnEntityClicked(parentVm);
        vm.RemoveUniqueConstraintCommand.Execute(parentVm.UniqueConstraints.Single());
        parentVm.TableName = "Parent2";
        DoEvents();
        vm.UndoCommand.Execute(null);
        vm.UndoCommand.Execute(null);
        DoEvents();
        parentVm.UniqueConstraints.Should().ContainSingle("Undo 2 回で制約が戻る");

        // リレーションを選んでキー列エディタを表示する
        var relationship = vm.Relationships.Should().ContainSingle().Subject;
        vm.OnRelationshipClicked(relationship);
        window.UpdateLayout();
        DoEvents();

        var items = (ItemsControl)window.FindName("RelationshipColumnPairItems")!;
        var container = (FrameworkElement)items.ItemContainerGenerator.ContainerFromIndex(0)!;
        container.UpdateLayout();
        var sourceComboBox = FindVisualChildren<ComboBox>(container).First();
        ((ColumnViewModel)sourceComboBox.SelectedItem).Name.Should().Be("Code");

        // 制約の削除を Redo する（Code は候補キーでなくなる）
        var generation = vm.UndoRedo.ChangeGeneration;
        vm.RedoCommand.Execute(null);
        window.UpdateLayout();
        DoEvents();

        parentVm.UniqueConstraints.Should().BeEmpty();
        relationship
            .ColumnPairs.Should()
            .ContainSingle("列が残っている限り、候補キーでなくなってもペアは残す");
        sourceComboBox
            .SelectedItem.Should()
            .BeSameAs(relationship.ColumnPairRows[0].SelectedSourceColumn);
        ((ColumnViewModel)sourceComboBox.SelectedItem)
            .Name.Should()
            .Be("Code", "画面の選択も落ちない");
        vm.UndoRedo.ChangeGeneration.Should().Be(generation + 1, "Redo 1 回分だけ進む");
        vm.UndoRedo.CanRedo.Should().BeTrue("入れ子の Execute が走ると残りの Redo が消える");

        // 制約の削除は履歴にあるので Undo で戻る
        vm.UndoCommand.Execute(null);
        window.UpdateLayout();
        DoEvents();

        parentVm.UniqueConstraints.Should().ContainSingle();
        relationship.ColumnPairs.Should().ContainSingle();
        ((ColumnViewModel)sourceComboBox.SelectedItem).Name.Should().Be("Code");
    }

    /// <summary>選択中の列を並べ替えても、コンボボックスの選択が落ちず候補が重複しないことを検証する</summary>
    [Fact(
        DisplayName = "リレーションのキー列エディタ: 選択中の列を並べ替えて Undo しても選択が落ちず候補が重複しない"
    )]
    public void RelationshipColumnPairEditor_KeepsSelectionOnColumnReorder()
    {
        RunInIsolatedWindow(AssertKeepsSelectionOnColumnReorder);
    }

    /// <summary>表示済みウィンドウ上で、親列の並べ替えとその Undo の後の列ペア行を検証する</summary>
    private static void AssertKeepsSelectionOnColumnReorder(MainViewModel vm, MainWindow window)
    {
        vm.ReplaceDiagramFromModule(CreateCodeReferenceDiagram());
        var parentVm = vm.Entities[0];
        var relationship = vm.Relationships.Should().ContainSingle().Subject;
        vm.OnRelationshipClicked(relationship);
        window.UpdateLayout();
        DoEvents();

        var items = (ItemsControl)window.FindName("RelationshipColumnPairItems")!;
        var container = (FrameworkElement)items.ItemContainerGenerator.ContainerFromIndex(0)!;
        container.UpdateLayout();
        var sourceComboBox = FindVisualChildren<ComboBox>(container).First();

        // 選択中の Code を先頭へ移す（候補の中で選択中の項目が動く）
        var generation = vm.UndoRedo.ChangeGeneration;
        vm.UndoRedo.Execute(
            new QuickER.UndoRedo.MoveColumnOrderCommand(parentVm.Columns, parentVm.Columns[1], 0)
        );
        window.UpdateLayout();
        DoEvents();

        sourceComboBox
            .Items.Cast<ColumnViewModel>()
            .Select(column => column.Name)
            .Should()
            .Equal("Code", "Id");
        ((ColumnViewModel)sourceComboBox.SelectedItem).Name.Should().Be("Code");
        relationship.ColumnPairs.Should().ContainSingle();
        vm.UndoRedo.ChangeGeneration.Should()
            .Be(generation + 1, "並べ替えだけが履歴に入り、列ペアの変更は積まれない");

        vm.UndoCommand.Execute(null);
        window.UpdateLayout();
        DoEvents();

        sourceComboBox
            .Items.Cast<ColumnViewModel>()
            .Select(column => column.Name)
            .Should()
            .Equal("Id", "Code");
        ((ColumnViewModel)sourceComboBox.SelectedItem).Name.Should().Be("Code");
        relationship.ColumnPairs.Should().ContainSingle();
    }

    /// <summary>
    /// 親（Id＝主キー・Code＝一意制約）と子（ChildId・ParentCode）を、Code → ParentCode の列ペアで結んだ図を作る
    /// </summary>
    private static ErDiagram CreateCodeReferenceDiagram()
    {
        var code = new Column { Name = "Code", DataType = "nvarchar(20)" };
        var parentCode = new Column { Name = "ParentCode", DataType = "nvarchar(20)" };
        var parent = new Entity
        {
            TableName = "Parent",
            Columns =
            {
                new Column
                {
                    Name = "Id",
                    DataType = "int",
                    IsPrimaryKey = true,
                },
                code,
            },
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
                    ColumnPairs = { new RelationshipColumnPair(code.Id, parentCode.Id) },
                },
            },
        };
    }

    /// <summary>指定コマンドへ束縛されたボタンを 1 つだけ取り出す</summary>
    private static Button FindCommandButton(
        DependencyObject root,
        System.Windows.Input.ICommand command
    ) =>
        FindVisualChildren<Button>(root).Single(button => ReferenceEquals(button.Command, command));

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
