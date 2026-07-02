using QuickER.Provider;

namespace QuickER.MySql;

/// <summary>MySQL 向けの <see cref="IDatabaseProvider"/> 実装（MySqlConnector ベース）</summary>
public sealed class MySqlProvider : IDatabaseProvider
{
    /// <summary>CLI の <c>--provider</c> 値</summary>
    public const string ProviderName = "mysql";

    /// <inheritdoc />
    public string Name => ProviderName;

    /// <inheritdoc />
    public string DisplayName => "MySQL";

    /// <inheritdoc />
    public int? DefaultPort => 3306;

    /// <inheritdoc />
    public ISchemaImporter SchemaImporter { get; } = new MySqlSchemaImporter();

    /// <inheritdoc />
    public IColumnTypeMapper TypeMapper { get; } = new MySqlCSharpTypeMapper();

    /// <inheritdoc />
    public ITypeCatalog TypeCatalog { get; } = new MySqlTypeCatalog();

    /// <inheritdoc />
    public ISyncScriptBuilder SyncScriptBuilder { get; } = new MySqlSyncScriptBuilder();

    /// <inheritdoc />
    public ISchemaSyncExecutor SyncExecutor { get; } = new MySqlSchemaSyncExecutor();

    /// <inheritdoc />
    public IDdlGenerator DdlGenerator { get; } = new MySqlDdlGenerator();

    /// <inheritdoc />
    public string BuildConnectionString(DbConnectionSettings settings) =>
        MySqlConnectionStringFactory.Build(settings);
}
