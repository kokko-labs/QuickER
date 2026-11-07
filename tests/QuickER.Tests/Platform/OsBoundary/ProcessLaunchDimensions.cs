using QuickER.Tests.TestSupport.Engines;

namespace QuickER.Tests.Platform.OsBoundary;

/// <summary>起動の表の次元表・網羅しない組の一覧・台帳の行</summary>
/// <remarks>
/// 入口 × 入力の形を数える。入口が受け取らない種類の入力（URL を受ける入口に PATH の形など）は原理的に起こり得ないので
/// <c>isValid</c> で外す（<see cref="KindsOf(LaunchEntrance)"/>・<see cref="KindsOf(LaunchShape)"/>）。
/// </remarks>
internal static class ProcessLaunchDimensions
{
    /// <summary>網羅しない組の照合の範囲</summary>
    public const string Scope = "os-boundary-process-launch";

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
                    Enum.GetValues<LaunchEntrance>().Select(e => new Level(e.ToString())).ToList(),
                    "起動の入口"
                ),
                new Factor(
                    Shape,
                    Enum.GetValues<LaunchShape>().Select(s => new Level(s.ToString())).ToList(),
                    "入力の形"
                ),
            ],
            isValid: IsValid
        );

    /// <summary>網羅しない組の一覧（受け入れ 1 項目だけ）</summary>
    /// <remarks>
    /// 埋められる組はすべて表の行で埋めた（行は <c>ProcessLaunchRows.Filled.cs</c>・<c>ProcessLaunchRows.Gaps.cs</c> ほか）。
    /// 残るのは、行を足しても意味が出ない組の受け入れだけ。
    /// </remarks>
    public static CoverageGapSet Gaps =>
        CoverageGap.Set(
            EngineIds.OsBoundary,
            [
                CoverageGap.AcceptedMatching(
                    Scope,
                    "P3-10",
                    "StartExecutable は引数を受け取らない（src/QuickER.Platform/ExternalLauncher.cs の "
                        + "StartExecutable(string fullPath, ...)。ProcessStartInfo はパスだけから作る）。"
                        + "BatchShimProcessGuard が防ぐのは、引数が cmd の再解釈で引用の外へ抜けることで、"
                        + "引数の無い起動には当たらない（src/QuickER.Platform/CLAUDE.md「.cmd / .bat シムの起動」）。"
                        + "しかも呼び出しは自分自身の exe の再起動だけ（src/QuickER.Gui/Services/WpfApplicationRestartService.cs:44）で、"
                        + "シムの実パスを渡す呼び出し元が無い。シムの拡張子を断る規則を持たない入口に行を足すと、"
                        + "方針（再起動の入口はシムを断るべきか）を黙って決めてしまうので、行は足さず受け入れる",
                    Entrance,
                    Shape,
                    (entrance, shape) =>
                        entrance == nameof(LaunchEntrance.ExternalStartExecutable)
                        && shape == nameof(LaunchShape.UnwrappedShim)
                ),
            ]
        );

    /// <summary>台帳の行</summary>
    public static EngineDescriptor Descriptor =>
        new(
            EngineIds.OsBoundary,
            "ProcessLaunch",
            "起動の表（入口 × 入力の形 → 結果。シムのガード・実行ファイルの解決・外部の起動）",
            Table,
            ProcessLaunchRows.Table.Count,
            [
                "tests/QuickER.Tests/Platform/OsBoundary/ProcessLaunchDimensions.cs",
                "tests/QuickER.Tests/Platform/OsBoundary/ProcessLaunchRows.cs",
                "tests/QuickER.Tests/Platform/OsBoundary/ProcessLaunchRows.Shim.cs",
                "tests/QuickER.Tests/Platform/OsBoundary/ProcessLaunchRows.Resolve.cs",
                "tests/QuickER.Tests/Platform/OsBoundary/ProcessLaunchRows.External.cs",
                "tests/QuickER.Tests/Platform/OsBoundary/ProcessLaunchRows.Filled.cs",
                "tests/QuickER.Tests/Platform/OsBoundary/ProcessLaunchRows.Gaps.cs",
                "tests/QuickER.Tests/Platform/OsBoundary/ProcessLaunchTests.cs",
            ],
            [
                "src/QuickER.Platform/BatchShimProcessGuard.cs",
                "src/QuickER.Platform/PathExecutableResolver.cs",
                "src/QuickER.Platform/ExternalLauncher.cs",
                "src/QuickER.Platform/UrlPolicy.cs",
                "src/QuickER.AI/CopilotCliLocator.cs",
                "src/QuickER.AI/CodexCliLocator.cs",
                "src/QuickER.AI/ClaudeCodeClient.cs",
                "src/QuickER.AI/CodexAppServerClient.cs",
                "src/QuickER.AI.UI/ShellChatLinkOpener.cs",
            ]
        );

    /// <summary>1 行を流した後、入口と形を数える</summary>
    public static void Observe(CoverageCounter counter, LaunchCase row) =>
        counter.Observe(
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [Entrance] = row.Entrance.ToString(),
                [Shape] = row.Shape.ToString(),
            }
        );

    /// <summary>入口が受け取る入力の種類</summary>
    public static IReadOnlyCollection<LaunchInputKind> KindsOf(LaunchEntrance entrance) =>
        entrance switch
        {
            LaunchEntrance.ShimIsBatchShim
            or LaunchEntrance.CodexResolveLaunch
            or LaunchEntrance.CopilotEnsureSafeToLaunch => [LaunchInputKind.ShimPath],
            LaunchEntrance.ShimWrapCommandLine
            or LaunchEntrance.ShimApply
            or LaunchEntrance.ClaudeApplyBatchShimGuard
            or LaunchEntrance.CodexResolveStartInfo =>
            [
                LaunchInputKind.ShimPath,
                LaunchInputKind.ShimArgument,
            ],
            LaunchEntrance.PathExecutableResolve
            or LaunchEntrance.CopilotLocate
            or LaunchEntrance.CodexLocate => [LaunchInputKind.PathSearch],
            LaunchEntrance.ExternalOpenUrl or LaunchEntrance.ShellChatLinkOpen =>
            [
                LaunchInputKind.Url,
            ],
            LaunchEntrance.ExternalOpenFolder => [LaunchInputKind.Folder],
            LaunchEntrance.ExternalStartExecutable or LaunchEntrance.ExternalCreateProcess =>
            [
                LaunchInputKind.Executable,
            ],
            _ => throw new ArgumentOutOfRangeException(nameof(entrance), entrance, null),
        };

    /// <summary>入力の形が属する入力の種類</summary>
    public static IReadOnlyCollection<LaunchInputKind> KindsOf(LaunchShape shape) =>
        shape switch
        {
            LaunchShape.ExeTarget
            or LaunchShape.ShimTarget
            or LaunchShape.ShimSpelling
            or LaunchShape.NonWindowsOrNullPath
            or LaunchShape.PathQuote
            or LaunchShape.PathPercent
            or LaunchShape.PathExclamation
            or LaunchShape.PathNewline
            or LaunchShape.ExePathWithShimMeta => [LaunchInputKind.ShimPath],
            LaunchShape.ArgCmdMeta
            or LaunchShape.ArgPercent
            or LaunchShape.ArgQuote
            or LaunchShape.ArgNewline
            or LaunchShape.ArgExclamation
            or LaunchShape.ArgBackslash
            or LaunchShape.ArgEmpty => [LaunchInputKind.ShimArgument],
            LaunchShape.OnPath
            or LaunchShape.CmdCandidate
            or LaunchShape.ExeAndCmd
            or LaunchShape.NotOnPath
            or LaunchShape.EmptyPath
            or LaunchShape.EmptyEntries
            or LaunchShape.RelativeEntries
            or LaunchShape.QuotedEntry
            or LaunchShape.ExtensionlessFirst
            or LaunchShape.PathExtOrder
            or LaunchShape.ExtensionOutsidePathExt
            or LaunchShape.PerCommandName
            or LaunchShape.UnvalidatedEntry => [LaunchInputKind.PathSearch],
            LaunchShape.SafeUrl
            or LaunchShape.UnsafeScheme
            or LaunchShape.UncUrl
            or LaunchShape.UserInfoUrl
            or LaunchShape.LaunchFails => [LaunchInputKind.Url],
            LaunchShape.MissingFolder or LaunchShape.ExistingFolder => [LaunchInputKind.Folder],
            LaunchShape.RelativePath => [LaunchInputKind.Folder, LaunchInputKind.Executable],
            LaunchShape.BareName
            or LaunchShape.AllowedBareName
            or LaunchShape.UnwrappedShim
            or LaunchShape.WrappedShim
            or LaunchShape.ShellExecute => [LaunchInputKind.Executable],
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

        // StartExecutable は引数を持てない（ExternalLauncher.StartExecutable(string fullPath, ...) はパス 1 つだけを受ける）ので、
        // Apply で包み直したシム（FileName が cmd.exe で引数つきの形）は原理的に渡らない。
        // 入口と形の種類の照合（下）とは別の規則：種類は同じ Executable でも、この入口にはその形を作る相手がいない
        if (
            entrance == nameof(LaunchEntrance.ExternalStartExecutable)
            && shape == nameof(LaunchShape.WrappedShim)
        )
        {
            return false;
        }

        var accepted = KindsOf(Enum.Parse<LaunchEntrance>(entrance));
        return KindsOf(Enum.Parse<LaunchShape>(shape)).Any(accepted.Contains);
    }
}
