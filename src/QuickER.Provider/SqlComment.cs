namespace QuickER.Provider;

/// <summary>SQL の <c>--</c> 行コメントへユーザーデータを載せるときのサニタイズを担う</summary>
/// <remarks>
/// <para>
/// 図の名前（テーブル名・列名・制約名）と説明は GUI の自由入力・DB 取込・DBML/Excel 取込・MCP/AI 経由の
/// 任意文字列であり、改行を含み得る。<c>--</c> は行末までを読み飛ばす行コメントなので、改行がそのまま乗ると
/// コメントが途中で終わり、2 行目以降が実行される SQL として解釈される（＝コメント行の突き破り）。
/// </para>
/// <para>
/// <c>--</c> 行コメントへユーザーデータを埋める箇所は DDL 生成・同期スクリプト生成の双方に散っているため、
/// 置換規則をここへ 1 本化する。enum 由来の固定文（セクション見出しの種別名など）は対象外でよい。
/// </para>
/// </remarks>
public static class SqlComment
{
    /// <summary>C0 制御文字の上限（この値未満を制御文字とみなす。空白 U+0020 は含まない）</summary>
    private const char ControlCharacterLimit = ' ';

    /// <summary>DEL（U+007F）。C0 制御文字の範囲外だが表示が壊れるため同じく畳む</summary>
    private const char Delete = (char)0x7F;

    /// <summary>NEXT LINE（U+0085）。C0 の外側だが C# ・多くの表示系が改行として扱う</summary>
    private const char NextLine = (char)0x85;

    /// <summary>LINE SEPARATOR（U+2028）</summary>
    private const char LineSeparator = (char)0x2028;

    /// <summary>PARAGRAPH SEPARATOR（U+2029）</summary>
    private const char ParagraphSeparator = (char)0x2029;

    /// <summary>行コメントへ載せる文字列から改行・制御文字を取り除く（それぞれ空白 1 つへ畳む）</summary>
    /// <remarks>
    /// 改行（CR / LF）に加えて、端末やエディタで表示が壊れる制御文字（U+0000〜U+001F・U+007F）も空白へ畳む。
    /// タブも制御文字として空白 1 つになる。連続する制御文字は畳まず、1 文字につき空白 1 つへ置き換える
    /// （置換後の文字数が元と一致し、位置のずれで別の解釈が生まれないため）。<c>null</c> は空文字を返す。
    /// </remarks>
    public static string Sanitize(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        // 制御文字を 1 文字も含まない（大多数の）入力では新しい文字列を作らずそのまま返す
        if (!ContainsControlCharacter(text))
        {
            return text;
        }

        return string.Create(
            text.Length,
            text,
            static (destination, source) =>
            {
                for (var i = 0; i < source.Length; i++)
                {
                    destination[i] = IsControlCharacter(source[i]) ? ' ' : source[i];
                }
            }
        );
    }

    /// <summary>文字列が改行・制御文字（U+0000〜U+001F・U+007F・U+0085・U+2028・U+2029）を含むか</summary>
    /// <remarks>
    /// 生成前診断（名前の改行・制御文字を Error にする判定）・入口の名前検証（<see cref="SqlNameText"/>）・
    /// サニタイズが同じ規則を共有する。C0 と DEL に加えて Unicode の行区切り 3 種を含むのは、これらが
    /// C# の new-line であり（<c>///</c> コメントや 1 行リテラルを行またぎで壊す）、表示系でも改行になるため。
    /// </remarks>
    public static bool ContainsControlCharacter(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        foreach (var ch in text)
        {
            if (IsControlCharacter(ch))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>C0 制御文字（U+0000〜U+001F）・DEL（U+007F）・Unicode の行区切り（U+0085 / U+2028 / U+2029）か</summary>
    private static bool IsControlCharacter(char ch) =>
        ch < ControlCharacterLimit
        || ch == Delete
        || ch == NextLine
        || ch == LineSeparator
        || ch == ParagraphSeparator;
}
