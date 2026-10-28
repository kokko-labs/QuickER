using QuickER.Tests.TestSupport.Engines;

namespace QuickER.Tests.TestSupport;

/// <summary>
/// 名前の総当たりの入口（<see cref="NameFuzzRunner.Run"/>）の次元表・網羅しない組の一覧・台帳の行
/// </summary>
/// <remarks>
/// <para>
/// ケースは、入口へ実際に流した名前の組 1 つ（入口が 1 つだけでも受け付けない名前の組と、作る C# の識別子が衝突する組を
/// 飛ばした後）。観測は <see cref="NameFuzzRunner.Run"/> が組を流す直前で、組の記録を読むだけ（乱数を引かない）。
/// </para>
/// <para>
/// 名前の種類（<see cref="Kind"/>）は入口ごとに 1 つに固定する（入口が受け持つ種類だけが網羅すべき組になる）。範囲は入口の名前。
/// 網羅しない組の照合は既定の種の数のときだけ行い、既知の失敗の照合の後に回す（<see cref="FuzzFailureLog.AssertMatchesKnown"/>）。
/// </para>
/// <para>
/// 実体は QuickER.Tests の TestSupport で、Integration も <c>NameFuzz*.cs</c> の glob で同じファイルをコンパイルする
/// （<see cref="NameFuzzRunner"/> がこの型を参照するため）。台帳の行は QuickER.Tests の台帳だけが載せる。
/// </para>
/// </remarks>
public static class NameFuzzDimensions
{
    /// <summary>名前の種類</summary>
    public const string Kind = "Kind";

    /// <summary>名前の組の違い</summary>
    public const string Difference = "Difference";

    /// <summary>名前が前後に空白を持つか（<see cref="NameFuzzRunner.Run"/> の性質の破れのキーと同じ分け方）</summary>
    public const string Shape = "Shape";

    /// <summary>どちらの名前も前後に空白を持たない</summary>
    public const string Plain = "Plain";

    /// <summary>どちらかの名前が前後に空白を持つ</summary>
    public const string WithWhitespace = "WithWhitespace";

    /// <summary>次元表</summary>
    public static DimensionTable Table { get; } =
        new(
            EngineIds.Name,
            [
                new Factor(
                    Kind,
                    Enum.GetValues<NameKind>()
                        .Select(value => new Level(value.ToString(), AxisMember.Of(value)))
                        .ToList(),
                    "名前の種類（入口ごとに 1 つに固定する）"
                ),
                new Factor(
                    Difference,
                    Enum.GetValues<NameDifference>()
                        .Select(value => new Level(value.ToString(), AxisMember.Of(value)))
                        .ToList(),
                    "名前の組の違い（NameIdentityRule.Classify）"
                ),
                new Factor(
                    Shape,
                    [new Level(Plain), new Level(WithWhitespace)],
                    "名前が前後に空白を持つか（どちらかが持てば WithWhitespace）"
                ),
            ],
            isValid: IsValid
        );

    /// <summary>
    /// 入口の名前（<see cref="NameFuzzRunner.Run"/> の entrance。網羅しない組の範囲。入口を足したらここにも足す＝
    /// 足さなければ、その入口の欠けが一覧に無い欠けとして赤になる）
    /// </summary>
    private static readonly string[] Entrances =
    [
        "codegen:table",
        "codegen:column",
        "codegen:query_name",
        "codegen:parameter",
        "codegen:field",
        "query-dialog:query_name",
        "query-dialog:parameter",
        "query-dialog:field",
        "query-tool:query_name",
        "query-tool:parameter",
        "query-tool:field",
        .. new[] { "gui-tool:", "mcp-tool:" }.SelectMany(prefix =>
            new[]
            {
                "add_entity",
                "set_entity_property",
                "add_column",
                "set_unique_constraint",
                "add_relationship",
            }.Select(tool => prefix + tool)
        ),
        "gui-vm:EntityViewModel.TableName",
        "gui-vm:ColumnViewModel.Name",
        .. new[] { "dbml", "excel", "mermaid" }.SelectMany(format =>
            new[] { $"import:{format}:table", $"import:{format}:column" }
        ),
        "db-import:sqlite:table",
        "db-import:sqlite:column",
    ];

    /// <summary>
    /// 名前から C# の識別子を作る入口（<c>derivedName</c> に <c>CSharpNameConverter</c> 由来の変換を渡すもの）
    /// </summary>
    private static readonly string[] IdentifierDerivingEntrances =
    [
        "codegen:table",
        "codegen:column",
        "codegen:query_name",
        "codegen:field",
        "query-dialog:query_name",
        "query-tool:query_name",
    ];

    /// <summary>網羅しない組の一覧（範囲は入口の名前）</summary>
    public static CoverageGapSet Gaps { get; } =
        CoverageGap.Set(
            EngineIds.Name,
            [
                .. Entrances.SelectMany(entrance =>
                    FoldGaps(
                        entrance,
                        NameDifference.CaseUpperFoldOnly,
                        "生成器の名前（NameFuzz の BaseNames と FixedPairs）に、OrdinalIgnoreCase では同じで ToLowerInvariant では違う組を作る文字が無い（例に挙げた点の無い i〔U+0131〕は .NET の OrdinalIgnoreCase でも I と同じにならず、FixedPairs の (ıd, ID) は Distinct に分類される）"
                    )
                ),
                .. IdentifierDerivingEntrances.SelectMany(entrance =>
                    FoldGaps(
                        entrance,
                        NameDifference.CaseLowerFoldOnly,
                        "小文字化だけで同じになる組はケルビン記号の名前（K〔U+212A〕ey と key）だけで、CSharpNameConverter は単語を ToLowerInvariant してから先頭を大文字にするので、作る識別子が同じ（Key）になり、Run が識別子の衝突として組ごと飛ばす"
                    )
                ),
            ]
        );

    /// <summary>台帳の行（入口）</summary>
    public static EngineDescriptor Descriptor { get; } =
        new(
            EngineIds.Name,
            "Entrances",
            "名前の総当たり（GUI・ツール・取込・クエリ定義・C# 生成の入口）",
            Table,
            0,
            [
                "tests/QuickER.Tests/TestSupport/NameFuzz.cs",
                "tests/QuickER.Tests/TestSupport/NameFuzzDimensions.cs",
                "tests/QuickER.Tests/TestSupport/NameFuzzKnownFailures.cs",
                "tests/QuickER.Tests/CodeGen.CSharp/NameIdentityGenerationFuzzTests.cs",
                "tests/QuickER.Tests/CodeGen.UI/NameIdentityQueryDialogFuzzTests.cs",
                "tests/QuickER.Tests/Gui/Services/NameIdentityImportFuzzTests.cs",
                "tests/QuickER.Tests/Gui/ViewModels/NameIdentityViewModelFuzzTests.cs",
                "tests/QuickER.Tests/Mcp.Tools/NameIdentityToolFuzzTests.cs",
                "tests/QuickER.Tests/Provider.Sqlite/NameIdentitySqliteImportFuzzTests.cs",
            ],
            [
                "src/QuickER.Model/SchemaNames.cs",
                "src/QuickER.Mcp.Tools",
                "src/QuickER.Gui/ViewModels",
                "src/QuickER.Gui/Services",
                "src/QuickER.CodeGen.UI",
                "src/QuickER.CodeGen.CSharp",
                "src/QuickER.Provider.Sqlite",
            ]
        );

    /// <summary>入口が受け持つ名前の種類を固定する水準</summary>
    /// <param name="kind">入口の名前の種類</param>
    public static IReadOnlyDictionary<string, string> FixedLevelsFor(NameKind kind) =>
        new Dictionary<string, string>(StringComparer.Ordinal) { [Kind] = kind.ToString() };

    /// <summary>名前の組の形（前後の空白を持つか）</summary>
    /// <param name="pair">名前の組</param>
    public static string ShapeOf(NamePair pair) =>
        pair.First.Trim() == pair.First && pair.Second.Trim() == pair.Second
            ? Plain
            : WithWhitespace;

    /// <summary>入口へ流す組 1 つを数える（組の記録を読むだけ）</summary>
    /// <param name="counter">数える先</param>
    /// <param name="kind">入口の名前の種類</param>
    /// <param name="pair">流す組</param>
    public static void Observe(CoverageCounter counter, NameKind kind, NamePair pair) =>
        counter.Observe(
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [Kind] = kind.ToString(),
                [Difference] = pair.Difference.ToString(),
                [Shape] = ShapeOf(pair),
            }
        );

    /// <summary>
    /// 組の違いの 1 つの水準を流さない入口の項目（その水準を含む 2 つの因子の組＝種類×違い・違い×形ごとに 1 項目）
    /// </summary>
    private static IEnumerable<CoverageGapEntry> FoldGaps(
        string entrance,
        NameDifference difference,
        string reason
    )
    {
        var level = difference.ToString();

        yield return CoverageGap.Matching(
            entrance,
            "P1-3",
            "P2",
            reason,
            Kind,
            Difference,
            (_, d) => d == level
        );
        yield return CoverageGap.Matching(
            entrance,
            "P1-3",
            "P2",
            reason,
            Difference,
            Shape,
            (d, _) => d == level
        );
    }

    /// <summary>原理的に起こり得ない組み合わせのときだけ偽（判断に要る因子が割り当てに無ければ真）</summary>
    private static bool IsValid(IReadOnlyDictionary<string, string> assignment)
    {
        if (
            !assignment.TryGetValue(Difference, out var difference)
            || !assignment.TryGetValue(Shape, out var shape)
        )
        {
            return true;
        }

        // NameIdentityRule.Classify は前後の空白の有無を Shape と同じ定義（Trim で変わるか）で読む:
        // 空白だけ・空白と大文字小文字の違いは、どちらかが前後に空白を持つ組にしか付かず、
        // 大文字小文字だけの違いは、どちらも前後に空白を持たない組にしか付かない
        return difference switch
        {
            nameof(NameDifference.Whitespace) or nameof(NameDifference.WhitespaceAndCase) => shape
                == WithWhitespace,
            nameof(NameDifference.Case) => shape == Plain,
            _ => true,
        };
    }
}
