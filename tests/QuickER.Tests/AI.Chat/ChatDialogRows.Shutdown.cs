using AwesomeAssertions;
using QuickER.Tests.TestSupport.Engines;
using static QuickER.Tests.AI.Chat.ChatDialogOp;
using static QuickER.Tests.AI.Chat.ChatDialogState;

namespace QuickER.Tests.AI.Chat;

internal static partial class ChatDialogRows
{
    /// <summary>ShutdownEngines（全エンジンの破棄・ハングしても上限で戻る）</summary>
    private static IEnumerable<ScenarioRow<ChatDialogCase>> ShutdownRows() =>
        [
            Row(
                "shutdown-disposes-all-engines",
                "ShutdownEngines は全エンジンを破棄する",
                [(Idle, Shutdown)],
                h =>
                {
                    var claudeCode = new DisposeTrackingClaudeCodeClient();
                    h.ClaudeCode = claudeCode;
                    var codex = h.Codex;
                    var copilot = h.Copilot;
                    var vm = h.Create();

                    // 破棄はスレッドプールで走る。全件の並列実行でプールが詰まっても届くまで待てる上限にする
                    // （届いた時点で戻るので遅くはならない。上限で戻ること自体は「ハングしても上限で戻る」が見る）
                    vm.ShutdownEngines(TimeSpan.FromSeconds(60));

                    codex.DisposeCount.Should().Be(1, "codex app-server は常駐するため破棄が要る");
                    claudeCode.DisposeCount.Should().Be(1);
                    copilot
                        .DisposeCount.Should()
                        .Be(1, "copilot ランタイムは常駐するため破棄が要る");
                }
            ),
            Row(
                "shutdown-hanging-dispose-returns-within-timeout",
                "ShutdownEngines は破棄がハングしても上限で戻る",
                [(Idle, Shutdown)],
                h =>
                {
                    h.ClaudeCode = new DisposeTrackingClaudeCodeClient();
                    var codex = h.Codex;
                    var copilot = h.Copilot;
                    var vm = h.Create();
                    var gate = new TaskCompletionSource();

                    try
                    {
                        codex.DisposeGate = gate.Task;

                        // 戻らない実装（上限なしの待ち）ではテストがハングするため、別スレッドで呼んで上限つきで観測する
                        var shutdown = Task.Run(
                            () => vm.ShutdownEngines(TimeSpan.FromMilliseconds(200)),
                            TestContext.Current.CancellationToken
                        );
                        var returned = false;
                        var wait = () =>
                        {
                            returned = shutdown.Wait(TimeSpan.FromSeconds(10));
                        };

                        wait.Should().NotThrow("終了経路はエンジンの破棄の失敗で妨げられない");
                        returned.Should().BeTrue("ハングした破棄でアプリの終了を止めない");

                        // ハングと並行して走った破棄の完了は、待ちの打ち切りより後になり得るため上限つきで待つ
                        SpinWait
                            .SpinUntil(() => copilot.DisposeCount == 1, TimeSpan.FromSeconds(10))
                            .Should()
                            .BeTrue("1 つのハングが他のエンジンの破棄を止めない");
                    }
                    finally
                    {
                        // 組の終わりの ShutdownEngines が詰まらないようにゲートを開ける
                        gate.SetResult();
                    }
                }
            ),
        ];
}
