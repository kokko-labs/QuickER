using System.Globalization;
using System.IO;
using AwesomeAssertions;
using QuickER.Gui.Abstractions;
using QuickER.Resources;
using QuickER.Services;
using QuickER.Tests.TestSupport.Engines;
using QuickER.ViewModels;
using static QuickER.Tests.Gui.Services.Lifecycle.LifecycleEntrance;
using static QuickER.Tests.Gui.Services.Lifecycle.LifecycleFault;

namespace QuickER.Tests.Gui.Services.Lifecycle;

/// <summary>
/// アプリの一生の表：言語の切替（QuickER.Gui の <c>LanguageSwitchViewModel</c>）。切替は再起動反映方式で、確認 OK のときのみ再起動する。
/// 再起動の確認は「選んだ言語がいま表示中の言語と違うとき」だけ出るので、表示中の言語（UI カルチャ）を初期言語に合わせて固定する
/// （起動時に App が保存した言語を適用するのと同じ状態。テストを動かす機械のカルチャに結果を左右させない）。
/// 変えた UI カルチャは行の終わりに戻す
/// </summary>
internal static partial class LifecycleRows
{
    /// <summary>再起動サービスのスタブ（実際には再起動せず、呼び出し回数だけ記録する）</summary>
    private sealed class FakeRestartService : IApplicationRestartService
    {
        public int RestartCount { get; private set; }

        public void Restart() => RestartCount++;
    }

    /// <summary>一時フォルダのストアと各スタブを注入した VM を作る（初期言語を保存済みにし、UI カルチャを合わせる）</summary>
    private static (
        LanguageSwitchViewModel Vm,
        FakeRestartService Restart,
        GuiAppSettingsStore Store
    ) CreateLanguageVm(LifecycleObservation o, string initialLanguage)
    {
        var original = CultureInfo.CurrentUICulture;
        o.OnDispose(() => CultureInfo.CurrentUICulture = original);

        var store = new GuiAppSettingsStore(o.Folder);
        store.Save(new GuiAppSettings { Language = initialLanguage });
        CultureInfo.CurrentUICulture = new CultureInfo(initialLanguage == "ja" ? "ja-JP" : "en-US");

        var restart = new FakeRestartService();
        var vm = new LanguageSwitchViewModel(o.Dialogs, store, restart);
        o.Bag["vm"] = vm;
        o.Bag["restart"] = restart;
        o.Bag["store"] = store;

        return (vm, restart, store);
    }

    private static IEnumerable<ScenarioRow<LifecycleCase>> LanguageRows()
    {
        yield return Row(
            "language-different-and-confirmed-saves-and-restarts-none",
            "別言語を選び確認 OK なら設定保存＋再起動する",
            LanguageSelect,
            None,
            o =>
            {
                var (vm, _, _) = CreateLanguageVm(o, "ja");
                o.Dialogs.ConfirmResult = true;

                vm.SelectLanguageCommand.Execute("en");
            },
            o =>
            {
                o.Get<GuiAppSettingsStore>("store").Load().Language.Should().Be("en");
                o.Get<LanguageSwitchViewModel>("vm").CurrentLanguage.Should().Be("en");
                o.Get<FakeRestartService>("restart").RestartCount.Should().Be(1);
                o.Dialogs.ConfirmMessages.Should()
                    .ContainSingle()
                    .Which.Should()
                    .Be(Strings.Language_RestartConfirm);
            }
        );

        // 保存できないまま「再起動しますか」に OK すると、元の言語で起動し直すだけになる（何が起きたか分からない）。
        // 既存ファイルを読めないためストアが書かずに戻る経路（例外にならない）は、設定ファイルを開いたまま掴むことでしか作れない
        yield return Row(
            "language-save-failure-shows-error-and-does-not-restart-save-fails",
            "言語を保存できなければ表示を変えずエラーで知らせ再起動しない",
            LanguageSelect,
            SaveFails,
            o =>
            {
                var (vm, _, store) = CreateLanguageVm(o, "ja");
                o.Dialogs.ConfirmResult = true;

                using (
                    new FileStream(
                        store.SettingsPath,
                        FileMode.Open,
                        FileAccess.Read,
                        FileShare.None
                    )
                )
                {
                    vm.SelectLanguageCommand.Execute("en");
                }
            },
            o =>
            {
                o.Get<LanguageSwitchViewModel>("vm").CurrentLanguage.Should().Be("ja");
                o.Get<GuiAppSettingsStore>("store").Load().Language.Should().Be("ja");
                o.Get<FakeRestartService>("restart").RestartCount.Should().Be(0);
                o.Dialogs.ConfirmMessages.Should().BeEmpty();
                o.Dialogs.ErrorMessages.Should()
                    .ContainSingle()
                    .Which.Should()
                    .Be(
                        Strings.Settings_SaveFailed
                            + Environment.NewLine
                            + Strings.Settings_FileUnreadable
                    );
            }
        );

        yield return Row(
            "language-different-and-declined-saves-without-restart-confirm-declined",
            "別言語を選び確認キャンセルなら保存のみで再起動しない",
            LanguageSelect,
            ConfirmDeclined,
            o =>
            {
                var (vm, _, _) = CreateLanguageVm(o, "ja");
                o.Dialogs.ConfirmResult = false;

                vm.SelectLanguageCommand.Execute("en");
            },
            o =>
            {
                o.Get<GuiAppSettingsStore>("store").Load().Language.Should().Be("en");
                o.Get<LanguageSwitchViewModel>("vm").CurrentLanguage.Should().Be("en");
                o.Get<FakeRestartService>("restart").RestartCount.Should().Be(0);
            }
        );

        yield return Row(
            "language-same-language-shows-no-confirmation-and-does-not-restart-same-language",
            "同じ言語を選び直すと確認も再起動も出さない",
            LanguageSelect,
            SameLanguage,
            o =>
            {
                var (vm, _, _) = CreateLanguageVm(o, "ja");

                vm.SelectLanguageCommand.Execute("ja");
            },
            o =>
            {
                o.Get<FakeRestartService>("restart").RestartCount.Should().Be(0);
                o.Dialogs.ConfirmMessages.Should().BeEmpty();
            }
        );
    }
}
