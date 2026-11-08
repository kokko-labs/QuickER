using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using AwesomeAssertions;
using Markdig;
using QuickER.AI.UI;
using QuickER.Tests.TestSupport;
using QuickER.Tests.TestSupport.Engines;
using static QuickER.Tests.AI.UI.ChatMarkdownProbe;

namespace QuickER.Tests.AI.UI;

/// <summary>Markdown 表示の表の HTML・入れ子の深さ・表示の行</summary>
internal static partial class ChatMarkdownRows
{
    /// <summary>HTML の行（解釈せず文字として出す。飾り・リンク・画像を作らない）</summary>
    private static IEnumerable<ScenarioRow<ChatMarkdownCase>> HtmlRows() =>
        [
            Html(
                "html-inline-tag",
                MarkdownElement.HtmlInline,
                MarkdownForm.Tag,
                "a <b>x</b> b",
                "a <b>x</b> b"
            ),
            Html(
                "html-inline-anchor-javascript",
                MarkdownElement.HtmlInline,
                MarkdownForm.Tag,
                "a <a href=\"javascript:alert(1)\">x</a> b",
                "a <a href=\"javascript:alert(1)\">x</a> b"
            ),
            Html(
                "html-inline-script",
                MarkdownElement.HtmlInline,
                MarkdownForm.Script,
                "a <script>alert(1)</script> b",
                "a <script>alert(1)</script> b"
            ),
            Html(
                "html-inline-img",
                MarkdownElement.HtmlInline,
                MarkdownForm.ImgTag,
                "a <b>x</b> <img src=\"https://example.com/t.png\"> b",
                "a <b>x</b> <img src=\"https://example.com/t.png\"> b"
            ),
            Html(
                "html-inline-comment",
                MarkdownElement.HtmlInline,
                MarkdownForm.Comment,
                "a <!-- c --> b",
                "a <!-- c --> b"
            ),
            Html(
                "html-inline-entity",
                MarkdownElement.HtmlInline,
                MarkdownForm.Entity,
                "a &lt;b&gt;x&lt;/b&gt; &amp; b",
                "a <b>x</b> & b"
            ),
            Html(
                "html-block-tag",
                MarkdownElement.HtmlBlock,
                MarkdownForm.Tag,
                "<div>\n<p><b>x</b></p>\n</div>",
                "<div>\n<p><b>x</b></p>\n</div>"
            ),
            Html(
                "html-block-script-in-div",
                MarkdownElement.HtmlBlock,
                MarkdownForm.Script,
                "<div>\n<script>alert(1)</script>\n</div>",
                "<div>",
                "<script>alert(1)</script>"
            ),
            Html(
                "html-block-script",
                MarkdownElement.HtmlBlock,
                MarkdownForm.Script,
                "<script>alert(1)</script>",
                "<script>alert(1)</script>"
            ),
            Html(
                "html-block-img",
                MarkdownElement.HtmlBlock,
                MarkdownForm.ImgTag,
                "<img src=\"https://example.com/t.png\">",
                "<img src=\"https://example.com/t.png\">"
            ),
            Html(
                "html-block-comment",
                MarkdownElement.HtmlBlock,
                MarkdownForm.Comment,
                "<!-- block -->",
                "<!-- block -->"
            ),
            Html(
                "html-block-entity",
                MarkdownElement.HtmlBlock,
                MarkdownForm.Entity,
                "&lt;script&gt;alert(1)&lt;/script&gt;",
                "<script>alert(1)</script>"
            ),
        ];

    /// <summary>HTML の 1 行</summary>
    private static ScenarioRow<ChatMarkdownCase> Html(
        string id,
        MarkdownElement element,
        MarkdownForm form,
        string markdown,
        params string[] contains
    ) =>
        Row(
            id,
            $"{(element == MarkdownElement.HtmlInline ? "インライン" : "ブロック")}の HTML（{form}）は解釈せず文字として出す",
            element,
            form,
            () =>
                Render(
                    markdown,
                    (document, _) =>
                    {
                        var nodes = Descendants(document).ToList();
                        nodes.OfType<Bold>().Should().BeEmpty("HTML のタグから飾りを作らない");
                        nodes.OfType<Italic>().Should().BeEmpty();
                        nodes
                            .OfType<Hyperlink>()
                            .Should()
                            .BeEmpty("HTML のアンカーからリンクを作らない");

                        foreach (var text in contains)
                        {
                            TextOf(document).Should().Contain(text);
                        }
                    }
                )
        );

    /// <summary>入れ子の深さの行</summary>
    /// <remarks>
    /// <para>構文木の深さ（文書 0 から数える）の導き方：</para>
    /// <list type="bullet">
    /// <item>引用 n 段：文書 → 引用 n 段 → 段落 → 段落の中のインラインの根 → 文字 で n＋3（29 段で 32・30 段で 33）。</item>
    /// <item>強調 n 段（<c>*a _a *a … x … a* a_ a*</c>）：文書 → 段落 → インラインの根 → 強調 n 段 → 文字 で n＋3（記号だけを並べた形は
    /// Markdig が 2 文字ずつ 1 段にまとめるので、語を挟む）。</item>
    /// <item>リスト k 段：段ごとにリストとリストの項目の 2 段で 2k＋3、最後の項目の中を強調にすると 2k＋4（14 段＋強調で 32・15 段で 33）。</item>
    /// </list>
    /// <para>上限ちょうどは組み立てて Markdown 表示、上限を超えると組み立てずに断り（<see cref="InvalidOperationException"/>）、表示部品は
    /// その吹き出しだけ本文そのままのテキスト表示へ落とす。200 段（引用・リストは Markdig 自身の 128 段の上限で断られる）も落ちずにテキスト表示。</para>
    /// </remarks>
    private static IEnumerable<ScenarioRow<ChatMarkdownCase>> DepthRows()
    {
        yield return Depth("emphasis", MarkdownForm.AtLimit, Emphasis(29), 32);
        yield return Depth("emphasis", MarkdownForm.OverLimit, Emphasis(30), 33);
        yield return Depth(
            "emphasis",
            MarkdownForm.Far,
            new string('*', 200) + "x" + new string('*', 200),
            null
        );
        yield return Depth("quote", MarkdownForm.AtLimit, new string('>', 29) + " x", 32);
        yield return Depth("quote", MarkdownForm.OverLimit, new string('>', 30) + " x", 33);
        yield return Depth("quote", MarkdownForm.Far, new string('>', 200) + " x", null);
        yield return Depth("list", MarkdownForm.AtLimit, List(14, emphasizeLast: true), 32);
        yield return Depth("list", MarkdownForm.OverLimit, List(15, emphasizeLast: false), 33);
        yield return Depth("list", MarkdownForm.Far, List(200, emphasizeLast: false), null);
    }

    /// <summary>入れ子の深さの 1 行</summary>
    /// <param name="shape">形（強調・引用・リスト）</param>
    /// <param name="form">深さの形</param>
    /// <param name="text">回答の本文</param>
    /// <param name="depth">構文木の深さ（導き方は <see cref="DepthRows"/>。200 段の行は null＝Markdig の上限で解析できないものを含む）</param>
    private static ScenarioRow<ChatMarkdownCase> Depth(
        string shape,
        MarkdownForm form,
        string text,
        int? depth
    )
    {
        var renders = form == MarkdownForm.AtLimit;

        return Row(
            $"depth-{shape}-{form switch { MarkdownForm.AtLimit => "at-limit", MarkdownForm.OverLimit => "over-limit", _ => "far" }}",
            renders
                ? $"{shape} の入れ子が上限ちょうど（深さ 32）の回答は組み立てて Markdown 表示にする"
                : $"{shape} の入れ子が上限を超える回答は組み立てず、その吹き出しだけ落ちずにテキスト表示になる（同じ本文で組み立てを繰り返さない）",
            MarkdownElement.Nesting,
            form,
            () =>
            {
                if (depth is not null)
                {
                    ChatMarkdownRenderer
                        .MeasureDepth(Markdown.Parse(text, ChatMarkdownRenderer.Pipeline))
                        .Should()
                        .Be(depth.Value, "行の本文が宣言した深さを持つ");
                }

                WpfApplicationTestSupport.RunSta(() =>
                {
                    var render = () => ChatMarkdownRenderer.Render(text, new RecordingOpener());

                    if (renders)
                    {
                        render.Should().NotThrow();
                    }
                    else if (form == MarkdownForm.OverLimit || shape == "emphasis")
                    {
                        // 強調の入れ子は Markdig に上限が無いので、組み立て側の上限が先に効く
                        render.Should().Throw<InvalidOperationException>();
                    }
                });

                View(
                    text,
                    view =>
                    {
                        if (renders)
                        {
                            view.IsShowingMarkdown.Should().BeTrue();
                            view.Content.Should().BeOfType<RichTextBox>();
                            return;
                        }

                        view.IsShowingMarkdown.Should().BeFalse();
                        view.Content.Should().BeOfType<TextBox>().Which.Text.Should().Be(text);

                        // トグルを切り替えても同じ本文で組み立てを繰り返さず、テキスト表示のまま
                        view.ShowMarkdown = false;
                        view.ShowMarkdown = true;
                        view.IsShowingMarkdown.Should().BeFalse();
                    }
                );
            }
        );
    }

    /// <summary>語を挟んだ強調の n 段の入れ子</summary>
    private static string Emphasis(int levels) =>
        string.Concat(Enumerable.Range(0, levels).Select(i => (i % 2 == 0 ? "*" : "_") + "a "))
        + "x"
        + string.Concat(
            Enumerable.Range(0, levels).Reverse().Select(i => " a" + (i % 2 == 0 ? "*" : "_"))
        );

    /// <summary>k 段の入れ子のリスト（最後の項目を強調にできる）</summary>
    private static string List(int levels, bool emphasizeLast) =>
        string.Concat(
            Enumerable
                .Range(0, levels)
                .Select(i =>
                    new string(' ', i * 2)
                    + (emphasizeLast && i == levels - 1 ? "- *x*\n" : "- x\n")
                )
        );

    /// <summary>表示の行（次元を持たない。Markdown の構文が対応する要素になる）</summary>
    private static IEnumerable<ScenarioRow<ChatMarkdownCase>> DisplayRows() =>
        [
            Display(
                "display-heading",
                "見出しは段に応じた大きさの太字の段落になる",
                "# Title\n\n### Sub",
                document =>
                {
                    var blocks = document.Blocks.OfType<Paragraph>().ToList();
                    blocks.Should().HaveCount(2);
                    TextOf(blocks[0]).Should().Be("Title");
                    blocks[0].FontSize.Should().Be(18);
                    blocks[0].FontWeight.Should().Be(FontWeights.SemiBold);
                    blocks[1].FontSize.Should().Be(14.5);
                }
            ),
            Display(
                "display-emphasis-and-inline-code",
                "太字・斜体・インラインコードは対応する要素になる",
                "**bold** *italic* `code`",
                document =>
                {
                    var nodes = Descendants(document).ToList();
                    TextOf(nodes.OfType<Bold>().Single()).Should().Be("bold");
                    TextOf(nodes.OfType<Italic>().Single()).Should().Be("italic");
                    var code = nodes.OfType<Run>().Single(r => r.Text == "code");
                    code.FontFamily.Source.Should().Contain("Consolas");
                    code.Background.Should().NotBeNull();
                }
            ),
            Display(
                "display-soft-line-break",
                "段落内の単一の改行（ソフト改行）は改行になる",
                "一行目\n二行目",
                document =>
                {
                    var paragraph = document.Blocks.OfType<Paragraph>().Single();
                    paragraph.Inlines.OfType<LineBreak>().Should().ContainSingle();
                    TextOf(document).Should().StartWith("一行目\n二行目");
                }
            ),
            Display(
                "display-fenced-code",
                "コードブロックは等幅・背景つきで行と中身をそのまま保つ",
                "```csharp\nvar a = 1;\n  **not bold**\n```",
                document =>
                {
                    var paragraph = document.Blocks.OfType<Paragraph>().Single();
                    paragraph.FontFamily.Source.Should().Contain("Consolas");
                    paragraph.Background.Should().NotBeNull();
                    paragraph
                        .Inlines.OfType<LineBreak>()
                        .Should()
                        .ContainSingle("行の区切りは元の改行 1 つだけ");
                    TextOf(paragraph).Should().Be("var a = 1;\n  **not bold**");
                    Descendants(document).OfType<Bold>().Should().BeEmpty();
                }
            ),
            Display(
                "display-lists",
                "箇条書きと番号付きリストは開始番号を保って List になる",
                "- a\n- b\n\n3. x\n4. y",
                document =>
                {
                    var lists = document.Blocks.OfType<List>().ToList();
                    lists.Should().HaveCount(2);
                    lists[0].MarkerStyle.Should().Be(TextMarkerStyle.Disc);
                    lists[0].ListItems.Select(RunText).Should().Equal("a", "b");
                    lists[1].MarkerStyle.Should().Be(TextMarkerStyle.Decimal);
                    lists[1].StartIndex.Should().Be(3);
                    lists[1].ListItems.Select(RunText).Should().Equal("x", "y");
                }
            ),
            Display(
                "display-pipe-table",
                "パイプテーブルは見出し行と列の寄せを反映した表になる",
                "| Name | Count |\n|:--|--:|\n| apple | 12 |\n| pear | 3 |",
                document =>
                {
                    var table = document.Blocks.OfType<Table>().Single();
                    table.Columns.Should().HaveCount(2);
                    var rows = table.RowGroups.Single().Rows;
                    rows.Should().HaveCount(3);
                    rows[0].FontWeight.Should().Be(FontWeights.SemiBold);
                    rows[1].Cells.Select(TextOf).Should().Equal("apple", "12");

                    var countCell = rows[1].Cells[1].Blocks.OfType<Paragraph>().Single();
                    countCell.TextAlignment.Should().Be(TextAlignment.Right);
                    rows[1]
                        .Cells[0]
                        .Blocks.OfType<Paragraph>()
                        .Single()
                        .TextAlignment.Should()
                        .Be(TextAlignment.Left);
                    Descendants(table)
                        .OfType<LineBreak>()
                        .Should()
                        .BeEmpty("表のセルに改行は入らない");
                }
            ),
            Display(
                "display-quote",
                "引用は左罫線つきのセクションになる",
                "> quoted",
                document =>
                {
                    var section = document.Blocks.OfType<Section>().Single();
                    section.BorderThickness.Left.Should().Be(3);
                    TextOf(section).Should().Be("quoted");
                }
            ),
            Display(
                "display-thematic-break",
                "区切り線は文字を持たない罫線の段落になる",
                "a\n\n---\n\nb",
                document =>
                {
                    var blocks = document.Blocks.OfType<Paragraph>().ToList();
                    blocks.Should().HaveCount(3);
                    blocks[1].BorderThickness.Bottom.Should().Be(1);
                    blocks[1].Inlines.Should().BeEmpty();
                }
            ),
            Display(
                "display-unmatched-brackets-in-link-merge-into-one-run",
                "リンクの中の対にならない記号が続く回答は飾りの無い文字を 1 つの Run にまとめる（1 文字ずつの Run が数千並ぶとレイアウトに数十秒以上かかる）",
                "[" + string.Concat(Enumerable.Repeat("[]", 500)) + "](https://example.com/)",
                document =>
                {
                    var hyperlink = Descendants(document).OfType<Hyperlink>().Single();
                    hyperlink.Inlines.Should().ContainSingle();
                    TextOf(hyperlink).Should().Be(string.Concat(Enumerable.Repeat("[]", 500)));
                }
            ),
            Display(
                "display-unmatched-brackets-around-bold-merge",
                "対にならない記号の後の太字は、前後の飾りの無い文字を 1 つずつの Run にまとめる",
                new string(']', 500) + "x **bold** y",
                document =>
                {
                    var paragraph = document.Blocks.OfType<Paragraph>().Single();
                    paragraph
                        .Inlines.Select(i => i.GetType().Name)
                        .Should()
                        .Equal("Run", "Bold", "Run");
                    TextOf(paragraph).Should().Be(new string(']', 500) + "x bold y");
                }
            ),
            Display(
                "display-last-block-has-no-bottom-margin",
                "最後のブロックの下余白は詰める",
                "a\n\nb",
                document =>
                {
                    document.Blocks.FirstBlock.Margin.Bottom.Should().BeGreaterThan(0);
                    document.Blocks.LastBlock.Margin.Bottom.Should().Be(0);
                }
            ),
            Display(
                "display-ordinary-nesting-renders",
                "実際の回答で使う程度の入れ子（リストの中の引用の中の太字とコード）は組み立てる",
                "- item\n  > quote with **bold `code`** and *em*",
                document => document.Blocks.Should().NotBeEmpty()
            ),
        ];

    /// <summary>表示の 1 行</summary>
    private static ScenarioRow<ChatMarkdownCase> Display(
        string id,
        string description,
        string markdown,
        Action<FlowDocument> assert
    ) =>
        Row(id, description, null, null, () => Render(markdown, (document, _) => assert(document)));
}
