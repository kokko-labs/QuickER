using AwesomeAssertions;
using QuickER.Tests.TestSupport.Engines;
using static QuickER.Tests.AI.ChatEngines.ChatEngineCapability;

namespace QuickER.Tests.AI.ChatEngines;

internal static partial class ChatEngineContractRows
{
    /// <summary>取り消しと中断（利用者の中断だけを Error の無い失敗＝無音の中断にする）</summary>
    private static IEnumerable<ScenarioRow<ChatContractScenario>> Cancel() =>
        [
            Row(
                "cancel-caller-token-is-silent-failure",
                "送信の途中で呼び出し元のトークンが取り消されたら、Error の無い失敗で 1 回完了する",
                ChatToolHostKind.None,
                async c =>
                {
                    await c.Rig.PlayTurnAsync(
                        "やあ",
                        ChatTurnScript.Of(new ChatTurnEnding.CallerCancels()),
                        c.CancellationToken
                    );

                    c.SingleCompletion(false, null);
                }
            ),
            Row(
                "cancel-foreign-token-is-failure-with-reason",
                "呼び出し元のトークンが立っていない取り消し（タイムアウトなど）は、中断でなく失敗の文で 1 回完了する",
                ChatToolHostKind.None,
                async c =>
                {
                    var timeout = new TaskCanceledException("HttpClient timeout");

                    await c.Rig.PlayTurnAsync(
                        "やあ",
                        ChatTurnScript.Of(new ChatTurnEnding.ForeignCancellation(timeout)),
                        c.CancellationToken
                    );

                    c.SingleCompletion(false, c.Adapter.ExpectedFailureFromException(timeout));
                }
            ),
            Row(
                "cancel-user-interrupt-is-silent-failure",
                "利用者がターンの途中で中断したら、Error の無い失敗で 1 回完了する",
                ChatToolHostKind.None,
                async c =>
                {
                    await c.Rig.PlayTurnAsync(
                        "やあ",
                        ChatTurnScript.Of(new ChatTurnEnding.UserInterrupts()),
                        c.CancellationToken
                    );

                    c.SingleCompletion(false, null);
                }
            ),
            Row(
                "cancel-user-interrupt-then-backend-throws-is-silent-failure",
                "中断を受けたバックエンドが取り消しの例外で終えても、Error の無い失敗で 1 回完了する",
                ChatToolHostKind.None,
                async c =>
                {
                    await c.Rig.PlayTurnAsync(
                        "やあ",
                        ChatTurnScript.Of(new ChatTurnEnding.UserInterrupts(BackendThrows: true)),
                        c.CancellationToken
                    );

                    c.SingleCompletion(false, null);
                },
                TurnRunsInsideSend
            ),
            Row(
                "cancel-interrupt-during-turn-reaches-backend",
                "実行中のターンの中断はバックエンドへ 1 回届く",
                ChatToolHostKind.None,
                async c =>
                {
                    await c.Rig.PlayTurnAsync(
                        "やあ",
                        ChatTurnScript.Of(new ChatTurnEnding.Open()),
                        c.CancellationToken
                    );

                    await c.Rig.Engine.InterruptAsync(c.CancellationToken);

                    await c.EventuallyAsync(
                        () => c.Rig.InterruptsReceived >= 1,
                        "中断がバックエンドへ届く"
                    );
                    c.Rig.InterruptsReceived.Should().Be(1);
                }
            ),
        ];
}
