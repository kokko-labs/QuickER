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
/// プロパティパネルの UNIQUE 制約カードの XAML 配線（制約一覧の実体化・構成列チェックボックスの
/// コマンド束縛と実行）を検証するテストクラス。
/// </summary>
/// <remarks>
/// チェックボックスは「IsChecked は OneWay・切替は Undo 可能なコマンド」という構成のため、
/// VM テストでは束縛そのものを守れない。lessons.md の先例に従い、画面外（Left/Top=-4000）・
/// 非アクティブで Show した実ウィンドウ上で ItemsControl のコンテナを実体化して検証する。
/// </remarks>
public class UniqueConstraintCardTests
{
    /// <summary>制約カードの項目実体化・チェックボックスのコマンド束縛・実行結果を検証する</summary>
    [Fact(DisplayName = "UNIQUE 制約カード: 制約行が実体化し、列チェックでコマンドが走る")]
    public void UniqueConstraintCard_ChecklistWiring()
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

        var checkBoxes = FindVisualChildren<CheckBox>(container).ToList();
        checkBoxes
            .Select(box => box.Content)
            .Should()
            .Equal(["Id", "Code"], "構成列候補はエンティティの全カラムを映す");

        // チェックボックスは切替コマンドへ束縛され、対象の構成列候補をパラメーターに持つ
        // （IsChecked は OneWay＝正本は制約側）
        var codeCheckBox = checkBoxes[1];
        codeCheckBox
            .Command.Should()
            .BeSameAs(vm.ToggleUniqueConstraintColumnCommand, "切替は Undo 可能なコマンド経由");
        codeCheckBox
            .CommandParameter.Should()
            .BeOfType<UniqueConstraintColumnViewModel>()
            .Which.Column.Name.Should()
            .Be("Code");
        codeCheckBox.IsChecked.Should().BeFalse();

        // クリック時に WPF が行うのと同じ「解決済み束縛の実行」を行う
        // （ButtonBase.OnClick は Command.Execute(CommandParameter) を呼ぶ。
        //   ClickEvent の直接発火や AutomationPeer.Toggle はこの経路を通らない）
        codeCheckBox.Command.Execute(codeCheckBox.CommandParameter);
        DoEvents();

        var constraint = vm.Entities[0].UniqueConstraints[0];
        constraint.ColumnIds.Should().ContainSingle();
        constraint.ColumnSummary.Should().Be("Code");
        codeCheckBox.IsChecked.Should().BeTrue("OneWay 束縛が制約側の変更を表示へ戻す");

        // Undo で構成列が外れ、チェック表示も追従する
        vm.UndoRedo.Undo();
        DoEvents();

        constraint.ColumnIds.Should().BeEmpty();
        codeCheckBox.IsChecked.Should().BeFalse();
    }

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
