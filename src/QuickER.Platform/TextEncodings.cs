using System.Text;

namespace QuickER.Platform;

/// <summary>ファイルへ書き出す文字コードの正本</summary>
public static class TextEncodings
{
    /// <summary>
    /// BOM なし UTF-8（不正なサロゲートは置き換えずに例外）。<see cref="AtomicFile"/> の既定で、
    /// <see cref="File.WriteAllText(string, string?)"/> の既定と同じ。
    /// </summary>
    /// <remarks>
    /// 書き出した内容をバイト列で扱う側（内容のハッシュ等）も、このインスタンスで符号化すれば
    /// 書き出しと同じバイト列になる。
    /// </remarks>
    public static Encoding Utf8NoBom { get; } =
        new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
}
