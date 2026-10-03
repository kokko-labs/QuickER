using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using AwesomeAssertions;
using QuickER.AI;
using QuickER.AI.Chat;
using QuickER.AI.UI;
using QuickER.Gui.Abstractions;
using QuickER.Tests.AI;
using QuickER.Tests.TestDoubles;
using QuickER.Tests.TestSupport;

namespace QuickER.Tests.AI.Chat;

/// <summary>
/// <see cref="AiChatDialog"/> の接続方式タブ（BackendTabs）が、実 XAML の
/// <c>Style.Triggers</c>（<see cref="AiChatDialogViewModel.IsTurnInProgress"/> への DataTrigger）で
/// ターン実行中に無効化されること、選択が VM の接続方式と束縛で往復すること（初期タブの復元・
/// 会話を消す確認のキャンセルでの巻き戻し）を検証するテストクラス（C2）。
/// </summary>
/// <remarks>
/// VM 単体テスト（<see cref="AiChatDialogViewModelTests"/>）では XAML の束縛配線切れを検出できないため、
/// 実ダイアログを画面外へ <c>Show</c> して見た目の面（<see cref="TabItem.IsEnabled"/>）を検証する
/// （クリックが実際に無反応になるかどうかは WPF ランタイムの責務で、ここでは検証しない）。
/// </remarks>
public class AiChatDialogBackendTabsTests
{
    private sealed class SyncUiDispatcher : IUiDispatcher
    {
        public T Invoke<T>(Func<T> func) => func();
    }

    private static (AiChatDialogViewModel vm, string folder) CreateVm(
        IDialogService? dialogService = null,
        string? folder = null
    )
    {
        folder ??= Path.Combine(Path.GetTempPath(), "QuickERTests", Guid.NewGuid().ToString("N"));
        var keyStore = new InMemoryApiKeyStore();
        var vm = new AiChatDialogViewModel(
            host: null,
            dispatcher: new SyncUiDispatcher(),
            settingsStore: new AiSettingsStore(folder),
            codexClient: new FakeCodexAppServerClient(),
            dialogService: dialogService,
            apiKeyLoader: keyStore.Load,
            apiKeySaver: keyStore.Save
        );
        return (vm, folder);
    }

    /// <summary>実ダイアログを画面外＋非アクティブで表示し、テスト本体を実行して閉じる</summary>
    private static void WithShownDialog(
        AiChatDialogViewModel vm,
        string folder,
        Action<TabControl, List<TabItem>> body
    )
    {
        try
        {
            var dialog = WpfApplicationTestSupport.LoadXamlComponent(() =>
                new AiChatDialog(vm, new RecordingShutdownFailureReporter())
            );
            dialog.WindowStartupLocation = WindowStartupLocation.Manual;
            dialog.Left = -4000;
            dialog.Top = -4000;
            dialog.ShowActivated = false;
            dialog.ShowInTaskbar = false;
            dialog.Show();

            try
            {
                dialog.UpdateLayout();
                WpfApplicationTestSupport.DoEvents();
                var tabControl = (TabControl)dialog.FindName("BackendTabs");
                body(tabControl, tabControl.Items.Cast<TabItem>().ToList());
            }
            finally
            {
                dialog.Close();
            }
        }
        finally
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }
        }
    }

    /// <summary>実クリックと同じ選択機構（UI オートメーションの SelectionItemPattern）でタブを選ぶ</summary>
    private static void SelectViaAutomation(TabControl tabControl, TabItem tab)
    {
        var peer = UIElementAutomationPeer.CreatePeerForElement(tabControl);
        var itemPeer = peer.GetChildren()
            .OfType<ItemAutomationPeer>()
            .First(p => ReferenceEquals(p.Item, tab));
        var selection = (ISelectionItemProvider)
            itemPeer.GetPattern(PatternInterface.SelectionItem)!;
        selection.Select();
    }

    /// <summary>実 MessageBox と同じく、開いている間メッセージポンプが回る確認を模して、決めた応答を返すスタブ</summary>
    private sealed class PumpingConfirmDialogService(bool result) : IDialogService
    {
        /// <summary>Confirm が呼ばれた回数</summary>
        public int ConfirmCount { get; private set; }

        public bool Confirm(string message, string title)
        {
            ConfirmCount++;
            // モーダル表示中のネストしたメッセージループ相当（キュー済みのディスパッチャ操作が処理される）
            WpfApplicationTestSupport.DoEvents();
            return result;
        }

        public bool ConfirmWarning(string message, string title) => result;

        public bool ConfirmWarningDetails(string message, string details, string title) => result;

        public void ShowInformation(string message, string title) { }

        public void ShowError(string message, string title) { }

        public void ShowInformationDetails(string message, string details, string title) { }

        public void ShowErrorDetails(string message, string details, string title) { }
    }

    /// <summary>タブの選択位置・各タブの選択状態・表示中の中身が、そろって指定のタブを指していることを表明する</summary>
    private static void ShouldShowTab(TabControl tabControl, List<TabItem> tabItems, int index)
    {
        // 束縛のソースだけでなく、画面に出る面（選択位置・タブの選択状態・表示中の中身）を見る
        tabControl.SelectedIndex.Should().Be(index);
        tabItems.Select(t => t.IsSelected).Should().Equal(tabItems.Select((_, i) => i == index));
        tabControl.SelectedContent.Should().BeSameAs(tabItems[index].Content);
    }

    /// <summary>
    /// 前回使った接続方式（保存した設定）が、表示時にタブの選択と VM の接続方式の両方へ反映されることを検証する。
    /// </summary>
    [Fact(DisplayName = "表示時に前回の接続方式のタブが選ばれ VM もその方式になる")]
    public void InitialBackend_IsShownOnLoad()
    {
        WpfApplicationTestSupport.RunSta(() =>
        {
            WpfApplicationTestSupport.EnsureApplicationResources();

            var (saver, folder) = CreateVm();
            saver.TryChangeBackend(ErChatBackendKind.Codex).Should().BeTrue();
            saver.SaveSettings();
            var (vm, _) = CreateVm(folder: folder);

            WithShownDialog(
                vm,
                folder,
                (tabControl, tabItems) =>
                {
                    vm.Connection.SelectedBackend.Should().Be(ErChatBackendKind.Codex);
                    ShouldShowTab(tabControl, tabItems, 1);
                }
            );
        });
    }

    /// <summary>
    /// タブを選ぶと、選択変更の処理が終わった後で（ディスパッチャの次の周回で）VM の接続方式が確定することを検証する
    /// （確認モーダルをタブの選択変更処理の内側で開かないための後置の配線を固定する）。
    /// </summary>
    [Fact(DisplayName = "タブの選択は選択変更の処理の後で VM の接続方式へ確定する")]
    public void SelectingTab_CommitsAfterSelectionChange()
    {
        WpfApplicationTestSupport.RunSta(() =>
        {
            WpfApplicationTestSupport.EnsureApplicationResources();

            var (vm, folder) = CreateVm(new PumpingConfirmDialogService(true));

            WithShownDialog(
                vm,
                folder,
                (tabControl, tabItems) =>
                {
                    SelectViaAutomation(tabControl, tabItems[1]);

                    vm.Connection.SelectedBackend.Should()
                        .Be(ErChatBackendKind.ApiKey, "確定は選択変更の処理が終わった後");

                    WpfApplicationTestSupport.DoEvents();

                    vm.Connection.SelectedBackend.Should().Be(ErChatBackendKind.Codex);
                    ShouldShowTab(tabControl, tabItems, 1);
                }
            );
        });
    }

    /// <summary>
    /// 会話があるときにタブを選び、会話を消す確認をキャンセルすると、タブの表示（選択位置・選択状態・中身）が
    /// 元の接続方式へ戻り、会話も残ることを検証する。
    /// </summary>
    [Fact(DisplayName = "会話を消す確認のキャンセルでタブの表示が元の接続方式へ戻る")]
    public void ConfirmCancelled_RevertsTab()
    {
        WpfApplicationTestSupport.RunSta(() =>
        {
            WpfApplicationTestSupport.EnsureApplicationResources();

            var dialogs = new PumpingConfirmDialogService(false);
            var (vm, folder) = CreateVm(dialogs);

            WithShownDialog(
                vm,
                folder,
                (tabControl, tabItems) =>
                {
                    vm.Messages.Add(
                        new ErChatMessage { Role = ErChatMessageRole.User, Content = "q" }
                    );

                    SelectViaAutomation(tabControl, tabItems[1]);
                    WpfApplicationTestSupport.DoEvents();

                    dialogs.ConfirmCount.Should().Be(1);
                    vm.Connection.SelectedBackend.Should().Be(ErChatBackendKind.ApiKey);
                    vm.HasConversation.Should().BeTrue();
                    ShouldShowTab(tabControl, tabItems, 0);
                }
            );
        });
    }

    /// <summary>会話があるときにタブを選び、確認を OK すると、切替先のまま会話が消えることを検証する</summary>
    [Fact(DisplayName = "会話を消す確認の OK でタブは切替先のまま会話が消える")]
    public void ConfirmAccepted_KeepsNewTab()
    {
        WpfApplicationTestSupport.RunSta(() =>
        {
            WpfApplicationTestSupport.EnsureApplicationResources();

            var dialogs = new PumpingConfirmDialogService(true);
            var (vm, folder) = CreateVm(dialogs);

            WithShownDialog(
                vm,
                folder,
                (tabControl, tabItems) =>
                {
                    vm.Messages.Add(
                        new ErChatMessage { Role = ErChatMessageRole.User, Content = "q" }
                    );

                    SelectViaAutomation(tabControl, tabItems[1]);
                    WpfApplicationTestSupport.DoEvents();

                    dialogs.ConfirmCount.Should().Be(1);
                    vm.Connection.SelectedBackend.Should().Be(ErChatBackendKind.Codex);
                    vm.HasConversation.Should().BeFalse();
                    ShouldShowTab(tabControl, tabItems, 1);
                }
            );
        });
    }

    /// <summary>
    /// ターン実行中は BackendTabs の全 TabItem が IsEnabled=false になり、
    /// ターン終了後は再び IsEnabled=true へ戻ることを検証する。
    /// </summary>
    [Fact(DisplayName = "ターン実行中は接続方式タブが全て無効化される")]
    public void BackendTabs_DisabledDuringTurn_AndReenabledAfter()
    {
        WpfApplicationTestSupport.RunSta(() =>
        {
            WpfApplicationTestSupport.EnsureApplicationResources();

            var (vm, folder) = CreateVm();

            try
            {
                var dialog = WpfApplicationTestSupport.LoadXamlComponent(() =>
                    new AiChatDialog(vm, new RecordingShutdownFailureReporter())
                );

                // 画面外＋非アクティブで Show し、テスト実行中に開発者のデスクトップを妨げない
                dialog.WindowStartupLocation = WindowStartupLocation.Manual;
                dialog.Left = -4000;
                dialog.Top = -4000;
                dialog.ShowActivated = false;
                dialog.ShowInTaskbar = false;

                dialog.Show();

                try
                {
                    dialog.UpdateLayout();
                    WpfApplicationTestSupport.DoEvents();

                    var tabControl = dialog.FindName("BackendTabs") as TabControl;
                    tabControl
                        .Should()
                        .NotBeNull("実 XAML に接続方式タブの TabControl が存在すること");

                    var tabItems = tabControl!.Items.Cast<TabItem>().ToList();
                    tabItems
                        .Should()
                        .HaveCount(4, "API キー / Codex / Claude Code / Copilot の 4 タブ");

                    // 見た目の面（IsEnabled）を確認する。初期状態（ターン非実行中）は全タブ有効
                    tabItems
                        .Should()
                        .OnlyContain(
                            t => t.IsEnabled,
                            "初期状態ではどのタブも無効化されていないこと"
                        );

                    vm.IsTurnInProgress = true;
                    tabControl.UpdateLayout();
                    WpfApplicationTestSupport.DoEvents();

                    tabItems
                        .Should()
                        .OnlyContain(t => !t.IsEnabled, "ターン実行中は全タブが無効化されること");

                    vm.IsTurnInProgress = false;
                    tabControl.UpdateLayout();
                    WpfApplicationTestSupport.DoEvents();

                    tabItems
                        .Should()
                        .OnlyContain(t => t.IsEnabled, "ターン終了後はタブが再び有効になること");
                }
                finally
                {
                    dialog.Close();
                }
            }
            finally
            {
                if (Directory.Exists(folder))
                {
                    Directory.Delete(folder, recursive: true);
                }
            }
        });
    }

    /// <summary>
    /// ターン実行中は API キータブの設定パネル（プロバイダー・モデル・エンドポイント・API キー）が
    /// IsEnabled=false になり、ターン終了後は再び編集できることを検証する。
    /// これらの設定は実行中ターンの次の往復から即座に効き、プロバイダー変更は実行中ループが書き換える
    /// 会話履歴の列挙も伴うため、実行中は編集させない（見た目の面＝IsEnabled のみを検証する）。
    /// </summary>
    [Fact(DisplayName = "ターン実行中は API キー接続の設定パネルが無効化される")]
    public void ApiKeySettingsPanel_DisabledDuringTurn_AndReenabledAfter()
    {
        WpfApplicationTestSupport.RunSta(() =>
        {
            WpfApplicationTestSupport.EnsureApplicationResources();

            var (vm, folder) = CreateVm();

            try
            {
                var dialog = WpfApplicationTestSupport.LoadXamlComponent(() =>
                    new AiChatDialog(vm, new RecordingShutdownFailureReporter())
                );

                // 画面外＋非アクティブで Show し、テスト実行中に開発者のデスクトップを妨げない
                dialog.WindowStartupLocation = WindowStartupLocation.Manual;
                dialog.Left = -4000;
                dialog.Top = -4000;
                dialog.ShowActivated = false;
                dialog.ShowInTaskbar = false;

                dialog.Show();

                try
                {
                    dialog.UpdateLayout();
                    WpfApplicationTestSupport.DoEvents();

                    var panel = dialog.FindName("ApiKeySettingsPanel") as StackPanel;
                    panel.Should().NotBeNull("実 XAML に API キー設定パネルが存在すること");
                    panel!.IsEnabled.Should().BeTrue("初期状態では設定を編集できること");

                    vm.IsTurnInProgress = true;
                    dialog.UpdateLayout();
                    WpfApplicationTestSupport.DoEvents();

                    panel.IsEnabled.Should().BeFalse("ターン実行中は設定パネルが無効化されること");

                    vm.IsTurnInProgress = false;
                    dialog.UpdateLayout();
                    WpfApplicationTestSupport.DoEvents();

                    panel.IsEnabled.Should().BeTrue("ターン終了後は再び編集できること");
                }
                finally
                {
                    dialog.Close();
                }
            }
            finally
            {
                if (Directory.Exists(folder))
                {
                    Directory.Delete(folder, recursive: true);
                }
            }
        });
    }
}
