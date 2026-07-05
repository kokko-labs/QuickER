namespace QuickER.Sqlite;

/// <summary>SQLite の識別子整形ユーティリティ</summary>
/// <remarks>
/// クォート・エスケープ・スキーマ分解などを複数の Builder / Importer で共有する。
/// SQLite は識別子を二重引用符でクォートすることで大文字小文字・記号を保持する
/// （<see cref="SqlIdentifier.Bracket"/> 相当。PostgreSQL の <c>PgIdentifier</c> と対称）。
/// SQLite にはスキーマ修飾（<c>schema.table</c>）の概念が実質無いが、SQL Server 由来のスキーマ付き
/// テーブル名を受け取った場合でも壊れないよう、ドット分割クォートに対応する。
/// </remarks>
public static class SqliteIdentifier
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

    /// <summary>識別子内の <c>"</c> を SQLite の規則（二重化）に従ってエスケープする</summary>
    public static string Escape(string name) => (name ?? string.Empty).Replace("\"", "\"\"");

    /// <summary>制約名などに使う安全な ID を生成する（"." と空白を "_" へ置換）</summary>
    public static string SafeName(string name) =>
        (name ?? string.Empty).Replace(".", "_").Replace(" ", "_");

    /// <summary><c>schema.table</c> 形式から <c>table</c> 部分のみを抽出する</summary>
    public static string TableNameOnly(string fullName) =>
        string.IsNullOrEmpty(fullName) ? string.Empty
        : fullName.Contains('.') ? fullName.Split('.', 2)[1]
        : fullName;

    /// <summary>SQL 文字列リテラル用に <c>'</c> を二重化してエスケープする</summary>
    public static string EscapeStringLiteral(string s) => (s ?? string.Empty).Replace("'", "''");
}
