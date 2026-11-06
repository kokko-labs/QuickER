using System.IO;
using AwesomeAssertions;
using QuickER.Platform;

namespace QuickER.Tests.Platform;

/// <summary>
/// <see cref="BatchShimProcessGuard"/>（.cmd / .bat シム起動のコマンド挿入ガード）のうち、cmd.exe の実在を検証するテストクラス。
/// </summary>
/// <remarks>
/// 残したテストは cmd.exe の実在（実 OS）を見る。入力の形の行は OS との境界のエンジンの起動の表（<c>Platform/OsBoundary/ProcessLaunchRows*.cs</c>）へ移した。
/// </remarks>
public class BatchShimProcessGuardTests
{
    /// <summary>包み直しの起動先として期待する、システムフォルダ直下の cmd.exe のフルパス</summary>
    /// <remarks>製品側のプロパティを参照せず独立に組み立てる（プロパティ自体の変異を検出するため）。</remarks>
    private static string SystemCmdExe => Path.Combine(Environment.SystemDirectory, "cmd.exe");

    [Fact]
    public void CmdExePath_システムフォルダ直下のcmdをフルパスで指す()
    {
        // 名前だけの "cmd.exe" を渡さない＝起動する実体を探索規則に委ねず固定する（防御の二重化）
        BatchShimProcessGuard.CmdExePath.Should().Be(SystemCmdExe);
        Path.IsPathFullyQualified(BatchShimProcessGuard.CmdExePath).Should().BeTrue();
        File.Exists(BatchShimProcessGuard.CmdExePath).Should().BeTrue();
    }
}
