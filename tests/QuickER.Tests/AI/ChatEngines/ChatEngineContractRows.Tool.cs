using AwesomeAssertions;
using QuickER.Tests.TestSupport.Engines;
using static QuickER.Tests.AI.ChatEngines.ChatEngineCapability;
using AiStrings = QuickER.AI.Resources.Strings;

namespace QuickER.Tests.AI.ChatEngines;

internal static partial class ChatEngineContractRows
{
    /// <summary>ツールの呼び出し（ChatToolInvocation の作法：例外は英語の失敗結果へ畳み、活動通知は購読側の例外から守る）</summary>
    private static IEnumerable<ScenarioRow<ChatContractScenario>> Tool() =>
        [
            Row(
                "tool-executes-and-returns-result",
                "ツールの要求をツールホストで実行し、結果をバックエンドへ返し、活動を通知する",
                ChatToolHostKind.Recording,
                async c =>
                {
                    await c.Rig.RequestToolAsync(BookCall, c.CancellationToken);

                    var call = c.Rig.Recording!.Calls.Should().ContainSingle().Subject;
                    call.Tool.Should().Be("add_entity");
                    call.Args.Should().Be(BookCall.ArgumentsJson);

                    var activity = c.Rig.Events.Activities.Should().ContainSingle().Subject;
                    activity.ToolName.Should().Be("add_entity");
                    activity.Success.Should().BeTrue();

                    var toolReturn = c.SingleToolReturn(true);

                    if (toolReturn.Id is not null)
                    {
                        toolReturn.Id.Should().Be(BookCall.Id);
                    }

                    c.Rig.Events.Completions.Should().OnlyContain(completion => completion.Success);
                }
            ),
            Row(
                "tool-host-throws-is-english-failure-result",
                "ツールホストの例外は英語の失敗結果へ畳んでバックエンドへ返し、ターンを詰まらせない",
                ChatToolHostKind.Throwing,
                async c =>
                {
                    await c.Rig.RequestToolAsync(BookCall, c.CancellationToken);

                    const string expected = "The tool 'add_entity' threw an exception: boom";
                    var activity = c.Rig.Events.Activities.Should().ContainSingle().Subject;
                    activity.Success.Should().BeFalse();
                    activity.Result.Should().Be(expected);

                    c.SingleToolReturn(false).Result.Should().Be(expected);
                    c.Rig.Events.Completions.Should().OnlyContain(completion => completion.Success);
                }
            ),
            Row(
                "tool-without-host-is-failure-result",
                "ツールホストの無いエンジンへのツールの要求は失敗の結果を返す",
                ChatToolHostKind.None,
                async c =>
                {
                    await c.Rig.RequestToolAsync(BookCall, c.CancellationToken);

                    c.SingleToolReturn(false);
                },
                OptionalToolHost
            ),
            Row(
                "tool-response-send-failure-is-reported-as-status",
                "ツールの結果の送信に失敗したら、その理由を状態の文で知らせる",
                ChatToolHostKind.Recording,
                async c =>
                {
                    await c.Rig.PrepareForToolsAsync(c.CancellationToken);
                    c.Rig.FailToolResponses(new InvalidOperationException("切断されました"));
                    var mark = c.Rig.Events.StatusMark;

                    await c.Rig.RequestToolAsync(BookCall, c.CancellationToken);

                    c.Rig.Events.StatusesSince(mark)
                        .Should()
                        .ContainSingle()
                        .Which.Should()
                        .Be(c.Adapter.ExpectedToolResponseSendFailed("切断されました"));
                },
                ToolResponseChannel
            ),
            Row(
                "tool-activity-subscriber-throws-still-returns-result",
                "活動通知の購読側が例外を投げても、実行した結果をバックエンドへ 1 件だけ返し、状態の文で知らせる",
                ChatToolHostKind.Recording,
                async c =>
                {
                    c.Rig.Engine.ToolActivityReceived += (_, _) =>
                        throw new InvalidOperationException("UI が落ちた");

                    await c.Rig.RequestToolAsync(BookCall, c.CancellationToken);

                    c.Rig.Recording!.Calls.Should().ContainSingle();
                    c.SingleToolReturn(true)
                        .Result.Should()
                        .NotContain("not executed", "実行済みの呼び出しへ合成結果を返さない");
                    c.Rig.Events.Statuses.Should()
                        .Contain(
                            string.Format(AiStrings.Chat_ToolActivityNotifyFailed, "UI が落ちた")
                        );
                    c.Rig.Events.Completions.Should().OnlyContain(completion => completion.Success);
                }
            ),
        ];

    /// <summary>ツールの呼び出しの入力（元のテストの add_entity）</summary>
    private static ChatStep.ToolCall BookCall =>
        new("call_1", "add_entity", "{\"table_name\":\"Book\"}");
}
