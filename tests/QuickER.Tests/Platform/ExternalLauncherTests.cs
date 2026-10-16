using System.Diagnostics;
using System.IO;
using AwesomeAssertions;
using QuickER.Platform;

namespace QuickER.Tests.Platform;

/// <summary><see cref="ExternalLauncher"/> が起動の前に確かめる規則を検証する（プロセスは起動しない）</summary>
public sealed class ExternalLauncherTests
{
    /// <summary>開いてはいけない URL（既定のブラウザへ渡さない）</summary>
    public static TheoryData<string> UnsafeUrls =>
        new()
        {
            "javascript:alert(1)",
            "file:///C:/Windows/System32/calc.exe",
            @"\\server\share\x.exe",
            "//server/share/x.exe",
            "ms-settings:display",
            "mailto:someone@example.com",
            "ftp://example.com/file",
            "https://example.com@evil.test/",
        };

    [Theory(
        DisplayName = "OpenUrl: http / https の絶対 URL でホストを持つもの以外は、起動せずに ArgumentException"
    )]
    [MemberData(nameof(UnsafeUrls))]
    public void OpenUrl_UnsafeUrl_ThrowsWithoutStarting(string url)
    {
        var started = new List<ProcessStartInfo>();
        var uri = new Uri(url, UriKind.Absolute);

        var act = () => ExternalLauncher.OpenUrl(uri, started.Add);

        act.Should().Throw<ArgumentException>();
        started.Should().BeEmpty("開いてはいけない URL はシェルへ渡さない");
    }

    [Theory(DisplayName = "OpenUrl: http / https の URL はシェル経由で開く")]
    [InlineData("https://github.com/kokko-labs/QuickER")]
    [InlineData("http://localhost:8080/path?q=1")]
    public void OpenUrl_SafeUrl_StartsThroughShell(string url)
    {
        var started = new List<ProcessStartInfo>();

        ExternalLauncher.OpenUrl(new Uri(url), started.Add);

        started.Should().ContainSingle();
        started[0].UseShellExecute.Should().BeTrue();
        started[0].FileName.Should().Be(new Uri(url).AbsoluteUri);
    }

    [Fact(DisplayName = "OpenFolder: フルパスでない・無いフォルダは、起動せずに ArgumentException")]
    public void OpenFolder_RelativeOrMissing_ThrowsWithoutStarting()
    {
        var started = new List<ProcessStartInfo>();
        var missing = Path.Combine(
            Path.GetTempPath(),
            "QuickERTests_" + Guid.NewGuid().ToString("N")
        );

        foreach (var path in new[] { "relative\\folder", missing })
        {
            var act = () => ExternalLauncher.OpenFolder(path, started.Add);

            act.Should().Throw<ArgumentException>(path);
        }

        started.Should().BeEmpty();
    }

    [Fact(DisplayName = "OpenFolder: 在るフォルダはフルパスでシェル経由で開く")]
    public void OpenFolder_ExistingFullPath_StartsThroughShell()
    {
        var started = new List<ProcessStartInfo>();
        var folder = Path.GetTempPath();

        ExternalLauncher.OpenFolder(folder, started.Add);

        started.Should().ContainSingle().Which.FileName.Should().Be(folder);
    }

    [Fact(
        DisplayName = "StartExecutable: フルパスでない実行ファイルは、起動せずに ArgumentException"
    )]
    public void StartExecutable_RelativePath_ThrowsWithoutStarting()
    {
        var started = new List<ProcessStartInfo>();

        var act = () => ExternalLauncher.StartExecutable("QuickER.exe", started.Add);

        act.Should().Throw<ArgumentException>();
        started.Should().BeEmpty();
    }

    [Theory(DisplayName = "CreateProcess: 名前だけの実行ファイルは断る（dotnet を除く）")]
    [InlineData("cmd.exe")]
    [InlineData("claude")]
    [InlineData("tools\\codex.exe")]
    public void CreateProcess_BareOrRelativeName_Throws(string fileName)
    {
        var act = () =>
            ExternalLauncher.CreateProcess(new ProcessStartInfo { FileName = fileName });

        act.Should().Throw<ArgumentException>();
    }

    [Fact(
        DisplayName = "CreateProcess: dotnet だけは名前のまま起動してよい（.cmd を拾わないため）"
    )]
    public void CreateProcess_Dotnet_IsAllowedByName()
    {
        using var process = ExternalLauncher.CreateProcess(
            new ProcessStartInfo { FileName = "dotnet" }
        );

        process.StartInfo.FileName.Should().Be("dotnet");
        process.EnableRaisingEvents.Should().BeTrue();
        ExternalLauncher.BareExecutableNames.Should().BeEquivalentTo(["dotnet"]);
    }

    [Theory(DisplayName = "CreateProcess: 包み直していない .cmd / .bat のシムは断る")]
    [InlineData(@"C:\npm\claude.cmd")]
    [InlineData(@"C:\npm\codex.BAT")]
    public void CreateProcess_UnwrappedShim_Throws(string fileName)
    {
        var act = () =>
            ExternalLauncher.CreateProcess(new ProcessStartInfo { FileName = fileName });

        act.Should().Throw<ArgumentException>().WithMessage("*BatchShimProcessGuard*");
    }

    [Fact(
        DisplayName = "CreateProcess: BatchShimProcessGuard で包み直したシムは起動できる形で返す"
    )]
    public void CreateProcess_WrappedShim_IsReturned()
    {
        var startInfo = new ProcessStartInfo { FileName = @"C:\npm\claude.cmd" };
        startInfo.ArgumentList.Add("-p");
        BatchShimProcessGuard.Apply(startInfo, "{0}", "{0}", "{0}", "{0}");

        using var process = ExternalLauncher.CreateProcess(startInfo);

        process.StartInfo.FileName.Should().Be(BatchShimProcessGuard.CmdExePath);
    }

    [Fact(DisplayName = "CreateProcess: シェル経由の起動は断る")]
    public void CreateProcess_ShellExecute_Throws()
    {
        var act = () =>
            ExternalLauncher.CreateProcess(
                new ProcessStartInfo
                {
                    FileName = BatchShimProcessGuard.CmdExePath,
                    UseShellExecute = true,
                }
            );

        act.Should().Throw<ArgumentException>();
    }
}
