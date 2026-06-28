using System.IO;
using FluentAssertions;
using QuickER.AI;
using QuickER.Services;

namespace QuickER.Tests.Services;

/// <summary><see cref="CodexAppServerSettingsStore"/> による設定ファイルの保存・読込をテストするクラス</summary>
public class CodexAppServerSettingsStoreTests
{
    /// <summary>Save した設定を Load で読み戻し、モデルプロバイダーとモデル名が往復で保持されることを検証する</summary>
    [Fact(DisplayName = "Save した設定を Load で復元できる")]
    public void SaveThenLoad_RoundTrip()
    {
        var folder = Path.Combine(Path.GetTempPath(), "QuickERTests", Guid.NewGuid().ToString("N"));
        var store = new CodexAppServerSettingsStore(folder);
        var expected = new CodexAppServerSettings
        {
            ModelProvider = "ollama-launch",
            Model = "gemma4:31b-cloud",
        };

        try
        {
            store.Save(expected);

            var actual = store.Load();
            actual.ModelProvider.Should().Be(expected.ModelProvider);
            actual.Model.Should().Be(expected.Model);
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
