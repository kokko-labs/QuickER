using System.IO;
using FluentAssertions;
using QuickER.AI;

namespace QuickER.Tests.Services;

/// <summary>
/// <see cref="ChatUiSettingsStore"/> の保存・読込と、
/// <see cref="ChatUiSettings.ParseLastBackend"/> の解釈を検証するテストクラス。
/// </summary>
public class ChatUiSettingsStoreTests
{
    /// <summary>一時フォルダに隔離したストアを生成する</summary>
    private static (ChatUiSettingsStore store, string folder) CreateStore()
    {
        var folder = Path.Combine(Path.GetTempPath(), "QuickERTests", Guid.NewGuid().ToString("N"));
        return (new ChatUiSettingsStore("test-ui.json", folder), folder);
    }

    private static void Cleanup(string folder)
    {
        if (Directory.Exists(folder))
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact(DisplayName = "保存した LastBackend が読込で復元される")]
    public void SaveAndLoad_RoundTripsLastBackend()
    {
        var (store, folder) = CreateStore();

        try
        {
            store.Save(new ChatUiSettings { LastBackend = "ClaudeCode" });

            store.Load().LastBackend.Should().Be("ClaudeCode");
        }
        finally
        {
            Cleanup(folder);
        }
    }

    [Fact(DisplayName = "ファイルが無ければ既定値を返す")]
    public void Load_ReturnsDefault_WhenFileMissing()
    {
        var (store, folder) = CreateStore();

        try
        {
            store.Load().LastBackend.Should().BeEmpty();
        }
        finally
        {
            Cleanup(folder);
        }
    }

    [Fact(DisplayName = "破損ファイルは既定値へフォールバックする")]
    public void Load_ReturnsDefault_WhenFileBroken()
    {
        var (store, folder) = CreateStore();

        try
        {
            Directory.CreateDirectory(folder);
            File.WriteAllText(store.SettingsPath, "{ broken json");

            store.Load().LastBackend.Should().BeEmpty();
        }
        finally
        {
            Cleanup(folder);
        }
    }

    [Theory(DisplayName = "ParseLastBackend は名前を大文字小文字を無視して解釈する")]
    [InlineData("ApiKey", ErChatBackendKind.ApiKey)]
    [InlineData("codex", ErChatBackendKind.Codex)]
    [InlineData("CLAUDECODE", ErChatBackendKind.ClaudeCode)]
    public void ParseLastBackend_ParsesKnownNames(string value, ErChatBackendKind expected)
    {
        new ChatUiSettings { LastBackend = value }
            .ParseLastBackend()
            .Should()
            .Be(expected);
    }

    [Theory(DisplayName = "ParseLastBackend は空・不正値を null にする")]
    [InlineData("")]
    [InlineData("Unknown")]
    public void ParseLastBackend_ReturnsNull_ForInvalidValues(string value)
    {
        new ChatUiSettings { LastBackend = value }
            .ParseLastBackend()
            .Should()
            .BeNull();
    }
}
