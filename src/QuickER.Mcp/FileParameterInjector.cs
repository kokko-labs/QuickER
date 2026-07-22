using System.Text.Json;
using System.Text.Json.Nodes;

namespace QuickER.Mcp;

/// <summary>
/// ツール定義の入力スキーマへ <c>file</c> パラメータ（対象図の JSON ファイルパス）を注入するヘルパー。
/// GUI 常駐図ではなくファイルを対象に実行する MCP ツールへ、必須の <c>file</c> 引数を付与するために用いる。
/// </summary>
public static class FileParameterInjector
{
    /// <summary>注入するパラメータ名</summary>
    private const string FileParameterName = "file";

    /// <summary>注入するパラメータの説明</summary>
    private const string FileParameterDescription =
        "Path to the diagram JSON file (DiagramDocument format).";

    /// <summary>
    /// 入力スキーマの <c>properties</c> へ <c>file</c>（type: string）を追加し、<c>required</c> にも加えた
    /// 新しい <see cref="ToolDefinition"/> を返す。元の定義は変更しない（非破壊）。
    /// </summary>
    /// <param name="definition">元のツール定義</param>
    /// <returns><c>file</c> パラメータを注入した新しいツール定義</returns>
    public static ToolDefinition Inject(ToolDefinition definition)
    {
        // 元の InputSchema（匿名型または JsonNode）を独立した JsonObject ツリーへ複製する
        var schema =
            JsonSerializer.SerializeToNode(definition.InputSchema) as JsonObject
            ?? new JsonObject { ["type"] = "object" };

        if (schema["properties"] is not JsonObject properties)
        {
            properties = [];
            schema["properties"] = properties;
        }

        properties[FileParameterName] = new JsonObject
        {
            ["type"] = "string",
            ["description"] = FileParameterDescription,
        };

        if (schema["required"] is not JsonArray required)
        {
            required = [];
            schema["required"] = required;
        }

        var alreadyRequired = required.Any(node =>
            node is not null
            && node.GetValueKind() == JsonValueKind.String
            && node.GetValue<string>() == FileParameterName
        );

        if (!alreadyRequired)
        {
            required.Add(FileParameterName);
        }

        return new ToolDefinition
        {
            Name = definition.Name,
            Description = definition.Description,
            DeferLoading = definition.DeferLoading,
            InputSchema = schema,
        };
    }
}
