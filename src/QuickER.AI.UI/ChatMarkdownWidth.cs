using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace QuickER.AI.UI;

/// <summary>
/// Markdown 表示の文書が、折り返さずに収まる自然な幅を測る（短い回答の吹き出しを内容の幅へ縮めるため）。
/// </summary>
/// <remarks>
/// <para>
/// <see cref="RichTextBox"/> は内容の幅に縮まず、与えられた幅いっぱいに広がる。テキスト表示の吹き出しは内容の幅なので、
/// そのままでは「了解しました。」のような短い回答だけが最大幅の吹き出しになる。
/// </para>
/// <para>
/// 測るのは、文書の直下が文章の段落（本文の段落・見出し）だけのときに限る。表・コードブロック・リスト・引用・区切り線を含む
/// 文書と、写し取れないインライン要素を含む段落は測らずに null を返し、呼び出し側は最大幅のままにする
/// （測り損ねて狭くすると文字が欠ける。広すぎるほうがまし）。
/// </para>
/// <para>
/// 段落ごとに、同じ書式（段落の大きさ・太さ、太字・斜体、インラインコードの等幅など）のインラインを
/// 折り返さない <see cref="TextBlock"/> へ写して測り、改行で区切られた行のうち最も長いものの幅を取る。
/// </para>
/// </remarks>
internal static class ChatMarkdownWidth
{
    /// <summary>写し取る文字書式のプロパティ（幅に効くもの）</summary>
    private static readonly DependencyProperty[] FontProperties =
    [
        TextElement.FontFamilyProperty,
        TextElement.FontSizeProperty,
        TextElement.FontWeightProperty,
        TextElement.FontStyleProperty,
        TextElement.FontStretchProperty,
    ];

    /// <summary>文書の自然な幅を測る</summary>
    /// <param name="document">表示中の文書（書式の継承を正しく読むため、表示部品に載った状態で渡す）</param>
    /// <param name="formattingMode">表示部品の文字の描画方式（測る側も同じ方式にする）</param>
    /// <returns>折り返さずに収まる幅。測れないときは null</returns>
    public static double? Measure(FlowDocument document, TextFormattingMode formattingMode)
    {
        var width = 0.0;

        foreach (var block in document.Blocks)
        {
            if (
                block is not Paragraph paragraph
                || !ReferenceEquals(paragraph.Tag, ChatMarkdownRenderer.TextParagraphTag)
            )
            {
                return null;
            }

            var measured = MeasureParagraph(paragraph, formattingMode);

            if (measured is null)
            {
                return null;
            }

            width = Math.Max(
                width,
                measured.Value
                    + paragraph.Margin.Left
                    + paragraph.Margin.Right
                    + paragraph.Padding.Left
                    + paragraph.Padding.Right
            );
        }

        return width;
    }

    /// <summary>段落 1 つの、折り返さない幅を測る（写し取れないインラインがあれば null）</summary>
    private static double? MeasureParagraph(Paragraph paragraph, TextFormattingMode formattingMode)
    {
        var probe = new TextBlock
        {
            TextWrapping = TextWrapping.NoWrap,
            FontFamily = paragraph.FontFamily,
            FontSize = paragraph.FontSize,
            FontWeight = paragraph.FontWeight,
            FontStyle = paragraph.FontStyle,
            FontStretch = paragraph.FontStretch,
        };
        TextOptions.SetTextFormattingMode(probe, formattingMode);

        foreach (var inline in paragraph.Inlines)
        {
            if (Clone(inline) is not { } copy)
            {
                return null;
            }

            probe.Inlines.Add(copy);
        }

        probe.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        return probe.DesiredSize.Width;
    }

    /// <summary>
    /// インラインを、幅に効く書式だけを保って写す。組み立てが作る種類（Run・改行・太字・斜体・リンク）以外は null。
    /// </summary>
    private static Inline? Clone(Inline inline)
    {
        Inline? copy = inline switch
        {
            Run run => new Run(run.Text),
            LineBreak => new LineBreak(),
            Bold => new Bold(),
            Italic => new Italic(),
            // リンクは下線と色だけで幅は変わらないため、同じ書式の Span として測る
            Hyperlink => new Span(),
            Span span when span.GetType() == typeof(Span) => new Span(),
            _ => null,
        };

        if (copy is null)
        {
            return null;
        }

        foreach (var property in FontProperties)
        {
            var local = inline.ReadLocalValue(property);

            if (local != DependencyProperty.UnsetValue)
            {
                copy.SetValue(property, local);
            }
        }

        if (inline is Span source && copy is Span target)
        {
            foreach (var child in source.Inlines)
            {
                if (Clone(child) is not { } childCopy)
                {
                    return null;
                }

                target.Inlines.Add(childCopy);
            }
        }

        return copy;
    }
}
