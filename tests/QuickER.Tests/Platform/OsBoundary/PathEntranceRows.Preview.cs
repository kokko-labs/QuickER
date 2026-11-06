using System.IO;
using AwesomeAssertions;
using QuickER.AI.Mock;
using QuickER.Tests.TestSupport.Engines;
using static QuickER.Tests.Platform.OsBoundary.PathEntrance;
using static QuickER.Tests.Platform.OsBoundary.PathOutcome;
using static QuickER.Tests.Platform.OsBoundary.PathShape;

namespace QuickER.Tests.Platform.OsBoundary;

/// <summary>
/// パスの入口の表：モックのプレビューの許可判定（QuickER.AI.Mock の <c>MockPreviewPanel.IsNavigationAllowed</c> と
/// <c>MockPreviewPanel.IsResourceAllowed</c>）
/// </summary>
internal static partial class PathEntranceRows
{
    private static Uri RootFile(string name) => new(Path.Combine(Root, name));

    private static string ParentOfRoot => Directory.GetParent(Root)!.FullName;

    /// <summary>遷移の許可判定の行を作る</summary>
    private static ScenarioRow<PathEntranceCase> Nav(
        string id,
        string description,
        PathShape shape,
        Uri? target,
        string? root,
        bool accepted
    ) =>
        Row(
            id,
            description,
            PreviewNavigation,
            shape,
            accepted ? Accepted : Rejected,
            _ => MockPreviewPanel.IsNavigationAllowed(target, root),
            o => ((bool)o.Value!).Should().Be(accepted)
        );

    /// <summary>サブリソースの許可判定の行を作る</summary>
    private static ScenarioRow<PathEntranceCase> Res(
        string id,
        string description,
        PathShape shape,
        Uri? target,
        string? root,
        bool accepted
    ) =>
        Row(
            id,
            description,
            PreviewResource,
            shape,
            accepted ? Accepted : Rejected,
            _ => MockPreviewPanel.IsResourceAllowed(target, root),
            o => ((bool)o.Value!).Should().Be(accepted)
        );

    private static IEnumerable<ScenarioRow<PathEntranceCase>> PreviewRows()
    {
        var baseIndex = RootFile("index.html");
        var baseOrderList = RootFile("OrderList.html");

        // 遷移
        yield return Nav(
            "preview-navigation-accepts-direct-child-html",
            "許可フォルダ直下の *.html は許可",
            DirectChild,
            RootFile("OrderList.html"),
            Root,
            true
        );
        yield return Nav(
            "preview-navigation-accepts-fragment-of-same-file",
            "#fragment 付き同一ファイルは許可",
            Fragment,
            new Uri(baseOrderList.AbsoluteUri + "#section"),
            Root,
            true
        );
        yield return Nav(
            "preview-navigation-rejects-subfolder",
            "サブフォルダ配下は拒否",
            SubFolder,
            new Uri(Path.Combine(Root, "sub", "OrderList.html")),
            Root,
            false
        );
        yield return Nav(
            "preview-navigation-rejects-outside-root",
            "フォルダ外は拒否",
            OutsideRoot,
            new Uri(Path.Combine(ParentOfRoot, "Secret.html")),
            Root,
            false
        );
        yield return Nav(
            "preview-navigation-rejects-parent-traversal",
            ".. でフォルダ外へ抜ける遷移は拒否",
            ParentTraversal,
            new Uri(baseIndex, "../Secret.html"),
            Root,
            false
        );
        yield return Nav(
            "preview-navigation-rejects-https-scheme",
            "http(s) など file 以外は拒否",
            HttpScheme,
            new Uri("https://example.com/OrderList.html"),
            Root,
            false
        );
        yield return Nav(
            "preview-navigation-rejects-http-scheme",
            "http(s) など file 以外は拒否",
            HttpScheme,
            new Uri("http://example.com/x.html"),
            Root,
            false
        );
        yield return Nav(
            "preview-navigation-rejects-non-html",
            "非 HTML ファイルは拒否",
            NonHtml,
            RootFile("data.txt"),
            Root,
            false
        );
        yield return Nav(
            "preview-navigation-rejects-empty-root",
            "許可フォルダが空なら拒否",
            EmptyRoot,
            RootFile("OrderList.html"),
            string.Empty,
            false
        );
        yield return Nav(
            "preview-navigation-rejects-null-root",
            "許可フォルダが空なら拒否",
            NullRoot,
            RootFile("OrderList.html"),
            null,
            false
        );

        // サブリソース
        foreach (
            var (name, shape) in new[]
            {
                ("style.css", NonHtml),
                ("OrderList.html", DirectChild),
                ("logo.png", NonHtml),
                ("app.js", NonHtml),
            }
        )
        {
            yield return Res(
                $"preview-resource-accepts-direct-child-{name.Replace('.', '-').ToLowerInvariant()}",
                "リソース: 許可フォルダ内は拡張子を問わず許可",
                shape,
                RootFile(name),
                Root,
                true
            );
        }

        foreach (
            var (url, shape, label) in new[]
            {
                ("https://example.com/x.png", HttpScheme, "https"),
                ("http://example.com/app.js", HttpScheme, "http"),
                ("ws://example.com/socket", WebSocketScheme, "ws"),
                ("wss://example.com/socket", WebSocketScheme, "wss"),
            }
        )
        {
            yield return Res(
                $"preview-resource-rejects-{label}-scheme",
                "リソース: file 以外のスキームは拒否",
                shape,
                new Uri(url),
                Root,
                false
            );
        }

        yield return Res(
            "preview-resource-rejects-outside-root",
            "リソース: フォルダ外・サブフォルダは拒否",
            OutsideRoot,
            new Uri(Path.Combine(ParentOfRoot, "secret.css")),
            Root,
            false
        );
        yield return Res(
            "preview-resource-rejects-subfolder",
            "リソース: フォルダ外・サブフォルダは拒否",
            SubFolder,
            new Uri(Path.Combine(Root, "sub", "style.css")),
            Root,
            false
        );
        yield return Res(
            "preview-resource-rejects-parent-traversal",
            "リソース: .. でフォルダ外を指すファイルは拒否",
            ParentTraversal,
            new Uri(baseIndex, "../secret.css"),
            Root,
            false
        );
        yield return Res(
            "preview-resource-rejects-empty-root",
            "リソース: 許可フォルダ未設定なら既定拒否",
            EmptyRoot,
            RootFile("style.css"),
            string.Empty,
            false
        );
        yield return Res(
            "preview-resource-rejects-null-root",
            "リソース: 許可フォルダ未設定なら既定拒否",
            NullRoot,
            RootFile("style.css"),
            null,
            false
        );
        yield return Res(
            "preview-resource-rejects-null-target",
            "リソース: 許可フォルダ未設定なら既定拒否",
            NullTarget,
            null,
            Root,
            false
        );
    }
}
