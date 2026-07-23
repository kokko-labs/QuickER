using System.Text.Json;
using Anthropic.Models.Messages;
using OpenAI.Chat;
using QuickER.Mcp;

namespace QuickER.AI;

/// <summary>
/// 中立なツール定義（<see cref="ToolDefinition"/>）を各 LLM SDK の形式へ変換する変換ヘルパー。
/// </summary>
/// <remarks>
/// 用途に依存しない純粋な形式変換のみを担う（ER 図操作ツールの具体定義は機能側 QuickER.AI.Chat が持つ）。
/// OpenAI 系ドライバ（<see cref="OpenAiTurnDriver"/>）・Anthropic ドライバ（<see cref="AnthropicChatTurnDriver"/>）が使用する。
/// </remarks>
public static class ChatToolConverter
{
    /// <summary>任意のツール定義一覧を OpenAI SDK の <see cref="ChatTool"/> 一覧へ変換する（Function Calling 用）</summary>
    public static IReadOnlyList<ChatTool> ToOpenAiTools(IReadOnlyList<ToolDefinition> definitions)
    {
        return definitions
            .Select(definition =>
                ChatTool.CreateFunctionTool(
                    functionName: definition.Name,
                    functionDescription: definition.Description,
                    functionParameters: BinaryData.FromString(
                        JsonSerializer.Serialize(definition.InputSchema)
                    )
                )
            )
            .ToList();
    }

    /// <summary>任意のツール定義一覧を Anthropic SDK の <see cref="Tool"/> 一覧へ変換する（Claude の Tool Use 用）</summary>
    public static IReadOnlyList<Tool> ToAnthropicTools(IReadOnlyList<ToolDefinition> definitions)
    {
        return definitions.Select(ToAnthropicTool).ToList();
    }

    /// <summary>1 つの dynamicTool 定義を Anthropic の <see cref="Tool"/> へ変換する</summary>
    private static Tool ToAnthropicTool(ToolDefinition definition)
    {
        var schema = JsonSerializer.SerializeToElement(definition.InputSchema);

        var properties = new Dictionary<string, JsonElement>();

        if (
            schema.TryGetProperty("properties", out var props)
            && props.ValueKind == JsonValueKind.Object
        )
        {
            foreach (var property in props.EnumerateObject())
            {
                properties[property.Name] = property.Value.Clone();
            }
        }

        var required = new List<string>();

        if (schema.TryGetProperty("required", out var req) && req.ValueKind == JsonValueKind.Array)
        {
            foreach (var element in req.EnumerateArray())
            {
                if (element.ValueKind == JsonValueKind.String)
                {
                    required.Add(element.GetString()!);
                }
            }
        }

        return new Tool
        {
            Name = definition.Name,
            Description = definition.Description,
            InputSchema = new InputSchema
            {
                Type = JsonSerializer.SerializeToElement("object"),
                Properties = properties,
                Required = required,
            },
        };
    }
}
