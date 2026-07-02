using QuickER.Provider;

namespace QuickER.Oracle;

/// <summary>Oracle 向けの <see cref="IDatabaseProvider"/> 実装（Oracle.ManagedDataAccess.Core ベース）</summary>
public sealed class OracleProvider : IDatabaseProvider
{
    /// <summary>CLI の <c>--provider</c> 値</summary>
    public const string ProviderName = "oracle";

    /// <inheritdoc />
    public string Name => ProviderName;

    /// <inheritdoc />
    public string DisplayName => "Oracle";

    /// <inheritdoc />
    public int? DefaultPort => 1521;

    /// <inheritdoc />
    public ISchemaImporter SchemaImporter { get; } = new OracleSchemaImporter();

    /// <inheritdoc />
    public IColumnTypeMapper TypeMapper { get; } = new OracleCSharpTypeMapper();

    /// <inheritdoc />
    public ITypeCatalog TypeCatalog { get; } = new OracleTypeCatalog();

    /// <inheritdoc />
    public ISyncScriptBuilder SyncScriptBuilder { get; } = new OracleSyncScriptBuilder();

    /// <inheritdoc />
    public ISchemaSyncExecutor SyncExecutor { get; } = new OracleSchemaSyncExecutor();

    /// <inheritdoc />
    public IDdlGenerator DdlGenerator { get; } = new OracleDdlGenerator();

    /// <inheritdoc />
    public string BuildConnectionString(DbConnectionSettings settings) =>
        OracleConnectionStringFactory.Build(settings);
}
