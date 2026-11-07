using AwesomeAssertions;
using QuickER.AI;
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
            Row(
                "shutdown-force-close-steps-during-turn-interrupt-save-and-dispose",
                "ターンの実行中に ForceClose の段（中断の要求 → 設定の保存 → エンジンの破棄）を流すと、中断を 1 回要求し、設定を保存し、全エンジンを破棄する",
                [(TurnInProgress, Shutdown)],
                h =>
                {
                    // 根拠：AiChatDialog.ForceClose の XmlDoc「(1) 実行中のターンを打ち切り (2) エンジンを破棄して常駐する子プロセスまで止める」
                    var claudeCode = (DisposeTrackingClaudeCodeClient)h.ClaudeCode;
                    var codex = h.Codex;
                    var copilot = h.Copilot;
                    var vm = h.Create();
                    ChatDialogHarness.StartCopilotTurn(vm);

                    ChatDialogHarness.RunForceCloseSteps(vm);

                    copilot.AbortCallCount.Should().Be(1, "ターン中は中断を要求する");
                    codex.DisposeCount.Should().Be(1);
                    claudeCode.DisposeCount.Should().Be(1);
                    copilot.DisposeCount.Should().Be(1);

                    h.Copilot = new FakeCopilotRuntimeClient();
                    h.Create().Connection.InitialBackend.Should().Be(ErChatBackendKind.Copilot);
                }
            ),
            Row(
                "shutdown-force-close-steps-in-conversation-dispose-without-interrupt",
                "会話を開いた状態（ターンの外）で ForceClose の段を流すと、中断は要求せず、全エンジンを破棄し、会話の表示は残す",
                [(Conversation, Shutdown)],
                h =>
                {
                    // 根拠：RequestInterrupt の XmlDoc（実行中のターンを打ち切る。ターンの外では何もしない）と ShutdownEngines の XmlDoc
                    var claudeCode = (DisposeTrackingClaudeCodeClient)h.ClaudeCode;
                    var codex = h.Codex;
                    var copilot = h.Copilot;
                    var vm = h.Create();
                    ChatDialogHarness.StartCopilotTurn(vm);
                    copilot.RaiseIdle();
                    vm.IsTurnInProgress.Should().BeFalse();
                    var messages = vm.Messages.Count;

                    ChatDialogHarness.RunForceCloseSteps(vm);

                    copilot.AbortCallCount.Should().Be(0, "ターンの外では中断を要求しない");
                    codex.DisposeCount.Should().Be(1);
                    claudeCode.DisposeCount.Should().Be(1);
                    copilot.DisposeCount.Should().Be(1);
                    vm.Messages.Should().HaveCount(messages);
                }
            ),
            Row(
                "shutdown-while-starting-returns-and-disposes-all-engines",
                "会話の開始を待つ間の破棄は、開始の完了を待たずに戻り、全エンジンを破棄する",
                [(Starting, Shutdown)],
                async h =>
                {
                    // 根拠：ShutdownEngines の XmlDoc（4 つのエンジンをすべて破棄する・上限を超えても戻る）
                    var claudeCode = (DisposeTrackingClaudeCodeClient)h.ClaudeCode;
                    var codex = h.Codex;
                    var copilot = h.Copilot;
                    var vm = h.Create();
                    var (starting, gate) = h.BeginCopilotStart(vm);

                    vm.ShutdownEngines(TimeSpan.FromSeconds(60));

                    starting.IsCompleted.Should().BeFalse("破棄は止めた開始を待たない");
                    codex.DisposeCount.Should().Be(1);
                    claudeCode.DisposeCount.Should().Be(1);
                    copilot.DisposeCount.Should().Be(1);

                    // 特性の固定（文書に定めが無い今の振る舞い）：破棄の後に門を開けても、開始は例外なく終わる
                    // （根拠：src/QuickER.AI/CopilotChatEngine.cs:216-242 は破棄済みかを見ずに StartSessionAsync の完了を受ける）
                    gate.SetResult();
                    await starting;
                }
            ),
        ];
}
