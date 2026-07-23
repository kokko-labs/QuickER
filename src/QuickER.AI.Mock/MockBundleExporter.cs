using System.Text;
using System.Text.RegularExpressions;

namespace QuickER.AI.Mock;

/// <summary>
/// モックフォルダの内容を、AI を使わず決定的な文字列処理だけで単一 HTML（SPA 風・ハッシュルーター）へ結合する。
/// </summary>
/// <remarks>
/// 各画面の <c>&lt;body&gt;</c> 内容を <c>&lt;section data-screen="…"&gt;</c> として連結し、共有 CSS を
/// <c>&lt;style&gt;</c> へインライン化する。相対リンク <c>href="Foo.html"</c> は <c>href="#Foo"</c> へ書き換え、
/// <c>location.hash</c> で表示画面を切り替える小さな JS を埋め込む。完全な HTML パースは行わない（best effort）。
/// </remarks>
public static class MockBundleExporter
{
    private static readonly Regex BodyRegex = new(
        "<body[^>]*>([\\s\\S]*?)</body>",
        RegexOptions.IgnoreCase | RegexOptions.Compiled
    );

    private static readonly Regex HeadRegex = new(
        "<head[^>]*>([\\s\\S]*?)</head>",
        RegexOptions.IgnoreCase | RegexOptions.Compiled
    );

    private static readonly Regex StyleRegex = new(
        "<style[^>]*>[\\s\\S]*?</style>",
        RegexOptions.IgnoreCase | RegexOptions.Compiled
    );

    private static readonly Regex ScriptRegex = new(
        "<script[\\s\\S]*?</script>",
        RegexOptions.IgnoreCase | RegexOptions.Compiled
    );

    /// <summary>href="*.html"（任意の #フラグメント付き）を捕捉するリンク書き換え用の正規表現</summary>
    private static readonly Regex HtmlLinkRegex = new(
        "href\\s*=\\s*([\"'])([^\"']+?\\.html)(#[^\"']*)?\\1",
        RegexOptions.IgnoreCase | RegexOptions.Compiled
    );

    /// <summary>モックフォルダ内容を単一 HTML へ結合する</summary>
    /// <param name="store">対象のモックフォルダストア</param>
    /// <returns>外部参照を持たない自己完結の単一 HTML</returns>
    public static string Export(MockFolderStore store)
    {
        var manifest = store.Manifest;
        var css = store.GetStylesheet() ?? string.Empty;

        var sections = new StringBuilder();
        string? firstScreenId = null;

        foreach (var screen in manifest.Screens)
        {
            var html = store.GetScreenHtml(screen.File);

            if (html is null)
            {
                // 実体を欠く宣言画面はスキップする
                continue;
            }

            var screenId = ScreenId(screen.File);
            firstScreenId ??= screenId;

            sections.Append(BuildSection(screenId, html));
        }

        return BuildDocument(manifest.Title, css, sections.ToString(), firstScreenId);
    }

    /// <summary>1 画面分の &lt;section&gt; を組み立てる（head 内 style ＋ body ＋ 末尾へ移設した script）</summary>
    private static string BuildSection(string screenId, string html)
    {
        // head 内の <style> を取り込む
        var headStyles = new StringBuilder();
        var headMatch = HeadRegex.Match(html);

        if (headMatch.Success)
        {
            foreach (Match style in StyleRegex.Matches(headMatch.Groups[1].Value))
            {
                headStyles.Append(style.Value);
            }
        }

        // body 内容（無ければ全体を素材にする）
        var bodyMatch = BodyRegex.Match(html);
        var body = bodyMatch.Success ? bodyMatch.Groups[1].Value : html;

        // body 内の <script> を収集し、本文からは除去して末尾へ移設する
        var scripts = new StringBuilder();
        var bodyWithoutScripts = ScriptRegex.Replace(
            body,
            match =>
            {
                scripts.Append(match.Value);
                return string.Empty;
            }
        );

        // 相対リンク href="Foo.html" を href="#Foo" へ書き換える
        var rewritten = RewriteLinks(bodyWithoutScripts);

        var section = new StringBuilder();
        section.Append("<section data-screen=\"").Append(EscapeAttribute(screenId)).Append("\">");
        section.Append(headStyles);
        section.Append(rewritten);
        section.Append(scripts);
        section.Append("</section>");

        return section.ToString();
    }

    /// <summary>相対 <c>*.html</c> リンクを <c>#ハッシュ</c>へ書き換える（外部 URL は素通し）</summary>
    private static string RewriteLinks(string content)
    {
        return HtmlLinkRegex.Replace(
            content,
            match =>
            {
                var quote = match.Groups[1].Value;
                var path = match.Groups[2].Value;

                // 絶対 URL（外部）は書き換えない
                if (path.Contains("://", StringComparison.Ordinal))
                {
                    return match.Value;
                }

                var id = ScreenId(path);
                var fragment = match.Groups[3].Value; // 末尾の #… があればそのまま温存

                return $"href={quote}#{id}{fragment}{quote}";
            }
        );
    }

    /// <summary>最終 HTML 文書を組み立てる（CSS インライン・全セクション・ハッシュルーター JS）</summary>
    private static string BuildDocument(
        string title,
        string css,
        string sections,
        string? firstScreenId
    )
    {
        var builder = new StringBuilder();

        builder.Append("<!DOCTYPE html>\n");
        builder.Append("<html lang=\"ja\">\n<head>\n");
        builder.Append("<meta charset=\"utf-8\">\n");
        builder.Append(
            "<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">\n"
        );
        builder.Append("<title>").Append(EscapeText(title)).Append("</title>\n");
        builder.Append("<style>\n").Append(css).Append("\n</style>\n");
        builder.Append("</head>\n<body>\n");
        builder.Append(sections);
        builder.Append('\n');
        builder.Append(BuildRouterScript(firstScreenId));
        builder.Append("\n</body>\n</html>\n");

        return builder.ToString();
    }

    /// <summary>ハッシュルーターの JS を組み立てる（hash なしはマニフェスト先頭画面を表示）</summary>
    private static string BuildRouterScript(string? firstScreenId)
    {
        // 先頭画面 ID を JS の文字列リテラルへ埋め込む（無ければ空文字）
        var fallback = firstScreenId is null ? "\"\"" : "\"" + EscapeJsString(firstScreenId) + "\"";

        return "<script>\n"
            + "(function(){\n"
            + "  var fallback = "
            + fallback
            + ";\n"
            + "  function show(){\n"
            + "    var id = location.hash.replace(/^#/, '');\n"
            + "    var sections = document.querySelectorAll('section[data-screen]');\n"
            + "    var matched = false;\n"
            + "    sections.forEach(function(s){\n"
            + "      var isMatch = s.getAttribute('data-screen') === id;\n"
            + "      s.style.display = isMatch ? '' : 'none';\n"
            + "      if (isMatch) { matched = true; }\n"
            + "    });\n"
            + "    if (!matched) {\n"
            + "      sections.forEach(function(s){\n"
            + "        s.style.display = s.getAttribute('data-screen') === fallback ? '' : 'none';\n"
            + "      });\n"
            + "    }\n"
            + "  }\n"
            + "  window.addEventListener('hashchange', show);\n"
            + "  show();\n"
            + "})();\n"
            + "</script>";
    }

    /// <summary>画面ファイル名から拡張子を除いたセクション ID を得る（例 <c>OrderList.html</c> → <c>OrderList</c>）</summary>
    private static string ScreenId(string file)
    {
        var normalized = file.Replace('\\', '/');
        var slash = normalized.LastIndexOf('/');

        if (slash >= 0)
        {
            normalized = normalized.Substring(slash + 1);
        }

        var dot = normalized.LastIndexOf('.');

        return dot > 0 ? normalized.Substring(0, dot) : normalized;
    }

    /// <summary>属性値向けの最小限のエスケープ</summary>
    private static string EscapeAttribute(string value) =>
        value.Replace("&", "&amp;").Replace("\"", "&quot;").Replace("<", "&lt;");

    /// <summary>テキストノード向けの最小限のエスケープ</summary>
    private static string EscapeText(string value) =>
        value.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");

    /// <summary>JS 文字列リテラル向けのエスケープ</summary>
    private static string EscapeJsString(string value) =>
        value.Replace("\\", "\\\\").Replace("\"", "\\\"");
}
