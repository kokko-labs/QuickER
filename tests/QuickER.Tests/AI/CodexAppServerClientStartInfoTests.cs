using System.IO;
using AwesomeAssertions;
using QuickER.AI;

namespace QuickER.Tests.AI;

/// <summary>
/// <see cref="CodexAppServerClient"/> のプロセス起動情報決定のうち、app-server の作業フォルダを検証するテストクラス
/// </summary>
/// <remarks>
/// 残したテストは作業フォルダ（実 OS の一時フォルダ）を見る。起動情報の決定（.cmd/.bat の cmd.exe ラップと
/// コマンド挿入につながる入力の拒否）は <c>Platform/OsBoundary/ProcessLaunchRows*.cs</c> へ移した。
/// </remarks>
public class CodexAppServerClientStartInfoTests
{
    private const string Arguments = "app-server --listen stdio://";

    /// <summary>
    /// app-server のプロセスはアプリのカレントフォルダでなく、中身の無い %TEMP%\QuickER\codex で起動する
    /// （実測では設定・AGENTS.md はスレッドの cwd の側から読まれるが、版の変更で読むようになっても実行フォルダに触れないため）
    /// </summary>
    [Fact(
        DisplayName = "app-server のプロセスの作業フォルダは %TEMP%\\QuickER\\codex で、カレントフォルダではない"
    )]
    public void CreateStartInfo_WorkingDirectoryIsBackendTempRoot()
    {
        var startInfo = CodexAppServerClient.CreateStartInfo(@"C:\tools\codex.exe", Arguments);

        startInfo
            .WorkingDirectory.Should()
            .Be(Path.Combine(QuickER.Platform.AppDataPaths.TempRoot, "codex"));
        startInfo.WorkingDirectory.Should().NotBe(Environment.CurrentDirectory);
        Directory.Exists(startInfo.WorkingDirectory).Should().BeTrue();
        startInfo.UseShellExecute.Should().BeFalse();
    }
}
