using System.IO;
using FluentAssertions;
using QuickER.CodeGen.UI;

namespace QuickER.Tests.Services;

/// <summary><see cref="CSharpGenerationSettingsStore"/> の保存・読込を検証するテストクラス</summary>
public class CSharpGenerationSettingsStoreTests
{
    /// <summary>一意の一時フォルダパスを作る</summary>
    private static string TempFolder() =>
        Path.Combine(Path.GetTempPath(), "QuickERTests", Guid.NewGuid().ToString("N"));

    /// <summary>保存した設定が同じ内容で読み込めることを検証する</summary>
    [Fact(DisplayName = "保存した C# 生成設定を読み込める")]
    public void SaveThenLoad_RoundTrips()
    {
        var folder = TempFolder();

        try
        {
            var store = new CSharpGenerationSettingsStore(folder);
            store.Save(
                new CSharpGenerationSettings
                {
                    SplitFilesByCategory = true,
                    BaseNamespace = "Acme.App",
                    EntityNamespace = "Acme.App.Domain",
                    GenerateRepositories = false,
                    GenerateValueObjects = true,
                    OutputFolderPath = @"C:\out",
                }
            );

            var loaded = store.Load();

            loaded.SplitFilesByCategory.Should().BeTrue();
            loaded.BaseNamespace.Should().Be("Acme.App");
            loaded.EntityNamespace.Should().Be("Acme.App.Domain");
            loaded.GenerateRepositories.Should().BeFalse();
            loaded.GenerateValueObjects.Should().BeTrue();
            loaded.OutputFolderPath.Should().Be(@"C:\out");
        }
        finally
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }
        }
    }

    /// <summary>設定ファイルが無い場合は工場出荷既定を返すことを検証する</summary>
    [Fact(DisplayName = "未保存なら工場出荷既定を返す")]
    public void Load_WhenMissing_ReturnsDefault()
    {
        var store = new CSharpGenerationSettingsStore(TempFolder());

        var loaded = store.Load();

        loaded.SplitFilesByCategory.Should().BeFalse();
        loaded.BaseNamespace.Should().Be(CSharpGenerationSettings.DefaultBaseNamespace);
        loaded.GenerateEntityClasses.Should().BeTrue();
    }

    /// <summary>破損ファイルでも例外を投げず既定値へフォールバックすることを検証する</summary>
    [Fact(DisplayName = "破損ファイルなら既定値へフォールバック")]
    public void Load_WhenCorrupt_ReturnsDefault()
    {
        var folder = TempFolder();

        try
        {
            Directory.CreateDirectory(folder);
            var store = new CSharpGenerationSettingsStore(folder);
            File.WriteAllText(store.SettingsPath, "{ this is not valid json");

            var loaded = store.Load();

            loaded.BaseNamespace.Should().Be(CSharpGenerationSettings.DefaultBaseNamespace);
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
