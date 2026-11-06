using QuickER.Tests.TestSupport.Engines;

namespace QuickER.Tests.Gui.Services.Lifecycle;

/// <summary>終了の最後の処理の総当たりの次元表・網羅しない組の一覧・台帳の行</summary>
/// <remarks>全組を列挙するので原理的に起こり得ない組は無く、網羅しない組も無い</remarks>
internal static class PostExitExhaustiveDimensions
{
    /// <summary>網羅しない組の照合の範囲</summary>
    public const string Scope = "os-boundary-post-exit-exhaustive";

    /// <summary>次元表</summary>
    public static DimensionTable Table { get; } =
        new(
            EngineIds.OsBoundary,
            [
                FactorOf<PostExitUpdate>("Update", "更新の適用の登録"),
                FactorOf<PostExitRequest>("Request", "再起動の要求"),
                FactorOf<PostExitStarter>("Starter", "起動処理の登録"),
                FactorOf<PostExitRelease>("Release", "座の手放しの登録"),
            ]
        );

    /// <summary>網羅しない組の一覧（無い）</summary>
    public static CoverageGapSet Gaps => CoverageGap.Set(EngineIds.OsBoundary, []);

    private static Factor FactorOf<T>(string name, string description)
        where T : struct, Enum =>
        new(name, Enum.GetValues<T>().Select(v => new Level(v.ToString())).ToList(), description);

    /// <summary>台帳の行</summary>
    public static EngineDescriptor Descriptor =>
        new(
            EngineIds.OsBoundary,
            "PostExitExhaustive",
            "終了の最後の処理の総当たり（更新 × 再起動の要求 × 起動処理 × 座の手放し・全 60 組）",
            Table,
            PostExitExhaustiveRows.Table.Count,
            [
                "tests/QuickER.Tests/Gui/Services/Lifecycle/PostExitExhaustiveDimensions.cs",
                "tests/QuickER.Tests/Gui/Services/Lifecycle/PostExitExhaustiveRows.cs",
                "tests/QuickER.Tests/Gui/Services/Lifecycle/PostExitExhaustiveTests.cs",
            ],
            ["src/QuickER.Gui/Services/PostExitActions.cs"]
        );

    /// <summary>1 組を数える</summary>
    public static void Observe(CoverageCounter counter, PostExitExhaustiveCase c) =>
        counter.Observe(
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["Update"] = c.Update.ToString(),
                ["Request"] = c.Request.ToString(),
                ["Starter"] = c.Starter.ToString(),
                ["Release"] = c.Release.ToString(),
            }
        );
}
