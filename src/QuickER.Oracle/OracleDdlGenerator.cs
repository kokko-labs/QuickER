using System.Text;
using QuickER.Model;
using QuickER.Provider;

namespace QuickER.Oracle;

/// <summary>
/// ER 図から Oracle 向けの DDL（<c>CREATE TABLE</c> / <c>ALTER TABLE ... ADD CONSTRAINT</c>）を生成する
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item>識別子は <see cref="OracleIdentifier"/> で二重引用符クォートする（<c>schema.table</c> は <c>"schema"."table"</c> に分割、<c>"</c> は <c>""</c> にエスケープ）</item>
///   <item>Oracle は <c>ON UPDATE</c> をサポートしないため出力しない。<c>UpdateAction</c> が <c>NoAction</c> 以外の場合は注意コメントを付す</item>
///   <item><c>ON DELETE</c> は <c>CASCADE</c> / <c>SET NULL</c> のみ句を出力する（<c>NO ACTION</c> は既定のため省略）</item>
///   <item>テーブル・列の説明は <c>COMMENT ON</c> として全 CREATE / FK の後に出力する
///     （取込が説明を読むため、DDL から新規 DB を作るフローでも説明が往復するようにする）</item>
///   <item>共通の DDL 組み立てロジックは <see cref="DdlGeneratorBase"/> を参照</item>
/// </list>
/// </remarks>
public sealed class OracleDdlGenerator : DdlGeneratorBase
{
    /// <inheritdoc />
    protected override string QuoteQualifiedName(string name) => OracleIdentifier.Quote(name);

    /// <inheritdoc />
    protected override string QuoteSimpleName(string name) => OracleIdentifier.QuoteSimple(name);

    /// <inheritdoc />
    protected override string SafeName(string name) => OracleIdentifier.SafeName(name);

    /// <inheritdoc />
    protected override string QuoteConstraintName(string constraintName) =>
        $"\"{OracleIdentifier.Escape(constraintName)}\"";

    /// <summary>Oracle は <c>ON DELETE</c> のみ対応するため、共通ヘルパーではなく <see cref="OracleReferentialAction"/> に委譲する</summary>
    protected override string BuildReferentialActionClause(Relationship relationship) =>
        OracleReferentialAction.BuildOnDeleteClause(relationship.OnDelete);

    /// <summary>ON UPDATE が指定されていても Oracle では無視される旨を注意コメントで残す</summary>
    protected override void AppendBeforeForeignKeyStatement(
        StringBuilder sb,
        Relationship relationship
    )
    {
        if (relationship.OnUpdate != ForeignKeyReferentialAction.NoAction)
        {
            sb.AppendLine("-- Note: Oracle does not support ON UPDATE; ignoring it");
        }
    }

    /// <summary>テーブル・列の説明を <c>COMMENT ON</c> 文として出力する</summary>
    /// <remarks>
    /// 本 DDL は基底の <see cref="DdlGeneratorBase.Build"/>（各文を <c>;</c> で終端）を用いるため、
    /// <c>COMMENT ON</c> も <c>;</c> 終端で出力する（同期スクリプト側の SQL*Plus「/」区切り方式とは別）。
    /// 識別子・リテラルの整形は <see cref="OracleSyncScriptBuilder"/> の <c>COMMENT ON</c> 出力と揃える
    /// </remarks>
    protected override void AppendDescriptions(StringBuilder sb, ErDiagram diagram) =>
        AppendDescriptionStatements(
            sb,
            diagram,
            entity =>
                $"COMMENT ON TABLE {OracleIdentifier.Quote(entity.TableName)} "
                + $"IS '{OracleIdentifier.EscapeStringLiteral(entity.Description)}';",
            (entity, column) =>
                $"COMMENT ON COLUMN {OracleIdentifier.Quote(entity.TableName)}.{OracleIdentifier.QuoteSimple(column.Name)} "
                + $"IS '{OracleIdentifier.EscapeStringLiteral(column.Description)}';"
        );
}
