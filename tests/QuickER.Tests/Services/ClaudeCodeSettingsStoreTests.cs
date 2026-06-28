using System.IO;
using FluentAssertions;
using QuickER.AI;
using QuickER.Services;

namespace QuickER.Tests.Services;

/// <summary><see cref="ClaudeCodeSettingsStore"/> の保存・読込を検証するテストクラス</summary>
public class ClaudeCodeSettingsStoreTests
{
    /// <summary>保存した設定が同じ内容で読み込めることを検証する</summary>
    [Fact(DisplayName = "保存した Claude Code 設定を読み込める")]
    public void SaveThenLoad_RoundTrips()
    {
        var folder = Path.Combine(Path.GetTempPath(), "QuickERTests", Guid.NewGuid().ToString("N"));

        try
        {
            var store = new ClaudeCodeSettingsStore(folder);
            store.Save(new ClaudeCodeSettings { Model = "sonnet" });

            var loaded = store.Load();

            loaded.Model.Should().Be("sonnet");
        }
        finally
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }
        }
    }

    /// <summary>設定ファイルが無い場合は既定値（空モデル）を返すことを検証する</summary>
    [Fact(DisplayName = "未保存なら既定値を返す")]
    public void Load_WhenMissing_ReturnsDefault()
    {
        var folder = Path.Combine(Path.GetTempPath(), "QuickERTests", Guid.NewGuid().ToString("N"));
        var store = new ClaudeCodeSettingsStore(folder);

        var loaded = store.Load();

        loaded.Model.Should().BeEmpty();
    }
}
