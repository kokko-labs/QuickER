using AwesomeAssertions;
using QuickER.Model;
using QuickER.MySql;
using QuickER.Provider;
using QuickER.SqlServer;

namespace QuickER.Tests.MySql;

/// <summary>
/// SQL Server ⇔ MySQL の方言間型変換（<see cref="DiagramTypeConverter"/> 経由）を検証するテストクラス。
/// </summary>
public class MySqlDialectConversionTests
{
    private static readonly SqlServerTypeCatalog Sql = new();
    private static readonly MySqlTypeCatalog My = new();

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

    /// <summary>from=SQL Server → to=MySQL の代表変換を検証する</summary>
    [Theory(DisplayName = "sqlserver → mysql の代表変換")]
    [InlineData("nvarchar(50)", "varchar(50)")]
    [InlineData("nvarchar(max)", "longtext")]
    [InlineData("int", "int")]
    [InlineData("bit", "tinyint(1)")]
    [InlineData("uniqueidentifier", "char(36)")]
    [InlineData("datetime2", "datetime")]
    [InlineData("datetimeoffset", "timestamp")]
    [InlineData("varbinary(max)", "longblob")]
    public void Convert_SqlServerToMySql(string source, string expected)
    {
        var diagram = BuildDiagram(source);

        var plan = DiagramTypeConverter.CreatePlan(diagram, Sql, My);

        plan.Converted.Should().ContainSingle();
        plan.Converted[0].NewType.Should().Be(expected);
    }

    /// <summary>変換不能な SQL Server 型（hierarchyid）が Unconverted に入ることを検証する</summary>
    [Fact(DisplayName = "sqlserver hierarchyid は mysql へ変換できず Unconverted")]
    public void Convert_HierarchyId_IsUnconverted()
    {
        var diagram = BuildDiagram("hierarchyid");

        var plan = DiagramTypeConverter.CreatePlan(diagram, Sql, My);

        plan.Converted.Should().BeEmpty();
        plan.Unconverted.Should().ContainSingle();
        plan.Unconverted[0].NewType.Should().BeNull();
    }

    /// <summary>from=MySQL → to=SQL Server の代表変換を検証する</summary>
    [Theory(DisplayName = "mysql → sqlserver の代表変換")]
    [InlineData("varchar(50)", "nvarchar(50)")]
    [InlineData("longtext", "nvarchar(max)")]
    [InlineData("int", "int")]
    [InlineData("tinyint(1)", "bit")]
    [InlineData("datetime", "datetime2")]
    [InlineData("timestamp", "datetimeoffset")]
    [InlineData("longblob", "varbinary(max)")]
    [InlineData("double", "float")]
    [InlineData("json", "nvarchar(max)")]
    public void Convert_MySqlToSqlServer(string source, string expected)
    {
        var diagram = BuildDiagram(source);

        var plan = DiagramTypeConverter.CreatePlan(diagram, My, Sql);

        plan.Converted.Should().ContainSingle();
        plan.Converted[0].NewType.Should().Be(expected);
    }

    /// <summary>変換不能な MySQL 型（enum）が Unconverted に入ることを検証する</summary>
    [Fact(DisplayName = "mysql enum は sqlserver へ変換できず Unconverted")]
    public void Convert_Enum_IsUnconverted()
    {
        var diagram = BuildDiagram("enum('a','b')");

        var plan = DiagramTypeConverter.CreatePlan(diagram, My, Sql);

        plan.Converted.Should().BeEmpty();
        plan.Unconverted.Should().ContainSingle();
        plan.Unconverted[0].NewType.Should().BeNull();
    }
}
