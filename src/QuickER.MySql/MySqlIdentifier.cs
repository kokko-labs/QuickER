namespace QuickER.MySql;

/// <summary>MySQL の識別子整形ユーティリティ</summary>
/// <remarks>
/// クォート・エスケープ・スキーマ分解などを複数の Builder / Importer で共有する。
/// MySQL は識別子をバッククォート（<c>`</c>）でクォートすることで大文字小文字・予約語衝突を回避する
/// （<see cref="QuickER.PostgreSql.PgIdentifier"/> の二重引用符クォートと対称）。
/// 識別子内の <c>`</c> は <c>``</c> に二重化してエスケープする。常にクォートし大小文字を保持する。
/// </remarks>
public static class MySqlIdentifier
{
    /// <summary>テーブル名を <c>`schema`.`name`</c> または <c>`name`</c> 形式へクォートする</summary>
    public static string Quote(string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return "``";
        }

        // ドットを含む場合はスキーマ修飾名として 2 分割し、各部を個別にクォートする
        if (name.Contains('.'))
        {
            var parts = name.Split('.', 2);
            return $"`{Escape(parts[0])}`.`{Escape(parts[1])}`";
        }

        return $"`{Escape(name)}`";
    }

    /// <summary>カラム名など単一識別子をクォートする</summary>
    public static string QuoteSimple(string name) => $"`{Escape(name)}`";

    /// <summary>識別子内の <c>`</c> を MySQL の規則（二重化）に従ってエスケープする</summary>
    public static string Escape(string name) => (name ?? string.Empty).Replace("`", "``");

    /// <summary>制約名などに使う安全な ID を生成する（"." と空白を "_" へ置換）</summary>
    public static string SafeName(string name) =>
        (name ?? string.Empty).Replace(".", "_").Replace(" ", "_");

    /// <summary><c>schema.table</c> 形式から <c>table</c> 部分のみを抽出する</summary>
    public static string TableNameOnly(string fullName) =>
        string.IsNullOrEmpty(fullName) ? string.Empty
        : fullName.Contains('.') ? fullName.Split('.', 2)[1]
        : fullName;

    /// <summary>
    /// SQL 文字列リテラル用にエスケープする。
    /// MySQL は既定でバックスラッシュもエスケープ文字として解釈するため、
    /// <c>\</c> を <c>\\</c> に、<c>'</c> を <c>''</c> に二重化する。
    /// </summary>
    public static string EscapeStringLiteral(string s) =>
        (s ?? string.Empty).Replace("\\", "\\\\").Replace("'", "''");

    /// <summary>
    /// 列定義に付与するインライン <c>COMMENT</c> 句（前置スペース込み）を組み立てる。
    /// 説明が空・空白のみなら空文字を返す（句を出力しない）。
    /// </summary>
    /// <remarks>
    /// DDL 生成（<c>MySqlDdlGenerator</c>）の列定義末尾と同期スクリプト（<c>MySqlSyncScriptBuilder</c> の
    /// 列定義再指定）で同じインライン COMMENT 表記を共有し、二重定義を避ける。
    /// </remarks>
    public static string ColumnCommentClause(string? description) =>
        string.IsNullOrWhiteSpace(description)
            ? string.Empty
            : $" COMMENT '{EscapeStringLiteral(description)}'";
}
