using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using AwesomeAssertions;
using QuickER.AI.UI;
using QuickER.Platform;
using QuickER.Tests.TestSupport;
using QuickER.Tests.TestSupport.Engines;

namespace QuickER.Tests.AI.UI;

/// <summary>Markdown 表示の表の行の入力</summary>
/// <param name="Element">要素（表示の行は null）</param>
/// <param name="Form">入力の形（表示の行は null）</param>
/// <param name="Body">流して表明する本体</param>
internal sealed record ChatMarkdownCase(MarkdownElement? Element, MarkdownForm? Form, Action Body);

/// <summary>Markdown 表示の表の組み立てと、全行に共通の表明</summary>
/// <remarks>
/// <para>全行に共通の表明（<see cref="Render"/> の中で、組み立てた文書ごとに行う。形の細部に依らず効く守り）：</para>
/// <list type="number">
/// <item>組み立てただけでは opener を呼ばない。</item>
/// <item>どの Hyperlink も <c>NavigateUri</c> が null（開く経路は opener の 1 本）。</item>
/// <item>どの Hyperlink のツールチップも、クリックで opener へ渡る URL も、http / https の絶対 URL でホストを持ちユーザー情報が無い
/// （製品の <see cref="UrlPolicy"/> を使わずにテストの側で判定する）。</item>
/// <item>画像・UI の入れ物（<see cref="Image"/>・<see cref="InlineUIContainer"/>・<see cref="BlockUIContainer"/>）を作らない。</item>
/// </list>
/// </remarks>
internal static class ChatMarkdownProbe
{
    /// <summary>開かれた URL を記録する opener</summary>
    public sealed class RecordingOpener : IChatLinkOpener
    {
        /// <summary>開かれた URL の記録</summary>
        public List<Uri> Opened { get; } = [];

        /// <inheritdoc />
        public void Open(Uri uri) => Opened.Add(uri);
    }

    /// <summary>STA で組み立て、共通の表明の後に行の表明を流す（行の表明の後にリンクを押して共通の表明 3 を見る）</summary>
    public static void Render(string markdown, Action<FlowDocument, RecordingOpener> assert) =>
        WpfApplicationTestSupport.RunSta(() =>
        {
            var opener = new RecordingOpener();
            var document = ChatMarkdownRenderer.Render(markdown, opener);
            var nodes = Descendants(document).ToList();

            opener.Opened.Should().BeEmpty("共通の表明 1：組み立てただけでは opener を呼ばない");
            nodes.OfType<Image>().Should().BeEmpty("共通の表明 4：画像を作らない");
            nodes.OfType<InlineUIContainer>().Should().BeEmpty("共通の表明 4");
            nodes.OfType<BlockUIContainer>().Should().BeEmpty("共通の表明 4");

            foreach (var hyperlink in nodes.OfType<Hyperlink>())
            {
                hyperlink
                    .NavigateUri.Should()
                    .BeNull("共通の表明 2：WPF の既定のナビゲーションへ渡さない");
                var tip = hyperlink.ToolTip.Should().BeOfType<string>().Subject;
                IsOpenableHttp(new Uri(tip, UriKind.Absolute))
                    .Should()
                    .BeTrue(
                        $"共通の表明 3：ツールチップ {tip} は http / https でホストを持ちユーザー情報が無い"
                    );
            }

            assert(document, opener);

            foreach (var hyperlink in nodes.OfType<Hyperlink>())
            {
                var before = opener.Opened.Count;
                hyperlink.RaiseEvent(new RoutedEventArgs(Hyperlink.ClickEvent, hyperlink));
                opener
                    .Opened.Should()
                    .HaveCount(before + 1, "共通の表明 3：押すと opener へちょうど 1 回渡る");
                IsOpenableHttp(opener.Opened[^1])
                    .Should()
                    .BeTrue(
                        $"共通の表明 3：opener へ渡る {opener.Opened[^1]} は http / https でホストを持ちユーザー情報が無い"
                    );
            }
        });

    /// <summary>応答済み・Markdown 表示・opener ありの表示部品を STA で作って表明する</summary>
    public static void View(string text, Action<AssistantMessageView> assert) =>
        WpfApplicationTestSupport.RunSta(() =>
        {
            var view = new AssistantMessageView
            {
                Text = text,
                IsStreaming = false,
                ShowMarkdown = true,
                LinkOpener = new RecordingOpener(),
            };
            assert(view);
        });

    /// <summary>文書の表示文字列（改行は \n に揃える）</summary>
    public static string TextOf(FlowDocument document) =>
        new TextRange(document.ContentStart, document.ContentEnd).Text.ReplaceLineEndings("\n");

    /// <summary>要素の表示文字列（改行は \n に揃える）</summary>
    public static string TextOf(TextElement element) =>
        new TextRange(element.ContentStart, element.ContentEnd).Text.ReplaceLineEndings("\n");

    /// <summary>要素に含まれる Run の文字をつなげる（リストの行頭記号のように Run でない表示を含めない）</summary>
    public static string RunText(DependencyObject element) =>
        string.Concat(Descendants(element).OfType<Run>().Select(r => r.Text));

    /// <summary>論理ツリーの子孫をすべて列挙する</summary>
    public static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        {
            yield return child;

            foreach (var nested in Descendants(child))
            {
                yield return nested;
            }
        }
    }

    /// <summary>
    /// 開く URL の表記（テストの側で組む：ホストは国際化ドメイン名を punycode に・既定でないポート・エスケープしたパスとクエリ・フラグメント）
    /// </summary>
    public static string DisplayOf(Uri uri) =>
        $"{uri.Scheme}://{(uri.HostNameType == UriHostNameType.Dns ? uri.IdnHost : uri.Host)}"
        + (uri.IsDefaultPort ? string.Empty : ":" + uri.Port.ToString(CultureInfo.InvariantCulture))
        + uri.PathAndQuery
        + uri.Fragment;

    /// <summary>http / https の絶対 URL でホストを持ちユーザー情報が無いか（テストの側の判定）</summary>
    private static bool IsOpenableHttp(Uri uri) =>
        uri.IsAbsoluteUri
        && (uri.Scheme is "http" or "https")
        && uri.Host.Length > 0
        && uri.UserInfo.Length == 0;
}

/// <summary>AI の回答の Markdown 表示の表の行</summary>
/// <remarks>
/// <para>
/// URL の行は、URL の目録（<see cref="UrlEntry"/>）を要素ごとの書き方で Markdown にした生成の行。開けるリンクになるかの期待は
/// 目録の項目ごとに手で書く（<see cref="UrlEntry.Links"/>）。開いてよいのは http / https の絶対 URL でホストを持ちユーザー情報が無いもの
/// （<c>src/QuickER.AI.UI/CLAUDE.md</c>「回答の Markdown 表示」）。開いてよい URL でも、Markdown の構文としてリンクにならない書き方
/// （制御文字・タブ・URL の中の空白・自動リンクの中の文字参照・拡張の自動リンクの大文字のスキーム）は文字として残る。
/// それは CommonMark と Markdig の解析の細部で、項目の <see cref="UrlEntry.Note"/> に「特性の固定」と書く。
/// </para>
/// <para>断る形の水準は CLAUDE.md の記述より広めに取る（AI の回答は外部の入力）。</para>
/// </remarks>
internal static partial class ChatMarkdownRows
{
    /// <summary>行の表</summary>
    public static ScenarioTable<ChatMarkdownCase> Table =>
        new(
            EngineIds.Ai,
            [.. UrlRows(), .. PolicyRows(), .. HtmlRows(), .. DepthRows(), .. DisplayRows()]
        );

    /// <summary>URL の目録の 1 項目</summary>
    /// <param name="Id">行の Id の一部</param>
    /// <param name="Form">形</param>
    /// <param name="Url">書く URL（Markdown へそのまま埋める）</param>
    /// <param name="Opens">リンクになるときのツールチップ（開く URL の表記）。どの要素でもリンクにならないなら null</param>
    /// <param name="Links">リンクになる要素の頭文字（I＝インライン・R＝参照・A＝山括弧・B＝拡張の自動リンク・L＝リンクの中の画像）</param>
    /// <param name="Policy">URL の規則に直接渡したときに開いてよいか</param>
    /// <param name="Visible">文字として残るとき、山括弧・拡張の自動リンクの表示に含まれる文字（null なら URL の前後の空白を除いたもの）</param>
    /// <param name="Unparsed">インライン・参照・画像・リンクの中の画像の構文として読まれない（Markdown の文字のまま残る）</param>
    /// <param name="AngleOpens">山括弧の書き方でリンクになるときのツールチップ（null なら <paramref name="Opens"/>）</param>
    /// <param name="Note">期待の根拠の注（特性の固定はその旨）</param>
    private sealed record UrlEntry(
        string Id,
        MarkdownForm Form,
        string Url,
        string? Opens,
        string Links,
        bool Policy,
        string? Visible = null,
        bool Unparsed = false,
        string? AngleOpens = null,
        string? Note = null
    );

    /// <summary>URL の目録</summary>
    private static readonly UrlEntry[] Urls =
    [
        // 開いてよい
        new(
            "https",
            MarkdownForm.Allowed,
            "https://example.com/a?b=1",
            "https://example.com/a?b=1",
            "IRABL",
            true
        ),
        new(
            "http",
            MarkdownForm.Allowed,
            "http://example.com/",
            "http://example.com/",
            "IRABL",
            true
        ),
        new(
            "port-fragment",
            MarkdownForm.Allowed,
            "https://example.com:8443/a#f",
            "https://example.com:8443/a#f",
            "IRABL",
            true
        ),
        new(
            "www",
            MarkdownForm.Allowed,
            "www.example.com",
            "http://www.example.com/",
            "B",
            false,
            Note: "スキームの無い www は相対 URL なのでリンクの宛先としては断り、拡張の自動リンクだけが http を補ってリンクにする"
        ),
        // 断るスキーム
        new("file", MarkdownForm.Refused, "file:///C:/Windows/notepad.exe", null, "", false),
        new("file-short", MarkdownForm.Refused, "file:///C:/x", null, "", false),
        new("javascript", MarkdownForm.Refused, "javascript:alert(1)", null, "", false),
        new("data", MarkdownForm.Refused, "data:text/html;base64,PHNjcmlwdD4=", null, "", false),
        new("vbscript", MarkdownForm.Refused, "vbscript:msgbox(1)", null, "", false),
        new("ms-settings", MarkdownForm.Refused, "ms-settings:privacy", null, "", false),
        new("vscode", MarkdownForm.Refused, "vscode://file/c:/x", null, "", false),
        new("ftp", MarkdownForm.Refused, "ftp://example.com/f", null, "", false),
        new("tel", MarkdownForm.Refused, "tel:+81123", null, "", false, Visible: "+81123"),
        new(
            "mailto",
            MarkdownForm.Refused,
            "mailto:a@example.com",
            null,
            "",
            false,
            Visible: "a@example.com"
        ),
        new("email", MarkdownForm.Refused, "a@example.com", null, "", false),
        // 大文字小文字
        new(
            "https-upper",
            MarkdownForm.Case,
            "HTTPS://EXAMPLE.COM/",
            "https://example.com/",
            "IRAL",
            true,
            Note: "特性の固定：拡張の自動リンクは小文字のスキームしか読まない（文字として残る）"
        ),
        new("javascript-mixed", MarkdownForm.Case, "JavaScript:alert(1)", null, "", false),
        new("data-upper", MarkdownForm.Case, "DATA:text/html,x", null, "", false),
        new("file-upper", MarkdownForm.Case, "FILE:///C:/x", null, "", false),
        // 空白・制御文字
        new(
            "surrounding-spaces",
            MarkdownForm.Whitespace,
            " https://example.com/w ",
            "https://example.com/w",
            "IRABL",
            true,
            Note: "宛先の前後の空白は CommonMark が捨てる。特性の固定：山括弧の中に空白があると山括弧の自動リンクにならず、中の URL を拡張の自動リンクがリンクにする"
        ),
        new(
            "control-before-scheme",
            MarkdownForm.Whitespace,
            "\u0001https://example.com/c",
            null,
            "",
            false,
            Unparsed: true,
            Note: "特性の固定：制御文字を含む宛先はリンクの構文として読まれず、文字のまま残る"
        ),
        new(
            "tab-in-scheme",
            MarkdownForm.Whitespace,
            "java\u0009script:alert(1)",
            null,
            "",
            false,
            Unparsed: true,
            Note: "特性の固定：タブを含む宛先はリンクの構文として読まれず、文字のまま残る"
        ),
        new(
            "space-before-javascript",
            MarkdownForm.Whitespace,
            " javascript:alert(1)",
            null,
            "",
            false
        ),
        new(
            "nbsp-before-scheme",
            MarkdownForm.Whitespace,
            "\u00a0https://example.com/n",
            "https://example.com/n",
            "IRABL",
            false,
            AngleOpens: "https://example.com/n%3E",
            Note: "Markdown の構文は宛先の前の NBSP を空白として捨てる（URL の規則へ直接渡すと絶対 URL として読めないので開かない）。特性の固定：山括弧の中は拡張の自動リンクが閉じの > まで URL に含める（http のままなので開いてよい）"
        ),
        new(
            "space-inside-host",
            MarkdownForm.Whitespace,
            "https://exa mple.com/",
            null,
            "",
            false,
            Unparsed: true,
            Note: "特性の固定：空白を含む宛先はリンクの構文として読まれない"
        ),
        // エスケープ
        new(
            "entity-scheme",
            MarkdownForm.Escaped,
            "&#106;avascript:alert(1)",
            null,
            "",
            false,
            Visible: "javascript:alert(1)"
        ),
        new(
            "entity-colon",
            MarkdownForm.Escaped,
            "javascript&#58;alert(1)",
            null,
            "",
            false,
            Visible: "javascript:alert(1)"
        ),
        new("percent-scheme", MarkdownForm.Escaped, "%6Aavascript:alert(1)", null, "", false),
        new(
            "backslash-colon",
            MarkdownForm.Escaped,
            "javascript\\:alert(1)",
            null,
            "",
            false,
            Visible: "javascript:alert(1)"
        ),
        new(
            "entity-https",
            MarkdownForm.Escaped,
            "&#104;ttps://example.com/e",
            "https://example.com/e",
            "IRL",
            false,
            Visible: "https://example.com/e",
            Note: "宛先の文字参照は CommonMark が戻す（開いてよい http になる）。特性の固定：自動リンクは文字参照を戻さないので文字として残る"
        ),
        // 空
        new("empty", MarkdownForm.Empty, "", null, "", false, Visible: ""),
        new("blank", MarkdownForm.Empty, " ", null, "", false, Visible: ""),
        // 相対・ネットワーク
        new("relative-path", MarkdownForm.Network, "/relative/path", null, "", false),
        new("relative-file", MarkdownForm.Network, "page.html", null, "", false),
        new("parent", MarkdownForm.Network, "../up", null, "", false),
        new("protocol-relative", MarkdownForm.Network, "//server/share", null, "", false),
        new(
            "unc-escaped",
            MarkdownForm.Network,
            "\\\\\\\\server\\\\share",
            null,
            "",
            false,
            Visible: "\\\\server\\share",
            Note: "Markdown の逆斜線のエスケープを戻すと UNC の \\\\server\\share になる"
        ),
        new(
            "unc-raw",
            MarkdownForm.Network,
            "\\\\server\\share",
            null,
            "",
            false,
            Visible: "\\server\\share"
        ),
        // ユーザー情報
        new("user-pass", MarkdownForm.UserInfo, "https://user:pass@host/", null, "", false),
        new(
            "host-lookalike",
            MarkdownForm.UserInfo,
            "https://example.com@evil.test/",
            null,
            "",
            false
        ),
        new("short-user", MarkdownForm.UserInfo, "https://a@b/", null, "", false),
        // 国際化ドメイン名
        new(
            "idn-lookalike",
            MarkdownForm.Idn,
            "https://\u0430pple.com/",
            "https://xn--pple-43d.com/",
            "IRABL",
            true,
            Note: "キリル文字の U+0430 を使ったホストは punycode で出す（apple.com と見分けられるように）"
        ),
        new(
            "idn-path-query",
            MarkdownForm.Idn,
            "https://例え.jp/パス?q=値",
            "https://xn--r8jz45g.jp/%E3%83%91%E3%82%B9?q=%E5%80%A4",
            "IRABL",
            true
        ),
    ];

    /// <summary>
    /// 開けるリンクになる行と文字として残る行の両方を持たない要素（無ければ空）。URL の規則の直接の呼び出しは、開いてよいと
    /// 開いてはいけないの両方を持つか。画像はリンクにならないので数えない
    /// </summary>
    public static IReadOnlyList<string> OutcomeCoverageProblems()
    {
        var problems = new List<string>();

        foreach (var letter in "IRABL")
        {
            if (!Urls.Any(entry => entry.Links.Contains(letter)))
            {
                problems.Add($"{letter}：開けるリンクになる行が無い");
            }

            if (!Urls.Any(entry => !entry.Links.Contains(letter)))
            {
                problems.Add($"{letter}：文字として残る行が無い");
            }
        }

        if (!Urls.Any(entry => entry.Policy) || !Urls.Any(entry => !entry.Policy))
        {
            problems.Add("Policy：開いてよい行と開いてはいけない行の両方が無い");
        }

        return problems;
    }

    /// <summary>URL を持つ要素（URL の規則の直接の呼び出しを除く）の行</summary>
    private static IEnumerable<ScenarioRow<ChatMarkdownCase>> UrlRows()
    {
        foreach (var entry in Urls)
        {
            yield return LinkRow(entry, MarkdownElement.InlineLink, 'I', $"[t]({entry.Url})", "t");
            yield return LinkRow(
                entry,
                MarkdownElement.ReferenceLink,
                'R',
                $"[t][r]\n\n[r]: {entry.Url}",
                "t"
            );
            yield return LinkRow(
                entry,
                MarkdownElement.AngleAutolink,
                'A',
                $"<{entry.Url}>",
                entry.Visible ?? entry.Url.Trim()
            );
            yield return LinkRow(
                entry,
                MarkdownElement.BareAutolink,
                'B',
                $"see {entry.Url} here",
                entry.Visible ?? entry.Url.Trim()
            );
            yield return LinkRow(
                entry,
                MarkdownElement.LinkedImage,
                'L',
                $"[![alt](https://example.com/i.png)]({entry.Url})",
                "alt"
            );
            yield return ImageRow(entry);
        }
    }

    /// <summary>リンクの要素の 1 行</summary>
    private static ScenarioRow<ChatMarkdownCase> LinkRow(
        UrlEntry entry,
        MarkdownElement element,
        char letter,
        string markdown,
        string visible
    )
    {
        var opens = entry.Links.Contains(letter)
            ? (letter == 'A' ? entry.AngleOpens ?? entry.Opens : entry.Opens)
            : null;
        var description =
            (
                opens is null
                    ? $"{element} の {entry.Form}（{entry.Id}）は押せない文字として残る"
                    : $"{element} の {entry.Form}（{entry.Id}）は押せるリンクになりツールチップに {opens} を出す"
            ) + (entry.Note is null ? string.Empty : "。" + entry.Note);

        return Row(
            $"{Kebab(element)}-{entry.Id}",
            description,
            element,
            entry.Form,
            () =>
                ChatMarkdownProbe.Render(
                    markdown,
                    (document, opener) =>
                    {
                        var hyperlinks = ChatMarkdownProbe
                            .Descendants(document)
                            .OfType<Hyperlink>()
                            .ToList();

                        if (opens is null)
                        {
                            hyperlinks.Should().BeEmpty();

                            if (visible.Length > 0)
                            {
                                ChatMarkdownProbe.TextOf(document).Should().Contain(visible);
                            }
                        }
                        else
                        {
                            var hyperlink = hyperlinks.Should().ContainSingle().Subject;
                            hyperlink.ToolTip.Should().Be(opens);

                            // 押すと、ツールチップに出した URL そのものが opener へ渡る（表記はテストの側で組む）
                            hyperlink.RaiseEvent(
                                new RoutedEventArgs(Hyperlink.ClickEvent, hyperlink)
                            );
                            ChatMarkdownProbe
                                .DisplayOf(opener.Opened.Should().ContainSingle().Subject)
                                .Should()
                                .Be(opens);
                        }
                    }
                )
        );
    }

    /// <summary>画像の 1 行（どの URL でも読み込まず、代替テキストだけを出し、リンクにもしない）</summary>
    private static ScenarioRow<ChatMarkdownCase> ImageRow(UrlEntry entry) =>
        Row(
            $"image-{entry.Id}",
            $"画像の {entry.Form}（{entry.Id}）は読み込まずリンクにもしない"
                + (
                    entry.Unparsed
                        ? "（構文として読まれず文字のまま残る）"
                        : "。代替テキストだけを出す"
                ),
            MarkdownElement.Image,
            entry.Form,
            () =>
                ChatMarkdownProbe.Render(
                    $"before ![alt]({entry.Url}) after",
                    (document, _) =>
                    {
                        ChatMarkdownProbe
                            .Descendants(document)
                            .OfType<Hyperlink>()
                            .Should()
                            .BeEmpty("画像はリンクにもしない");

                        if (!entry.Unparsed)
                        {
                            var text = ChatMarkdownProbe.TextOf(document);
                            text.Should().Contain("before alt after");

                            if (entry.Url.Trim().Length > 0)
                            {
                                text.Should().NotContain(entry.Url.Trim());
                            }
                        }
                    }
                )
        );

    /// <summary>URL の規則の直接の呼び出しの行（Markdown で書けない null を含む）</summary>
    private static IEnumerable<ScenarioRow<ChatMarkdownCase>> PolicyRows()
    {
        foreach (var entry in Urls)
        {
            yield return Row(
                $"policy-{entry.Id}",
                $"URL の規則は {entry.Form}（{entry.Id}）を"
                    + (entry.Policy ? "開いてよいとする" : "開いてはいけないとする"),
                MarkdownElement.Policy,
                entry.Form,
                () =>
                {
                    UrlPolicy.TryGetSafeUri(entry.Url, out var uri).Should().Be(entry.Policy);
                    (uri is not null).Should().Be(entry.Policy);
                }
            );
        }

        yield return Row(
            "policy-null",
            "URL の規則は null を開いてはいけないとする",
            MarkdownElement.Policy,
            MarkdownForm.Empty,
            () =>
            {
                UrlPolicy.TryGetSafeUri(null, out var uri).Should().BeFalse();
                uri.Should().BeNull();
            }
        );
    }

    private static string Kebab(MarkdownElement element) =>
        element switch
        {
            MarkdownElement.InlineLink => "inline",
            MarkdownElement.ReferenceLink => "reference",
            MarkdownElement.AngleAutolink => "angle",
            MarkdownElement.BareAutolink => "bare",
            MarkdownElement.LinkedImage => "linked-image",
            _ => element.ToString().ToLowerInvariant(),
        };

    private static ScenarioRow<ChatMarkdownCase> Row(
        string id,
        string description,
        MarkdownElement? element,
        MarkdownForm? form,
        Action body
    ) => new(id, null, description, new ChatMarkdownCase(element, form, body));
}
