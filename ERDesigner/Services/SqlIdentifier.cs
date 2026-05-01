using System;

namespace ERDesigner.Services;

/// <summary>
/// SQL Server の識別子整形ユーティリティ (Bracket / SafeName / NormalizeTable など)。
/// 複数の Builder/Importer で共有します。
/// </summary>
internal static class SqlIdentifier
{
    /// <summary>
    /// テーブル名を <c>[schema].[name]</c> または <c>[name]</c> 形式に括弧付けします。
    /// </summary>
    public static string Bracket(string name)
    {
        if (string.IsNullOrEmpty(name)) return "[]";
        if (name.Contains('.'))
        {
            var parts = name.Split('.', 2);
            return $"[{Escape(parts[0])}].[{Escape(parts[1])}]";
        }
        return $"[{Escape(name)}]";
    }

    /// <summary>カラム名など単一識別子の括弧付け。</summary>
    public static string BracketSimple(string name) => $"[{Escape(name)}]";

    /// <summary>
    /// 識別子内の <c>]</c> を SQL Server の規則に従ってエスケープします。
    /// </summary>
    public static string Escape(string name) => (name ?? string.Empty).Replace("]", "]]");

    /// <summary>
    /// 制約名等に使う安全な ID。"." と空白を "_" に置換。
    /// </summary>
    public static string SafeName(string name)
        => (name ?? string.Empty).Replace(".", "_").Replace(" ", "_");

    /// <summary>
    /// <c>schema.table</c> 形式から <c>table</c> 部分のみを抽出。
    /// </summary>
    public static string TableNameOnly(string fullName)
        => string.IsNullOrEmpty(fullName) ? string.Empty
            : fullName.Contains('.') ? fullName.Split('.', 2)[1]
            : fullName;

    /// <summary>
    /// <c>schema.table</c> 形式から <c>schema</c> 部分のみを抽出。省略時は <c>dbo</c>。
    /// </summary>
    public static string SchemaOf(string fullName)
        => string.IsNullOrEmpty(fullName) ? "dbo"
            : fullName.Contains('.') ? fullName.Split('.', 2)[0]
            : "dbo";

    /// <summary>SQL 文字列リテラル用に <c>'</c> をエスケープします。</summary>
    public static string EscapeStringLiteral(string s) => (s ?? string.Empty).Replace("'", "''");
}
