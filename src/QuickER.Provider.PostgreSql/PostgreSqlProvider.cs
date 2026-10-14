using QuickER.Provider;

namespace QuickER.Provider.PostgreSql;

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
    /// <remarks>
    /// 型表記の比較は <see cref="PostgreSqlTypeNotation.Normalize"/>（取込が返す短縮語彙の表記）へ寄せてから行う
    /// （素の <c>timestamp</c> は <c>timestamp(6)</c>、<c>numeric(18,0)</c> は <c>numeric(18)</c> として返るため）。
    /// </remarks>
    public SyncDialectCapabilities SyncCapabilities { get; } =
        new()
        {
            NormalizeTypeNotation = PostgreSqlTypeNotation.Normalize,
            SupportsComputedColumnTypeChange = true,
            ReferencedKeyRequiresSameColumnOrder = false,
            IdentifierLengthLimit = PgIdentifier.Limit,
            TruncatesLongIdentifiers = true,
        };

    /// <inheritdoc />
    public ISchemaSyncExecutor SyncExecutor { get; } = new PostgreSqlSchemaSyncExecutor();

    /// <inheritdoc />
    public IDdlGenerator DdlGenerator { get; } = new PostgreSqlDdlGenerator();

    /// <inheritdoc />
    public string BuildConnectionString(DbConnectionSettings settings) =>
        PostgreSqlConnectionStringFactory.Build(settings);
}
