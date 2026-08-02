using AwesomeAssertions;
using QuickER.Model;
using QuickER.Provider;
using QuickER.Sqlite;
using QuickER.SqlServer;

namespace QuickER.Tests.Sqlite;

/// <summary>
/// SQL Server ⇔ SQLite の方言間型変換（<see cref="DiagramTypeConverter"/> 経由）を検証するテストクラス。
/// SQLite は宣言型を verbatim 保存できるため、SQL Server ⇄ SQLite の往復がほぼ無損失になる点を重点的に検証する。
/// </summary>
public class SqliteDialectConversionTests
{
    private static readonly SqlServerTypeCatalog Sql = new();
    private static readonly SqliteTypeCatalog Lite = new();

    private static ErDiagram BuildDiagram(string dataType) =>
        new()
        {
            Entities =
            [
                new Entity
                {
                    TableName = "t",
                    Columns = [new Column { Name = "c", DataType = dataType }],
                },
            ],
        };

    /// <summary>from=SQL Server → to=SQLite の代表変換を検証する</summary>
    [Theory(DisplayName = "sqlserver → sqlite の代表変換")]
    [InlineData("int", "INT")]
    [InlineData("bigint", "BIGINT")]
    [InlineData("smallint", "SMALLINT")]
    [InlineData("tinyint", "TINYINT")]
    [InlineData("bit", "BIT")]
    [InlineData("nvarchar(50)", "NVARCHAR(50)")]
    [InlineData("nvarchar(max)", "NVARCHAR(MAX)")]
    [InlineData("varchar(50)", "VARCHAR(50)")]
    [InlineData("decimal(18,2)", "DECIMAL(18,2)")]
    [InlineData("uniqueidentifier", "UNIQUEIDENTIFIER")]
    [InlineData("datetime2", "DATETIME2")]
    [InlineData("datetimeoffset", "DATETIMEOFFSET")]
    [InlineData("varbinary(max)", "VARBINARY(MAX)")]
    public void Convert_SqlServerToSqlite(string source, string expected)
    {
        var diagram = BuildDiagram(source);

        var plan = DiagramTypeConverter.CreatePlan(diagram, Sql, Lite);

        plan.Converted.Should().ContainSingle();
        plan.Converted[0].NewType.Should().Be(expected);
    }

    /// <summary>from=SQLite → to=SQL Server の代表変換を検証する</summary>
    [Theory(DisplayName = "sqlite → sqlserver の代表変換")]
    [InlineData("INT", "int")]
    [InlineData("BIGINT", "bigint")]
    [InlineData("BIT", "bit")]
    [InlineData("NVARCHAR(50)", "nvarchar(50)")]
    [InlineData("VARCHAR(50)", "varchar(50)")]
    [InlineData("DECIMAL(18,2)", "decimal(18,2)")]
    [InlineData("UNIQUEIDENTIFIER", "uniqueidentifier")]
    [InlineData("DATETIME2", "datetime2")]
    [InlineData("VARBINARY(MAX)", "varbinary(max)")]
    // SQLite 親和性キーワード TEXT / BLOB は SQL Server の max 型へ寄る
    [InlineData("TEXT", "nvarchar(max)")]
    [InlineData("BLOB", "varbinary(max)")]
    // JSON は SQL Server に型が無いため nvarchar(max) へフォールバックする
    [InlineData("JSON", "nvarchar(max)")]
    public void Convert_SqliteToSqlServer(string source, string expected)
    {
        var diagram = BuildDiagram(source);

        var plan = DiagramTypeConverter.CreatePlan(diagram, Lite, Sql);

        plan.Converted.Should().ContainSingle();
        plan.Converted[0].NewType.Should().Be(expected);
    }

    /// <summary>
    /// SQL Server → SQLite → SQL Server の往復で型が無損失に復元されることを検証する
    /// （リッチ宣言型方針の中核。長さ・精度・スケールが保持されること）
    /// </summary>
    [Theory(DisplayName = "sqlserver → sqlite → sqlserver の往復が無損失")]
    [InlineData("int")]
    [InlineData("bigint")]
    [InlineData("smallint")]
    [InlineData("tinyint")]
    [InlineData("bit")]
    [InlineData("decimal(18,2)")]
    [InlineData("money")]
    [InlineData("real")]
    [InlineData("float")]
    [InlineData("nvarchar(50)")]
    [InlineData("nvarchar(max)")]
    [InlineData("varchar(255)")]
    [InlineData("nchar(10)")]
    [InlineData("char(5)")]
    [InlineData("varbinary(50)")]
    [InlineData("varbinary(max)")]
    [InlineData("binary(16)")]
    [InlineData("date")]
    [InlineData("time(7)")]
    [InlineData("datetime2(3)")]
    [InlineData("datetimeoffset(7)")]
    [InlineData("uniqueidentifier")]
    [InlineData("xml")]
    public void RoundTrip_SqlServer_Sqlite_SqlServer_IsLossless(string sqlServerType)
    {
        // SQL Server → SQLite
        var toSqlitePlan = DiagramTypeConverter.CreatePlan(BuildDiagram(sqlServerType), Sql, Lite);
        toSqlitePlan.Converted.Should().ContainSingle();
        var sqliteType = toSqlitePlan.Converted[0].NewType!;

        // SQLite → SQL Server
        var backPlan = DiagramTypeConverter.CreatePlan(BuildDiagram(sqliteType), Lite, Sql);
        backPlan.Converted.Should().ContainSingle();

        backPlan.Converted[0].NewType.Should().Be(sqlServerType);
    }

    /// <summary>変換不能な SQL Server 型（hierarchyid）が Unconverted に入ることを検証する</summary>
    [Fact(DisplayName = "sqlserver hierarchyid は sqlite へ変換できず Unconverted")]
    public void Convert_HierarchyId_IsUnconverted()
    {
        var diagram = BuildDiagram("hierarchyid");

        var plan = DiagramTypeConverter.CreatePlan(diagram, Sql, Lite);

        plan.Converted.Should().BeEmpty();
        plan.Unconverted.Should().ContainSingle();
        plan.Unconverted[0].NewType.Should().BeNull();
    }

    /// <summary>変換不能な SQLite 宣言型（未知の型）が Unconverted に入ることを検証する</summary>
    [Fact(DisplayName = "sqlite の未知型は sqlserver へ変換できず Unconverted")]
    public void Convert_UnknownSqliteType_IsUnconverted()
    {
        var diagram = BuildDiagram("no_such_type");

        var plan = DiagramTypeConverter.CreatePlan(diagram, Lite, Sql);

        plan.Converted.Should().BeEmpty();
        plan.Unconverted.Should().ContainSingle();
        plan.Unconverted[0].NewType.Should().BeNull();
    }
}
