using QuickER.Provider;

namespace QuickER.Sqlite;

/// <summary>SQLite 向けの <see cref="IDatabaseProvider"/> 実装（Microsoft.Data.Sqlite ベース）</summary>
/// <remarks>
/// SQLite はファイル型 DB のため既定ポートを持たない（<see cref="DefaultPort"/> は <c>null</c>）。
/// DB 同期（<see cref="SyncScriptBuilder"/> / <see cref="SyncExecutor"/>）はテーブル再構築方式で対応する
/// （<see cref="SyncCapabilities"/> が rebuild を宣言し、<see cref="SyncPlanner"/> が合成計画を組み立てる）。
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
    /// <remarks>
    /// SQLite は逐次 DDL での列型変更・列削除・FK 変更が制限され、多くの変更はテーブル再構築が必要。
    /// 制約名も永続化されない（合成名）ため、対応能力は最小に宣言する。
    /// </remarks>
    public SyncDialectCapabilities SyncCapabilities { get; } =
        new()
        {
            SupportsAlterColumn = false,
            SupportsForeignKeyAlter = false,
            SupportsDescriptions = false,
            PersistsForeignKeyConstraintNames = false,
            ColumnReorder = ColumnReorderMode.Rebuild,
        };

    /// <inheritdoc />
    public ISchemaSyncExecutor SyncExecutor { get; } = new SqliteSchemaSyncExecutor();

    /// <inheritdoc />
    public IDdlGenerator DdlGenerator { get; } = new SqliteDdlGenerator();

    /// <inheritdoc />
    public string BuildConnectionString(DbConnectionSettings settings) =>
        SqliteConnectionStringFactory.Build(settings);
}
