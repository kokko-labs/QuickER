using System.Diagnostics;
using AwesomeAssertions;
using QuickER.AI.UI;

namespace QuickER.Tests.AI.UI;

/// <summary>
/// <see cref="EngineShutdown"/> が、終了経路でエンジンをまとめて破棄する作法
/// （全部を破棄する・1 つの失敗で他を止めない・ハングしても上限で戻る）を守ることを検証する。
/// </summary>
public class EngineShutdownTests
{
    /// <summary>破棄の回数を記録し、指定どおりに失敗・停止する破棄対象</summary>
    private sealed class ProbeDisposable : IAsyncDisposable
    {
        /// <summary>DisposeAsync が呼ばれた回数</summary>
        public int DisposeCount { get; private set; }

        /// <summary>DisposeAsync が呼ばれたことの合図（スレッドプールでの実行を待つ用）</summary>
        public ManualResetEventSlim Disposed { get; } = new(false);

        /// <summary>非 null なら DisposeAsync が同期的にこの例外を投げる</summary>
        public Exception? ThrowSynchronously { get; init; }

        /// <summary>非 null なら DisposeAsync がこのタスクの完了まで戻らない（破棄のハングを模擬する）</summary>
        public Task? Gate { get; init; }

        public ValueTask DisposeAsync()
        {
            DisposeCount++;
            Disposed.Set();

            if (ThrowSynchronously is not null)
            {
                throw ThrowSynchronously;
            }

            return Gate is null ? ValueTask.CompletedTask : new ValueTask(Gate);
        }
    }

    /// <summary>渡したものを全部破棄し、null の要素は無視することを検証する</summary>
    [Fact(DisplayName = "DisposeAll は渡したエンジンをすべて破棄し null は無視する")]
    public void DisposeAll_DisposesEveryEngine_IgnoringNull()
    {
        var first = new ProbeDisposable();
        var second = new ProbeDisposable();

        EngineShutdown.DisposeAll(TimeSpan.FromSeconds(5), first, null, second);

        first.DisposeCount.Should().Be(1);
        second.DisposeCount.Should().Be(1);
    }

    /// <summary>1 つの破棄が投げても、他のエンジンの破棄を止めず、例外も外へ出さないことを検証する</summary>
    [Fact(DisplayName = "DisposeAll は 1 つの破棄が失敗しても他を破棄し例外を出さない")]
    public void DisposeAll_OneFailure_DoesNotStopOthers()
    {
        var failing = new ProbeDisposable
        {
            ThrowSynchronously = new InvalidOperationException("dispose failed"),
        };
        var after = new ProbeDisposable();

        var act = () => EngineShutdown.DisposeAll(TimeSpan.FromSeconds(5), failing, after);

        act.Should().NotThrow();
        failing.DisposeCount.Should().Be(1);
        after.DisposeCount.Should().Be(1);
    }

    /// <summary>破棄がハングしても上限で戻り、他のエンジンの破棄は済んでいることを検証する</summary>
    [Fact(DisplayName = "DisposeAll は破棄がハングしても上限で戻る")]
    public void DisposeAll_HangingDisposal_ReturnsAtTimeout()
    {
        var gate = new TaskCompletionSource();
        var hanging = new ProbeDisposable { Gate = gate.Task };
        var other = new ProbeDisposable();

        try
        {
            var stopwatch = Stopwatch.StartNew();
            EngineShutdown.DisposeAll(TimeSpan.FromMilliseconds(200), hanging, other);
            stopwatch.Stop();

            // 上限で戻る（ハングした破棄の完了を待ち続けない）。余裕を持たせて判定する
            stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(10));

            // ハングした破棄に他の破棄が巻き込まれない。破棄はスレッドプールで走るため、
            // 全スイートの並列実行でプールが飽和しても落ちないよう、合図を長めの上限で待つ
            other
                .Disposed.Wait(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken)
                .Should()
                .BeTrue();
            hanging.DisposeCount.Should().Be(1);
        }
        finally
        {
            gate.SetResult();
        }
    }
}
