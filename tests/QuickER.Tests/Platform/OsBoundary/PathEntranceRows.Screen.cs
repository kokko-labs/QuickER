using System.IO;
using AwesomeAssertions;
using QuickER.AI.Mock;
using QuickER.Tests.TestSupport.Engines;
using static QuickER.Tests.Platform.OsBoundary.PathEntrance;
using static QuickER.Tests.Platform.OsBoundary.PathOutcome;
using static QuickER.Tests.Platform.OsBoundary.PathShape;

namespace QuickER.Tests.Platform.OsBoundary;

/// <summary>
/// パスの入口の表：API キー方式の素材読みの画面のファイル名（QuickER.AI.Mock の
/// <c>ApiKeyMockProjectAgent.ReadScreenHtml</c>・設計フォルダの実ファイルを読む）
/// </summary>
internal static partial class PathEntranceRows
{
    private const string OrdersHtml = "<html>orders</html>";

    /// <summary>元のテストと同じ実ファイルを一時フォルダに作る（設計フォルダの外の秘密・直下の画面・html でないファイル）</summary>
    private static (string Design, string Outside) PrepareScreenFolder(PathObservation o)
    {
        var outside = Path.Combine(o.TempFolder, "secret.html");
        File.WriteAllText(outside, "SECRET");
        var design = Path.Combine(o.TempFolder, "proj", "design", "mock");
        Directory.CreateDirectory(design);
        File.WriteAllText(Path.Combine(design, "orders.html"), OrdersHtml);
        File.WriteAllText(Path.Combine(design, "notes.txt"), "NOTES");
        return (design, outside);
    }

    /// <summary>画面のファイル名の行を作る（<paramref name="file"/> は設計フォルダと外の秘密のパスから決める）</summary>
    private static ScenarioRow<PathEntranceCase> Screen(
        string id,
        PathShape shape,
        Func<string, string, string> file,
        string? expectedHtml
    ) =>
        Row(
            id,
            "画面のファイル名として断る名前（絶対パス・..・.html でない）は読まずに空を返し、直下の .html は読む",
            ReadScreenHtml,
            shape,
            expectedHtml is null ? Rejected : Accepted,
            o =>
            {
                var (design, outside) = PrepareScreenFolder(o);
                return ApiKeyMockProjectAgent.ReadScreenHtml(design, file(design, outside));
            },
            o =>
            {
                if (expectedHtml is null)
                {
                    ((string)o.Value!).Should().BeEmpty();
                }
                else
                {
                    ((string)o.Value!).Should().Be(expectedHtml);
                }
            }
        );

    private static IEnumerable<ScenarioRow<PathEntranceCase>> ScreenRows() =>
        [
            Screen(
                "screen-read-rejects-absolute-path-outside",
                Absolute,
                (_, outside) => outside,
                null
            ),
            Screen(
                "screen-read-rejects-parent-traversal",
                ParentTraversal,
                (_, _) => Path.Combine("..", "..", "..", "secret.html"),
                null
            ),
            Screen("screen-read-rejects-non-html", NonHtml, (_, _) => "notes.txt", null),
            Screen(
                "screen-read-rejects-empty-name",
                EmptyOrWhitespace,
                (_, _) => string.Empty,
                null
            ),
            Screen(
                "screen-read-reads-direct-child-html",
                ValidScreenFile,
                (_, _) => "orders.html",
                OrdersHtml
            ),
        ];
}
