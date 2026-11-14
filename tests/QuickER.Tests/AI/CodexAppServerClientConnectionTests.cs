using System.IO;
using System.Reflection;
using AwesomeAssertions;
using QuickER.AI;
using AiStrings = QuickER.AI.Resources.Strings;

namespace QuickER.Tests.AI;

/// <summary>
/// <see cref="CodexAppServerClient"/> の接続の扱い（詰まった書き込みの打ち切り・接続ごとの標準エラー）を、
/// 偽の app-server（<see cref="FakeCodexAppServerProcess"/>）を実プロセスとして起動して検証するテストクラス。
/// </summary>
[Collection(RealProcessTestCollection.Name)]
public class CodexAppServerClientConnectionTests
{
    /// <summary>並列実行でも揺れない待ちの上限（壊れたときだけ待つ。全件の実行の負荷でも揺れないよう 60 秒）</summary>
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(60);

    /// <summary>パイプのバッファに収まらず、相手が読まなければ書き込みが詰まる大きさのプロンプト</summary>
    private static readonly string LargePrompt = new('x', 4 * 1024 * 1024);

    /// <summary>
    /// 相手が標準入力を読まず書き込みが詰まったリクエストは、呼び出し元の取り消しで戻り、接続を捨てて
    /// 接続断として通知することを検証する（書き込みの途中で止めた接続は JSON の行が半端に残って使えない）。
    /// </summary>
    [Fact(DisplayName = "書き込みが詰まったリクエストは取り消しで戻り接続断として扱う")]
    public async Task Request_WhenWriteIsStuck_CancelsAndAbortsConnection()
    {
        await using var server = new FakeCodexAppServerProcess("block");
        var client = CreateClientWithoutTimeout(server);
        var disconnected = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        client.Disconnected += (_, _) => disconnected.TrySetResult();

        try
        {
            await server.StartAsync(client, TestContext.Current.CancellationToken);

            using var requestCts = new CancellationTokenSource(TimeSpan.FromSeconds(1));
            var request = async () =>
                await client.StartTurnAsync("thread", LargePrompt, requestCts.Token);

            await request
                .Should()
                .ThrowAsync<OperationCanceledException>()
                .WaitAsync(Patience, TestContext.Current.CancellationToken);
            await disconnected.Task.WaitAsync(Patience, TestContext.Current.CancellationToken);
            client.IsStarted.Should().BeFalse("書き込みの途中で止めた接続は捨てる");

            // 取り消しが先に戻っても、書き込みロックは書き込みが終わった（接続を捨てて失敗した）ときにちょうど 1 回手放す
            // （手放さないと次の接続の送信がすべて止まり、2 回手放すと上限を超えて SemaphoreFullException になる）
            await WaitUntilAsync(() => GetWriteLock(client).CurrentCount == 1);
        }
        finally
        {
            await client.DisposeAsync();
        }
    }

    /// <summary>
    /// 取り消しが書き込みの終わりより先に勝っても、呼び出し元へはすぐ取り消しで戻り、書き込みロックは書き込みが終わるまで持ち続け、
    /// 終わったときにちょうど 1 回手放すことを検証する（プールが詰まったときに起きる経路を、書き込みの終わりを門で止めて作る）。
    /// </summary>
    [Fact(
        DisplayName = "取り消しが書き込みの終わりより先に勝っても、書き込みロックは書き込みが終わったときにちょうど 1 回手放す"
    )]
    public async Task Request_CanceledBeforeWriteEnds_ReleasesWriteLockOnceWhenWriteEnds()
    {
        await using var server = new FakeCodexAppServerProcess("block");
        var client = CreateClientWithoutTimeout(server);
        var writeEnds = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        var disconnected = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        client.Disconnected += (_, _) => disconnected.TrySetResult();

        try
        {
            await server.StartAsync(client, TestContext.Current.CancellationToken);

            // 門は起動の後に掛ける（起動のハンドシェイクの書き込みは止めない）
            client.WriteCompletionForTests = async write =>
            {
                await writeEnds.Task.ConfigureAwait(false);
                await write.ConfigureAwait(false);
            };

            using var requestCts = new CancellationTokenSource(TimeSpan.FromSeconds(1));
            var request = async () =>
                await client.StartTurnAsync("thread", LargePrompt, requestCts.Token);

            await request
                .Should()
                .ThrowAsync<OperationCanceledException>()
                .WaitAsync(Patience, TestContext.Current.CancellationToken);
            await disconnected.Task.WaitAsync(Patience, TestContext.Current.CancellationToken);
            GetWriteLock(client)
                .CurrentCount.Should()
                .Be(0, "終わっていない書き込みの後ろに次の行を書かせない");

            writeEnds.SetResult();

            // 2 回手放すと SemaphoreSlim(1,1) の上限を超える。先に手放していれば上の 0 の表明が赤になる
            await WaitUntilAsync(() => GetWriteLock(client).CurrentCount == 1);
        }
        finally
        {
            writeEnds.TrySetResult();
            await client.DisposeAsync();
        }
    }

    /// <summary>
    /// 書き込みロックを待っている段階で取り消したリクエストは、何も書いていないので接続を捨てないことを検証する。
    /// </summary>
    [Fact(DisplayName = "書き込みロックを待つ段階で取り消したリクエストは接続を捨てない")]
    public async Task Request_CanceledWhileWaitingForWriteLock_KeepsConnection()
    {
        await using var server = new FakeCodexAppServerProcess("block");
        var client = CreateClientWithoutTimeout(server);
        var disconnected = false;
        client.Disconnected += (_, _) => disconnected = true;
        Task? stuck = null;

        try
        {
            await server.StartAsync(client, TestContext.Current.CancellationToken);

            // 先のリクエストが書き込みロックを握ったまま詰まる（取り消さず、タイムアウトも無いので破棄まで握り続ける）
            stuck = client.StartTurnAsync("thread", LargePrompt, CancellationToken.None);
            await WaitUntilAsync(() => GetWriteLock(client).CurrentCount == 0);

            using var requestCts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
            var request = async () => await client.ReadAccountAsync(false, requestCts.Token);

            await request
                .Should()
                .ThrowAsync<OperationCanceledException>()
                .WaitAsync(Patience, TestContext.Current.CancellationToken);
            disconnected.Should().BeFalse("ロック待ちの取り消しでは何も書いていない");
            client.IsStarted.Should().BeTrue();
        }
        finally
        {
            await client.DisposeAsync();

            if (stuck is not null)
            {
                // 破棄で止めた書き込みの失敗を観測しておく（未観測の例外を残さない）
                await stuck.ContinueWith(_ => { }, TaskScheduler.Default);
            }
        }
    }

    /// <summary>
    /// 書き込みが詰まっている最中の破棄が、例外を出さずに子プロセスを止め、詰まった書き込みもすぐに終わらせることを
    /// 検証する（アプリの終了＝エンジンの破棄で app-server を孤児として残さない）。
    /// </summary>
    /// <remarks>
    /// 標準入力を先に閉じようとすると、書き込みの途中なので閉じる処理が例外になり、子プロセスを止める前に抜ける。
    /// さらにその後は相手を止めても詰まった書き込みが失敗で終わらず、タイムアウトまで残る。
    /// 要求のタイムアウトを外してあるので、要求が終わるなら破棄が終わらせたことになる
    /// </remarks>
    [Fact(
        DisplayName = "書き込みが詰まっている最中の破棄は子プロセスを止めて詰まった要求もすぐ終わらせる"
    )]
    public async Task Dispose_WhileWriteIsStuck_StopsServerAndEndsStuckRequest()
    {
        await using var server = new FakeCodexAppServerProcess("block");
        var client = CreateClientWithoutTimeout(server);
        Task? stuck = null;

        try
        {
            await server.StartAsync(client, TestContext.Current.CancellationToken);

            stuck = client.StartTurnAsync("thread", LargePrompt, CancellationToken.None);
            await WaitUntilAsync(() => GetWriteLock(client).CurrentCount == 0);

            var dispose = async () => await client.DisposeAsync();

            await dispose.Should().NotThrowAsync();
            (await server.WaitForServersToExitAsync(Patience)).Should().Be(0);

            // 相手を止めた書き込みは、パイプの切断（IOException）か停止の取り消し（接続断）のどちらかが先に起きて失敗で終わる
            var failure = await StuckRequestFailureAsync(stuck);
            failure
                .Should()
                .Match<Exception>(
                    exception =>
                        exception is IOException
                        || (
                            exception is InvalidOperationException
                            && exception.Message == AiStrings.Codex_ConnectionClosed
                        ),
                    "破棄で止めた書き込みは接続断で失敗する"
                );
        }
        finally
        {
            await client.DisposeAsync();

            if (stuck is not null)
            {
                await stuck.ContinueWith(_ => { }, TaskScheduler.Default);
            }
        }
    }

    /// <summary>
    /// 相手のプロセスを止めてもパイプの読み口がほかに残っている（負荷の下で実際に起きた）ときでも、破棄が詰まった
    /// 書き込みを自分で取り消し、要求をすぐ接続断で終わらせることを検証する。
    /// </summary>
    /// <remarks>
    /// 読み口が残ると、詰まった書き込みは相手が止まっても失敗で終わらず、タイムアウトまで残る。
    /// 偽サーバーの標準入力のハンドルをこのテストのプロセスへ複製して持ち、その状態を決定的に作る。
    /// 要求のタイムアウトを外してあるので、要求が終わるなら破棄が終わらせたことになる
    /// </remarks>
    [Fact(
        DisplayName = "読み口が残っていても、書き込みが詰まっている最中の破棄は詰まった要求をすぐ終わらせる"
    )]
    public async Task Dispose_WhileWriteIsStuckAndReadEndStaysOpen_EndsStuckRequest()
    {
        await using var server = new FakeCodexAppServerProcess("stdin-handle", "block");
        var client = CreateClientWithoutTimeout(server);
        Task? stuck = null;
        var heldReadEnd = IntPtr.Zero;

        try
        {
            await server.StartAsync(client, TestContext.Current.CancellationToken);
            await WaitUntilAsync(() =>
                client.BuildRecentStandardErrorSuffix().Contains("stdin-handle:")
            );

            // 偽サーバーの標準入力（パイプの読み口）をこのプロセスへ複製し、相手が止まっても読み口が残る状態にする
            var suffix = client.BuildRecentStandardErrorSuffix();
            var value = long.Parse(
                System
                    .Text.RegularExpressions.Regex.Match(suffix, @"stdin-handle:(\d+)")
                    .Groups[1]
                    .Value,
                System.Globalization.CultureInfo.InvariantCulture
            );
            var serverProcess = (System.Diagnostics.Process)
                typeof(CodexAppServerClient)
                    .GetField("_process", BindingFlags.NonPublic | BindingFlags.Instance)!
                    .GetValue(client)!;
            var source = NativeHandles.OpenProcess(
                NativeHandles.ProcessDupHandle,
                false,
                serverProcess.Id
            );
            source.Should().NotBe(IntPtr.Zero, "偽サーバーのプロセスを開ける");

            try
            {
                NativeHandles
                    .DuplicateHandle(
                        source,
                        new IntPtr(value),
                        NativeHandles.GetCurrentProcess(),
                        out heldReadEnd,
                        0,
                        false,
                        NativeHandles.DuplicateSameAccess
                    )
                    .Should()
                    .BeTrue("偽サーバーの標準入力を複製できる");
            }
            finally
            {
                NativeHandles.CloseHandle(source);
            }

            stuck = client.StartTurnAsync("thread", LargePrompt, CancellationToken.None);
            await WaitUntilAsync(() => GetWriteLock(client).CurrentCount == 0);

            await client.DisposeAsync();
            (await server.WaitForServersToExitAsync(Patience)).Should().Be(0);

            // 読み口が残っているのでパイプは切れない。停止の取り消しだけが書き込みを終わらせる
            var failure = await StuckRequestFailureAsync(stuck);
            failure
                .Should()
                .BeOfType<InvalidOperationException>(
                    "破棄で取り消した書き込みは接続断の失敗で終わる"
                )
                .Which.Message.Should()
                .Be(AiStrings.Codex_ConnectionClosed);
        }
        finally
        {
            await client.DisposeAsync();

            if (heldReadEnd != IntPtr.Zero)
            {
                NativeHandles.CloseHandle(heldReadEnd);
            }

            if (stuck is not null)
            {
                await stuck.ContinueWith(_ => { }, TaskScheduler.Default);
            }
        }
    }

    /// <summary>ハンドルの複製に使う Win32 API</summary>
    private static class NativeHandles
    {
        public const uint ProcessDupHandle = 0x0040;

        public const uint DuplicateSameAccess = 0x0002;

        [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
        public static extern IntPtr OpenProcess(uint access, bool inherit, int processId);

        [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
        [return: System.Runtime.InteropServices.MarshalAs(
            System.Runtime.InteropServices.UnmanagedType.Bool
        )]
        public static extern bool DuplicateHandle(
            IntPtr sourceProcess,
            IntPtr sourceHandle,
            IntPtr targetProcess,
            out IntPtr targetHandle,
            uint access,
            bool inherit,
            uint options
        );

        [System.Runtime.InteropServices.DllImport("kernel32.dll")]
        public static extern IntPtr GetCurrentProcess();

        [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
        [return: System.Runtime.InteropServices.MarshalAs(
            System.Runtime.InteropServices.UnmanagedType.Bool
        )]
        public static extern bool CloseHandle(IntPtr handle);
    }

    /// <summary>
    /// 再接続した後の標準エラーの直近行に、前の接続（終了したプロセス）の出力が混ざらないことを検証する。
    /// </summary>
    [Fact(DisplayName = "再接続した後の標準エラーに前の接続の出力を混ぜない")]
    public async Task Reconnect_DoesNotCarryOverPreviousStandardError()
    {
        await using var server = new FakeCodexAppServerProcess("stderr:OLD-STDERR", "exit");
        var client = server.CreateClient();

        try
        {
            await server.StartAsync(client, TestContext.Current.CancellationToken);
            await WaitUntilAsync(() =>
                !client.IsStarted && client.BuildRecentStandardErrorSuffix().Contains("OLD-STDERR")
            );

            server.ReplaceServerMessages();
            await server.StartAsync(client, TestContext.Current.CancellationToken);

            client.IsStarted.Should().BeTrue();
            client.BuildRecentStandardErrorSuffix().Should().NotContain("OLD-STDERR");
        }
        finally
        {
            await client.DisposeAsync();
        }
    }

    /// <summary>
    /// 要求のタイムアウトを外したクライアントを作る（取り消しと破棄の経路を確かめるテスト用）
    /// </summary>
    /// <remarks>
    /// 取り消しと 30 秒のタイムアウトは、どちらもスレッドプールで走るタイマーで知らされる。混んだ機械でプールの待ちが
    /// 30 秒に届くと、取り消しを処理する時点で両方が立っていて、優先順でタイムアウトとして返る。
    /// タイムアウトを外せば、取り消しと破棄だけが要求を終わらせる
    /// </remarks>
    private static CodexAppServerClient CreateClientWithoutTimeout(FakeCodexAppServerProcess server)
    {
        var client = server.CreateClient();
        client.RequestTimeoutForTests = Timeout.InfiniteTimeSpan;
        return client;
    }

    /// <summary>詰まっていた要求が失敗で終わるのを待ち、その例外を返す（上限を過ぎたら失敗にする）</summary>
    private static async Task<Exception> StuckRequestFailureAsync(Task stuck)
    {
        try
        {
            await stuck.WaitAsync(Patience, TestContext.Current.CancellationToken);
        }
        catch (TimeoutException) when (!stuck.IsCompleted)
        {
            throw new Xunit.Sdk.XunitException(
                "破棄の後も詰まった要求が上限時間内に終わりませんでした"
            );
        }
        catch (Exception exception)
        {
            return exception;
        }

        throw new Xunit.Sdk.XunitException("詰まっていた要求が失敗せずに終わりました");
    }

    /// <summary>条件が成り立つまで待つ（上限を過ぎたら失敗にする）</summary>
    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + Patience;

        while (!condition())
        {
            if (DateTime.UtcNow >= deadline)
            {
                throw new Xunit.Sdk.XunitException("条件が上限時間内に成り立ちませんでした");
            }

            await Task.Delay(50, TestContext.Current.CancellationToken);
        }
    }

    /// <summary>書き込みロック（private readonly フィールド）を取り出す</summary>
    private static SemaphoreSlim GetWriteLock(CodexAppServerClient client) =>
        (SemaphoreSlim)
            typeof(CodexAppServerClient)
                .GetField("_writeLock", BindingFlags.NonPublic | BindingFlags.Instance)!
                .GetValue(client)!;
}
