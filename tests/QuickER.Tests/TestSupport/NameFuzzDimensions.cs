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

    /// <summary>網羅しない組の一覧（範囲は入口の名前・空）</summary>
    /// <remarks>
    /// 大文字化だけで同じになる組（CaseUpperFoldOnly）は網羅の組（<see cref="NameFuzz.CoveragePairs"/>）が全入口で作る。
    /// 小文字化だけで同じになる組（CaseLowerFoldOnly）は、名前から C# の識別子を作る入口でも、作る識別子が大文字小文字を
    /// 無視して同じなら断るのが答えとして流す（<see cref="NameFuzzRunner.Run"/> の derivedName）。
    /// </remarks>
    public static CoverageGapSet Gaps { get; } = CoverageGap.Set(EngineIds.Name, []);

    /// <summary>台帳の行（入口）</summary>
    public static EngineDescriptor Descriptor { get; } =
        new(
            EngineIds.Name,
            "Entrances",
            "名前の総当たり（GUI・ツール・取込・クエリ定義・C# 生成・DDL と差分の入口）",
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
                "tests/QuickER.Tests/Provider/NameIdentityDdlFuzzTests.cs",
            ],
            [
                "src/QuickER.Model/SchemaNames.cs",
                "src/QuickER.Model/DiagramNameDuplicates.cs",
                "src/QuickER.Provider/SqlNameDuplicates.cs",
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
