using AwesomeAssertions;
using QuickER.CodeGen.CSharp;
using QuickER.Model;

namespace QuickER.Tests.CodeGen.CSharp;

/// <summary>
/// 生成コードの XML doc <c>&lt;summary&gt;</c> へ図の説明（Description）を反映する機能を検証するテストクラス。
/// </summary>
/// <remarks>
/// 図の Description が非空なら summary を説明文へ置き換え、空なら従来の定型文へフォールバックする。
/// 説明は XML doc へ安全に埋め込めるよう <c>&amp;</c>/<c>&lt;</c>/<c>&gt;</c> をエスケープし、改行は空白 1 つへ畳む。
/// ランタイム挙動には影響しないコメントのみの変更で、[DbColumnMeta]/[DbTableMeta] の属性側 Description とは独立に共存する。
/// </remarks>
public class XmlDocDescriptionTests
{
    /// <summary>customers テーブル（PK＋通常列）の図を、テーブル・列の説明を差し込んで構築する</summary>
    private static ErDiagram BuildDiagram(string? tableDescription, string? columnDescription) =>
        new()
        {
            Entities =
            [
                new Entity
                {
                    Id = Guid.NewGuid(),
                    TableName = "customers",
                    Description = tableDescription ?? string.Empty,
                    Columns =
                    [
                        new Column
                        {
                            Id = Guid.NewGuid(),
                            Name = "customer_id",
                            DataType = "int",
                            IsPrimaryKey = true,
                            IsNullable = false,
                        },
                        new Column
                        {
                            Id = Guid.NewGuid(),
                            Name = "name",
                            DataType = "nvarchar(100)",
                            IsNullable = false,
                            Description = columnDescription ?? string.Empty,
                        },
                    ],
                },
            ],
        };

    /// <summary>既定オプション（Entity/EditModel/Mapper 生成）で生成し単一ファイルの中身を返す</summary>
    private static string Generate(ErDiagram diagram) =>
        Generate(diagram, new CodeGenerationOptions { RootNamespace = "Sample.Domain" });

    /// <summary>指定オプションで生成し単一ファイルの中身を返す</summary>
    private static string Generate(ErDiagram diagram, CodeGenerationOptions options)
    {
        var result = new CSharpCodeGenerationService().Generate(diagram, options);
        result.HasErrors.Should().BeFalse();

        return result.Files.Should().ContainSingle().Subject.Content;
    }

    [Fact(DisplayName = "Entity クラス: 説明なしのテーブルは従来の定型文のまま")]
    public void EntityClass_WithoutDescription_UsesFallbackSummary()
    {
        var content = Generate(BuildDiagram(tableDescription: null, columnDescription: null));

        content.Should().Contain("/// <summary>Entity for the customers table</summary>");
    }

    [Fact(DisplayName = "Entity プロパティ: 説明なしの列は従来の定型文のまま")]
    public void EntityProperty_WithoutDescription_UsesFallbackSummary()
    {
        var content = Generate(BuildDiagram(tableDescription: null, columnDescription: null));

        content.Should().Contain("/// <summary>Property for the name column</summary>");
    }

    // 残す理由（P3-4b）：説明ありの列でも「確定値」の XmlDoc は定型文のまま残る（生成器の固定の英文）。フィールド・公開バインディングプロパティの summary が説明文になり、定型文でないこと（持ち主の集合）は行 code-xmldoc-column-description-is-property-summary へ移した
    [Fact(
        DisplayName = "EditModel 入力プロパティ: 説明ありの列でも「確定値」の summary は定型文のまま残る"
    )]
    public void EditModelInputProperty_WithDescription_UsesDescriptionInBothComments()
    {
        var content = Generate(BuildDiagram(tableDescription: null, columnDescription: "顧客名"));

        // 説明ありの列でも「確定値」コメント（対象外）は定型文のまま残る
        content.Should().Contain("/// <summary>Confirmed value of Name.</summary>");
    }
}
