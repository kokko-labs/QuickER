using System.Collections.Concurrent;
using System.IO;
using System.Reflection;
using System.Text.Json;
using AwesomeAssertions;
using QuickER.AI;
using AiStrings = QuickER.AI.Resources.Strings;

namespace QuickER.Tests.AI;

/// <summary>
/// <see cref="CodexAppServerClient"/> の停止・破棄の分離を検証するテストクラス。
/// ハンドシェイク失敗時のクリーンアップ（<c>StopProcessAsync</c>）は再接続できる状態を残す必要があり、
/// 書き込みロックまで破棄すると次回送信が <see cref="ObjectDisposedException"/> で必ず失敗する
/// （＝UI の「再確認」を何度押しても復帰できない）ため、実プロセスなしで構造を守る
/// </summary>
public class CodexAppServerClientLifetimeTests
{
    /// <summary>プロセス停止のクリーンアップでは書き込みロックを破棄しない（＝再接続できる）ことを検証する</summary>
    [Fact(DisplayName = "プロセス停止のクリーンアップは書き込みロックを破棄しない")]
    public async Task StopProcessAsync_KeepsWriteLockUsable()
    {
        var client = new CodexAppServerClient();

        await client.StopProcessAsync();

        var writeLock = GetWriteLock(client);
        writeLock
            .Wait(0, TestContext.Current.CancellationToken)
            .Should()
            .BeTrue("再接続後の送信でロックを取得できる必要がある");
        writeLock.Release();
        client.IsStarted.Should().BeFalse();
    }

    /// <summary>最終破棄では書き込みロックまで破棄し、二重呼び出しでも例外にならないことを検証する</summary>
    [Fact(DisplayName = "最終破棄は書き込みロックを破棄し二重呼び出しでも安全")]
    public async Task DisposeAsync_DisposesWriteLockAndIsIdempotent()
    {
        var client = new CodexAppServerClient();

        await client.DisposeAsync();
        var secondDispose = async () => await client.DisposeAsync();

        await secondDispose.Should().NotThrowAsync();
        var writeLock = GetWriteLock(client);
        var wait = () => writeLock.Wait(0);
        wait.Should().Throw<ObjectDisposedException>();
    }

    /// <summary>破棄済みインスタンスの再起動は分かる例外で弾かれることを検証する</summary>
    [Fact(DisplayName = "破棄済みインスタンスの起動は ObjectDisposedException")]
    public async Task StartAsync_AfterDispose_Throws()
    {
        var client = new CodexAppServerClient();
        await client.DisposeAsync();

        var act = async () =>
            await client.StartAsync(
                new CodexAppServerSettings(),
                "erdesigner",
                "QuickER",
                "1.0.0",
                TestContext.Current.CancellationToken
            );

        await act.Should().ThrowAsync<ObjectDisposedException>();
    }

    /// <summary>クリーンアップ時に応答待ちリクエストが接続断で即座に解消されることを検証する</summary>
    /// <remarks>解消しないとタイムアウト（30 秒）まで呼び出し側が待たされる</remarks>
    [Fact(DisplayName = "プロセス停止で応答待ちリクエストは接続断として解消される")]
    public async Task StopProcessAsync_FailsPendingRequests()
    {
        var client = new CodexAppServerClient();
        var pending = new TaskCompletionSource<JsonElement?>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        GetPendingRequests(client)[1] = pending;

        await client.StopProcessAsync();

        var act = async () => await pending.Task;
        (await act.Should().ThrowAsync<InvalidOperationException>())
            .Which.Message.Should()
            .Be(AiStrings.Codex_ConnectionClosed);
        GetPendingRequests(client).Should().BeEmpty();
    }

    /// <summary>
    /// 受信ループが stdout の EOF で終わったとき、接続断イベントが 1 回だけ発火することを検証する。
    /// </summary>
    /// <remarks>
    /// 応答待ちリクエストが無い状態でプロセスが落ちると <c>FailPendingRequests</c> だけでは誰にも伝わらず、
    /// エンジン側の実行中ターンが固着する。二度目の終了で重ねて発火しないことも同時に固定する
    /// </remarks>
    [Fact(DisplayName = "受信ループの EOF は接続断を 1 回だけ通知する")]
    public async Task ReadLoop_OnEndOfStream_RaisesDisconnectedOnce()
    {
        var client = new CodexAppServerClient();
        using var readerCts = new CancellationTokenSource();
        SetField(client, "_readerCts", readerCts);
        var raised = 0;
        client.Disconnected += (_, _) => raised++;

        await InvokeReadLoopAsync(client, readerCts.Token);

        raised.Should().Be(1);

        await InvokeReadLoopAsync(client, readerCts.Token);

        raised.Should().Be(1, "1 接続につき接続断の通知は 1 回だけ");
    }

    /// <summary>意図的な停止（受信ループがキャンセル済み）では接続断を通知しないことを検証する</summary>
    /// <remarks>StopAsync / DisposeAsync のたびに接続断が出ると、正常終了が事故として通知される</remarks>
    [Fact(DisplayName = "キャンセル済みの受信ループ終了では接続断を通知しない")]
    public async Task ReadLoop_WhenCancelled_DoesNotRaiseDisconnected()
    {
        var client = new CodexAppServerClient();
        using var readerCts = new CancellationTokenSource();
        await readerCts.CancelAsync();
        SetField(client, "_readerCts", readerCts);
        var raised = 0;
        client.Disconnected += (_, _) => raised++;

        await InvokeReadLoopAsync(client, readerCts.Token);

        raised.Should().Be(0);
    }

    /// <summary>公開 API の停止も内部クリーンアップと同じく再接続できる状態を残すことを検証する</summary>
    [Fact(DisplayName = "StopAsync は再接続できる停止である")]
    public async Task StopAsync_KeepsWriteLockUsable()
    {
        var client = new CodexAppServerClient();

        await client.StopAsync();

        var writeLock = GetWriteLock(client);
        writeLock
            .Wait(0, TestContext.Current.CancellationToken)
            .Should()
            .BeTrue("停止後の再接続で送信ロックを取得できる必要がある");
        writeLock.Release();
        client.IsStarted.Should().BeFalse();
    }

    /// <summary>空の stdout（＝即 EOF）で受信ループを 1 回まわす</summary>
    private static async Task InvokeReadLoopAsync(
        CodexAppServerClient client,
        CancellationToken cancellationToken
    )
    {
        using var reader = new StreamReader(new MemoryStream());
        var method = typeof(CodexAppServerClient).GetMethod(
            "ReadLoopAsync",
            BindingFlags.NonPublic | BindingFlags.Instance
        )!;
        await (Task)method.Invoke(client, [reader, cancellationToken])!;
    }

    /// <summary>実プロセスを起動せず受信ループの前提（キャンセルソース）を整えるため private フィールドへ代入する</summary>
    private static void SetField(CodexAppServerClient client, string name, object value) =>
        typeof(CodexAppServerClient)
            .GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(client, value);

    /// <summary>書き込みロック（private readonly フィールド）を取り出す</summary>
    private static SemaphoreSlim GetWriteLock(CodexAppServerClient client) =>
        (SemaphoreSlim)GetField(client, "_writeLock");

    /// <summary>応答待ちリクエスト表（private readonly フィールド）を取り出す</summary>
    private static ConcurrentDictionary<int, TaskCompletionSource<JsonElement?>> GetPendingRequests(
        CodexAppServerClient client
    ) =>
        (ConcurrentDictionary<int, TaskCompletionSource<JsonElement?>>)
            GetField(client, "_pendingRequests");

    /// <summary>実プロセスを起動せず内部状態を確認するため、private フィールドを名前で取り出す</summary>
    private static object GetField(CodexAppServerClient client, string name) =>
        typeof(CodexAppServerClient)
            .GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(client)!;
}
