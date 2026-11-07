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
                ProductCandidate(
                    BoundaryEntryKind.Hidden,
                    "製品の候補（P5・ユーザーの判断）：上書きの走査は、隠し属性の別ソリューション・別プロジェクト（直下の *.sln・*.slnx・*.csproj）を一覧に出さない"
                        + "（src/QuickER.AI.Mock/MockProjectOverwriteScanner.cs:45-49・:75-91 の直下の列挙は既定の AttributesToSkip＝Hidden / System を飛ばす。2026-11-07 に実測）。"
                        + "自分のもの（sln・csproj・README・Generated/・design/mock/）は File.Exists / Directory.Exists なので隠しでも出る非対称で、"
                        + "XmlDoc は『直下の別ソリューション・別プロジェクト』を確認の対象に挙げる。特性として固定すると、隠しの同居物を確認から外す方針を黙って決めるので行にしない"
                ),
                ProductCandidate(
                    BoundaryEntryKind.UnlistableReparse,
                    "製品の候補（P5・ユーザーの判断）：出力フォルダ自身が中を列挙できない reparse フォルダのとき、上書きの走査が IOException を投げる"
                        + "（src/QuickER.AI.Mock/MockProjectOverwriteScanner.cs:75-91 の IgnoreInaccessible はアクセス拒否にしか効かない。2026-11-07 に実測）。"
                        + "走査は生成コマンドの try の外（src/QuickER.AI.Mock/MockGenerationDialogViewModel.cs:1284）から呼ばれ、"
                        + "XmlDoc（MockProjectOverwriteScanner.cs:38-44）は『読めないものは確認そのものを妨げる理由にはならない』と書く。"
                        + "Generated/ が列挙できない形だけの行で組を埋めると不具合が目録から消え、特性として固定すると例外で止まる方針を黙って決めるので行にしない"
                ),
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

    /// <summary>上書きの走査で、製品の候補として行にせず残す（入口 D, 種別）の項目（直す区切りは P5）</summary>
    private static CoverageGapEntry ProductCandidate(BoundaryEntryKind kind, string reason) =>
        CoverageGap.Tuple(
            Scope,
            "P3-11b",
            "P5",
            reason,
            (Entrance, BoundaryEntrance.OverwriteScan.ToString()),
            (Kind, kind.ToString())
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
