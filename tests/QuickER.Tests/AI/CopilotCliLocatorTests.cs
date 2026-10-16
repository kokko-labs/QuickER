using System.IO;
using AwesomeAssertions;
using QuickER.AI;
using QuickER.Platform;
using AiStrings = QuickER.AI.Resources.Strings;

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

    /// <summary>
    /// Windows では引用符で囲んだ PATH 要素（Windows では正当）も、引用符を外して走査することを検証する。
    /// </summary>
    [Fact(DisplayName = "Windows では引用符で囲んだ PATH 要素も走査する")]
    public void PathExecutableResolver_QuotedEntry_IsScanned()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var expected = Path.Combine(FakeBinDirectory, "copilot.cmd");
        var pathValue = "\"" + FakeBinDirectory + "\"";

        PathExecutableResolver
            .Resolve("copilot", pathValue, Existing(expected))
            .Should()
            .Be(expected);
    }

    /// <summary>
    /// Windows では拡張子なしのファイル（npm が置く sh スクリプト）を候補にせず、後ろのフォルダの実体まで走査することを
    /// 検証する。拡張子なしの名前は CreateProcess が .exe を補うため起動できない。
    /// </summary>
    [Fact(DisplayName = "Windows では拡張子なしのファイルを読み飛ばして後ろのフォルダの実体を返す")]
    public void PathExecutableResolver_ExtensionlessFile_IsSkippedOnWindows()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var shScriptDirectory = Path.Combine(Path.GetTempPath(), "QuickERTests", "sh-only");
        var shScript = Path.Combine(shScriptDirectory, "copilot");
        var expected = Path.Combine(FakeBinDirectory, "copilot.cmd");
        var pathValue = string.Join(Path.PathSeparator, shScriptDirectory, FakeBinDirectory);

        PathExecutableResolver
            .Resolve("copilot", pathValue, Existing(shScript, expected))
            .Should()
            .Be(expected);
    }

    /// <summary>
    /// Windows 以外では拡張子なしの名前が唯一の候補で、引用符は名前の一部として扱うことを検証する
    /// （Unix の PATH に引用の規則は無い）。
    /// </summary>
    [Fact(DisplayName = "Windows 以外では拡張子なしの名前を候補にし引用符を外さない")]
    public void PathExecutableResolver_NonWindows_UsesBareNameAndKeepsQuotes()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var expected = Path.Combine(FakeBinDirectory, "copilot");

        PathExecutableResolver
            .Resolve("copilot", FakeBinDirectory, Existing(expected))
            .Should()
            .Be(expected);
        PathExecutableResolver
            .Resolve("copilot", "\"" + FakeBinDirectory + "\"", Existing(expected))
            .Should()
            .BeNull();
    }

    /// <summary>
    /// 起動直前の関門が、シムのパスに引用符を含む解決結果を拒否することを検証する。
    /// copilot は SDK の内側で起動されるため QuickER が引数を引用し直せず、掛けられる防御はここだけ。
    /// </summary>
    [Fact(DisplayName = "引用符を含む cmd シムのパスは起動前に拒否する")]
    public void EnsureSafeToLaunch_QuoteInShimPath_Throws()
    {
        const string path = @"C:\tools\evil"" & calc & ""x.cmd";

        var act = () => CopilotCliLocator.EnsureSafeToLaunch(path);

        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage(string.Format(AiStrings.Copilot_PathHasQuote, path));
    }

    /// <summary>
    /// 起動直前の関門が、シムのパスに <c>%</c> を含む解決結果を拒否することを検証する
    /// （シムを起動するとき CreateProcess が挟む cmd が環境変数として展開するため）。
    /// </summary>
    [Fact(DisplayName = "% を含む cmd シムのパスは起動前に拒否する")]
    public void EnsureSafeToLaunch_PercentInShimPath_Throws()
    {
        const string path = @"C:\%EVIL%\copilot.cmd";

        var act = () => CopilotCliLocator.EnsureSafeToLaunch(path);

        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage(string.Format(AiStrings.Copilot_PathHasEnvExpansion, path));
    }

    /// <summary>
    /// 起動直前の関門が、シムのパスに <c>!</c> を含む解決結果を拒否することを検証する。
    /// codex / claude は自分で <c>/v:off</c> を付けて包み直すが、copilot は SDK が cmd を起動するため
    /// 付けられず、利用者がレジストリで遅延展開を有効にしていると <c>!名前!</c> が展開される。
    /// </summary>
    [Fact(DisplayName = "! を含む cmd シムのパスは起動前に拒否する")]
    public void EnsureSafeToLaunch_ExclamationInShimPath_Throws()
    {
        const string path = @"C:\!EVIL!\copilot.cmd";

        var act = () => CopilotCliLocator.EnsureSafeToLaunch(path);

        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage(string.Format(AiStrings.Copilot_PathHasDelayedExpansion, path));
    }

    /// <summary>
    /// 改行を含む cmd シムのパスは起動前に拒否する（cmd は改行から後ろを捨てる。判定は包み直しの入口と共有する
    /// <see cref="BatchShimProcessGuard.ContainsNewline"/>）
    /// </summary>
    [Theory(DisplayName = "改行を含む cmd シムのパスは起動前に拒否する")]
    [InlineData("C:\\npm\nx\\copilot.cmd")]
    [InlineData("C:\\npm\rx\\copilot.cmd")]
    public void EnsureSafeToLaunch_NewlineInShimPath_Throws(string path)
    {
        var act = () => CopilotCliLocator.EnsureSafeToLaunch(path);

        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage(string.Format(AiStrings.Copilot_PathHasNewline, path));
    }

    /// <summary>正当なパス（シム・実行ファイルとも）はそのまま通ることを検証する</summary>
    /// <remarks>実行ファイルは cmd を経由しないため、パスに <c>%</c> があっても通す。</remarks>
    [Theory]
    [InlineData(@"C:\npm\copilot.cmd")]
    [InlineData(@"C:\Program Files\GitHub\copilot.exe")]
    [InlineData(@"C:\100%\copilot.exe")]
    [InlineData(@"C:\Hi!\copilot.exe")]
    public void EnsureSafeToLaunch_SafePath_PassesThrough(string path)
    {
        CopilotCliLocator.EnsureSafeToLaunch(path).Should().Be(path);
    }

    /// <summary>
    /// 解決（<see cref="CopilotCliLocator.IsAvailable"/> が通る経路）は検証を行わず例外も投げないことを検証する。
    /// 検証を解決側へ置くと、接続タブを開いて検出するだけで例外になる。
    /// </summary>
    [Fact(DisplayName = "解決は引用符を含むパスでも例外を投げない")]
    public void ResolveExecutablePath_DoesNotValidate()
    {
        var directory = @"C:\evil"" & calc";
        var expected = Path.Combine(directory, PrimaryCandidate);

        var resolved = CopilotCliLocator.ResolveExecutablePath(directory, Existing(expected));

        resolved.Should().Be(expected);
    }
}
