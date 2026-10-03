using AwesomeAssertions;
using QuickER.AI;

namespace QuickER.Tests.AI;

/// <summary>
/// <see cref="SingleFlight"/>（接続・初期化を重ねて走らせない共有）の検証。
/// 共有するのは実行中のあいだだけで、完了した処理（成功・失敗・取り消し）は次の呼び出しでやり直すことを固定する。
/// </summary>
public class SingleFlightTests
{
    /// <summary>実行中に重ねて呼ぶと、処理は 1 回だけ起動し、両方の呼び出しが同じ完了を待つことを検証する</summary>
    [Fact(DisplayName = "実行中に重ねて呼んでも処理は 1 回だけ起動する")]
    public async Task RunAsync_WhileRunning_SharesTheRunningTask()
    {
        var flight = new SingleFlight();
        var gate = new TaskCompletionSource();
        var starts = 0;

        Task Start(CancellationToken _)
        {
            starts++;
            return gate.Task;
        }

        var first = flight.RunAsync(Start, TestContext.Current.CancellationToken);
        var second = flight.RunAsync(Start, TestContext.Current.CancellationToken);

        starts.Should().Be(1);
        second.IsCompleted.Should().BeFalse();

        gate.SetResult();
        await first;
        await second;

        starts.Should().Be(1);
    }

    /// <summary>
    /// 1 回目が失敗した後の呼び出しは、失敗した処理を返し続けず、処理をやり直すことを検証する
    /// （失敗した接続を保持し続けると、以後の再試行がずっと同じ失敗を返す）。
    /// </summary>
    [Fact(DisplayName = "1 回目が失敗した後の 2 回目は再試行される")]
    public async Task RunAsync_AfterFailure_StartsAgain()
    {
        var flight = new SingleFlight();
        var starts = 0;

        Task Start(CancellationToken _)
        {
            starts++;
            return starts == 1
                ? Task.FromException(new InvalidOperationException("first fails"))
                : Task.CompletedTask;
        }

        var first = () => flight.RunAsync(Start, TestContext.Current.CancellationToken);
        await first.Should().ThrowAsync<InvalidOperationException>();

        await flight.RunAsync(Start, TestContext.Current.CancellationToken);

        starts.Should().Be(2);
    }

    /// <summary>同期的に投げる起動も失敗した処理として扱い、次の呼び出しで再試行することを検証する</summary>
    [Fact(DisplayName = "起動が同期的に投げても次の呼び出しで再試行される")]
    public async Task RunAsync_AfterSynchronousThrow_StartsAgain()
    {
        var flight = new SingleFlight();
        var starts = 0;

        Task Start(CancellationToken _)
        {
            starts++;

            if (starts == 1)
            {
                throw new InvalidOperationException("sync");
            }

            return Task.CompletedTask;
        }

        var first = () => flight.RunAsync(Start, TestContext.Current.CancellationToken);
        await first.Should().ThrowAsync<InvalidOperationException>();

        await flight.RunAsync(Start, TestContext.Current.CancellationToken);

        starts.Should().Be(2);
    }

    /// <summary>取り消しで終わった処理も保持せず、次の呼び出しで再試行することを検証する</summary>
    [Fact(DisplayName = "取り消しで終わった後の呼び出しは再試行される")]
    public async Task RunAsync_AfterCancellation_StartsAgain()
    {
        var flight = new SingleFlight();
        var starts = 0;

        Task Start(CancellationToken token)
        {
            starts++;
            return Task.Delay(Timeout.Infinite, token);
        }

        using var cts = new CancellationTokenSource();
        var first = flight.RunAsync(Start, cts.Token);
        await cts.CancelAsync();
        var awaitFirst = () => first;
        await awaitFirst.Should().ThrowAsync<OperationCanceledException>();

        using var second = new CancellationTokenSource();
        var next = flight.RunAsync(Start, second.Token);
        starts.Should().Be(2);
        await second.CancelAsync();
        var awaitNext = () => next;
        await awaitNext.Should().ThrowAsync<OperationCanceledException>();
    }

    /// <summary>成功した処理も保持せず、次の呼び出しは処理をやり直すことを検証する（結果をキャッシュしない）</summary>
    [Fact(DisplayName = "成功した後の呼び出しは処理をやり直す")]
    public async Task RunAsync_AfterSuccess_StartsAgain()
    {
        var flight = new SingleFlight();
        var starts = 0;

        Task Start(CancellationToken _)
        {
            starts++;
            return Task.CompletedTask;
        }

        await flight.RunAsync(Start, TestContext.Current.CancellationToken);
        await flight.RunAsync(Start, TestContext.Current.CancellationToken);

        starts.Should().Be(2);
    }

    /// <summary>
    /// 相乗りした呼び出しの取り消しは、その呼び出しの待ちだけをやめ、共有している処理は取り消さないことを検証する。
    /// </summary>
    [Fact(DisplayName = "相乗りした呼び出しの取り消しは共有している処理を止めない")]
    public async Task RunAsync_JoinerCancellation_DoesNotCancelSharedWork()
    {
        var flight = new SingleFlight();
        var gate = new TaskCompletionSource();

        var first = flight.RunAsync(_ => gate.Task, TestContext.Current.CancellationToken);

        using var cts = new CancellationTokenSource();
        var joiner = flight.RunAsync(_ => Task.CompletedTask, cts.Token);
        await cts.CancelAsync();

        var awaitJoiner = () => joiner;
        await awaitJoiner.Should().ThrowAsync<OperationCanceledException>();
        first.IsCompleted.Should().BeFalse("相乗りした側の取り消しで共有の処理は止まらない");

        gate.SetResult();
        await first;
    }
}
