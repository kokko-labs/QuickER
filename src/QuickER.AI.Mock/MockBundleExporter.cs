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
/// 既知の制限: 画面 body 内に対応の取れていない生の <c>&lt;/section&gt;</c> があると、その画面用に開いた
/// ラッパー <c>&lt;section data-screen&gt;</c> がそこで閉じてしまい、後続の内容が現在の表示画面の外（全画面共通）へ
/// 漏れて表示される（結合後の見た目が乱れるだけで、script の実行範囲は変わらない＝結合後の script は元から
/// 常に文書全体で実行される）。
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

            var screenId = MockScreenLinks.ScreenId(screen.File);
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

    /// <summary>相対 <c>*.html</c> リンクを <c>#画面ID</c> へ書き換える（外部 URL は素通し）</summary>
    /// <remarks>
    /// リンク先のページ内フラグメント（<c>Foo.html#items</c> の <c>#items</c>）は捨てる。単一 HTML では
    /// <c>location.hash</c> が表示画面の指定そのものなので、<c>#Foo#items</c> のように残すと画面 ID と
    /// 一致せず遷移できない（遷移先の画面は表示されるが、その中のアンカー位置へはスクロールしない）。
    /// </remarks>
    private static string RewriteLinks(string content)
    {
        return MockScreenLinks.RelativeScreenLink.Replace(
            content,
            match =>
            {
                // 絶対 URL（外部）は書き換えない
                if (!MockScreenLinks.TryGetScreenFile(match.Groups[2].Value, out var file))
                {
                    return match.Value;
                }

                // リンク先は元の属性値のテキスト（HTML の文字参照を含み得る）なので、エスケープし直さない
                var quote = match.Groups[1].Value;

                return $"href={quote}#{MockScreenLinks.ScreenId(file)}{quote}";
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
    /// <remarks>
    /// <para>
    /// 照合は hash と <c>data-screen</c> の両方を <c>decodeURIComponent</c>（失敗したら元の値）と小文字化で
    /// そろえてから行う。ブラウザは空白・非 ASCII を含む hash を % エンコードして返し、画面の同一性は
    /// 他所（保存・検証・ファイルシステム）では大文字小文字を区別しないため。
    /// </para>
    /// <para>
    /// どの画面にも一致しない hash（ページ内アンカー <c>#items</c> 等）では表示中の画面を据え置き、
    /// ブラウザのアンカー移動に任せる。先頭画面へ戻すのは、まだ何も表示していない初回だけ。
    /// </para>
    /// </remarks>
    private static string BuildRouterScript(string? firstScreenId)
    {
        // 先頭画面 ID を JS の文字列リテラルへ埋め込む（無ければ空文字）
        var fallback = firstScreenId is null ? "\"\"" : "\"" + EscapeJsString(firstScreenId) + "\"";

        return "<script>\n"
            + "(function(){\n"
            + "  var fallback = "
            + fallback
            + ";\n"
            + "  var current = null;\n"
            + "  function key(value){\n"
            + "    try { value = decodeURIComponent(value); } catch (e) {}\n"
            + "    return value.toLowerCase();\n"
            + "  }\n"
            + "  function show(){\n"
            + "    var id = key(location.hash.replace(/^#/, ''));\n"
            + "    var sections = document.querySelectorAll('section[data-screen]');\n"
            + "    var target = null;\n"
            + "    sections.forEach(function(s){\n"
            + "      if (target === null && key(s.getAttribute('data-screen')) === id) { target = s; }\n"
            + "    });\n"
            + "    if (target === null) {\n"
            + "      if (current !== null) { return; }\n"
            + "      sections.forEach(function(s){\n"
            + "        if (target === null && s.getAttribute('data-screen') === fallback) { target = s; }\n"
            + "      });\n"
            + "    }\n"
            + "    sections.forEach(function(s){\n"
            + "      s.style.display = s === target ? '' : 'none';\n"
            + "    });\n"
            + "    current = target;\n"
            + "  }\n"
            + "  window.addEventListener('hashchange', show);\n"
            + "  show();\n"
            + "})();\n"
            + "</script>";
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
