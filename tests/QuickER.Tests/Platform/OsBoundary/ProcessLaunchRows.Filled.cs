using System.Diagnostics;
using System.IO;
using AwesomeAssertions;
using QuickER.AI;
using QuickER.AI.UI;
using QuickER.Tests.TestSupport.Engines;
using static QuickER.Tests.Platform.OsBoundary.LaunchEntrance;
using static QuickER.Tests.Platform.OsBoundary.LaunchOutcome;
using static QuickER.Tests.Platform.OsBoundary.LaunchShape;

namespace QuickER.Tests.Platform.OsBoundary;

/// <summary>
/// 起動の表：網羅の計数で見えた穴を埋める行（元の単発のテストを持たない行）
/// </summary>
/// <remarks>
/// 期待はどれも製品の XmlDoc・コメントから導いたもので、行ごとに根拠を書く。
/// QuickER.Platform の <c>BatchShimProcessGuard</c>（クラスの remarks：Wrap はメタ文字 <c>" &amp; | &lt; &gt; ^ %</c> を拒否・
/// Apply は引用の内側の <c>&amp; | &lt; &gt; ^ ( )</c> を通す・<c>!</c> は <c>/v:off</c> で無効なので引数でもパスでも拒否しない・
/// <c>IsBatchShim</c> は末尾の空白と <c>.</c> を落として拡張子を見る）、
/// <c>PathExecutableResolver</c>（相対の PATH 要素は読み飛ばす）、<c>UrlPolicy</c>（ユーザー情報を持つ URL は断る）。
/// </remarks>
internal static partial class ProcessLaunchRows
{
    private static IEnumerable<ScenarioRow<LaunchCase>> FilledRows() =>
        [
            // Wrap が拒否するメタ文字の集合のうち、元の行が持たなかった < と ^
            .. new (string Id, string Arguments, char Detected)[]
            {
                ("redirect-in", "app-server < in.txt", '<'),
                ("caret", "app-server ^ calc", '^'),
            }.Select(c =>
                Row(
                    $"shim-wrap-rejects-arg-meta-{c.Id}",
                    "WrapCommandLine はメタ文字（< ^）を含む引数を拒否する",
                    ShimWrapCommandLine,
                    ArgCmdMeta,
                    Rejected,
                    _ => Wrap(@"C:\tools\codex.cmd", c.Arguments),
                    o =>
                        o.ShouldHaveThrown<InvalidOperationException>(
                            string.Format(MetaCharFormat, c.Detected, c.Arguments)
                        )
                )
            ),
            // ! はメタ文字の集合に無く、包み直しの /v:off で遅延展開が無効なので拒否しない
            Row(
                "shim-wrap-passes-arg-exclamation",
                "WrapCommandLine は ! を含む引数を拒否せず包み直す",
                ShimWrapCommandLine,
                ArgExclamation,
                Wrapped,
                _ => Wrap(@"C:\tools\codex.cmd", "app-server !x!"),
                o =>
                {
                    var (fileName, arguments) = Pair(o);
                    fileName.Should().Be(SystemCmdExe);
                    arguments
                        .Should()
                        .Be("/d /s /v:off /c \"\"C:\\tools\\codex.cmd\" app-server !x!\"");
                }
            ),
            // パスの ! も /v:off で無効なので拒否しない（パスで拒否するのは " と % と改行だけ）
            Row(
                "shim-wrap-path-exclamation-is-wrapped",
                "WrapCommandLine はパスの ! を拒否せず包み直す",
                ShimWrapCommandLine,
                PathExclamation,
                Wrapped,
                _ => Wrap(@"C:\!EVIL!\codex.cmd", "app-server"),
                o =>
                {
                    var (fileName, arguments) = Pair(o);
                    fileName.Should().Be(SystemCmdExe);
                    arguments
                        .Should()
                        .Be("/d /s /v:off /c \"\"C:\\!EVIL!\\codex.cmd\" app-server\"");
                }
            ),
            Row(
                "shim-apply-path-exclamation-is-wrapped",
                "Apply はパスの ! を拒否せず包み直す",
                ShimApply,
                PathExclamation,
                Wrapped,
                _ => Apply(Info(@"C:\!EVIL!\claude.cmd", "-p")),
                o =>
                {
                    Started(o).FileName.Should().Be(SystemCmdExe);
                    Started(o).Arguments.Should().Contain("\"C:\\!EVIL!\\claude.cmd\"");
                }
            ),
            // Apply は引用の内側で無害になる文字を通す（> は元の行の < と同じ扱い）・! は /v:off で無効
            .. new (string Id, string Argument, LaunchShape Shape)[]
            {
                ("redirect-out", "a > b", ArgCmdMeta),
                ("exclamation", "!EVIL!", ArgExclamation),
            }.Select(c =>
                Row(
                    $"shim-apply-passes-quoted-meta-{c.Id}",
                    "Apply は引用の内側で無害になる文字（> !）を拒否せず通す",
                    ShimApply,
                    c.Shape,
                    Wrapped,
                    _ => Apply(Info(@"C:\npm\claude.cmd", c.Argument)),
                    o => Started(o).Arguments.Should().Contain($"\"{c.Argument}\"")
                )
            ),
            // IsBatchShim は末尾の空白と . を落としてから拡張子を見るので、綴りの揺れたシムも包み直す
            Row(
                "shim-wrap-trailing-dot-shim-is-wrapped",
                "WrapCommandLine は末尾にドットのあるシムも包み直す",
                ShimWrapCommandLine,
                ShimSpelling,
                Wrapped,
                _ => Wrap(@"C:\tools\codex.cmd.", "app-server"),
                o => Pair(o).FileName.Should().Be(SystemCmdExe)
            ),
            Row(
                "shim-apply-trailing-space-shim-is-wrapped",
                "Apply は末尾に空白のあるシムも包み直す",
                ShimApply,
                ShimSpelling,
                Wrapped,
                _ => Apply(Info(@"C:\npm\claude.cmd ", "-p")),
                o =>
                {
                    Started(o).FileName.Should().Be(SystemCmdExe);
                    Started(o).ArgumentList.Should().BeEmpty();
                }
            ),
            // シムでなければ書き換えない（拡張子が .cmd / .bat でない Unix の形のパス）
            Row(
                "shim-wrap-unix-path-passes-through",
                "WrapCommandLine は Unix の形のパスを素通しする",
                ShimWrapCommandLine,
                NonWindowsOrNullPath,
                PassThrough,
                _ => Wrap("/usr/local/bin/codex", "app-server"),
                o =>
                {
                    var (fileName, arguments) = Pair(o);
                    fileName.Should().Be("/usr/local/bin/codex");
                    arguments.Should().Be("app-server");
                }
            ),
            Row(
                "shim-apply-unix-path-untouched",
                "Apply は Unix の形のパスを書き換えない",
                ShimApply,
                NonWindowsOrNullPath,
                PassThrough,
                _ => Apply(Info("/usr/local/bin/claude", "-p")),
                o =>
                {
                    Started(o).FileName.Should().Be("/usr/local/bin/claude");
                    Started(o).ArgumentList.Should().Equal("-p");
                    Started(o).Arguments.Should().BeEmpty();
                }
            ),
            // 共有の走査は相対の PATH 要素を読み飛ばす（codex の行と同じ入力の形を copilot で）
            Row(
                "resolve-copilot-skips-relative-entries",
                "copilot の PATH の相対要素は読み飛ばしてフルパスだけを返す",
                CopilotLocate,
                RelativeEntries,
                Resolved,
                _ =>
                {
                    var bin = FakeBin("copilot");
                    return CopilotCliLocator.ResolveExecutablePath(
                        string.Join(Path.PathSeparator, ".", "bin", bin),
                        candidate =>
                            !Path.IsPathFullyQualified(candidate)
                            || candidate == Path.Combine(bin, Primary("copilot"))
                    );
                },
                o =>
                    ((string?)o.Value)
                        .Should()
                        .Be(Path.Combine(FakeBin("copilot"), Primary("copilot")))
            ),
            // ShellChatLinkOpener は UrlPolicy.IsSafe でもう一度確かめる＝ユーザー情報を持つ URL は渡さない
            Row(
                "external-chat-link-does-not-launch-user-info",
                "ShellChatLinkOpener はユーザー情報を持つ URL をシェルへ渡さない",
                ShellChatLinkOpen,
                UserInfoUrl,
                NotLaunched,
                o =>
                {
                    new ShellChatLinkOpener(o.Dialogs, "Title", o.Launched.Add).Open(
                        new Uri("https://example.com@evil.test/", UriKind.Absolute)
                    );
                    return null;
                },
                o =>
                {
                    o.Launched.Should().BeEmpty();
                    o.Dialogs.ErrorMessages.Should().BeEmpty();
                }
            ),
        ];
}
