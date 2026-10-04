using System.Text;

namespace QuickER.Provider.Oracle;

/// <summary>テーブル・列の説明を設定する <c>COMMENT ON … IS '…'</c> 文を組み立てる（DDL 生成と同期スクリプトで共有）</summary>
/// <remarks>
/// <para>
/// 出力した DDL・同期スクリプトは SQL*Plus（と同じ規則で読む SQL Developer 等）で流され得る。SQL*Plus は文字列リテラルの
/// 内側でも行単位で入力を読むため、説明の中の改行で文が途中で切れ、後続の行が別の文として実行される。
/// また <c>&amp;</c> は置換変数として扱われ、入力を求めたり後続の行を値として読み込んだりする。
/// <c>COMMENT ON … IS</c> は文字列リテラルしか受け付けず、<c>'a' || CHR(10) || 'b'</c> のような式を書けない。
/// </para>
/// <para>
/// そこで、これらの文字を含む説明だけを PL/SQL 無名ブロックの <c>EXECUTE IMMEDIATE</c> で包み、該当する文字を
/// <c>CHR(n)</c> で連結して出力に生のまま残さない。含まない説明は従来どおり 1 行の <c>COMMENT ON</c> 文のまま
/// （出力はバイト不変）。
/// </para>
/// <para>
/// 包む文字は SQL*Plus 23 での実測で決めた。行の区切りとして扱われたのは LF だけ（CRLF を含む）で、CR 単独・
/// その他の制御文字・U+0085 / U+2028 / U+2029 は文字として保存された。CR は環境やツールの改行変換で行の区切りに
/// 変わり得るため、念のため包む対象に含める。
/// </para>
/// <para>
/// 文字列は 2 段の文字列リテラルの中に入る（PL/SQL のリテラルの中に、動的 SQL のリテラルがある）ため、説明の
/// <c>'</c> は 2 回二重化する（説明の <c>'</c> 1 つが出力では <c>''''</c> になる）。テーブル名・列名も動的 SQL の
/// 文字列の中に入るので、クォートの後にリテラルエスケープを掛ける。連結の各部分は 1 行ずつに分け、文字列の部分は
/// <see cref="MaxSegmentLength"/> 文字ごとに切る（SQL*Plus は 1 行の長さに上限があり、超えた行を無視する）。
/// </para>
/// </remarks>
internal static class OracleCommentStatement
{
    /// <summary>ブロックで包むときに文字列リテラルの 1 部分へ入れる説明の最大文字数</summary>
    /// <remarks>
    /// <c>'</c> の二重化を 2 回受けると最大 4 倍に伸びるため、1 行が SQL*Plus の上限（23 では 4999 文字・
    /// 古い版では 2499 文字）に届かない長さにする。
    /// </remarks>
    internal const int MaxSegmentLength = 200;

    /// <summary><c>COMMENT ON TABLE</c> 文を組み立てる</summary>
    /// <param name="tableName">テーブル名（<c>schema.table</c> 形式も可）</param>
    /// <param name="description">設定する説明（空なら説明の削除＝<c>IS ''</c>）</param>
    public static string ForTable(string tableName, string description) =>
        Build(
            $"TABLE {OracleIdentifier.Quote(tableName)}",
            $"TABLE {OracleIdentifier.QuoteForDynamicSql(tableName)}",
            description
        );

    /// <summary><c>COMMENT ON COLUMN</c> 文を組み立てる</summary>
    /// <param name="tableName">テーブル名（<c>schema.table</c> 形式も可）</param>
    /// <param name="columnName">列名</param>
    /// <param name="description">設定する説明（空なら説明の削除＝<c>IS ''</c>）</param>
    public static string ForColumn(string tableName, string columnName, string description) =>
        Build(
            $"COLUMN {OracleIdentifier.Quote(tableName)}.{OracleIdentifier.QuoteSimple(columnName)}",
            $"COLUMN {OracleIdentifier.QuoteForDynamicSql(tableName)}.{OracleIdentifier.EscapeStringLiteral(OracleIdentifier.QuoteSimple(columnName))}",
            description
        );

    /// <summary>組み立てた文が PL/SQL ブロック（<c>BEGIN</c> 始まり・<c>END;</c> 終わり）かどうか</summary>
    /// <remarks>
    /// <c>;</c> 終端の DDL では、ブロックの後ろに <c>/</c> だけの行を置かないと SQL*Plus が実行しない。
    /// 呼び出し側がその要否を判断するために使う。
    /// </remarks>
    public static bool IsBlock(string statement) =>
        statement.StartsWith("BEGIN", StringComparison.Ordinal);

    /// <summary>説明が SQL*Plus で再解釈される文字（LF・CR・<c>&amp;</c>）を含むかどうか</summary>
    internal static bool NeedsBlock(string description) =>
        description.AsSpan().IndexOfAny('\n', '\r', '&') >= 0;

    /// <summary>説明の内容に応じて、1 行の文か PL/SQL ブロックのどちらかを組み立てる</summary>
    /// <param name="target">素の文で使う対象（<c>TABLE "T"</c> / <c>COLUMN "T"."c"</c>）</param>
    /// <param name="dynamicTarget">動的 SQL の文字列リテラル内で使う対象（リテラルエスケープ済み）</param>
    private static string Build(string target, string dynamicTarget, string description)
    {
        var value = description ?? string.Empty;

        if (!NeedsBlock(value))
        {
            return $"COMMENT ON {target} IS '{OracleIdentifier.EscapeStringLiteral(value)}';";
        }

        var sb = new StringBuilder();
        sb.AppendLine("BEGIN");
        // 動的 SQL の値は「COMMENT ON … IS '」＋説明（' を二重化）＋「'」。PL/SQL のリテラルとしてもう一段二重化する
        sb.AppendLine($"    EXECUTE IMMEDIATE 'COMMENT ON {dynamicTarget} IS '''");

        foreach (var part in SplitParts(value))
        {
            sb.AppendLine($"        || {part}");
        }

        sb.AppendLine("        || '''';");
        sb.Append("END;");
        return sb.ToString();
    }

    /// <summary>説明を連結の部分（文字列リテラルか <c>CHR(n)</c>）へ分ける</summary>
    /// <remarks>
    /// 文字列リテラルの部分は <see cref="MaxSegmentLength"/> 文字ごとに切る（サロゲートペアの途中では切らない）。
    /// </remarks>
    private static IEnumerable<string> SplitParts(string value)
    {
        var segment = new StringBuilder();

        foreach (var c in value)
        {
            if (c is '\n' or '\r' or '&')
            {
                if (segment.Length > 0)
                {
                    yield return Literal(segment.ToString());
                    segment.Clear();
                }

                yield return $"CHR({(int)c})";
                continue;
            }

            // 上限に達したら切る。ただし直前が上位サロゲートなら下位サロゲートと離さない
            if (segment.Length >= MaxSegmentLength && !char.IsHighSurrogate(segment[^1]))
            {
                yield return Literal(segment.ToString());
                segment.Clear();
            }

            segment.Append(c);
        }

        if (segment.Length > 0)
        {
            yield return Literal(segment.ToString());
        }
    }

    /// <summary>説明の一部を、動的 SQL のリテラル用と PL/SQL のリテラル用に 2 回エスケープしたリテラルにする</summary>
    private static string Literal(string text) =>
        $"'{OracleIdentifier.EscapeStringLiteral(OracleIdentifier.EscapeStringLiteral(text))}'";
}
