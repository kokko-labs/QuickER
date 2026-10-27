using QuickER.Tests.TestSupport.Engines;

namespace QuickER.Tests.TestSupport;

/// <summary>
/// ツールの引数の総当たり（<see cref="ToolArgumentFuzz"/>）の次元表・網羅しない組の一覧・台帳の行
/// </summary>
/// <remarks>
/// <para>
/// ケースは、<see cref="ToolArgumentFuzz.Generate"/> が作った崩したケース 1 件。観測は 3 つのテスト
/// （MCP の ER 図操作・内蔵チャット・MCP のコード生成）がケースを数える位置（<c>caseCount++</c> の隣）で、ケースが持つ
/// 崩し方・型・必須・深さを読むだけ（ケースの ID・並び・数は変えない）。
/// </para>
/// <para>
/// 範囲は入口（<see cref="McpScope"/>・<see cref="ChatScope"/>・<see cref="CodegenScope"/>）。種を持たないので、網羅しない組は
/// 毎回照合する。
/// </para>
/// </remarks>
internal static class ToolArgumentFuzzDimensions
{
    /// <summary>MCP の ER 図操作ツールの範囲</summary>
    public const string McpScope = "mcp";

    /// <summary>内蔵チャットのツールの範囲</summary>
    public const string ChatScope = "chat";

    /// <summary>MCP のコード生成ツールの範囲</summary>
    public const string CodegenScope = "codegen";

    /// <summary>崩し方</summary>
    public const string Breakage = "Breakage";

    /// <summary>崩した引数のスキーマの型</summary>
    public const string ArgumentType = "ArgumentType";

    /// <summary>崩した引数が必須か</summary>
    public const string Required = "Required";

    /// <summary>崩した引数の深さ</summary>
    public const string Nesting = "Nesting";

    /// <summary>スキーマに <c>type</c> の無い引数（<c>ToolArgumentShape</c> は型を見ない）</summary>
    private const string Untyped = "untyped";

    /// <summary>次元表</summary>
    public static DimensionTable Table { get; } =
        new(
            EngineIds.DiagramEdit,
            [
                new Factor(
                    Breakage,
                    [
                        new Level(ToolArgumentFuzz.Breakages.Missing),
                        new Level(ToolArgumentFuzz.Breakages.Null),
                        new Level(ToolArgumentFuzz.Breakages.WrongType),
                        new Level(ToolArgumentFuzz.Breakages.Element),
                        new Level(ToolArgumentFuzz.Breakages.ElementAfterOk),
                        new Level(ToolArgumentFuzz.Breakages.DictionaryValue),
                    ],
                    "引数の崩し方（必須の欠落・null・型違い・配列の要素・正しい要素の後ろの要素・辞書の値）"
                ),
                new Factor(
                    ArgumentType,
                    [
                        new Level("string"),
                        new Level("number"),
                        new Level("integer"),
                        new Level("boolean"),
                        new Level("array"),
                        new Level("object"),
                        new Level(Untyped),
                    ],
                    "崩した引数の入力スキーマの型（ToolArgumentShape が見分ける 6 つと、型を宣言しない引数）"
                ),
                new Factor(
                    Required,
                    [new Level("Yes"), new Level("No")],
                    "崩した引数がスキーマの required にあるか"
                ),
                new Factor(
                    Nesting,
                    [new Level("Top"), new Level("InArrayElement")],
                    "崩した引数の深さ（ツールの引数か、オブジェクトの配列の要素の中の引数か）"
                ),
            ],
            isValid: IsValid
        );

    /// <summary>どのツールも宣言していない型（3 つの入口に共通）</summary>
    private static readonly string[] UndeclaredTypes = ["number", "integer", Untyped];

    /// <summary>網羅しない組の一覧（範囲は入口）</summary>
    public static CoverageGapSet Gaps { get; } =
        CoverageGap.Set(
            EngineIds.DiagramEdit,
            [
                .. new[] { McpScope, ChatScope, CodegenScope }.SelectMany(UndeclaredTypeGaps),
                .. ScopeGaps(),
            ]
        );

    /// <summary>台帳の行</summary>
    public static EngineDescriptor Descriptor { get; } =
        new(
            EngineIds.DiagramEdit,
            "ToolArgument",
            "ツールの引数の形の総当たり（MCP・内蔵チャット・コード生成）",
            Table,
            0,
            [
                "tests/QuickER.Tests/TestSupport/ToolArgumentFuzz.cs",
                "tests/QuickER.Tests/TestSupport/ToolArgumentFuzzDimensions.cs",
                "tests/QuickER.Tests/TestSupport/ToolArgumentFuzzSeed.cs",
                "tests/QuickER.Tests/TestSupport/ToolArgumentShapeFuzzKnownFailures.cs",
                "tests/QuickER.Tests/Mcp.Tools/ToolArgumentShapeFuzzTests.cs",
                "tests/QuickER.Tests/AI.Chat/ChatToolArgumentShapeFuzzTests.cs",
                "tests/QuickER.Tests/Cli/CodeGenToolArgumentShapeFuzzTests.cs",
            ],
            [
                "src/QuickER.Mcp/ToolArgumentShape.cs",
                "src/QuickER.Mcp/ToolArguments.cs",
                "src/QuickER.Mcp/McpToolSet.cs",
                "src/QuickER.Mcp.Tools",
                "src/QuickER.AI.Chat/ErDiagramHostChatAdapter.cs",
                "src/QuickER.Cli/CodeGenToolSet.cs",
            ]
        );

    /// <summary>崩したケース 1 件を数える（ケースの記録を読むだけ）</summary>
    /// <param name="counter">数える先</param>
    /// <param name="fuzzCase">崩したケース</param>
    public static void Observe(CoverageCounter counter, ToolArgumentFuzz.Case fuzzCase) =>
        counter.Observe(
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [Breakage] = fuzzCase.Breakage,
                [ArgumentType] =
                    fuzzCase.ArgumentType.Length == 0 ? Untyped : fuzzCase.ArgumentType,
                [Required] = fuzzCase.Required ? "Yes" : "No",
                // 2 段以上の入れ子は宣言していない水準＝作るようになったら観測が宣言の誤りとして断る
                [Nesting] = fuzzCase.Depth switch
                {
                    0 => "Top",
                    1 => "InArrayElement",
                    _ => $"Depth{fuzzCase.Depth}",
                },
            }
        );

    /// <summary>どのツールも宣言していない型を、型を含む因子の組ごとに 1 項目にする（ツールのスキーマの事実＝受け入れた組）</summary>
    private static IEnumerable<CoverageGapEntry> UndeclaredTypeGaps(string scope)
    {
        const string reason =
            "この入口のツールの入力スキーマに number・integer の引数も型を宣言しない引数も無い（ToolArgumentShape は 3 つとも見分けるが、崩す相手の引数が無い＝ツールのスキーマの事実で、エンジンを直しても作れない）";

        foreach (var other in new[] { Breakage, Required, Nesting })
        {
            yield return CoverageGap.AcceptedMatching(
                scope,
                "P1-3",
                reason,
                ArgumentType,
                other,
                (type, _) => UndeclaredTypes.Contains(type)
            );
        }
    }

    /// <summary>入口ごとの網羅しない組（ツールの入力スキーマの形から決まる）</summary>
    private static IEnumerable<CoverageGapEntry> ScopeGaps()
    {
        // MCP の ER 図操作と内蔵チャットは同じカタログ（ErDiagramToolCatalog）の入力スキーマを崩すので、同じ形が欠ける
        foreach (var scope in new[] { McpScope, ChatScope })
        {
            yield return AcceptedGap(
                scope,
                "真偽とオブジェクトの引数はどれも省略できる（required に無い）ので、欠落の崩しを作らない（ツールのスキーマの事実で、エンジンを直しても作れない）",
                Breakage,
                ArgumentType,
                (breakage, type) =>
                    breakage == ToolArgumentFuzz.Breakages.Missing && type is "boolean" or "object"
            );
            yield return AcceptedGap(
                scope,
                "文字列を値に持つ辞書の引数（set_query の sql）は省略できる（required に無い。ツールのスキーマの事実で、エンジンを直しても作れない）",
                Breakage,
                Required,
                (breakage, required) =>
                    breakage == ToolArgumentFuzz.Breakages.DictionaryValue && required == "Yes"
            );
            yield return AcceptedGap(
                scope,
                "オブジェクトの配列の要素（set_query の parameters・fields・order_by）は文字列と真偽の引数だけを持ち、配列も辞書も持たない（ツールのスキーマの事実で、エンジンを直しても作れない）",
                Breakage,
                Nesting,
                (breakage, nesting) =>
                    nesting == "InArrayElement"
                    && breakage
                        is ToolArgumentFuzz.Breakages.Element
                            or ToolArgumentFuzz.Breakages.ElementAfterOk
                            or ToolArgumentFuzz.Breakages.DictionaryValue
            );
            yield return AcceptedGap(
                scope,
                "真偽とオブジェクトの引数はどれも省略できる（required に無い。ツールのスキーマの事実で、エンジンを直しても作れない）",
                ArgumentType,
                Required,
                (type, required) => required == "Yes" && type is "boolean" or "object"
            );
            yield return AcceptedGap(
                scope,
                "オブジェクトの配列の要素（set_query の parameters・fields・order_by）は文字列と真偽の引数だけを持ち、配列もオブジェクトも持たない（ツールのスキーマの事実で、エンジンを直しても作れない）",
                ArgumentType,
                Nesting,
                (type, nesting) => nesting == "InArrayElement" && type is "array" or "object"
            );
        }

        // MCP のコード生成（generate_csharp・generate_ddl）の引数は文字列と真偽（force）だけ
        const string noContainers =
            "コード生成の 2 ツール（generate_csharp・generate_ddl）の入力スキーマの引数は文字列と真偽（force）だけで、配列もオブジェクトも持たない（ツールのスキーマの事実で、エンジンを直しても作れない）";

        yield return AcceptedGap(
            CodegenScope,
            noContainers,
            Breakage,
            ArgumentType,
            (_, type) => type is "array" or "object"
        );
        yield return AcceptedGap(
            CodegenScope,
            "真偽の引数（generate_csharp の force）は省略できる（required に無い）ので、欠落の崩しを作らない（ツールのスキーマの事実で、エンジンを直しても作れない）",
            Breakage,
            ArgumentType,
            (breakage, type) => breakage == ToolArgumentFuzz.Breakages.Missing && type == "boolean"
        );
        yield return AcceptedGap(
            CodegenScope,
            noContainers + "（要素と辞書の値の崩しを作れない）",
            Breakage,
            Required,
            (breakage, _) =>
                breakage
                    is ToolArgumentFuzz.Breakages.Element
                        or ToolArgumentFuzz.Breakages.ElementAfterOk
                        or ToolArgumentFuzz.Breakages.DictionaryValue
        );
        yield return AcceptedGap(
            CodegenScope,
            noContainers + "（要素と辞書の値の崩しを作れず、オブジェクトの配列の要素の中も無い）",
            Breakage,
            Nesting,
            (breakage, nesting) =>
                nesting == "InArrayElement"
                || breakage
                    is ToolArgumentFuzz.Breakages.Element
                        or ToolArgumentFuzz.Breakages.ElementAfterOk
                        or ToolArgumentFuzz.Breakages.DictionaryValue
        );
        yield return AcceptedGap(
            CodegenScope,
            noContainers,
            ArgumentType,
            Required,
            (type, _) => type is "array" or "object"
        );
        yield return AcceptedGap(
            CodegenScope,
            "真偽の引数（generate_csharp の force）は省略できる（required に無い。ツールのスキーマの事実で、エンジンを直しても作れない）",
            ArgumentType,
            Required,
            (type, required) => type == "boolean" && required == "Yes"
        );
        yield return AcceptedGap(
            CodegenScope,
            noContainers + "（オブジェクトの配列の要素の中が無い）",
            ArgumentType,
            Nesting,
            (type, nesting) => nesting == "InArrayElement" || type is "array" or "object"
        );
        yield return AcceptedGap(
            CodegenScope,
            noContainers + "（オブジェクトの配列の要素の中が無い）",
            Required,
            Nesting,
            (_, nesting) => nesting == "InArrayElement"
        );
    }

    /// <summary>受け入れた網羅しない組の項目（指摘 P1-3・ツールのスキーマの事実で作れない組）</summary>
    private static CoverageGapEntry AcceptedGap(
        string scope,
        string reason,
        string factorA,
        string factorB,
        Func<string, string, bool> predicate
    ) => CoverageGap.AcceptedMatching(scope, "P1-3", reason, factorA, factorB, predicate);

    /// <summary>原理的に起こり得ない組み合わせのときだけ偽（判断に要る因子が割り当てに無ければ真）</summary>
    private static bool IsValid(IReadOnlyDictionary<string, string> assignment)
    {
        assignment.TryGetValue(Breakage, out var breakage);
        assignment.TryGetValue(ArgumentType, out var type);
        assignment.TryGetValue(Required, out var required);

        // 省略できる引数を省くのは「指定しなかった」という正しい入力で、崩し方にならない（欠落は必須の引数だけの崩し方）
        if (breakage == ToolArgumentFuzz.Breakages.Missing && required == "No")
        {
            return false;
        }

        if (breakage is not null && type is not null)
        {
            // 要素の種類違いは配列の引数にしか作れない（要素を持つのは配列だけ）
            if (
                breakage
                    is ToolArgumentFuzz.Breakages.Element
                        or ToolArgumentFuzz.Breakages.ElementAfterOk
                && type != "array"
            )
            {
                return false;
            }

            // 辞書の値の種類違いはオブジェクトの引数にしか作れない（値を持つのはオブジェクトだけ）
            if (breakage == ToolArgumentFuzz.Breakages.DictionaryValue && type != "object")
            {
                return false;
            }
        }

        return true;
    }
}
