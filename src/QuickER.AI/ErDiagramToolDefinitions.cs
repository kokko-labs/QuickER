using System.Text.Json;
using Anthropic.Models.Messages;
using OpenAI.Chat;

namespace QuickER.AI;

/// <summary>ER 図操作ツールの定義（スキーマ）。Codex dynamicTools / OpenAI / Anthropic の各形式へ変換する</summary>
/// <remarks>VM 非依存の純粋なツール定義。実行は app 側 <c>ErDiagramDynamicTools.Execute</c> が担う</remarks>
public static class ErDiagramToolDefinitions
{
    /// <summary>ER 図操作ツール定義を OpenAI SDK の <see cref="ChatTool"/> 一覧へ変換する（Function Calling 用）</summary>
    /// <remarks>定義・説明文は <see cref="GetDefinitions"/> と共有し、二重管理を避ける</remarks>
    public static IReadOnlyList<ChatTool> ToOpenAiTools() => ToOpenAiTools(GetDefinitions());

    /// <summary>任意のツール定義一覧を OpenAI SDK の <see cref="ChatTool"/> 一覧へ変換する（用途プロファイル対応）</summary>
    public static IReadOnlyList<ChatTool> ToOpenAiTools(
        IReadOnlyList<CodexDynamicToolDefinition> definitions
    )
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

    /// <summary>ER 図操作ツール定義を Anthropic SDK の <see cref="Tool"/> 一覧へ変換する（Claude の Tool Use 用）</summary>
    /// <remarks>定義・説明文・入力スキーマは <see cref="GetDefinitions"/> と共有し、二重管理を避ける</remarks>
    public static IReadOnlyList<Tool> ToAnthropicTools() => ToAnthropicTools(GetDefinitions());

    /// <summary>任意のツール定義一覧を Anthropic SDK の <see cref="Tool"/> 一覧へ変換する（用途プロファイル対応）</summary>
    public static IReadOnlyList<Tool> ToAnthropicTools(
        IReadOnlyList<CodexDynamicToolDefinition> definitions
    )
    {
        return definitions.Select(ToAnthropicTool).ToList();
    }

    /// <summary>1 つの dynamicTool 定義を Anthropic の <see cref="Tool"/> へ変換する</summary>
    private static Tool ToAnthropicTool(CodexDynamicToolDefinition definition)
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

    /// <summary>全 dynamicTool の定義一覧を返す</summary>
    public static IReadOnlyList<CodexDynamicToolDefinition> GetDefinitions()
    {
        return
        [
            new CodexDynamicToolDefinition
            {
                Name = "get_diagram_summary",
                Description =
                    "現在の ER 図のエンティティ（テーブル）とリレーション（外部キー）の一覧をテキストで返します。",
                DeferLoading = false,
                InputSchema = new
                {
                    type = "object",
                    properties = new { },
                    required = Array.Empty<string>(),
                },
            },
            new CodexDynamicToolDefinition
            {
                Name = "add_entity",
                Description =
                    "新しいエンティティ（テーブル）を ER 図に追加します。列は作成されないので、追加後にまず主キー列を 1 列だけ add_column で定義し、続けてその他の列を定義してください。",
                DeferLoading = false,
                InputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        table_name = new { type = "string", description = "テーブル名" },
                        description = new
                        {
                            type = "string",
                            description = "テーブルの説明（省略可）",
                        },
                    },
                    required = new[] { "table_name" },
                },
            },
            new CodexDynamicToolDefinition
            {
                Name = "remove_entity",
                Description = "指定したテーブル名のエンティティを ER 図から削除します。",
                DeferLoading = false,
                InputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        table_name = new { type = "string", description = "削除するテーブル名" },
                    },
                    required = new[] { "table_name" },
                },
            },
            new CodexDynamicToolDefinition
            {
                Name = "add_column",
                Description =
                    $"指定したテーブルにカラムを追加します。{ErDesignRules.SinglePrimaryKeyRule}キー相当の列を複数持たせたい場合は 2 列目以降を is_primary_key=false で追加し、参照は add_relationship で定義してください。",
                DeferLoading = false,
                InputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        table_name = new { type = "string", description = "テーブル名" },
                        column_name = new { type = "string", description = "カラム名" },
                        data_type = new
                        {
                            type = "string",
                            description = "データ型（例: int, nvarchar(100), datetime2）",
                        },
                        is_primary_key = new { type = "boolean", description = "主キーかどうか" },
                        is_nullable = new
                        {
                            type = "boolean",
                            description = "NULL を許可するかどうか",
                        },
                        description = new
                        {
                            type = "string",
                            description = "カラムの説明（省略可）",
                        },
                    },
                    required = new[] { "table_name", "column_name", "data_type" },
                },
            },
            new CodexDynamicToolDefinition
            {
                Name = "remove_column",
                Description = "指定したテーブルからカラムを削除します。",
                DeferLoading = false,
                InputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        table_name = new { type = "string", description = "テーブル名" },
                        column_name = new { type = "string", description = "削除するカラム名" },
                    },
                    required = new[] { "table_name", "column_name" },
                },
            },
            new CodexDynamicToolDefinition
            {
                Name = "set_entity_property",
                Description = "エンティティのプロパティ（テーブル名、メモ、説明）を変更します。",
                DeferLoading = false,
                InputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        table_name = new { type = "string", description = "変更対象のテーブル名" },
                        new_table_name = new
                        {
                            type = "string",
                            description = "新しいテーブル名（変更する場合）",
                        },
                        memo = new { type = "string", description = "メモ（変更する場合）" },
                        description = new { type = "string", description = "説明（変更する場合）" },
                    },
                    required = new[] { "table_name" },
                },
            },
            new CodexDynamicToolDefinition
            {
                Name = "set_column_property",
                Description =
                    "指定したテーブルのカラムのプロパティ（説明、データ型、NULL 許容）を変更します。description / data_type / is_nullable のうち少なくとも 1 つを必ず指定してください。",
                DeferLoading = false,
                InputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        table_name = new { type = "string", description = "変更対象のテーブル名" },
                        column_name = new { type = "string", description = "変更対象のカラム名" },
                        description = new
                        {
                            type = "string",
                            description = "カラムの説明（変更する場合に指定）",
                        },
                        data_type = new
                        {
                            type = "string",
                            description = "データ型（変更する場合に指定。例: int, nvarchar(100), datetime2）",
                        },
                        is_nullable = new
                        {
                            type = "boolean",
                            description = "NULL 許容フラグ（変更する場合に指定）",
                        },
                    },
                    required = new[] { "table_name", "column_name" },
                },
            },
            new CodexDynamicToolDefinition
            {
                Name = "add_relationship",
                Description =
                    $"2 つのテーブル間にリレーション（外部キー）を追加します。参照列を確実に指定するため、source_column（参照元の主キー列名）と target_column（参照先の外部キー列名）も指定してください。{ErDesignRules.SingleColumnForeignKeyRule}役割が異なる複数の外部キーは別リレーションとして追加してください。",
                DeferLoading = false,
                InputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        source_table = new
                        {
                            type = "string",
                            description = "参照元（親）テーブル名",
                        },
                        source_column = new
                        {
                            type = "string",
                            description = "参照元（親）テーブルの参照カラム名。通常は主キー列（省略時は主キー列を使用）",
                        },
                        target_table = new
                        {
                            type = "string",
                            description = "参照先（子）テーブル名",
                        },
                        target_column = new
                        {
                            type = "string",
                            description = "参照先（子）テーブルの外部キーカラム名（省略時はカラム名から推測し、推測できなければ未割当）",
                        },
                        relationship_type = new
                        {
                            type = "string",
                            @enum = new[] { "OneToOne", "OneToMany", "ManyToMany" },
                            description = "リレーション種別",
                        },
                    },
                    required = new[] { "source_table", "target_table", "relationship_type" },
                },
            },
            new CodexDynamicToolDefinition
            {
                Name = "remove_relationship",
                Description = "2 つのテーブル間のリレーションを削除します。",
                DeferLoading = false,
                InputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        source_table = new { type = "string", description = "参照元テーブル名" },
                        target_table = new { type = "string", description = "参照先テーブル名" },
                    },
                    required = new[] { "source_table", "target_table" },
                },
            },
        ];
    }
}
