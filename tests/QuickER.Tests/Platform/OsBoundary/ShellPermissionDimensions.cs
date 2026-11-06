using System.IO;
using GitHub.Copilot;
using QuickER.AI;
using QuickER.Platform;
using QuickER.Tests.TestSupport.Engines;

namespace QuickER.Tests.Platform.OsBoundary;

/// <summary>シェル許可の表の次元表・網羅しない組の一覧・台帳の行</summary>
/// <remarks>
/// 因子の水準は行の宣言ではなく、行が組んだ要求から計算する（<see cref="Levels"/>。宣言が嘘をつけないように）。
/// 種類に無い因子（書き込みの Commands など）は原理的に起こり得ないので <c>isValid</c> で外す。
/// </remarks>
internal static class ShellPermissionDimensions
{
    /// <summary>網羅しない組の照合の範囲</summary>
    public const string Scope = "os-boundary-shell-permission";

    /// <summary>要求の種類</summary>
    public const string Kind = "Kind";

    /// <summary>シェル要求のコマンド</summary>
    public const string Commands = "Commands";

    /// <summary>要求が参照するパス</summary>
    public const string Paths = "Paths";

    /// <summary>要求が参照する URL</summary>
    public const string Url = "Url";

    /// <summary>書き込みリダイレクト</summary>
    public const string Redirect = "Redirect";

    /// <summary>サンドボックス迂回の要求</summary>
    public const string Bypass = "Bypass";

    private static readonly string[] KindLevels = ["Shell", "Write", "Read", "Url", "Mcp"];
    private static readonly string[] CommandLevels = ["None", "Listed", "Unlisted", "Composite"];
    private static readonly string[] PathLevels = ["None", "Under", "Outside"];
    private static readonly string[] YesNo = ["No", "Yes"];

    /// <summary>次元表</summary>
    public static DimensionTable Table { get; } =
        new(
            EngineIds.OsBoundary,
            [
                MakeFactor(Kind, KindLevels, "許可要求の種類"),
                MakeFactor(
                    Commands,
                    CommandLevels,
                    "コマンドの識別子（なし・全部が許可リスト・どれも許可リスト外・混在）"
                ),
                MakeFactor(
                    Paths,
                    PathLevels,
                    "参照パス（なし・すべて基準フォルダ配下・外のものがある）"
                ),
                MakeFactor(Url, YesNo, "URL を参照するか"),
                MakeFactor(Redirect, YesNo, "書き込みリダイレクトがあるか"),
                MakeFactor(Bypass, YesNo, "サンドボックス迂回を要求するか"),
            ],
            isValid: IsValid
        );

    /// <summary>網羅しない組の一覧</summary>
    public static CoverageGapSet Gaps => CoverageGap.Set(EngineIds.OsBoundary, []);

    /// <summary>台帳の行</summary>
    public static EngineDescriptor Descriptor =>
        new(
            EngineIds.OsBoundary,
            "ShellPermission",
            "シェル許可の表（許可要求 → 自動承認してよいか。QuickER.AI の CopilotWorkspacePermissionPolicy）",
            Table,
            ShellPermissionRows.Table.Count,
            [
                "tests/QuickER.Tests/Platform/OsBoundary/ShellPermissionDimensions.cs",
                "tests/QuickER.Tests/Platform/OsBoundary/ShellPermissionRows.cs",
                "tests/QuickER.Tests/Platform/OsBoundary/ShellPermissionRows.Filled.cs",
                "tests/QuickER.Tests/Platform/OsBoundary/ShellPermissionTests.cs",
            ],
            ["src/QuickER.AI/CopilotWorkspacePermissionPolicy.cs"]
        );

    /// <summary>1 行を流した後、要求から計算した水準を数える</summary>
    public static void Observe(CoverageCounter counter, ShellPermissionCase row) =>
        counter.Observe(Levels(row.Build()));

    /// <summary>要求から各因子の水準を計算する</summary>
    public static IReadOnlyDictionary<string, string> Levels(PermissionRequest request) =>
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [Kind] = KindOf(request),
            [Commands] = CommandsOf(request),
            [Paths] = PathsOf(request),
            [Url] = Flag(request is PermissionRequestUrl || HasUrls(request)),
            [Redirect] = Flag(request is PermissionRequestShell { HasWriteFileRedirection: true }),
            [Bypass] = Flag(
                request
                    is PermissionRequestShell { RequestSandboxBypass: true }
                        or PermissionRequestWrite { RequestSandboxBypass: true }
                        or PermissionRequestRead { RequestSandboxBypass: true }
            ),
        };

    private static string Flag(bool value) => value ? "Yes" : "No";

    private static string KindOf(PermissionRequest request) =>
        request switch
        {
            PermissionRequestShell => "Shell",
            PermissionRequestWrite => "Write",
            PermissionRequestRead => "Read",
            PermissionRequestUrl => "Url",
            PermissionRequestMcp => "Mcp",
            _ => throw new ArgumentOutOfRangeException(
                nameof(request),
                request.GetType().Name,
                null
            ),
        };

    private static bool HasUrls(PermissionRequest request) =>
        request is PermissionRequestShell { PossibleUrls.Length: > 0 };

    /// <summary>コマンドの識別子を製品の許可リストと突き合わせる</summary>
    private static string CommandsOf(PermissionRequest request)
    {
        if (request is not PermissionRequestShell { Commands.Length: > 0 } shell)
        {
            return "None";
        }

        var commands = shell.Commands;
        var listed = commands.Count(command =>
            command?.Identifier is { } id
            && CopilotWorkspacePermissionPolicy.AllowedPathlessShellCommands.Contains(id)
        );

        return listed == commands.Length ? "Listed"
            : listed == 0 ? "Unlisted"
            : "Composite";
    }

    /// <summary>参照パスが基準フォルダ配下かを <see cref="PathContainment.IsSameOrUnder"/> で判定する</summary>
    private static string PathsOf(PermissionRequest request)
    {
        var paths = request switch
        {
            PermissionRequestShell { PossiblePaths.Length: > 0 } shell => shell.PossiblePaths,
            PermissionRequestWrite { FileName: { } fileName } => [fileName],
            PermissionRequestRead { Path: { } path } => [path],
            _ => Array.Empty<string>(),
        };

        if (paths.Length == 0)
        {
            return "None";
        }

        var root = ShellPermissionRows.Root;

        return paths.All(path => PathContainment.IsSameOrUnder(Path.Combine(root, path), root))
            ? "Under"
            : "Outside";
    }

    private static Factor MakeFactor(string name, string[] levels, string description) =>
        new(name, levels.Select(level => new Level(level)).ToList(), description);

    /// <summary>原理的に起こり得ない組：種類に無い因子</summary>
    private static bool IsValid(IReadOnlyDictionary<string, string> assignment)
    {
        if (!assignment.TryGetValue(Kind, out var kind))
        {
            return true;
        }

        // 因子が割り当てられていて、許す水準に入らないとき true
        bool Violates(string factor, params string[] allowed) =>
            assignment.TryGetValue(factor, out var level) && !allowed.Contains(level);

        return kind switch
        {
            // シェル要求は全因子が自由に組み合わさる
            "Shell" => true,

            // 読み書きの要求は対象のパスを必ず 1 つ持つ。コマンド・URL・リダイレクトを持たない
            "Write" or "Read" => !(
                Violates(Commands, "None")
                || Violates(Paths, "Under", "Outside")
                || Violates(Url, "No")
                || Violates(Redirect, "No")
            ),

            // URL 取得の要求は URL だけを持つ。コマンド・パス・リダイレクトも、迂回の要求の欄も持たない
            "Url" => !(
                Violates(Commands, "None")
                || Violates(Paths, "None")
                || Violates(Url, "Yes")
                || Violates(Redirect, "No")
                || Violates(Bypass, "No")
            ),

            // MCP の要求はコマンド・パス・URL・リダイレクト・迂回の要求の欄のどれも持たない
            "Mcp" => !(
                Violates(Commands, "None")
                || Violates(Paths, "None")
                || Violates(Url, "No")
                || Violates(Redirect, "No")
                || Violates(Bypass, "No")
            ),

            _ => true,
        };
    }
}
