using AwesomeAssertions;
using QuickER.Tests.TestSupport.Engines;
using static QuickER.Tests.AI.Mock.Dialog.MockDialogOp;
using static QuickER.Tests.AI.Mock.Dialog.MockDialogState;

namespace QuickER.Tests.AI.Mock.Dialog;

internal static partial class MockDialogRows
{
    /// <summary>終了の経路（中断の要求・エンジンの破棄・切り離した破棄の待ち）</summary>
    private static IEnumerable<ScenarioRow<MockDialogCase>> ShutdownRows() =>
        [
            Row(
                "shutdown-request-interrupt-stops-running-generation",
                "終了の経路の中断の要求は、実行中でなければ何もせず、生成の実行中なら取り消して生成器へ中断を渡す",
                [(Conversation, Shutdown), (GenRun, Shutdown)],
                async h =>
                {
                    var vm = h.Create();
                    await h.PrepareGenerationAsync(vm);

                    vm.RequestInterrupt();
                    h.Generator.InterruptCount.Should().Be(0, "実行中でなければ生成器に触らない");

                    var run = h.StartHeldGeneration(vm);
                    vm.RequestInterrupt();

                    h.Generator.InterruptCount.Should().Be(1);
                    h.Generator.CapturedToken.IsCancellationRequested.Should().BeTrue();

                    h.Generator.ResultSuccess = false;
                    h.Generator.ResultInterrupted = true;
                    h.Generator.Release();
                    await run;
                    vm.IsMockGenInProgress.Should().BeFalse();
                }
            ),
            Row(
                "shutdown-request-interrupt-reaches-turn-engine",
                "終了の経路の中断の要求は、ターンの実行中ならセッションのエンジンへ中断を渡す",
                [(Turn, Shutdown)],
                async h =>
                {
                    var vm = h.CreateApiKeyReady();
                    var engine = await h.StartHeldTurnAsync(vm);

                    vm.RequestInterrupt();

                    engine.InterruptCount.Should().Be(1);
                    h.Generator.InterruptCount.Should().Be(0, "生成していなければ生成器に触らない");
                }
            ),
            Row(
                "shutdown-disposes-session-engine",
                "エンジンの破棄は会話中のセッションのエンジンを破棄する",
                [(Conversation, Shutdown)],
                h =>
                {
                    var vm = h.CreateApiKeyReady();
                    vm.StartConversationCommand.Execute(null);
                    h.Engine.DisposeCount.Should().Be(0, "会話中はまだ破棄されない");

                    // 破棄はスレッドプールで走る。全件の並列実行でプールが詰まっても届くまで待てる上限にする
                    h.ShutdownEngines(vm, TimeSpan.FromSeconds(60));

                    h.Engine.DisposeCount.Should().Be(1);
                }
            ),
            Row(
                "shutdown-waits-for-detached-session-disposal",
                "会話を切り替えた直後に閉じても、切り離した旧セッションの破棄（門で止めた）を待ってから戻る",
                [(Conversation, Shutdown)],
                async h =>
                {
                    var vm = h.CreateApiKeyReady();
                    using var gate = new ManualResetEventSlim(false);

                    try
                    {
                        vm.StartConversationCommand.Execute(null);
                        var oldEngine = h.Engine;
                        oldEngine.DisposeGate = gate;

                        vm.StartConversationCommand.Execute(null);
                        oldEngine
                            .DisposeStarted.Wait(
                                TimeSpan.FromSeconds(30),
                                TestContext.Current.CancellationToken
                            )
                            .Should()
                            .BeTrue("切替で旧セッションの破棄が始まること");

                        var shutdown = Task.Run(
                            () => h.ShutdownEngines(vm, TimeSpan.FromSeconds(30)),
                            TestContext.Current.CancellationToken
                        );

                        // 門が開くまで終了処理は戻らない（＝旧セッションの破棄を待っている）
                        var finishedEarly = await Task.WhenAny(
                            shutdown,
                            Task.Delay(300, TestContext.Current.CancellationToken)
                        );
                        finishedEarly
                            .Should()
                            .NotBeSameAs(
                                shutdown,
                                "切り離し済みセッションの破棄が終わるまで待つこと"
                            );

                        gate.Set();
                        await shutdown;

                        oldEngine
                            .DisposeCount.Should()
                            .Be(1, "切り離した旧セッションも破棄されること");
                    }
                    finally
                    {
                        gate.Set();
                    }
                }
            ),
            Row(
                "shutdown-waits-for-every-detached-disposal-after-repeated-switches",
                "続けて会話を切り替えた後に閉じても、切り離したすべてのセッション（最初の 1 つを門で止めた）の破棄を待つ",
                [(Conversation, Shutdown)],
                async h =>
                {
                    var vm = h.CreateApiKeyReady();
                    using var gate = new ManualResetEventSlim(false);

                    try
                    {
                        vm.StartConversationCommand.Execute(null);
                        var first = h.Engine;
                        first.DisposeGate = gate;

                        vm.StartConversationCommand.Execute(null);
                        first
                            .DisposeStarted.Wait(
                                TimeSpan.FromSeconds(30),
                                TestContext.Current.CancellationToken
                            )
                            .Should()
                            .BeTrue("1 回目の切替で最初のセッションの破棄が始まること");

                        var second = h.Engine;
                        second.Should().NotBeSameAs(first);
                        vm.StartConversationCommand.Execute(null);

                        var shutdown = Task.Run(
                            () => h.ShutdownEngines(vm, TimeSpan.FromSeconds(30)),
                            TestContext.Current.CancellationToken
                        );

                        var finishedEarly = await Task.WhenAny(
                            shutdown,
                            Task.Delay(300, TestContext.Current.CancellationToken)
                        );
                        finishedEarly
                            .Should()
                            .NotBeSameAs(shutdown, "最初に切り離したセッションの破棄も待つこと");

                        gate.Set();
                        await shutdown;

                        first.DisposeCount.Should().Be(1, "最初に切り離した分も破棄されること");
                        second.DisposeCount.Should().Be(1, "次に切り離した分も破棄されること");
                        h.Engine.DisposeCount.Should().Be(1, "現在のセッションも破棄されること");
                    }
                    finally
                    {
                        gate.Set();
                    }
                }
            ),
            Row(
                "shutdown-without-conversation-does-not-throw",
                "会話を開始していなくても終了の経路は例外を投げない",
                [(Ready, Shutdown)],
                h =>
                {
                    var vm = h.CreateApiKeyReady();

                    var shutdown = () => h.ShutdownEngines(vm, TimeSpan.FromSeconds(5));

                    shutdown.Should().NotThrow();
                }
            ),
            Row(
                "shutdown-on-fresh-dialog-touches-nothing",
                "作った直後（モックフォルダなし）の終了の経路は、中断の要求も破棄も何にも触らず例外を投げない",
                [(Idle, Shutdown)],
                h =>
                {
                    var vm = h.Create();

                    var close = () =>
                    {
                        vm.RequestInterrupt();
                        h.ShutdownEngines(vm, TimeSpan.FromSeconds(5));
                    };

                    close.Should().NotThrow();
                    h.Generator.InterruptCount.Should().Be(0);
                    h.Engines.Should().BeEmpty();
                    vm.DetachedDisposalForTests.Should().BeNull();
                }
            ),
        ];
}
