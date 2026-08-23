using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Threading;
using AwesomeAssertions;
using QuickER.Provider;
using QuickER.Sqlite;
using QuickER.SqlServer;
using QuickER.Tests.TestDoubles;
using QuickER.Tests.TestSupport;
using QuickER.ViewModels;

namespace QuickER.Tests.Gui.Views;

/// <summary>
/// ターゲット DBMS 切替の続行確認をキャンセルしたとき、実 ComboBox の表示が
/// 現在方言へ戻ることを、MainWindow と同じ TwoWay バインディングで検証するテストクラス。
/// </summary>
/// <remarks>
/// VM 単体のテスト（PropertyChanged の発行観測）では「WPF のバインディングがその通知を
/// ターゲットへ反映するか」までは検証できないため、実コントロール＋実バインディングで固定する。
/// </remarks>
public class TargetDbmsComboBoxRevertTests
{
    /// <summary>SQL Server と SQLite（実プロバイダ）を登録した VM とスタブダイアログを作る</summary>
    private static (MainViewModel Vm, SqliteProvider Sqlite, StubDialogService Dialogs) CreateVm()
    {
        var sqlite = new SqliteProvider();
        var registry = new DatabaseProviderRegistry(
            new IDatabaseProvider[] { new SqlServerProvider(), sqlite }
        );
        var dialogs = new StubDialogService();
        var vm = new MainViewModel(dialogs, providers: registry);
        return (vm, sqlite, dialogs);
    }

    /// <summary>キューに積まれた Dispatcher 操作をすべて処理する（SystemIdle まで排出）</summary>
    private static void DoEvents()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(
            DispatcherPriority.SystemIdle,
            new Action(() => frame.Continue = false)
        );
        Dispatcher.PushFrame(frame);
    }

    /// <summary>MainWindow と同じ形の ComboBox（ItemsSource＋SelectedItem TwoWay）を VM へ結線する</summary>
    private static ComboBox CreateBoundComboBox(MainViewModel vm)
    {
        var combo = new ComboBox { ItemsSource = vm.AvailableProviders };
        combo.SetBinding(
            ComboBox.SelectedItemProperty,
            new Binding(nameof(MainViewModel.SelectedProvider))
            {
                Source = vm,
                Mode = BindingMode.TwoWay,
            }
        );
        return combo;
    }

    /// <summary>
    /// 実クリックと同じ選択機構（AutomationPeer の SelectionItemPattern）で切替先を選択し、
    /// モーダル相当のメッセージポンプ中にキャンセルしても ComboBox の表示が戻ることを検証する
    /// </summary>
    [Fact(DisplayName = "実クリック相当の選択でも続行確認のキャンセルで表示が戻る")]
    public void ConfirmCancelled_ViaSelectionItemPattern_ComboBoxReverts()
    {
        WpfApplicationTestSupport.RunSta(() =>
        {
            WpfApplicationTestSupport.EnsureApplicationResources();
            // 実 MessageBox と同じく「開いている間メッセージポンプが回る」ダイアログでキャンセルを返す
            var dialogs = new PumpingCancelDialogService();
            var vm2 = new MainViewModel(dialogs, providers: CreateRegistry(out var sqlite2));
            vm2.SetUiPost(action => Dispatcher.CurrentDispatcher.BeginInvoke(action));
            vm2.AddEntityCommand.Execute(null);
            vm2.AddColumnCommand.Execute(null);
            var column = vm2.Entities[0].Columns[^1];
            column.Name = "RowVer";
            column.DataType = "rowversion";
            column.IsNullable = false;

            var combo = CreateBoundComboBox(vm2);
            // 実クリックと同じ ItemContainer 経由の選択にはコンテナ生成が要るため、ウィンドウへ載せて表示する
            var window = new System.Windows.Window
            {
                WindowStartupLocation = System.Windows.WindowStartupLocation.Manual,
                Left = -10000,
                Top = -10000,
                ShowActivated = false,
                ShowInTaskbar = false,
                Content = combo,
            };

            try
            {
                window.Show();
                combo.IsDropDownOpen = true; // コンテナ生成（実クリック時と同じ状態）
                DoEvents();

                var peer =
                    System.Windows.Automation.Peers.UIElementAutomationPeer.CreatePeerForElement(
                        combo
                    );
                var itemPeer = peer.GetChildren()
                    .OfType<System.Windows.Automation.Peers.ItemAutomationPeer>()
                    .First(p => ReferenceEquals(p.Item, sqlite2));
                var selection = (System.Windows.Automation.Provider.ISelectionItemProvider)
                    itemPeer.GetPattern(
                        System.Windows.Automation.Peers.PatternInterface.SelectionItem
                    )!;

                // 実クリックと同じ SelectionChange 機構を通る選択（この中で続行確認が出てキャンセルされる）
                selection.Select();
                // 実クリック（NotifyComboBoxItemMouseUp）と同じく、選択と同一スタックで閉じる
                combo.IsDropDownOpen = false;
                DoEvents();

                dialogs.WarningConfirmCount.Should().Be(1);
                vm2.CurrentProvider.Name.Should().Be("sqlserver");
                combo.SelectedItem.Should().BeSameAs(vm2.CurrentProvider);
                // SelectedItem だけでは足りない＝ComboBox の内部選択状態は SelectedItem と独立に進むため、
                // 実際に画面へ出る表示（SelectionBoxItem）と選択位置まで戻っていることを固定する
                combo.SelectionBoxItem.Should().BeSameAs(vm2.CurrentProvider);
                combo.SelectedIndex.Should().Be(0);
            }
            finally
            {
                window.Close();
            }
        });
    }

    /// <summary>実 MessageBox と同様に、開いている間メッセージポンプが回る状況を模してキャンセルを返すスタブ</summary>
    private sealed class PumpingCancelDialogService : QuickER.Gui.Abstractions.IDialogService
    {
        /// <summary>ConfirmWarning が呼ばれた回数</summary>
        public int WarningConfirmCount { get; private set; }

        public bool Confirm(string message, string title) => false;

        public bool ConfirmWarning(string message, string title)
        {
            WarningConfirmCount++;
            // モーダル表示中のネストしたメッセージループ相当（キュー済みの Dispatcher 操作が処理される）
            DoEvents();
            return false;
        }

        public void ShowInformation(string message, string title) { }

        public void ShowError(string message, string title) { }

        public void ShowInformationDetails(string message, string details, string title) { }

        public void ShowErrorDetails(string message, string details, string title) { }
    }

    /// <summary>SQL Server と SQLite（実プロバイダ）のレジストリを作る</summary>
    private static DatabaseProviderRegistry CreateRegistry(out SqliteProvider sqlite)
    {
        sqlite = new SqliteProvider();
        return new DatabaseProviderRegistry(
            new IDatabaseProvider[] { new SqlServerProvider(), sqlite }
        );
    }

    /// <summary>続行確認をキャンセルすると ComboBox の表示が現在方言へ戻ることを検証する</summary>
    [Fact(DisplayName = "続行確認のキャンセルで ComboBox の選択表示が現在方言へ戻る")]
    public void ConfirmCancelled_ComboBoxRevertsToCurrentProvider()
    {
        WpfApplicationTestSupport.RunSta(() =>
        {
            var (vm, sqlite, dialogs) = CreateVm();
            // 本番（MainWindow.xaml.cs）と同じ Dispatcher 経由の uiPost を結線する
            vm.SetUiPost(action => Dispatcher.CurrentDispatcher.BeginInvoke(action));
            // NOT NULL 解除の警告が出る図（rowversion 列）＝切替時に続行確認が出る
            vm.AddEntityCommand.Execute(null);
            vm.AddColumnCommand.Execute(null);
            var column = vm.Entities[0].Columns[^1];
            column.Name = "RowVer";
            column.DataType = "rowversion";
            column.IsNullable = false;
            dialogs.ConfirmResult = false;

            var combo = CreateBoundComboBox(vm);
            DoEvents();
            combo.SelectedItem.Should().BeSameAs(vm.CurrentProvider);

            // ユーザーの選択操作に相当（ターゲット変更 → バインディングが VM のセッターを駆動する）
            combo.SelectedItem = sqlite;
            DoEvents();

            // キャンセルなので VM は現在方言のまま・ComboBox の表示も戻る
            dialogs.WarningConfirmMessages.Should().ContainSingle();
            vm.CurrentProvider.Name.Should().Be("sqlserver");
            combo.SelectedItem.Should().BeSameAs(vm.CurrentProvider);
            combo.SelectionBoxItem.Should().BeSameAs(vm.CurrentProvider);
            combo.SelectedIndex.Should().Be(0);
        });
    }

    /// <summary>続行確認を OK すると ComboBox の表示が切替先のまま維持されることを検証する</summary>
    [Fact(DisplayName = "続行確認の OK で ComboBox の選択表示は切替先のまま")]
    public void ConfirmAccepted_ComboBoxKeepsNewProvider()
    {
        WpfApplicationTestSupport.RunSta(() =>
        {
            var (vm, sqlite, dialogs) = CreateVm();
            vm.SetUiPost(action => Dispatcher.CurrentDispatcher.BeginInvoke(action));
            vm.AddEntityCommand.Execute(null);
            vm.AddColumnCommand.Execute(null);
            var column = vm.Entities[0].Columns[^1];
            column.Name = "RowVer";
            column.DataType = "rowversion";
            column.IsNullable = false;
            dialogs.ConfirmResult = true;

            var combo = CreateBoundComboBox(vm);
            DoEvents();

            combo.SelectedItem = sqlite;
            DoEvents();

            dialogs.WarningConfirmMessages.Should().ContainSingle();
            vm.CurrentProvider.Name.Should().Be("sqlite");
            combo.SelectedItem.Should().BeSameAs(sqlite);
            combo.SelectionBoxItem.Should().BeSameAs(sqlite);
        });
    }
}
