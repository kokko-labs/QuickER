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
/// プロパティパネルの主キー順カードの XAML 配線（複合主キーのときだけ出る表示条件・並び替え行の実体化・
/// ↑ / ↓ ボタンのコマンド束縛と端の行での無効化）を検証するテストクラス。
/// </summary>
/// <remarks>
/// カードの出現条件・行の実体化・ボタンの有効無効はいずれも束縛の組み合わせで成り立っており、
/// VM テストでは守れない（削除済みプロパティを束縛したままでも WPF は無言で空表示にする）。
/// 画面外（Left/Top=-4000）・非アクティブで Show した実ウィンドウ上で検証する。
/// </remarks>
public class PrimaryKeyOrderCardTests
{
    /// <summary>カードの表示条件・行の実体化・移動ボタンの束縛と実行結果を検証する</summary>
    [Fact(DisplayName = "主キー順カード: 複合主キーで行が実体化し、↑ ボタンで実効順が入れ替わる")]
    public void PrimaryKeyOrderCard_RowWiring()
    {
        Exception? captured = null;

        // MainWindow ctor の Initialize() が実 %LOCALAPPDATA% の自動保存を復元し、Close の AutoSave が
        // 書き戻すため、永続化先を一時フォルダへ隔離する（実ユーザーデータの読み書きを断つ）
        var folder = Path.Combine(
            Path.GetTempPath(),
            "quicker-pkorder-card-" + Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(folder);

        try
        {
            RunPrimaryKeyOrderCardScenario(folder, ref captured);
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

    /// <summary>STA スレッド上で実ウィンドウを表示し、主キー順カードの配線を検証する本体</summary>
    private static void RunPrimaryKeyOrderCardScenario(string folder, ref Exception? captured)
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
                    // 画面外・非アクティブで表示する（開発者のデスクトップを妨げない）
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
                    AssertPrimaryKeyOrderCard(vm, window);
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

    /// <summary>表示済みウィンドウ上で主キー順カードの実体化とコマンド配線を検証する</summary>
    private static void AssertPrimaryKeyOrderCard(MainViewModel vm, MainWindow window)
    {
        // 主キー順カードは選択中エンティティのプロパティパネルに出るため、まず 1 個追加して選択する
        vm.ReplaceDiagramFromModule(
            new ErDiagram
            {
                Entities =
                {
                    new Entity
                    {
                        TableName = "TenantRegion",
                        Columns =
                        {
                            new Column
                            {
                                Name = "TenantId",
                                DataType = "int",
                                IsPrimaryKey = true,
                                IsNullable = false,
                            },
                            new Column { Name = "RegionCode", DataType = "nvarchar(10)" },
                            new Column { Name = "Note", DataType = "nvarchar(50)" },
                        },
                    },
                },
            }
        );
        vm.SelectedEntity = vm.Entities[0];
        window.UpdateLayout();
        DoEvents();

        var items = (ItemsControl)window.FindName("PrimaryKeyOrderItems")!;
        var card = FindVisualAncestor<Border>(items);

        card.Visibility.Should().Be(Visibility.Collapsed, "単一主キーでは並び替える対象が無い");

        // カラム一覧の PK チェックと同じ経路（膜の切替）で複合主キーにする
        var entity = vm.Entities[0];
        entity.Columns[1].IsPrimaryKey = true;
        window.UpdateLayout();
        DoEvents();

        card.Visibility.Should().Be(Visibility.Visible, "複合主キーになればカードが出る");
        items.Items.Count.Should().Be(2, "主キー列だけが行になる（Note は含まれない）");

        var firstRow = (FrameworkElement)items.ItemContainerGenerator.ContainerFromIndex(0)!;
        var secondRow = (FrameworkElement)items.ItemContainerGenerator.ContainerFromIndex(1)!;
        firstRow.UpdateLayout();
        secondRow.UpdateLayout();

        // 行の列名は実効順（順序リスト未設定なのでカラム宣言順）で出る
        RowColumnName(firstRow).Should().Be("TenantId");
        RowColumnName(secondRow).Should().Be("RegionCode");

        var firstUp = FindCommandButton(firstRow, vm.MovePrimaryKeyColumnUpCommand);
        var firstDown = FindCommandButton(firstRow, vm.MovePrimaryKeyColumnDownCommand);
        var secondUp = FindCommandButton(secondRow, vm.MovePrimaryKeyColumnUpCommand);
        var secondDown = FindCommandButton(secondRow, vm.MovePrimaryKeyColumnDownCommand);

        firstUp.IsEnabled.Should().BeFalse("先頭行はこれ以上前へ動かせない");
        firstDown.IsEnabled.Should().BeTrue();
        secondUp.IsEnabled.Should().BeTrue();
        secondDown.IsEnabled.Should().BeFalse("末尾行はこれ以上後ろへ動かせない");

        // ボタンのパラメーターはその行の VM（＝移動対象が行だけで決まる）
        secondUp.CommandParameter.Should().BeSameAs(entity.PrimaryKeyMembers[1]);

        // クリック時に WPF が行うのと同じ「解決済み束縛の実行」を行う
        // （ButtonBase.OnClick は Command.Execute(CommandParameter) を呼ぶ）
        secondUp.Command.Execute(secondUp.CommandParameter);
        window.UpdateLayout();
        DoEvents();

        entity
            .PrimaryKeyMembers.Select(member => member.Column.Name)
            .Should()
            .Equal("RegionCode", "TenantId");
        entity
            .GetPrimaryKeyColumnsInOrder()
            .Select(column => column.Name)
            .Should()
            .Equal("RegionCode", "TenantId");

        // 行インスタンスは使い回されるため、表示だけが入れ替わる
        RowColumnName(firstRow).Should().Be("RegionCode");
        RowColumnName(secondRow).Should().Be("TenantId");
    }

    /// <summary>並び替え行の列名テキストを取り出す（行テンプレート先頭の TextBlock）</summary>
    private static string RowColumnName(DependencyObject row) =>
        FindVisualChildren<TextBlock>(row).First().Text;

    /// <summary>指定コマンドへ束縛されたボタンを 1 つだけ取り出す</summary>
    private static Button FindCommandButton(
        DependencyObject root,
        System.Windows.Input.ICommand command
    ) =>
        FindVisualChildren<Button>(root).Single(button => ReferenceEquals(button.Command, command));

    /// <summary>ビジュアルツリーを親方向に辿り、最初に見つかった指定型の祖先を返す</summary>
    private static T FindVisualAncestor<T>(DependencyObject start)
        where T : DependencyObject
    {
        var current = System.Windows.Media.VisualTreeHelper.GetParent(start);

        while (current is not null and not T)
        {
            current = System.Windows.Media.VisualTreeHelper.GetParent(current);
        }

        return (T)current!;
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
