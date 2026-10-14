using System;
using System.Linq;
using AwesomeAssertions;
using QuickER.CodeGen.CSharp;
using QuickER.CodeReverse.CSharp;
using QuickER.Model;
using QuickER.Provider;
using QuickER.Provider.MySql;
using QuickER.Provider.SqlServer;
using CodeGenStrings = QuickER.CodeGen.CSharp.Resources.Strings;

namespace QuickER.Tests.CodeReverse.CSharp;

/// <summary>
/// 精度を書かない <c>decimal</c> 列（SQL Server＝<c>decimal(18,0)</c>・MySQL＝<c>decimal(10,0)</c>）が、
/// 方言の既定の精度・スケールの型トークンで刻まれ、元の綴りを併記して C# リバースで警告なしに戻ることを検証する。
/// </summary>
/// <remarks>
/// <para>
/// 型カタログは素の <c>decimal</c> を既定の精度・スケールとして読む。トークンは <c>decimal(18,0)</c> /
/// <c>decimal(10,0)</c> になり、同じ方言へ書き戻すと綴りが揃わない（<c>decimal(18,0)</c> ≠ <c>decimal</c>）ため、
/// 生成物は元の綴りを <c>NativeType</c> として併記し、生成時 Info（綴りが変わる列）で名指しする。
/// </para>
/// <para>
/// 精度なし（任意の小数）と読んでいたときは、両方言とも精度なしの Decimal を書き出せないため、C# リバースが
/// 「トークンを対象方言へ展開できなかった」という偽の警告を出していた（列の型は <c>decimal</c> のまま正しかった）。
/// </para>
/// </remarks>
public class CSharpReverseBareDecimalTests
{
    /// <summary>素の decimal の列 1 本を持つ図を作る</summary>
    private static ErDiagram Diagram(string targetDbms, string keyType) =>
        new()
        {
            TargetDbms = targetDbms,
            Entities =
            {
                new Entity
                {
                    TableName = "samples",
                    Columns =
                    {
                        new Column
                        {
                            Name = "sample_id",
                            DataType = keyType,
                            IsPrimaryKey = true,
                            IsNullable = false,
                        },
                        new Column
                        {
                            Name = "amount",
                            DataType = "decimal",
                            IsNullable = true,
                        },
                    },
                },
            },
        };

    private static (CodeGenerationResult Result, ITypeCatalog Catalog) Generate(string dialect)
    {
        IDatabaseProvider provider = dialect switch
        {
            "SqlServer" => new SqlServerProvider(),
            "MySql" => new MySqlProvider(),
            _ => throw new ArgumentOutOfRangeException(nameof(dialect), dialect, "未知の方言"),
        };
        var diagram = Diagram(provider.Name, provider.TypeCatalog.DefaultDataType);
        var result = DiagramCodeGenerator.Generate(
            provider.TypeMapper,
            provider.TypeCatalog,
            diagram,
            new CodeGenerationOptions
            {
                RootNamespace = "QuickER.Tests.ReverseBareDecimal",
                GenerateValueObjects = false,
                SplitFilesByCategory = false,
            }
        );

        return (result, provider.TypeCatalog);
    }

    /// <summary>
    /// 素の decimal は既定の精度・スケールのトークンで刻まれ、元の綴りが併記され、生成時 Info で名指しされ、
    /// C# リバースで警告なしに <c>decimal</c> へ戻る。
    /// </summary>
    [Theory(DisplayName = "素の decimal は既定の精度のトークンと元の綴りで刻まれ、警告なしに戻る")]
    [InlineData("SqlServer", "decimal(18,0)")]
    [InlineData("MySql", "decimal(10,0)")]
    public void RoundTrip_BareDecimal_KeepsSpellingWithoutWarning(string dialect, string token)
    {
        var (generation, catalog) = Generate(dialect);
        generation.HasErrors.Should().BeFalse();

        var source = generation.Files.Single().Content;
        source.Should().Contain($"[DbColumnMeta(\"{token}\", NativeType = \"decimal\"");

        var prefix = CodeGenStrings.CodeGen_Info_NonCanonicalTypeSpellingColumns.Split('{')[0];
        generation
            .Diagnostics.Should()
            .ContainSingle(diagnostic =>
                diagnostic.Severity == GenerationDiagnosticSeverity.Info
                && diagnostic.Message.StartsWith(prefix, StringComparison.Ordinal)
                && diagnostic.Message.Contains("samples.amount", StringComparison.Ordinal)
            );

        var reversed = new CSharpReverseParser().Parse(
            source,
            catalog,
            IdentifierLengthLimit.Unlimited
        );
        reversed
            .Entities.Single()
            .Columns.Single(column => column.Name == "amount")
            .DataType.Should()
            .Be("decimal");
        reversed.Warnings.Should().BeEmpty();
    }

    /// <summary>
    /// SQL Server の素の decimal は MySQL へ decimal(18,0) として正確に切り替わる（精度なしと読んでいたときは、
    /// MySQL が精度なしの Decimal を書き出せず変換不能になっていた）。
    /// </summary>
    [Fact(DisplayName = "SQL Server の素の decimal は MySQL へ decimal(18,0) として切り替わる")]
    public void Switch_SqlServerBareDecimal_ToMySql_IsExact()
    {
        new SqlServerTypeCatalog().TryParse("decimal", out var canonical).Should().BeTrue();

        new MySqlTypeCatalog().TryFormat(canonical, out var mySqlType).Should().BeTrue();
        mySqlType.Should().Be("decimal(18,0)");
    }
}
