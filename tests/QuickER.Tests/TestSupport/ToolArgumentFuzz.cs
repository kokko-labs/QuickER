using System.Text.Json;
using System.Text.Json.Nodes;
using QuickER.Mcp;

namespace QuickER.Tests.TestSupport;

/// <summary>
/// ツールの入力スキーマ（<c>InputSchema</c>）から、引数の形を 1 か所ずつ崩したケースを作る（引数の総当たりのガード用）
/// </summary>
/// <remarks>
/// <para>
/// 起点は「成功する引数」（ベースライン）で、そこから次を 1 つずつ崩す：必須の引数の欠落、各引数の <c>null</c>、
/// スキーマの型と違う種類の値（文字列・数・真偽・配列・オブジェクト）、配列の要素の種類、オブジェクトの配列の要素の
/// 中の引数（欠落・<c>null</c>・型違い。ベースラインのどの要素の中も崩す）、文字列を値に持つ辞書（<c>additionalProperties</c>）の
/// 値の種類（ベースラインのどのキーの値も崩す）。崩した場所が容器（配列・辞書）の最初か 2 番目以降かは <see cref="Case.Position"/> に残す。
/// これとは別に、引数の全体をオブジェクトでない値（配列・文字列・数・真偽・<c>null</c>）に置き換えたケースを、ツールごとに
/// 1 回作る（<see cref="RootCases"/>）。
/// </para>
/// <para>
/// 断るべきケース（<see cref="Case.MustFail"/>）は、必須の欠落と <c>null</c>・型違い・要素や値の種類違い・オブジェクトでない引数の全体。
/// 省略できる引数の <c>null</c> は「指定しなかった」と同じに扱ってよいので、断ることを求めない。
/// どのケースにも「生の例外にならない」「失敗したら状態が変わらない」を表明するのは呼び出し側の役目。
/// </para>
/// <para>
/// 再帰するのはオブジェクトの配列の要素の中だけ。配列の要素でも辞書でもないオブジェクト（<c>properties</c> を持つ引数）が
/// スキーマに現れたら、崩さずに例外で名指しする（作らない入れ子を黙って素通りさせない。水準を足す合図）。
/// </para>
/// </remarks>
internal static class ToolArgumentFuzz
{
    /// <summary>崩したケース 1 件</summary>
    /// <param name="Id">ケース ID（<c>ツール#ベースライン.引数.崩し方</c>）</param>
    /// <param name="Arguments">崩した引数</param>
    /// <param name="MustFail">ツールが断らなければならないか</param>
    /// <param name="Breakage">崩し方（<see cref="Breakages"/> のどれか。網羅の数え上げに使う）</param>
    /// <param name="ArgumentType">崩した引数のスキーマの型（<c>type</c> が無ければ空）</param>
    /// <param name="Required">崩した引数が必須か</param>
    /// <param name="Depth">崩した引数の深さ（-1＝引数の全体・0＝ツールの引数・1＝オブジェクトの配列の要素の中の引数）</param>
    /// <param name="Position">
    /// 崩した場所の容器の中の位置（<see cref="Positions"/> のどれか。配列の要素・辞書の値・配列の要素の中でなければ該当なし）
    /// </param>
    /// <param name="Expected">
    /// 入口の形の検査が返すはずの問題（断るべきケースだけ。崩した場所と崩し方から組み、形の検査を呼んで作らない）
    /// </param>
    internal sealed record Case(
        string Id,
        JsonNode? Arguments,
        bool MustFail,
        string Breakage,
        string ArgumentType,
        bool Required,
        int Depth = 0,
        string Position = Positions.NotApplicable,
        ToolArgumentProblem? Expected = null
    );

    /// <summary>崩した場所の容器の中の位置（<see cref="Case.Position"/>）</summary>
    internal static class Positions
    {
        /// <summary>容器の中でない（ツールの引数そのもの・引数の全体）</summary>
        public const string NotApplicable = "n/a";

        /// <summary>配列の最初の要素・辞書の最初のキーの値</summary>
        public const string First = "first";

        /// <summary>配列の 2 番目以降の要素・辞書の 2 番目以降のキーの値</summary>
        public const string Later = "later";

        /// <summary>容器の中の番号から位置を決める</summary>
        public static string Of(int index) => index == 0 ? First : Later;
    }

    /// <summary>崩し方の名前（<see cref="Case.Breakage"/>）</summary>
    internal static class Breakages
    {
        /// <summary>必須の引数の欠落</summary>
        public const string Missing = "missing";

        /// <summary>引数の <c>null</c></summary>
        public const string Null = "null";

        /// <summary>スキーマの型と違う種類の値</summary>
        public const string WrongType = "wrong-type";

        /// <summary>配列の要素の種類違い（要素 1 つだけの配列）</summary>
        public const string Element = "element";

        /// <summary>正しい要素の後ろに 1 つだけ混ぜた種類違いの要素</summary>
        public const string ElementAfterOk = "element-after-ok";

        /// <summary>文字列を値に持つ辞書の値の種類違い（<c>null</c> を含む）</summary>
        public const string DictionaryValue = "dictionary-value";

        /// <summary>引数の全体をオブジェクトでない値（配列・文字列・数・真偽・<c>null</c>）に置き換える</summary>
        public const string NonObjectRoot = "non-object-root";
    }

    /// <summary>型違いとして試す値（スキーマの型と同じ種類のものは除いて使う）</summary>
    private static readonly (string Kind, Func<JsonNode?> Value)[] Kinds =
    [
        ("string", () => JsonValue.Create("x")),
        ("number", () => JsonValue.Create(1)),
        ("boolean", () => JsonValue.Create(true)),
        ("array", () => new JsonArray()),
        ("object", () => new JsonObject()),
    ];

    /// <summary>引数の全体に当てたケースの型の印（<see cref="Case.ArgumentType"/>。スキーマの引数の型と区別する）</summary>
    public const string RootType = "(arguments)";

    /// <summary>
    /// 引数の全体をオブジェクトでない値に置き換えたケースを列挙する（ツールごとに 1 回。どのツールも断らなければならない）
    /// </summary>
    /// <param name="prefix">ケース ID の接頭辞（<c>ツール#(arguments)</c>）</param>
    public static IEnumerable<Case> RootCases(string prefix) =>
        Kinds
            .Where(candidate => candidate.Kind != "object")
            .Append(("null", () => (JsonNode?)null))
            .Select(candidate => new Case(
                $"{prefix}.{candidate.Kind}",
                candidate.Value(),
                true,
                Breakages.NonObjectRoot,
                RootType,
                true,
                Depth: -1,
                Expected: new(ToolArgumentProblemKind.WrongType, RootType, "object")
            ));

    /// <summary>入口へ渡す引数の JSON の文字列（<c>null</c> は JSON の <c>null</c>）</summary>
    /// <param name="arguments">ケースの引数</param>
    public static string JsonText(JsonNode? arguments) => arguments?.ToJsonString() ?? "null";

    /// <summary>ツール定義の入力スキーマを JSON オブジェクトとして取り出す</summary>
    /// <param name="inputSchema">ツール定義の <c>InputSchema</c>（匿名型または JsonNode）</param>
    public static JsonObject SchemaOf(object inputSchema) =>
        JsonSerializer.SerializeToNode(inputSchema) as JsonObject
        ?? throw new InvalidOperationException("InputSchema must serialize to a JSON object.");

    /// <summary>ベースラインから崩したケースを列挙する</summary>
    /// <param name="prefix">ケース ID の接頭辞（<c>ツール#ベースライン</c>）</param>
    /// <param name="schema">オブジェクトのスキーマ（<c>properties</c> と <c>required</c> を持つ）</param>
    /// <param name="baseline">成功する引数</param>
    /// <param name="pathPrefix">引数の位置の接頭辞（形の検査の問題のパスと同じ書き方。配列の要素の中なら <c>parameters[1].</c>）</param>
    public static IEnumerable<Case> Generate(
        string prefix,
        JsonObject schema,
        JsonObject baseline,
        string pathPrefix = ""
    )
    {
        var properties = schema["properties"] as JsonObject ?? [];
        var required = RequiredNames(schema);

        foreach (var (name, node) in properties)
        {
            var propertySchema = node as JsonObject ?? [];
            var isRequired = required.Contains(name);
            var type = TypeOf(propertySchema);
            var path = pathPrefix + name;

            if (propertySchema.ContainsKey("properties"))
            {
                throw new InvalidOperationException(
                    $"[{prefix}.{name}] 配列の要素でも辞書でもない入れ子のオブジェクト（properties を持つ引数）は崩さない。"
                        + "この形の水準を総当たりに足す（ToolArgumentFuzz.Generate を再帰させ、ToolArgumentShapeFuzzNotGenerated の行を消す）。"
                );
            }

            if (isRequired && baseline.ContainsKey(name))
            {
                yield return new(
                    $"{prefix}.{name}.missing",
                    Without(baseline, name),
                    true,
                    Breakages.Missing,
                    type,
                    isRequired,
                    Expected: Problem(ToolArgumentProblemKind.Missing, path, type)
                );
            }

            // 必須の引数の null は欠落と同じ問題・省略できる引数の null は省略と同じ（断らなくてよい）
            yield return new(
                $"{prefix}.{name}.null",
                With(baseline, name, null),
                isRequired,
                Breakages.Null,
                type,
                isRequired,
                Expected: isRequired ? Problem(ToolArgumentProblemKind.Missing, path, type) : null
            );

            foreach (var (kind, value) in Kinds.Where(candidate => candidate.Kind != type))
            {
                yield return new(
                    $"{prefix}.{name}.{kind}",
                    With(baseline, name, value()),
                    true,
                    Breakages.WrongType,
                    type,
                    isRequired,
                    Expected: Problem(ToolArgumentProblemKind.WrongType, path, type)
                );
            }

            if (type == "array" && propertySchema["items"] is JsonObject items)
            {
                foreach (
                    var nested in ArrayCases(
                        $"{prefix}.{name}",
                        name,
                        path,
                        items,
                        baseline,
                        isRequired
                    )
                )
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
                    .Prepend(("null", () => (JsonNode?)null))
                    .ToArray();
                var existingMap = baseline[name] as JsonObject;
                var keys = existingMap is { Count: > 0 }
                    ? existingMap.Select(pair => pair.Key).ToArray()
                    : ["sqlserver"];

                // どのキーの値も崩す（2 番目以降のキーの値を見落とす形の検査を拾う）
                for (var index = 0; index < keys.Length; index++)
                {
                    foreach (var (kind, value) in badValues)
                    {
                        var map = existingMap is null ? [] : (JsonObject)existingMap.DeepClone();
                        map[keys[index]] = value();
                        yield return new(
                            index == 0
                                ? $"{prefix}.{name}{{{kind}}}"
                                : $"{prefix}.{name}{{{keys[index]}:{kind}}}",
                            With(baseline, name, map),
                            true,
                            Breakages.DictionaryValue,
                            type,
                            isRequired,
                            Position: Positions.Of(index),
                            Expected: Problem(
                                ToolArgumentProblemKind.WrongType,
                                $"{path}.{keys[index]}",
                                valueType
                            )
                        );
                    }
                }
            }
        }
    }

    /// <summary>配列の要素の種類違いと、オブジェクトの配列の要素の中の崩しを列挙する</summary>
    private static IEnumerable<Case> ArrayCases(
        string prefix,
        string name,
        string path,
        JsonObject items,
        JsonObject baseline,
        bool isRequired
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
                true,
                Breakages.Element,
                "array",
                isRequired,
                Position: Positions.First,
                Expected: Problem(ToolArgumentProblemKind.WrongType, $"{path}[0]", itemType)
            );

            // 正しい要素の後ろに 1 つだけ混ぜる（途中まで処理してから落ちる形を拾う）
            if (existing is { Count: > 0 })
            {
                var mixed = (JsonArray)existing.DeepClone();
                mixed.Add(element());
                yield return new(
                    $"{prefix}[ok,{kind}]",
                    With(baseline, name, mixed),
                    true,
                    Breakages.ElementAfterOk,
                    "array",
                    isRequired,
                    Position: Positions.Later,
                    Expected: Problem(
                        ToolArgumentProblemKind.WrongType,
                        $"{path}[{existing.Count}]",
                        itemType
                    )
                );
            }
        }

        if (itemType != "object" || existing is not { Count: > 0 })
        {
            yield break;
        }

        // どの要素の中も崩す（最初の要素だけを見る形の検査を、2 番目以降の要素の崩しで拾う）
        for (var index = 0; index < existing.Count; index++)
        {
            if (existing[index] is not JsonObject element)
            {
                continue;
            }

            foreach (
                var nested in Generate($"{prefix}[{index}]", items, element, $"{path}[{index}].")
            )
            {
                var replaced = (JsonArray)existing.DeepClone();
                replaced[index] = nested.Arguments;
                yield return nested with
                {
                    Arguments = With(baseline, name, replaced),
                    Depth = nested.Depth + 1,
                    Position = Positions.Of(index),
                };
            }
        }
    }

    /// <summary>
    /// 崩した場所と崩し方から組む、形の検査が返すはずの問題（パスは引数名・配列の番号・辞書のキーをつなげた書き方。スキーマの型が
    /// 無い引数は形の検査が見ないので null）
    /// </summary>
    private static ToolArgumentProblem? Problem(
        ToolArgumentProblemKind kind,
        string path,
        string expectedType
    ) => expectedType.Length == 0 ? null : new(kind, path, expectedType);

    /// <summary>
    /// スキーマの必須の引数（ツールの引数の <c>required</c> と、オブジェクトの配列の要素の <c>required</c> を
    /// <c>{引数}[].{名前}</c> と書いたもの。手書きの必須の一覧と照合する）
    /// </summary>
    /// <param name="schema">ツールの入力スキーマ</param>
    public static HashSet<string> RequiredPaths(JsonObject schema)
    {
        var paths = RequiredNames(schema);
        var properties = schema["properties"] as JsonObject ?? [];

        foreach (var (name, node) in properties)
        {
            if (
                node is JsonObject propertySchema
                && TypeOf(propertySchema) == "array"
                && propertySchema["items"] is JsonObject items
            )
            {
                paths.UnionWith(RequiredNames(items).Select(inner => $"{name}[].{inner}"));
            }
        }

        return paths;
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
