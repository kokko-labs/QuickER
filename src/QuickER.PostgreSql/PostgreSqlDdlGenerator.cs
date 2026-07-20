using System.Text;
using QuickER.Model;
using QuickER.Provider;

namespace QuickER.PostgreSql;

/// <summary>
/// ER 図から PostgreSQL 向けの DDL（<c>CREATE TABLE</c> / <c>ALTER TABLE ... ADD CONSTRAINT</c>）を生成する
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item>識別子は <see cref="PgIdentifier"/> で二重引用符クォートする（<c>schema.table</c> は <c>"schema"."table"</c> に分割、<c>"</c> は <c>""</c> にエスケープ）</item>
///   <item>テーブル・列の説明は <c>COMMENT ON</c> として全 CREATE / FK の後に出力する
///     （取込が説明を読むため、DDL から新規 DB を作るフローでも説明が往復するようにする）</item>
///   <item>共通の DDL 組み立てロジックは <see cref="DdlGeneratorBase"/> を参照</item>
/// </list>
/// </remarks>
public sealed class PostgreSqlDdlGenerator : DdlGeneratorBase
{
    /// <inheritdoc />
    protected override string QuoteQualifiedName(string name) => PgIdentifier.Quote(name);

    /// <inheritdoc />
    protected override string QuoteSimpleName(string name) => PgIdentifier.QuoteSimple(name);

    /// <inheritdoc />
    protected override string SafeName(string name) => PgIdentifier.SafeName(name);

    /// <inheritdoc />
    protected override string QuoteConstraintName(string constraintName) =>
        $"\"{PgIdentifier.Escape(constraintName)}\"";

    /// <summary>テーブル・列の説明を <c>COMMENT ON</c> 文として出力する</summary>
    /// <remarks>
    /// PostgreSQL の <c>COMMENT ON</c> は独立した文であり、対象テーブル・列が既に存在している必要がある。
    /// 基底の <see cref="DdlGeneratorBase.Build"/> は「全テーブルを CREATE → 全 FK を ALTER」で組むため、
    /// COMMENT はその全てが揃った末尾にまとめて出す（＝各 CREATE 直後に散らすより自然で、同期スクリプトが説明を
    /// 最後のセクションに置く順序とも一致する）。新値は空でないもののみ対象のため <c>IS NULL</c>（削除）は出さない。
    /// 識別子・リテラルの整形は <see cref="PostgreSqlSyncScriptBuilder"/> の <c>COMMENT ON</c> 出力と揃える
    /// </remarks>
    protected override void AppendDescriptions(StringBuilder sb, ErDiagram diagram) =>
        AppendDescriptionStatements(
            sb,
            diagram,
            entity =>
                $"COMMENT ON TABLE {PgIdentifier.Quote(entity.TableName)} "
                + $"IS '{PgIdentifier.EscapeStringLiteral(entity.Description)}';",
            (entity, column) =>
                $"COMMENT ON COLUMN {PgIdentifier.Quote(entity.TableName)}.{PgIdentifier.QuoteSimple(column.Name)} "
                + $"IS '{PgIdentifier.EscapeStringLiteral(column.Description)}';"
        );
}
