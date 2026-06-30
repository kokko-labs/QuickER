using QuickER.Provider;

namespace QuickER.SqlServer;

/// <summary>SQL Server 向けの <see cref="IDatabaseProvider"/> 実装（SqlClient ベース）</summary>
public sealed class SqlServerProvider : IDatabaseProvider
{
    /// <summary>CLI の <c>--provider</c> 値</summary>
    public const string ProviderName = "sqlserver";

    /// <inheritdoc />
    public string Name => ProviderName;

    /// <inheritdoc />
    public ISchemaImporter SchemaImporter { get; } = new SqlServerSchemaImporter();

    /// <inheritdoc />
    public IColumnTypeMapper TypeMapper { get; } = new SqlServerCSharpTypeMapper();

    /// <inheritdoc />
    public IReadOnlyList<string> DataTypes => SqlServerDataTypes.All;
}
