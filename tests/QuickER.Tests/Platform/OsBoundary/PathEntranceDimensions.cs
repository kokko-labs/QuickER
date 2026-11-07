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
    /// <remarks>空。足りない組が出たら表の行で足し、ここから消す。</remarks>
    public static CoverageGapSet Gaps => CoverageGap.Set(EngineIds.OsBoundary, []);

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
