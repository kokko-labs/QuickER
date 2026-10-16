using System.Diagnostics;
using System.IO;
using System.Text;
using AwesomeAssertions;
using QuickER.Platform;
using QuickER.Tests.AI;

namespace QuickER.Tests.Platform;

/// <summary>
/// 末尾に空白や <c>.</c> の付いた <c>.cmd</c> のパスも、シムとして包み直して起動し、引数の <c>&amp;</c> を cmd に解釈させないことを
/// 実プロセスで固定する
/// </summary>
/// <remarks>
/// Windows はファイル名の末尾の空白と <c>.</c> を黙って落として開くので、<c>t.cmd </c> は <c>t.cmd</c> として cmd 経由で起動される。
/// 拡張子の完全一致だけでシムを判定すると、包み直しも引数の検査も通らずに cmd が引数を解釈し、<c>x&amp;echo.INJECTED</c> の
/// 後半がコマンドとして走っていた。
/// </remarks>
[Collection(RealProcessTestCollection.Name)]
public sealed class BatchShimTrailingNameTests
{
    [Theory(
        DisplayName = "末尾に空白や . の付いた .cmd も包み直して起動し、引数の & をコマンドとして走らせない"
    )]
    [InlineData("t.cmd ")]
    [InlineData("t.cmd.")]
    [InlineData("t.cmd. .")]
    public void TrailingDotOrSpaceShim_IsWrapped_AndArgumentsAreNotInjected(string name)
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "QuickER.Tests",
            "shim-trailing",
            Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(directory);

        try
        {
            File.WriteAllText(Path.Combine(directory, "t.cmd"), "@echo RAN %*\r\n", Encoding.ASCII);
            var startInfo = new ProcessStartInfo(Path.Combine(directory, name))
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            startInfo.ArgumentList.Add("x&echo.INJECTED");

            BatchShimProcessGuard.IsBatchShim(startInfo.FileName).Should().BeTrue();
            BatchShimProcessGuard.Apply(startInfo, "{0}", "{0}", "{0}", "{0}");
            startInfo
                .FileName.Should()
                .Be(BatchShimProcessGuard.CmdExePath, "cmd.exe 経由へ包み直す");

            using var process = ExternalLauncher.CreateProcess(startInfo);
            process.Start();
            var stdout = process.StandardOutput.ReadToEnd();
            var stderr = process.StandardError.ReadToEnd();
            process.WaitForExit();

            var lines = (stdout + "\n" + stderr).Split(
                ["\r\n", "\n"],
                StringSplitOptions.RemoveEmptyEntries
            );
            lines
                .Should()
                .NotContain(line => line.Trim() == "INJECTED", "引数の & は引用の内側でただの文字");

            // 末尾の空白は cmd も落として起動する。末尾の . は引用した名前を cmd が開けず、何も実行せずに止まる（閉じる側）
            if (!name.EndsWith('.'))
            {
                lines.Should().Contain(line => line.StartsWith("RAN", StringComparison.Ordinal));
            }
        }
        finally
        {
            try
            {
                Directory.Delete(directory, recursive: true);
            }
            catch (IOException)
            {
                // 消せなくてもテストの結果には関係しない（一時フォルダ）
            }
        }
    }
}
