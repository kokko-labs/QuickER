using QuickER.Model;

namespace QuickER.Provider.Sqlite;

/// <summary>SQLite の識別子整形ユーティリティ</summary>
/// <remarks>
/// クォート・エスケープ・スキーマ分解などを複数の Builder / Importer で共有する。
/// SQLite は識別子を二重引用符でクォートすることで大文字小文字・記号を保持する
/// （<see cref="SqlIdentifier.Bracket"/> 相当。PostgreSQL の <c>PgIdentifier</c> と対称）。
/// SQLite にはスキーマ修飾（<c>schema.table</c>）の概念が実質無いが、SQL Server 由来のスキーマ付き
/// テーブル名を受け取った場合でも壊れないよう、ドット分割クォートに対応する。
/// <para>
/// <b>この分割クォートは DDL（<c>CREATE TABLE</c> / <c>REFERENCES</c> 等）の <c>Quote</c> 専用</b>で、
/// <c>PRAGMA table_xinfo(...)</c> / <c>index_list(...)</c> / <c>index_info(...)</c> /
/// <c>foreign_key_list(...)</c> のように「1 つの識別子を書く場所」では使わないこと。PRAGMA の引数は
/// スキーマ修飾ではなく<b>単一のテーブル名／インデックス名そのもの</b>（取込範囲は単一 DB のため）で、
/// ドットは名前の一部でしかない。<c>Quote</c> を通すとドットのある名前（<c>"a.b"</c> テーブル等）が
/// <c>PRAGMA table_xinfo("a"."b")</c> のように誤って 2 分割クォートされ、構文エラーで取込全体が失敗する。
/// PRAGMA の引数には <see cref="QuoteSimple"/> を使うこと。
/// </para>
/// </remarks>
public static class SqliteIdentifier
{
    /// <summary>テーブル名を <c>"schema"."name"</c> または <c>"name"</c> 形式へクォートする（DDL 専用）</summary>
    /// <remarks>
    /// ドットをスキーマ区切りとして 2 分割する。PRAGMA の引数のように「ドットも含めた 1 つの識別子」を
    /// 渡す場所では使わないこと（<see cref="QuoteSimple"/> を使う）。
    /// </remarks>
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

    /// <summary>
    /// 単一識別子をクォートする（ドットを分割しない）。カラム名や、<c>PRAGMA</c> の引数のように
    /// 「ドットも含めて 1 つの名前」を書く場所（<c>table_xinfo</c> / <c>index_list</c> / <c>index_info</c> /
    /// <c>foreign_key_list</c> の引数）に使う。
    /// </summary>
    public static string QuoteSimple(string name) => $"\"{Escape(name)}\"";

    /// <summary>識別子内の <c>"</c> を SQLite の規則（二重化）に従ってエスケープする</summary>
    public static string Escape(string name) => (name ?? string.Empty).Replace("\"", "\"\"");

    /// <summary>制約名などに使う安全な ID を生成する（規則の正本は <see cref="ConstraintNames.SafeName"/>）</summary>
    public static string SafeName(string name) => ConstraintNames.SafeName(name);

    /// <summary><c>schema.table</c> 形式から <c>table</c> 部分のみを抽出する</summary>
    public static string TableNameOnly(string fullName) =>
        string.IsNullOrEmpty(fullName) ? string.Empty
        : fullName.Contains('.') ? fullName.Split('.', 2)[1]
        : fullName;

    /// <summary>SQL 文字列リテラル用に <c>'</c> を二重化してエスケープする</summary>
    public static string EscapeStringLiteral(string s) => (s ?? string.Empty).Replace("'", "''");
}
