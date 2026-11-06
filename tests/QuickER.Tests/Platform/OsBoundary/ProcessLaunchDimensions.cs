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

    /// <summary>網羅しない組の一覧</summary>
    /// <remarks>
    /// どれも元の単発のテストが持たず、P3-10 では行を足していない組（期待を製品の XmlDoc から導ける組は
    /// <c>ProcessLaunchRows.Filled.cs</c> で足した）。足すなら表の行で足し、ここから消す。
    /// </remarks>
    public static CoverageGapSet Gaps =>
        CoverageGap.Set(
            EngineIds.OsBoundary,
            [
                Group(
                    "配線の入口（Claude・Codex・Copilot）は BatchShimProcessGuard の同じ判定へ委ね、配線の行が固定するのは"
                        + "文言のキーと起動の形だけ。同じ入力の形は Guard の入口の行が見ている",
                    (LaunchEntrance.ClaudeApplyBatchShimGuard, ClaudeShapes),
                    (LaunchEntrance.CodexResolveStartInfo, CodexStartInfoShapes),
                    (LaunchEntrance.CodexResolveLaunch, CodexLaunchShapes),
                    (
                        LaunchEntrance.CopilotEnsureSafeToLaunch,
                        [LaunchShape.NonWindowsOrNullPath, LaunchShape.ShimSpelling]
                    )
                ),
                Group(
                    "3 つの入口は同じ PATH の走査（PathExecutableResolver）を通る。PATHEXT と拡張子の形は共有の入口の行、"
                        + "PATH の要素の形はロケータの行が見ている",
                    (LaunchEntrance.PathExecutableResolve, SharedResolverShapes),
                    (LaunchEntrance.CodexLocate, CodexLocateShapes),
                    (
                        LaunchEntrance.CopilotLocate,
                        [
                            LaunchShape.ExtensionOutsidePathExt,
                            LaunchShape.ExtensionlessFirst,
                            LaunchShape.PathExtOrder,
                            LaunchShape.PerCommandName,
                            LaunchShape.QuotedEntry,
                        ]
                    )
                ),
                Group(
                    "IsBatchShim は拡張子だけを見る（中身の文字は判定に入らない）。中身の文字の形は Wrap・Apply の行が見ている",
                    (
                        LaunchEntrance.ShimIsBatchShim,
                        [
                            LaunchShape.ExePathWithShimMeta,
                            LaunchShape.PathExclamation,
                            LaunchShape.PathNewline,
                            LaunchShape.PathPercent,
                            LaunchShape.PathQuote,
                        ]
                    )
                ),
                Group(
                    "WrapCommandLine は引数を連結済みの 1 本の文字列のまま包む（トークンごとの引用が無い）。"
                        + "バックスラッシュ・空の引数を包んだ形は製品の XmlDoc から導けない",
                    (
                        LaunchEntrance.ShimWrapCommandLine,
                        [LaunchShape.ArgBackslash, LaunchShape.ArgEmpty]
                    )
                ),
                Group(
                    "元のテストが持たず、期待を製品の XmlDoc から導けない（StartExecutable はフルパスかどうかだけを"
                        + "確かめる入口で、名前だけ・シム・ShellExecute の扱いを書いていない。OpenUrl の起動の失敗の扱いも書いていない）",
                    (
                        LaunchEntrance.ExternalStartExecutable,
                        [
                            LaunchShape.AllowedBareName,
                            LaunchShape.BareName,
                            LaunchShape.ShellExecute,
                            LaunchShape.UnwrappedShim,
                            LaunchShape.WrappedShim,
                        ]
                    ),
                    (LaunchEntrance.ExternalOpenUrl, [LaunchShape.LaunchFails])
                ),
            ]
        );

    private static readonly LaunchShape[] ClaudeShapes =
    [
        LaunchShape.ArgBackslash,
        LaunchShape.ArgCmdMeta,
        LaunchShape.ArgEmpty,
        LaunchShape.ArgExclamation,
        LaunchShape.ArgQuote,
        LaunchShape.ExePathWithShimMeta,
        LaunchShape.NonWindowsOrNullPath,
        LaunchShape.PathExclamation,
        LaunchShape.PathNewline,
        LaunchShape.ShimSpelling,
    ];

    private static readonly LaunchShape[] CodexStartInfoShapes =
    [
        LaunchShape.ArgBackslash,
        LaunchShape.ArgEmpty,
        LaunchShape.ArgExclamation,
        LaunchShape.ArgNewline,
        LaunchShape.NonWindowsOrNullPath,
        LaunchShape.PathExclamation,
        LaunchShape.PathNewline,
        LaunchShape.ShimSpelling,
    ];

    private static readonly LaunchShape[] CodexLaunchShapes =
    [
        LaunchShape.ExePathWithShimMeta,
        LaunchShape.PathExclamation,
        LaunchShape.PathNewline,
        LaunchShape.PathPercent,
        LaunchShape.PathQuote,
        LaunchShape.ShimSpelling,
    ];

    private static readonly LaunchShape[] SharedResolverShapes =
    [
        LaunchShape.CmdCandidate,
        LaunchShape.EmptyEntries,
        LaunchShape.EmptyPath,
        LaunchShape.ExeAndCmd,
        LaunchShape.NotOnPath,
        LaunchShape.OnPath,
        LaunchShape.RelativeEntries,
        LaunchShape.UnvalidatedEntry,
    ];

    private static readonly LaunchShape[] CodexLocateShapes =
    [
        LaunchShape.ExeAndCmd,
        LaunchShape.ExtensionOutsidePathExt,
        LaunchShape.ExtensionlessFirst,
        LaunchShape.PathExtOrder,
        LaunchShape.PerCommandName,
        LaunchShape.QuotedEntry,
        LaunchShape.UnvalidatedEntry,
    ];

    /// <summary>入口ごとに形を名指しした組をまとめて 1 つの項目にする（理由が同じ組）</summary>
    private static CoverageGapEntry Group(
        string reason,
        params (LaunchEntrance Entrance, LaunchShape[] Shapes)[] pairs
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

        var accepted = KindsOf(Enum.Parse<LaunchEntrance>(entrance));
        return KindsOf(Enum.Parse<LaunchShape>(shape)).Any(accepted.Contains);
    }
}
