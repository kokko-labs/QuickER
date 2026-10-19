using System;
using System.Linq;
using AwesomeAssertions;
using QuickER.CodeGen.CSharp;
using QuickER.Model;
using QuickER.Provider;
using QuickER.Provider.SqlServer;
using Xunit;

namespace QuickER.Tests.CodeGen.CSharp;

/// <summary>
/// EF Core の Fluent が、EF Core が意味のある値を作れない単一列の主キー（整数など）に <c>ValueGeneratedNever()</c> を出すことを
/// 検証するテストクラス。
/// </summary>
/// <remarks>
/// EF Core は整数の単一列の主キーを、既定で DB が値を作る列とみなし、0 を「キー未設定」として送らない。生成されるリポジトリは
/// キーをアプリが決めて書くので、QuickER 版と同じく 0 もそのまま送るよう構成する。採番の主キー（<c>IsIdentity</c>）と Guid の
/// 主キーは EF Core の規約のまま（前者は DB の採番、後者は <c>Guid.Empty</c> のとき EF Core が値を作る＝実行時は
/// <see cref="EfCoreGuidKeyExecutionTests"/>）。複合主キーは EF Core が値を作らないので出さない。
/// </remarks>
public class EfCoreValueGeneratedNeverTests
{
    /// <summary>主キーの形を変えた 1 表の図</summary>
    private static ErDiagram BuildDiagram(string keyType, bool composite, bool identity) =>
        new()
        {
            TargetDbms = "sqlserver",
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
                            DataType = keyType,
                            IsPrimaryKey = true,
                            IsIdentity = identity,
                            IsNullable = false,
                        },
                        new Column
                        {
                            Name = "branch_no",
                            DataType = "int",
                            IsPrimaryKey = composite,
                            IsNullable = false,
                        },
                        new Column
                        {
                            Name = "title",
                            DataType = "nvarchar(50)",
                            IsNullable = false,
                        },
                    },
                },
            },
        };

    /// <summary>EF Core の Fluent の Property 行を返す</summary>
    private static string[] PropertyLines(ErDiagram diagram, bool valueObjects)
    {
        var provider = new SqlServerProvider();
        var result = DiagramCodeGenerator.Generate(
            provider.TypeMapper,
            provider.TypeCatalog,
            diagram,
            new CodeGenerationOptions
            {
                RootNamespace = "Test.ValueGeneratedNever",
                GenerateEfCoreRepositories = true,
                GenerateValueObjects = valueObjects,
            }
        );
        result
            .HasErrors.Should()
            .BeFalse(string.Join(" / ", result.Diagnostics.Select(d => d.Message)));

        return result
            .Files.SelectMany(file => file.Content.Split('\n'))
            .Select(line => line.Trim())
            .Where(line => line.StartsWith("entity.Property(", StringComparison.Ordinal))
            .ToArray();
    }

    [Theory(
        DisplayName = "EF Core の Fluent: 整数などの単一列の主キーに ValueGeneratedNever() を出し、ほかの列には出さない"
    )]
    [InlineData("int", false)]
    [InlineData("int", true)]
    [InlineData("bigint", false)]
    [InlineData("nvarchar(20)", false)]
    public void SingleColumnKey_IsValueGeneratedNever(string keyType, bool valueObjects)
    {
        var lines = PropertyLines(
            BuildDiagram(keyType, composite: false, identity: false),
            valueObjects
        );

        lines
            .Single(line =>
                line.StartsWith("entity.Property(e => e.ItemId)", StringComparison.Ordinal)
            )
            .Should()
            .Contain(".ValueGeneratedNever()");
        lines
            .Where(line =>
                !line.StartsWith("entity.Property(e => e.ItemId)", StringComparison.Ordinal)
            )
            .Should()
            .NotContain(line => line.Contains("ValueGeneratedNever", StringComparison.Ordinal));
    }

    [Fact(
        DisplayName = "EF Core の Fluent: 採番の主キー（IsIdentity）には ValueGeneratedNever() を出さない"
    )]
    public void IdentityKey_IsLeftToTheDatabase()
    {
        PropertyLines(BuildDiagram("int", composite: false, identity: true), valueObjects: false)
            .Should()
            .NotContain(line => line.Contains("ValueGeneratedNever", StringComparison.Ordinal));
    }

    [Theory(
        DisplayName = "EF Core の Fluent: Guid の主キーには ValueGeneratedNever() を出さない（EF Core が値を作る）"
    )]
    [InlineData(false)]
    [InlineData(true)]
    public void GuidKey_IsLeftToEfCore(bool valueObjects)
    {
        PropertyLines(
                BuildDiagram("uniqueidentifier", composite: false, identity: false),
                valueObjects
            )
            .Should()
            .NotContain(line => line.Contains("ValueGeneratedNever", StringComparison.Ordinal));
    }

    [Fact(DisplayName = "EF Core の Fluent: 複合主キーには ValueGeneratedNever() を出さない")]
    public void CompositeKey_EmitsNothing()
    {
        PropertyLines(BuildDiagram("int", composite: true, identity: false), valueObjects: false)
            .Should()
            .NotContain(line => line.Contains("ValueGeneratedNever", StringComparison.Ordinal));
    }
}
