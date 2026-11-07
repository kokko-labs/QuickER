using QuickER.Tests.TestSupport.Engines;

namespace QuickER.Tests.AI.Mock.BuildBoundary;

/// <summary>最終ビルドの境界の表の次元表・網羅しない組の一覧・台帳の行</summary>
/// <remarks>1 行＝（入口, 種別）を数える。行が置く種別ごとに、どの入口で流したかを見る。</remarks>
internal static class MockBuildBoundaryDimensions
{
    /// <summary>網羅しない組の照合の範囲</summary>
    public const string Scope = "mock-build-boundary";

    /// <summary>入口</summary>
    public const string Entrance = "Entrance";

    /// <summary>種別</summary>
    public const string Kind = "Kind";

    /// <summary>次元表</summary>
    public static DimensionTable Table =>
        new(
            EngineIds.Ai,
            [
                new Factor(
                    Entrance,
                    Enum.GetValues<BoundaryEntrance>()
                        .Select(entrance => new Level(entrance.ToString()))
                        .ToList(),
                    "入口（A＝変更検知・B＝ビルド出力の削除・C＝成果物の探索・D＝上書きの走査・E＝最終ビルドの引数）"
                ),
                new Factor(
                    Kind,
                    Enum.GetValues<BoundaryEntryKind>()
                        .Select(kind => new Level(kind.ToString()))
                        .ToList(),
                    "行が置くファイル・フォルダの種別"
                ),
            ]
        );

    /// <summary>網羅しない組の一覧（手で書いた静的な一覧）</summary>
    public static CoverageGapSet Gaps =>
        CoverageGap.Set(
            EngineIds.Ai,
            [
                CoverageGap.AcceptedMatching(
                    Scope,
                    "修正 N",
                    "最終ビルドの引数（DotnetBuildRunner.CreateBuildStartInfo）はソリューションのパスから引数を組む純粋な関数で、"
                        + "フォルダの中身を読まない。通常のファイル以外の種別を置いても引数は変わらない",
                    Entrance,
                    Kind,
                    (e, k) =>
                        e == BoundaryEntrance.BuildArguments.ToString()
                        && k != BoundaryEntryKind.Plain.ToString()
                ),
            ]
        );

    /// <summary>1 行を数える</summary>
    public static void Observe(CoverageCounter counter, MockBuildBoundaryScenario scenario) =>
        counter.Observe(
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [Entrance] = scenario.Entrance.ToString(),
                [Kind] = scenario.Kind.ToString(),
            }
        );

    /// <summary>台帳の行</summary>
    public static EngineDescriptor Descriptor =>
        new(
            EngineIds.Ai,
            "MockBuildBoundary",
            "モックプロジェクトの最終ビルドの境界の表（変更検知・ビルド出力の削除・成果物の探索・上書きの走査・最終ビルドの引数 × 種別）",
            Table,
            MockBuildBoundaryRows.Table.Count,
            [
                "tests/QuickER.Tests/AI.Mock/BuildBoundary/MockBuildBoundaryContext.cs",
                "tests/QuickER.Tests/AI.Mock/BuildBoundary/MockBuildBoundaryDimensions.cs",
                "tests/QuickER.Tests/AI.Mock/BuildBoundary/MockBuildBoundaryRows.cs",
                "tests/QuickER.Tests/AI.Mock/BuildBoundary/MockBuildBoundaryRows.Arguments.cs",
                "tests/QuickER.Tests/AI.Mock/BuildBoundary/MockBuildBoundaryRows.Clear.cs",
                "tests/QuickER.Tests/AI.Mock/BuildBoundary/MockBuildBoundaryRows.Files.cs",
                "tests/QuickER.Tests/AI.Mock/BuildBoundary/MockBuildBoundaryRows.Kinds.cs",
                "tests/QuickER.Tests/AI.Mock/BuildBoundary/MockBuildBoundaryTests.cs",
            ],
            [
                "src/QuickER.AI.Mock/MockProjectBuildBoundary.cs",
                "src/QuickER.AI.Mock/MockProjectFiles.cs",
                "src/QuickER.AI.Mock/MockProjectOverwriteScanner.cs",
                "src/QuickER.AI.Mock/MockProjectAgentRunner.cs",
                "src/QuickER.AI.Mock/DotnetBuildRunner.cs",
            ]
        );
}
