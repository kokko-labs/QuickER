using System.IO;
using AwesomeAssertions;
using QuickER.AI.Mock;

namespace QuickER.Tests.AI.Mock;

/// <summary>
/// API キー方式の素材読み（<see cref="ApiKeyMockProjectAgent.ReadScreenHtml"/>）が、マニフェストの画面のファイル名を
/// ストアと同じ判定（<see cref="MockFolderStore.CheckScreenFileName"/>）に通し、設計フォルダの外を読まないことを固定する
/// </summary>
/// <remarks>
/// 画面のファイル名はマニフェスト（<c>mock.json</c>）から来る。ストアは開くときに検証するが、開いた後に書き換えられた
/// 絶対パスや <c>..</c> を素材読みがそのまま <c>Path.Combine</c> すると、設計フォルダの外のファイルを AI へ渡してしまう。
/// </remarks>
public sealed class ApiKeyMockProjectAgentScreenReadTests : IDisposable
{
    private readonly string _folder = Path.Combine(
        Path.GetTempPath(),
        "QuickERTests",
        "screen-read-" + Guid.NewGuid().ToString("N")
    );

    public ApiKeyMockProjectAgentScreenReadTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_folder, recursive: true);
        }
        catch (IOException)
        {
            // 後始末の失敗は無視する
        }
    }

    [Fact(
        DisplayName = "画面のファイル名として断る名前（絶対パス・..・.html でない）は読まずに空を返し、直下の .html は読む"
    )]
    public void ReadScreenHtml_ReadsOnlyValidScreenFiles()
    {
        var outside = Path.Combine(_folder, "secret.html");
        File.WriteAllText(outside, "SECRET");
        var design = Path.Combine(_folder, "proj", "design", "mock");
        Directory.CreateDirectory(design);
        File.WriteAllText(Path.Combine(design, "orders.html"), "<html>orders</html>");
        File.WriteAllText(Path.Combine(design, "notes.txt"), "NOTES");

        ApiKeyMockProjectAgent.ReadScreenHtml(design, outside).Should().BeEmpty();
        ApiKeyMockProjectAgent
            .ReadScreenHtml(design, Path.Combine("..", "..", "..", "secret.html"))
            .Should()
            .BeEmpty();
        ApiKeyMockProjectAgent.ReadScreenHtml(design, "notes.txt").Should().BeEmpty();
        ApiKeyMockProjectAgent.ReadScreenHtml(design, string.Empty).Should().BeEmpty();
        ApiKeyMockProjectAgent
            .ReadScreenHtml(design, "orders.html")
            .Should()
            .Be("<html>orders</html>");
    }
}
