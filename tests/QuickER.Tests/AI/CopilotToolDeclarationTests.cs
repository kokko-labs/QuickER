using System.Text.Json;
using AwesomeAssertions;
using Microsoft.Extensions.AI;
using QuickER.AI;
using QuickER.Mcp;

namespace QuickER.Tests.AI;

/// <summary>
/// 中立なツール定義（<see cref="ToolDefinition"/>）から Copilot SDK のツール宣言
/// （<see cref="CopilotToolDeclaration"/>）への変換を検証するテストクラス。
/// </summary>
/// <remarks>
/// 「呼び出せる実体（<see cref="AIFunction"/>）を伴わない宣言」であることが Copilot 接続の要
/// （SDK が自動実行せず client 側の手動解決へ回す条件）なので、その性質を退行防止として固定する。
/// </remarks>
public class CopilotToolDeclarationTests
{
    private static ToolDefinition SampleDefinition() =>
        new()
        {
            Name = "add_entity",
            Description = "Adds a table.",
            InputSchema = new
            {
                type = "object",
                properties = new { name = new { type = "string" } },
                required = new[] { "name" },
            },
        };

    /// <summary>名前・説明・入力スキーマがそのまま写ることを検証する</summary>
    [Fact(DisplayName = "名前・説明・入力スキーマを写す")]
    public void Declaration_CopiesNameDescriptionAndSchema()
    {
        var declaration = new CopilotToolDeclaration(SampleDefinition());

        declaration.Name.Should().Be("add_entity");
        declaration.Description.Should().Be("Adds a table.");

        var schema = declaration.JsonSchema;
        schema.GetProperty("type").GetString().Should().Be("object");
        schema.GetProperty("properties").TryGetProperty("name", out _).Should().BeTrue();
        schema
            .GetProperty("required")
            .EnumerateArray()
            .Select(element => element.GetString())
            .Should()
            .Equal("name");
    }

    /// <summary>
    /// 呼び出し可能な <see cref="AIFunction"/> ではないことを検証する
    /// （SDK はこの型判定で「自動実行するか、client へ手動解決を委ねるか」を分ける）。
    /// </summary>
    [Fact(DisplayName = "AIFunction ではない（手動解決へ回る宣言）")]
    public void Declaration_IsNotInvocableFunction()
    {
        var declaration = new CopilotToolDeclaration(SampleDefinition());

        declaration.Should().BeAssignableTo<AIFunctionDeclaration>();
        declaration.Should().NotBeAssignableTo<AIFunction>();
    }

    /// <summary>自前実行するツールに毎回の許可プロンプトを出させない印が付くことを検証する</summary>
    [Fact(DisplayName = "skip_permission を立てる")]
    public void Declaration_SkipsPermissionPrompt()
    {
        var declaration = new CopilotToolDeclaration(SampleDefinition());

        declaration
            .AdditionalProperties.Should()
            .ContainKey("skip_permission")
            .WhoseValue.Should()
            .Be(true);
    }

    /// <summary>JsonNode で与えた入力スキーマも同じ形へ写ることを検証する（匿名型以外の経路）</summary>
    [Fact(DisplayName = "JsonNode の入力スキーマも写せる")]
    public void Declaration_AcceptsJsonNodeSchema()
    {
        var definition = new ToolDefinition
        {
            Name = "remove_entity",
            Description = "Removes a table.",
            InputSchema = System.Text.Json.Nodes.JsonNode.Parse(
                """{"type":"object","properties":{"name":{"type":"string"}}}"""
            )!,
        };

        var declaration = new CopilotToolDeclaration(definition);

        declaration.JsonSchema.ValueKind.Should().Be(JsonValueKind.Object);
        declaration.JsonSchema.GetProperty("type").GetString().Should().Be("object");
    }
}
