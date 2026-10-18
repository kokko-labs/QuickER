using QuickER.Dialects;
using QuickER.Model;

namespace QuickER.Provider.MySql;

/// <summary>MySQL の識別子整形ユーティリティ</summary>
/// <remarks>
/// クォート・エスケープ・スキーマ分解などを複数の Builder / Importer で共有する。
/// MySQL は識別子をバッククォート（<c>`</c>）でクォートすることで大文字小文字・予約語衝突を回避する
/// （<see cref="QuickER.Provider.PostgreSql.PgIdentifier"/> の二重引用符クォートと対称）。
/// 識別子内の <c>`</c> は <c>``</c> に二重化してエスケープする。常にクォートし大小文字を保持する。
/// </remarks>
public static class MySqlIdentifier
{
    /// <summary>識別子の長さの上限（64 文字と、表の名前のファイル名での 251 バイト。値の正本は <see cref="IdentifierLengthLimits"/>）</summary>
    public static IdentifierLengthLimit Limit => IdentifierLengthLimits.MySql;

    /// <summary>テーブル名を <c>`schema`.`name`</c> または <c>`name`</c> 形式へクォートする</summary>
    public static string Quote(string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return "``";
        }

        // ドットを含む場合はスキーマ修飾名として 2 分割し、各部を個別にクォートする
        if (SchemaQualifiedName.TrySplit(name, out var schema, out var table))
        {
            return $"`{Escape(schema)}`.`{Escape(table)}`";
        }

        return $"`{Escape(name)}`";
    }

    /// <summary>カラム名など単一識別子をクォートする</summary>
    public static string QuoteSimple(string name) => $"`{Escape(name)}`";

    /// <summary>識別子内の <c>`</c> を MySQL の規則（二重化）に従ってエスケープする</summary>
    public static string Escape(string name) => (name ?? string.Empty).Replace("`", "``");

    /// <summary><c>schema.table</c> 形式から <c>table</c> 部分のみを抽出する</summary>
    public static string TableNameOnly(string fullName) =>
        string.IsNullOrEmpty(fullName) ? string.Empty : SchemaQualifiedName.Split(fullName).Name;

    /// <summary><c>schema.table</c> 形式から <c>schema</c> 部分を抽出する（無修飾なら <c>null</c>）</summary>
    /// <remarks>
    /// <see cref="TableNameOnly"/> と対で使う。分割規則（最初の <c>.</c> で 2 分割）は
    /// <see cref="Quote"/> / <see cref="TableNameOnly"/> と同一で、両者を合わせると元の名前に戻る。
    /// <c>information_schema</c> を引く側は、この戻り値が <c>null</c> ならカレント DB
    /// （<c>DATABASE()</c>）を、非 <c>null</c> ならその名前を検索スコープにする
    /// （さもないと修飾名の操作対象と検索スコープが食い違い、別スキーマの同名テーブルを引き当てる）。
    /// </remarks>
    public static string? SchemaNameOnly(string fullName) =>
        string.IsNullOrEmpty(fullName) ? null : SchemaQualifiedName.Split(fullName).Schema;

    /// <summary>
    /// SQL 文字列リテラル用にエスケープする。
    /// MySQL は既定でバックスラッシュもエスケープ文字として解釈するため、
    /// <c>\</c> を <c>\\</c> に、<c>'</c> を <c>''</c> に二重化する。
    /// </summary>
    public static string EscapeStringLiteral(string s) =>
        (s ?? string.Empty).Replace("\\", "\\\\").Replace("'", "''");

    /// <summary>動的 SQL の文字列リテラル内へ埋め込むテーブル名を、クォート＋リテラルエスケープして返す</summary>
    /// <remarks>
    /// <c>SET @sql = IF(…, '…')</c> / <c>CONCAT('…', @fk, '…')</c> のように組み立てた SQL を文字列リテラルとして
    /// 渡す経路では、クォートだけでは不十分で、名前に含まれる <c>'</c>（および MySQL では <c>\</c>）が
    /// 外側のリテラルを壊してしまう。クォートの後にリテラルエスケープを掛けたこのメソッドを通すこと
    /// （4 方言で同名・同意味のヘルパーを持つ）。
    /// </remarks>
    public static string QuoteForDynamicSql(string name) => EscapeStringLiteral(Quote(name));

    /// <summary>
    /// 動的 SQL の文字列リテラル内へ埋め込む単一識別子（列名など）を、クォート＋リテラルエスケープして返す。
    /// </summary>
    /// <remarks>
    /// <see cref="QuoteForDynamicSql"/> の単一識別子版。列名はドットを含み得るが、テーブル名と違って
    /// スキーマ修飾ではないため <see cref="QuoteSimple"/>（分割しないクォート）を土台にする。
    /// </remarks>
    public static string QuoteSimpleForDynamicSql(string name) =>
        EscapeStringLiteral(QuoteSimple(name));

    /// <summary>
    /// 列定義に付与するインライン <c>COMMENT</c> 句（前置スペース込み）を組み立てる。
    /// 説明が空・空白のみなら空文字を返す（句を出力しない）。
    /// </summary>
    /// <remarks>
    /// DDL 生成（<c>MySqlDdlGenerator</c>）の列定義末尾と同期スクリプト（<c>MySqlSyncScriptBuilder</c> の
    /// 列定義再指定）で同じインライン COMMENT 表記を共有し、二重定義を避ける。
    /// </remarks>
    public static string ColumnCommentClause(string? description) =>
        !DescriptionText.IsPresent(description)
            ? string.Empty
            : $" COMMENT '{EscapeStringLiteral(description)}'";
}
