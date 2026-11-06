using System.Diagnostics;
using AwesomeAssertions;
using QuickER.AI.Mock;
using QuickER.Platform;
using QuickER.Tests.AI;

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
    /// 実行中に取り消すと、プロセスを止めて速やかに <see cref="OperationCanceledException"/> で抜けることを検証する
    /// （止めずに待つと、120 秒走る子プロセスの終了まで戻らない）。
    /// </summary>
    /// <remarks>
    /// 上限 60 秒は「止めた（すぐ戻る）」と「止めずに待った（120 秒）」を分ける境目。
    /// 全件の実行の負荷で止めるまでが数秒遅れても揺れないよう、子プロセスの走る時間を上限より十分長くして、境目を広く取る。
    /// </remarks>
    [Fact(DisplayName = "実行中の取り消しはプロセスを止めて速やかに抜ける")]
    public async Task RunAsync_CanceledWhileRunning_KillsAndThrows()
    {
        var startInfo = new ProcessStartInfo
        {
            // 子プロセスはフルパスで起動する（ExternalLauncher.CreateProcess が名前だけの起動を断る）
            FileName = BatchShimProcessGuard.CmdExePath,
            ArgumentList = { "/c", "ping", "-n", "120", "127.0.0.1" },
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
        var stopwatch = Stopwatch.StartNew();

        var run = () => DotnetBuildRunner.RunAsync(startInfo, cts.Token);

        await run.Should().ThrowAsync<OperationCanceledException>();
        stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(60));
    }
}
