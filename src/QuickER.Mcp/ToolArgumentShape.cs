using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace QuickER.Mcp;

/// <summary>引数の形の問題の種類</summary>
public enum ToolArgumentProblemKind
{
    /// <summary>必須の引数が無い（<c>null</c> も含む）</summary>
    Missing,

    /// <summary>引数・配列の要素・辞書の値の JSON の種類がスキーマと違う</summary>
    WrongType,
}

/// <summary>引数の形の問題 1 件（言語中立。文言は面ごとに組み立てる）</summary>
/// <param name="Kind">問題の種類</param>
/// <param name="Path">問題のある引数の位置（例 <c>table_name</c>・<c>parameters[1].name</c>・<c>sql.sqlite</c>）</param>
/// <param name="ExpectedType">スキーマの型（<c>string</c>・<c>boolean</c>・<c>array</c>・<c>object</c>・<c>number</c>・<c>integer</c>）</param>
public sealed record ToolArgumentProblem(
    ToolArgumentProblemKind Kind,
    string Path,
    string ExpectedType
);

/// <summary>
/// ツールの引数を、ツール定義の入力スキーマ（<see cref="ToolDefinition.InputSchema"/>）の形と突き合わせる
/// </summary>
/// <remarks>
/// <para>
/// ツールを呼ぶ入口（MCP の <see cref="McpToolSet.Execute"/> と内蔵チャットのツール実行口）が、ツールを実行する前に
/// 1 回だけ呼ぶ。見るのは JSON の形だけで、規則は次のとおり。
/// </para>
/// <list type="bullet">
/// <item>必須の引数が無い・<c>null</c> → <see cref="ToolArgumentProblemKind.Missing"/></item>
/// <item>省略できる引数の <c>null</c> → 指定しなかったのと同じに扱う（問題にしない）</item>
/// <item>引数の JSON の種類がスキーマの <c>type</c> と違う → <see cref="ToolArgumentProblemKind.WrongType"/></item>
/// <item>配列の要素の種類が <c>items</c> と違う（<c>null</c> の要素も含む）→ 同上。オブジェクトの要素は中の引数も同じ規則で見る</item>
/// <item>辞書（<c>additionalProperties</c>）の値の種類が違う（<c>null</c> の値も含む）→ 同上</item>
/// </list>
/// <para>
/// <c>enum</c> の値は見ない。各ツールが大文字小文字を無視して解釈し、受け付ける値を挙げたより詳しい文言で断るため。
/// スキーマに無い引数も見ない（無視されるだけで害が無く、断ると呼び手の余分な引数で呼び出しが止まる）。
/// 欠落と型違いを入口で断るのは、ツールが既定値で黙って進めたり、読み捨てたりしないため
/// （例：<c>table_name</c> が数だとテーブル名 "NewTable" の表ができていた）。
/// </para>
/// </remarks>
public static class ToolArgumentShape
{
    /// <summary>入力スキーマを JSON にしたもの（匿名型のスキーマを呼び出しのたびに直列化しない）</summary>
    private static readonly ConditionalWeakTable<object, JsonObject> SchemaCache = [];

    /// <summary>引数を入力スキーマと突き合わせ、最初の問題を返す（スキーマの並び順に見る）</summary>
    /// <param name="inputSchema">ツール定義の入力スキーマ（匿名型または JsonNode）</param>
    /// <param name="arguments">引数（JSON オブジェクト）</param>
    /// <returns>最初の問題。形が合っていれば null</returns>
    public static ToolArgumentProblem? Check(object inputSchema, JsonElement arguments)
    {
        ArgumentNullException.ThrowIfNull(inputSchema);

        var schema = SchemaCache.GetValue(
            inputSchema,
            static source =>
                JsonSerializer.SerializeToNode(source) as JsonObject
                ?? throw new ArgumentException("The input schema must be a JSON object.")
        );

        return arguments.ValueKind == JsonValueKind.Object
            ? CheckObject(schema, arguments, prefix: string.Empty)
            : new ToolArgumentProblem(ToolArgumentProblemKind.WrongType, "(arguments)", "object");
    }

    /// <summary>問題を MCP の応答の英語の 1 文にする（内蔵チャットは resx で同じ内容を組み立てる）</summary>
    /// <param name="problem">問題</param>
    public static string DescribeEnglish(ToolArgumentProblem problem) =>
        problem.Kind switch
        {
            ToolArgumentProblemKind.Missing => $"The argument '{problem.Path}' is required.",
            ToolArgumentProblemKind.WrongType =>
                $"The argument '{problem.Path}' must be a JSON {problem.ExpectedType}.",
            _ => throw new ArgumentOutOfRangeException(nameof(problem), problem.Kind, null),
        };

    /// <summary>オブジェクトの引数をスキーマの <c>properties</c> / <c>required</c> と突き合わせる</summary>
    private static ToolArgumentProblem? CheckObject(
        JsonObject schema,
        JsonElement value,
        string prefix
    )
    {
        var required = schema["required"] is JsonArray names
            ? names.OfType<JsonValue>().Select(name => name.GetValue<string>()).ToHashSet()
            : [];

        if (schema["properties"] is JsonObject properties)
        {
            foreach (var (name, node) in properties)
            {
                var path = prefix + name;

                if (
                    !value.TryGetProperty(name, out var property)
                    || property.ValueKind == JsonValueKind.Null
                )
                {
                    if (required.Contains(name))
                    {
                        return new ToolArgumentProblem(
                            ToolArgumentProblemKind.Missing,
                            path,
                            TypeOf(node as JsonObject)
                        );
                    }

                    continue;
                }

                if (
                    node is JsonObject propertySchema
                    && CheckValue(propertySchema, property, path) is { } problem
                )
                {
                    return problem;
                }
            }
        }

        if (schema["additionalProperties"] is JsonObject valueSchema)
        {
            foreach (var property in value.EnumerateObject())
            {
                if (
                    schema["properties"] is JsonObject declared
                    && declared.ContainsKey(property.Name)
                )
                {
                    continue;
                }

                if (CheckValue(valueSchema, property.Value, prefix + property.Name) is { } problem)
                {
                    return problem;
                }
            }
        }

        return null;
    }

    /// <summary>値 1 つを型のスキーマと突き合わせる（配列・オブジェクトは中まで見る）</summary>
    private static ToolArgumentProblem? CheckValue(
        JsonObject schema,
        JsonElement value,
        string path
    )
    {
        var type = TypeOf(schema);

        if (type.Length == 0)
        {
            return null;
        }

        if (!Matches(type, value))
        {
            return new ToolArgumentProblem(ToolArgumentProblemKind.WrongType, path, type);
        }

        if (type == "array" && schema["items"] is JsonObject items)
        {
            var index = 0;

            foreach (var element in value.EnumerateArray())
            {
                if (CheckValue(items, element, $"{path}[{index}]") is { } problem)
                {
                    return problem;
                }

                index++;
            }
        }

        if (type == "object")
        {
            return CheckObject(schema, value, path + ".");
        }

        return null;
    }

    /// <summary>JSON の値がスキーマの型に合うか（<c>null</c> はどの型にも合わない）</summary>
    private static bool Matches(string type, JsonElement value) =>
        type switch
        {
            "string" => value.ValueKind == JsonValueKind.String,
            "boolean" => value.ValueKind is JsonValueKind.True or JsonValueKind.False,
            "array" => value.ValueKind == JsonValueKind.Array,
            "object" => value.ValueKind == JsonValueKind.Object,
            "number" => value.ValueKind == JsonValueKind.Number,
            "integer" => value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out _),
            _ => true,
        };

    /// <summary>スキーマの <c>type</c>（無ければ空）</summary>
    private static string TypeOf(JsonObject? schema) =>
        schema?["type"] is JsonValue value && value.TryGetValue<string>(out var text)
            ? text
            : string.Empty;
}
