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

    [Fact(DisplayName = "Entity クラス: 説明ありのテーブルは summary が説明文へ置き換わる")]
    public void EntityClass_WithDescription_UsesDescriptionInSummary()
    {
        var content = Generate(
            BuildDiagram(tableDescription: "顧客マスタ", columnDescription: null)
        );

        content.Should().Contain("/// <summary>顧客マスタ</summary>");
        content.Should().NotContain("/// <summary>Entity for the customers table</summary>");
    }

    [Fact(DisplayName = "Entity クラス: 説明なしのテーブルは従来の定型文のまま")]
    public void EntityClass_WithoutDescription_UsesFallbackSummary()
    {
        var content = Generate(BuildDiagram(tableDescription: null, columnDescription: null));

        content.Should().Contain("/// <summary>Entity for the customers table</summary>");
    }

    [Fact(DisplayName = "Entity プロパティ: 説明ありの列は summary が説明文へ置き換わる")]
    public void EntityProperty_WithDescription_UsesDescriptionInSummary()
    {
        var content = Generate(BuildDiagram(tableDescription: null, columnDescription: "顧客名"));

        content.Should().Contain("/// <summary>顧客名</summary>");
        content.Should().NotContain("/// <summary>Property for the name column</summary>");
    }

    [Fact(DisplayName = "Entity プロパティ: 説明なしの列は従来の定型文のまま")]
    public void EntityProperty_WithoutDescription_UsesFallbackSummary()
    {
        var content = Generate(BuildDiagram(tableDescription: null, columnDescription: null));

        content.Should().Contain("/// <summary>Property for the name column</summary>");
    }

    [Fact(DisplayName = "VO クラス: 説明ありの列は summary が説明文へ置き換わる")]
    public void ValueObject_WithDescription_UsesDescriptionInSummary()
    {
        var options = new CodeGenerationOptions
        {
            RootNamespace = "Sample.Domain",
            GenerateValueObjects = true,
        };
        var content = Generate(
            BuildDiagram(tableDescription: null, columnDescription: "顧客名"),
            options
        );

        // name 列が VO 化され、その VO クラスの summary が説明文になる
        content.Should().Contain("/// <summary>顧客名</summary>");
        content.Should().NotContain("/// <summary>Value object for the name column</summary>");
    }

    [Fact(DisplayName = "EditModel クラス: 説明ありのテーブルは summary が説明文へ置き換わる")]
    public void EditModelClass_WithDescription_UsesDescriptionInSummary()
    {
        var content = Generate(
            BuildDiagram(tableDescription: "顧客マスタ", columnDescription: null)
        );

        content.Should().Contain("/// <summary>顧客マスタ</summary>");
        content
            .Should()
            .NotContain(
                "/// <summary>Edit model for on-screen editing of the customers table.</summary>"
            );
    }

    [Fact(
        DisplayName = "EditModel 入力プロパティ: 説明ありの列はフィールド・公開プロパティ両方の summary が説明文へ置き換わる"
    )]
    public void EditModelInputProperty_WithDescription_UsesDescriptionInBothComments()
    {
        var content = Generate(BuildDiagram(tableDescription: null, columnDescription: "顧客名"));

        // フィールドコメント・公開バインディングプロパティコメントの両方が説明文になる
        content.Should().Contain("/// <summary>顧客名</summary>");
        content.Should().NotContain("/// <summary>On-screen input string for Name.</summary>");
        content
            .Should()
            .NotContain(
                "/// <summary>On-screen input binding string for Name (converted to the confirmed value when set).</summary>"
            );

        // 説明ありの列でも「確定値」コメント（対象外）は定型文のまま残る
        content.Should().Contain("/// <summary>Confirmed value of Name.</summary>");
    }

    [Fact(DisplayName = "XML 特殊文字（& < >）を含む説明は summary で正しくエスケープされる")]
    public void Description_WithXmlSpecialChars_IsEscaped()
    {
        var content = Generate(
            BuildDiagram(tableDescription: null, columnDescription: "A & B < C > D")
        );

        content.Should().Contain("/// <summary>A &amp; B &lt; C &gt; D</summary>");
        // 生の特殊文字が summary へ漏れていないこと
        content.Should().NotContain("/// <summary>A & B < C > D</summary>");
    }

    [Fact(DisplayName = "改行（CRLF/LF）を含む説明は summary で空白へ畳まれ 1 行になる")]
    public void Description_WithLineBreaks_IsCollapsedToSingleLine()
    {
        var content = Generate(
            BuildDiagram(tableDescription: null, columnDescription: "1 行目\r\n2 行目\n3 行目")
        );

        content.Should().Contain("/// <summary>1 行目 2 行目 3 行目</summary>");
        // summary が複数行へ割れていないこと（生の改行が残っていない）
        content.Should().NotContain("1 行目\r\n2 行目");
    }
}
