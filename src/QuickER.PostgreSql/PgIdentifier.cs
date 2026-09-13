using System.Text;

namespace QuickER.PostgreSql;

/// <summary>PostgreSQL の識別子整形ユーティリティ</summary>
/// <remarks>
/// クォート・エスケープ・スキーマ分解などを複数の Builder / Importer で共有する。
/// PostgreSQL は識別子を二重引用符でクォートすることで大文字小文字を保持する（<see cref="SqlIdentifier.Bracket"/> と対称）。
/// </remarks>
public static class PgIdentifier
{
    /// <summary>テーブル名を <c>"schema"."name"</c> または <c>"name"</c> 形式へクォートする</summary>
    public static string Quote(string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return "\"\"";
        }

        // ドットを含む場合はスキーマ修飾名として 2 分割し、各部を個別にクォートする
        if (name.Contains('.'))
        {
            var parts = name.Split('.', 2);
            return $"\"{Escape(parts[0])}\".\"{Escape(parts[1])}\"";
        }

        return $"\"{Escape(name)}\"";
    }

    /// <summary>カラム名など単一識別子をクォートする</summary>
    public static string QuoteSimple(string name) => $"\"{Escape(name)}\"";

    /// <summary>識別子内の <c>"</c> を PostgreSQL の規則（二重化）に従ってエスケープする</summary>
    public static string Escape(string name) => (name ?? string.Empty).Replace("\"", "\"\"");

    /// <summary>制約名などに使う安全な ID を生成する（"." と空白を "_" へ置換）</summary>
    public static string SafeName(string name) =>
        (name ?? string.Empty).Replace(".", "_").Replace(" ", "_");

    /// <summary><c>schema.table</c> 形式から <c>table</c> 部分のみを抽出する</summary>
    public static string TableNameOnly(string fullName) =>
        string.IsNullOrEmpty(fullName) ? string.Empty
        : fullName.Contains('.') ? fullName.Split('.', 2)[1]
        : fullName;

    /// <summary><c>schema.table</c> 形式から <c>schema</c> 部分を抽出する（省略時は <c>public</c>）</summary>
    public static string SchemaOf(string fullName) =>
        string.IsNullOrEmpty(fullName) ? "public"
        : fullName.Contains('.') ? fullName.Split('.', 2)[0]
        : "public";

    /// <summary>SQL 文字列リテラル用に <c>'</c> を二重化してエスケープする</summary>
    public static string EscapeStringLiteral(string s) => (s ?? string.Empty).Replace("'", "''");

    /// <summary>動的 SQL の文字列リテラル内へ埋め込むテーブル名を、クォート＋リテラルエスケープして返す</summary>
    /// <remarks>
    /// <c>DO $$ … EXECUTE '…' … $$</c> のように組み立てた SQL を文字列リテラルとして渡す経路では、クォートだけでは
    /// 不十分で、名前に含まれる <c>'</c> が外側のリテラルを閉じてしまう。クォートの後にリテラルエスケープを
    /// 掛けたこのメソッドを通すこと（4 方言で同名・同意味のヘルパーを持つ）。
    /// </remarks>
    public static string QuoteForDynamicSql(string name) => EscapeStringLiteral(Quote(name));

    /// <summary>既定のドルクォートタグ（本文と衝突しない限りこれを使う）</summary>
    private const string DefaultDollarQuoteTag = "$$";

    /// <summary>タグを伸ばすときに詰める文字（識別子として妥当な文字を使う）</summary>
    private const char DollarQuoteTagFiller = 'q';

    /// <summary>
    /// <c>DO … ;</c> ブロックの本文を包むドルクォートタグを、本文と衝突しない形で<b>決定的に</b>選ぶ。
    /// </summary>
    /// <param name="body">タグで包む本文（テーブル名など図の名前を埋め込んだ後の完成テキスト）</param>
    /// <returns>本文中に 1 度も現れないドルクォートタグ（既定は <c>$$</c>）</returns>
    /// <remarks>
    /// <para>
    /// ドルクォートは<b>タグ文字列そのもの</b>に出会った時点で終端する。リテラルエスケープ
    /// （<see cref="EscapeStringLiteral"/>）は <c>'</c> しか二重化しないため、<c>$$</c> を含む名前を埋めると
    /// ブロックがそこで閉じ、以降が最上位の SQL 文として解釈される（＝任意の SQL 実行）。
    /// </para>
    /// <para>
    /// タグは既定の <c>$$</c> から始め、本文に現れる間だけ <c>$q$</c> → <c>$qq$</c> … と 1 文字ずつ伸ばす。
    /// 乱数を使わないのは、同じ図からは常に同じスクリプトが出る（＝出力の決定性）ことを保つため。
    /// 本文は有限長なので、タグ長が本文長を超えた時点で必ず衝突しなくなり、この反復は必ず終わる。
    /// </para>
    /// </remarks>
    public static string DollarQuoteTag(string? body)
    {
        if (
            string.IsNullOrEmpty(body)
            || !body.Contains(DefaultDollarQuoteTag, StringComparison.Ordinal)
        )
        {
            return DefaultDollarQuoteTag;
        }

        var filler = new StringBuilder();

        while (true)
        {
            filler.Append(DollarQuoteTagFiller);
            var tag = $"${filler}$";

            if (!body.Contains(tag, StringComparison.Ordinal))
            {
                return tag;
            }
        }
    }
}
