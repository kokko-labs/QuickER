using System.IO;
using AwesomeAssertions;
using QuickER.AI;
using QuickER.Platform;
using QuickER.Tests.TestSupport.Engines;
using static QuickER.Tests.Platform.OsBoundary.LaunchEntrance;
using static QuickER.Tests.Platform.OsBoundary.LaunchOutcome;
using static QuickER.Tests.Platform.OsBoundary.LaunchShape;

namespace QuickER.Tests.Platform.OsBoundary;

/// <summary>
/// 起動の表：実行ファイルの解決（QuickER.Platform の <c>PathExecutableResolver</c> と、QuickER.AI の
/// <c>CopilotCliLocator</c> / <c>CodexCliLocator</c> の PATH 走査）。
/// 実 PATH を書き換えるとプロセス全体（＝並列テスト）へ影響するため、PATH の値と存在判定を注入して流す
/// </summary>
internal static partial class ProcessLaunchRows
{
    /// <summary>PATHEXT の順の行が使うフォルダ</summary>
    private static readonly string ToolsFolder = Path.Combine(Path.GetTempPath(), "tools");

    /// <summary>走査対象に使う架空のディレクトリ（実在は不要＝存在判定は注入する）</summary>
    private static string FakeBin(string tool) =>
        Path.Combine(Path.GetTempPath(), "QuickERTests", $"{tool}-bin");

    /// <summary>OS ごとに最初に当たる候補ファイル名</summary>
    private static string Primary(string tool) =>
        OperatingSystem.IsWindows() ? $"{tool}.exe" : tool;

    /// <summary>指定した実在パス集合に対する存在判定を作る（Windows に合わせて大文字小文字は無視する）</summary>
    private static Func<string, bool> Existing(params string[] paths) =>
        candidate => paths.Contains(candidate, StringComparer.OrdinalIgnoreCase);

    /// <summary>PATHEXT の値と、<see cref="ToolsFolder"/> にあるファイルの集合で <c>codex</c> を解決する</summary>
    private static string? ResolveInTools(string? pathExt, params string[] existing)
    {
        var files = existing
            .Select(name => Path.Combine(ToolsFolder, name))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return PathExecutableResolver.Resolve("codex", ToolsFolder, files.Contains, pathExt);
    }

    private static IEnumerable<ScenarioRow<LaunchCase>> ResolveRows() =>
        [
            .. CopilotLocateRows(),
            .. CodexLocateRows(),
            .. SharedResolverRows(),
            .. DefaultOrderRows(),
            .. PathExtOrderRows(),
            .. MissingFromPathExtRows(),
        ];

    /// <summary>copilot CLI の PATH 走査（<c>CopilotCliLocator.ResolveExecutablePath</c>）</summary>
    private static IEnumerable<ScenarioRow<LaunchCase>> CopilotLocateRows()
    {
        var bin = FakeBin("copilot");

        static string? Locate(string? pathValue, Func<string, bool> exists) =>
            CopilotCliLocator.ResolveExecutablePath(pathValue, exists);

        string?[] emptyPaths = [null, string.Empty];

        return new[]
        {
            Row(
                "resolve-copilot-found-on-path-returns-full-path",
                "PATH 上に copilot があればフルパスを返す",
                CopilotLocate,
                OnPath,
                Resolved,
                _ => Locate(bin, Existing(Path.Combine(bin, Primary("copilot")))),
                o => ((string?)o.Value).Should().Be(Path.Combine(bin, Primary("copilot")))
            ),
            // Windows では拡張子付き候補（.cmd 等）も走査対象になる（npm 製 CLI 対策）
            Row(
                "resolve-copilot-windows-cmd-candidate-is-found",
                "Windows では copilot.cmd も検出できる",
                CopilotLocate,
                CmdCandidate,
                Resolved,
                _ => Locate(bin, Existing(Path.Combine(bin, "copilot.cmd"))),
                o => ((string?)o.Value).Should().Be(Path.Combine(bin, "copilot.cmd")),
                windowsOnly: true
            ),
            // 候補の走査順を固定する
            Row(
                "resolve-copilot-windows-prefers-exe-over-cmd",
                "Windows では copilot.exe が copilot.cmd より優先される",
                CopilotLocate,
                ExeAndCmd,
                Resolved,
                _ =>
                    Locate(
                        bin,
                        Existing(Path.Combine(bin, "copilot.exe"), Path.Combine(bin, "copilot.cmd"))
                    ),
                o => ((string?)o.Value).Should().Be(Path.Combine(bin, "copilot.exe")),
                windowsOnly: true
            ),
            Row(
                "resolve-copilot-not-on-path-returns-null",
                "PATH 上に copilot が無ければ null",
                CopilotLocate,
                NotOnPath,
                NotFound,
                _ =>
                    Locate(
                        string.Join(
                            Path.PathSeparator,
                            bin,
                            Path.Combine(Path.GetTempPath(), "QuickERTests", "other-bin")
                        ),
                        anyPath => false
                    ),
                o => o.Value.Should().BeNull()
            ),
            // 空要素・空白のみの要素を含む PATH でも落ちず、後続のディレクトリを走査する
            Row(
                "resolve-copilot-skips-empty-entries",
                "PATH の空要素は読み飛ばして後続を走査する",
                CopilotLocate,
                EmptyEntries,
                Resolved,
                _ =>
                    Locate(
                        string.Join(Path.PathSeparator, string.Empty, "   ", bin),
                        Existing(Path.Combine(bin, Primary("copilot")))
                    ),
                o => ((string?)o.Value).Should().Be(Path.Combine(bin, Primary("copilot")))
            ),
            // 検証を解決側へ置くと、接続タブを開いて検出するだけで例外になる
            Row(
                "resolve-copilot-does-not-validate-quoted-directory",
                "解決は引用符を含むパスでも例外を投げない",
                CopilotLocate,
                UnvalidatedEntry,
                Resolved,
                _ =>
                    Locate(
                        @"C:\evil"" & calc",
                        Existing(Path.Combine(@"C:\evil"" & calc", Primary("copilot")))
                    ),
                o =>
                    ((string?)o.Value)
                        .Should()
                        .Be(Path.Combine(@"C:\evil"" & calc", Primary("copilot")))
            ),
        }.Concat(
            emptyPaths.Select(pathValue =>
                Row(
                    $"resolve-copilot-empty-path-{(pathValue is null ? "null" : "empty")}-returns-null",
                    "PATH が空・未設定なら null",
                    CopilotLocate,
                    EmptyPath,
                    NotFound,
                    _ => Locate(pathValue, anyPath => true),
                    o => o.Value.Should().BeNull()
                )
            )
        );
    }

    /// <summary>codex CLI の PATH 走査（<c>CodexCliLocator.ResolveExecutablePath</c>）</summary>
    private static IEnumerable<ScenarioRow<LaunchCase>> CodexLocateRows()
    {
        var bin = FakeBin("codex");

        static string? Locate(string? pathValue, Func<string, bool> exists) =>
            CodexCliLocator.ResolveExecutablePath(pathValue, exists);

        string?[] emptyPaths = [null, string.Empty];

        return new[]
        {
            Row(
                "resolve-codex-found-on-path-returns-full-path",
                "PATH 上に codex があればフルパスを返す",
                CodexLocate,
                OnPath,
                Resolved,
                _ => Locate(bin, Existing(Path.Combine(bin, Primary("codex")))),
                o => ((string?)o.Value).Should().Be(Path.Combine(bin, Primary("codex")))
            ),
            // Windows では拡張子付き候補（.cmd 等）も走査対象になる
            Row(
                "resolve-codex-windows-cmd-candidate-is-found",
                "Windows では codex.cmd も検出できる",
                CodexLocate,
                CmdCandidate,
                Resolved,
                _ => Locate(bin, Existing(Path.Combine(bin, "codex.cmd"))),
                o => ((string?)o.Value).Should().Be(Path.Combine(bin, "codex.cmd")),
                windowsOnly: true
            ),
            Row(
                "resolve-codex-not-on-path-returns-null",
                "PATH 上に codex が無ければ null",
                CodexLocate,
                NotOnPath,
                NotFound,
                _ =>
                    Locate(
                        string.Join(
                            Path.PathSeparator,
                            bin,
                            Path.Combine(Path.GetTempPath(), "QuickERTests", "other-bin")
                        ),
                        anyPath => false
                    ),
                o => o.Value.Should().BeNull()
            ),
            // 空要素・空白のみの要素を含む PATH でも落ちず、後続のディレクトリを走査する
            Row(
                "resolve-codex-skips-empty-entries",
                "PATH の空要素は読み飛ばして後続を走査する",
                CodexLocate,
                EmptyEntries,
                Resolved,
                _ =>
                    Locate(
                        string.Join(Path.PathSeparator, string.Empty, "   ", bin),
                        Existing(Path.Combine(bin, Primary("codex")))
                    ),
                o => ((string?)o.Value).Should().Be(Path.Combine(bin, Primary("codex")))
            ),
            // 相対パスの PATH 要素は、カレントフォルダに候補があっても採用せず後続を走査する
            // （解決結果は常にフルパス＝起動する実体がカレントフォルダ次第で変わらない）
            // 相対の候補はすべて「存在する」扱いにする＝相対要素を読み飛ばさなければ ".\codex.exe" が返る
            Row(
                "resolve-codex-skips-relative-entries",
                "PATH の相対要素は読み飛ばしてフルパスだけを返す",
                CodexLocate,
                RelativeEntries,
                Resolved,
                _ =>
                    Locate(
                        string.Join(Path.PathSeparator, ".", "bin", bin),
                        candidate =>
                            !Path.IsPathFullyQualified(candidate)
                            || candidate == Path.Combine(bin, Primary("codex"))
                    ),
                o => ((string?)o.Value).Should().Be(Path.Combine(bin, Primary("codex")))
            ),
        }.Concat(
            emptyPaths.Select(pathValue =>
                Row(
                    $"resolve-codex-empty-path-{(pathValue is null ? "null" : "empty")}-returns-null",
                    "PATH が空・未設定なら null",
                    CodexLocate,
                    EmptyPath,
                    NotFound,
                    _ => Locate(pathValue, anyPath => true),
                    o => o.Value.Should().BeNull()
                )
            )
        );
    }

    /// <summary>共有走査（<c>PathExecutableResolver</c>）。codex / claude / copilot の 3 ロケーターが同じ走査本体を使う前提</summary>
    private static IEnumerable<ScenarioRow<LaunchCase>> SharedResolverRows()
    {
        var bin = FakeBin("copilot");
        var copilot = Path.Combine(bin, Primary("copilot"));

        return
        [
            Row(
                "resolve-shared-per-command-name-finds-existing-command",
                "PathExecutableResolver_ResolvesPerCommandName",
                PathExecutableResolve,
                PerCommandName,
                Resolved,
                _ => PathExecutableResolver.Resolve("copilot", bin, Existing(copilot)),
                o => ((string?)o.Value).Should().Be(copilot)
            ),
            Row(
                "resolve-shared-per-command-name-skips-other-command",
                "PathExecutableResolver_ResolvesPerCommandName",
                PathExecutableResolve,
                PerCommandName,
                NotFound,
                _ => PathExecutableResolver.Resolve("codex", bin, Existing(copilot)),
                o => o.Value.Should().BeNull()
            ),
            // Windows では引用符で囲んだ PATH 要素（Windows では正当）も、引用符を外して走査する
            Row(
                "resolve-shared-windows-quoted-entry-is-scanned",
                "Windows では引用符で囲んだ PATH 要素も走査する",
                PathExecutableResolve,
                QuotedEntry,
                Resolved,
                _ =>
                    PathExecutableResolver.Resolve(
                        "copilot",
                        "\"" + bin + "\"",
                        Existing(Path.Combine(bin, "copilot.cmd"))
                    ),
                o => ((string?)o.Value).Should().Be(Path.Combine(bin, "copilot.cmd")),
                windowsOnly: true
            ),
            // 拡張子なしの名前は CreateProcess が .exe を補うため起動できない。npm が置く sh スクリプトを候補にせず、後ろの実体まで走査する
            Row(
                "resolve-shared-windows-extensionless-file-is-skipped",
                "Windows では拡張子なしのファイルを読み飛ばして後ろのフォルダの実体を返す",
                PathExecutableResolve,
                ExtensionlessFirst,
                Resolved,
                _ =>
                {
                    var shScriptDirectory = Path.Combine(
                        Path.GetTempPath(),
                        "QuickERTests",
                        "sh-only"
                    );
                    var shScript = Path.Combine(shScriptDirectory, "copilot");
                    var expected = Path.Combine(bin, "copilot.cmd");
                    var pathValue = string.Join(Path.PathSeparator, shScriptDirectory, bin);

                    return PathExecutableResolver.Resolve(
                        "copilot",
                        pathValue,
                        Existing(shScript, expected)
                    );
                },
                o => ((string?)o.Value).Should().Be(Path.Combine(bin, "copilot.cmd")),
                windowsOnly: true
            ),
        ];
    }

    /// <summary>PATHEXT が無いときの既定の順（.exe・.bat・.cmd）</summary>
    private static IEnumerable<ScenarioRow<LaunchCase>> DefaultOrderRows() =>
        [
            Row(
                "resolve-default-order-bat-before-cmd",
                "既定の順（PATHEXT が無いとき）は .exe・.bat・.cmd で、.cmd と .bat が並べば .bat を選ぶ",
                PathExecutableResolve,
                PathExtOrder,
                Resolved,
                _ => ResolveInTools(null, "codex.cmd", "codex.bat"),
                o => ((string?)o.Value).Should().Be(Path.Combine(ToolsFolder, "codex.bat"))
            ),
            Row(
                "resolve-default-order-exe-before-cmd",
                "既定の順（PATHEXT が無いとき）は .exe・.bat・.cmd で、.cmd と .bat が並べば .bat を選ぶ",
                PathExecutableResolve,
                PathExtOrder,
                Resolved,
                _ => ResolveInTools(null, "codex.cmd", "codex.exe"),
                o => ((string?)o.Value).Should().Be(Path.Combine(ToolsFolder, "codex.exe"))
            ),
            Row(
                "resolve-default-order-cmd-only",
                "既定の順（PATHEXT が無いとき）は .exe・.bat・.cmd で、.cmd と .bat が並べば .bat を選ぶ",
                PathExecutableResolve,
                PathExtOrder,
                Resolved,
                _ => ResolveInTools(null, "codex.cmd"),
                o => ((string?)o.Value).Should().Be(Path.Combine(ToolsFolder, "codex.cmd"))
            ),
            Row(
                "resolve-default-order-overload-without-pathext",
                "既定の順（PATHEXT が無いとき）は .exe・.bat・.cmd で、.cmd と .bat が並べば .bat を選ぶ",
                PathExecutableResolve,
                PathExtOrder,
                Resolved,
                _ =>
                    PathExecutableResolver.Resolve(
                        "codex",
                        ToolsFolder,
                        name =>
                            name.EndsWith(".bat", StringComparison.Ordinal)
                            || name.EndsWith(".cmd", StringComparison.Ordinal)
                    ),
                o =>
                    ((string?)o.Value)
                        .Should()
                        .Be(
                            Path.Combine(ToolsFolder, "codex.bat"),
                            "PATHEXT を渡さない多重定義も既定の順"
                        )
            ),
        ];

    /// <summary>PATHEXT の順に試し、.exe / .cmd / .bat 以外の拡張子は試さない</summary>
    private static IEnumerable<ScenarioRow<LaunchCase>> PathExtOrderRows()
    {
        (string Id, string PathExt, string Expected)[] cases =
        [
            ("com-exe-bat-cmd-vbs", ".COM;.EXE;.BAT;.CMD;.VBS", "codex.exe"),
            ("bat-cmd-exe", ".BAT;.CMD;.EXE", "codex.bat"),
            ("cmd-exe-bat", ".CMD;.EXE;.BAT", "codex.cmd"),
            ("spaced-cmd-bat-exe", " .cmd ; .bat ;.exe", "codex.cmd"),
            ("exe-cmd", ".EXE;.CMD", "codex.exe"),
            ("com-ps1", ".COM;.PS1", "codex.exe"),
            ("empty", "", "codex.exe"),
        ];

        return cases.Select(c =>
            Row(
                $"resolve-pathext-order-{c.Id}",
                "PATHEXT の順に試し、.exe / .cmd / .bat 以外の拡張子は試さない",
                PathExecutableResolve,
                PathExtOrder,
                Resolved,
                _ =>
                    ResolveInTools(
                        c.PathExt,
                        "codex.exe",
                        "codex.cmd",
                        "codex.bat",
                        "codex.com",
                        "codex.ps1"
                    ),
                o => ((string?)o.Value).Should().Be(Path.Combine(ToolsFolder, c.Expected))
            )
        );
    }

    /// <summary>PATHEXT に無い候補は、ファイルがあっても選ばない</summary>
    private static IEnumerable<ScenarioRow<LaunchCase>> MissingFromPathExtRows()
    {
        (string Id, string File)[] cases = [("bat", "codex.bat"), ("com", "codex.com")];

        return cases.Select(c =>
            Row(
                $"resolve-pathext-missing-{c.Id}-is-not-tried",
                "PATHEXT に無い候補は、ファイルがあっても選ばない",
                PathExecutableResolve,
                ExtensionOutsidePathExt,
                NotFound,
                _ => ResolveInTools(".EXE;.CMD", c.File),
                o => o.Value.Should().BeNull()
            )
        );
    }
}
