using QuickER.Provider;

namespace QuickER.Provider.MySql;

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
    /// <remarks>
    /// MySQL は <c>ALTER TABLE ... MODIFY ... AFTER</c> による列順のネイティブ変更が可能。
    /// 外部キーの両端の列の型変更を拒否するため、列定義変更の前後で外部キーを外して付け直す。
    /// 旧主キーの索引に支えられた自表の外部キーも、主キー変更の前後で外して付け直す（エラー 1553 対策）。
    /// 型表記の比較は <see cref="MySqlTypeNotation.Normalize"/>（取込が返す表記）へ寄せてから行う。
    /// InnoDB は参照アクションの <c>SET DEFAULT</c> を宣言として受け付けるが効かせない（親を消すと外部キーエラー＝
    /// <c>NO ACTION</c> と同じ）ので、DDL・同期は句を省いて注意コメントを置き、差分の比較でも <c>NO ACTION</c> へ畳む。
    /// </remarks>
    public SyncDialectCapabilities SyncCapabilities { get; } =
        new()
        {
            ColumnReorder = ColumnReorderMode.Native,
            AlterColumnRequiresForeignKeyRebuild = true,
            ForeignKeyRequiresLeadingIndex = true,
            ForeignKeyAllowsStringLengthDifference = true,
            TransactionalDdl = false,
            SupportsSetDefaultAction = false,
            NormalizeTypeNotation = MySqlTypeNotation.Normalize,
            IdentifierLengthLimit = MySqlIdentifier.Limit,
        };

    /// <inheritdoc />
    public ISchemaSyncExecutor SyncExecutor { get; } = new MySqlSchemaSyncExecutor();

    /// <inheritdoc />
    public IDdlGenerator DdlGenerator { get; } = new MySqlDdlGenerator();

    /// <inheritdoc />
    public string BuildConnectionString(DbConnectionSettings settings) =>
        MySqlConnectionStringFactory.Build(settings);
}
