using System.IO;
using AwesomeAssertions;
using QuickER.AI;

namespace QuickER.Tests.AI;

/// <summary>
/// copilot CLI の PATH 走査（<see cref="CopilotCliLocator"/>）を検証するテストクラス。
/// 実 PATH を書き換えるとプロセス全体（＝並列テスト）へ影響するため、走査対象の PATH 値と
/// ファイル存在判定を注入する internal オーバーロードで検証する（<see cref="CodexCliLocatorTests"/> と同型）。
/// </summary>
public class CopilotCliLocatorTests
{
    /// <summary>走査対象に使う架空のディレクトリ（実在は不要＝存在判定は注入する）</summary>
    private static string FakeBinDirectory =>
        Path.Combine(Path.GetTempPath(), "QuickERTests", "copilot-bin");

    /// <summary>OS ごとに最初に当たる候補ファイル名</summary>
    private static string PrimaryCandidate =>
        OperatingSystem.IsWindows() ? "copilot.exe" : "copilot";

    /// <summary>指定した実在パス集合に対する存在判定を作る（Windows に合わせて大文字小文字は無視する）</summary>
    private static Func<string, bool> Existing(params string[] paths) =>
        candidate => paths.Contains(candidate, StringComparer.OrdinalIgnoreCase);

    /// <summary>PATH 上のディレクトリに copilot 実行ファイルがあれば、そのフルパスを返すことを検証する</summary>
    [Fact(DisplayName = "PATH 上に copilot があればフルパスを返す")]
    public void ResolveExecutablePath_FoundOnPath_ReturnsFullPath()
    {
        var expected = Path.Combine(FakeBinDirectory, PrimaryCandidate);

        var resolved = CopilotCliLocator.ResolveExecutablePath(
            FakeBinDirectory,
            Existing(expected)
        );

        resolved.Should().Be(expected);
    }

    /// <summary>Windows では拡張子付き候補（.cmd 等）も走査対象になることを検証する（npm 製 CLI 対策）</summary>
    [Fact(DisplayName = "Windows では copilot.cmd も検出できる")]
    public void ResolveExecutablePath_WindowsCmdCandidate_IsFound()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var expected = Path.Combine(FakeBinDirectory, "copilot.cmd");

        var resolved = CopilotCliLocator.ResolveExecutablePath(
            FakeBinDirectory,
            Existing(expected)
        );

        resolved.Should().Be(expected);
    }

    /// <summary>Windows では .exe が .cmd より優先されることを検証する（候補の走査順を固定する）</summary>
    [Fact(DisplayName = "Windows では copilot.exe が copilot.cmd より優先される")]
    public void ResolveExecutablePath_PrefersExeOverCmd()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var exe = Path.Combine(FakeBinDirectory, "copilot.exe");
        var cmd = Path.Combine(FakeBinDirectory, "copilot.cmd");

        var resolved = CopilotCliLocator.ResolveExecutablePath(
            FakeBinDirectory,
            Existing(exe, cmd)
        );

        resolved.Should().Be(exe);
    }

    /// <summary>PATH 上のどのディレクトリにも無ければ null（＝未検出）になることを検証する</summary>
    [Fact(DisplayName = "PATH 上に copilot が無ければ null")]
    public void ResolveExecutablePath_NotFound_ReturnsNull()
    {
        var pathValue = string.Join(
            Path.PathSeparator,
            FakeBinDirectory,
            Path.Combine(Path.GetTempPath(), "QuickERTests", "other-bin")
        );

        var resolved = CopilotCliLocator.ResolveExecutablePath(pathValue, _ => false);

        resolved.Should().BeNull();
    }

    /// <summary>PATH が空・未設定なら走査せず null を返すことを検証する</summary>
    [Theory(DisplayName = "PATH が空・未設定なら null")]
    [InlineData(null)]
    [InlineData("")]
    public void ResolveExecutablePath_EmptyPath_ReturnsNull(string? pathValue)
    {
        var resolved = CopilotCliLocator.ResolveExecutablePath(pathValue, _ => true);

        resolved.Should().BeNull();
    }

    /// <summary>空要素・空白のみの要素を含む PATH でも落ちず、後続のディレクトリを走査することを検証する</summary>
    [Fact(DisplayName = "PATH の空要素は読み飛ばして後続を走査する")]
    public void ResolveExecutablePath_SkipsEmptyEntries()
    {
        var expected = Path.Combine(FakeBinDirectory, PrimaryCandidate);
        var pathValue = string.Join(Path.PathSeparator, string.Empty, "   ", FakeBinDirectory);

        var resolved = CopilotCliLocator.ResolveExecutablePath(pathValue, Existing(expected));

        resolved.Should().Be(expected);
    }

    /// <summary>
    /// 共有走査（<see cref="PathExecutableResolver"/>）が、コマンド名ごとに別の実行ファイルを解決することを検証する。
    /// codex / claude / copilot の 3 ロケーターが同じ走査本体を使う前提を固定する。
    /// </summary>
    [Fact(DisplayName = "共有走査はコマンド名ごとに別の実行ファイルを解決する")]
    public void PathExecutableResolver_ResolvesPerCommandName()
    {
        var copilot = Path.Combine(FakeBinDirectory, PrimaryCandidate);
        var exists = Existing(copilot);

        PathExecutableResolver.Resolve("copilot", FakeBinDirectory, exists).Should().Be(copilot);
        PathExecutableResolver.Resolve("codex", FakeBinDirectory, exists).Should().BeNull();
    }
}
