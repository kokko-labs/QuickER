using System.Linq;
using AwesomeAssertions;
using QuickER.CodeGen.CSharp;
using QuickER.Model;
using QuickER.Provider;
using QuickER.Provider.Oracle;
using QuickER.Provider.PostgreSql;
using QuickER.Provider.SqlServer;
using Xunit;

namespace QuickER.Tests.CodeGen.CSharp;

/// <summary>
/// EF Core の Fluent が、Unicode でない文字列の列（varchar 系）に <c>IsUnicode(false)</c> を出すことを検証するテストクラス。
/// </summary>
/// <remarks>
/// 判定の正本は正規型の種類（<c>AnsiString</c> / <c>AnsiFixedString</c>）で、方言の型表記を読み直さない。
/// 出さないと EF Core はその列を nvarchar と見なし、検索・更新のパラメータと定数を Unicode で送るので、
/// SQL Server は列の側を変換してから比べ、索引が効きにくくなる。Unicode の列の Fluent は変わらない。
/// </remarks>
public class EfCoreNonUnicodeColumnTests
{
    /// <summary>主キーと、指定の型の列 2 本（Unicode でない列・Unicode の列）を持つ 1 表の図</summary>
    private static ErDiagram BuildDiagram(
        string targetDbms,
        string nonUnicodeType,
        string unicodeType
    ) =>
        new()
        {
            TargetDbms = targetDbms,
            Entities =
            {
                new Entity
                {
                    TableName = "items",
                    Columns =
                    {
                        new Column
                        {
                            Name = "item_id",
                            DataType = targetDbms == "oracle" ? "NUMBER(10)" : "int",
                            IsPrimaryKey = true,
                            IsNullable = false,
                        },
                        new Column
                        {
                            Name = "code",
                            DataType = nonUnicodeType,
                            IsNullable = false,
                        },
                        new Column
                        {
                            Name = "title",
                            DataType = unicodeType,
                            IsNullable = false,
                        },
                    },
                },
            },
        };

    /// <summary>EF Core の Fluent の、指定のプロパティの行を返す</summary>
    private static string FluentLine(
        IDatabaseProvider provider,
        ErDiagram diagram,
        bool valueObjects,
        string property
    )
    {
        var result = DiagramCodeGenerator.Generate(
            provider.TypeMapper,
            provider.TypeCatalog,
            diagram,
            new CodeGenerationOptions
            {
                RootNamespace = "Test.NonUnicode",
                GenerateEfCoreRepositories = true,
                GenerateValueObjects = valueObjects,
            }
        );
        result
            .HasErrors.Should()
            .BeFalse(string.Join(" / ", result.Diagnostics.Select(d => d.Message)));

        return result
            .Files.SelectMany(file => file.Content.Split('\n'))
            .Single(line => line.Contains($"entity.Property(e => e.{property})"))
            .Trim();
    }

    [Theory(
        DisplayName = "EF Core: SQL Server の varchar 系の列は IsUnicode(false)、nvarchar 系の列は従来どおり"
    )]
    [InlineData("varchar(20)", "nvarchar(50)", false)]
    [InlineData("varchar(20)", "nvarchar(50)", true)]
    [InlineData("char(5)", "nchar(5)", false)]
    [InlineData("text", "ntext", false)]
    [InlineData("varchar(max)", "nvarchar(max)", true)]
    public void SqlServer_NonUnicodeColumn_IsDeclared(
        string nonUnicode,
        string unicode,
        bool valueObjects
    )
    {
        var provider = new SqlServerProvider();
        var diagram = BuildDiagram("sqlserver", nonUnicode, unicode);

        FluentLine(provider, diagram, valueObjects, "Code").Should().Contain(".IsUnicode(false)");
        FluentLine(provider, diagram, valueObjects, "Title").Should().NotContain("IsUnicode");
    }

    [Fact(
        DisplayName = "EF Core: varchar の列の IsUnicode(false) は HasMaxLength の直後、変換（HasConversion）より前に出る"
    )]
    public void NonUnicode_IsEmittedNextToMaxLength()
    {
        var provider = new SqlServerProvider();
        var diagram = BuildDiagram("sqlserver", "varchar(20)", "nvarchar(50)");

        FluentLine(provider, diagram, valueObjects: true, "Code")
            .Should()
            .Contain(".HasMaxLength(20).IsUnicode(false).HasConversion(");
        FluentLine(provider, diagram, valueObjects: false, "Code")
            .Should()
            .EndWith(".HasMaxLength(20).IsUnicode(false);");
    }

    [Fact(DisplayName = "EF Core: Oracle の VARCHAR2 は IsUnicode(false)、NVARCHAR2 は従来どおり")]
    public void Oracle_NonUnicodeColumn_IsDeclared()
    {
        var provider = new OracleProvider();
        var diagram = BuildDiagram("oracle", "VARCHAR2(20)", "NVARCHAR2(50)");

        FluentLine(provider, diagram, false, "Code").Should().Contain(".IsUnicode(false)");
        FluentLine(provider, diagram, false, "Title").Should().NotContain("IsUnicode");
    }

    [Fact(
        DisplayName = "EF Core: PostgreSQL の varchar は Unicode の文字列なので IsUnicode を出さない"
    )]
    public void PostgreSql_Varchar_IsUnicode()
    {
        var provider = new PostgreSqlProvider();
        var diagram = BuildDiagram("postgresql", "varchar(20)", "text");

        FluentLine(provider, diagram, false, "Code").Should().NotContain("IsUnicode");
        FluentLine(provider, diagram, false, "Title").Should().NotContain("IsUnicode");
    }
}
