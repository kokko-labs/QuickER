using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using AwesomeAssertions;
using QuickER.AI.UI;
using QuickER.Tests.TestSupport;

namespace QuickER.Tests.AI.UI;

/// <summary>
/// <see cref="AssistantMessageView"/> がテキスト表示と Markdown 表示を正しく選び、
/// 組み立てに失敗する回答（入れ子が深すぎる等）でも落ちずにテキスト表示へ落とすことを検証するテストクラス。
/// </summary>
public class AssistantMessageViewTests
{
    /// <summary>開かれた URL を記録する opener</summary>
    private sealed class RecordingOpener : IChatLinkOpener
    {
        /// <summary>開かれた URL の記録</summary>
        public List<Uri> Opened { get; } = new();

        /// <inheritdoc />
        public void Open(Uri uri) => Opened.Add(uri);
    }

    /// <summary>応答済み・Markdown 表示・opener ありの部品を STA で作って検証する</summary>
    private static void WithView(string text, Action<AssistantMessageView, RecordingOpener> assert)
    {
        WpfApplicationTestSupport.RunSta(() =>
        {
            var opener = new RecordingOpener();
            var view = new AssistantMessageView
            {
                Text = text,
                IsStreaming = false,
                ShowMarkdown = true,
                LinkOpener = opener,
            };
            assert(view, opener);
        });
    }

    /// <summary>Markdown 表示の文書の論理ツリーをすべて列挙する</summary>
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

    /// <summary>応答済みの回答は Markdown 表示になることを検証する</summary>
    [Fact(DisplayName = "応答済みの回答は Markdown 表示になる")]
    public void Completed_ShowsMarkdown()
    {
        WithView(
            "**bold** text",
            (view, _) =>
            {
                view.IsShowingMarkdown.Should().BeTrue();
                var box = view.Content.Should().BeOfType<RichTextBox>().Subject;
                box.IsReadOnly.Should().BeTrue();
                box.IsDocumentEnabled.Should().BeTrue("リンクを押せるようにする");
                Descendants(box.Document).OfType<Bold>().Should().ContainSingle();
            }
        );
    }

    /// <summary>応答中の回答はテキスト表示で、応答が終わると Markdown 表示へ切り替わることを検証する</summary>
    [Fact(DisplayName = "応答中はテキスト表示で、応答が終わると Markdown 表示へ切り替わる")]
    public void Streaming_ShowsTextUntilCompleted()
    {
        WithView(
            "**bold**",
            (view, _) =>
            {
                view.IsStreaming = true;
                view.IsShowingMarkdown.Should().BeFalse();
                view.Content.Should().BeOfType<TextBox>().Which.Text.Should().Be("**bold**");

                view.Text = "**bold** more";
                view.Content.Should().BeOfType<TextBox>().Which.Text.Should().Be("**bold** more");

                view.IsStreaming = false;
                view.IsShowingMarkdown.Should().BeTrue();
            }
        );
    }

    /// <summary>トグルを切ると元の文章のテキスト表示になり、戻すと Markdown 表示に戻ることを検証する</summary>
    [Fact(DisplayName = "トグルを切ると元の文章のテキスト表示になり、戻すと Markdown 表示に戻る")]
    public void Toggle_SwitchesBetweenTextAndMarkdown()
    {
        WithView(
            "# Title",
            (view, _) =>
            {
                view.ShowMarkdown = false;
                view.Content.Should().BeOfType<TextBox>().Which.Text.Should().Be("# Title");

                view.ShowMarkdown = true;
                view.IsShowingMarkdown.Should().BeTrue();
            }
        );
    }

    /// <summary>リンクを開く処理が無いあいだはテキスト表示にすることを検証する</summary>
    [Fact(DisplayName = "リンクを開く処理が無いあいだはテキスト表示にする")]
    public void WithoutOpener_ShowsText()
    {
        WithView(
            "**bold**",
            (view, _) =>
            {
                view.LinkOpener = null;
                view.IsShowingMarkdown.Should().BeFalse();
            }
        );
    }

    /// <summary>Markdown 表示のまま本文が変わったら組み立て直すことを検証する</summary>
    [Fact(DisplayName = "Markdown 表示のまま本文が変わったら組み立て直す")]
    public void TextChange_Rerenders()
    {
        WithView(
            "first",
            (view, _) =>
            {
                view.Text = "**second**";
                var box = view.Content.Should().BeOfType<RichTextBox>().Subject;
                Descendants(box.Document).OfType<Bold>().Should().ContainSingle();
            }
        );
    }

    /// <summary>Markdown 表示のリンクを押すと、部品に渡した opener で開くことを検証する</summary>
    [Fact(DisplayName = "Markdown 表示のリンクを押すと渡した opener で開く")]
    public void ClickingLink_UsesOpener()
    {
        WithView(
            "[docs](https://example.com/)",
            (view, opener) =>
            {
                var box = view.Content.Should().BeOfType<RichTextBox>().Subject;
                var link = Descendants(box.Document).OfType<Hyperlink>().Single();
                link.RaiseEvent(new RoutedEventArgs(Hyperlink.ClickEvent, link));
                opener.Opened.Should().Equal(new Uri("https://example.com/"));
            }
        );
    }

    /// <summary>
    /// 入れ子が深すぎて組み立てられない回答でも、例外を漏らさずその吹き出しだけテキスト表示に落とすことを検証する
    /// （強調の入れ子は組み立て側の上限で、引用・リストの入れ子は Markdig 自身の上限で断られる）。
    /// </summary>
    [Theory(DisplayName = "入れ子が深すぎる回答は落ちずにテキスト表示になる")]
    [InlineData("emphasis")]
    [InlineData("quote")]
    [InlineData("list")]
    public void TooDeeplyNested_FallsBackToText(string kind)
    {
        var depth = 200;
        var text = kind switch
        {
            "emphasis" => new string('*', depth) + "x" + new string('*', depth),
            "quote" => new string('>', depth) + " x",
            _ => string.Concat(
                Enumerable.Range(0, depth).Select(i => new string(' ', i * 2) + "- x\n")
            ),
        };

        WithView(
            text,
            (view, _) =>
            {
                view.IsShowingMarkdown.Should().BeFalse();
                view.Content.Should().BeOfType<TextBox>().Which.Text.Should().Be(text);

                // トグルを切り替えても同じ本文で組み立てを繰り返さず、テキスト表示のまま
                view.ShowMarkdown = false;
                view.ShowMarkdown = true;
                view.IsShowingMarkdown.Should().BeFalse();
            }
        );
    }

    /// <summary>
    /// 組み立て側の入れ子の上限は、強調の入れ子で Markdig より先に効くことを検証する
    /// （上限が無いと、数千段の強調で組み立ての再帰がスタックを使い切ってプロセスごと落ちる）。
    /// </summary>
    [Fact(DisplayName = "強調の入れ子が上限を超える回答は組み立てずに断る")]
    public void Renderer_RejectsDeepEmphasis()
    {
        WpfApplicationTestSupport.RunSta(() =>
        {
            var depth = 200;
            var deep = new string('*', depth) + "x" + new string('*', depth);
            var render = () => ChatMarkdownRenderer.Render(deep, new RecordingOpener());
            render.Should().Throw<InvalidOperationException>();

            // 実際の回答で使う程度の入れ子（リストの中の引用の中の太字とコード）は組み立てられる
            var ordinary = "- item\n  > quote with **bold `code`** and *em*";
            ChatMarkdownRenderer
                .Render(ordinary, new RecordingOpener())
                .Blocks.Should()
                .NotBeEmpty();
        });
    }

    /// <summary>吹き出しの最大幅（AI チャットのダイアログと同じ）</summary>
    private const double BubbleMaxWidth = 600;

    /// <summary>
    /// ダイアログと同じ形の吹き出し（左寄せ・最大幅 600・内側の余白つき）に部品を載せて画面外に表示し、検証する
    /// </summary>
    private static void WithBubble(
        string text,
        Action<Window, AssistantMessageView, RichTextBox> assert
    )
    {
        WpfApplicationTestSupport.RunSta(() =>
        {
            var view = new AssistantMessageView
            {
                Text = text,
                IsStreaming = false,
                ShowMarkdown = true,
                LinkOpener = new RecordingOpener(),
            };
            var bubble = new Border
            {
                Margin = new Thickness(2, 5, 80, 5),
                Padding = new Thickness(13, 9, 13, 9),
                BorderThickness = new Thickness(1),
                HorizontalAlignment = HorizontalAlignment.Left,
                MaxWidth = BubbleMaxWidth,
                Child = view,
            };
            var window = new Window
            {
                Content = new StackPanel { Children = { bubble } },
                Width = 900,
                Height = 600,
                FontFamily = new System.Windows.Media.FontFamily("Segoe UI, Yu Gothic UI, Meiryo"),
                FontSize = 13,
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = -4000,
                Top = -4000,
                ShowActivated = false,
                ShowInTaskbar = false,
            };
            window.Show();

            try
            {
                WpfApplicationTestSupport.DoEvents();
                var box = view.Content.Should().BeOfType<RichTextBox>().Subject;
                assert(window, view, box);
            }
            finally
            {
                window.Close();
            }
        });
    }

    /// <summary>吹き出しの中で本文に使える最大の幅（最大幅から吹き出しの余白と枠を引いたもの）</summary>
    private static double InnerMaxWidth => BubbleMaxWidth - 13 * 2 - 1 * 2;

    /// <summary>
    /// 段落ごとの表示行数を数える（行の先頭を次々にたどる。書体の混ざる行でも 1 行として数える）
    /// </summary>
    private static List<int> LineCounts(FlowDocument document) =>
        document
            .Blocks.OfType<Paragraph>()
            .Select(paragraph =>
            {
                var count = 1;
                var line = paragraph.ContentStart.GetLineStartPosition(0);

                while (line is not null)
                {
                    line = line.GetLineStartPosition(1, out var moved);

                    if (moved != 1 || line is null || line.CompareTo(paragraph.ContentEnd) >= 0)
                    {
                        break;
                    }

                    count++;
                }

                return count;
            })
            .ToList();

    /// <summary>短い 1 行の回答は、吹き出しが最大幅より狭くなり、1 行に収まることを検証する</summary>
    [Fact(DisplayName = "短い 1 行の回答は吹き出しが最大幅より狭くなる")]
    public void ShortAnswer_ShrinksToContent()
    {
        WithBubble(
            "了解しました。",
            (_, _, box) =>
            {
                box.ActualWidth.Should().BeLessThan(InnerMaxWidth / 2);
                LineCounts(box.Document).Should().Equal(1);
            }
        );
    }

    /// <summary>長い段落の回答は、最大幅で折り返すことを検証する</summary>
    [Fact(DisplayName = "長い段落の回答は最大幅で折り返す")]
    public void LongParagraph_UsesMaxWidthAndWraps()
    {
        var text = string.Concat(
            Enumerable.Repeat("この文章は吹き出しの最大幅で折り返されるはずの長い段落です。", 6)
        );

        WithBubble(
            text,
            (_, _, box) =>
            {
                box.ActualWidth.Should().BeApproximately(InnerMaxWidth, 0.5);
                LineCounts(box.Document).Single().Should().BeGreaterThan(1);
            }
        );
    }

    /// <summary>表を含む回答は、内容が短くても最大幅のままにすることを検証する</summary>
    [Fact(DisplayName = "表を含む回答は最大幅のまま")]
    public void Table_KeepsMaxWidth()
    {
        WithBubble(
            "| A | B |\n|--|--|\n| 1 | 2 |",
            (_, _, box) =>
            {
                box.Document.Blocks.OfType<Table>().Should().ContainSingle();
                box.ActualWidth.Should().BeApproximately(InnerMaxWidth, 0.5);
            }
        );
    }

    /// <summary>
    /// 太字・見出し・インラインコードを含む短い回答でも、縮めた幅で文字が次の行へ落ちたり右端が見切れたりしないことを検証する
    /// </summary>
    [Theory(DisplayName = "太字や見出しを含む短い回答でも縮めた幅で文字が欠けない")]
    [InlineData("# 見出しの短い回答")]
    [InlineData("**太字** と `code` と *斜体* の混ざった短い回答です")]
    [InlineData("## Heading **bold** WWW\n2 行目は少しだけ長い行です。WWWWW")]
    [InlineData("[リンク](https://example.com/) で確認してください MMMM")]
    public void FormattedShortAnswer_IsNotClipped(string text)
    {
        WithBubble(
            text,
            (_, _, box) =>
            {
                box.ActualWidth.Should().BeLessThan(InnerMaxWidth, "文章だけの短い回答は縮める");

                // 段落ごとの行数が、元の改行の数どおり（縮めたせいで折り返していない）
                var expected = box
                    .Document.Blocks.OfType<Paragraph>()
                    .Select(p => p.Inlines.OfType<LineBreak>().Count() + 1);
                LineCounts(box.Document).Should().Equal(expected);

                // 最後の文字の右端が、文書の表示領域（部品の幅から内側の余白を引いた範囲）に収まっている
                var last = box.Document.Blocks.LastBlock.ContentEnd.GetInsertionPosition(
                    LogicalDirection.Backward
                );
                var right = last.GetCharacterRect(LogicalDirection.Backward).Right;
                right
                    .Should()
                    .BeLessThanOrEqualTo(box.ActualWidth - box.Document.PagePadding.Right);
            }
        );
    }

    /// <summary>縮めた吹き出しも、ウィンドウを狭めて幅が足りなくなれば、その幅で折り返すことを検証する</summary>
    [Fact(DisplayName = "縮めた吹き出しもウィンドウを狭めればその幅で折り返す")]
    public void ShrunkAnswer_FollowsWindowWidth()
    {
        WithBubble(
            "ウィンドウを狭めたときに折り返すことを確かめるための一行の回答です。",
            (window, _, box) =>
            {
                var wide = box.ActualWidth;
                wide.Should().BeLessThan(InnerMaxWidth);
                LineCounts(box.Document).Should().Equal(1);

                window.Width = 300;
                WpfApplicationTestSupport.DoEvents();

                box.ActualWidth.Should().BeLessThan(wide);
                LineCounts(box.Document).Single().Should().BeGreaterThan(1);
            }
        );
    }
}
