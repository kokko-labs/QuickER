using System.IO;
using AwesomeAssertions;
using QuickER.Platform;

namespace QuickER.Tests.Platform;

/// <summary>
/// <see cref="PathExecutableResolver"/> が、同じフォルダの <c>.exe</c> / <c>.cmd</c> / <c>.bat</c> のうち PATHEXT の順で最初のものを選ぶことを固定する
/// </summary>
/// <remarks>
/// コマンドラインで名前を打って起動されるのと同じものを選ぶ。以前は <c>.exe</c>・<c>.cmd</c>・<c>.bat</c> の固定の順で、
/// 既定の PATHEXT（<c>.COM;.EXE;.BAT;.CMD;…</c>）と違い、<c>.cmd</c> と <c>.bat</c> が並ぶフォルダでは <c>.cmd</c> を選んでいた。
/// </remarks>
public sealed class PathExecutableResolverTests
{
    private static readonly string Folder = Path.Combine(Path.GetTempPath(), "tools");

    private static string? Resolve(string? pathExt, params string[] existing)
    {
        var files = existing
            .Select(name => Path.Combine(Folder, name))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return PathExecutableResolver.Resolve("codex", Folder, files.Contains, pathExt);
    }

    [Fact(
        DisplayName = "既定の順（PATHEXT が無いとき）は .exe・.bat・.cmd で、.cmd と .bat が並べば .bat を選ぶ"
    )]
    public void DefaultOrder_FollowsWindowsDefaultPathExt()
    {
        Resolve(null, "codex.cmd", "codex.bat").Should().Be(Path.Combine(Folder, "codex.bat"));
        Resolve(null, "codex.cmd", "codex.exe").Should().Be(Path.Combine(Folder, "codex.exe"));
        Resolve(null, "codex.cmd").Should().Be(Path.Combine(Folder, "codex.cmd"));
        PathExecutableResolver
            .Resolve(
                "codex",
                Folder,
                name =>
                    name.EndsWith(".bat", StringComparison.Ordinal)
                    || name.EndsWith(".cmd", StringComparison.Ordinal)
            )
            .Should()
            .Be(Path.Combine(Folder, "codex.bat"), "PATHEXT を渡さない多重定義も既定の順");
    }

    [Theory(DisplayName = "PATHEXT の順に試し、.exe / .cmd / .bat 以外の拡張子は試さない")]
    [InlineData(".COM;.EXE;.BAT;.CMD;.VBS", "codex.exe")]
    [InlineData(".BAT;.CMD;.EXE", "codex.bat")]
    [InlineData(".CMD;.EXE;.BAT", "codex.cmd")]
    [InlineData(" .cmd ; .bat ;.exe", "codex.cmd")]
    [InlineData(".EXE;.CMD", "codex.exe")]
    [InlineData(".COM;.PS1", "codex.exe")]
    [InlineData("", "codex.exe")]
    public void PathExtOrder_IsFollowed(string pathExt, string expected)
    {
        Resolve(pathExt, "codex.exe", "codex.cmd", "codex.bat", "codex.com", "codex.ps1")
            .Should()
            .Be(Path.Combine(Folder, expected));
    }

    [Fact(DisplayName = "PATHEXT に無い候補は、ファイルがあっても選ばない")]
    public void ExtensionMissingFromPathExt_IsNotTried()
    {
        Resolve(".EXE;.CMD", "codex.bat").Should().BeNull();
        Resolve(".EXE;.CMD", "codex.com").Should().BeNull();
    }
}
