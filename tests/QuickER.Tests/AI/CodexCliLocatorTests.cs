using System.IO;
using AwesomeAssertions;
using QuickER.AI;

namespace QuickER.Tests.AI;

/// <summary>
/// codex CLI の PATH 走査（<see cref="CodexCliLocator"/>）を検証するテストクラス。
/// 実 PATH を書き換えるとプロセス全体（＝並列テスト）へ影響するため、走査対象の PATH 値と
/// ファイル存在判定を注入する internal オーバーロードで検証する。
/// </summary>
public class CodexCliLocatorTests
{
    /// <summary>走査対象に使う架空のディレクトリ（実在は不要＝存在判定は注入する）</summary>
    private static string FakeBinDirectory =>
        Path.Combine(Path.GetTempPath(), "QuickERTests", "codex-bin");

    /// <summary>OS ごとに最初に当たる候補ファイル名</summary>
    private static string PrimaryCandidate => OperatingSystem.IsWindows() ? "codex.exe" : "codex";

    /// <summary>指定した実在パス集合に対する存在判定を作る（Windows に合わせて大文字小文字は無視する）</summary>
    private static Func<string, bool> Existing(params string[] paths) =>
        candidate => paths.Contains(candidate, StringComparer.OrdinalIgnoreCase);

    /// <summary>PATH 上のディレクトリに codex 実行ファイルがあれば、そのフルパスを返すことを検証する</summary>
    [Fact(DisplayName = "PATH 上に codex があればフルパスを返す")]
    public void ResolveExecutablePath_FoundOnPath_ReturnsFullPath()
    {
        var expected = Path.Combine(FakeBinDirectory, PrimaryCandidate);

        var resolved = CodexCliLocator.ResolveExecutablePath(FakeBinDirectory, Existing(expected));

        resolved.Should().Be(expected);
    }

    /// <summary>Windows では拡張子付き候補（.cmd 等）も走査対象になることを検証する</summary>
    [Fact(DisplayName = "Windows では codex.cmd も検出できる")]
    public void ResolveExecutablePath_WindowsCmdCandidate_IsFound()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var expected = Path.Combine(FakeBinDirectory, "codex.cmd");

        var resolved = CodexCliLocator.ResolveExecutablePath(FakeBinDirectory, Existing(expected));

        resolved.Should().Be(expected);
    }

    /// <summary>PATH 上のどのディレクトリにも無ければ null（＝未検出）になることを検証する</summary>
    [Fact(DisplayName = "PATH 上に codex が無ければ null")]
    public void ResolveExecutablePath_NotFound_ReturnsNull()
    {
        var pathValue = string.Join(
            Path.PathSeparator,
            FakeBinDirectory,
            Path.Combine(Path.GetTempPath(), "QuickERTests", "other-bin")
        );

        var resolved = CodexCliLocator.ResolveExecutablePath(pathValue, _ => false);

        resolved.Should().BeNull();
    }

    /// <summary>PATH が空・未設定なら走査せず null を返すことを検証する</summary>
    [Theory(DisplayName = "PATH が空・未設定なら null")]
    [InlineData(null)]
    [InlineData("")]
    public void ResolveExecutablePath_EmptyPath_ReturnsNull(string? pathValue)
    {
        var resolved = CodexCliLocator.ResolveExecutablePath(pathValue, _ => true);

        resolved.Should().BeNull();
    }

    /// <summary>空要素・空白のみの要素を含む PATH でも落ちず、後続のディレクトリを走査することを検証する</summary>
    [Fact(DisplayName = "PATH の空要素は読み飛ばして後続を走査する")]
    public void ResolveExecutablePath_SkipsEmptyEntries()
    {
        var expected = Path.Combine(FakeBinDirectory, PrimaryCandidate);
        var pathValue = string.Join(Path.PathSeparator, string.Empty, "   ", FakeBinDirectory);

        var resolved = CodexCliLocator.ResolveExecutablePath(pathValue, Existing(expected));

        resolved.Should().Be(expected);
    }
}
