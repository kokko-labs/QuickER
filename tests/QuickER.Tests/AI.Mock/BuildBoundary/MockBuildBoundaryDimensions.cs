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
                    "入口（A＝変更検知・B＝ビルド出力の削除・C＝成果物の探索・D＝上書きの走査）"
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
                Group(
                    "元のテストに無い：変更検知へ読み取り専用・削除を妨げるハンドルのファイルを書いた組と、出力フォルダが無い組"
                        + "（EnumerateComparedEntries は空を返す）は流していない",
                    BoundaryEntrance.ChangeDetection,
                    BoundaryEntryKind.ReadOnly,
                    BoundaryEntryKind.Locked,
                    BoundaryEntryKind.Absent
                ),
                Group(
                    "元のテストに無い：obj / bin の削除は、隠し属性・ファイルのシンボリックリンク・列挙できない reparse フォルダ・"
                        + "一覧の拒否を obj へ置いた組を流していない。obj / bin が無い組（ListResidue は空を返す）を名指しで表明する行も無い",
                    BoundaryEntrance.ClearOutputs,
                    BoundaryEntryKind.Hidden,
                    BoundaryEntryKind.FileSymlink,
                    BoundaryEntryKind.UnlistableReparse,
                    BoundaryEntryKind.AclDenied,
                    BoundaryEntryKind.Absent
                ),
                Group(
                    "元のテストに無い：成果物の探索の再帰の列挙は隠し属性を飛ばす（MockProjectFiles の RecursiveOptions は AttributesToSkip を変えない）が、"
                        + "その組は流していない。読み取り専用・削除を妨げるハンドルは読むだけの入口で流していない",
                    BoundaryEntrance.Enumerate,
                    BoundaryEntryKind.Hidden,
                    BoundaryEntryKind.ReadOnly,
                    BoundaryEntryKind.Locked
                ),
                Group(
                    "元のテストに無い：上書きの走査（MockProjectOverwriteScanner）は直下の名前と既定の上書き先の有無だけを見る。"
                        + "種別を変えた組は流していない",
                    BoundaryEntrance.OverwriteScan,
                    BoundaryEntryKind.Hidden,
                    BoundaryEntryKind.ReadOnly,
                    BoundaryEntryKind.Locked,
                    BoundaryEntryKind.FileSymlink,
                    BoundaryEntryKind.Junction,
                    BoundaryEntryKind.NonLinkReparse,
                    BoundaryEntryKind.UnlistableReparse
                ),
            ]
        );

    /// <summary>入口ごとに、流していない種別をまとめて 1 つの項目にする（理由が同じ組）</summary>
    private static CoverageGapEntry Group(
        string reason,
        BoundaryEntrance entrance,
        params BoundaryEntryKind[] kinds
    )
    {
        var set = kinds.Select(kind => kind.ToString()).ToHashSet(StringComparer.Ordinal);
        return CoverageGap.Matching(
            Scope,
            "P3-11b",
            "P4",
            reason,
            Entrance,
            Kind,
            (e, k) => e == entrance.ToString() && set.Contains(k)
        );
    }

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
            "モックプロジェクトの最終ビルドの境界の表（変更検知・ビルド出力の削除・成果物の探索・上書きの走査 × 種別）",
            Table,
            MockBuildBoundaryRows.Table.Count,
            [
                "tests/QuickER.Tests/AI.Mock/BuildBoundary/MockBuildBoundaryContext.cs",
                "tests/QuickER.Tests/AI.Mock/BuildBoundary/MockBuildBoundaryDimensions.cs",
                "tests/QuickER.Tests/AI.Mock/BuildBoundary/MockBuildBoundaryRows.cs",
                "tests/QuickER.Tests/AI.Mock/BuildBoundary/MockBuildBoundaryRows.Clear.cs",
                "tests/QuickER.Tests/AI.Mock/BuildBoundary/MockBuildBoundaryRows.Files.cs",
                "tests/QuickER.Tests/AI.Mock/BuildBoundary/MockBuildBoundaryTests.cs",
            ],
            [
                "src/QuickER.AI.Mock/MockProjectBuildBoundary.cs",
                "src/QuickER.AI.Mock/MockProjectFiles.cs",
                "src/QuickER.AI.Mock/MockProjectOverwriteScanner.cs",
                "src/QuickER.AI.Mock/MockProjectAgentRunner.cs",
            ]
        );
}
