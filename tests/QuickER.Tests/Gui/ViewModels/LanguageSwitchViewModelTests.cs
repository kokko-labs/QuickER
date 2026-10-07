using System;
using System.IO;
using AwesomeAssertions;
using QuickER.Gui.Abstractions;
using QuickER.Resources;
using QuickER.Services;
using QuickER.Tests.TestDoubles;
using QuickER.ViewModels;
using Xunit;

namespace QuickER.Tests.Gui.ViewModels;

/// <summary>
/// 言語切替 VM の挙動を検証する。切替は再起動反映方式で、確認 OK のときのみ再起動する。
/// </summary>
public class LanguageSwitchViewModelTests
{
    /// <summary>再起動サービスのスタブ（実際には再起動せず、呼び出し回数だけ記録する）</summary>
    private sealed class FakeRestartService : IApplicationRestartService
    {
        public int RestartCount { get; private set; }

        public void Restart() => RestartCount++;
    }

    /// <summary>一時フォルダのストアと各スタブを注入した VM を生成する（初期言語を保存済みにする）</summary>
    private static (
        LanguageSwitchViewModel Vm,
        StubDialogService Dialogs,
        FakeRestartService Restart,
        GuiAppSettingsStore Store
    ) CreateVm(string initialLanguage)
    {
        var folder = Path.Combine(
            Path.GetTempPath(),
            "QuickERTest_" + Guid.NewGuid().ToString("N")
        );
        var store = new GuiAppSettingsStore(folder);
        store.Save(new GuiAppSettings { Language = initialLanguage });

        var dialogs = new StubDialogService();
        var restart = new FakeRestartService();
        var vm = new LanguageSwitchViewModel(dialogs, store, restart);

        return (vm, dialogs, restart, store);
    }

    /// <summary>別言語を選んで確認 OK すると、設定を保存しつつ再起動する</summary>
    [Fact(DisplayName = "別言語を選び確認 OK なら設定保存＋再起動する")]
    public void SelectLanguage_DifferentAndConfirmed_SavesAndRestarts()
    {
        var (vm, dialogs, restart, store) = CreateVm("ja");
        dialogs.ConfirmResult = true;

        vm.SelectLanguageCommand.Execute("en");

        store.Load().Language.Should().Be("en");
        vm.CurrentLanguage.Should().Be("en");
        restart.RestartCount.Should().Be(1);
        dialogs
            .ConfirmMessages.Should()
            .ContainSingle()
            .Which.Should()
            .Be(Strings.Language_RestartConfirm);
    }

    /// <summary>
    /// 選んだ言語を保存できなかったときは、表示も言語も変えずにエラーで知らせ、再起動の確認も出さないことを検証する
    /// </summary>
    /// <remarks>
    /// 保存できないまま「再起動しますか」に OK すると、元の言語で起動し直すだけになる（何が起きたか分からない）。
    /// 既存ファイルを読めないためストアが書かずに戻る経路（例外にならない）で確かめる。
    /// </remarks>
    [Fact(DisplayName = "言語を保存できなければ表示を変えずエラーで知らせ再起動しない")]
    public void SelectLanguage_WhenSaveFails_ShowsErrorAndDoesNotRestart()
    {
        var (vm, dialogs, restart, store) = CreateVm("ja");
        dialogs.ConfirmResult = true;

        using (new FileStream(store.SettingsPath, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            vm.SelectLanguageCommand.Execute("en");
        }

        vm.CurrentLanguage.Should().Be("ja");
        store.Load().Language.Should().Be("ja");
        restart.RestartCount.Should().Be(0);
        dialogs.ConfirmMessages.Should().BeEmpty();
        dialogs
            .ErrorMessages.Should()
            .ContainSingle()
            .Which.Should()
            .Be(
                Strings.Settings_SaveFailed + Environment.NewLine + Strings.Settings_FileUnreadable
            );
    }

    /// <summary>別言語を選んでも確認をキャンセルすると、設定は保存されるが再起動しない</summary>
    [Fact(DisplayName = "別言語を選び確認キャンセルなら保存のみで再起動しない")]
    public void SelectLanguage_DifferentAndDeclined_SavesButDoesNotRestart()
    {
        var (vm, dialogs, restart, store) = CreateVm("ja");
        dialogs.ConfirmResult = false;

        vm.SelectLanguageCommand.Execute("en");

        store.Load().Language.Should().Be("en");
        vm.CurrentLanguage.Should().Be("en");
        restart.RestartCount.Should().Be(0);
    }

    /// <summary>同じ言語を選び直したときは何もしない（確認も再起動も出さない）</summary>
    [Fact(DisplayName = "同じ言語を選び直すと確認も再起動も出さない")]
    public void SelectLanguage_Same_NoOp()
    {
        var (vm, dialogs, restart, _) = CreateVm("ja");

        vm.SelectLanguageCommand.Execute("ja");

        restart.RestartCount.Should().Be(0);
        dialogs.ConfirmMessages.Should().BeEmpty();
    }
}
