using AwesomeAssertions;
using QuickER.Model;
using QuickER.Oracle;
using QuickER.Provider;
using QuickER.SqlServer;

namespace QuickER.Tests.Oracle;

/// <summary>
/// SQL Server ⇔ Oracle の方言間型変換（<see cref="DiagramTypeConverter"/> 経由）を検証するテストクラス。
/// </summary>
public class OracleDialectConversionTests
{
    private static readonly SqlServerTypeCatalog Sql = new();
    private static readonly OracleTypeCatalog Ora = new();

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

    /// <summary>from=SQL Server → to=Oracle の代表変換を検証する</summary>
    [Theory(DisplayName = "sqlserver → oracle の代表変換")]
    [InlineData("int", "NUMBER(10)")]
    [InlineData("bigint", "NUMBER(19)")]
    [InlineData("smallint", "NUMBER(5)")]
    [InlineData("tinyint", "NUMBER(3)")]
    [InlineData("bit", "NUMBER(1)")]
    [InlineData("nvarchar(50)", "NVARCHAR2(50)")]
    [InlineData("varchar(50)", "VARCHAR2(50)")]
    [InlineData("varchar(max)", "CLOB")]
    [InlineData("nvarchar(max)", "NCLOB")]
    [InlineData("uniqueidentifier", "RAW(16)")]
    [InlineData("datetime2", "TIMESTAMP")]
    [InlineData("varbinary(max)", "BLOB")]
    public void Convert_SqlServerToOracle(string source, string expected)
    {
        var diagram = BuildDiagram(source);

        var plan = DiagramTypeConverter.CreatePlan(diagram, Sql, Ora);

        plan.Converted.Should().ContainSingle();
        plan.Converted[0].NewType.Should().Be(expected);
    }

    /// <summary>SQL Server の time(7) は Oracle に TIME 型が無いため変換不能（Unconverted）</summary>
    [Fact(DisplayName = "sqlserver time(7) は oracle へ変換できず Unconverted")]
    public void Convert_Time_IsUnconverted()
    {
        var diagram = BuildDiagram("time(7)");

        var plan = DiagramTypeConverter.CreatePlan(diagram, Sql, Ora);

        plan.Converted.Should().BeEmpty();
        plan.Unconverted.Should().ContainSingle();
        plan.Unconverted[0].NewType.Should().BeNull();
    }

    /// <summary>変換不能な SQL Server 型（hierarchyid）が Unconverted に入ることを検証する</summary>
    [Fact(DisplayName = "sqlserver hierarchyid は oracle へ変換できず Unconverted")]
    public void Convert_HierarchyId_IsUnconverted()
    {
        var diagram = BuildDiagram("hierarchyid");

        var plan = DiagramTypeConverter.CreatePlan(diagram, Sql, Ora);

        plan.Converted.Should().BeEmpty();
        plan.Unconverted.Should().ContainSingle();
        plan.Unconverted[0].NewType.Should().BeNull();
    }

    /// <summary>from=Oracle → to=SQL Server の代表変換を検証する</summary>
    [Theory(DisplayName = "oracle → sqlserver の代表変換")]
    [InlineData("NUMBER(10)", "int")]
    [InlineData("NUMBER(19)", "bigint")]
    [InlineData("NUMBER(5)", "smallint")]
    [InlineData("NUMBER(1)", "bit")]
    [InlineData("NVARCHAR2(50)", "nvarchar(50)")]
    [InlineData("VARCHAR2(50)", "varchar(50)")]
    [InlineData("NCLOB", "nvarchar(max)")]
    [InlineData("CLOB", "varchar(max)")]
    [InlineData("BLOB", "varbinary(max)")]
    // Oracle の RAW(n) は正規型 Binary(n)。SQL Server では可変長 varbinary(n) へ寄る
    [InlineData("RAW(16)", "varbinary(16)")]
    [InlineData("BINARY_FLOAT", "real")]
    [InlineData("BINARY_DOUBLE", "float")]
    public void Convert_OracleToSqlServer(string source, string expected)
    {
        var diagram = BuildDiagram(source);

        var plan = DiagramTypeConverter.CreatePlan(diagram, Ora, Sql);

        plan.Converted.Should().ContainSingle();
        plan.Converted[0].NewType.Should().Be(expected);
    }

    /// <summary>変換不能な Oracle 型（ROWID）が Unconverted に入ることを検証する</summary>
    [Fact(DisplayName = "oracle ROWID は sqlserver へ変換できず Unconverted")]
    public void Convert_Rowid_IsUnconverted()
    {
        var diagram = BuildDiagram("ROWID");

        var plan = DiagramTypeConverter.CreatePlan(diagram, Ora, Sql);

        plan.Converted.Should().BeEmpty();
        plan.Unconverted.Should().ContainSingle();
        plan.Unconverted[0].NewType.Should().BeNull();
    }
}
