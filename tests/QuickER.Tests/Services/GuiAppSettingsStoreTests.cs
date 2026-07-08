using System.IO;
using FluentAssertions;
using QuickER.Services;

namespace QuickER.Tests.Services;

/// <summary><see cref="GuiAppSettingsStore"/> の保存・読込を検証するテストクラス</summary>
public class GuiAppSettingsStoreTests
{
    /// <summary>一意の一時フォルダパスを作る</summary>
    private static string TempFolder() =>
        Path.Combine(Path.GetTempPath(), "QuickERTests", Guid.NewGuid().ToString("N"));

    /// <summary>保存した言語設定が同じ内容で読み込めることを検証する</summary>
    [Fact(DisplayName = "保存した言語設定を読み込める")]
    public void SaveThenLoad_RoundTrips()
    {
        var folder = TempFolder();

        try
        {
            var store = new GuiAppSettingsStore(folder);
            store.Save(new GuiAppSettings { Language = "en" });

            var loaded = store.Load();

            loaded.Language.Should().Be("en");
        }
        finally
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }
        }
    }

    /// <summary>未保存なら Language は未設定（null）を返すことを検証する</summary>
    [Fact(DisplayName = "未保存なら Language は null")]
    public void Load_WhenMissing_ReturnsNullLanguage()
    {
        var store = new GuiAppSettingsStore(TempFolder());

        var loaded = store.Load();

        loaded.Language.Should().BeNull();
    }

    /// <summary>破損ファイルでも例外を投げず既定値へフォールバックすることを検証する</summary>
    [Fact(DisplayName = "破損ファイルなら既定値へフォールバック")]
    public void Load_WhenCorrupt_ReturnsDefault()
    {
        var folder = TempFolder();

        try
        {
            Directory.CreateDirectory(folder);
            var store = new GuiAppSettingsStore(folder);
            File.WriteAllText(store.SettingsPath, "{ this is not valid json");

            var loaded = store.Load();

            loaded.Language.Should().BeNull();
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
