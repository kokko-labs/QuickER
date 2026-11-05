using QuickER.Tests.TestSupport.Engines;

namespace QuickER.Tests.Gui.Services;

/// <summary>自動整列の表の次元表・網羅しない組の一覧・台帳の行</summary>
/// <remarks>
/// ケースは全組の Theory（<see cref="AutoLayoutScenarioTests.Combo_KeepsCommonProperties"/>）の 1 組と、行の 1 つ（形の表の形の行だけ）。
/// 宣言を数えるだけで流さない。種を持たないので、網羅しない組は毎回照合する。
/// </remarks>
internal static class AutoLayoutDimensions
{
    /// <summary>網羅しない組の照合の範囲</summary>
    public const string Scope = "auto-layout";

    /// <summary>図の形</summary>
    public const string Shape = "Shape";

    /// <summary>整列の種類</summary>
    public const string Kind = "Kind";

    /// <summary>追記の置き場所（追記でない整列は <see cref="NotAppend"/>）</summary>
    public const string Placement = "Placement";

    /// <summary>追記でない整列の置き場所の水準</summary>
    public const string NotAppend = "NotAppend";

    /// <summary>次元表</summary>
    public static DimensionTable Table { get; } =
        new(
            EngineIds.DiagramEdit,
            [
                new Factor(
                    Shape,
                    LayoutShapes.All.Keys.Select(shape => new Level(shape)).ToList(),
                    "図の形（LayoutShapes）"
                ),
                new Factor(
                    Kind,
                    Enum.GetValues<LayoutKind>()
                        .Select(kind => new Level(kind.ToString()))
                        .ToList(),
                    "整列の種類（AutoLayoutService の公開の静的メソッド）"
                ),
                new Factor(
                    Placement,
                    [
                        new Level(NotAppend),
                        .. Enum.GetValues<AppendPlacement>()
                            .Select(placement => new Level(placement.ToString())),
                    ],
                    "追記が新しい表を置いた場所（製品の分岐：固定の群の下か右か）"
                ),
            ],
            null,
            IsValid
        );

    /// <summary>網羅しない組の一覧</summary>
    public static CoverageGapSet Gaps { get; } = CoverageGap.Set(EngineIds.DiagramEdit, []);

    /// <summary>台帳の行</summary>
    public static EngineDescriptor Descriptor { get; } =
        new(
            EngineIds.DiagramEdit,
            "Layout",
            "自動整列の全組の共通の性質と行の指標（図の形 × 整列の種類）",
            Table,
            AutoLayoutScenarioRows.Table.Count
                + LayoutShapes.All.Count * Enum.GetValues<LayoutKind>().Length,
            [
                "tests/QuickER.Tests/Gui/Services/AutoLayoutScenario.cs",
                "tests/QuickER.Tests/Gui/Services/AutoLayoutScenarioRows.cs",
                "tests/QuickER.Tests/Gui/Services/AutoLayoutScenarioTests.cs",
                "tests/QuickER.Tests/Gui/Services/AutoLayoutDimensions.cs",
            ],
            [
                "src/QuickER.Gui/Services/AutoLayoutService.cs",
                "src/QuickER.Gui/Services/LayoutGeometry.cs",
            ]
        );

    /// <summary>全組の Theory と、形の表の形を使う行の宣言を数える（流さない）</summary>
    public static void ObserveDeclarations(CoverageCounter counter)
    {
        foreach (var shape in LayoutShapes.All.Keys)
        {
            foreach (var kind in Enum.GetValues<LayoutKind>())
            {
                Observe(counter, shape, kind);
            }
        }

        foreach (
            var row in AutoLayoutScenarioRows.Table.Where(row =>
                LayoutShapes.All.ContainsKey(row.Input.Shape)
            )
        )
        {
            Observe(counter, row.Input.Shape, row.Input.Kind);
        }
    }

    private static void Observe(CoverageCounter counter, string shape, LayoutKind kind) =>
        counter.Observe(
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [Shape] = shape,
                [Kind] = kind.ToString(),
                [Placement] =
                    kind == LayoutKind.Append
                        ? LayoutShapes.AppendPlacementOf(shape).ToString()
                        : NotAppend,
            }
        );

    /// <summary>原理的に起こり得ない組のときだけ偽（判断に要る因子が割り当てに無ければ真）</summary>
    private static bool IsValid(IReadOnlyDictionary<string, string> assignment)
    {
        assignment.TryGetValue(Shape, out var shape);
        assignment.TryGetValue(Kind, out var kind);
        assignment.TryGetValue(Placement, out var placement);

        // 置き場所は追記の分岐。追記でない整列に置き場所は無く、追記には必ず置き場所（何もしないを含む）がある
        if (
            kind is not null
            && placement is not null
            && kind == nameof(LayoutKind.Append) != (placement != NotAppend)
        )
        {
            return false;
        }

        // 追記の置き場所は図の形（固定の群と新しい表の外形）から 1 つに決まる。形の宣言と違う置き場所は起こり得ない
        return shape is null
            || placement is null
            || placement == NotAppend
            || placement == LayoutShapes.AppendPlacementOf(shape).ToString();
    }
}
