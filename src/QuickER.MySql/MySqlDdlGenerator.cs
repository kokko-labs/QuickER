using QuickER.Provider;

namespace QuickER.MySql;

/// <summary>
/// ER 図から MySQL 向けの DDL（<c>CREATE TABLE</c> / <c>ALTER TABLE ... ADD CONSTRAINT</c>）を生成する
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item>識別子は <see cref="MySqlIdentifier"/> でバッククォートクォートする（<c>schema.table</c> は <c>`schema`.`table`</c> に分割、<c>`</c> は <c>``</c> にエスケープ）</item>
///   <item>MySQL 8.0 の既定ストレージエンジンは InnoDB のため <c>ENGINE</c> 句は書かない（他方言との対称性）</item>
///   <item>SQL Server / PostgreSQL 版の DDL が説明を出力しないのと対称に、本 DDL でも <c>COMMENT</c> は出力しない（説明の反映は同期スクリプトが担う）</item>
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
}
