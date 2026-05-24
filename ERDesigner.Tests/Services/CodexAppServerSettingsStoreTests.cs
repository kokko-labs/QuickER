using System.IO;
using ERDesigner.Services;
using FluentAssertions;

namespace ERDesigner.Tests.Services;

/// <summary>
/// <see cref="CodexAppServerSettingsStore"/> の保存/読込を検証します。
/// </summary>
public class CodexAppServerSettingsStoreTests
{
    [Fact(DisplayName = "Save した設定を Load で復元できる")]
    public void SaveThenLoad_RoundTrip()
    {
        var folder = Path.Combine(Path.GetTempPath(), "ERDesignerTests", Guid.NewGuid().ToString("N"));
        var store = new CodexAppServerSettingsStore(folder);
        var expected = new CodexAppServerSettings { ModelProvider = "ollama-launch", Model = "gemma4:31b-cloud" };

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
