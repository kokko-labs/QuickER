using System.IO;
using AwesomeAssertions;
using QuickER.Services;
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
        var vm = new UpdateSettingsViewModel(new GuiAppSettingsStore(TempFolder()));

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

            var vm = new UpdateSettingsViewModel(store);

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

            var vm = new UpdateSettingsViewModel(store);
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
}
