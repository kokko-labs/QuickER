using System.Diagnostics;
using System.IO;
using AwesomeAssertions;
using QuickER.AI.UI;
using QuickER.Platform;
using QuickER.Tests.TestSupport;
using QuickER.Tests.TestSupport.Engines;
using static QuickER.Tests.Platform.OsBoundary.LaunchEntrance;
using static QuickER.Tests.Platform.OsBoundary.LaunchOutcome;
using static QuickER.Tests.Platform.OsBoundary.LaunchShape;

namespace QuickER.Tests.Platform.OsBoundary;

/// <summary>
/// 起動の表：外部の起動（QuickER.Platform の <c>ExternalLauncher</c> が起動の前に確かめる規則と、
/// QuickER.AI.UI の <c>ShellChatLinkOpener</c>）。起動のデリゲートを注入するのでプロセスは起動しない
/// </summary>
internal static partial class ProcessLaunchRows
{
    private static IEnumerable<ScenarioRow<LaunchCase>> ExternalRows() =>
        [
            .. OpenUrlRows(),
            .. OpenFolderRows(),
            .. StartExecutableRows(),
            .. CreateProcessRows(),
            .. ShellChatLinkRows(),
        ];

    /// <summary><c>ExternalLauncher.OpenUrl</c>：http / https の絶対 URL でホストを持つもの以外は、起動せずに ArgumentException</summary>
    private static IEnumerable<ScenarioRow<LaunchCase>> OpenUrlRows()
    {
        (string Id, string Url, LaunchShape Shape)[] unsafeUrls =
        [
            ("javascript", "javascript:alert(1)", UnsafeScheme),
            ("file", "file:///C:/Windows/System32/calc.exe", UnsafeScheme),
            ("unc-backslash", @"\\server\share\x.exe", UncUrl),
            ("unc-slash", "//server/share/x.exe", UncUrl),
            ("ms-settings", "ms-settings:display", UnsafeScheme),
            ("mailto", "mailto:someone@example.com", UnsafeScheme),
            ("ftp", "ftp://example.com/file", UnsafeScheme),
            ("user-info", "https://example.com@evil.test/", UserInfoUrl),
        ];

        string[] safeUrls =
        [
            "https://github.com/kokko-labs/QuickER",
            "http://localhost:8080/path?q=1",
        ];

        return unsafeUrls
            .Select(c =>
                Row(
                    $"external-open-url-rejects-{c.Id}",
                    "OpenUrl: http / https の絶対 URL でホストを持つもの以外は、起動せずに ArgumentException",
                    ExternalOpenUrl,
                    c.Shape,
                    Rejected,
                    o =>
                    {
                        var uri = new Uri(c.Url, UriKind.Absolute);
                        ExternalLauncher.OpenUrl(uri, o.Started.Add);
                        return null;
                    },
                    o =>
                    {
                        o.ShouldHaveThrown<ArgumentException>();
                        o.Started.Should().BeEmpty("開いてはいけない URL はシェルへ渡さない");
                    }
                )
            )
            .Concat(
                safeUrls.Select(url =>
                    Row(
                        $"external-open-url-starts-through-shell-{(url.StartsWith("https", StringComparison.Ordinal) ? "https" : "http")}",
                        "OpenUrl: http / https の URL はシェル経由で開く",
                        ExternalOpenUrl,
                        SafeUrl,
                        Launched,
                        o =>
                        {
                            ExternalLauncher.OpenUrl(new Uri(url), o.Started.Add);
                            return null;
                        },
                        o =>
                        {
                            o.Started.Should().ContainSingle();
                            o.Started[0].UseShellExecute.Should().BeTrue();
                            o.Started[0].FileName.Should().Be(new Uri(url).AbsoluteUri);
                        }
                    )
                )
            );
    }

    /// <summary><c>ExternalLauncher.OpenFolder</c></summary>
    private static IEnumerable<ScenarioRow<LaunchCase>> OpenFolderRows()
    {
        (string Id, LaunchShape Shape, Func<string> Path)[] rejected =
        [
            ("relative", RelativePath, () => "relative\\folder"),
            (
                "missing",
                MissingFolder,
                () => Path.Combine(TestTempFolder.Root, "missing-" + Guid.NewGuid().ToString("N"))
            ),
        ];

        return rejected
            .Select(c =>
                Row(
                    $"external-open-folder-rejects-{c.Id}",
                    "OpenFolder: フルパスでない・無いフォルダは、起動せずに ArgumentException",
                    ExternalOpenFolder,
                    c.Shape,
                    Rejected,
                    o =>
                    {
                        ExternalLauncher.OpenFolder(c.Path(), o.Started.Add);
                        return null;
                    },
                    o =>
                    {
                        o.ShouldHaveThrown<ArgumentException>();
                        o.Started.Should().BeEmpty();
                    }
                )
            )
            .Append(
                Row(
                    "external-open-folder-existing-full-path-starts-through-shell",
                    "OpenFolder: 在るフォルダはフルパスでシェル経由で開く",
                    ExternalOpenFolder,
                    ExistingFolder,
                    Launched,
                    o =>
                    {
                        ExternalLauncher.OpenFolder(Path.GetTempPath(), o.Started.Add);
                        return null;
                    },
                    o =>
                        o.Started.Should()
                            .ContainSingle()
                            .Which.FileName.Should()
                            .Be(Path.GetTempPath())
                )
            );
    }

    /// <summary><c>ExternalLauncher.StartExecutable</c></summary>
    private static IEnumerable<ScenarioRow<LaunchCase>> StartExecutableRows() =>
        [
            Row(
                "external-start-executable-rejects-relative-path",
                "StartExecutable: フルパスでない実行ファイルは、起動せずに ArgumentException",
                ExternalStartExecutable,
                RelativePath,
                Rejected,
                o =>
                {
                    ExternalLauncher.StartExecutable("QuickER.exe", o.Started.Add);
                    return null;
                },
                o =>
                {
                    o.ShouldHaveThrown<ArgumentException>();
                    o.Started.Should().BeEmpty();
                }
            ),
        ];

    /// <summary><c>ExternalLauncher.CreateProcess</c>（<c>Process</c> を作るだけで Start しない）</summary>
    private static IEnumerable<ScenarioRow<LaunchCase>> CreateProcessRows()
    {
        (string Id, string FileName, LaunchShape Shape)[] bareNames =
        [
            ("cmd-exe", "cmd.exe", BareName),
            ("claude", "claude", BareName),
            ("relative-path", "tools\\codex.exe", RelativePath),
        ];

        (string Id, string FileName)[] unwrappedShims =
        [
            ("cmd", @"C:\npm\claude.cmd"),
            ("bat-upper", @"C:\npm\codex.BAT"),
        ];

        // 起動の前に確かめる規則だけを見る（Start はしない）
        static Process CreateProcess(ProcessStartInfo startInfo) =>
            ExternalLauncher.CreateProcess(startInfo);

        return bareNames
            .Select(c =>
                Row(
                    $"external-create-process-rejects-bare-name-{c.Id}",
                    "CreateProcess: 名前だけの実行ファイルは断る（dotnet を除く）",
                    ExternalCreateProcess,
                    c.Shape,
                    Rejected,
                    _ =>
                    {
                        using var process = CreateProcess(
                            new ProcessStartInfo { FileName = c.FileName }
                        );
                        return null;
                    },
                    o => o.ShouldHaveThrown<ArgumentException>()
                )
            )
            .Append(
                Row(
                    "external-create-process-dotnet-allowed-by-name",
                    "CreateProcess: dotnet だけは名前のまま起動してよい（.cmd を拾わないため）",
                    ExternalCreateProcess,
                    AllowedBareName,
                    Launched,
                    o =>
                    {
                        using var process = CreateProcess(
                            new ProcessStartInfo { FileName = "dotnet" }
                        );
                        o.Started.Add(process.StartInfo);
                        return (process.StartInfo.FileName, process.EnableRaisingEvents);
                    },
                    o =>
                    {
                        var (fileName, enableRaisingEvents) = ((string, bool))o.Value!;
                        fileName.Should().Be("dotnet");
                        enableRaisingEvents.Should().BeTrue();
                        ExternalLauncher.BareExecutableNames.Should().BeEquivalentTo(["dotnet"]);
                    }
                )
            )
            .Concat(
                unwrappedShims.Select(c =>
                    Row(
                        $"external-create-process-rejects-unwrapped-shim-{c.Id}",
                        "CreateProcess: 包み直していない .cmd / .bat のシムは断る",
                        ExternalCreateProcess,
                        UnwrappedShim,
                        Rejected,
                        _ =>
                        {
                            using var process = CreateProcess(
                                new ProcessStartInfo { FileName = c.FileName }
                            );
                            return null;
                        },
                        o => o.ShouldHaveThrown<ArgumentException>("*BatchShimProcessGuard*")
                    )
                )
            )
            .Concat([
                Row(
                    "external-create-process-wrapped-shim-is-returned",
                    "CreateProcess: BatchShimProcessGuard で包み直したシムは起動できる形で返す",
                    ExternalCreateProcess,
                    WrappedShim,
                    Launched,
                    o =>
                    {
                        var startInfo = new ProcessStartInfo { FileName = @"C:\npm\claude.cmd" };
                        startInfo.ArgumentList.Add("-p");
                        BatchShimProcessGuard.Apply(startInfo, "{0}", "{0}", "{0}", "{0}");

                        using var process = CreateProcess(startInfo);
                        o.Started.Add(process.StartInfo);
                        return process.StartInfo.FileName;
                    },
                    o => ((string)o.Value!).Should().Be(BatchShimProcessGuard.CmdExePath)
                ),
                Row(
                    "external-create-process-rejects-shell-execute",
                    "CreateProcess: シェル経由の起動は断る",
                    ExternalCreateProcess,
                    ShellExecute,
                    Rejected,
                    _ =>
                    {
                        using var process = CreateProcess(
                            new ProcessStartInfo
                            {
                                FileName = BatchShimProcessGuard.CmdExePath,
                                UseShellExecute = true,
                            }
                        );
                        return null;
                    },
                    o => o.ShouldHaveThrown<ArgumentException>()
                ),
            ]);
    }

    /// <summary><c>ShellChatLinkOpener</c>：http / https だけをシェルへ渡し、失敗をエラーのモーダルで知らせる</summary>
    private static IEnumerable<ScenarioRow<LaunchCase>> ShellChatLinkRows()
    {
        // 規則を外れた URL は、渡されてもシェルへ渡さない（組み立て側との二重化）
        (string Id, string Url, LaunchShape Shape)[] unsafeUrls =
        [
            ("file", "file:///C:/Windows/notepad.exe", UnsafeScheme),
            ("unc", @"\\server\share\x.exe", UncUrl),
            ("ms-settings", "ms-settings:privacy", UnsafeScheme),
            ("javascript", "javascript:alert(1)", UnsafeScheme),
        ];

        return unsafeUrls
            .Select(c =>
                Row(
                    $"external-chat-link-does-not-launch-{c.Id}",
                    "http / https 以外の URL は渡されてもシェルへ渡さない",
                    ShellChatLinkOpen,
                    c.Shape,
                    NotLaunched,
                    o =>
                    {
                        new ShellChatLinkOpener(o.Dialogs, "Title", o.Launched.Add).Open(
                            new Uri(c.Url, UriKind.Absolute)
                        );
                        return null;
                    },
                    o =>
                    {
                        o.Launched.Should().BeEmpty();
                        o.Dialogs.ErrorMessages.Should().BeEmpty();
                    }
                )
            )
            .Prepend(
                Row(
                    "external-chat-link-launches-safe-url",
                    "http / https の URL はシェルへ渡る",
                    ShellChatLinkOpen,
                    SafeUrl,
                    Launched,
                    o =>
                    {
                        new ShellChatLinkOpener(o.Dialogs, "Title", o.Launched.Add).Open(
                            new Uri("https://example.com/a?b=1")
                        );
                        return null;
                    },
                    o =>
                    {
                        o.Launched.Should().Equal("https://example.com/a?b=1");
                        o.Dialogs.ErrorMessages.Should().BeEmpty();
                    }
                )
            )
            .Append(
                // URL と原因の例外文を添えてエラーのモーダルで知らせる
                Row(
                    "external-chat-link-reports-launch-failure-as-error",
                    "開けなかったときは URL と原因を添えてエラーで知らせる",
                    ShellChatLinkOpen,
                    LaunchFails,
                    Reported,
                    o =>
                    {
                        new ShellChatLinkOpener(
                            o.Dialogs,
                            "Title",
                            _ => throw new InvalidOperationException("no browser")
                        ).Open(new Uri("https://example.com/"));
                        return null;
                    },
                    o =>
                    {
                        o.Dialogs.ErrorMessages.Should().ContainSingle();
                        o.Dialogs.ErrorMessages[0].Should().Contain("https://example.com/");
                        o.Dialogs.ErrorMessages[0].Should().Contain("no browser");
                    }
                )
            );
    }
}
