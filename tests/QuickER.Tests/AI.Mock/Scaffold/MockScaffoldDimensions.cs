using QuickER.Tests.AI.Mock.Agents;
using QuickER.Tests.TestSupport.Engines;

namespace QuickER.Tests.AI.Mock.Scaffold;

/// <summary>スキャフォールドの表の次元表・網羅しない組の一覧・台帳の行</summary>
/// <remarks>1 行＝（ターゲット, 方言, スキャフォールドの前の出力フォルダの状態）を数える。</remarks>
internal static class MockScaffoldDimensions
{
    /// <summary>網羅しない組の照合の範囲</summary>
    public const string Scope = "mock-scaffold";

    /// <summary>ターゲット</summary>
    public const string Target = "Target";

    /// <summary>方言</summary>
    public const string Dialect = "Dialect";

    /// <summary>スキャフォールドの前の出力フォルダの状態</summary>
    public const string Existing = "Existing";

    /// <summary>方言の水準（QuickER 版 Repository を出す 2 つと、出さない代表の PostgreSQL）</summary>
    private static readonly string[] Dialects = ["sqlserver", "sqlite", "postgresql"];

    /// <summary>次元表</summary>
    public static DimensionTable Table =>
        new(
            EngineIds.Ai,
            [
                new Factor(
                    Target,
                    MockAgentTarget.All.Select(target => new Level(target.Name)).ToList(),
                    "生成のターゲット（スキャフォールドへ渡すプロファイル）"
                ),
                new Factor(
                    Dialect,
                    Dialects.Select(dialect => new Level(dialect)).ToList(),
                    "図の方言（QuickER 版 Repository を出す方言と、出さない方言の代表）"
                ),
                new Factor(
                    Existing,
                    Enum.GetValues<ScaffoldExisting>()
                        .Select(existing => new Level(existing.ToString()))
                        .ToList(),
                    "スキャフォールドの前の出力フォルダの状態"
                ),
            ]
        );

    /// <summary>網羅しない組の一覧（手で書いた静的な一覧。作り直しの 3 状態は代表の本体をターゲット × 方言の全部で流すので空）</summary>
    public static CoverageGapSet Gaps => CoverageGap.Set(EngineIds.Ai, []);

    /// <summary>1 行を数える</summary>
    public static void Observe(CoverageCounter counter, MockScaffoldScenario scenario) =>
        counter.Observe(
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [Target] = scenario.Target.Name,
                [Dialect] = scenario.Dialect,
                [Existing] = scenario.Existing.ToString(),
            }
        );

    /// <summary>台帳の行</summary>
    public static EngineDescriptor Descriptor =>
        new(
            EngineIds.Ai,
            "MockScaffold",
            "モックプロジェクトのスキャフォールドの表（ターゲット × 方言 × 出力フォルダの状態。構成物・名前空間の案内・プロジェクト名・作り直し）",
            Table,
            MockScaffoldRows.Table.Count,
            [
                "tests/QuickER.Tests/AI.Mock/Scaffold/MockScaffoldContext.cs",
                "tests/QuickER.Tests/AI.Mock/Scaffold/MockScaffoldDimensions.cs",
                "tests/QuickER.Tests/AI.Mock/Scaffold/MockScaffoldRows.cs",
                "tests/QuickER.Tests/AI.Mock/Scaffold/MockScaffoldRows.Regeneration.cs",
                "tests/QuickER.Tests/AI.Mock/Scaffold/MockScaffoldTests.cs",
            ],
            [
                "src/QuickER.AI.Mock/MockProjectScaffoldService.cs",
                "src/QuickER.AI.Mock/MockProjectFiles.cs",
                "src/QuickER.AI.Mock/ApiKeyMockProjectAgent.cs",
            ]
        );
}
