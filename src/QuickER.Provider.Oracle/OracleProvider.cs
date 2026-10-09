using QuickER.Provider;

namespace QuickER.Provider.Oracle;

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
    /// <remarks>
    /// Oracle の外部キーは <c>ON UPDATE</c> も <c>ON DELETE SET DEFAULT</c> も持たない（DDL・同期は注意コメントを
    /// 出して句を省く）。差分の比較で図のそれらを <c>NO ACTION</c> へ畳まないと、同期しても消えない外部キーの差分が
    /// 出続ける（既定どおり追加だけを選ぶと同じ名前の外部キーで ORA-02275）。型表記の比較は
    /// <see cref="OracleTypeNotation.Normalize"/>（取込が返す表記）へ寄せてから行う。
    /// </remarks>
    public SyncDialectCapabilities SyncCapabilities { get; } =
        new()
        {
            SupportsOnUpdateAction = false,
            SupportsSetDefaultAction = false,
            SupportsUniqueConstraintOnPrimaryKey = false,
            ReferencedKeyRequiresSameColumnOrder = false,
            TransactionalDdl = false,
            NormalizeTypeNotation = OracleTypeNotation.Normalize,
        };

    /// <inheritdoc />
    public ISchemaSyncExecutor SyncExecutor { get; } = new OracleSchemaSyncExecutor();

    /// <inheritdoc />
    public IDdlGenerator DdlGenerator { get; } = new OracleDdlGenerator();

    /// <inheritdoc />
    public string BuildConnectionString(DbConnectionSettings settings) =>
        OracleConnectionStringFactory.Build(settings);
}
