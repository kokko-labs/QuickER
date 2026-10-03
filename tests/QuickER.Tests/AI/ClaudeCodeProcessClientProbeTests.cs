using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using AwesomeAssertions;
using QuickER.AI;

namespace QuickER.Tests.AI;

/// <summary>
/// <see cref="ClaudeCodeProcessClient.ProbeLoginAsync"/>（ログイン確認）が、返らない claude を
/// 上限時間と破棄で止めることを検証するテストクラス。
/// </summary>
/// <remarks>
/// <para>
/// 偽の claude は一時フォルダの <c>claude.cmd</c>（1 秒ごとに heartbeat.txt へ追記し続けるループ）。
/// .cmd なので本物の npm 導入と同じく <see cref="BatchShimProcessGuard"/> が cmd.exe 経由へ包み直す経路を通り、
/// heartbeat が伸びなくなる＝cmd.exe とその子（ping）までプロセスツリーごと止まった、と確かめられる。
/// </para>
/// <para>
/// 確認が止まらないとアプリの終了（ForceClose → エンジンの破棄）でも子プロセスが孤児として残る。
/// 待ちの上限は並列実行で揺れないよう 30 秒に取り、テストが失敗しても偽の claude は finally で止める
/// （スクリプト自身も約 2 分で終わる）。
/// </para>
/// </remarks>
[Collection(RealProcessTestCollection.Name)]
public class ClaudeCodeProcessClientProbeTests
{
    /// <summary>並列実行でも揺れない待ちの上限</summary>
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(30);

    /// <summary>上限時間を過ぎた確認はプロセスツリーごと止まり、「確認できなかった」を返すことを検証する</summary>
    [Fact(
        DisplayName = "ログイン確認は上限時間を過ぎると claude を止めて確認できなかったとして返る"
    )]
    public async Task ProbeLogin_WhenTimeoutElapses_KillsProcessTreeAndReturnsUnavailable()
    {
        await using var fake = new FakeClaude();
        var client = new ClaudeCodeProcessClient(() => fake.ScriptPath, TimeSpan.FromSeconds(2));

        try
        {
            var probe = client.ProbeLoginAsync(TestContext.Current.CancellationToken);
            await fake.TrackProbeProcessAsync(client);

            var result = await probe.WaitAsync(Patience, TestContext.Current.CancellationToken);

            result.Should().Be(ClaudeLoginProbeResult.Unavailable);
            (await fake.HasStoppedAsync()).Should().BeTrue("上限時間で偽の claude ごと止まる");
        }
        finally
        {
            await client.DisposeAsync();
        }
    }

    /// <summary>実行中の確認は破棄でプロセスツリーごと止まり、「確認できなかった」を返すことを検証する</summary>
    [Fact(DisplayName = "ログイン確認は破棄で claude を止めて確認できなかったとして返る")]
    public async Task ProbeLogin_WhenDisposed_KillsProcessTreeAndReturnsUnavailable()
    {
        await using var fake = new FakeClaude();
        var client = new ClaudeCodeProcessClient(
            () => fake.ScriptPath,
            ClaudeCodeProcessClient.DefaultProbeTimeout
        );

        try
        {
            var probe = client.ProbeLoginAsync(TestContext.Current.CancellationToken);
            await fake.TrackProbeProcessAsync(client);
            await fake.WaitForHeartbeatAsync();

            await client.DisposeAsync();
            var result = await probe.WaitAsync(Patience, TestContext.Current.CancellationToken);

            result.Should().Be(ClaudeLoginProbeResult.Unavailable);
            (await fake.HasStoppedAsync()).Should().BeTrue("破棄で偽の claude ごと止まる");
        }
        finally
        {
            await client.DisposeAsync();
        }
    }

    /// <summary>
    /// 起動の直後（標準入力へ書き終える前）に破棄されても、確認は例外を出さずに「確認できなかった」を返すことを検証する。
    /// </summary>
    /// <remarks>
    /// 標準入力への書き込みはスレッドプールで進むため、破棄の Kill が先に走ると書き込みが閉じたパイプへ当たって
    /// <see cref="IOException"/> になる（全スイートの負荷下で実際に起きた）。素通しにすると、アプリの終了と
    /// 確認が重なったときに確認の呼び出し元へ例外が漏れる。競合なので毎回この順になるとは限らず、
    /// 書き込みが先に終わった回は破棄のテストと同じ経路を通る（どちらでも結果は Unavailable）
    /// </remarks>
    [Fact(
        DisplayName = "ログイン確認は起動直後に破棄されても例外を出さずに確認できなかったとして返る"
    )]
    public async Task ProbeLogin_WhenDisposedRightAfterStart_ReturnsUnavailableWithoutThrowing()
    {
        await using var fake = new FakeClaude();
        var client = new ClaudeCodeProcessClient(
            () => fake.ScriptPath,
            ClaudeCodeProcessClient.DefaultProbeTimeout
        );

        try
        {
            for (var attempt = 0; attempt < 5; attempt++)
            {
                var probe = client.ProbeLoginAsync(TestContext.Current.CancellationToken);
                await client.DisposeAsync();

                var result = await probe.WaitAsync(Patience, TestContext.Current.CancellationToken);

                result.Should().Be(ClaudeLoginProbeResult.Unavailable);
            }
        }
        finally
        {
            await client.DisposeAsync();
        }
    }

    /// <summary>返らない偽の claude（.cmd）と、その生死の確かめ方</summary>
    private sealed class FakeClaude : IAsyncDisposable
    {
        private readonly string _directory = Path.Combine(
            Path.GetTempPath(),
            "QuickER.Tests",
            "fake-claude",
            Guid.NewGuid().ToString("N")
        );

        /// <summary>確認が起動したプロセス（cmd.exe）の PID と開始時刻（後始末で止めるため）</summary>
        private (int Id, DateTime StartTime)? _probeProcess;

        public FakeClaude()
        {
            Directory.CreateDirectory(_directory);
            // 1 秒ごとに heartbeat を伸ばす。約 2 分で自分から終わる（テストが失敗しても残り続けない）
            File.WriteAllLines(
                ScriptPath,
                [
                    "@echo off",
                    "for /L %%i in (1,1,120) do (",
                    "  echo x>>\"%~dp0heartbeat.txt\"",
                    "  ping -n 2 127.0.0.1 >nul",
                    ")",
                ],
                Encoding.ASCII
            );
        }

        public string ScriptPath => Path.Combine(_directory, "claude.cmd");

        private string HeartbeatPath => Path.Combine(_directory, "heartbeat.txt");

        /// <summary>確認が起動したプロセスを控える（クライアントが掴むまで待つ）</summary>
        public async Task TrackProbeProcessAsync(ClaudeCodeProcessClient client)
        {
            var field = typeof(ClaudeCodeProcessClient).GetField(
                "_probeProcess",
                BindingFlags.NonPublic | BindingFlags.Instance
            )!;
            var deadline = DateTime.UtcNow + Patience;

            while (DateTime.UtcNow < deadline)
            {
                if (field.GetValue(client) is Process process)
                {
                    try
                    {
                        _probeProcess = (process.Id, process.StartTime);
                        return;
                    }
                    catch (InvalidOperationException)
                    {
                        // 控える前に終わって破棄された
                        return;
                    }
                }

                await Task.Delay(20);
            }

            throw new Xunit.Sdk.XunitException("偽の claude（claude.cmd）が起動しませんでした");
        }

        /// <summary>偽の claude が動き出した（heartbeat が書かれた）ことを待つ</summary>
        public async Task WaitForHeartbeatAsync()
        {
            var deadline = DateTime.UtcNow + Patience;

            while (!File.Exists(HeartbeatPath))
            {
                if (DateTime.UtcNow >= deadline)
                {
                    throw new Xunit.Sdk.XunitException(
                        "偽の claude（claude.cmd）が動き出しませんでした"
                    );
                }

                await Task.Delay(50);
            }
        }

        /// <summary>heartbeat が伸びなくなったか（＝cmd.exe とその子が止まったか）を 3 秒見て判定する</summary>
        public async Task<bool> HasStoppedAsync()
        {
            // 停止の途中で書かれた最後の 1 行を拾わないよう、少し置いてから測り始める
            await Task.Delay(500);
            var before = HeartbeatLength();
            await Task.Delay(3000);
            return HeartbeatLength() == before;
        }

        private long HeartbeatLength() =>
            File.Exists(HeartbeatPath) ? new FileInfo(HeartbeatPath).Length : 0;

        public async ValueTask DisposeAsync()
        {
            // テストが失敗して偽の claude が残っていれば止める（PID の再利用に備えて開始時刻も照合する）
            if (_probeProcess is { } tracked)
            {
                try
                {
                    using var process = Process.GetProcessById(tracked.Id);

                    if (!process.HasExited && process.StartTime == tracked.StartTime)
                    {
                        process.Kill(entireProcessTree: true);
                        // 同期の WaitForExit でテストのスレッドを塞がない
                        using var exitCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                        await process.WaitForExitAsync(exitCts.Token);
                    }
                }
                catch (Exception ex)
                    when (ex
                            is ArgumentException
                                or InvalidOperationException
                                or OperationCanceledException
                    )
                {
                    // 既に終わっている
                }
            }

            try
            {
                Directory.Delete(_directory, recursive: true);
            }
            catch (IOException)
            {
                // 消せなくてもテストの結果には関係しない（一時フォルダ）
            }
        }
    }
}
