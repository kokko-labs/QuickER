using QuickER.Tests.TestSupport.Engines;

namespace QuickER.Tests.Platform.OsBoundary;

/// <summary>パスの入口の表の次元表・網羅しない組の一覧・台帳の行</summary>
/// <remarks>
/// 入口 × 入力の形を数える。入口が受け取らない種類の入力（URI を受ける入口に相対パスの形など）は原理的に起こり得ないので
/// <c>isValid</c> で外す（<see cref="KindsOf(PathEntrance)"/>・<see cref="KindsOf(PathShape)"/>）。
/// </remarks>
internal static class PathEntranceDimensions
{
    /// <summary>網羅しない組の照合の範囲</summary>
    public const string Scope = "os-boundary-path-entrance";

    /// <summary>入口</summary>
    public const string Entrance = "Entrance";

    /// <summary>入力の形</summary>
    public const string Shape = "Shape";

    /// <summary>次元表</summary>
    public static DimensionTable Table { get; } =
        new(
            EngineIds.OsBoundary,
            [
                new Factor(
                    Entrance,
                    Enum.GetValues<PathEntrance>().Select(e => new Level(e.ToString())).ToList(),
                    "パスの入口"
                ),
                new Factor(
                    Shape,
                    Enum.GetValues<PathShape>().Select(s => new Level(s.ToString())).ToList(),
                    "入力の形"
                ),
            ],
            isValid: IsValid
        );

    /// <summary>網羅しない組の一覧</summary>
    /// <remarks>
    /// どれも元の単発のテストが持たず、P3-10 では行を足していない組。足すなら表の行で足し、ここから消す。
    /// </remarks>
    public static CoverageGapSet Gaps =>
        CoverageGap.Set(
            EngineIds.OsBoundary,
            [
                Group(
                    "Blazor のプロファイルの行は許可拡張子の違い（受理する UI 層ソース・別ターゲットの拡張子・拒否メッセージの列挙）だけを見る。"
                        + "プロファイルが変えるのは許可拡張子だけで、ほかの検査（絶対パス・保護フォルダ・別名・空）は両プロファイル共通の経路を通るため、"
                        + "元のテストは WPF の行だけで見ていて、Blazor で同じ入力を流す行は足していない",
                    (
                        PathEntrance.EmitPathBlazor,
                        [
                            PathShape.Absolute,
                            PathShape.BackslashRelative,
                            PathShape.BuildFolder,
                            PathShape.DotsOnly,
                            PathShape.DriveRelative,
                            PathShape.DroppedSegmentsProtected,
                            PathShape.EmptyOrWhitespace,
                            PathShape.Extensionless,
                            PathShape.ParentTraversal,
                            PathShape.ProtectedAlias,
                            PathShape.ScaffoldFile,
                        ]
                    )
                ),
                Group(
                    "元のテストが持たない：リソースの許可判定の行に #fragment 付きの入力が無い（遷移の行だけが持つ）",
                    (PathEntrance.PreviewResource, [PathShape.Fragment])
                ),
                Group(
                    "元のテストが持たない：遷移の許可判定の行に ws / wss のスキームと null の対象が無い"
                        + "（リソースの行だけが持つ。遷移の http / https の行が同じ scheme 判定を見ている）",
                    (
                        PathEntrance.PreviewNavigation,
                        [PathShape.WebSocketScheme, PathShape.NullTarget]
                    )
                ),
            ]
        );

    /// <summary>入口ごとに形を名指しした組をまとめて 1 つの項目にする（理由が同じ組）</summary>
    private static CoverageGapEntry Group(
        string reason,
        params (PathEntrance Entrance, PathShape[] Shapes)[] pairs
    )
    {
        var set = pairs
            .SelectMany(p => p.Shapes.Select(s => (p.Entrance.ToString(), s.ToString())))
            .ToHashSet();
        return CoverageGap.Matching(
            Scope,
            "P3-10",
            "P4",
            reason,
            Entrance,
            Shape,
            (entrance, shape) => set.Contains((entrance, shape))
        );
    }

    /// <summary>台帳の行</summary>
    public static EngineDescriptor Descriptor =>
        new(
            EngineIds.OsBoundary,
            "PathEntrances",
            "パスの入口の表（入口 × 入力の形 → 結果。モックのプレビュー・提出パス・画面のファイル名）",
            Table,
            PathEntranceRows.Table.Count,
            [
                "tests/QuickER.Tests/Platform/OsBoundary/PathEntranceDimensions.cs",
                "tests/QuickER.Tests/Platform/OsBoundary/PathEntranceRows.cs",
                "tests/QuickER.Tests/Platform/OsBoundary/PathEntranceRows.Preview.cs",
                "tests/QuickER.Tests/Platform/OsBoundary/PathEntranceRows.Emit.cs",
                "tests/QuickER.Tests/Platform/OsBoundary/PathEntranceRows.Screen.cs",
                "tests/QuickER.Tests/Platform/OsBoundary/PathEntranceTests.cs",
            ],
            [
                "src/QuickER.AI.Mock/MockPreviewPanel.xaml.cs",
                "src/QuickER.AI.Mock/MockProjectEmitTools.cs",
                "src/QuickER.AI.Mock/ApiKeyMockProjectAgent.cs",
                "src/QuickER.Platform/PathContainment.cs",
                "src/QuickER.Platform/PathSegments.cs",
            ]
        );

    /// <summary>1 行を流した後、入口と形を数える</summary>
    public static void Observe(CoverageCounter counter, PathEntranceCase row) =>
        counter.Observe(
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [Entrance] = row.Entrance.ToString(),
                [Shape] = row.Shape.ToString(),
            }
        );

    /// <summary>入口が受け取る入力の種類</summary>
    public static IReadOnlyCollection<PathInputKind> KindsOf(PathEntrance entrance) =>
        entrance switch
        {
            PathEntrance.PreviewNavigation or PathEntrance.PreviewResource =>
            [
                PathInputKind.UriTarget,
            ],
            PathEntrance.EmitPathWpf or PathEntrance.EmitPathBlazor => [PathInputKind.EmitRelative],
            PathEntrance.FindBlockedFolder => [PathInputKind.NormalizedFull],
            PathEntrance.ReadScreenHtml => [PathInputKind.ScreenFile],
            _ => throw new ArgumentOutOfRangeException(nameof(entrance), entrance, null),
        };

    /// <summary>入力の形が属する入力の種類</summary>
    public static IReadOnlyCollection<PathInputKind> KindsOf(PathShape shape) =>
        shape switch
        {
            PathShape.DirectChild
            or PathShape.Fragment
            or PathShape.SubFolder
            or PathShape.OutsideRoot
            or PathShape.HttpScheme
            or PathShape.WebSocketScheme
            or PathShape.EmptyRoot
            or PathShape.NullRoot
            or PathShape.NullTarget => [PathInputKind.UriTarget],
            PathShape.ParentTraversal =>
            [
                PathInputKind.UriTarget,
                PathInputKind.EmitRelative,
                PathInputKind.ScreenFile,
            ],
            PathShape.NonHtml => [PathInputKind.UriTarget, PathInputKind.ScreenFile],
            PathShape.Absolute or PathShape.EmptyOrWhitespace =>
            [
                PathInputKind.EmitRelative,
                PathInputKind.ScreenFile,
            ],
            PathShape.AllowedRelative
            or PathShape.BackslashRelative
            or PathShape.DriveRelative
            or PathShape.ScaffoldFile
            or PathShape.BuildControlFile
            or PathShape.BuildFolder
            or PathShape.ProtectedAlias
            or PathShape.LegitimatePath
            or PathShape.DroppedSegmentsProtected
            or PathShape.DotsOnly
            or PathShape.Extensionless
            or PathShape.OtherTargetExtension => [PathInputKind.EmitRelative],
            PathShape.NormalizedProtectedSegment or PathShape.NormalizedUnprotected =>
            [
                PathInputKind.NormalizedFull,
            ],
            PathShape.ValidScreenFile => [PathInputKind.ScreenFile],
            _ => throw new ArgumentOutOfRangeException(nameof(shape), shape, null),
        };

    /// <summary>原理的に起こり得ない組：入口が受け取らない種類の入力の形</summary>
    private static bool IsValid(IReadOnlyDictionary<string, string> assignment)
    {
        if (
            !assignment.TryGetValue(Entrance, out var entrance)
            || !assignment.TryGetValue(Shape, out var shape)
        )
        {
            return true;
        }

        var accepted = KindsOf(Enum.Parse<PathEntrance>(entrance));
        return KindsOf(Enum.Parse<PathShape>(shape)).Any(accepted.Contains);
    }
}
