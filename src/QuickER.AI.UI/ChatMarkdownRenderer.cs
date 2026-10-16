using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using Markdig;
using Markdig.Extensions.Tables;
using Markdig.Helpers;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using QuickER.Platform;
using MdBlock = Markdig.Syntax.Block;
using MdInline = Markdig.Syntax.Inlines.Inline;
using MdTable = Markdig.Extensions.Tables.Table;
using MdTableCell = Markdig.Extensions.Tables.TableCell;
using MdTableRow = Markdig.Extensions.Tables.TableRow;
using WpfBlock = System.Windows.Documents.Block;
using WpfInline = System.Windows.Documents.Inline;
using WpfList = System.Windows.Documents.List;
using WpfTable = System.Windows.Documents.Table;
using WpfTableCell = System.Windows.Documents.TableCell;
using WpfTableRow = System.Windows.Documents.TableRow;

namespace QuickER.AI.UI;

/// <summary>
/// チャットの回答（Markdown）を、読み取り専用の <see cref="System.Windows.Controls.RichTextBox"/> で表示する
/// <see cref="FlowDocument"/> へ組み立てる（AI チャット・AI モック生成のチャット欄が共有する）。
/// </summary>
/// <remarks>
/// <para>
/// 解析は Markdig で行い、構文木を FlowDocument の要素へ写す。対応する要素は見出し・段落・太字/斜体・
/// インラインコード・コードブロック・箇条書き/番号付きリスト・表（パイプテーブル）・引用・区切り線・リンク
/// （自動リンクを含む）。パイプラインは必要な拡張だけを明示的に有効にする（一括有効化は使わない）。
/// </para>
/// <para>
/// AI の回答は外部の入力として扱う。(1) リンクは <see cref="QuickER.Platform.UrlPolicy"/> が通した http / https の絶対 URL だけを
/// クリックできるようにし、それ以外はリンク文字列をただの文字として出す。 (2) 画像は読み込まず代替テキストだけを出す
/// （外部 URL の画像で通信を起こさない）。 (3) 生の HTML は解釈せず文字として出す（解析段階で
/// <c>DisableHtml</c> により HTML として扱わせないうえ、HTML の節点が来ても文字として出す二重化）。
/// </para>
/// <para>
/// 段落内の単一の改行（ソフト改行）は改行として表示する。CommonMark では空白扱いだが、日本語の回答では行の間に
/// 空白が挟まり、AI はリストでない改行を多用するため（GitHub のコメント表示と同じ扱い）。
/// </para>
/// </remarks>
public static class ChatMarkdownRenderer
{
    /// <summary>コード（インライン・ブロック）の等幅フォント</summary>
    private static readonly FontFamily CodeFont = new("Consolas, Courier New");

    /// <summary>コードの背景</summary>
    private static readonly Brush CodeBackground = Freeze(
        new SolidColorBrush(Color.FromRgb(0xF1, 0xF5, 0xF9))
    );

    /// <summary>罫線（コードブロック・表・区切り線）</summary>
    private static readonly Brush RuleBrush = Freeze(
        new SolidColorBrush(Color.FromRgb(0xD8, 0xDE, 0xE6))
    );

    /// <summary>引用の左罫線</summary>
    private static readonly Brush QuoteBarBrush = Freeze(
        new SolidColorBrush(Color.FromRgb(0xC7, 0xD2, 0xDE))
    );

    /// <summary>引用の文字色</summary>
    private static readonly Brush QuoteForeground = Freeze(
        new SolidColorBrush(Color.FromRgb(0x47, 0x55, 0x69))
    );

    /// <summary>表の見出し行の背景</summary>
    private static readonly Brush TableHeaderBackground = Freeze(
        new SolidColorBrush(Color.FromRgb(0xF4, 0xF6, 0xF9))
    );

    /// <summary>リンクの文字色</summary>
    private static readonly Brush LinkForeground = Freeze(
        new SolidColorBrush(Color.FromRgb(0x25, 0x63, 0xEB))
    );

    /// <summary>ブロックどうしの間隔（論理ピクセル）</summary>
    private const double BlockSpacing = 8;

    /// <summary>
    /// 組み立てる構文木の入れ子の深さの上限（ブロックとインラインを合わせた段数）。
    /// これを超える回答は組み立てずに <see cref="InvalidOperationException"/> で断る。
    /// </summary>
    /// <remarks>
    /// Markdig はブロックの入れ子を 128 段で打ち切って例外にするが、強調などインラインの入れ子には上限が無い。
    /// 組み立ては構文木を再帰でたどるので、数千段の強調でスタックを使い切ってプロセスごと落ちる
    /// （スタックの枯渇は例外として捕まえられない）。実際の回答で使う深さ（リストの中の引用の中の太字など）は
    /// 10 段に満たないため、十分な余裕を取ったうえで再帰に入る前に止める。
    /// </remarks>
    internal const int MaxNestingDepth = 32;

    /// <summary>
    /// 飾りの無い文字の Run に付ける目印（隣り合う Run を 1 つにまとめる対象を、等幅などの飾りつきの Run と区別する）
    /// </summary>
    private static readonly object PlainRunTag = new();

    /// <summary>
    /// 文章の段落（本文の段落・見出し）に付ける目印。文書の直下がこの段落だけなら、吹き出しを内容の幅へ縮められる
    /// （<see cref="ChatMarkdownWidth"/> が見る）。
    /// </summary>
    internal static readonly object TextParagraphTag = new();

    /// <summary>
    /// 使う拡張だけを明示したパイプライン（パイプテーブル・URL の自動リンク・HTML の無効化）。
    /// <c>&lt;https://…&gt;</c> 形式の自動リンクは CommonMark の標準なので拡張なしで解析される。
    /// </summary>
    internal static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UsePipeTables()
        .UseAutoLinks()
        .DisableHtml()
        .Build();

    /// <summary>Markdown の文章を FlowDocument へ組み立てる</summary>
    /// <param name="markdown">回答の本文</param>
    /// <param name="linkOpener">リンクをクリックしたときに開く処理</param>
    /// <returns>
    /// 表示用の FlowDocument（左寄せ。ページの余白は 0 にするが、<see cref="System.Windows.Controls.RichTextBox"/> は
    /// 載せた時点で自分の既定の余白へ置き換える）
    /// </returns>
    /// <exception cref="ArgumentException">ブロックの入れ子が深すぎて Markdig が解析を断ったとき</exception>
    /// <exception cref="InvalidOperationException">入れ子が <see cref="MaxNestingDepth"/> を超えるとき</exception>
    /// <remarks>AI の回答は外部の入力なので、呼び出し側は例外を受け止めてテキスト表示へ落とすこと。</remarks>
    public static FlowDocument Render(string markdown, IChatLinkOpener linkOpener)
    {
        var document = new FlowDocument
        {
            PagePadding = new Thickness(0),
            TextAlignment = TextAlignment.Left,
        };

        var parsed = Markdown.Parse(markdown ?? string.Empty, Pipeline);

        if (MeasureDepth(parsed) > MaxNestingDepth)
        {
            throw new InvalidOperationException(
                $"The Markdown is nested more than {MaxNestingDepth} levels deep."
            );
        }

        var context = new RenderContext(linkOpener);
        AddBlocks(document.Blocks, parsed, context);
        TrimLastMargin(document.Blocks);
        return document;
    }

    /// <summary>組み立て中に共有する状態</summary>
    /// <param name="LinkOpener">リンクを開く処理</param>
    private sealed record RenderContext(IChatLinkOpener LinkOpener);

    /// <summary>コンテナの子ブロックを順に組み立てて追加する</summary>
    private static void AddBlocks(
        BlockCollection target,
        ContainerBlock container,
        RenderContext context
    )
    {
        foreach (var block in container)
        {
            if (RenderBlock(block, context) is { } rendered)
            {
                target.Add(rendered);
            }
        }
    }

    /// <summary>Markdig のブロック 1 つを FlowDocument のブロックへ写す（表示しないものは null）</summary>
    private static WpfBlock? RenderBlock(MdBlock block, RenderContext context)
    {
        switch (block)
        {
            case HeadingBlock heading:
                return RenderHeading(heading, context);

            case ParagraphBlock paragraph:
                return RenderInlineParagraph(paragraph.Inline, context);

            case FencedCodeBlock
            or CodeBlock:
                return RenderCodeBlock((LeafBlock)block);

            case HtmlBlock html:
                // HTML は解釈しない（DisableHtml で通常は来ないが、来ても文字として出す）
                return RenderPlainLines(html);

            case ListBlock list:
                return RenderList(list, context);

            case QuoteBlock quote:
                return RenderQuote(quote, context);

            case ThematicBreakBlock:
                return RenderThematicBreak();

            case MdTable table:
                return RenderTable(table, context);

            case LinkReferenceDefinitionGroup:
                // リンクの参照定義は本文として表示しない
                return null;

            case ContainerBlock other:
            {
                var section = new Section();
                AddBlocks(section.Blocks, other, context);
                TrimLastMargin(section.Blocks);
                return section.Blocks.Count == 0 ? null : section;
            }

            default:
                return null;
        }
    }

    /// <summary>見出しを、段に応じた大きさの太字の段落へ写す</summary>
    private static Paragraph RenderHeading(HeadingBlock heading, RenderContext context)
    {
        var paragraph = RenderInlineParagraph(heading.Inline, context);
        paragraph.FontWeight = FontWeights.SemiBold;
        paragraph.FontSize = heading.Level switch
        {
            1 => 18,
            2 => 16,
            3 => 14.5,
            _ => 13,
        };
        paragraph.Margin = new Thickness(0, 4, 0, 6);
        return paragraph;
    }

    /// <summary>インラインの並びを 1 つの段落へ写す</summary>
    private static Paragraph RenderInlineParagraph(ContainerInline? inlines, RenderContext context)
    {
        var paragraph = new Paragraph
        {
            Margin = new Thickness(0, 0, 0, BlockSpacing),
            Tag = TextParagraphTag,
        };
        AddInlines(paragraph.Inlines, inlines, context);
        MergePlainRuns(paragraph.Inlines);
        return paragraph;
    }

    /// <summary>コードブロックを、等幅・背景つきの段落へ写す（行はそのまま改行で区切る）</summary>
    private static Paragraph RenderCodeBlock(LeafBlock code)
    {
        var paragraph = new Paragraph
        {
            FontFamily = CodeFont,
            FontSize = 12,
            Background = CodeBackground,
            BorderBrush = RuleBrush,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(8, 6, 8, 6),
            Margin = new Thickness(0, 0, 0, BlockSpacing),
        };
        AddLines(paragraph.Inlines, code.Lines);
        return paragraph;
    }

    /// <summary>行の並び（HTML ブロック）を、解釈せずに文字として段落へ写す</summary>
    private static Paragraph RenderPlainLines(LeafBlock block)
    {
        var paragraph = new Paragraph { Margin = new Thickness(0, 0, 0, BlockSpacing) };
        AddLines(paragraph.Inlines, block.Lines);
        return paragraph;
    }

    /// <summary>行の並びを Run と改行で追加する</summary>
    private static void AddLines(InlineCollection target, StringLineGroup lines)
    {
        for (var i = 0; i < lines.Count; i++)
        {
            if (i > 0)
            {
                target.Add(new LineBreak());
            }

            target.Add(new Run(lines.Lines[i].Slice.ToString()));
        }
    }

    /// <summary>箇条書き・番号付きリストを写す（番号付きは開始番号を保つ）</summary>
    private static WpfList RenderList(ListBlock listBlock, RenderContext context)
    {
        var list = new WpfList
        {
            MarkerStyle = listBlock.IsOrdered ? TextMarkerStyle.Decimal : TextMarkerStyle.Disc,
            Margin = new Thickness(0, 0, 0, BlockSpacing),
            Padding = new Thickness(20, 0, 0, 0),
        };

        if (
            listBlock.IsOrdered
            && int.TryParse(
                listBlock.OrderedStart,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var start
            )
            && start > 0
        )
        {
            list.StartIndex = start;
        }

        foreach (var child in listBlock)
        {
            var item = new ListItem();

            if (child is ContainerBlock itemBlock)
            {
                AddBlocks(item.Blocks, itemBlock, context);
            }

            // 項目内の段落の下余白は詰める（項目どうしの間が空きすぎないように）
            foreach (var paragraph in item.Blocks.OfType<Paragraph>())
            {
                paragraph.Margin = new Thickness(0, 0, 0, 2);
            }

            list.ListItems.Add(item);
        }

        return list;
    }

    /// <summary>引用を、左罫線つき・淡色のセクションへ写す</summary>
    private static Section RenderQuote(QuoteBlock quote, RenderContext context)
    {
        var section = new Section
        {
            BorderBrush = QuoteBarBrush,
            BorderThickness = new Thickness(3, 0, 0, 0),
            Padding = new Thickness(10, 0, 0, 0),
            Margin = new Thickness(0, 0, 0, BlockSpacing),
            Foreground = QuoteForeground,
        };
        AddBlocks(section.Blocks, quote, context);
        TrimLastMargin(section.Blocks);
        return section;
    }

    /// <summary>区切り線を、下罫線だけを持つ高さの小さい段落で表す</summary>
    private static Paragraph RenderThematicBreak() =>
        new()
        {
            BorderBrush = RuleBrush,
            BorderThickness = new Thickness(0, 0, 0, 1),
            FontSize = 2,
            LineHeight = 2,
            Margin = new Thickness(0, 2, 0, BlockSpacing + 2),
        };

    /// <summary>パイプテーブルを写す（見出し行は太字・背景つき、列の寄せを反映する）</summary>
    private static WpfTable RenderTable(MdTable mdTable, RenderContext context)
    {
        var table = new WpfTable
        {
            CellSpacing = 0,
            BorderBrush = RuleBrush,
            BorderThickness = new Thickness(1, 1, 0, 0),
            Margin = new Thickness(0, 0, 0, BlockSpacing),
        };

        var columnCount = Math.Max(
            mdTable.ColumnDefinitions.Count,
            mdTable.OfType<MdTableRow>().Select(r => r.Count).DefaultIfEmpty(0).Max()
        );

        for (var i = 0; i < columnCount; i++)
        {
            table.Columns.Add(new TableColumn());
        }

        var group = new TableRowGroup();
        table.RowGroups.Add(group);

        foreach (var mdRow in mdTable.OfType<MdTableRow>())
        {
            var row = new WpfTableRow();

            if (mdRow.IsHeader)
            {
                row.FontWeight = FontWeights.SemiBold;
                row.Background = TableHeaderBackground;
            }

            // 列の位置は行内の並びから数える（結合セルは結合した列数ぶん進める）
            var columnIndex = 0;

            foreach (var mdCell in mdRow.OfType<MdTableCell>())
            {
                var cell = new WpfTableCell
                {
                    BorderBrush = RuleBrush,
                    BorderThickness = new Thickness(0, 0, 1, 1),
                    Padding = new Thickness(6, 3, 6, 3),
                    ColumnSpan = Math.Max(1, mdCell.ColumnSpan),
                };
                AddBlocks(cell.Blocks, mdCell, context);
                var alignment = ToTextAlignment(mdTable, columnIndex);
                columnIndex += cell.ColumnSpan;

                foreach (var paragraph in cell.Blocks.OfType<Paragraph>())
                {
                    paragraph.Margin = new Thickness(0);
                    paragraph.TextAlignment = alignment;
                }

                row.Cells.Add(cell);
            }

            group.Rows.Add(row);
        }

        return table;
    }

    /// <summary>表の列の寄せ指定を、段落の寄せへ写す（指定なしは左寄せ）</summary>
    private static TextAlignment ToTextAlignment(MdTable table, int columnIndex)
    {
        if (columnIndex < 0 || columnIndex >= table.ColumnDefinitions.Count)
        {
            return TextAlignment.Left;
        }

        return table.ColumnDefinitions[columnIndex].Alignment switch
        {
            TableColumnAlign.Center => TextAlignment.Center,
            TableColumnAlign.Right => TextAlignment.Right,
            _ => TextAlignment.Left,
        };
    }

    /// <summary>インラインの並びを順に写して追加する</summary>
    private static void AddInlines(
        InlineCollection target,
        ContainerInline? container,
        RenderContext context
    )
    {
        if (container is null)
        {
            return;
        }

        foreach (var inline in container)
        {
            AddInline(target, inline, context);
        }
    }

    /// <summary>Markdig のインライン 1 つを写して追加する（表示しないものは何も足さない）</summary>
    private static void AddInline(InlineCollection target, MdInline inline, RenderContext context)
    {
        switch (inline)
        {
            case LiteralInline literal:
                target.Add(PlainRun(literal.Content.ToString()));
                break;

            case EmphasisInline emphasis:
                target.Add(RenderEmphasis(emphasis, context));
                break;

            case CodeInline code:
                target.Add(
                    new Run(code.Content) { FontFamily = CodeFont, Background = CodeBackground }
                );
                break;

            case LineBreakInline:
                // ソフト改行も改行として出す（型の XmlDoc を参照）
                target.Add(new LineBreak());
                break;

            case LinkInline link:
                AddLink(target, link, context);
                break;

            case AutolinkInline autolink:
                AddAutolink(target, autolink, context);
                break;

            case HtmlInline html:
                // HTML は解釈しない（DisableHtml で通常は来ないが、来ても文字として出す）
                target.Add(PlainRun(html.Tag));
                break;

            case HtmlEntityInline entity:
                target.Add(PlainRun(entity.Transcoded.ToString()));
                break;

            case DelimiterInline delimiter:
                // 対にならなかった強調記号などは、元の文字のまま出す
                target.Add(PlainRun(delimiter.ToLiteral()));
                AddInlines(target, delimiter, context);
                break;

            case ContainerInline other:
                AddInlines(target, other, context);
                break;
        }
    }

    /// <summary>強調を写す（記号 2 つは太字、1 つは斜体）</summary>
    private static Span RenderEmphasis(EmphasisInline emphasis, RenderContext context)
    {
        Span span = emphasis.DelimiterCount >= 2 ? new Bold() : new Italic();
        AddInlines(span.Inlines, emphasis, context);
        return span;
    }

    /// <summary>
    /// リンク・画像を写す。画像は読み込まず代替テキストだけを出す。リンクは開いてよい URL のときだけ
    /// クリックできるようにし、それ以外はリンク文字列をただの文字として出す。
    /// </summary>
    private static void AddLink(InlineCollection target, LinkInline link, RenderContext context)
    {
        if (link.IsImage)
        {
            // 画像は読み込まない（外部 URL への通信を起こさない）。代替テキストだけを文字として出す
            AddInlines(target, link, context);
            return;
        }

        if (!UrlPolicy.TryGetSafeUri(link.Url, out var uri))
        {
            AddInlines(target, link, context);
            return;
        }

        var hyperlink = CreateHyperlink(uri!, context);
        AddInlines(hyperlink.Inlines, link, context);

        if (hyperlink.Inlines.Count == 0)
        {
            hyperlink.Inlines.Add(new Run(uri!.AbsoluteUri));
        }

        target.Add(hyperlink);
    }

    /// <summary>
    /// <c>&lt;…&gt;</c> 形式の自動リンクを写す。開いてよい URL ならリンク、それ以外（メールアドレス等）は文字として出す。
    /// </summary>
    private static void AddAutolink(
        InlineCollection target,
        AutolinkInline autolink,
        RenderContext context
    )
    {
        if (!autolink.IsEmail && UrlPolicy.TryGetSafeUri(autolink.Url, out var uri))
        {
            var hyperlink = CreateHyperlink(uri!, context);
            hyperlink.Inlines.Add(new Run(autolink.Url));
            target.Add(hyperlink);
            return;
        }

        target.Add(PlainRun(autolink.Url));
    }

    /// <summary>
    /// クリックで <see cref="IChatLinkOpener"/> を呼ぶリンクを作る。
    /// </summary>
    /// <remarks>
    /// <see cref="Hyperlink.NavigateUri"/> は設定しない。設定すると WPF の既定のナビゲーション経路
    /// （RequestNavigate・ナビゲーションホスト）が開く処理を担い得るため、開く経路を opener の 1 本に限る。
    /// ツールチップは実際に開く URL を出す（表示文字列と行き先の食い違いを確かめられるように）。ホストは
    /// <see cref="UrlPolicy.ToDisplayText"/> で punycode にする（見た目の似た別の文字のホストを見分けられるように）。
    /// </remarks>
    private static Hyperlink CreateHyperlink(Uri uri, RenderContext context)
    {
        var hyperlink = new Hyperlink
        {
            ToolTip = UrlPolicy.ToDisplayText(uri),
            Foreground = LinkForeground,
        };
        var opener = context.LinkOpener;
        hyperlink.Click += (_, e) =>
        {
            opener.Open(uri);
            e.Handled = true;
        };
        return hyperlink;
    }

    /// <summary>構文木の入れ子の深さを、再帰を使わずに測る（深い入力でスタックを使わないため）</summary>
    internal static int MeasureDepth(MarkdownDocument document)
    {
        var maxDepth = 0;
        var pending = new Stack<(MarkdownObject Node, int Depth)>();
        pending.Push((document, 0));

        while (pending.Count > 0)
        {
            var (node, depth) = pending.Pop();
            maxDepth = Math.Max(maxDepth, depth);

            switch (node)
            {
                case ContainerBlock container:
                    foreach (var child in container)
                    {
                        pending.Push((child, depth + 1));
                    }

                    break;

                case LeafBlock { Inline: { } inline }:
                    pending.Push((inline, depth + 1));
                    break;

                case ContainerInline containerInline:
                    for (
                        var child = containerInline.FirstChild;
                        child is not null;
                        child = child.NextSibling
                    )
                    {
                        pending.Push((child, depth + 1));
                    }

                    break;
            }
        }

        return maxDepth;
    }

    /// <summary>飾りの無い文字の Run を作る（隣り合うものは <see cref="MergePlainRuns"/> が 1 つにまとめる）</summary>
    private static Run PlainRun(string text) => new(text) { Tag = PlainRunTag };

    /// <summary>
    /// 隣り合う飾りの無い Run を 1 つにまとめる（入れ子の Span の中も同じようにまとめる）。
    /// </summary>
    /// <remarks>
    /// 対にならない記号（<c>[</c> など）は Markdig が 1 文字ずつ別の節点にするため、そのまま写すと短い Run が
    /// 数千並ぶ。リンクの中で区切りの無い短い Run が数千並ぶと、WPF のレイアウトは数十秒から数分かかる
    /// （実測: 4,000 個で約 30 秒）。1 つの Run にまとめれば同じ文字でも一瞬で済む。
    /// </remarks>
    private static void MergePlainRuns(InlineCollection inlines)
    {
        Run? head = null;
        StringBuilder? merged = null;

        foreach (var inline in inlines.ToList())
        {
            if (inline is Run run && ReferenceEquals(run.Tag, PlainRunTag))
            {
                if (head is null)
                {
                    head = run;
                    continue;
                }

                merged ??= new StringBuilder(head.Text);
                merged.Append(run.Text);
                inlines.Remove(run);
                continue;
            }

            Flush();

            if (inline is Span span)
            {
                MergePlainRuns(span.Inlines);
            }
        }

        Flush();

        // まとめた文字を先頭の Run へ書き戻し、次のまとまりに備える
        void Flush()
        {
            if (head is not null && merged is not null)
            {
                head.Text = merged.ToString();
            }

            head = null;
            merged = null;
        }
    }

    /// <summary>並びの最後のブロックの下余白を消す（吹き出しの下に余分な隙間を作らない）</summary>
    private static void TrimLastMargin(BlockCollection blocks)
    {
        if (blocks.LastBlock is { } last)
        {
            var margin = last.Margin;
            last.Margin = new Thickness(margin.Left, margin.Top, margin.Right, 0);
        }
    }

    /// <summary>ブラシを凍結して返す（スレッド間で共有するため）</summary>
    private static Brush Freeze(Brush brush)
    {
        brush.Freeze();
        return brush;
    }
}
