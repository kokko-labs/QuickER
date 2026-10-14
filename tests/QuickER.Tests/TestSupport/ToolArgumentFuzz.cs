using System.Text.Json;
using System.Text.Json.Nodes;

namespace QuickER.Tests.TestSupport;

/// <summary>
/// ツールの入力スキーマ（<c>InputSchema</c>）から、引数の形を 1 か所ずつ崩したケースを作る（引数の総当たりのガード用）
/// </summary>
/// <remarks>
/// <para>
/// 起点は「成功する引数」（ベースライン）で、そこから次を 1 つずつ崩す：必須の引数の欠落、各引数の <c>null</c>、
/// スキーマの型と違う種類の値（文字列・数・真偽・配列・オブジェクト）、配列の要素の種類、オブジェクトの配列の要素の
/// 中の引数（欠落・<c>null</c>・型違い）、文字列を値に持つ辞書（<c>additionalProperties</c>）の値の種類。
/// </para>
/// <para>
/// 断るべきケース（<see cref="Case.MustFail"/>）は、必須の欠落と <c>null</c>・型違い・要素や値の種類違い。
/// 省略できる引数の <c>null</c> は「指定しなかった」と同じに扱ってよいので、断ることを求めない。
/// どのケースにも「生の例外にならない」「失敗したら状態が変わらない」を表明するのは呼び出し側の役目。
/// </para>
/// </remarks>
internal static class ToolArgumentFuzz
{
    /// <summary>崩したケース 1 件</summary>
    /// <param name="Id">ケース ID（<c>ツール#ベースライン.引数.崩し方</c>）</param>
    /// <param name="Arguments">崩した引数</param>
    /// <param name="MustFail">ツールが断らなければならないか</param>
    internal sealed record Case(string Id, JsonObject Arguments, bool MustFail);

    /// <summary>型違いとして試す値（スキーマの型と同じ種類のものは除いて使う）</summary>
    private static readonly (string Kind, Func<JsonNode?> Value)[] Kinds =
    [
        ("string", () => JsonValue.Create("x")),
        ("number", () => JsonValue.Create(1)),
        ("boolean", () => JsonValue.Create(true)),
        ("array", () => new JsonArray()),
        ("object", () => new JsonObject()),
    ];

    /// <summary>ツール定義の入力スキーマを JSON オブジェクトとして取り出す</summary>
    /// <param name="inputSchema">ツール定義の <c>InputSchema</c>（匿名型または JsonNode）</param>
    public static JsonObject SchemaOf(object inputSchema) =>
        JsonSerializer.SerializeToNode(inputSchema) as JsonObject
        ?? throw new InvalidOperationException("InputSchema must serialize to a JSON object.");

    /// <summary>ベースラインから崩したケースを列挙する</summary>
    /// <param name="prefix">ケース ID の接頭辞（<c>ツール#ベースライン</c>）</param>
    /// <param name="schema">オブジェクトのスキーマ（<c>properties</c> と <c>required</c> を持つ）</param>
    /// <param name="baseline">成功する引数</param>
    public static IEnumerable<Case> Generate(string prefix, JsonObject schema, JsonObject baseline)
    {
        var properties = schema["properties"] as JsonObject ?? [];
        var required = RequiredNames(schema);

        foreach (var (name, node) in properties)
        {
            var propertySchema = node as JsonObject ?? [];
            var isRequired = required.Contains(name);
            var type = TypeOf(propertySchema);

            if (isRequired && baseline.ContainsKey(name))
            {
                yield return new($"{prefix}.{name}.missing", Without(baseline, name), true);
            }

            yield return new($"{prefix}.{name}.null", With(baseline, name, null), isRequired);

            foreach (var (kind, value) in Kinds.Where(candidate => candidate.Kind != type))
            {
                yield return new($"{prefix}.{name}.{kind}", With(baseline, name, value()), true);
            }

            if (type == "array" && propertySchema["items"] is JsonObject items)
            {
                foreach (var nested in ArrayCases($"{prefix}.{name}", name, items, baseline))
                {
                    yield return nested;
                }
            }

            if (
                type == "object"
                && propertySchema["additionalProperties"] is JsonObject valueSchema
            )
            {
                var valueType = TypeOf(valueSchema);
                var badValues = Kinds
                    .Where(candidate => candidate.Kind != valueType)
                    .Prepend(("null", () => (JsonNode?)null));

                foreach (var (kind, value) in badValues)
                {
                    var map = baseline[name] is JsonObject existing
                        ? (JsonObject)existing.DeepClone()
                        : [];
                    var key = map.Select(pair => pair.Key).FirstOrDefault() ?? "sqlserver";
                    map[key] = value();
                    yield return new($"{prefix}.{name}{{{kind}}}", With(baseline, name, map), true);
                }
            }
        }
    }

    /// <summary>配列の要素の種類違いと、オブジェクトの配列の要素の中の崩しを列挙する</summary>
    private static IEnumerable<Case> ArrayCases(
        string prefix,
        string name,
        JsonObject items,
        JsonObject baseline
    )
    {
        var itemType = TypeOf(items);
        var existing = baseline[name] as JsonArray;
        var badElements = Kinds
            .Where(candidate => candidate.Kind != itemType)
            .Append(("null", () => (JsonNode?)null));

        foreach (var (kind, element) in badElements)
        {
            yield return new(
                $"{prefix}[{kind}]",
                With(baseline, name, new JsonArray(element())),
                true
            );

            // 正しい要素の後ろに 1 つだけ混ぜる（途中まで処理してから落ちる形を拾う）
            if (existing is { Count: > 0 })
            {
                var mixed = (JsonArray)existing.DeepClone();
                mixed.Add(element());
                yield return new($"{prefix}[ok,{kind}]", With(baseline, name, mixed), true);
            }
        }

        if (
            itemType != "object"
            || existing is not { Count: > 0 }
            || existing[0] is not JsonObject first
        )
        {
            yield break;
        }

        foreach (var nested in Generate($"{prefix}[0]", items, first))
        {
            var replaced = (JsonArray)existing.DeepClone();
            replaced[0] = nested.Arguments;
            yield return nested with
            {
                Arguments = With(baseline, name, replaced),
            };
        }
    }

    /// <summary>スキーマの <c>required</c> の名前</summary>
    private static HashSet<string> RequiredNames(JsonObject schema) =>
        schema["required"] is JsonArray required
            ? required.OfType<JsonValue>().Select(value => value.GetValue<string>()).ToHashSet()
            : [];

    /// <summary>スキーマの <c>type</c>（無ければ空）</summary>
    private static string TypeOf(JsonObject schema) =>
        schema["type"] is JsonValue value ? value.GetValue<string>() : string.Empty;

    /// <summary>引数を 1 つ差し替えた複製</summary>
    private static JsonObject With(JsonObject source, string name, JsonNode? value)
    {
        var copy = (JsonObject)source.DeepClone();
        copy[name] = value;
        return copy;
    }

    /// <summary>引数を 1 つ取り除いた複製</summary>
    private static JsonObject Without(JsonObject source, string name)
    {
        var copy = (JsonObject)source.DeepClone();
        copy.Remove(name);
        return copy;
    }
}
