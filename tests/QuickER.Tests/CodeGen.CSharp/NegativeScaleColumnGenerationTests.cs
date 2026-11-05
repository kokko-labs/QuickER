using System;
using System.Linq;
using AwesomeAssertions;
using QuickER.CodeGen.CSharp;
using QuickER.Model;
using Xunit;

namespace QuickER.Tests.CodeGen.CSharp;

/// <summary>
/// 負のスケールを宣言した decimal 列（PostgreSQL の <c>numeric(10,-2)</c>・Oracle の <c>NUMBER(10,-2)</c>）の
/// 生成結果を検証するテストクラス。
/// </summary>
/// <remarks>
/// <para>
/// 5 方言の型マッパーは負のスケールを<b>意図的に読まない</b>（正規表現が一致せず精度・スケールとも null）。
/// そのため生成コードには桁数の情報が一切載らない一方、DB 側は値を丸める（<c>numeric(10,-2)</c> は
/// 1234 を 1200 として保存する）。どちらも生成物を読んでも気づけないので、生成時に名指しする。
/// </para>
/// <para>
/// <b>Warning ではなく Info。</b>負のスケールは誤って書く型ではなく「DB に百単位で丸めさせる」ことを
/// 意図して書くものなので、生成のたびに警告で鳴らすと形骸化する
/// （<see cref="FallbackTypeGenerationTests"/> と同じ理屈）。
/// </para>
/// <para>
/// 生成時 Info 診断（VO の有無・正と 0 のスケール・整数の型では出ないこと）は、決まった行（<c>CodegenCaseTests</c> の
/// <c>diag-negative-scale-*</c>）が持つ。ここではプロパティが通常どおり decimal で出ることを固定する。
/// </para>
/// </remarks>
public class NegativeScaleColumnGenerationTests
{
    /// <summary>金額列 1 本を持つ最小ダイアグラム</summary>
    /// <param name="amountType">金額列の DB 型表記</param>
    private static ErDiagram Diagram(string amountType) =>
        new()
        {
            TargetDbms = "postgresql",
            Entities =
            [
                new Entity
                {
                    Id = Guid.NewGuid(),
                    TableName = "orders",
                    Columns =
                    [
                        new Column
                        {
                            Id = Guid.NewGuid(),
                            Name = "order_id",
                            DataType = "int",
                            IsPrimaryKey = true,
                            IsNullable = false,
                        },
                        new Column
                        {
                            Id = Guid.NewGuid(),
                            Name = "amount",
                            DataType = amountType,
                            IsNullable = true,
                        },
                    ],
                },
            ],
        };

    private static CodeGenerationResult Generate(ErDiagram diagram) =>
        new CSharpCodeGenerationService().Generate(
            diagram,
            new CodeGenerationOptions
            {
                RootNamespace = "Sample.Domain",
                GenerateRepositories = true,
            }
        );

    /// <summary>負のスケールでも生成コードは通常どおり decimal で出ることを検証する</summary>
    /// <remarks>
    /// 精度・スケールが載らないだけで、列そのものは扱える。名指しが「生成を諦めた」の意味に読まれないよう固定する。
    /// </remarks>
    [Fact(DisplayName = "負のスケール: プロパティは通常どおり decimal として生成される")]
    public void Generate_NegativeScaleColumn_PropertyIsDecimal()
    {
        var content = Generate(Diagram("numeric(10,-2)"))
            .Files.Single(f => f.FileName.EndsWith(".g.cs", StringComparison.Ordinal))
            .Content;

        content.Should().Contain("public decimal? Amount");
    }
}
