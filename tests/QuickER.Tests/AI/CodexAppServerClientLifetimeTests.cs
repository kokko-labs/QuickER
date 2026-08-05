using System.Collections.Concurrent;
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
        writeLock.Wait(0).Should().BeTrue("再接続後の送信でロックを取得できる必要がある");
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
