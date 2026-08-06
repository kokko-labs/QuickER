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
/// プロパティパネルの UNIQUE 制約カードの XAML 配線（制約一覧の実体化・構成列の行リスト＝列選択
/// コンボボックスと ＋ / × ボタンのコマンド束縛と実行）を検証するテストクラス。
/// </summary>
/// <remarks>
/// 構成列の編集は「行の並び＝宣言順、候補は他行の未使用列」という束縛の組み合わせで成り立っており、
/// VM テストでは束縛そのものを守れない。lessons.md の先例に従い、画面外（Left/Top=-4000）・
/// 非アクティブで Show した実ウィンドウ上で ItemsControl のコンテナを実体化して検証する。
/// </remarks>
public class UniqueConstraintCardTests
{
    /// <summary>制約カードの項目実体化・行リストのコマンド束縛・選択確定の結果を検証する</summary>
    [Fact(
        DisplayName = "UNIQUE 制約カード: 列行が実体化し、コンボボックスの選択で構成列が確定する"
    )]
    public void UniqueConstraintCard_MemberRowWiring()
    {
        Exception? captured = null;

        // MainWindow ctor の Initialize() が実 %APPDATA% の自動保存を復元し、Close の AutoSave が
        // 書き戻すため、永続化先を一時フォルダへ隔離する（実ユーザーデータの読み書きを断つ）
        var folder = Path.Combine(
            Path.GetTempPath(),
            "quicker-unique-card-" + Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(folder);

        try
        {
            RunUniqueConstraintCardScenario(folder, ref captured);
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

    /// <summary>STA スレッド上で実ウィンドウを表示し、UNIQUE 制約カードの配線を検証する本体</summary>
    private static void RunUniqueConstraintCardScenario(string folder, ref Exception? captured)
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
                    AssertUniqueConstraintCard(vm, window);
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

    /// <summary>表示済みウィンドウ上で制約カードの実体化とコマンド配線を検証する</summary>
    private static void AssertUniqueConstraintCard(MainViewModel vm, MainWindow window)
    {
        // 制約カードは選択中エンティティのプロパティパネルに出るため、まず 1 個追加して選択する
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
                            new Column { Name = "Code", DataType = "nvarchar(20)" },
                        },
                    },
                },
            }
        );
        vm.SelectedEntity = vm.Entities[0];
        window.UpdateLayout();
        DoEvents();

        var items = (ItemsControl)window.FindName("UniqueConstraintsItems")!;
        items.Items.Count.Should().Be(0, "初期状態では制約なし");

        // 「+」で制約を 1 件追加すると、カードの ItemsControl に行が実体化する
        vm.AddUniqueConstraintCommand.Execute(null);
        window.UpdateLayout();
        DoEvents();

        items.Items.Count.Should().Be(1);

        var container = (FrameworkElement)items.ItemContainerGenerator.ContainerFromIndex(0)!;
        container.UpdateLayout();

        var constraint = vm.Entities[0].UniqueConstraints[0];
        FindVisualChildren<ComboBox>(container)
            .Should()
            .BeEmpty("制約を足しただけでは構成列の行は無い");

        // 構成列の「＋」は行追加コマンドへ束縛され、対象の制約をパラメーターに持つ
        var addMemberButton = FindCommandButton(container, vm.AddUniqueConstraintMemberSlotCommand);
        addMemberButton.CommandParameter.Should().BeSameAs(constraint);
        addMemberButton.IsEnabled.Should().BeTrue("未使用の列が 2 つある");

        // クリック時に WPF が行うのと同じ「解決済み束縛の実行」を行う
        // （ButtonBase.OnClick は Command.Execute(CommandParameter) を呼ぶ。
        //   ClickEvent の直接発火や AutomationPeer.Toggle はこの経路を通らない）
        addMemberButton.Command.Execute(addMemberButton.CommandParameter);
        window.UpdateLayout();
        DoEvents();

        var comboBox = FindVisualChildren<ComboBox>(container).Should().ContainSingle().Subject;
        comboBox.ItemsSource.Should().BeSameAs(constraint.Members[0].AvailableColumns);
        comboBox.Items.Count.Should().Be(2, "空スロットの候補はエンティティの全カラム");
        comboBox.SelectedItem.Should().BeNull("追加直後は未選択の空スロット");
        constraint.ColumnIds.Should().BeEmpty("空スロットはまだモデルへ反映しない");

        // 一覧から選ぶのと同じ経路（Selector が SetCurrentValue で SelectedItem を更新し、
        //  TwoWay 束縛が VM へ書き戻す）で列を確定させる
        comboBox.SelectedIndex = 1;
        DoEvents();

        constraint.ColumnIds.Should().ContainSingle();
        constraint.Members.Select(m => m.SelectedColumn!.Name).Should().Equal("Code");

        // Undo で構成列が外れ、行そのものも消える（空スロットも復元しない）
        vm.UndoRedo.Undo();
        window.UpdateLayout();
        DoEvents();

        constraint.ColumnIds.Should().BeEmpty();
        FindVisualChildren<ComboBox>(container).Should().BeEmpty();

        vm.UndoRedo.Redo();
        window.UpdateLayout();
        DoEvents();

        constraint.ColumnIds.Should().ContainSingle();

        // 残る 1 列を 2 行目に選ぶと、未使用の列が尽きて「＋」が無効化される
        addMemberButton.Command.Execute(addMemberButton.CommandParameter);
        window.UpdateLayout();
        DoEvents();

        var secondComboBox = FindVisualChildren<ComboBox>(container).ElementAt(1);
        secondComboBox.Items.Count.Should().Be(1, "他行が使う Code は候補から外れる");
        secondComboBox.SelectedIndex = 0;
        window.UpdateLayout();
        DoEvents();

        constraint.Members.Select(m => m.SelectedColumn!.Name).Should().Equal("Code", "Id");
        addMemberButton.IsEnabled.Should().BeFalse("未使用の列が無ければ行を足せない");

        // 行の「×」は行削除コマンドへ束縛され、その行をパラメーターに持つ
        var removeButtons = FindVisualChildren<Button>(container)
            .Where(button =>
                ReferenceEquals(button.Command, vm.RemoveUniqueConstraintMemberCommand)
            )
            .ToList();
        removeButtons.Should().HaveCount(2);
        removeButtons[1].CommandParameter.Should().BeSameAs(constraint.Members[1]);

        removeButtons[1].Command.Execute(removeButtons[1].CommandParameter);
        window.UpdateLayout();
        DoEvents();

        constraint.Members.Select(m => m.SelectedColumn!.Name).Should().Equal("Code");
        addMemberButton.IsEnabled.Should().BeTrue("列が解放されたので再び行を足せる");
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
