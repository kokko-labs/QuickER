using ERDesigner.Models;
using ERDesigner.Services;
using FluentAssertions;

namespace ERDesigner.Tests.Services;

/// <summary><see cref="OpenAiSchemaClient.BuildSystemPrompt"/> のシステムプロンプト組み立てを検証するテストクラス</summary>
public class OpenAiSchemaClientPromptTests
{
    /// <summary>新規生成モードのプロンプトが共通設計原則（複合キー禁止を含む）と命名指示を含むことを検証する</summary>
    [Fact(DisplayName = "新規生成モードのプロンプトは共通設計原則と命名指示を含む")]
    public void BuildSystemPrompt_CreateNew_ContainsPrinciplesAndNamingInstructions()
    {
        var settings = new AiGenerationSettings
        {
            GenerationMode = AiGenerationMode.CreateNew,
            IdentifierNamingStyle = AiIdentifierNamingStyle.SnakeCase,
            TableNameNumberStyle = AiTableNameNumberStyle.Plural,
        };

        var prompt = OpenAiSchemaClient.BuildSystemPrompt(settings);

        prompt.Should().Contain(ErDesignRules.CommonDesignPrinciples);
        prompt.Should().Contain("複合主キー（複数列の主キー）は禁止");
        prompt.Should().Contain("複合外部キーは禁止");
        prompt.Should().Contain("スネークケース");
        prompt.Should().Contain("複数形");
        // JSON 出力形式の指示も含まれる
        prompt.Should().Contain("tables 配列");
        prompt.Should().Contain("relationships");
    }

    /// <summary>更新モードのプロンプトにも複合キー禁止が常に含まれ、既存図 JSON が添付されることを検証する</summary>
    [Fact(DisplayName = "更新モードのプロンプトにも複合キー禁止と既存図 JSON が含まれる")]
    public void BuildSystemPrompt_UpdateExisting_ContainsProhibitionAndExistingDiagram()
    {
        var diagram = new ErDiagram
        {
            Entities =
            [
                new Entity
                {
                    TableName = "Customer",
                    Columns = [new Column { Name = "CustomerId", DataType = "int", IsPrimaryKey = true, IsNullable = false }],
                },
            ],
        };
        var settings = new AiGenerationSettings
        {
            GenerationMode = AiGenerationMode.UpdateExisting,
            ExistingDiagram = diagram,
        };

        var prompt = OpenAiSchemaClient.BuildSystemPrompt(settings);

        prompt.Should().Contain(ErDesignRules.CommonDesignPrinciples);
        prompt.Should().Contain("複合主キー（複数列の主キー）は禁止");
        prompt.Should().Contain("更新後の完全なスキーマ");
        prompt.Should().Contain("Customer");
        prompt.Should().Contain("CustomerId");
    }

    /// <summary>新規生成モードでパスカルケース・単数形の指示行が選ばれることを検証する</summary>
    [Fact(DisplayName = "新規生成モードの既定はパスカルケース・単数形の指示になる")]
    public void BuildSystemPrompt_DefaultStyles_UsesPascalCaseAndSingular()
    {
        var prompt = OpenAiSchemaClient.BuildSystemPrompt(new AiGenerationSettings());

        prompt.Should().Contain("パスカルケース");
        prompt.Should().Contain("単数形");
    }
}
