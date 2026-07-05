using QuickER.Provider;

namespace QuickER.Sqlite;

/// <summary>SQLite 向けの <see cref="IDatabaseProvider"/> 実装（Microsoft.Data.Sqlite ベース）</summary>
/// <remarks>
/// SQLite はファイル型 DB のため既定ポートを持たない（<see cref="DefaultPort"/> は <c>null</c>）。
/// DB 同期（<see cref="SyncScriptBuilder"/> / <see cref="SyncExecutor"/>）は初回スコープ外で、
/// 明示的に <see cref="NotSupportedException"/> を投げるスタブを束ねる。
/// </remarks>
public sealed class SqliteProvider : IDatabaseProvider
{
    /// <summary>CLI の <c>--provider</c> 値</summary>
    public const string ProviderName = "sqlite";

    /// <inheritdoc />
    public string Name => ProviderName;

    /// <inheritdoc />
    public string DisplayName => "SQLite";

    /// <inheritdoc />
    public int? DefaultPort => null;

    /// <inheritdoc />
    public ISchemaImporter SchemaImporter { get; } = new SqliteSchemaImporter();

    /// <inheritdoc />
    public IColumnTypeMapper TypeMapper { get; } = new SqliteCSharpTypeMapper();

    /// <inheritdoc />
    public ITypeCatalog TypeCatalog { get; } = new SqliteTypeCatalog();

    /// <inheritdoc />
    public ISyncScriptBuilder SyncScriptBuilder { get; } = new SqliteSyncScriptBuilder();

    /// <inheritdoc />
    public ISchemaSyncExecutor SyncExecutor { get; } = new SqliteSchemaSyncExecutor();

    /// <inheritdoc />
    public IDdlGenerator DdlGenerator { get; } = new SqliteDdlGenerator();

    /// <inheritdoc />
    public string BuildConnectionString(DbConnectionSettings settings) =>
        SqliteConnectionStringFactory.Build(settings);
}
