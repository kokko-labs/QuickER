using System;
using System.Linq;
using AwesomeAssertions;
using QuickER.CodeGen.CSharp;
using QuickER.Model;
using Xunit;

namespace QuickER.Tests.CodeGen.CSharp;

/// <summary>
/// 負のスケールを宣言した decimal 列（PostgreSQL の <c>numeric(10,-2)</c>・Oracle の <c>NUMBER(10,-2)</c>）の
/// 生成時 Info 診断を検証するテストクラス。
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

    private static CodeGenerationResult Generate(ErDiagram diagram, bool generateValueObjects) =>
        new CSharpCodeGenerationService().Generate(
            diagram,
            new CodeGenerationOptions
            {
                RootNamespace = "Sample.Domain",
                GenerateRepositories = true,
                GenerateValueObjects = generateValueObjects,
            }
        );

    /// <summary>負のスケールの列を Info 診断が「テーブル.列 (元の型表記)」で名指しすることを検証する</summary>
    [Fact(DisplayName = "負のスケール: Info 診断が「テーブル.列 (元の型表記)」で名指しする")]
    public void Generate_NegativeScaleColumn_ReportsInfoDiagnostic()
    {
        var result = Generate(Diagram("numeric(10,-2)"), generateValueObjects: true);

        result.HasErrors.Should().BeFalse("名指しは Info であって生成を止めない");
        result
            .Diagnostics.Should()
            .ContainSingle(d =>
                d.Severity == GenerationDiagnosticSeverity.Info
                && d.Message.Contains("orders.amount (numeric(10,-2))", StringComparison.Ordinal)
            );
    }

    /// <summary>値オブジェクトを生成しない構成でも名指しすることを検証する</summary>
    /// <remarks>
    /// 丸められるという事実は値オブジェクトと無関係で、値オブジェクトはオプトインなので、
    /// そちらで絞ると既定の構成では鳴らなくなる。
    /// </remarks>
    [Fact(DisplayName = "負のスケール: 値オブジェクトを生成しない構成でも名指しする")]
    public void Generate_NegativeScaleColumn_ReportsEvenWithoutValueObjects()
    {
        var result = Generate(Diagram("numeric(10,-2)"), generateValueObjects: false);

        result
            .Diagnostics.Should()
            .ContainSingle(d =>
                d.Severity == GenerationDiagnosticSeverity.Info
                && d.Message.Contains("orders.amount (numeric(10,-2))", StringComparison.Ordinal)
            );
    }

    /// <summary>正のスケールでは名指ししないことを検証する（負のアーム）</summary>
    [Theory(DisplayName = "負のスケールでない列は名指ししない")]
    [InlineData("numeric(10,2)")]
    [InlineData("numeric(10)")]
    [InlineData("int")]
    public void Generate_NonNegativeScaleColumn_DoesNotReport(string amountType)
    {
        var result = Generate(Diagram(amountType), generateValueObjects: true);

        result
            .Diagnostics.Should()
            .NotContain(
                d => d.Message.Contains("orders.amount", StringComparison.Ordinal),
                "桁数の情報が載る列については何も通知しない"
            );
    }

    /// <summary>負のスケールでも生成コードは通常どおり decimal で出ることを検証する</summary>
    /// <remarks>
    /// 精度・スケールが載らないだけで、列そのものは扱える。名指しが「生成を諦めた」の意味に読まれないよう固定する。
    /// </remarks>
    [Fact(DisplayName = "負のスケール: プロパティは通常どおり decimal として生成される")]
    public void Generate_NegativeScaleColumn_PropertyIsDecimal()
    {
        var content = Generate(Diagram("numeric(10,-2)"), generateValueObjects: false)
            .Files.Single(f => f.FileName.EndsWith(".g.cs", StringComparison.Ordinal))
            .Content;

        content.Should().Contain("public decimal? Amount");
    }
}
