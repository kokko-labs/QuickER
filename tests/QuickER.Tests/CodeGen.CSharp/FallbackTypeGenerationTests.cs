using System;
using System.Linq;
using AwesomeAssertions;
using QuickER.CodeGen.CSharp;
using QuickER.Model;
using Xunit;

namespace QuickER.Tests.CodeGen.CSharp;

/// <summary>
/// 型カタログが解析できず <c>string</c> へフォールバックした列（<see cref="CSharpTypeInfo.IsFallbackType"/>）の
/// 生成結果を検証するテストクラス。
/// </summary>
/// <remarks>
/// ここで固定するのは、フォールバックした列のプロパティが通常の <c>string</c> の列と同じに出ること。
/// 生成時 Info 診断（「テーブル.列 (元の DB 型表記)」の名指し・該当列が無い図では出ないこと・生成を止めないこと）は、
/// 決まった行（<c>CodegenCaseTests</c> の <c>diag-fallback-type-*</c>）が持つ。
/// 型解決そのもの（<c>IsFallbackType</c> が立つ条件）は <c>DialectTypeTableTests</c> のマッパーの行（<c>map-*</c>）が固定する。
/// </remarks>
public class FallbackTypeGenerationTests
{
    /// <summary>型カタログが解析できない型の列 <c>location</c> を 1 本含む最小ダイアグラム</summary>
    private static ErDiagram Diagram() =>
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
                            DataType = "geometry",
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

    /// <summary>フォールバック型の列があっても生成コードのプロパティは string で通常どおり出ることを検証する</summary>
    [Fact(DisplayName = "フォールバック型: プロパティは通常どおり string として生成される")]
    public void Generate_FallbackTypeColumn_PropertyIsStringLikeAnyOtherStringColumn()
    {
        var content = SingleFile(Generate(Diagram()));

        content.Should().Contain("public string? Location");
    }
}
