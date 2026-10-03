using System.Reflection;
using AwesomeAssertions;
using QuickER.AI;

namespace QuickER.Tests.AI;

/// <summary>
/// <see cref="CodexAppServerClient"/> の接続の扱い（詰まった書き込みの打ち切り・接続ごとの標準エラー）を、
/// 偽の app-server（<see cref="FakeCodexAppServerProcess"/>）を実プロセスとして起動して検証するテストクラス。
/// </summary>
[Collection(RealProcessTestCollection.Name)]
public class CodexAppServerClientConnectionTests
{
    /// <summary>並列実行でも揺れない待ちの上限</summary>
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(30);

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
        var client = server.CreateClient();
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
        }
        finally
        {
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
        var client = server.CreateClient();
        var disconnected = false;
        client.Disconnected += (_, _) => disconnected = true;
        Task? stuck = null;

        try
        {
            await server.StartAsync(client, TestContext.Current.CancellationToken);

            // 先のリクエストが書き込みロックを握ったまま詰まる（取り消さない＝30 秒のタイムアウトまで握り続ける）
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
    /// さらにその後は相手を止めても詰まった書き込みが失敗で終わらず、30 秒のタイムアウトまで残る
    /// </remarks>
    [Fact(
        DisplayName = "書き込みが詰まっている最中の破棄は子プロセスを止めて詰まった要求もすぐ終わらせる"
    )]
    public async Task Dispose_WhileWriteIsStuck_StopsServerAndEndsStuckRequest()
    {
        await using var server = new FakeCodexAppServerProcess("block");
        var client = server.CreateClient();
        Task? stuck = null;

        try
        {
            await server.StartAsync(client, TestContext.Current.CancellationToken);

            stuck = client.StartTurnAsync("thread", LargePrompt, CancellationToken.None);
            await WaitUntilAsync(() => GetWriteLock(client).CurrentCount == 0);

            var dispose = async () => await client.DisposeAsync();

            await dispose.Should().NotThrowAsync();
            (await server.WaitForServersToExitAsync(Patience)).Should().Be(0);

            var completed = await Task.WhenAny(
                stuck,
                Task.Delay(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken)
            );

            completed.Should().BeSameAs(stuck, "30 秒のタイムアウトを待たずに終わる");
            stuck.IsFaulted.Should().BeTrue("相手を止めた書き込みは失敗で終わる");
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
