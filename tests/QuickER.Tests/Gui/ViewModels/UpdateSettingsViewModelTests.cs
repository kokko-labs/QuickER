using System.IO;
using AwesomeAssertions;
using QuickER.Resources;
using QuickER.Services;
using QuickER.Tests.TestDoubles;
using QuickER.ViewModels;

namespace QuickER.Tests.Gui.ViewModels;

/// <summary>
/// 起動時更新チェックのトグル（<see cref="UpdateSettingsViewModel"/>）の
/// 初期値読み出しと永続化を検証するテストクラス。
/// 永続化先は一時フォルダへ隔離し、実 %LOCALAPPDATA% へは触れない。
/// </summary>
public class UpdateSettingsViewModelTests
{
    /// <summary>一意の一時フォルダパスを作る</summary>
    private static string TempFolder() =>
        Path.Combine(Path.GetTempPath(), "QuickERTests", Guid.NewGuid().ToString("N"));

    /// <summary>未保存（キーなし）なら「確認する」で初期化されることを検証する</summary>
    [Fact(DisplayName = "未保存なら初期値は確認する")]
    public void Constructor_WhenNoSettings_DefaultsToEnabled()
    {
        var vm = new UpdateSettingsViewModel(
            new StubDialogService(),
            new GuiAppSettingsStore(TempFolder())
        );

        vm.IsCheckOnStartupEnabled.Should().BeTrue();
    }

    /// <summary>保存済みのオプトアウトを初期値として読み出すことを検証する</summary>
    [Fact(DisplayName = "保存済みのオプトアウトを初期値として読む")]
    public void Constructor_WhenDisabledInSettings_LoadsDisabled()
    {
        var folder = TempFolder();

        try
        {
            var store = new GuiAppSettingsStore(folder);
            store.Save(new GuiAppSettings { CheckForUpdatesOnStartup = false });

            var vm = new UpdateSettingsViewModel(new StubDialogService(), store);

            vm.IsCheckOnStartupEnabled.Should().BeFalse();
        }
        finally
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }
        }
    }

    /// <summary>
    /// トグルの切り替えが即座に設定へ保存され、かつ他セクション（言語）を消さないことを検証する
    /// （read-modify-write の前提）。
    /// </summary>
    [Fact(DisplayName = "切り替えは保存され、他の設定を消さない")]
    public void Toggle_PersistsAndPreservesOtherSections()
    {
        var folder = TempFolder();

        try
        {
            var store = new GuiAppSettingsStore(folder);
            store.Save(new GuiAppSettings { Language = "ja" });

            var vm = new UpdateSettingsViewModel(new StubDialogService(), store);
            vm.IsCheckOnStartupEnabled = false;

            var reloaded = store.Load();
            reloaded.CheckForUpdatesOnStartup.Should().BeFalse();
            reloaded.Language.Should().Be("ja");

            // 戻す操作も同じ経路で保存される
            vm.IsCheckOnStartupEnabled = true;
            store.Load().CheckForUpdatesOnStartup.Should().BeTrue();
        }
        finally
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }
        }
    }

    /// <summary>
    /// 切り替えを保存できなかったときは、表示を元に戻して理由つきのエラーで知らせることを検証する
    /// </summary>
    /// <remarks>
    /// 2 通りを確かめる。設定ファイルが読み取り専用で書き込みが失敗するとき（例外）と、既存ファイルを読めないため
    /// ストアが書かずに戻ったとき（例外にならない）。どちらも戻さないと、チェックは外れて見えるのに次回起動でも
    /// 更新を確認する、という食い違いが黙って残っていた。
    /// </remarks>
    [Theory(DisplayName = "保存できない切り替えは表示を戻してエラーで知らせる")]
    [InlineData(true)]
    [InlineData(false)]
    public void Toggle_WhenSaveFails_RevertsAndShowsError(bool readOnlyFile)
    {
        var folder = TempFolder();
        var store = new GuiAppSettingsStore(folder);
        store.Save(new GuiAppSettings { CheckForUpdatesOnStartup = true });
        var dialogs = new StubDialogService();
        var vm = new UpdateSettingsViewModel(dialogs, store);

        try
        {
            if (readOnlyFile)
            {
                File.SetAttributes(store.SettingsPath, FileAttributes.ReadOnly);
                vm.IsCheckOnStartupEnabled = false;
            }
            else
            {
                using (
                    new FileStream(
                        store.SettingsPath,
                        FileMode.Open,
                        FileAccess.Read,
                        FileShare.None
                    )
                )
                {
                    vm.IsCheckOnStartupEnabled = false;
                }
            }

            vm.IsCheckOnStartupEnabled.Should().BeTrue("保存できなかった切り替えは表示も戻す");
            store.Load().CheckForUpdatesOnStartup.Should().BeTrue();
            var error = dialogs.ErrorMessages.Should().ContainSingle().Subject;
            error.Should().StartWith(Strings.Settings_SaveFailed);

            if (!readOnlyFile)
            {
                error.Should().EndWith(Strings.Settings_FileUnreadable);
            }
        }
        finally
        {
            File.SetAttributes(store.SettingsPath, FileAttributes.Normal);
            Directory.Delete(folder, recursive: true);
        }
    }
}
