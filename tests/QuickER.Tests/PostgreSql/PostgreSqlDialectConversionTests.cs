using AwesomeAssertions;
using QuickER.Model;
using QuickER.PostgreSql;
using QuickER.Provider;
using QuickER.SqlServer;

namespace QuickER.Tests.PostgreSql;

/// <summary>
/// SQL Server ⇔ PostgreSQL の方言間型変換（<see cref="DiagramTypeConverter"/> 経由）を検証するテストクラス。
/// </summary>
public class PostgreSqlDialectConversionTests
{
    private static readonly SqlServerTypeCatalog Sql = new();
    private static readonly PostgreSqlTypeCatalog Pg = new();

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

    /// <summary>from=SQL Server → to=PostgreSQL の代表変換を検証する</summary>
    [Theory(DisplayName = "sqlserver → postgresql の代表変換")]
    [InlineData("nvarchar(50)", "varchar(50)")]
    [InlineData("nvarchar(max)", "text")]
    [InlineData("int", "integer")]
    [InlineData("uniqueidentifier", "uuid")]
    [InlineData("datetime2", "timestamp")]
    [InlineData("datetimeoffset", "timestamptz")]
    [InlineData("varbinary(max)", "bytea")]
    [InlineData("bit", "boolean")]
    public void Convert_SqlServerToPostgreSql(string source, string expected)
    {
        var diagram = BuildDiagram(source);

        var plan = DiagramTypeConverter.CreatePlan(diagram, Sql, Pg);

        plan.Converted.Should().ContainSingle();
        plan.Converted[0].NewType.Should().Be(expected);
    }

    /// <summary>変換不能な SQL Server 型（hierarchyid）が Unconverted に入ることを検証する</summary>
    [Fact(DisplayName = "sqlserver hierarchyid は postgresql へ変換できず Unconverted")]
    public void Convert_HierarchyId_IsUnconverted()
    {
        var diagram = BuildDiagram("hierarchyid");

        var plan = DiagramTypeConverter.CreatePlan(diagram, Sql, Pg);

        plan.Converted.Should().BeEmpty();
        plan.Unconverted.Should().ContainSingle();
        plan.Unconverted[0].NewType.Should().BeNull();
    }

    /// <summary>from=PostgreSQL → to=SQL Server の代表変換を検証する</summary>
    [Theory(DisplayName = "postgresql → sqlserver の代表変換")]
    [InlineData("varchar(50)", "nvarchar(50)")]
    [InlineData("text", "nvarchar(max)")]
    [InlineData("integer", "int")]
    [InlineData("uuid", "uniqueidentifier")]
    [InlineData("timestamp", "datetime2")]
    [InlineData("timestamptz", "datetimeoffset")]
    [InlineData("bytea", "varbinary(max)")]
    [InlineData("boolean", "bit")]
    [InlineData("double precision", "float")]
    [InlineData("jsonb", "nvarchar(max)")]
    public void Convert_PostgreSqlToSqlServer(string source, string expected)
    {
        var diagram = BuildDiagram(source);

        var plan = DiagramTypeConverter.CreatePlan(diagram, Pg, Sql);

        plan.Converted.Should().ContainSingle();
        plan.Converted[0].NewType.Should().Be(expected);
    }

    /// <summary>変換不能な PostgreSQL 型（serial）が Unconverted に入ることを検証する</summary>
    [Fact(DisplayName = "postgresql serial は sqlserver へ変換できず Unconverted")]
    public void Convert_Serial_IsUnconverted()
    {
        var diagram = BuildDiagram("serial");

        var plan = DiagramTypeConverter.CreatePlan(diagram, Pg, Sql);

        plan.Converted.Should().BeEmpty();
        plan.Unconverted.Should().ContainSingle();
        plan.Unconverted[0].NewType.Should().BeNull();
    }
}
