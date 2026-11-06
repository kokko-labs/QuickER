using QuickER.Tests.AI.Mock.Agents;
using QuickER.Tests.TestSupport.Engines;

namespace QuickER.Tests.AI.Mock.Runner;

/// <summary>ランナーの流れの表の次元表・網羅しない組の一覧・台帳の行</summary>
/// <remarks>1 行＝（ターゲット, 結果の分類）を数える。分類は行が宣言し、テストが実際のランの結果を分類して照合する。</remarks>
internal static class MockRunnerDimensions
{
    /// <summary>網羅しない組の照合の範囲</summary>
    public const string Scope = "mock-runner";

    /// <summary>ターゲット</summary>
    public const string Target = "Target";

    /// <summary>結果の分類</summary>
    public const string Result = "Result";

    /// <summary>次元表</summary>
    public static DimensionTable Table =>
        new(
            EngineIds.Ai,
            [
                new Factor(
                    Target,
                    MockAgentTarget.All.Select(target => new Level(target.Name)).ToList(),
                    "生成のターゲット（ランナーへ渡すプロファイル）"
                ),
                new Factor(
                    Result,
                    Enum.GetValues<MockRunResult>()
                        .Select(result => new Level(result.ToString()))
                        .ToList(),
                    "ランの結果の分類"
                ),
            ]
        );

    /// <summary>網羅しない組の一覧（手で書いた静的な一覧）</summary>
    public static CoverageGapSet Gaps =>
        CoverageGap.Set(
            EngineIds.Ai,
            [
                CoverageGap.AcceptedTuple(
                    Scope,
                    "P3-11b",
                    "WPF のプロファイルは画面の数が足りなくても失敗にしない宣言（src/QuickER.AI.Mock/WpfMockProjectTargetProfile.cs の ScreenShortfallFailsRun が false）",
                    (Target, MockAgentTarget.Wpf.Name),
                    (Result, nameof(MockRunResult.ScreensMissing))
                ),
                Group(
                    "実行器の未ログインの申告をランナーの結果へ写す期待は、ランナーの元のテストに無い（実行器の網の login-* の行が実行器の側を見る）",
                    (MockAgentTarget.Wpf, [MockRunResult.NotLoggedIn]),
                    (MockAgentTarget.Blazor, [MockRunResult.NotLoggedIn])
                ),
                Group(
                    "最終ビルドの前の確認の取り消しは、最終ビルドの境界の表（P3-11b のフェーズ 2 / 6）が持つ。流れの表に重ねた期待は元に無い",
                    (MockAgentTarget.Wpf, [MockRunResult.BuildDeclined]),
                    (MockAgentTarget.Blazor, [MockRunResult.BuildDeclined])
                ),
                Group(
                    "ランナーの元のテストは、Blazor では成果物・画面の数・実行ログの形だけを持つ。失敗・タイムアウト・中断・例外・委譲を Blazor に重ねた期待は元に無い",
                    (
                        MockAgentTarget.Blazor,
                        [
                            MockRunResult.ArtifactsMissing,
                            MockRunResult.BuildFailed,
                            MockRunResult.TimedOut,
                            MockRunResult.BuildTimedOut,
                            MockRunResult.Canceled,
                            MockRunResult.Thrown,
                            MockRunResult.NotRun,
                        ]
                    )
                ),
            ]
        );

    /// <summary>ターゲットごとに分類を名指しした組をまとめて 1 つの項目にする（理由が同じ組）</summary>
    private static CoverageGapEntry Group(
        string reason,
        params (MockAgentTarget Target, MockRunResult[] Results)[] pairs
    )
    {
        var set = pairs
            .SelectMany(pair =>
                pair.Results.Select(result => (pair.Target.Name, result.ToString()))
            )
            .ToHashSet();
        return CoverageGap.Matching(
            Scope,
            "P3-11b",
            "P4",
            reason,
            Target,
            Result,
            (target, result) => set.Contains((target, result))
        );
    }

    /// <summary>1 行を数える</summary>
    public static void Observe(CoverageCounter counter, MockRunnerScenario scenario) =>
        counter.Observe(
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [Target] = scenario.Target.Name,
                [Result] = scenario.Result.ToString(),
            }
        );

    /// <summary>台帳の行</summary>
    public static EngineDescriptor Descriptor =>
        new(
            EngineIds.Ai,
            "MockRunner",
            "モックプロジェクト生成のランナーの流れの表（実行器 → 成果物 → 画面の数 → 最終ビルド → 結果の分類）",
            Table,
            MockRunnerRows.Table.Count,
            [
                "tests/QuickER.Tests/AI.Mock/Runner/MockRunnerContext.cs",
                "tests/QuickER.Tests/AI.Mock/Runner/MockRunnerDimensions.cs",
                "tests/QuickER.Tests/AI.Mock/Runner/MockRunnerFakes.cs",
                "tests/QuickER.Tests/AI.Mock/Runner/MockRunnerRows.cs",
                "tests/QuickER.Tests/AI.Mock/Runner/MockRunnerTests.cs",
            ],
            ["src/QuickER.AI.Mock/MockProjectAgentRunner.cs"]
        );
}
