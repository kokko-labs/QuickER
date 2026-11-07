using System.Diagnostics;
using System.IO;
using AwesomeAssertions;
using QuickER.AI;
using QuickER.Platform;
using QuickER.Tests.TestSupport.Engines;
using static QuickER.Tests.Platform.OsBoundary.LaunchEntrance;
using static QuickER.Tests.Platform.OsBoundary.LaunchOutcome;
using static QuickER.Tests.Platform.OsBoundary.LaunchShape;
using AiStrings = QuickER.AI.Resources.Strings;

namespace QuickER.Tests.Platform.OsBoundary;

/// <summary>
/// 起動の表：網羅しない組の一覧（<c>ProcessLaunchDimensions.Gaps</c>）から消した組を埋める行
/// （配線の入口・ロケータ・Wrap の引数・外部の起動）
/// </summary>
/// <remarks>
/// 期待は製品の XmlDoc・CLAUDE.md から導く。配線の入口は Guard の入口の行（<c>ShimApply</c>・<c>ShimWrapCommandLine</c>）と
/// 同じ入力の形を流し、各入口が固定するのは Guard と同じ結果と、その入口の文言・起動の形。
/// 文書に定めが無い振る舞いを写す行は、行の説明の先頭に「特性の固定（文書に定めが無い今の振る舞い）：」と書く。
/// </remarks>
internal static partial class ProcessLaunchRows
{
    private static IEnumerable<ScenarioRow<LaunchCase>> GapRows() =>
        [
            .. ClaudeWiringRows(),
            .. CodexStartInfoWiringRows(),
            .. CodexLaunchWiringRows(),
            .. CopilotWiringRows(),
            .. LocatorGapRows(),
            .. WrapGapRows(),
            .. ExternalGapRows(),
        ];

    /// <summary>配線の入口の 1 つの形（入口を呼ぶ処理と、その結果への表明）</summary>
    private sealed record WiringCase(
        string Id,
        LaunchShape Shape,
        LaunchOutcome Outcome,
        Func<object?> Act,
        Action<LaunchObservation> Expect
    );

    private static IEnumerable<ScenarioRow<LaunchCase>> WiringRows(
        LaunchEntrance entrance,
        string idPrefix,
        string description,
        IEnumerable<WiringCase> cases
    ) =>
        cases.Select(c =>
            Row(
                $"{idPrefix}-{c.Id}",
                description,
                entrance,
                c.Shape,
                c.Outcome,
                _ => c.Act(),
                c.Expect
            )
        );

    /// <summary>断る行の表明：メッセージは製品と同じ resx の書式から組んだ完全一致</summary>
    private static Action<LaunchObservation> RejectsWith(string message) =>
        o => o.ShouldHaveThrown<InvalidOperationException>(message);

    /// <summary>包み直す行の表明：起動先が cmd.exe のフルパスで、引数に指定の文字列を含む</summary>
    private static Action<LaunchObservation> WrappedInfoContaining(string contained) =>
        o =>
        {
            Started(o).FileName.Should().Be(SystemCmdExe);
            Started(o).Arguments.Should().Contain(contained);
        };

    /// <summary>
    /// <c>ClaudeCodeProcessClient.ApplyBatchShimGuard</c>：Guard の <c>ShimApply</c> の行と同じ入力の形を、claude の配線へ流す
    /// </summary>
    private static IEnumerable<ScenarioRow<LaunchCase>> ClaudeWiringRows()
    {
        const string shim = @"C:\npm\claude.cmd";
        const string newlinePath = "C:\\npm\\cl\r\naude.cmd";

        static ProcessStartInfo Guard(string fileName, params string[] arguments)
        {
            var startInfo = Info(fileName, arguments);
            ClaudeCodeProcessClient.ApplyBatchShimGuard(startInfo);
            return startInfo;
        }

        // 引数 1 本を引用して連結へ入れる形（引用の仕方は AppendQuotedArgument の remarks）
        (string Id, LaunchShape Shape, string Argument, string Quoted)[] quotedArguments =
        [
            ("arg-backslash", ArgBackslash, @"C:\dir\", "\"C:\\dir\\\\\""),
            ("arg-cmd-meta", ArgCmdMeta, "a & b", "\"a & b\""),
            ("arg-exclamation", ArgExclamation, "!EVIL!", "\"!EVIL!\""),
            ("arg-quote", ArgQuote, "say \"hi\"", "\"say \"\"hi\"\"\""),
        ];

        WiringCase[] cases =
        [
            .. quotedArguments.Select(c => new WiringCase(
                c.Id,
                c.Shape,
                Wrapped,
                () => Guard(shim, c.Argument),
                WrappedInfoContaining(c.Quoted)
            )),
            new(
                "arg-empty",
                ArgEmpty,
                Wrapped,
                () => Guard(shim, "--flag", string.Empty),
                o => Started(o).Arguments.Should().EndWith("\"--flag\" \"\"\"")
            ),
            new(
                "path-exclamation",
                PathExclamation,
                Wrapped,
                () => Guard(@"C:\!EVIL!\claude.cmd", "-p"),
                WrappedInfoContaining("\"C:\\!EVIL!\\claude.cmd\"")
            ),
            new(
                "shim-spelling",
                ShimSpelling,
                Wrapped,
                () => Guard(@"C:\npm\claude.cmd ", "-p"),
                WrappedInfoContaining("\"-p\"")
            ),
            new(
                "exe-path-with-shim-meta",
                ExePathWithShimMeta,
                PassThrough,
                () => Guard(@"C:\100%\claude.exe", "-p"),
                o =>
                {
                    Started(o).FileName.Should().Be(@"C:\100%\claude.exe");
                    Started(o).ArgumentList.Should().Equal("-p");
                }
            ),
            new(
                "unix-path",
                NonWindowsOrNullPath,
                PassThrough,
                () => Guard("/usr/local/bin/claude", "-p"),
                o =>
                {
                    Started(o).FileName.Should().Be("/usr/local/bin/claude");
                    Started(o).ArgumentList.Should().Equal("-p");
                }
            ),
            new(
                "path-newline",
                PathNewline,
                Rejected,
                () => Guard(newlinePath),
                RejectsWith(string.Format(AiStrings.ClaudeCode_ArgHasNewline, newlinePath))
            ),
        ];

        return WiringRows(
            ClaudeApplyBatchShimGuard,
            "shim-claude-guard",
            "ApplyBatchShimGuard: Guard（Apply）と同じ入力の形に、同じ結果と claude の文言を返す",
            cases
        );
    }

    /// <summary>
    /// <c>CodexAppServerClient.ResolveStartInfo</c>：Guard の <c>ShimWrapCommandLine</c> の行と同じ入力の形を、codex の配線へ流す
    /// </summary>
    private static IEnumerable<ScenarioRow<LaunchCase>> CodexStartInfoWiringRows()
    {
        const string shim = @"C:\tools\codex.cmd";
        const string newlinePath = "C:\\tools\\co\r\ndex.cmd";
        const string newlineArguments = "app-server\r\n--listen stdio://";

        // 引数にバックスラッシュ・空を含む形は、拒否しないこと（メタ文字の集合に無い）と cmd.exe 経由であることだけを見る。
        // 連結の末尾の形（外側の引用符や空白）は固定しない
        static Action<LaunchObservation> WrappedNotRejected(string startsWith) =>
            o =>
            {
                var (fileName, arguments) = Pair(o);
                fileName.Should().Be(SystemCmdExe);
                arguments.Should().StartWith(startsWith);
            };

        WiringCase[] cases =
        [
            new(
                "arg-backslash",
                ArgBackslash,
                Wrapped,
                () => CodexAppServerClient.ResolveStartInfo(shim, @"app-server C:\dir\"),
                WrappedNotRejected("/d /s /v:off /c \"\"C:\\tools\\codex.cmd\" ")
            ),
            new(
                "arg-empty",
                ArgEmpty,
                Wrapped,
                () => CodexAppServerClient.ResolveStartInfo(shim, string.Empty),
                WrappedNotRejected("/d /s /v:off /c \"\"C:\\tools\\codex.cmd\"")
            ),
            new(
                "arg-exclamation",
                ArgExclamation,
                Wrapped,
                () => CodexAppServerClient.ResolveStartInfo(shim, "app-server !x!"),
                o =>
                {
                    var (fileName, arguments) = Pair(o);
                    fileName.Should().Be(SystemCmdExe);
                    arguments
                        .Should()
                        .Be("/d /s /v:off /c \"\"C:\\tools\\codex.cmd\" app-server !x!\"");
                }
            ),
            new(
                "arg-newline",
                ArgNewline,
                Rejected,
                () => CodexAppServerClient.ResolveStartInfo(shim, newlineArguments),
                RejectsWith(string.Format(AiStrings.Codex_ArgHasNewline, newlineArguments))
            ),
            new(
                "unix-path",
                NonWindowsOrNullPath,
                PassThrough,
                () => CodexAppServerClient.ResolveStartInfo("/usr/local/bin/codex", CodexArguments),
                o =>
                {
                    var (fileName, arguments) = Pair(o);
                    fileName.Should().Be("/usr/local/bin/codex");
                    arguments.Should().Be(CodexArguments);
                }
            ),
            new(
                "path-exclamation",
                PathExclamation,
                Wrapped,
                () => CodexAppServerClient.ResolveStartInfo(@"C:\!EVIL!\codex.cmd", "app-server"),
                o =>
                {
                    var (fileName, arguments) = Pair(o);
                    fileName.Should().Be(SystemCmdExe);
                    arguments
                        .Should()
                        .Be("/d /s /v:off /c \"\"C:\\!EVIL!\\codex.cmd\" app-server\"");
                }
            ),
            new(
                "path-newline",
                PathNewline,
                Rejected,
                () => CodexAppServerClient.ResolveStartInfo(newlinePath, "app-server"),
                RejectsWith(string.Format(AiStrings.Codex_ArgHasNewline, newlinePath))
            ),
            new(
                "shim-spelling",
                ShimSpelling,
                Wrapped,
                () => CodexAppServerClient.ResolveStartInfo(@"C:\tools\codex.cmd.", "app-server"),
                o => Pair(o).FileName.Should().Be(SystemCmdExe)
            ),
        ];

        return WiringRows(
            CodexResolveStartInfo,
            "shim-codex-start",
            "ResolveStartInfo: Guard（WrapCommandLine）と同じ入力の形に、同じ結果と codex の文言を返す",
            cases
        );
    }

    /// <summary>
    /// <c>CodexAppServerClient.ResolveLaunch</c>：Guard のパスの行と同じ入力の形を、固定の起動引数のまま codex の配線へ流す
    /// </summary>
    private static IEnumerable<ScenarioRow<LaunchCase>> CodexLaunchWiringRows()
    {
        const string newlinePath = "C:\\npm\\co\r\ndex.cmd";
        const string percentPath = @"C:\%EVIL%\codex.cmd";
        const string quotedPath = @"C:\tools\evil"" & calc & ""x.cmd";

        WiringCase[] cases =
        [
            new(
                "exe-path-with-shim-meta",
                ExePathWithShimMeta,
                PassThrough,
                () => CodexAppServerClient.ResolveLaunch(@"C:\tools\a & b\codex.exe"),
                o =>
                {
                    var (fileName, arguments) = Pair(o);
                    fileName.Should().Be(@"C:\tools\a & b\codex.exe");
                    arguments.Should().Be(CodexArguments);
                }
            ),
            new(
                "path-exclamation",
                PathExclamation,
                Wrapped,
                () => CodexAppServerClient.ResolveLaunch(@"C:\!EVIL!\codex.cmd"),
                o =>
                {
                    var (fileName, arguments) = Pair(o);
                    fileName.Should().Be(SystemCmdExe);
                    arguments
                        .Should()
                        .Be($"/d /s /v:off /c \"\"C:\\!EVIL!\\codex.cmd\" {CodexArguments}\"");
                }
            ),
            new(
                "path-newline",
                PathNewline,
                Rejected,
                () => CodexAppServerClient.ResolveLaunch(newlinePath),
                RejectsWith(string.Format(AiStrings.Codex_ArgHasNewline, newlinePath))
            ),
            new(
                "path-percent",
                PathPercent,
                Rejected,
                () => CodexAppServerClient.ResolveLaunch(percentPath),
                RejectsWith(string.Format(AiStrings.Codex_PathHasEnvExpansion, percentPath))
            ),
            new(
                "path-quote",
                PathQuote,
                Rejected,
                () => CodexAppServerClient.ResolveLaunch(quotedPath),
                RejectsWith(string.Format(AiStrings.Codex_PathHasQuote, quotedPath))
            ),
            new(
                "shim-spelling",
                ShimSpelling,
                Wrapped,
                () => CodexAppServerClient.ResolveLaunch(@"C:\npm\codex.cmd "),
                o => Pair(o).FileName.Should().Be(SystemCmdExe)
            ),
        ];

        return WiringRows(
            CodexResolveLaunch,
            "shim-codex-launch",
            "ResolveLaunch: Guard と同じ入力の形に、同じ結果と codex の文言を返す（引数は固定の app-server の起動引数）",
            cases
        );
    }

    /// <summary><c>CopilotCliLocator.EnsureSafeToLaunch</c>：検証を通ったパスをそのまま返す・綴りの揺れたシムも検証する</summary>
    private static IEnumerable<ScenarioRow<LaunchCase>> CopilotWiringRows()
    {
        // 末尾の空白と大文字の拡張子でも、シムとして検証する（落とさないと % の検証を素通りする）
        const string spelledPath = @"C:\%EVIL%\copilot.CMD ";

        WiringCase[] cases =
        [
            new(
                "unix-path-passes-through",
                NonWindowsOrNullPath,
                PassThrough,
                () => CopilotCliLocator.EnsureSafeToLaunch("/usr/local/bin/copilot"),
                o => ((string)o.Value!).Should().Be("/usr/local/bin/copilot")
            ),
            new(
                "shim-spelling-is-checked",
                ShimSpelling,
                Rejected,
                () => CopilotCliLocator.EnsureSafeToLaunch(spelledPath),
                RejectsWith(string.Format(AiStrings.Copilot_PathHasEnvExpansion, spelledPath))
            ),
        ];

        return WiringRows(
            CopilotEnsureSafeToLaunch,
            "shim-copilot",
            "EnsureSafeToLaunch: Guard と同じ入力の形に、検証を通ったパスのまま返すか copilot の文言で断る",
            cases
        );
    }

    /// <summary>PATH の走査を呼ぶ入口（PATH の値と存在判定を注入する）</summary>
    private delegate string? Locator(string? pathValue, Func<string, bool> exists);

    /// <summary>ロケータへ流す 1 つの形。<c>Expected</c> が null の形は見つからない（null）が期待</summary>
    private sealed record LocateCase(
        string Id,
        LaunchShape Shape,
        string Description,
        bool WindowsOnly,
        Func<string, Locator, string?> Act,
        Func<string, string?> Expected
    );

    private static IEnumerable<ScenarioRow<LaunchCase>> LocatorGapRows() =>
        [
            .. LocatorRows(
                PathExecutableResolve,
                "resolve-shared-locator",
                "copilot",
                (pathValue, exists) => PathExecutableResolver.Resolve("copilot", pathValue, exists),
                CmdCandidate,
                EmptyEntries,
                EmptyPath,
                ExeAndCmd,
                NotOnPath,
                OnPath,
                RelativeEntries,
                UnvalidatedEntry
            ),
            .. LocatorRows(
                CodexLocate,
                "resolve-codex-locator",
                "codex",
                (pathValue, exists) => CodexCliLocator.ResolveExecutablePath(pathValue, exists),
                ExeAndCmd,
                ExtensionOutsidePathExt,
                ExtensionlessFirst,
                PathExtOrder,
                PerCommandName,
                QuotedEntry,
                UnvalidatedEntry
            ),
            .. LocatorRows(
                CopilotLocate,
                "resolve-copilot-locator",
                "copilot",
                (pathValue, exists) => CopilotCliLocator.ResolveExecutablePath(pathValue, exists),
                ExtensionOutsidePathExt,
                ExtensionlessFirst,
                PathExtOrder,
                PerCommandName,
                QuotedEntry
            ),
        ];

    /// <summary>入口が受け取る形のうち、指定の形だけを同じ PATH・存在の注入で流す（3 つの入口で同じ表を共有する）</summary>
    private static IEnumerable<ScenarioRow<LaunchCase>> LocatorRows(
        LaunchEntrance entrance,
        string idPrefix,
        string tool,
        Locator locate,
        params LaunchShape[] shapes
    ) =>
        LocateCases()
            .Where(c => shapes.Contains(c.Shape))
            .Select(c =>
            {
                var expected = c.Expected(tool);

                return Row(
                    $"{idPrefix}-{c.Id}",
                    c.Description,
                    entrance,
                    c.Shape,
                    expected is null ? NotFound : Resolved,
                    _ => c.Act(tool, locate),
                    o => ((string?)o.Value).Should().Be(expected),
                    c.WindowsOnly
                );
            });

    private static LocateCase[] LocateCases()
    {
        static string Bin(string tool) => FakeBin(tool);

        static string Other(string tool) => tool == "copilot" ? "codex" : "copilot";

        static string In(string tool, string name) => Path.Combine(Bin(tool), name);

        const string quotedDirectory = @"C:\evil"" & calc";

        return
        [
            new(
                "found-on-path-returns-full-path",
                OnPath,
                "PATH 上にコマンドがあればフルパスを返す",
                false,
                (t, locate) => locate(Bin(t), Existing(In(t, Primary(t)))),
                t => In(t, Primary(t))
            ),
            new(
                "windows-cmd-candidate-is-found",
                CmdCandidate,
                "Windows では .cmd の候補も検出できる",
                true,
                (t, locate) => locate(Bin(t), Existing(In(t, t + ".cmd"))),
                t => In(t, t + ".cmd")
            ),
            new(
                "windows-prefers-exe-over-cmd",
                ExeAndCmd,
                "Windows では .exe が .cmd より優先される",
                true,
                (t, locate) => locate(Bin(t), Existing(In(t, t + ".exe"), In(t, t + ".cmd"))),
                t => In(t, t + ".exe")
            ),
            new(
                "not-on-path-returns-null",
                NotOnPath,
                "どの要素にも無ければ null",
                false,
                (t, locate) =>
                    locate(
                        string.Join(
                            Path.PathSeparator,
                            Bin(t),
                            Path.Combine(Path.GetTempPath(), "QuickERTests", "other-bin")
                        ),
                        _ => false
                    ),
                _ => null
            ),
            new(
                "empty-path-null-returns-null",
                EmptyPath,
                "PATH が null なら null",
                false,
                (_, locate) => locate(null, _ => true),
                _ => null
            ),
            new(
                "empty-path-empty-returns-null",
                EmptyPath,
                "PATH が空なら null",
                false,
                (_, locate) => locate(string.Empty, _ => true),
                _ => null
            ),
            new(
                "skips-empty-entries",
                EmptyEntries,
                "PATH の空要素は読み飛ばして後続を走査する",
                false,
                (t, locate) =>
                    locate(
                        string.Join(Path.PathSeparator, string.Empty, "   ", Bin(t)),
                        Existing(In(t, Primary(t)))
                    ),
                t => In(t, Primary(t))
            ),
            new(
                "skips-relative-entries",
                RelativeEntries,
                "PATH の相対要素は、相対の候補があっても読み飛ばしてフルパスだけを返す",
                false,
                (t, locate) =>
                    locate(
                        string.Join(Path.PathSeparator, ".", "bin", Bin(t)),
                        candidate =>
                            !Path.IsPathFullyQualified(candidate) || candidate == In(t, Primary(t))
                    ),
                t => In(t, Primary(t))
            ),
            new(
                "does-not-validate-quoted-directory",
                UnvalidatedEntry,
                "解決は引用符を含む PATH の要素でも例外を投げない（検証は起動直前の関門に置く）",
                false,
                (t, locate) =>
                    locate(quotedDirectory, Existing(Path.Combine(quotedDirectory, Primary(t)))),
                t => Path.Combine(quotedDirectory, Primary(t))
            ),
            new(
                "skips-other-command",
                PerCommandName,
                "コマンド名ごとに探す（別のコマンドのファイルがあっても拾わない）",
                false,
                (t, locate) => locate(Bin(t), Existing(In(t, Primary(Other(t))))),
                _ => null
            ),
            new(
                "windows-quoted-entry-is-scanned",
                QuotedEntry,
                "Windows では引用符で囲んだ PATH 要素も、引用符を外して走査する",
                true,
                (t, locate) => locate("\"" + Bin(t) + "\"", Existing(In(t, t + ".cmd"))),
                t => In(t, t + ".cmd")
            ),
            new(
                "windows-extensionless-file-is-skipped",
                ExtensionlessFirst,
                "Windows では拡張子なしのファイルを読み飛ばして、後ろのフォルダの実体を返す",
                true,
                (t, locate) =>
                {
                    var shDirectory = Path.Combine(Path.GetTempPath(), "QuickERTests", "sh-only");

                    return locate(
                        string.Join(Path.PathSeparator, shDirectory, Bin(t)),
                        Existing(Path.Combine(shDirectory, t), In(t, t + ".cmd"))
                    );
                },
                t => In(t, t + ".cmd")
            ),
            new(
                "windows-default-order-bat-before-cmd",
                PathExtOrder,
                "ロケータは PATHEXT を渡さず既定の順（.exe・.bat・.cmd）で走査する。.cmd と .bat が並べば .bat を選ぶ",
                true,
                (t, locate) => locate(Bin(t), Existing(In(t, t + ".cmd"), In(t, t + ".bat"))),
                t => In(t, t + ".bat")
            ),
            new(
                "extension-outside-candidates-is-not-tried",
                ExtensionOutsidePathExt,
                "ロケータは PATHEXT を注入できないので、形の意味を候補の外の拡張子と読み替える："
                    + "候補（.exe・.cmd・.bat）の外の拡張子（.com・.ps1）のファイルだけがあっても選ばない",
                false,
                (t, locate) => locate(Bin(t), Existing(In(t, t + ".com"), In(t, t + ".ps1"))),
                _ => null
            ),
        ];
    }

    /// <summary><c>WrapCommandLine</c> に引数のバックスラッシュ・空を渡す（メタ文字の集合に無いので断らない）</summary>
    private static IEnumerable<ScenarioRow<LaunchCase>> WrapGapRows() =>
        [
            // 断らないこと（XmlDoc：拒否するのは " & | < > ^ % と改行だけ）と cmd.exe 経由の形だけを見る。
            // 外側の引用符や連結の末尾の形は実装の都合なので固定しない
            Row(
                "shim-wrap-passes-arg-backslash",
                "WrapCommandLine はバックスラッシュで終わる引数を拒否せず包み直す",
                ShimWrapCommandLine,
                ArgBackslash,
                Wrapped,
                _ => Wrap(@"C:\tools\codex.cmd", @"app-server C:\dir\"),
                o =>
                {
                    var (fileName, arguments) = Pair(o);
                    fileName.Should().Be(SystemCmdExe);
                    arguments.Should().StartWith("/d /s /v:off /c \"\"C:\\tools\\codex.cmd\" ");
                }
            ),
            Row(
                "shim-wrap-passes-arg-empty",
                "WrapCommandLine は空の引数を拒否せず包み直す",
                ShimWrapCommandLine,
                ArgEmpty,
                Wrapped,
                _ => Wrap(@"C:\tools\codex.cmd", string.Empty),
                o => Pair(o).FileName.Should().Be(SystemCmdExe)
            ),
        ];

    /// <summary><c>ExternalLauncher.StartExecutable</c>・<c>OpenUrl</c> の、起動の前の規則と起動の形</summary>
    private static IEnumerable<ScenarioRow<LaunchCase>> ExternalGapRows()
    {
        // StartExecutable はフルパスだけを受ける（XmlDoc：「フルパスだけ」。BareExecutableNames の許可は CreateProcess だけ）
        (string Id, LaunchShape Shape, string FileName)[] bareNames =
        [
            ("bare-name", BareName, "cmd.exe"),
            ("allowed-bare-name-is-for-create-process-only", AllowedBareName, "dotnet"),
        ];

        var restartPath = Path.Combine(Path.GetTempPath(), "QuickER.exe");

        return bareNames
            .Select(c =>
                Row(
                    $"external-start-executable-rejects-{c.Id}",
                    "StartExecutable: フルパスでない実行ファイル（名前だけ）は、dotnet でも起動せずに ArgumentException",
                    ExternalStartExecutable,
                    c.Shape,
                    Rejected,
                    o =>
                    {
                        ExternalLauncher.StartExecutable(c.FileName, o.Started.Add);
                        return null;
                    },
                    o =>
                    {
                        o.ShouldHaveThrown<ArgumentException>();
                        o.Started.Should().BeEmpty();
                    }
                )
            )
            .Concat([
                Row(
                    "external-start-executable-starts-through-shell",
                    "StartExecutable: フルパスの実行ファイルはシェル経由で、そのパスのまま起動する",
                    ExternalStartExecutable,
                    ShellExecute,
                    Launched,
                    o =>
                    {
                        ExternalLauncher.StartExecutable(restartPath, o.Started.Add);
                        return null;
                    },
                    o =>
                    {
                        o.Started.Should().ContainSingle();
                        o.Started[0].UseShellExecute.Should().BeTrue();
                        o.Started[0].FileName.Should().Be(restartPath);
                    }
                ),
                // 根拠：ExternalLauncher.cs:54 は起動のデリゲートを try の外で呼ぶので例外はそのまま出る。
                // 呼び出し元はこれを前提に catch している（ShellChatLinkOpener.cs:48-58 は全例外を catch してエラーで知らせ、
                // AboutDialog.xaml.cs:59-72 は Win32Exception・InvalidOperationException・ArgumentException を catch する）
                Row(
                    "external-open-url-propagates-launch-failure",
                    "特性の固定（文書に定めが無い今の振る舞い）：OpenUrl は起動のデリゲートが投げた例外をそのまま伝える",
                    ExternalOpenUrl,
                    LaunchFails,
                    Rejected,
                    o =>
                    {
                        ExternalLauncher.OpenUrl(
                            new Uri("https://example.com/"),
                            _ => throw new InvalidOperationException("no browser")
                        );
                        return null;
                    },
                    o =>
                    {
                        o.ShouldHaveThrown<InvalidOperationException>("no browser");
                        o.Started.Should().BeEmpty();
                    }
                ),
            ]);
    }
}
