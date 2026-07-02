using QuickER.Provider;

namespace QuickER.SqlServer;

/// <summary>
/// ER 図から SQL Server 向けの DDL（<c>CREATE TABLE</c> / <c>ALTER TABLE ... ADD CONSTRAINT</c>）を生成する
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item>識別子は <see cref="SqlIdentifier"/> で角括弧付けする（<c>schema.table</c> は <c>[schema].[table]</c> に分割、<c>]</c> は <c>]]</c> にエスケープ）</item>
///   <item>共通の DDL 組み立てロジックは <see cref="DdlGeneratorBase"/> を参照</item>
/// </list>
/// </remarks>
public sealed class SqlServerDdlGenerator : DdlGeneratorBase
{
    /// <inheritdoc />
    protected override string QuoteQualifiedName(string name) => SqlIdentifier.Bracket(name);

    /// <inheritdoc />
    protected override string QuoteSimpleName(string name) => SqlIdentifier.BracketSimple(name);

    /// <inheritdoc />
    protected override string SafeName(string name) => SqlIdentifier.SafeName(name);

    /// <inheritdoc />
    protected override string QuoteConstraintName(string constraintName) =>
        $"[{SqlIdentifier.Escape(constraintName)}]";
}
