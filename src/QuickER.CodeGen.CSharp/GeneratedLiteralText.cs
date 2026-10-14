using System.Globalization;
using System.Text;

namespace QuickER.CodeGen.CSharp;

/// <summary>
/// 生成コードへ埋め込む文字列リテラルの、文字ごとの扱い（改行になる文字・エスケープして書く文字）の正本
/// </summary>
/// <remarks>
/// <para>
/// 生成物は描画の後に <see cref="string.ReplaceLineEndings(string)"/> で改行を正規化し、UTF-8 でファイルへ書かれる。
/// そのため、リテラルの中に生のまま書いてよい文字は限られる：
/// 改行として認識される文字（<see cref="IsLineBreak"/>）はリテラルを行またぎにして壊し、
/// 対になっていないサロゲートは UTF-8 で表せず U+FFFD に置き換わって値が変わる。
/// 制御文字は値は保たれるが、目に見えず、ほかのツールが扱いを変えることがあるので、これも書かない。
/// </para>
/// <para>
/// 条件式の文字列・生 SQL・図の名前と説明のリテラル、名前の検証（制御文字の判定）は、すべてここを通す
/// （同じ集合を写さない）。
/// </para>
/// </remarks>
internal static class GeneratedLiteralText
{
    /// <summary>
    /// 描画後の <see cref="string.ReplaceLineEndings(string)"/> が改行として認識する文字か
    /// （CR / LF / FORM FEED / NEL / LINE SEPARATOR / PARAGRAPH SEPARATOR）
    /// </summary>
    internal static bool IsLineBreak(char ch) =>
        ch is '\r' or '\n' or '\f' or '\u0085' or '\u2028' or '\u2029';

    /// <summary>
    /// 生成コードのリテラルへ生のまま書けない文字か（C0 制御文字・DEL・改行になる文字・対になっていないサロゲート）
    /// </summary>
    /// <param name="text">文字列</param>
    /// <param name="index">調べる位置</param>
    /// <param name="length">その位置から 1 文字として扱う長さ（正しいサロゲートの対は 2）</param>
    internal static bool MustEscape(string text, int index, out int length)
    {
        var ch = text[index];

        if (
            char.IsHighSurrogate(ch)
            && index + 1 < text.Length
            && char.IsLowSurrogate(text[index + 1])
        )
        {
            length = 2;
            return false;
        }

        length = 1;
        return ch < ' ' || ch == '\u007F' || IsLineBreak(ch) || char.IsSurrogate(ch);
    }

    /// <summary>
    /// 1 文字を C# の通常の文字列リテラル（<c>"..."</c>）の中の書き方へ直す（<see cref="MustEscape"/> の文字・
    /// <c>\</c>・<c>"</c> はエスケープ、それ以外はそのまま）
    /// </summary>
    internal static void AppendRegularLiteralChar(StringBuilder builder, char ch)
    {
        switch (ch)
        {
            case '\\':
                builder.Append("\\\\");
                break;
            case '"':
                builder.Append("\\\"");
                break;
            case '\r':
                builder.Append("\\r");
                break;
            case '\n':
                builder.Append("\\n");
                break;
            case '\t':
                builder.Append("\\t");
                break;
            case '\f':
                builder.Append("\\f");
                break;
            case '\0':
                builder.Append("\\0");
                break;
            default:
                if (ch < ' ' || ch == '\u007F' || IsLineBreak(ch) || char.IsSurrogate(ch))
                {
                    builder
                        .Append("\\u")
                        .Append(((int)ch).ToString("X4", CultureInfo.InvariantCulture));
                }
                else
                {
                    builder.Append(ch);
                }

                break;
        }
    }

    /// <summary>
    /// 文字列の中身を C# の通常の文字列リテラル（<c>"..."</c>）の中の書き方へ直す（引用符は付けない）。
    /// 正しいサロゲートの対はそのまま、対になっていないサロゲートと制御文字は <c>\uXXXX</c> で書く
    /// </summary>
    internal static void AppendRegularLiteralBody(StringBuilder builder, string text)
    {
        for (var i = 0; i < text.Length; i++)
        {
            if (!MustEscape(text, i, out var length) && length == 2)
            {
                builder.Append(text, i, 2);
                i++;
                continue;
            }

            AppendRegularLiteralChar(builder, text[i]);
        }
    }
}
