using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using AwesomeAssertions;
using QuickER.Model;
using QuickER.Services;
using QuickER.Tests.TestSupport;
using QuickER.ViewModels;
using Xunit;

namespace QuickER.Tests.Gui.Views;

/// <summary>
/// プロパティパネルのリレーション「キー列の対応」エディタの XAML 配線（列ペア行の実体化・親列と子列の
/// コンボボックスと ＋ / × ボタンのコマンド束縛と実行・未選択プレースホルダー）を検証するテストクラス。
/// </summary>
/// <remarks>
/// 列ペアの編集は「行の並び＝宣言順、候補は他行の未使用列、両側が揃って確定」という束縛の組み合わせで
/// 成り立っており、VM テストでは束縛そのものを守れない。lessons.md の先例に従い、画面外
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
        Exception? captured = null;

        // MainWindow ctor の Initialize() が実 %APPDATA% の自動保存を復元し、Close の AutoSave が
        // 書き戻すため、永続化先を一時フォルダへ隔離する（実ユーザーデータの読み書きを断つ）
        var folder = Path.Combine(
            Path.GetTempPath(),
            "quicker-relpair-card-" + Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(folder);

        try
        {
            RunRelationshipColumnPairScenario(folder, ref captured);
        }
        finally
        {
            try
            {
                Directory.Delete(folder, recursive: true);
            }
            catch
            {
                // 後始末の失敗はテスト結果に影響させない
            }
        }

        captured.Should().BeNull();
    }

    /// <summary>STA スレッド上で実ウィンドウを表示し、キー列エディタの配線を検証する本体</summary>
    private static void RunRelationshipColumnPairScenario(string folder, ref Exception? captured)
    {
        Exception? threadCaptured = null;

        var thread = new Thread(() =>
        {
            try
            {
                WpfApplicationTestSupport.EnsureApplicationResources();

                var vm = new MainViewModel();
                vm.UsePersistenceForTests(
                    new GuiAppSettingsStore(folder),
                    Path.Combine(folder, "last_diagram.json")
                );
                var window = new MainWindow(vm)
                {
                    // 画面外・非アクティブで表示する（開発者のデスクトップを妨げない。lessons.md の先例）
                    WindowStartupLocation = WindowStartupLocation.Manual,
                    Left = -4000,
                    Top = -4000,
                    ShowActivated = false,
                };

                window.Show();
                window.UpdateLayout();
                DoEvents();

                try
                {
                    AssertRelationshipColumnPairEditor(vm, window);
                }
                finally
                {
                    window.Close();
                    DoEvents();
                }
            }
            catch (Exception ex)
            {
                threadCaptured = ex;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();
        thread.Join();

        captured = threadCaptured;
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

    /// <summary>保留中のディスパッチャ処理（レイアウト・束縛反映）を流し切る</summary>
    private static void DoEvents()
    {
        var frame = new System.Windows.Threading.DispatcherFrame();
        System.Windows.Threading.Dispatcher.CurrentDispatcher.BeginInvoke(
            System.Windows.Threading.DispatcherPriority.ApplicationIdle,
            new Action(() => frame.Continue = false)
        );
        System.Windows.Threading.Dispatcher.PushFrame(frame);
    }
}
