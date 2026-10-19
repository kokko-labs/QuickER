using System;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using QuickER.Tests.GeneratedBinaryFixture;
using Xunit;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>
/// リモートの要求の印（<see cref="UnboundedBinaryColumns.EnterRemoteRequest"/>）が、入れ子・例外・並行の要求・呼び出し元へ
/// 漏れないことを検証する（印が漏れると、直結の EF Core の更新が除外列を書かなくなる）。
/// </summary>
public sealed class RemoteRequestMarkRuntimeTests
{
    [Fact(DisplayName = "リモートの要求の印: 破棄すると外の状態へ戻り、2 回破棄しても変わらない")]
    public void Dispose_RestoresOuterState()
    {
        UnboundedBinaryColumns.InRemoteRequest.Should().BeFalse();

        var mark = UnboundedBinaryColumns.EnterRemoteRequest();
        UnboundedBinaryColumns.InRemoteRequest.Should().BeTrue();

        mark.Dispose();
        UnboundedBinaryColumns.InRemoteRequest.Should().BeFalse();

        mark.Dispose();
        UnboundedBinaryColumns.InRemoteRequest.Should().BeFalse();
    }

    [Fact(DisplayName = "リモートの要求の印: 入れ子の内側を破棄しても外側の印は残る")]
    public void Nested_InnerDisposeKeepsOuter()
    {
        using (UnboundedBinaryColumns.EnterRemoteRequest())
        {
            using (UnboundedBinaryColumns.EnterRemoteRequest())
            {
                UnboundedBinaryColumns.InRemoteRequest.Should().BeTrue();
            }

            UnboundedBinaryColumns.InRemoteRequest.Should().BeTrue();
        }

        UnboundedBinaryColumns.InRemoteRequest.Should().BeFalse();
    }

    [Fact(DisplayName = "リモートの要求の印: 要求の中で例外が出ても印は残らない")]
    public void Exception_DoesNotLeaveMark()
    {
        var act = () =>
        {
            using (UnboundedBinaryColumns.EnterRemoteRequest())
            {
                throw new InvalidOperationException("boom");
            }
        };

        act.Should().Throw<InvalidOperationException>();
        UnboundedBinaryColumns.InRemoteRequest.Should().BeFalse();
    }

    [Fact(
        DisplayName = "リモートの要求の印: 非同期のメソッドの中で立てた印は、呼び出し元へ戻らない"
    )]
    public async Task MarkSetInAsyncMethod_DoesNotFlowBackToCaller()
    {
        await EnterWithoutDisposingAsync();

        UnboundedBinaryColumns.InRemoteRequest.Should().BeFalse();
    }

    [Fact(DisplayName = "リモートの要求の印: 同時に走る別の流れには見えない")]
    public async Task Mark_IsNotSeenByConcurrentFlow()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var observed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var seenByOther = true;

        var request = Task.Run(
            async () =>
            {
                using (UnboundedBinaryColumns.EnterRemoteRequest())
                {
                    entered.SetResult();
                    await observed.Task;
                    UnboundedBinaryColumns.InRemoteRequest.Should().BeTrue();
                }
            },
            TestContext.Current.CancellationToken
        );
        var other = Task.Run(
            async () =>
            {
                await entered.Task;
                seenByOther = UnboundedBinaryColumns.InRemoteRequest;
                observed.SetResult();
            },
            TestContext.Current.CancellationToken
        );

        await Task.WhenAll(request, other);

        seenByOther.Should().BeFalse();
    }

    /// <summary>印を立てたまま戻る（破棄し忘れた要求の形）</summary>
    private static async Task EnterWithoutDisposingAsync()
    {
        UnboundedBinaryColumns.EnterRemoteRequest();
        await Task.Yield();
        UnboundedBinaryColumns.InRemoteRequest.Should().BeTrue();
    }
}
