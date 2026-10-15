using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using AwesomeAssertions;
using QuickER.AI.UI;
using QuickER.Platform;
using QuickER.Tests.TestSupport;

namespace QuickER.Tests.AI.UI;

/// <summary>
/// <see cref="ChatMarkdownRenderer"/> が Markdown を FlowDocument へ写すこと、
/// および安全側の扱い（リンクは http / https だけ・画像は読み込まない・HTML は解釈しない）を検証するテストクラス。
/// </summary>
public class ChatMarkdownRendererTests
{
    /// <summary>開かれた URL を記録する opener</summary>
    private sealed class RecordingOpener : IChatLinkOpener
    {
        /// <summary>開かれた URL の記録</summary>
        public List<Uri> Opened { get; } = new();

        /// <inheritdoc />
        public void Open(Uri uri) => Opened.Add(uri);
    }

    /// <summary>STA で組み立て、検証を実行する</summary>
    private static void Render(string markdown, Action<FlowDocument, RecordingOpener> assert)
    {
        WpfApplicationTestSupport.RunSta(() =>
        {
            var opener = new RecordingOpener();
            var document = ChatMarkdownRenderer.Render(markdown, opener);
            assert(document, opener);
        });
    }

    /// <summary>文書の表示文字列（改行は \n に揃える）</summary>
    private static string TextOf(FlowDocument document) =>
        new TextRange(document.ContentStart, document.ContentEnd).Text.ReplaceLineEndings("\n");

    /// <summary>要素の表示文字列（改行は \n に揃える）</summary>
    private static string TextOf(TextElement element) =>
        new TextRange(element.ContentStart, element.ContentEnd).Text.ReplaceLineEndings("\n");

    /// <summary>要素に含まれる Run の文字をつなげる（リストの行頭記号のように Run でない表示を含めない）</summary>
    private static string RunText(DependencyObject element) =>
        string.Concat(Descendants(element).OfType<Run>().Select(r => r.Text));

    /// <summary>論理ツリーの子孫をすべて列挙する</summary>
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
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

    /// <summary>見出しが段に応じた大きさの太字の段落になることを検証する</summary>
    [Fact(DisplayName = "見出しは段に応じた大きさの太字の段落になる")]
    public void Heading_BecomesLargeBoldParagraph()
    {
        Render(
            "# Title\n\n### Sub",
            (document, _) =>
            {
                var blocks = document.Blocks.OfType<Paragraph>().ToList();
                blocks.Should().HaveCount(2);
                TextOf(blocks[0]).Should().Be("Title");
                blocks[0].FontSize.Should().Be(18);
                blocks[0].FontWeight.Should().Be(FontWeights.SemiBold);
                blocks[1].FontSize.Should().Be(14.5);
            }
        );
    }

    /// <summary>太字・斜体・インラインコードが対応する要素になることを検証する</summary>
    [Fact(DisplayName = "太字・斜体・インラインコードは対応する要素になる")]
    public void Emphasis_And_InlineCode()
    {
        Render(
            "**bold** *italic* `code`",
            (document, _) =>
            {
                var nodes = Descendants(document).ToList();
                TextOf(nodes.OfType<Bold>().Single()).Should().Be("bold");
                TextOf(nodes.OfType<Italic>().Single()).Should().Be("italic");
                var code = nodes.OfType<Run>().Single(r => r.Text == "code");
                code.FontFamily.Source.Should().Contain("Consolas");
                code.Background.Should().NotBeNull();
            }
        );
    }

    /// <summary>段落内の単一の改行が改行として表示されることを検証する</summary>
    [Fact(DisplayName = "段落内の単一の改行（ソフト改行）は改行になる")]
    public void SoftLineBreak_InParagraph_BecomesLineBreak()
    {
        Render(
            "一行目\n二行目",
            (document, _) =>
            {
                var paragraph = document.Blocks.OfType<Paragraph>().Single();
                paragraph.Inlines.OfType<LineBreak>().Should().ContainSingle();
                TextOf(document).Should().StartWith("一行目\n二行目");
            }
        );
    }

    /// <summary>コードブロックは等幅・背景つきで、行と中身がそのまま保たれることを検証する</summary>
    [Fact(DisplayName = "コードブロックは等幅・背景つきで行と中身をそのまま保つ")]
    public void FencedCode_KeepsLinesVerbatim()
    {
        Render(
            "```csharp\nvar a = 1;\n  **not bold**\n```",
            (document, _) =>
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
        );
    }

    /// <summary>箇条書きと番号付きリスト（開始番号つき）を検証する</summary>
    [Fact(DisplayName = "箇条書きと番号付きリストは開始番号を保って List になる")]
    public void Lists_BecomeLists()
    {
        Render(
            "- a\n- b\n\n3. x\n4. y",
            (document, _) =>
            {
                var lists = document.Blocks.OfType<List>().ToList();
                lists.Should().HaveCount(2);
                lists[0].MarkerStyle.Should().Be(TextMarkerStyle.Disc);
                lists[0].ListItems.Select(RunText).Should().Equal("a", "b");
                lists[1].MarkerStyle.Should().Be(TextMarkerStyle.Decimal);
                lists[1].StartIndex.Should().Be(3);
                lists[1].ListItems.Select(RunText).Should().Equal("x", "y");
            }
        );
    }

    /// <summary>パイプテーブルが表になり、見出し行と列の寄せが反映されることを検証する</summary>
    [Fact(DisplayName = "パイプテーブルは見出し行と列の寄せを反映した表になる")]
    public void PipeTable_BecomesTable()
    {
        Render(
            "| Name | Count |\n|:--|--:|\n| apple | 12 |\n| pear | 3 |",
            (document, _) =>
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
                Descendants(table).OfType<LineBreak>().Should().BeEmpty("表のセルに改行は入らない");
            }
        );
    }

    /// <summary>引用が左罫線つきのセクションになることを検証する</summary>
    [Fact(DisplayName = "引用は左罫線つきのセクションになる")]
    public void Quote_BecomesBorderedSection()
    {
        Render(
            "> quoted",
            (document, _) =>
            {
                var section = document.Blocks.OfType<Section>().Single();
                section.BorderThickness.Left.Should().Be(3);
                TextOf(section).Should().Be("quoted");
            }
        );
    }

    /// <summary>区切り線が文字を持たない罫線の段落になることを検証する</summary>
    [Fact(DisplayName = "区切り線は文字を持たない罫線の段落になる")]
    public void ThematicBreak_BecomesRule()
    {
        Render(
            "a\n\n---\n\nb",
            (document, _) =>
            {
                var blocks = document.Blocks.OfType<Paragraph>().ToList();
                blocks.Should().HaveCount(3);
                blocks[1].BorderThickness.Bottom.Should().Be(1);
                blocks[1].Inlines.Should().BeEmpty();
            }
        );
    }

    /// <summary>http / https のリンクは押せるリンクになり、ツールチップに実際の URL を出すことを検証する</summary>
    [Theory(
        DisplayName = "http / https のリンクは押せるリンクになりツールチップに実際の URL を出す"
    )]
    [InlineData("[docs](https://example.com/a?b=1)", "docs", "https://example.com/a?b=1")]
    [InlineData("[site](http://example.com/)", "site", "http://example.com/")]
    [InlineData("see https://example.com/x here", "https://example.com/x", "https://example.com/x")]
    [InlineData("<https://example.com/y>", "https://example.com/y", "https://example.com/y")]
    [InlineData("visit www.example.com now", "www.example.com", "http://www.example.com/")]
    public void SafeLink_BecomesHyperlink(string markdown, string text, string url)
    {
        Render(
            markdown,
            (document, _) =>
            {
                var hyperlink = Descendants(document).OfType<Hyperlink>().Single();
                TextOf(hyperlink).Should().Be(text);
                hyperlink.ToolTip.Should().Be(url);
                hyperlink
                    .NavigateUri.Should()
                    .BeNull(
                        "開く経路は opener の 1 本に限り、WPF の既定のナビゲーションへ渡さない"
                    );
            }
        );
    }

    /// <summary>リンクのクリックで、検証済みの URL が opener へ渡ることを検証する</summary>
    [Fact(DisplayName = "リンクのクリックで検証済みの URL が opener へ渡る")]
    public void ClickingLink_OpensThroughOpener()
    {
        Render(
            "[docs](https://example.com/path)",
            (document, opener) =>
            {
                var hyperlink = Descendants(document).OfType<Hyperlink>().Single();
                hyperlink.RaiseEvent(new RoutedEventArgs(Hyperlink.ClickEvent, hyperlink));
                opener.Opened.Should().Equal(new Uri("https://example.com/path"));
            }
        );
    }

    /// <summary>http / https 以外のリンクは押せない普通の文字になることを検証する</summary>
    [Theory(DisplayName = "http / https 以外のリンクは押せない普通の文字になる")]
    [InlineData("[t](file:///C:/Windows/notepad.exe)", "t")]
    [InlineData("[t](\\\\\\\\server\\\\share)", "t")]
    [InlineData("[t](javascript:alert(1))", "t")]
    [InlineData("[t](ms-settings:privacy)", "t")]
    [InlineData("[t](/relative/path)", "t")]
    [InlineData("[t](page.html)", "t")]
    [InlineData("[t](//server/share)", "t")]
    [InlineData("[t](mailto:a@example.com)", "t")]
    [InlineData("<mailto:a@example.com>", "mailto:a@example.com")]
    [InlineData("<a@example.com>", "a@example.com")]
    [InlineData("<file:///C:/x>", "file:///C:/x")]
    public void UnsafeLink_IsPlainText(string markdown, string text)
    {
        Render(
            markdown,
            (document, opener) =>
            {
                Descendants(document).OfType<Hyperlink>().Should().BeEmpty();
                TextOf(document).Should().Contain(text);
                opener.Opened.Should().BeEmpty();
            }
        );
    }

    /// <summary>リンク先の規則（http / https の絶対 URL でホストを持つものだけ）を検証する</summary>
    [Theory(DisplayName = "開いてよいリンク先は http / https の絶対 URL でホストを持つものだけ")]
    [InlineData("https://example.com/", true)]
    [InlineData("HTTP://EXAMPLE.COM/", true)]
    [InlineData("file:///C:/x", false)]
    [InlineData(@"\\server\share", false)]
    [InlineData("javascript:alert(1)", false)]
    [InlineData("ms-settings:privacy", false)]
    [InlineData("vscode://file/c:/x", false)]
    [InlineData("/relative", false)]
    [InlineData("relative.html", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Policy_AllowsOnlyHttpAndHttps(string? url, bool expected)
    {
        UrlPolicy.TryGetSafeUri(url, out var uri).Should().Be(expected);
        (uri is not null).Should().Be(expected);
    }

    /// <summary>画像は読み込まず代替テキストだけを出すことを検証する</summary>
    [Fact(DisplayName = "画像は読み込まず代替テキストだけを出す")]
    public void Image_ShowsAltTextOnly()
    {
        Render(
            "before ![a cat](https://example.com/cat.png) after",
            (document, opener) =>
            {
                var nodes = Descendants(document).ToList();
                nodes.OfType<Hyperlink>().Should().BeEmpty("画像はリンクにもしない");
                nodes.OfType<InlineUIContainer>().Should().BeEmpty();
                nodes.OfType<BlockUIContainer>().Should().BeEmpty();
                nodes.OfType<Image>().Should().BeEmpty();
                TextOf(document).Should().Contain("before a cat after");
                TextOf(document).Should().NotContain("cat.png");
            }
        );
    }

    /// <summary>インラインの HTML を解釈せず文字として出すことを検証する</summary>
    [Fact(DisplayName = "インラインの HTML は解釈せず文字として出す")]
    public void InlineHtml_IsLiteralText()
    {
        Render(
            "a <b>x</b> <img src=\"https://example.com/t.png\"> b",
            (document, _) =>
            {
                Descendants(document).OfType<Bold>().Should().BeEmpty();
                TextOf(document)
                    .Should()
                    .Contain("a <b>x</b> <img src=\"https://example.com/t.png\"> b");
            }
        );
    }

    /// <summary>ブロックの HTML を解釈せず文字として出すことを検証する</summary>
    [Fact(DisplayName = "ブロックの HTML は解釈せず文字として出す")]
    public void BlockHtml_IsLiteralText()
    {
        Render(
            "<div>\n<script>alert(1)</script>\n</div>",
            (document, _) =>
            {
                TextOf(document).Should().Contain("<div>");
                TextOf(document).Should().Contain("<script>alert(1)</script>");
            }
        );
    }

    /// <summary>
    /// 対にならない記号が続く回答では、隣り合う飾りの無い文字を 1 つの Run にまとめることを検証する
    /// （1 文字ずつの Run がリンクの中に数千並ぶと、WPF のレイアウトに数十秒以上かかる）。
    /// </summary>
    [Fact(DisplayName = "対にならない記号が続く回答は飾りの無い文字を 1 つの Run にまとめる")]
    public void UnmatchedDelimiters_AreMergedIntoOneRun()
    {
        var brackets = string.Concat(Enumerable.Repeat("[]", 500));

        Render(
            "[" + brackets + "](https://example.com/)",
            (document, _) =>
            {
                var hyperlink = Descendants(document).OfType<Hyperlink>().Single();
                hyperlink.Inlines.Should().ContainSingle();
                TextOf(hyperlink).Should().Be(brackets);
            }
        );

        Render(
            new string(']', 500) + "x **bold** y",
            (document, _) =>
            {
                var paragraph = document.Blocks.OfType<Paragraph>().Single();
                paragraph
                    .Inlines.Select(i => i.GetType().Name)
                    .Should()
                    .Equal("Run", "Bold", "Run");
                TextOf(paragraph).Should().Be(new string(']', 500) + "x bold y");
            }
        );
    }

    /// <summary>最後のブロックの下余白が詰められることを検証する</summary>
    [Fact(DisplayName = "最後のブロックの下余白は詰める")]
    public void LastBlock_HasNoBottomMargin()
    {
        Render(
            "a\n\nb",
            (document, _) =>
            {
                document.Blocks.FirstBlock.Margin.Bottom.Should().BeGreaterThan(0);
                document.Blocks.LastBlock.Margin.Bottom.Should().Be(0);
            }
        );
    }
}
