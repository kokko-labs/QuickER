using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json;
using AwesomeAssertions;
using QuickER.AI;

namespace QuickER.Tests.AI;

/// <summary>
/// <see cref="CodexAppServerClient"/> を偽の app-server（実プロセス）越しに動かし、起動の配線と
/// JSON-RPC の応答規則を検証するテストクラス。
/// </summary>
/// <remarks>
/// 偽サーバーは <see cref="FakeCodexAppServerProcess"/>（powershell.exe）。待ちの上限は並列実行で
/// 揺れないよう 30 秒に取る（通常は 1 秒以内に終わる）。各テストは最後に偽サーバーが残っていないことも確かめる。
/// </remarks>
[Collection(RealProcessTestCollection.Name)]
public class CodexAppServerClientProtocolTests
{
    /// <summary>並列実行でも揺れない待ちの上限</summary>
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(30);

    /// <summary>
    /// サーバー発のリクエストには、購読側へ渡せないときも必ず 1 回エラー応答が返り、
    /// 通知（id なし）には何も返らないことを検証する。
    /// </summary>
    /// <remarks>
    /// 応答が返らないと app-server は応答を永久に待つ。扱えないメソッド・購読者なし＝-32601、
    /// パラメータを解釈できない＝-32602（購読側へは渡さない）、購読側が同期的に投げた＝-32603
    /// </remarks>
    [Fact(DisplayName = "扱えないサーバー発リクエストには JSON-RPC のエラー応答を 1 回だけ返す")]
    public async Task ServerRequests_ThatCannotBeHandled_GetExactlyOneErrorResponse()
    {
        await using var server = new FakeCodexAppServerProcess(
            """{"id":100,"method":"x/unknown","params":{}}""",
            """{"id":101,"method":"item/tool/call"}""",
            """{"id":102,"method":"item/tool/call","params":{"threadId":"t","turnId":"u","callId":"c","tool":"x","arguments":{}}}""",
            """{"id":103,"method":"item/commandExecution/requestApproval","params":{"threadId":"t"}}""",
            """{"method":"x/unknownNotification","params":{}}"""
        );
        var client = server.CreateClient();
        var echoes = new ConcurrentQueue<JsonElement>();
        var allEchoed = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        var toolCalls = new ConcurrentQueue<int>();

        client.NotificationReceived += (_, notification) =>
        {
            if (
                notification.Method != FakeCodexAppServerProcess.EchoMethod
                || notification.Params is not JsonElement echoed
            )
            {
                return;
            }

            echoes.Enqueue(echoed);

            if (echoes.Count >= 4)
            {
                allEchoed.TrySetResult();
            }
        };
        // 購読はするが、渡されたら同期的に投げる（＝誰も応答しないまま終わる購読者）
        client.DynamicToolCallReceived += (_, request) =>
        {
            toolCalls.Enqueue(request.RequestId);
            throw new InvalidOperationException("subscriber failure");
        };

        try
        {
            await server.StartAsync(client, TestContext.Current.CancellationToken);
            await allEchoed.Task.WaitAsync(Patience, TestContext.Current.CancellationToken);

            // 1 往復を挟む＝これより前に送られた応答はすべて偽サーバーへ届いて送り返されている
            using var roundTrip = new CancellationTokenSource(Patience);
            await client.ReadAccountAsync(false, roundTrip.Token);
            await Task.Delay(200, TestContext.Current.CancellationToken);

            var codes = echoes
                .Select(echo =>
                    (
                        Id: echo.GetProperty("id").GetInt32(),
                        Code: echo.TryGetProperty("error", out var error)
                            ? error.GetProperty("code").GetInt32()
                            : (int?)null
                    )
                )
                .OrderBy(entry => entry.Id)
                .ToList();
            codes
                .Should()
                .Equal(
                    [(100, -32601), (101, -32602), (102, -32603), (103, -32601)],
                    "id ごとにエラー応答がちょうど 1 回・通知には応答しない"
                );
            toolCalls.Should().Equal([102], "パラメータを解釈できないリクエストは購読側へ渡さない");
        }
        finally
        {
            await client.DisposeAsync();
        }

        (await server.WaitForServersToExitAsync(Patience))
            .Should()
            .Be(0, "破棄で偽サーバーのプロセスが止まる");
    }

    /// <summary>
    /// 起動時に渡したトークンを後から取り消しても受信ループは生きていて、次の要求が通ることを検証する。
    /// 破棄すれば受信ループは止まり、意図的な停止なので接続断は通知しない
    /// </summary>
    /// <remarks>
    /// 受信ループの寿命を起動の呼び出し元のトークンに連結すると、その取り消し（ターンの中断など）で受信ループだけが
    /// 止まり、プロセスは生きたまま誰も応答を読まなくなる＝以後の全要求が 30 秒のタイムアウトになる
    /// </remarks>
    [Fact(DisplayName = "起動時のトークンを取り消しても受信ループは止まらない")]
    public async Task CancellingStartToken_AfterStart_KeepsReadLoopAlive()
    {
        await using var server = new FakeCodexAppServerProcess();
        var client = server.CreateClient();
        var disconnected = 0;
        client.Disconnected += (_, _) => Interlocked.Increment(ref disconnected);

        try
        {
            using var startCts = new CancellationTokenSource();
            await server.StartAsync(client, startCts.Token);

            await startCts.CancelAsync();
            await Task.Delay(500, TestContext.Current.CancellationToken);

            client.IsStarted.Should().BeTrue("起動時のトークンは受信ループの寿命に関係しない");
            using var requestCts = new CancellationTokenSource(Patience);
            var account = async () => await client.ReadAccountAsync(false, requestCts.Token);
            await account.Should().NotThrowAsync();

            var readerTask = (Task)
                typeof(CodexAppServerClient)
                    .GetField("_readerTask", BindingFlags.NonPublic | BindingFlags.Instance)!
                    .GetValue(client)!;

            await client.DisposeAsync();

            readerTask.IsCompleted.Should().BeTrue("破棄で受信ループは止まる");
            client.IsStarted.Should().BeFalse();
            disconnected.Should().Be(0, "取り消し・破棄はどちらも接続断ではない");
        }
        finally
        {
            await client.DisposeAsync();
        }

        (await server.WaitForServersToExitAsync(Patience))
            .Should()
            .Be(0, "破棄で偽サーバーのプロセスが止まる");
    }
}
