using AwesomeAssertions;
using QuickER.Tests.TestSupport.Engines;
using static QuickER.Tests.AI.ChatEngines.ChatEngineCapability;

namespace QuickER.Tests.AI.ChatEngines;

internal static partial class ChatEngineContractRows
{
    /// <summary>会話（送信が会話を開く・続きのターンは同じ会話・開き直し・接続断の後）</summary>
    private static IEnumerable<ScenarioRow<ChatContractScenario>> Conversation() =>
        [
            Row(
                "conversation-send-without-start-opens-one",
                "会話を開かずに送ったら、送信が会話を 1 つ開いてプロンプトを届ける",
                ChatToolHostKind.None,
                async c =>
                {
                    await c.Rig.PlayTurnAsync(
                        "やあ",
                        ChatTurnScript.Of(new ChatTurnEnding.Success()),
                        c.CancellationToken,
                        openConversation: false
                    );

                    c.Rig.ConversationsOpened.Should().Be(1);
                    c.Rig.PromptsReceived.Should().Equal("やあ");
                    c.SingleCompletion(true, null);
                }
            ),
            Row(
                "conversation-second-turn-continues",
                "2 ターン目は同じ会話を続ける（会話を開き直さない）",
                ChatToolHostKind.None,
                async c =>
                {
                    await c.Rig.PlayTurnAsync(
                        "1 回目",
                        ChatTurnScript.Of(new ChatTurnEnding.Success()),
                        c.CancellationToken
                    );
                    await c.Rig.PlayTurnAsync(
                        "2 回目",
                        ChatTurnScript.Of(new ChatTurnEnding.Success()),
                        c.CancellationToken
                    );

                    c.Rig.ConversationsOpened.Should().Be(1);
                    c.Rig.PromptsReceived.Should().Equal("1 回目", "2 回目");
                }
            ),
            Row(
                "conversation-restart-opens-new",
                "会話を開き直した後のターンは新しい会話になる",
                ChatToolHostKind.None,
                async c =>
                {
                    await c.Rig.PlayTurnAsync(
                        "1 回目",
                        ChatTurnScript.Of(new ChatTurnEnding.Success()),
                        c.CancellationToken
                    );
                    await c.Rig.RestartConversationAsync(c.CancellationToken);
                    await c.Rig.PlayTurnAsync(
                        "2 回目",
                        ChatTurnScript.Of(new ChatTurnEnding.Success()),
                        c.CancellationToken
                    );

                    c.Rig.ConversationsOpened.Should().Be(2);
                    c.Rig.PromptsReceived.Should().Equal("1 回目", "2 回目");
                }
            ),
            Row(
                "conversation-reopens-after-connection-lost",
                "ターンの外で接続が切れた後の送信は、接続し直して会話を開き直す",
                ChatToolHostKind.None,
                async c =>
                {
                    await c.Rig.PlayTurnAsync(
                        "1 回目",
                        ChatTurnScript.Of(new ChatTurnEnding.Success()),
                        c.CancellationToken
                    );
                    c.Rig.LoseConnection();
                    await c.Rig.PlayTurnAsync(
                        "2 回目",
                        ChatTurnScript.Of(new ChatTurnEnding.Success()),
                        c.CancellationToken
                    );

                    c.Rig.ConversationsOpened.Should().Be(2);
                    c.Rig.PromptsReceived.Should().Equal("1 回目", "2 回目");
                    c.Rig.Readiness.RuntimeStarted.Should().BeTrue();
                },
                ConnectionCanDrop
            ),
        ];
}
