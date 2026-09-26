using System;
using System.Linq;
using AwesomeAssertions;
using QuickER.CodeGen.CSharp;
using QuickER.Model;
using Xunit;

namespace QuickER.Tests.CodeGen.CSharp;

/// <summary>
/// 型カタログが解析できず <c>string</c> へフォールバックした列（<see cref="CSharpTypeInfo.IsFallbackType"/>）の
/// 生成時 Info 診断を検証するテストクラス。
/// </summary>
/// <remarks>
/// 固定するのは 3 点: 該当列がある図では「テーブル.列 (元の DB 型表記)」形式で Info 診断が名指しすること、
/// 該当列が無い図では診断が出ないこと（負のアーム）、フォールバックは生成自体を止めないこと。
/// 型解決そのもの（<c>IsFallbackType</c> が立つ条件）は各方言の <c>*CSharpTypeMapperTests</c> が固定する。
/// </remarks>
public class FallbackTypeGenerationTests
{
    /// <summary>未知の型を持つ列 1 本を含む最小ダイアグラム</summary>
    /// <param name="includeUnknownType">true なら <c>location</c> 列に型カタログが解析できない型を与える</param>
    private static ErDiagram Diagram(bool includeUnknownType) =>
        new()
        {
            Entities =
            [
                new Entity
                {
                    Id = Guid.NewGuid(),
                    TableName = "items",
                    Columns =
                    [
                        new Column
                        {
                            Id = Guid.NewGuid(),
                            Name = "item_id",
                            DataType = "int",
                            IsPrimaryKey = true,
                            IsNullable = false,
                        },
                        new Column
                        {
                            Id = Guid.NewGuid(),
                            Name = "location",
                            DataType = includeUnknownType ? "geometry" : "nvarchar(50)",
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

    private static string SingleFile(CodeGenerationResult result) =>
        result.Files.Single(f => f.FileName.EndsWith(".g.cs", StringComparison.Ordinal)).Content;

    /// <summary>フォールバック型の列を Info 診断が「テーブル.列 (元の DB 型表記)」で名指しすることを検証する</summary>
    [Fact(DisplayName = "フォールバック型: Info 診断が「テーブル.列 (元の型表記)」で名指しする")]
    public void Generate_FallbackTypeColumn_ReportsInfoDiagnosticWithTableColumnAndDataType()
    {
        var result = Generate(Diagram(includeUnknownType: true));

        result.HasErrors.Should().BeFalse("フォールバックは Info であって生成を止めない");
        result
            .Diagnostics.Should()
            .ContainSingle(d =>
                d.Severity == GenerationDiagnosticSeverity.Info
                && d.Message.Contains("items.location (geometry)", StringComparison.Ordinal)
            );
    }

    /// <summary>フォールバック型の列が無い図では Info 診断が出ないことを検証する（負のアーム）</summary>
    [Fact(DisplayName = "フォールバック型なし: Info 診断は出ない")]
    public void Generate_NoFallbackTypeColumn_DoesNotReportInfoDiagnostic()
    {
        var result = Generate(Diagram(includeUnknownType: false));

        result
            .Diagnostics.Should()
            .NotContain(
                d => d.Message.Contains("items.location", StringComparison.Ordinal),
                "解決できる型なら何も通知しない"
            );
    }

    /// <summary>フォールバック型の列があっても生成コードのプロパティは string で通常どおり出ることを検証する</summary>
    [Fact(DisplayName = "フォールバック型: プロパティは通常どおり string として生成される")]
    public void Generate_FallbackTypeColumn_PropertyIsStringLikeAnyOtherStringColumn()
    {
        var content = SingleFile(Generate(Diagram(includeUnknownType: true)));

        content.Should().Contain("public string? Location");
    }
}
