using AwesomeAssertions;
using QuickER.AI.Mock;
using QuickER.Tests.TestSupport.Engines;
using static QuickER.Tests.AI.Mock.Outputs.MockOutputsContext;

namespace QuickER.Tests.AI.Mock.Outputs;

/// <summary>リンク先の宣言の状態（検証器へ渡す既知の画面・遷移・他の画面の宣言）</summary>
internal enum LinkTarget
{
    /// <summary>既知の画面（フォルダの HTML かマニフェストの宣言）</summary>
    Known,

    /// <summary>この画面の遷移で予告した</summary>
    Transition,

    /// <summary>他の画面の遷移が宣言した</summary>
    OtherScreen,

    /// <summary>どこにも無い</summary>
    Unknown,
}

/// <summary>
/// モックフォルダの出力の表：リンクの表記と画面 ID は同じ入力を ScreenLinks・検証器・単一 HTML（・設計書）へ流し、
/// ほかは対象ごとの入力→出力の行
/// </summary>
/// <remarks>
/// リンクの表記の行は「検証が解決済みとしたリンク（既知の画面のときに警告しない）は、単一 HTML で #画面ID へ書き換わる」を全行で確かめる
/// （元のテスト Export_RewritesEveryLinkTheValidatorResolves の性質を表の性質にした）。
/// </remarks>
internal static partial class MockOutputsRows
{
    /// <summary>行の表</summary>
    public static ScenarioTable<MockOutputScenario> Table =>
        new(
            EngineIds.Ai,
            [
                .. Links(),
                .. ScreenIds(),
                .. Validator(),
                .. Bundle(),
                .. Load(),
                .. Doc(),
                .. Resume(),
                .. Mismatch(),
            ]
        );

    /// <summary>リンクの表記の 1 行</summary>
    /// <param name="Notation">表記の観点</param>
    /// <param name="Open">開きの引用符</param>
    /// <param name="Href">属性値（フラグメントを含む）</param>
    /// <param name="Close">閉じの引用符</param>
    /// <param name="Matches">相対画面リンクの正規表現に一致するか</param>
    /// <param name="File">属性値（フラグメントを除く）から取り出せる画面のファイル名（取り出せなければ null。単一 HTML・検証器が画面リンクとして扱うのは Matches のときだけ）</param>
    /// <param name="Id">画面 ID（画面リンクでなければ null）</param>
    /// <param name="Target">検証器へ渡すリンク先の宣言の状態</param>
    /// <param name="External">外部参照の警告が出るか</param>
    private sealed record LinkCase(
        string Notation,
        string Open,
        string Href,
        string Close,
        bool Matches,
        string? File,
        string? Id,
        LinkTarget Target,
        bool External = false
    );

    private static readonly LinkCase[] LinkCases =
    [
        new("link-double-quote", "\"", "B.html", "\"", true, "B.html", "B", LinkTarget.Known),
        new("link-single-quote", "'", "B.html", "'", true, "B.html", "B", LinkTarget.Transition),
        new("link-dot-slash", "\"", "./B.html", "\"", true, "B.html", "B", LinkTarget.OtherScreen),
        new("link-folder", "\"", "sub/B.html", "\"", true, "B.html", "B", LinkTarget.Unknown),
        new("link-backslash", "\"", "sub\\B.html", "\"", true, "B.html", "B", LinkTarget.Known),
        new("link-parent-folder", "\"", "../B.html", "\"", true, "B.html", "B", LinkTarget.Unknown),
        new(
            "link-upper-case-extension",
            "\"",
            "b.HTML",
            "\"",
            true,
            "b.HTML",
            "b",
            LinkTarget.Known
        ),
        new("link-fragment", "\"", "B.html#x", "\"", true, "B.html", "B", LinkTarget.Known),
        new("link-fragment", "'", "B.html#top", "'", true, "B.html", "B", LinkTarget.Transition),
        new(
            "link-mismatched-quotes",
            "\"",
            "C.html",
            "'",
            false,
            "C.html",
            "C",
            LinkTarget.Unknown
        ),
        new(
            "link-mismatched-quotes",
            "'",
            "B.html",
            "\"",
            false,
            "B.html",
            "B",
            LinkTarget.Unknown
        ),
        new(
            "link-absolute-url",
            "\"",
            "https://example.com/D.html",
            "\"",
            true,
            null,
            null,
            LinkTarget.Unknown,
            External: true
        ),
        new(
            "link-file-url",
            "\"",
            "file://server/share/B.html",
            "\"",
            true,
            null,
            null,
            LinkTarget.Unknown
        ),
        new("link-empty-file-name", "\"", "sub/", "\"", false, null, null, LinkTarget.Unknown),
        new(
            "link-dotted-name",
            "\"",
            "a.b.html",
            "\"",
            true,
            "a.b.html",
            "a.b",
            LinkTarget.Unknown
        ),
    ];

    /// <summary>リンクの表記 × 宣言の状態を ScreenLinks・検証器・単一 HTML へ流す</summary>
    private static IEnumerable<ScenarioRow<MockOutputScenario>> Links() =>
        LinkCases.Select(c =>
        {
            var anchor = $"<a href={c.Open}{c.Href}{c.Close}>x</a>";
            var target = "target-" + KebabTarget(c.Target);

            return Row(
                $"{c.Notation}-{QuoteName(c.Open)}-{KebabTarget(c.Target)}",
                $"リンク {anchor} を、ScreenLinks（一致 {c.Matches}・画面 {c.File ?? "なし"}）・検証器（リンク先は {c.Target}）・単一 HTML（画面リンクなら #画面ID へ書き換え、ほかは素通し）へ流す",
                [
                    (MockOutputSut.ScreenLinks, c.Notation),
                    (MockOutputSut.Validator, c.Notation),
                    (MockOutputSut.Validator, target),
                    (MockOutputSut.Bundle, c.Notation),
                ],
                context => RunLink(context, c, anchor)
            );
        });

    private static string KebabTarget(LinkTarget target) =>
        target switch
        {
            LinkTarget.Known => "known",
            LinkTarget.Transition => "transition",
            LinkTarget.OtherScreen => "other-screen",
            _ => "unknown",
        };

    private static string QuoteName(string open) => open == "'" ? "single" : "double";

    private static void RunLink(MockOutputsContext context, LinkCase c, string anchor)
    {
        // ScreenLinks：正規表現の一致・画面のファイル名・画面 ID
        MockScreenLinks.RelativeScreenLink.IsMatch(anchor).Should().Be(c.Matches);
        var path = c.Href.Split('#')[0];
        MockScreenLinks.TryGetScreenFile(path, out var file).Should().Be(c.File is not null);

        if (c.File is not null)
        {
            file.Should().Be(c.File);
            MockScreenLinks.ScreenId(file).Should().Be(c.Id);
        }

        // 検証器：警告はリンク先がどこにも無い画面リンクと外部参照だけ（共有 CSS は参照している）
        var html = Screen(anchor);
        var resolvedFile = c.Matches ? c.File : null;
        var linkWarned = resolvedFile is not null && c.Target == LinkTarget.Unknown;
        var target = resolvedFile ?? "B.html";
        var warnings = MockContentValidator.ValidateScreen(
            "A.html",
            html,
            c.Target == LinkTarget.Transition
                ? [new MockTransition { From = "A.html", To = target }]
                : Array.Empty<MockTransition>(),
            c.Target == LinkTarget.Known ? [target] : Array.Empty<string>(),
            c.Target == LinkTarget.OtherScreen ? [target] : Array.Empty<string>()
        );

        warnings.Should().HaveCount((linkWarned ? 1 : 0) + (c.External ? 1 : 0));
        warnings
            .Any(w => w.Contains($"Link target '{resolvedFile}'", StringComparison.Ordinal))
            .Should()
            .Be(linkWarned);
        warnings
            .Any(w =>
                w.Contains("External reference", StringComparison.Ordinal) && w.Contains(c.Href)
            )
            .Should()
            .Be(c.External);

        // 既知の画面として渡せば、画面リンクはどれも解決済み（警告しない）
        MockContentValidator
            .ValidateScreen("A.html", html, Array.Empty<MockTransition>(), [target])
            .Should()
            .NotContain(w => w.Contains("Link target", StringComparison.Ordinal));

        // 単一 HTML：解決できる画面リンクは #画面ID へ書き換わり、元の表記もフラグメントも残らない。ほかは素通し
        var store = context.CreateStore();
        store.SaveStylesheet("body{}", "css");
        store.SaveScreen("A.html", "a", "", html, Array.Empty<MockTransition>(), "v1");
        store.SaveScreen(
            "B.html",
            "b",
            "",
            Screen("<p>b</p>"),
            Array.Empty<MockTransition>(),
            "v2"
        );
        var section = SectionOf(ExportBundle(store), "A");

        if (resolvedFile is not null)
        {
            section.Should().Contain($"href={c.Open}#{c.Id}{c.Close}");
            section.Should().NotContain($"href={c.Open}{c.Href}{c.Close}");
            section.Should().NotContain(c.Href);
        }
        else
        {
            section.Should().Contain(anchor);
        }
    }

    /// <summary>画面 ID の元の名前の 1 行（設計書のノード ID は保存できる名前だけ）</summary>
    private sealed record IdCase(string Shape, string Slug, string File, string Id, string? NodeId);

    private static readonly IdCase[] IdCases =
    [
        new("id-plain", "order-list", "OrderList.html", "OrderList", "s_OrderList"),
        new("id-folder", "sub-order-list", "sub/OrderList.html", "OrderList", null),
        new("id-dotted", "a-b", "a.b.html", "a.b", "s_a_b"),
        new("id-extension-only", "html", ".html", ".html", "s__html"),
        new("id-reserved-word", "end", "end.html", "end", "s_end"),
        new("id-reserved-word", "subgraph", "subgraph.html", "subgraph", "s_subgraph"),
        new("id-leading-digit", "1st", "1st.html", "1st", "s_1st"),
    ];

    /// <summary>画面 ID を ScreenLinks・設計書（mermaid のノード ID）・単一 HTML（section）へ流す</summary>
    private static IEnumerable<ScenarioRow<MockOutputScenario>> ScreenIds() =>
        IdCases.Select(c =>
            Row(
                $"{c.Shape}-{c.Slug}",
                $"画面のファイル名 {c.File} の画面 ID は {c.Id}（拡張子を除いた最後のセグメント）。保存できる名前なら設計書のノード ID は常に s_ を前置した {c.NodeId}（予約語・数字始まりでも図が壊れない）で、単一 HTML の section も同じ画面 ID",
                c.NodeId is null
                    ? [(MockOutputSut.ScreenLinks, c.Shape)]
                    :
                    [
                        (MockOutputSut.ScreenLinks, c.Shape),
                        (MockOutputSut.DesignDoc, c.Shape),
                        (MockOutputSut.Bundle, c.Shape),
                    ],
                context =>
                {
                    MockScreenLinks.ScreenId(c.File).Should().Be(c.Id);
                    (MockFolderStore.CheckScreenFileName(c.File) is null)
                        .Should()
                        .Be(c.NodeId is not null, "設計書・単一 HTML へ流すのは保存できる名前だけ");

                    if (c.NodeId is null)
                    {
                        return;
                    }

                    var store = context.CreateStore();
                    store.SaveStylesheet("body{}", "css");
                    store.SaveScreen(
                        c.File,
                        "画面",
                        "",
                        Screen("<h1>x</h1>"),
                        Array.Empty<MockTransition>(),
                        "v1"
                    );
                    store.SaveScreen(
                        "Home.html",
                        "ホーム",
                        "",
                        Screen("<h1>h</h1>"),
                        [new MockTransition { From = "Home.html", To = c.File }],
                        "v2"
                    );

                    var lf = ((char)10).ToString();
                    var doc = ExportDoc(store);
                    doc.Should().Contain(lf + "  " + c.NodeId + "[\"画面\"]" + lf);
                    doc.Should().Contain(lf + "  s_Home --> " + c.NodeId + lf);
                    doc.Should().NotContain(lf + "  " + c.NodeId.Substring(2) + "[");
                    ExportBundle(store).Should().Contain($"<section data-screen=\"{c.Id}\">");
                }
            )
        );

    /// <summary>検証器に固有の行（外部参照・遷移の宣言の乖離・共有 CSS）</summary>
    private static IEnumerable<ScenarioRow<MockOutputScenario>> Validator() =>
        [
            Row(
                "validator-external-src-and-href-warned",
                "src・href の絶対 URL（http・https）を外部参照として警告する",
                [(MockOutputSut.Validator, "validator-external-attribute")],
                _ =>
                {
                    var html = Screen(
                        "<img src=\"https://cdn.example.com/a.png\">"
                            + "<a href=\"http://example.com\">外部</a>"
                    );

                    var warnings = MockContentValidator.ValidateScreen(
                        "Current.html",
                        html,
                        Array.Empty<MockTransition>(),
                        Array.Empty<string>()
                    );

                    warnings.Should().Contain(w => w.Contains("https://cdn.example.com/a.png"));
                    warnings.Should().Contain(w => w.Contains("http://example.com"));
                }
            ),
            Row(
                "validator-external-css-url-and-import-warned",
                "CSS の url() と @import の絶対 URL を外部参照として警告する",
                [(MockOutputSut.Validator, "validator-external-css")],
                _ =>
                {
                    var warnings = MockContentValidator.ValidateStylesheet(
                        "@import \"https://fonts.example.com/f.css\";"
                            + " body { background: url('https://img.example.com/bg.png'); }"
                    );

                    warnings.Should().Contain(w => w.Contains("https://fonts.example.com/f.css"));
                    warnings.Should().Contain(w => w.Contains("https://img.example.com/bg.png"));
                }
            ),
            Row(
                "validator-transition-target-not-linked-warned",
                "遷移の宣言の To が、既存の画面にも HTML のリンクにも無ければ警告する",
                [(MockOutputSut.Validator, "validator-transition-not-linked")],
                _ =>
                {
                    var warnings = MockContentValidator.ValidateScreen(
                        "Current.html",
                        Screen("<h1>本文のみ・リンクなし</h1>"),
                        [new MockTransition { From = "Current.html", To = "Dangling.html" }],
                        Array.Empty<string>()
                    );

                    warnings
                        .Should()
                        .Contain(w =>
                            w.Contains("Transition target") && w.Contains("Dangling.html")
                        );
                }
            ),
            Row(
                "validator-declared-targets-count-only-for-link-check",
                "他の画面が宣言した遷移の To はリンク先としては予告済みだが、遷移の宣言の乖離の検査には使わない",
                [(MockOutputSut.Validator, "validator-declared-targets-link-only")],
                _ =>
                {
                    var warnings = MockContentValidator.ValidateScreen(
                        "Current.html",
                        Screen("<a href=\"Planned.html\">p</a>"),
                        [new MockTransition { From = "Current.html", To = "Dangling.html" }],
                        Array.Empty<string>(),
                        ["Planned.html", "Dangling.html"]
                    );

                    warnings.Should().NotContain(w => w.Contains("Link target"));
                    warnings
                        .Should()
                        .Contain(w =>
                            w.Contains("Transition target") && w.Contains("Dangling.html")
                        );
                }
            ),
            Row(
                "validator-missing-stylesheet-link-warned",
                "共有 CSS（style.css）を参照しない画面を警告する（参照する画面はリンクの表記の行が全行で警告しないことを見る）",
                [(MockOutputSut.Validator, "validator-stylesheet-missing")],
                _ =>
                {
                    var warnings = MockContentValidator.ValidateScreen(
                        "Current.html",
                        "<!DOCTYPE html><html><head></head><body><h1>x</h1></body></html>",
                        Array.Empty<MockTransition>(),
                        Array.Empty<string>()
                    );

                    warnings.Should().Contain(w => w.Contains("style.css"));
                }
            ),
        ];

    /// <summary>2 画面＋遷移＋CSS の標準のモック</summary>
    private static MockFolderStore TwoScreenMock(
        MockOutputsContext context,
        string listName = "一覧"
    )
    {
        var store = context.CreateStore("受注管理", "schema");
        store.SaveStylesheet("body { color: #222; }", "css");
        store.SaveScreen(
            "OrderList.html",
            listName,
            "",
            Screen("<h1>注文一覧</h1><a href=\"OrderDetail.html\">詳細へ</a>"),
            [new MockTransition { From = "OrderList.html", To = "OrderDetail.html" }],
            "v1"
        );
        store.SaveScreen(
            "OrderDetail.html",
            "詳細",
            "",
            Screen("<h1>注文詳細</h1><a href=\"OrderList.html\">戻る</a>"),
            [new MockTransition { From = "OrderDetail.html", To = "OrderList.html" }],
            "v2"
        );
        return store;
    }

    /// <summary>単一 HTML に固有の行（section・CSS・ルーター・script）</summary>
    private static IEnumerable<ScenarioRow<MockOutputScenario>> Bundle() =>
        [
            Row(
                "bundle-two-screens-become-sections-with-hash-links",
                "2 画面を section へ連結し、相対リンクを #画面ID へ書き換え、.html の相対リンクを残さない",
                [(MockOutputSut.Bundle, "bundle-sections")],
                context =>
                {
                    var bundle = ExportBundle(TwoScreenMock(context));

                    bundle.Should().Contain("data-screen=\"OrderList\"");
                    bundle.Should().Contain("data-screen=\"OrderDetail\"");
                    bundle.Should().Contain("注文一覧");
                    bundle.Should().Contain("注文詳細");
                    bundle.Should().Contain("href=\"#OrderDetail\"");
                    bundle.Should().Contain("href=\"#OrderList\"");
                    bundle.Should().NotContain("href=\"OrderDetail.html\"");
                    bundle.Should().NotContain("href=\"OrderList.html\"");
                }
            ),
            Row(
                "bundle-stylesheet-inlined",
                "共有 CSS を <style> へ入れ、style.css への link を残さない",
                [(MockOutputSut.Bundle, "bundle-inline-css")],
                context =>
                {
                    var bundle = ExportBundle(TwoScreenMock(context));

                    bundle.Should().Contain("<style>");
                    bundle.Should().Contain("body { color: #222; }");
                    bundle.Should().NotContain("href=\"style.css\"");
                }
            ),
            Row(
                "bundle-hash-router-embedded",
                "ハッシュルーターの JS を埋め込む（hash と data-screen をデコード・小文字化して照らし、一致しない hash では表示中の画面を据え置き、初回だけ先頭の画面）",
                [(MockOutputSut.Bundle, "bundle-router")],
                context =>
                {
                    var bundle = ExportBundle(TwoScreenMock(context));

                    bundle.Should().Contain("hashchange");
                    bundle.Should().Contain("location.hash");
                    bundle.Should().Contain("data-screen");
                    bundle
                        .Should()
                        .Contain("try { value = decodeURIComponent(value); } catch (e) {}");
                    bundle.Should().Contain("return value.toLowerCase();");
                    bundle.Should().Contain("var id = key(location.hash.replace(/^#/, ''));");
                    bundle.Should().Contain("key(s.getAttribute('data-screen')) === id");
                    bundle.Should().Contain("if (current !== null) { return; }");
                    bundle.Should().Contain("var fallback = \"OrderList\";");
                    bundle.Should().Contain("current = target;");
                }
            ),
            Row(
                "bundle-script-moves-to-section-end",
                "画面の中の script は本文から外して section の末尾（</section> の直前）へ移す",
                [(MockOutputSut.Bundle, "bundle-script-moved")],
                context =>
                {
                    var store = context.CreateStore();
                    store.SaveStylesheet("body{}", "css");
                    store.SaveScreen(
                        "A.html",
                        "a",
                        "",
                        Screen("<h1>x</h1><script>console.log('hi');</script><p>after</p>"),
                        Array.Empty<MockTransition>(),
                        "v1"
                    );

                    SectionOf(ExportBundle(store), "A")
                        .Should()
                        .EndWith(
                            "<h1>x</h1><p>after</p><script>console.log('hi');</script></section>"
                        );
                }
            ),
        ];

    /// <summary>行を作る</summary>
    private static ScenarioRow<MockOutputScenario> Row(
        string id,
        string description,
        IReadOnlyList<(MockOutputSut Sut, string Aspect)> observations,
        Action<MockOutputsContext> body
    ) => new(id, null, description, new MockOutputScenario(observations, body));
}
