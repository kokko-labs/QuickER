using System;
using System.Linq;
using AwesomeAssertions;
using QuickER.CodeGen.CSharp;
using QuickER.CodeReverse.CSharp;
using QuickER.Dialects;
using QuickER.Model;
using QuickER.Provider;
using QuickER.Provider.MySql;
using QuickER.Provider.Oracle;
using QuickER.Provider.PostgreSql;
using QuickER.Provider.SqlServer;
using ReverseStrings = QuickER.CodeReverse.CSharp.Resources.Strings;

namespace QuickER.Tests.CodeReverse.CSharp;

/// <summary>
/// 精度なし・スケールありの数値（Oracle の <c>NUMBER(*,s)</c>）が、型トークン <c>decimal(*,s)</c> として
/// 生成コードへ刻まれ、C# リバースで戻ることを検証するテストクラス。
/// </summary>
/// <remarks>
/// <para>
/// トークンがスケールを落として <c>decimal</c> と書くと、リバースで Oracle の素の <c>NUMBER</c>（浮動）へ戻り、
/// 「s 桁へ丸める」宣言が消える。<c>INTEGER</c> の列を取り込んだ図（取込表記 <c>NUMBER(*,0)</c>）では、
/// これが整数の制約の喪失になる。
/// </para>
/// <para>
/// 書き出せるのは Oracle だけで、他方言のカタログでリバースすると、トークン文字列のまま列の型に採り警告する
/// （長さの無いトークンと同じ一般則）。
/// </para>
/// </remarks>
public class CSharpReversePrecisionlessDecimalTests
{
    /// <summary>型表記 1 つを持つ 1 テーブルの Oracle の図を作る</summary>
    private static ErDiagram SingleColumnDiagram(string dataType) =>
        new()
        {
            TargetDbms = "oracle",
            Entities =
            {
                new Entity
                {
                    TableName = "SAMPLES",
                    Columns =
                    {
                        new Column
                        {
                            Name = "SAMPLE_ID",
                            DataType = "NUMBER(10)",
                            IsPrimaryKey = true,
                            IsNullable = false,
                        },
                        new Column
                        {
                            Name = "PROBE",
                            DataType = dataType,
                            IsNullable = true,
                        },
                    },
                },
            },
        };

    private static CodeGenerationResult Generate(ErDiagram diagram)
    {
        var provider = new OracleProvider();

        return DiagramCodeGenerator.Generate(
            provider.TypeMapper,
            provider.TypeCatalog,
            diagram,
            new CodeGenerationOptions
            {
                RootNamespace = "QuickER.Tests.ReversePrecisionless",
                GenerateValueObjects = false,
                SplitFilesByCategory = false,
            }
        );
    }

    /// <summary>
    /// <c>NUMBER(*,s)</c> は <c>decimal(*,s)</c> のトークンで刻まれ（verbatim 併記は不要＝同じ方言へ書き戻すと
    /// 綴りが揃う）、Oracle のカタログでリバースすると元の表記へ戻る。
    /// </summary>
    [Theory(DisplayName = "NUMBER(*,s) は decimal(*,s) のトークンで刻まれ Oracle へ戻る")]
    [InlineData("NUMBER(*,2)", "decimal(*,2)")]
    [InlineData("NUMBER(*,0)", "decimal(*,0)")]
    public void RoundTrip_PrecisionlessScaledNumber_IsPreserved(string dataType, string token)
    {
        var generation = Generate(SingleColumnDiagram(dataType));
        generation.HasErrors.Should().BeFalse();

        var source = generation.Files.Single().Content;
        source.Should().Contain($"[DbColumnMeta(\"{token}\"");
        source.Should().NotContain("NativeType =");

        var reversed = new CSharpReverseParser().Parse(
            source,
            new OracleTypeCatalog(),
            IdentifierLengthLimit.Unlimited
        );
        var column = reversed
            .Entities.Single()
            .Columns.Single(candidate => candidate.Name == "PROBE");

        column.DataType.Should().Be(dataType);
        reversed.Warnings.Should().BeEmpty();
    }

    /// <summary>
    /// 精度なし・スケールありを書き出せない方言では、トークン文字列のまま列の型に採り警告する
    /// （素の decimal / numeric へ黙って落とすと小数部の丸めや桁の宣言が変わる）。
    /// </summary>
    [Theory(DisplayName = "decimal(*,s) は Oracle 以外ではトークン文字列のまま採用され警告が出る")]
    [InlineData("SqlServer")]
    [InlineData("MySql")]
    [InlineData("PostgreSql")]
    public void Parse_PrecisionlessScaledToken_OtherDialects_KeepsTokenAndWarns(string dialect)
    {
        const string Source = """
            namespace Sample;

            [Table("docs")]
            public partial class DocEntity
            {
                [Column("amount")]
                [DbColumnMeta("decimal(*,2)")]
                public decimal? Amount { get; set; }
            }
            """;
        ITypeCatalog catalog = dialect switch
        {
            "SqlServer" => new SqlServerTypeCatalog(),
            "MySql" => new MySqlTypeCatalog(),
            "PostgreSql" => new PostgreSqlTypeCatalog(),
            _ => throw new ArgumentOutOfRangeException(nameof(dialect), dialect, "未知の方言"),
        };

        var result = new CSharpReverseParser().Parse(
            Source,
            catalog,
            IdentifierLengthLimit.Unlimited
        );

        result.Entities.Single().Columns.Single().DataType.Should().Be("decimal(*,2)");
        result
            .Warnings.Should()
            .ContainSingle()
            .Which.Should()
            .Be(
                string.Format(
                    ReverseStrings.Reverse_TypeTokenUnresolved,
                    "decimal(*,2)",
                    "docs",
                    "amount"
                )
            );
    }
}
