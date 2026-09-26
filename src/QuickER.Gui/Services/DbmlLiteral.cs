using System.Text;

namespace QuickER.Services;

/// <summary>
/// DBML のリテラル（単一引用符の <c>Note: '…'</c> / <c>note: '…'</c> / <c>name: '…'</c> と、
/// 二重引用符の引用識別子 <c>"列 名"</c>）の相互変換
/// </summary>
/// <remarks>
/// <para>
/// エスケープと復元は必ず対で保つ。バックスラッシュを二重化せずに <c>'</c> だけを <c>\'</c> にすると、
/// 元から <c>\</c> で終わる文字列（例 <c>a\'b</c> の <c>\</c>）とエスケープ由来の <c>\</c> が区別できなくなり、
/// リテラルが 1 文字早く閉じる・説明が壊れるといった往復不能が起きる。
/// </para>
/// <para>
/// 改行は空白 1 つへ畳む（<see cref="ExportTextSanitizer.Sanitize"/>）。DBML の複数行 Note は
/// トリプルクォート <c>'''</c> ブロックだが、<see cref="DbmlImporter"/> は行単位の正規表現で読むため対応しておらず、
/// 単一引用符リテラルへ生の改行を出すと行構造そのものが壊れる（dbdiagram でも構文エラーになる）。
/// 改行を落とす代わりに出力が必ず 1 行に収まることを取る割り切りで、バックスラッシュとクォートは完全に往復する。
/// </para>
/// </remarks>
internal static class DbmlLiteral
{
    /// <summary>
    /// DBML のリテラルへ埋め込める形へエスケープする（引用符自体は呼び出し側が付ける）
    /// </summary>
    /// <param name="text">埋め込む文字列</param>
    /// <param name="quote">囲む引用符。既定は単一引用符、引用識別子では <c>"</c> を渡す</param>
    public static string Escape(string? text, char quote = '\'') =>
        // バックスラッシュを先に二重化する（後続の \' を二重エスケープしないため）
        ExportTextSanitizer.Sanitize(text).Replace("\\", "\\\\").Replace($"{quote}", $"\\{quote}");

    /// <summary>DBML のリテラルの中身を元の文字列へ復元する</summary>
    /// <param name="text">引用符を除いたリテラルの中身</param>
    /// <param name="quote">囲んでいた引用符。<see cref="Escape"/> へ渡した値と対で指定する</param>
    /// <remarks>
    /// 左から 1 パスで <c>\\</c> と <c>\'</c>（引用識別子では <c>\"</c>）だけを解く。
    /// <c>Replace("\\\\", "\\")</c> と <c>Replace("\\'", "'")</c> を順に掛けると、
    /// 解いた結果がもう一方のエスケープに見えて二重に解けてしまう。
    /// </remarks>
    public static string Unescape(string text, char quote = '\'')
    {
        if (!text.Contains('\\'))
        {
            return text;
        }

        var builder = new StringBuilder(text.Length);

        for (var i = 0; i < text.Length; i++)
        {
            // エスケープ対象（\\ と \<引用符>）のときだけ次の 1 文字を素の文字として採用する
            if (
                text[i] == '\\'
                && i + 1 < text.Length
                && (text[i + 1] == '\\' || text[i + 1] == quote)
            )
            {
                i++;
            }

            builder.Append(text[i]);
        }

        return builder.ToString();
    }
}
