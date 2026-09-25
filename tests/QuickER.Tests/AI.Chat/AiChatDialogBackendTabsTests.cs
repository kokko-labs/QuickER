using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using AwesomeAssertions;
using QuickER.AI;
using QuickER.AI.Chat;
using QuickER.AI.UI;
using QuickER.Tests.AI;
using QuickER.Tests.TestDoubles;
using QuickER.Tests.TestSupport;

namespace QuickER.Tests.AI.Chat;

/// <summary>
/// <see cref="AiChatDialog"/> の接続方式タブ（BackendTabs）が、実 XAML の
/// <c>Style.Triggers</c>（<see cref="AiChatDialogViewModel.IsTurnInProgress"/> への DataTrigger）で
/// ターン実行中に無効化されることを検証するテストクラス（C2）。
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

    private static (AiChatDialogViewModel vm, string folder) CreateVm()
    {
        var folder = Path.Combine(Path.GetTempPath(), "QuickERTests", Guid.NewGuid().ToString("N"));
        var keyStore = new InMemoryApiKeyStore();
        var vm = new AiChatDialogViewModel(
            host: null,
            dispatcher: new SyncUiDispatcher(),
            settingsStore: new AiSettingsStore(folder),
            codexClient: new FakeCodexAppServerClient(),
            apiKeyLoader: keyStore.Load,
            apiKeySaver: keyStore.Save
        );
        return (vm, folder);
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
                    new AiChatDialog(vm)
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
                    new AiChatDialog(vm)
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
