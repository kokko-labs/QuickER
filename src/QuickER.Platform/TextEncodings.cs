using System.Text;

namespace QuickER.Platform;

/// <summary>ファイルや子プロセスとやり取りする文字コードの正本</summary>
public static class TextEncodings
{
    /// <summary>
    /// BOM なし UTF-8（不正なサロゲートは置き換えずに例外）。<see cref="AtomicFile"/> の既定で、
    /// <see cref="File.WriteAllText(string, string?)"/> の既定と同じ。
    /// </summary>
    /// <remarks>
    /// 書き出した内容をバイト列で扱う側（内容のハッシュ等）も、このインスタンスで符号化すれば
    /// 書き出しと同じバイト列になる。読み取り側で「UTF-8 として正しいか」を確かめるのにも使える。
    /// </remarks>
    public static Encoding Utf8NoBom { get; } =
        new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    /// <summary>BOM なし UTF-8（不正なサロゲートは例外にせず U+FFFD へ置き換える）</summary>
    /// <remarks>
    /// 利用者の入力や AI の出力をそのまま含む書き出し（生成物・モックの出力・ログ）と、子プロセスの標準入出力向け。
    /// 対になっていないサロゲートが 1 つ混じっただけで書き出し全体を失敗させない。
    /// </remarks>
    public static Encoding Utf8NoBomLenient { get; } =
        new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: false);
}
