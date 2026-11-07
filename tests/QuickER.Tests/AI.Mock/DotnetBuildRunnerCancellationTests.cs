using System.Diagnostics;
using System.IO;
using AwesomeAssertions;
using QuickER.AI.Mock;
using QuickER.Platform;
using QuickER.Tests.AI;
using QuickER.Tests.TestSupport;

namespace QuickER.Tests.AI.Mock;

/// <summary>
/// <see cref="DotnetBuildRunner"/> の取り消しの扱い（取り消しを「利用不可」に畳まない・プロセスを止めて
/// 読み取りの完了を待ってから抜ける）を、実プロセスで検証するテストクラス。
/// </summary>
/// <remarks>
/// 実プロセスを起動するため RealProcess コレクションへ入れる（標準入出力は 3 つともリダイレクトする）。
/// </remarks>
[Collection(RealProcessTestCollection.Name)]
public class DotnetBuildRunnerCancellationTests
{
    /// <summary>
    /// 取り消し済みのトークンでの可用性確認が、false でなく <see cref="OperationCanceledException"/> になることを検証する
    /// （取り消しを「dotnet が無い」と取り違えない）。
    /// </summary>
    [Fact(DisplayName = "可用性確認は取り消しを利用不可に畳まない")]
    public async Task IsDotnetAvailableAsync_Canceled_Throws()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var check = () => new DotnetBuildRunner().IsDotnetAvailableAsync(cts.Token);

        await check.Should().ThrowAsync<OperationCanceledException>();
    }

    /// <summary>
    /// 実行中に取り消すと、プロセスツリー（子の cmd と孫の ping）を止めて速やかに <see cref="OperationCanceledException"/> で抜け、
    /// 子も孫も残さないことを検証する。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 止めたことは、孫の ping が標準出力として開いたままにする印のファイルで見る：取り消しの前は排他で開けず（孫が走っている）、
    /// 取り消しの後は排他で開ける（印を持つ子も孫も終わった）。「速やかに抜ける」だけでは足りない：読み取りは取り消しのトークンで
    /// 先に終わるので、プロセスを止めなくても抜けてしまう。止めないと孫が 120 秒残り、子だけを止めると孫が残る。
    /// </para>
    /// <para>
    /// 上限 60 秒は「止めた（すぐ戻る）」と「止めずに待った（120 秒）」を分ける境目。
    /// 全件の実行の負荷で止めるまでが数秒遅れても揺れないよう、孫の走る時間を上限より十分長くして、境目を広く取る。
    /// </para>
    /// </remarks>
    [Fact(DisplayName = "実行中の取り消しはプロセスツリーを止めて速やかに抜け、子も孫も残さない")]
    public async Task RunAsync_CanceledWhileRunning_KillsAndThrows()
    {
        using var folder = new TestTempFolder();
        var marker = Path.Combine(folder.Path, "running.txt");
        var startInfo = new ProcessStartInfo
        {
            // 子プロセスはフルパスで起動する（ExternalLauncher.CreateProcess が名前だけの起動を断る）。
            // 孫の ping の標準出力を印のファイルへ向ける（孫が走っている間、印は開いたまま）
            FileName = BatchShimProcessGuard.CmdExePath,
            ArgumentList = { "/c", "ping", "-n", "120", "127.0.0.1", ">", marker },
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        using var cts = new CancellationTokenSource();

        var run = DotnetBuildRunner.RunAsync(startInfo, cts.Token);

        (await WaitUntilAsync(() => File.Exists(marker) && !CanOpenExclusively(marker)))
            .Should()
            .BeTrue("取り消しの前に孫の ping が印のファイルを開いている（前提）");

        var stopwatch = Stopwatch.StartNew();
        await cts.CancelAsync();

        await ((Func<Task>)(() => run)).Should().ThrowAsync<OperationCanceledException>();
        stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(60));
        (await WaitUntilAsync(() => CanOpenExclusively(marker)))
            .Should()
            .BeTrue("取り消しの後は子も孫も終わり、印のファイルを誰も開いていない");
    }

    /// <summary>条件が真になるまで待つ（上限 30 秒・孫の 120 秒より十分短い）</summary>
    private static async Task<bool> WaitUntilAsync(Func<bool> condition)
    {
        var stopwatch = Stopwatch.StartNew();

        while (stopwatch.Elapsed < TimeSpan.FromSeconds(30))
        {
            if (condition())
            {
                return true;
            }

            await Task.Delay(50);
        }

        return condition();
    }

    /// <summary>ファイルを排他で開けるか（どのプロセスも開いていないか）</summary>
    private static bool CanOpenExclusively(string path)
    {
        try
        {
            using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.ReadWrite,
                FileShare.None
            );
            return true;
        }
        catch (IOException)
        {
            return false;
        }
    }
}
