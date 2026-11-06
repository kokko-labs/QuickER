using AwesomeAssertions;
using QuickER.Tests.TestSupport.Engines;
using static QuickER.Tests.AI.ChatEngines.ChatEngineCapability;

namespace QuickER.Tests.AI.ChatEngines;

internal static partial class ChatEngineContractRows
{
    /// <summary>ターンの流れ</summary>
    private static IEnumerable<ScenarioRow<ChatContractScenario>> Turn() =>
        [
            Row(
                "turn-streams-deltas-then-completes",
                "差分を順に流し、最後の差分の後に成功で 1 回だけ完了する",
                ChatToolHostKind.None,
                async c =>
                {
                    await c.Rig.PlayTurnAsync(
                        "やあ",
                        ChatTurnScript.Of(
                            new ChatTurnEnding.Success(),
                            new ChatStep.Delta("こん"),
                            new ChatStep.Delta("にちは")
                        ),
                        c.CancellationToken
                    );

                    c.Rig.Events.Log.Should().Equal("delta:こん", "delta:にちは", "completed:ok");
                    c.SingleCompletion(true, null);
                }
            ),
            Row(
                "turn-stray-completion-after-turn-is-dropped",
                "ターンが終わった後に届いたターンの完了の通知は流さない（完了は 1 回）",
                ChatToolHostKind.None,
                async c =>
                {
                    await c.Rig.PlayTurnAsync(
                        "やあ",
                        ChatTurnScript.Of(new ChatTurnEnding.Success()),
                        c.CancellationToken
                    );
                    c.Rig.RaiseStrayCompletion();

                    c.Rig.Events.Completions.Should().ContainSingle();
                },
                AsyncCompletion
            ),
            Row(
                "turn-stray-completion-before-any-turn-is-dropped",
                "ターンを始める前に届いたターンの完了の通知は流さない",
                ChatToolHostKind.Recording,
                c =>
                {
                    c.Rig.RaiseStrayCompletion();

                    c.Rig.Events.Completions.Should().BeEmpty();
                    return Task.CompletedTask;
                },
                AsyncCompletion
            ),
        ];
}
