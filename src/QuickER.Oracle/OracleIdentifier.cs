namespace QuickER.Oracle;

/// <summary>Oracle の識別子整形ユーティリティ</summary>
/// <remarks>
/// クォート・エスケープ・スキーマ分解などを複数の Builder / Importer で共有する。
/// Oracle は識別子を二重引用符でクォートすると大文字小文字がそのまま保持されるため、本ツールでは常に
/// クォートして図の名前をそのまま維持する（<see cref="PgIdentifier"/> と同じ二重引用符方式）。
/// なお Oracle でクォートせずに作成した既存 DB 上の識別子は大文字へ畳み込まれて格納される点に注意が必要で、
/// そうしたオブジェクトは取込時に大文字の名前として取り込まれる。
/// </remarks>
public static class OracleIdentifier
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

    /// <summary>識別子内の <c>"</c> を Oracle の規則（二重化）に従ってエスケープする</summary>
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
