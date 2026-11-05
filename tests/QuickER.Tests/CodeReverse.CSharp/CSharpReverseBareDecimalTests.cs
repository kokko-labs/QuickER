using System;
using System.Linq;
using AwesomeAssertions;
using QuickER.CodeGen.CSharp;
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
    /// 素の decimal の列は、綴りが変わる列として生成時 Info で名指しされる
    /// （トークン・元の綴りの併記・警告なしに decimal へ戻る往復は形式の往復のエンジンの形の行 csharp-bare-decimal-* が見る）
    /// </summary>
    [Theory(DisplayName = "素の decimal は綴りが変わる列として生成時 Info で名指しされる")]
    [InlineData("SqlServer")]
    [InlineData("MySql")]
    public void Generate_BareDecimal_ReportsNonCanonicalSpellingInfo(string dialect)
    {
        var (generation, _) = Generate(dialect);
        generation.HasErrors.Should().BeFalse();

        var prefix = CodeGenStrings.CodeGen_Info_NonCanonicalTypeSpellingColumns.Split('{')[0];
        generation
            .Diagnostics.Should()
            .ContainSingle(diagnostic =>
                diagnostic.Severity == GenerationDiagnosticSeverity.Info
                && diagnostic.Message.StartsWith(prefix, StringComparison.Ordinal)
                && diagnostic.Message.Contains("samples.amount", StringComparison.Ordinal)
            );
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
