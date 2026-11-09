using System.Text;

namespace QuickER.Mcp;

/// <summary>
/// ツールの応答へ利用者の文を埋め込むときの共通処理（MCP のツールホストと内蔵チャットのツールが共有する）
/// </summary>
public static class ToolResponseText
{
    /// <summary>改行を空白 1 つへ畳み、文を 1 行にする</summary>
    /// <param name="text">埋め込む文（表・列の説明など）</param>
    /// <returns>改行（CR LF の組は 1 つ・CR・LF・U+0085・U+2028・U+2029）をそれぞれ空白 1 つへ置き換えた文</returns>
    /// <remarks>
    /// 要約は「1 行が 1 件の記録」の形で、説明は見出しの下の 1 行や列の行の末尾に置く。改行をそのまま残すと、説明の続きが行頭に出て
    /// 偽の見出しや偽の列の行に読める。改行の前後の空白は残し、ほかの文字は変えない。
    /// </remarks>
    public static string ToSingleLine(string text)
    {
        if (text.AsSpan().IndexOfAny("\r\n\u0085\u2028\u2029") < 0)
        {
            return text;
        }

        var sb = new StringBuilder(text.Length);

        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];

            switch (c)
            {
                case '\r':
                    sb.Append(' ');

                    // CR LF の組は 1 つの改行として畳む
                    if (i + 1 < text.Length && text[i + 1] == '\n')
                    {
                        i++;
                    }

                    break;
                case '\n' or '\u0085' or '\u2028' or '\u2029':
                    sb.Append(' ');
                    break;
                default:
                    sb.Append(c);
                    break;
            }
        }

        return sb.ToString();
    }
}
