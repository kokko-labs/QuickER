using System.ComponentModel;
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

/// <summary>起動の表：.cmd / .bat シムのガード（QuickER.Platform の <c>BatchShimProcessGuard</c> と、QuickER.AI の配線）</summary>
internal static partial class ProcessLaunchRows
{
    /// <summary>Guard を直接呼ぶ行の文言の書式（resx に依らない自前の書式）</summary>
    private const string PathQuoteFormat = "path has quote: {0}";

    private const string PathPercentFormat = "path has percent: {0}";
    private const string MetaCharFormat = "arg has meta '{0}': {1}";
    private const string EnvExpansionFormat = "arg has percent: {0}";
    private const string NewlineFormat = "arg has newline: {0}";

    /// <summary>Codex の app-server の起動引数（元のテストの定数）</summary>
    private const string CodexArguments = "app-server --listen stdio://";

    /// <summary>包み直しの起動先として期待する cmd.exe のフルパス（製品のプロパティを参照せず独立に組み立てる）</summary>
    private static string SystemCmdExe => Path.Combine(Environment.SystemDirectory, "cmd.exe");

    private static (string FileName, string Arguments) Wrap(string path, string arguments) =>
        BatchShimProcessGuard.WrapCommandLine(
            path,
            arguments,
            PathQuoteFormat,
            PathPercentFormat,
            MetaCharFormat,
            NewlineFormat
        );

    /// <summary>ArgumentList 形式の起動情報を作る</summary>
    private static ProcessStartInfo Info(string fileName, params string[] arguments)
    {
        var startInfo = new ProcessStartInfo { FileName = fileName };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        return startInfo;
    }

    /// <summary>自前の書式で <c>Apply</c> を呼び、書き換えた起動情報を返す</summary>
    private static ProcessStartInfo Apply(ProcessStartInfo startInfo)
    {
        BatchShimProcessGuard.Apply(
            startInfo,
            PathQuoteFormat,
            PathPercentFormat,
            EnvExpansionFormat,
            NewlineFormat
        );
        return startInfo;
    }

    private static ProcessStartInfo Started(LaunchObservation o) => (ProcessStartInfo)o.Value!;

    private static (string FileName, string Arguments) Pair(LaunchObservation o) =>
        ((string, string))o.Value!;

    private static IEnumerable<ScenarioRow<LaunchCase>> ShimRows() =>
        [
            .. IsBatchShimRows(),
            .. WrapCommandLineRows(),
            .. WrapMetaRows(),
            .. ApplyRows(),
            .. ApplyMetaRows(),
            .. ApplyNewlineRows(),
            .. ClaudeRows(),
            .. CodexRows(),
            .. CodexMetaRows(),
            .. CopilotRows(),
        ];

    private static IEnumerable<ScenarioRow<LaunchCase>> IsBatchShimRows()
    {
        (string Id, string? Path, bool Expected, LaunchShape Shape)[] cases =
        [
            ("cmd", @"C:\tools\codex.cmd", true, ShimTarget),
            ("bat-upper", @"C:\tools\codex.BAT", true, ShimSpelling),
            ("exe", @"C:\tools\codex.exe", false, ExeTarget),
            ("cmd-trailing-space", @"C:\tools\codex.cmd ", true, ShimSpelling),
            ("cmd-trailing-dot", @"C:\tools\codex.cmd.", true, ShimSpelling),
            ("bat-trailing-dot-space-dot", @"C:\tools\codex.bat. .", true, ShimSpelling),
            ("exe-trailing-space", @"C:\tools\codex.exe ", false, ShimSpelling),
            ("unix-path", "/usr/local/bin/codex", false, NonWindowsOrNullPath),
            ("null", null, false, NonWindowsOrNullPath),
        ];

        return cases.Select(c =>
            Row(
                $"shim-is-batch-shim-{c.Id}",
                "IsBatchShim_拡張子でシムを判定する",
                ShimIsBatchShim,
                c.Shape,
                Classified,
                _ => BatchShimProcessGuard.IsBatchShim(c.Path),
                o => ((bool)o.Value!).Should().Be(c.Expected)
            )
        );
    }

    /// <summary><c>WrapCommandLine</c>（引数が 1 本の文字列）のうち、メタ文字の行以外</summary>
    private static IEnumerable<ScenarioRow<LaunchCase>> WrapCommandLineRows()
    {
        const string quotedPath = @"C:\tools\evil"" & calc & ""x.cmd";
        const string percentPath = @"C:\%EVIL%\codex.cmd";

        return
        [
            Row(
                "shim-wrap-exe-passes-through",
                "WrapCommandLine_シムでなければ素通しする",
                ShimWrapCommandLine,
                ExeTarget,
                PassThrough,
                _ => Wrap(@"C:\tools\codex.exe", "app-server"),
                o =>
                {
                    var (fileName, arguments) = Pair(o);
                    fileName.Should().Be(@"C:\tools\codex.exe");
                    arguments.Should().Be("app-server");
                }
            ),
            Row(
                "shim-wrap-shim-wrapped-in-quoted-form",
                "WrapCommandLine_シムは決定的な引用形式でラップする",
                ShimWrapCommandLine,
                ShimTarget,
                Wrapped,
                _ => Wrap(@"C:\Program Files\nodejs\codex.cmd", "app-server --listen stdio://"),
                o =>
                {
                    var (fileName, arguments) = Pair(o);
                    fileName.Should().Be(SystemCmdExe);
                    arguments
                        .Should()
                        .Be(
                            "/d /s /v:off /c \"\"C:\\Program Files\\nodejs\\codex.cmd\" app-server --listen stdio://\""
                        );
                }
            ),
            Row(
                "shim-wrap-rejects-path-quote",
                "WrapCommandLine_引用符を含むパスは拒否する",
                ShimWrapCommandLine,
                PathQuote,
                Rejected,
                _ => Wrap(quotedPath, "app-server"),
                o => o.ShouldHaveThrown<InvalidOperationException>($"path has quote: {quotedPath}")
            ),
            // パスの % は cmd が環境変数として展開し、起動する実体が別物に化け得るため拒否する
            Row(
                "shim-wrap-rejects-path-percent",
                "WrapCommandLine_パーセントを含むシムのパスは拒否する",
                ShimWrapCommandLine,
                PathPercent,
                Rejected,
                _ => Wrap(percentPath, "app-server"),
                o =>
                    o.ShouldHaveThrown<InvalidOperationException>(
                        $"path has percent: {percentPath}"
                    )
            ),
            // シムでなければ cmd を経由しないため、パスに % があっても素通しする
            Row(
                "shim-wrap-exe-percent-path-passes-through",
                "WrapCommandLine_パーセントを含むパスでもexeなら素通しする",
                ShimWrapCommandLine,
                ExePathWithShimMeta,
                PassThrough,
                _ => Wrap(@"C:\100%\codex.exe", "app-server"),
                o => Pair(o).FileName.Should().Be(@"C:\100%\codex.exe")
            ),
            // 改行を含む引数はこちらの入口でも拒否する（メタ文字より先に見るため専用の文言が出る）
            Row(
                "shim-wrap-rejects-arg-newline",
                "WrapCommandLine_改行を含む引数は拒否する",
                ShimWrapCommandLine,
                ArgNewline,
                Rejected,
                _ => Wrap(@"C:\tools\codex.cmd", "app-server\r\n--listen stdio://"),
                o => o.ShouldHaveThrown<InvalidOperationException>("arg has newline: *")
            ),
            Row(
                "shim-wrap-rejects-path-newline",
                "WrapCommandLine_改行を含むパスは拒否する",
                ShimWrapCommandLine,
                PathNewline,
                Rejected,
                _ => Wrap("C:\\tools\\co\r\ndex.cmd", "app-server"),
                o => o.ShouldHaveThrown<InvalidOperationException>("arg has newline: *")
            ),
        ];
    }

    private static IEnumerable<ScenarioRow<LaunchCase>> WrapMetaRows()
    {
        (string Id, string Arguments)[] cases =
        [
            ("ampersand", "app-server & calc"),
            ("pipe", "app-server | calc"),
            ("redirect", "app-server > out.txt"),
            ("percent", "app-server %TEMP%"),
            ("quote", "app-server \"quoted\""),
        ];

        return cases.Select(c =>
            Row(
                $"shim-wrap-rejects-arg-meta-{c.Id}",
                "WrapCommandLine_メタ文字を含む引数は拒否する",
                ShimWrapCommandLine,
                c.Id switch
                {
                    "percent" => ArgPercent,
                    "quote" => ArgQuote,
                    _ => ArgCmdMeta,
                },
                Rejected,
                _ => Wrap(@"C:\tools\codex.cmd", c.Arguments),
                o => o.ShouldHaveThrown<InvalidOperationException>("arg has meta *")
            )
        );
    }

    /// <summary><c>Apply</c>（ArgumentList）のうち、メタ文字・改行の行以外</summary>
    private static IEnumerable<ScenarioRow<LaunchCase>> ApplyRows()
    {
        const string quotedPath = @"C:\tools\evil"" & calc & ""x.cmd";
        const string percentPath = @"C:\%EVIL%\claude.cmd";

        return
        [
            Row(
                "shim-apply-exe-untouched",
                "Apply_シムでなければ何もしない",
                ShimApply,
                ExeTarget,
                PassThrough,
                _ => Apply(Info(@"C:\tools\claude.exe", "-p")),
                o =>
                {
                    var startInfo = Started(o);
                    startInfo.FileName.Should().Be(@"C:\tools\claude.exe");
                    startInfo.ArgumentList.Should().Equal("-p");
                    startInfo.Arguments.Should().BeEmpty();
                }
            ),
            Row(
                "shim-apply-shim-wrapped-with-quoted-tokens",
                "Apply_シムは各トークンを引用してCmd経由へ包み直す",
                ShimApply,
                ShimTarget,
                Wrapped,
                _ => Apply(Info(@"C:\npm\claude.cmd", "-p", "--model", "sonnet")),
                o =>
                {
                    var startInfo = Started(o);
                    startInfo.FileName.Should().Be(SystemCmdExe);

                    // ArgumentList と Arguments の併用は Process.Start が拒否するため、必ず片方だけになっている
                    startInfo.ArgumentList.Should().BeEmpty();
                    startInfo
                        .Arguments.Should()
                        .Be(
                            "/d /s /v:off /c \"\"C:\\npm\\claude.cmd\" \"-p\" \"--model\" \"sonnet\"\""
                        );
                }
            ),
            // 引数中の引用符は "" へ倍加され、引用の外へ抜けない
            Row(
                "shim-apply-doubles-arg-quote",
                "Apply_引数の引用符は倍加して閉じ込める",
                ShimApply,
                ArgQuote,
                Wrapped,
                _ => Apply(Info(@"C:\npm\claude.cmd", "say \"hi\"")),
                o =>
                    Started(o)
                        .Arguments.Should()
                        .Be("/d /s /v:off /c \"\"C:\\npm\\claude.cmd\" \"say \"\"hi\"\"\"\"")
            ),
            // 引用符の直前の連続バックスラッシュは C ランタイム規約に合わせて倍にする
            // a \ \ "" b → 引用符の前の \ が 1 本から 2 本へ、引用符自身は "" へ
            Row(
                "shim-apply-doubles-backslash-before-quote",
                "Apply_引用符直前のバックスラッシュを倍にする",
                ShimApply,
                ArgBackslash,
                Wrapped,
                _ => Apply(Info(@"C:\npm\claude.cmd", @"a\""b")),
                o => Started(o).Arguments.Should().Contain(@"""a\\""""b""")
            ),
            // 閉じ引用符の直前の連続バックスラッシュも倍にする（そのままだと閉じ引用符を潰す）
            Row(
                "shim-apply-doubles-trailing-backslash",
                "Apply_末尾のバックスラッシュを倍にする",
                ShimApply,
                ArgBackslash,
                Wrapped,
                _ => Apply(Info(@"C:\npm\claude.cmd", @"C:\dir\")),
                o => Started(o).Arguments.Should().Contain(@"""C:\dir\\""")
            ),
            // 空の引数も 1 トークンとして残る（引用しないと消える）
            Row(
                "shim-apply-keeps-empty-arg",
                "Apply_空の引数も引用して残す",
                ShimApply,
                ArgEmpty,
                Wrapped,
                _ => Apply(Info(@"C:\npm\claude.cmd", "--flag", string.Empty)),
                o => Started(o).Arguments.Should().EndWith("\"--flag\" \"\"\"")
            ),
            // % は引用の内側でも展開され、展開結果が引用符を持ち込めば引用を破れるため拒否する
            Row(
                "shim-apply-rejects-arg-percent",
                "Apply_環境変数展開を含む引数は拒否する",
                ShimApply,
                ArgPercent,
                Rejected,
                _ => Apply(Info(@"C:\npm\claude.cmd", "--model", "%EVIL%")),
                o => o.ShouldHaveThrown<InvalidOperationException>("arg has percent: %EVIL%")
            ),
            Row(
                "shim-apply-rejects-path-quote",
                "Apply_引用符を含むパスは拒否する",
                ShimApply,
                PathQuote,
                Rejected,
                _ => Apply(Info(quotedPath)),
                o => o.ShouldHaveThrown<InvalidOperationException>($"path has quote: {quotedPath}")
            ),
            // パスの % は引数と同じく cmd に展開されるため、こちらの入口でも拒否する
            Row(
                "shim-apply-rejects-path-percent",
                "Apply_パーセントを含むシムのパスは拒否する",
                ShimApply,
                PathPercent,
                Rejected,
                _ => Apply(Info(percentPath, "-p")),
                o =>
                    o.ShouldHaveThrown<InvalidOperationException>(
                        $"path has percent: {percentPath}"
                    )
            ),
            // シムでなければ cmd を経由しないため、パスに % があっても何もしない
            Row(
                "shim-apply-exe-percent-path-untouched",
                "Apply_パーセントを含むパスでもexeなら何もしない",
                ShimApply,
                ExePathWithShimMeta,
                PassThrough,
                _ => Apply(Info(@"C:\100%\claude.exe", "-p")),
                o =>
                {
                    var startInfo = Started(o);
                    startInfo.FileName.Should().Be(@"C:\100%\claude.exe");
                    startInfo.ArgumentList.Should().Equal("-p");
                }
            ),
        ];
    }

    /// <summary>
    /// cmd のメタ文字（引用の内側では素の文字になるもの）は拒否せず、引用して通す。
    /// 拒否側に倒すと、<c>&lt;Routes /&gt;</c> のような記述を含むシステムプロンプトを引数で渡す既存の用途が .cmd 導入の環境で動かなくなる
    /// </summary>
    private static IEnumerable<ScenarioRow<LaunchCase>> ApplyMetaRows()
    {
        (string Id, string Argument)[] cases =
        [
            ("ampersand", "a & b"),
            ("pipe", "a | b"),
            ("angle-brackets", "<Routes />"),
            ("caret", "a ^ b"),
        ];

        return cases.Select(c =>
            Row(
                $"shim-apply-passes-quoted-meta-{c.Id}",
                "Apply_引用の内側で無害になるメタ文字は拒否せず通す",
                ShimApply,
                ArgCmdMeta,
                Wrapped,
                _ => Apply(Info(@"C:\npm\claude.cmd", c.Argument)),
                o => Started(o).Arguments.Should().Contain($"\"{c.Argument}\"")
            )
        );
    }

    /// <summary>
    /// 改行を含む引数は拒否する（引用しても防げないため、黙った切り捨てを明示エラーへ倒す）。
    /// cmd は npm cmd-shim の <c>%*</c> を展開する段階で最初の改行から後ろを捨てる。複数行の内容はファイル経由で渡す
    /// </summary>
    private static IEnumerable<ScenarioRow<LaunchCase>> ApplyNewlineRows()
    {
        (string Id, string Argument)[] cases =
        [
            ("crlf", "line1\r\nline2"),
            ("lf", "line1\nline2"),
            ("cr", "line1\rline2"),
        ];

        return cases
            .Select(c =>
                Row(
                    $"shim-apply-rejects-arg-newline-{c.Id}",
                    "Apply_改行を含む引数は拒否する",
                    ShimApply,
                    ArgNewline,
                    Rejected,
                    _ => Apply(Info(@"C:\npm\claude.cmd", c.Argument)),
                    o => o.ShouldHaveThrown<InvalidOperationException>("arg has newline: *")
                )
            )
            .Append(
                // パス側の改行も拒否する（正規のパスには現れず、あれば切り捨てを招くだけ）
                Row(
                    "shim-apply-rejects-path-newline",
                    "Apply_改行を含むパスは拒否する",
                    ShimApply,
                    PathNewline,
                    Rejected,
                    _ => Apply(Info("C:\\npm\\cl\r\naude.cmd")),
                    o => o.ShouldHaveThrown<InvalidOperationException>("arg has newline: *")
                )
            );
    }

    /// <summary>Claude の起動が、ガードへ claude の文言で配線されていること（<c>ApplyBatchShimGuard</c>）</summary>
    private static IEnumerable<ScenarioRow<LaunchCase>> ClaudeRows()
    {
        const string quotedPath = @"C:\tools\evil"" & calc & ""x.cmd";
        const string percentPath = @"C:\%EVIL%\claude.cmd";
        const string newlineArgument = "line1\r\nline2";

        static ProcessStartInfo Guard(ProcessStartInfo startInfo)
        {
            ClaudeCodeProcessClient.ApplyBatchShimGuard(startInfo);
            return startInfo;
        }

        return
        [
            Row(
                "shim-claude-exe-untouched",
                "ApplyBatchShimGuard_exeは書き換えない",
                ClaudeApplyBatchShimGuard,
                ExeTarget,
                PassThrough,
                _ => Guard(Info(@"C:\tools\claude.exe", "-p")),
                o =>
                {
                    var startInfo = Started(o);
                    startInfo.FileName.Should().Be(@"C:\tools\claude.exe");
                    startInfo.ArgumentList.Should().Equal("-p");
                }
            ),
            Row(
                "shim-claude-cmd-wrapped-through-cmd-exe",
                "ApplyBatchShimGuard_cmdシムはCmdExe経由へ包み直す",
                ClaudeApplyBatchShimGuard,
                ShimTarget,
                Wrapped,
                _ => Guard(Info(@"C:\npm\claude.cmd", "-p")),
                o =>
                {
                    var startInfo = Started(o);
                    startInfo.FileName.Should().Be(SystemCmdExe);
                    startInfo.ArgumentList.Should().BeEmpty();
                    startInfo.Arguments.Should().Contain(@"""C:\npm\claude.cmd""");
                }
            ),
            // 製品コードと同じ resx キーからフォーマット済みメッセージを導出し、カルチャに依らず完全一致で検証する
            Row(
                "shim-claude-rejects-path-quote-with-claude-text",
                "ApplyBatchShimGuard_引用符を含むパスはClaudeの文言で拒否する",
                ClaudeApplyBatchShimGuard,
                PathQuote,
                Rejected,
                _ => Guard(Info(quotedPath)),
                o =>
                    o.ShouldHaveThrown<InvalidOperationException>(
                        string.Format(AiStrings.ClaudeCode_PathHasQuote, quotedPath)
                    )
            ),
            Row(
                "shim-claude-rejects-path-percent-with-claude-text",
                "ApplyBatchShimGuard_パーセントを含むシムのパスはClaudeの文言で拒否する",
                ClaudeApplyBatchShimGuard,
                PathPercent,
                Rejected,
                _ => Guard(Info(percentPath)),
                o =>
                    o.ShouldHaveThrown<InvalidOperationException>(
                        string.Format(AiStrings.ClaudeCode_PathHasEnvExpansion, percentPath)
                    )
            ),
            Row(
                "shim-claude-rejects-arg-percent-with-claude-text",
                "ApplyBatchShimGuard_環境変数展開を含む引数はClaudeの文言で拒否する",
                ClaudeApplyBatchShimGuard,
                ArgPercent,
                Rejected,
                _ => Guard(Info(@"C:\npm\claude.cmd", "%EVIL%")),
                o =>
                    o.ShouldHaveThrown<InvalidOperationException>(
                        string.Format(AiStrings.ClaudeCode_ArgHasEnvExpansion, "%EVIL%")
                    )
            ),
            Row(
                "shim-claude-rejects-arg-newline-with-claude-text",
                "ApplyBatchShimGuard_改行を含む引数はClaudeの文言で拒否する",
                ClaudeApplyBatchShimGuard,
                ArgNewline,
                Rejected,
                _ => Guard(Info(@"C:\npm\claude.cmd", newlineArgument)),
                o =>
                    o.ShouldHaveThrown<InvalidOperationException>(
                        string.Format(AiStrings.ClaudeCode_ArgHasNewline, newlineArgument)
                    )
            ),
        ];
    }

    /// <summary>Codex の起動が、ガードへ codex の文言で配線されていること（<c>ResolveLaunch</c>・<c>ResolveStartInfo</c>）</summary>
    private static IEnumerable<ScenarioRow<LaunchCase>> CodexRows()
    {
        const string quotedPath = @"C:\tools\evil"" & calc & ""x.cmd";
        const string percentPath = @"C:\%EVIL%\codex.cmd";

        return
        [
            // 名前だけ（"codex"）へ戻さない＝探索を Process.Start に委ねない（防御の二重化）
            Row(
                "shim-codex-launch-exe-keeps-full-path",
                "ResolveLaunch_解決済みのexeはフルパスのまま起動に使う",
                CodexResolveLaunch,
                ExeTarget,
                PassThrough,
                _ => CodexAppServerClient.ResolveLaunch(@"C:\tools\codex.exe"),
                o =>
                {
                    var (fileName, arguments) = Pair(o);
                    fileName.Should().Be(@"C:\tools\codex.exe");
                    arguments.Should().Be(CodexArguments);
                }
            ),
            Row(
                "shim-codex-launch-cmd-wrapped-through-cmd-exe",
                "ResolveLaunch_cmdシムはシステムフォルダのcmdをフルパスで起動する",
                CodexResolveLaunch,
                ShimTarget,
                Wrapped,
                _ => CodexAppServerClient.ResolveLaunch(@"C:\npm\codex.cmd"),
                o =>
                {
                    var (fileName, arguments) = Pair(o);
                    fileName.Should().Be(SystemCmdExe);
                    arguments
                        .Should()
                        .Be($"/d /s /v:off /c \"\"C:\\npm\\codex.cmd\" {CodexArguments}\"");
                }
            ),
            Row(
                "shim-codex-launch-missing-fails-with-win32",
                "ResolveLaunch_未検出なら名前だけの起動と同じWin32Exceptionで失敗する",
                CodexResolveLaunch,
                NonWindowsOrNullPath,
                Rejected,
                _ => CodexAppServerClient.ResolveLaunch(null),
                o =>
                {
                    o.ShouldHaveThrown<Win32Exception>();
                    ((Win32Exception)o.Thrown!).NativeErrorCode.Should().Be(2);
                }
            ),
            Row(
                "shim-codex-start-exe-launched-directly",
                "ResolveStartInfo_exeは直接起動する",
                CodexResolveStartInfo,
                ExeTarget,
                PassThrough,
                _ => CodexAppServerClient.ResolveStartInfo(@"C:\tools\codex.exe", CodexArguments),
                o =>
                {
                    var (fileName, arguments) = Pair(o);
                    fileName.Should().Be(@"C:\tools\codex.exe");
                    arguments.Should().Be(CodexArguments);
                }
            ),
            Row(
                "shim-codex-start-cmd-wrapped-in-quoted-form",
                "ResolveStartInfo_cmdシムは決定的な引用形式でラップする",
                CodexResolveStartInfo,
                ShimTarget,
                Wrapped,
                _ =>
                    CodexAppServerClient.ResolveStartInfo(
                        @"C:\Program Files\nodejs\codex.cmd",
                        CodexArguments
                    ),
                o =>
                {
                    var (fileName, arguments) = Pair(o);
                    fileName.Should().Be(SystemCmdExe);
                    arguments
                        .Should()
                        .Be(
                            $"/d /s /v:off /c \"\"C:\\Program Files\\nodejs\\codex.cmd\" {CodexArguments}\""
                        );
                }
            ),
            // 製品コードと同じ resx キーからフォーマット済みメッセージを導出し、カルチャに依らず完全一致で検証する
            Row(
                "shim-codex-start-rejects-path-quote-with-codex-text",
                "ResolveStartInfo_引用符を含むパスは起動前に拒否する",
                CodexResolveStartInfo,
                PathQuote,
                Rejected,
                _ => CodexAppServerClient.ResolveStartInfo(quotedPath, CodexArguments),
                o =>
                    o.ShouldHaveThrown<InvalidOperationException>(
                        string.Format(AiStrings.Codex_PathHasQuote, quotedPath)
                    )
            ),
            Row(
                "shim-codex-start-rejects-path-percent-with-codex-text",
                "ResolveStartInfo_パーセントを含むシムのパスはCodexの文言で拒否する",
                CodexResolveStartInfo,
                PathPercent,
                Rejected,
                _ => CodexAppServerClient.ResolveStartInfo(percentPath, CodexArguments),
                o =>
                    o.ShouldHaveThrown<InvalidOperationException>(
                        string.Format(AiStrings.Codex_PathHasEnvExpansion, percentPath)
                    )
            ),
            // .exe は cmd を経由しないためガード対象外（ProcessStartInfo が安全に扱う）
            Row(
                "shim-codex-start-exe-meta-path-passes-through",
                "ResolveStartInfo_メタ文字を含むパスでもexeなら素通しする",
                CodexResolveStartInfo,
                ExePathWithShimMeta,
                PassThrough,
                _ =>
                    CodexAppServerClient.ResolveStartInfo(
                        @"C:\tools\a & b\codex.exe",
                        CodexArguments
                    ),
                o => Pair(o).FileName.Should().Be(@"C:\tools\a & b\codex.exe")
            ),
        ];
    }

    /// <summary>
    /// Codex の cmd メタ文字を含む引数の拒否。文言は <c>Codex_ArgHasCmdMeta</c> を、製品が書式へ渡す
    /// 検出文字（<c>BatchShimProcessGuard.WrapCommandLine</c> が引数の先頭側から見つけたメタ文字）と引数全体で組んだ完全一致
    /// </summary>
    private static IEnumerable<ScenarioRow<LaunchCase>> CodexMetaRows()
    {
        (string Id, string Arguments, char Detected, LaunchShape Shape)[] cases =
        [
            ("ampersand", "app-server & calc", '&', ArgCmdMeta),
            ("pipe", "app-server | calc", '|', ArgCmdMeta),
            ("redirect-out", "app-server > out.txt", '>', ArgCmdMeta),
            ("redirect-in", "app-server < in.txt", '<', ArgCmdMeta),
            ("caret", "app-server ^& calc", '^', ArgCmdMeta),
            ("percent", "app-server %TEMP%", '%', ArgPercent),
            ("quote", "app-server \"quoted\"", '"', ArgQuote),
        ];

        return cases.Select(c =>
            Row(
                $"shim-codex-start-rejects-arg-meta-{c.Id}",
                "ResolveStartInfo_cmdメタ文字を含む引数は起動前に拒否する",
                CodexResolveStartInfo,
                c.Shape,
                Rejected,
                _ => CodexAppServerClient.ResolveStartInfo(@"C:\tools\codex.cmd", c.Arguments),
                o =>
                    o.ShouldHaveThrown<InvalidOperationException>(
                        string.Format(AiStrings.Codex_ArgHasCmdMeta, c.Detected, c.Arguments)
                    )
            )
        );
    }

    /// <summary>
    /// Copilot の起動直前の関門（<c>CopilotCliLocator.EnsureSafeToLaunch</c>）。
    /// copilot は SDK の内側で起動されるため QuickER が引数を引用し直せず、掛けられる防御はここだけ
    /// </summary>
    private static IEnumerable<ScenarioRow<LaunchCase>> CopilotRows()
    {
        const string quotedPath = @"C:\tools\evil"" & calc & ""x.cmd";
        const string percentPath = @"C:\%EVIL%\copilot.cmd";
        const string exclamationPath = @"C:\!EVIL!\copilot.cmd";

        // 改行を含む cmd シムのパスは起動前に拒否する（cmd は改行から後ろを捨てる）
        (string Id, string Path)[] newlinePaths =
        [
            ("lf", "C:\\npm\nx\\copilot.cmd"),
            ("cr", "C:\\npm\rx\\copilot.cmd"),
        ];

        // 正当なパス（シム・実行ファイルとも）はそのまま通る。実行ファイルは cmd を経由しないため % があっても通す
        (string Id, string Path, LaunchShape Shape)[] safePaths =
        [
            ("cmd", @"C:\npm\copilot.cmd", ShimTarget),
            ("exe-program-files", @"C:\Program Files\GitHub\copilot.exe", ExeTarget),
            ("exe-percent", @"C:\100%\copilot.exe", ExePathWithShimMeta),
            ("exe-exclamation", @"C:\Hi!\copilot.exe", ExePathWithShimMeta),
        ];

        return new[]
        {
            Row(
                "shim-copilot-rejects-path-quote",
                "引用符を含む cmd シムのパスは起動前に拒否する",
                CopilotEnsureSafeToLaunch,
                PathQuote,
                Rejected,
                _ => CopilotCliLocator.EnsureSafeToLaunch(quotedPath),
                o =>
                    o.ShouldHaveThrown<InvalidOperationException>(
                        string.Format(AiStrings.Copilot_PathHasQuote, quotedPath)
                    )
            ),
            Row(
                "shim-copilot-rejects-path-percent",
                "% を含む cmd シムのパスは起動前に拒否する",
                CopilotEnsureSafeToLaunch,
                PathPercent,
                Rejected,
                _ => CopilotCliLocator.EnsureSafeToLaunch(percentPath),
                o =>
                    o.ShouldHaveThrown<InvalidOperationException>(
                        string.Format(AiStrings.Copilot_PathHasEnvExpansion, percentPath)
                    )
            ),
            // codex / claude は自分で /v:off を付けて包み直すが、copilot は SDK が cmd を起動するため付けられない
            Row(
                "shim-copilot-rejects-path-exclamation",
                "! を含む cmd シムのパスは起動前に拒否する",
                CopilotEnsureSafeToLaunch,
                PathExclamation,
                Rejected,
                _ => CopilotCliLocator.EnsureSafeToLaunch(exclamationPath),
                o =>
                    o.ShouldHaveThrown<InvalidOperationException>(
                        string.Format(AiStrings.Copilot_PathHasDelayedExpansion, exclamationPath)
                    )
            ),
        }.Concat(newlinePaths.Select(c => Row($"shim-copilot-rejects-path-newline-{c.Id}", "改行を含む cmd シムのパスは起動前に拒否する", CopilotEnsureSafeToLaunch, PathNewline, Rejected, _ => CopilotCliLocator.EnsureSafeToLaunch(c.Path), o => o.ShouldHaveThrown<InvalidOperationException>(string.Format(AiStrings.Copilot_PathHasNewline, c.Path))))).Concat(safePaths.Select(c => Row($"shim-copilot-safe-path-{c.Id}", "EnsureSafeToLaunch_SafePath_PassesThrough", CopilotEnsureSafeToLaunch, c.Shape, PassThrough, _ => CopilotCliLocator.EnsureSafeToLaunch(c.Path), o => ((string)o.Value!).Should().Be(c.Path))));
    }
}
