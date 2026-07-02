using QuickER.Provider;

namespace QuickER.PostgreSql;

/// <summary>
/// ER 図から PostgreSQL 向けの DDL（<c>CREATE TABLE</c> / <c>ALTER TABLE ... ADD CONSTRAINT</c>）を生成する
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item>識別子は <see cref="PgIdentifier"/> で二重引用符クォートする（<c>schema.table</c> は <c>"schema"."table"</c> に分割、<c>"</c> は <c>""</c> にエスケープ）</item>
///   <item>SQL Server 版の DDL が説明（拡張プロパティ）を出力しないのと対称に、本 DDL でも COMMENT ON は出力しない（説明の反映は同期スクリプトが担う）</item>
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
}
