using QuickER.Dialects;
using QuickER.Provider;

namespace QuickER.Provider.SqlServer;

/// <summary>SQL Server 向けの <see cref="IDatabaseProvider"/> 実装（SqlClient ベース）</summary>
public sealed class SqlServerProvider : IDatabaseProvider
{
    /// <summary>CLI の <c>--provider</c> 値</summary>
    public const string ProviderName = DialectNames.SqlServer;

    /// <inheritdoc />
    public string Name => ProviderName;

    /// <inheritdoc />
    public string DisplayName => "SQL Server";

    /// <inheritdoc />
    public int? DefaultPort => null;

    /// <inheritdoc />
    public ISchemaImporter SchemaImporter { get; } = new SqlServerSchemaImporter();

    /// <inheritdoc />
    public IColumnTypeMapper TypeMapper { get; } = new SqlServerCSharpTypeMapper();

    /// <inheritdoc />
    public ITypeCatalog TypeCatalog { get; } = new SqlServerTypeCatalog();

    /// <inheritdoc />
    public ISyncScriptBuilder SyncScriptBuilder { get; } = new SqlServerSyncScriptBuilder();

    /// <inheritdoc />
    /// <remarks>
    /// FK・主キー・一意制約に参加している列の型変更は依存エラー（Msg 5074）になるため、
    /// <see cref="SyncDialectCapabilities.AlterColumnRequiresForeignKeyRebuild"/> と
    /// <see cref="SyncDialectCapabilities.AlterColumnRequiresKeyConstraintRebuild"/> を立てて
    /// プランナーにそれらの自動 DROP / ADD を組ませる。型表記の比較は <see cref="SqlServerTypeNotation.Normalize"/>
    /// （取込が返す表記）へ寄せてから行う。
    /// </remarks>
    public SyncDialectCapabilities SyncCapabilities { get; } =
        new()
        {
            SupportsAlterColumn = true,
            SupportsForeignKeyAlter = true,
            SupportsDescriptions = true,
            AlterColumnRequiresForeignKeyRebuild = true,
            AlterColumnRequiresKeyConstraintRebuild = true,
            ForeignKeyRequiresLeadingIndex = false,
            PersistsForeignKeyConstraintNames = true,
            ColumnReorder = ColumnReorderMode.None,
            NormalizeTypeNotation = SqlServerTypeNotation.Normalize,
            SupportsOnUpdateAction = true,
            SupportsSetDefaultAction = true,
            SetDefaultRequiresColumnDefault = true,
            SetNullRequiresNullableChildColumn = true,
            SupportsSchemaQualifiedTableNames = true,
            ChecksDefaultOnColumnTypeChange = true,
            SupportsComputedColumnTypeChange = false,
            SupportsUniqueConstraintOnPrimaryKey = true,
            ReferencedKeyRequiresSameColumnOrder = true,
            ForeignKeyAllowsStringLengthDifference = false,
            TransactionalDdl = true,
            IdentifierLengthLimit = SqlIdentifier.Limit,
            TruncatesLongIdentifiers = false,
        };

    /// <inheritdoc />
    public ISchemaSyncExecutor SyncExecutor { get; } = new SqlServerSchemaSyncExecutor();

    /// <inheritdoc />
    public IDdlGenerator DdlGenerator { get; } = new SqlServerDdlGenerator();

    /// <inheritdoc />
    public string BuildConnectionString(DbConnectionSettings settings) =>
        SqlServerConnectionStringFactory.Build(settings);
}
