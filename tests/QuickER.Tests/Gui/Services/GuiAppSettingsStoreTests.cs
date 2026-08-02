using System.IO;
using AwesomeAssertions;
using QuickER.Services;

namespace QuickER.Tests.Gui.Services;

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

    /// <summary>保存した表示トグルが同じ内容で読み込めることを検証する</summary>
    [Fact(DisplayName = "保存した DiagramView 設定を読み込める")]
    public void SaveThenLoad_DiagramView_RoundTrips()
    {
        var folder = TempFolder();

        try
        {
            var store = new GuiAppSettingsStore(folder);
            store.Save(
                new GuiAppSettings
                {
                    DiagramView = new DiagramViewSettings
                    {
                        ShowColumnDescriptions = true,
                        ShowNullability = false,
                        IsCompactView = true,
                    },
                }
            );

            var loaded = store.Load();

            loaded.DiagramView.ShowColumnDescriptions.Should().BeTrue();
            loaded.DiagramView.ShowNullability.Should().BeFalse();
            loaded.DiagramView.IsCompactView.Should().BeTrue();
        }
        finally
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }
        }
    }

    /// <summary>未保存なら DiagramView は既定値（NULL 許容表示のみ ON）を返すことを検証する</summary>
    [Fact(DisplayName = "未保存なら DiagramView は既定値")]
    public void Load_WhenMissing_ReturnsDefaultDiagramView()
    {
        var store = new GuiAppSettingsStore(TempFolder());

        var loaded = store.Load();

        loaded.DiagramView.ShowColumnDescriptions.Should().BeFalse();
        loaded.DiagramView.ShowNullability.Should().BeTrue();
        loaded.DiagramView.IsCompactView.Should().BeFalse();
    }

    /// <summary>破損ファイルでも DiagramView は既定値へフォールバックすることを検証する</summary>
    [Fact(DisplayName = "破損ファイルなら DiagramView も既定値へフォールバック")]
    public void Load_WhenCorrupt_ReturnsDefaultDiagramView()
    {
        var folder = TempFolder();

        try
        {
            Directory.CreateDirectory(folder);
            var store = new GuiAppSettingsStore(folder);
            File.WriteAllText(store.SettingsPath, "{ this is not valid json");

            var loaded = store.Load();

            loaded.DiagramView.ShowNullability.Should().BeTrue();
        }
        finally
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }
        }
    }

    /// <summary>DiagramView だけを変更する保存が Language を消さないことを検証する（read-modify-write の前提）</summary>
    [Fact(DisplayName = "DiagramView の保存は Language を消さない")]
    public void Save_DiagramView_PreservesLanguage()
    {
        var folder = TempFolder();

        try
        {
            var store = new GuiAppSettingsStore(folder);

            // 先に言語のみを書く
            store.Save(new GuiAppSettings { Language = "en" });

            // 後から DiagramView を read-modify-write で書く（言語は温存されるべき）
            var settings = store.Load();
            settings.DiagramView = new DiagramViewSettings { ShowColumnDescriptions = true };
            store.Save(settings);

            var reloaded = store.Load();
            reloaded.Language.Should().Be("en");
            reloaded.DiagramView.ShowColumnDescriptions.Should().BeTrue();
        }
        finally
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }
        }
    }

    /// <summary>Language だけを変更する保存が DiagramView を消さないことを検証する（逆方向の read-modify-write）</summary>
    [Fact(DisplayName = "Language の保存は DiagramView を消さない")]
    public void Save_Language_PreservesDiagramView()
    {
        var folder = TempFolder();

        try
        {
            var store = new GuiAppSettingsStore(folder);

            // 先に DiagramView のみを書く
            store.Save(
                new GuiAppSettings
                {
                    DiagramView = new DiagramViewSettings { IsCompactView = true },
                }
            );

            // 後から言語を read-modify-write で書く（DiagramView は温存されるべき）
            var settings = store.Load();
            settings.Language = "ja";
            store.Save(settings);

            var reloaded = store.Load();
            reloaded.Language.Should().Be("ja");
            reloaded.DiagramView.IsCompactView.Should().BeTrue();
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
