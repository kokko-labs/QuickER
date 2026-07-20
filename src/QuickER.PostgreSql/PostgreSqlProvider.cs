using QuickER.Provider;

namespace QuickER.PostgreSql;

/// <summary>PostgreSQL 向けの <see cref="IDatabaseProvider"/> 実装（Npgsql ベース）</summary>
public sealed class PostgreSqlProvider : IDatabaseProvider
{
    /// <summary>CLI の <c>--provider</c> 値</summary>
    public const string ProviderName = "postgresql";

    /// <inheritdoc />
    public string Name => ProviderName;

    /// <inheritdoc />
    public string DisplayName => "PostgreSQL";

    /// <inheritdoc />
    public int? DefaultPort => 5432;

    /// <inheritdoc />
    public ISchemaImporter SchemaImporter { get; } = new PostgreSqlSchemaImporter();

    /// <inheritdoc />
    public IColumnTypeMapper TypeMapper { get; } = new PostgreSqlCSharpTypeMapper();

    /// <inheritdoc />
    public ITypeCatalog TypeCatalog { get; } = new PostgreSqlTypeCatalog();

    /// <inheritdoc />
    public ISyncScriptBuilder SyncScriptBuilder { get; } = new PostgreSqlSyncScriptBuilder();

    /// <inheritdoc />
    public SyncDialectCapabilities SyncCapabilities { get; } = new();

    /// <inheritdoc />
    public ISchemaSyncExecutor SyncExecutor { get; } = new PostgreSqlSchemaSyncExecutor();

    /// <inheritdoc />
    public IDdlGenerator DdlGenerator { get; } = new PostgreSqlDdlGenerator();

    /// <inheritdoc />
    public string BuildConnectionString(DbConnectionSettings settings) =>
        PostgreSqlConnectionStringFactory.Build(settings);
}
