using System.Text.RegularExpressions;

namespace QuickER.AI.Mock;

/// <summary>
/// 画面 HTML・共有 CSS に対する軽量な機械検証。オフライン完結・共有 CSS・リンク整合の規約逸脱を検知する。
/// </summary>
/// <remarks>
/// 返す診断は<b>すべて警告（英語文字列）で保存は拒否しない</b>。正規表現ベースの単純検出で、
/// HTML パーサは導入しない（過剰検出は許容）。空/非 HTML の拒否は呼び出し側（<see cref="MockFolderStore"/>）の責務。
/// </remarks>
public static class MockContentValidator
{
    /// <summary>src= / href= 属性内の絶対 URL（http/https）</summary>
    private static readonly Regex ExternalAttributeUrl = new(
        "(?:src|href)\\s*=\\s*[\"']?\\s*(https?://[^\"'\\s>]+)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled
    );

    /// <summary>CSS の url(...) 内の絶対 URL（@import url(...) も含む）</summary>
    private static readonly Regex ExternalCssUrl = new(
        "url\\(\\s*[\"']?\\s*(https?://[^\"')\\s]+)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled
    );

    /// <summary>@import "..."（url() を伴わない形）内の絶対 URL</summary>
    private static readonly Regex ExternalImportUrl = new(
        "@import\\s+[\"'](https?://[^\"']+)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled
    );

    /// <summary>href="*.html"（相対リンク・任意の #フラグメント付き）</summary>
    private static readonly Regex HtmlLink = new(
        "href\\s*=\\s*[\"']([^\"']+?\\.html)(?:#[^\"']*)?[\"']",
        RegexOptions.IgnoreCase | RegexOptions.Compiled
    );

    /// <summary>style.css への link 参照</summary>
    private static readonly Regex StylesheetLink = new(
        "href\\s*=\\s*[\"'][^\"']*style\\.css[\"']",
        RegexOptions.IgnoreCase | RegexOptions.Compiled
    );

    /// <summary>
    /// 画面 HTML を検証し警告一覧を返す。外部参照・未解決リンク・遷移宣言のリンク欠落・共有 CSS 未参照を検知する。
    /// </summary>
    /// <param name="file">検証対象の画面ファイル名</param>
    /// <param name="html">画面 HTML 全体</param>
    /// <param name="transitions">この画面を起点として宣言された遷移（To は「予告済み」リンクとして扱う）</param>
    /// <param name="knownScreenFiles">既知の画面ファイル名集合（保存後のフォルダ内 HTML ＋マニフェスト宣言）</param>
    /// <returns>英語の警告文字列一覧（問題なしなら空）</returns>
    public static IReadOnlyList<string> ValidateScreen(
        string file,
        string html,
        IReadOnlyList<MockTransition> transitions,
        IReadOnlyCollection<string> knownScreenFiles
    )
    {
        var warnings = new List<string>();

        // 外部参照（オフライン完結の規約違反）
        warnings.AddRange(DetectExternalReferences(html));

        // 予告済みリンク先の集合（既知画面 ∪ 遷移 To ∪ 自分自身）を大文字小文字無視で組む
        var known = new HashSet<string>(knownScreenFiles, StringComparer.OrdinalIgnoreCase)
        {
            file,
        };

        foreach (var transition in transitions)
        {
            if (!string.IsNullOrWhiteSpace(transition.To))
            {
                known.Add(transition.To);
            }
        }

        // HTML 内リンク先の実在チェック（既知にも予告にも無ければ前方参照の取りこぼしとして警告）
        var linkedTargets = ExtractHtmlLinkTargets(html);

        foreach (var target in linkedTargets)
        {
            if (!known.Contains(target))
            {
                warnings.Add(
                    $"Link target '{target}' does not exist as a screen and is not declared in the manifest or transitions."
                );
            }
        }

        // 遷移宣言の To が、既存画面にも HTML 内リンクにも無ければ警告（宣言と実装の乖離）
        var linkedSet = new HashSet<string>(linkedTargets, StringComparer.OrdinalIgnoreCase);
        var existingScreens = new HashSet<string>(
            knownScreenFiles,
            StringComparer.OrdinalIgnoreCase
        );

        foreach (var transition in transitions)
        {
            if (string.IsNullOrWhiteSpace(transition.To))
            {
                continue;
            }

            if (!existingScreens.Contains(transition.To) && !linkedSet.Contains(transition.To))
            {
                warnings.Add(
                    $"Transition target '{transition.To}' is neither an existing screen nor linked from the HTML."
                );
            }
        }

        // 共有 CSS の参照が無ければ規約逸脱として警告
        if (!StylesheetLink.IsMatch(html))
        {
            warnings.Add(
                $"The screen does not reference the shared stylesheet ('{MockManifest.StylesheetFileName}')."
            );
        }

        return warnings;
    }

    /// <summary>共有 CSS を検証し警告一覧を返す（外部参照の検知のみ）</summary>
    /// <param name="css">CSS 全体</param>
    /// <returns>英語の警告文字列一覧（問題なしなら空）</returns>
    public static IReadOnlyList<string> ValidateStylesheet(string css)
    {
        return DetectExternalReferences(css);
    }

    /// <summary>src=/href=/url(...)/@import 内の絶対 URL を検出し、見つかったものを警告文字列にする</summary>
    private static IReadOnlyList<string> DetectExternalReferences(string content)
    {
        var found = new List<string>();

        CollectMatches(ExternalAttributeUrl, content, found);
        CollectMatches(ExternalCssUrl, content, found);
        CollectMatches(ExternalImportUrl, content, found);

        if (found.Count == 0)
        {
            return Array.Empty<string>();
        }

        // 重複を畳んで 1 件ずつの警告にする（オフライン完結規約の逸脱）
        var distinct = found.Distinct(StringComparer.OrdinalIgnoreCase);
        var warnings = new List<string>();

        foreach (var url in distinct)
        {
            warnings.Add($"External reference detected (offline-only mock): {url}");
        }

        return warnings;
    }

    /// <summary>正規表現のキャプチャ 1 群目を収集する</summary>
    private static void CollectMatches(Regex regex, string content, List<string> sink)
    {
        foreach (Match match in regex.Matches(content))
        {
            sink.Add(match.Groups[1].Value);
        }
    }

    /// <summary>HTML 内の相対 <c>*.html</c> リンク先ファイル名を抽出する（外部 URL は除外）</summary>
    private static IReadOnlyList<string> ExtractHtmlLinkTargets(string html)
    {
        var targets = new List<string>();

        foreach (Match match in HtmlLink.Matches(html))
        {
            var raw = match.Groups[1].Value;

            // 絶対 URL（外部）は対象外
            if (raw.Contains("://", StringComparison.Ordinal))
            {
                continue;
            }

            // 先頭の "./" を除去し、ファイル名部分のみを取り出す（フォルダ直下想定）
            var normalized = raw.Replace('\\', '/');

            if (normalized.StartsWith("./", StringComparison.Ordinal))
            {
                normalized = normalized.Substring(2);
            }

            var slash = normalized.LastIndexOf('/');
            var fileName = slash >= 0 ? normalized.Substring(slash + 1) : normalized;

            if (!string.IsNullOrWhiteSpace(fileName))
            {
                targets.Add(fileName);
            }
        }

        return targets;
    }
}
