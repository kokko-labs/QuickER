using System.IO;
using System.Text;
using FluentAssertions;
using QuickER.Services;

namespace QuickER.Tests.Services;

/// <summary>
/// <see cref="MockPreviewFileStore"/> の一時ファイル書き出し・<c>file:///</c> URI 生成・
/// 版ごとの別名化・削除を検証するテストクラス。
/// </summary>
public class MockPreviewFileStoreTests
{
    private static string TempFolder() =>
        Path.Combine(Path.GetTempPath(), "QuickERTests", Guid.NewGuid().ToString("N"));

    /// <summary>HTML が UTF-8（BOM なし）で書き出され、返る URI がそのファイルを指すことを検証する</summary>
    [Fact(DisplayName = "HTML を UTF-8 で書き出し file URI を返す")]
    public void Write_WritesUtf8AndReturnsFileUri()
    {
        var folder = TempFolder();
        var store = new MockPreviewFileStore(folder);

        try
        {
            const string html = "<!DOCTYPE html><html><body><h1>日本語見出し</h1></body></html>";
            var uri = store.Write(html);

            uri.IsFile.Should().BeTrue();
            uri.Scheme.Should().Be("file");

            var path = uri.LocalPath;
            File.Exists(path).Should().BeTrue();
            File.ReadAllText(path, Encoding.UTF8).Should().Be(html);

            // BOM が付いていないこと（先頭 3 バイトが EF BB BF でない）
            var bytes = File.ReadAllBytes(path);
            (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
                .Should()
                .BeFalse();
        }
        finally
        {
            store.Clear();
        }
    }

    /// <summary>版ごとにファイル名（URI）が変わり、キャッシュ回避されることを検証する</summary>
    [Fact(DisplayName = "版ごとに別名の URI を返す")]
    public void Write_ProducesDistinctUrisPerRevision()
    {
        var folder = TempFolder();
        var store = new MockPreviewFileStore(folder);

        try
        {
            var first = store.Write("<html>1</html>");
            var second = store.Write("<html>2</html>");

            first.Should().NotBe(second);
            File.Exists(first.LocalPath).Should().BeTrue();
            File.Exists(second.LocalPath).Should().BeTrue();
        }
        finally
        {
            store.Clear();
        }
    }

    /// <summary>Clear で書き出しフォルダごと削除されることを検証する</summary>
    [Fact(DisplayName = "Clear で一時ファイルを削除する")]
    public void Clear_RemovesFolder()
    {
        var folder = TempFolder();
        var store = new MockPreviewFileStore(folder);
        store.Write("<html></html>");

        Directory.Exists(folder).Should().BeTrue();

        store.Clear();

        Directory.Exists(folder).Should().BeFalse();
    }
}
