using QuickER.Model;
using QuickER.Provider;

namespace QuickER.MySql;

/// <summary>
/// ER 図から MySQL 向けの DDL（<c>CREATE TABLE</c> / <c>ALTER TABLE ... ADD CONSTRAINT</c>）を生成する
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item>識別子は <see cref="MySqlIdentifier"/> でバッククォートクォートする（<c>schema.table</c> は <c>`schema`.`table`</c> に分割、<c>`</c> は <c>``</c> にエスケープ）</item>
///   <item>MySQL 8.0 の既定ストレージエンジンは InnoDB のため <c>ENGINE</c> 句は書かない（他方言との対称性）</item>
///   <item>テーブル・列の説明は MySQL の自然な流儀で出力する＝列は列定義インラインの <c>COMMENT '…'</c>、
///     テーブルは <c>CREATE TABLE</c> 閉じ括弧後の <c>COMMENT='…'</c> 句（取込が <c>COMMENT</c> を読むため往復する）</item>
///   <item>共通の DDL 組み立てロジックは <see cref="DdlGeneratorBase"/> を参照</item>
/// </list>
/// </remarks>
public sealed class MySqlDdlGenerator : DdlGeneratorBase
{
    /// <inheritdoc />
    protected override string QuoteQualifiedName(string name) => MySqlIdentifier.Quote(name);

    /// <inheritdoc />
    protected override string QuoteSimpleName(string name) => MySqlIdentifier.QuoteSimple(name);

    /// <inheritdoc />
    protected override string SafeName(string name) => MySqlIdentifier.SafeName(name);

    /// <inheritdoc />
    protected override string QuoteConstraintName(string constraintName) =>
        $"`{MySqlIdentifier.Escape(constraintName)}`";

    /// <summary>列定義末尾へインライン <c>COMMENT '…'</c> を付ける（説明が空なら何も付けない）</summary>
    /// <remarks>同期スクリプトの列定義再指定と同じ表記を <see cref="MySqlIdentifier.ColumnCommentClause"/> で共有する</remarks>
    protected override string BuildColumnDefinitionSuffix(Column column) =>
        MySqlIdentifier.ColumnCommentClause(column.Description);

    /// <summary>テーブル定義の閉じ括弧後へ <c>COMMENT='…'</c> 句を付ける（説明が空なら何も付けない）</summary>
    protected override string BuildTableOptionsSuffix(Entity entity) =>
        string.IsNullOrWhiteSpace(entity.Description)
            ? string.Empty
            : $" COMMENT='{MySqlIdentifier.EscapeStringLiteral(entity.Description)}'";
}
