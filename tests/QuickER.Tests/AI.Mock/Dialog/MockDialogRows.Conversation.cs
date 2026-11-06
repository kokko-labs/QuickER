using System.IO;
using AwesomeAssertions;
using QuickER.AI;
using QuickER.AI.Mock;
using QuickER.Model;
using QuickER.Tests.TestSupport.Engines;
using static QuickER.Tests.AI.Mock.Dialog.MockDialogOp;
using static QuickER.Tests.AI.Mock.Dialog.MockDialogState;
using MockStrings = QuickER.AI.Mock.Resources.Strings;

namespace QuickER.Tests.AI.Mock.Dialog;

internal static partial class MockDialogRows
{
    /// <summary>会話の開始・送信・ターンの終わり・初回ターン・応答中の印・生成中の会話</summary>
    private static IEnumerable<ScenarioRow<MockDialogCase>> ConversationRows() =>
        [
            Row(
                "converse-send-requires-started-conversation-and-input",
                "会話の開始前は送信できず、開始後は入力があれば送信でき、入力が空なら送信できない",
                [(Ready, Converse), (Conversation, Converse)],
                h =>
                {
                    var vm = h.CreateApiKeyReady();

                    vm.UserInput = "シンプルな管理画面で";
                    vm.CanSendMessage.Should().BeFalse();

                    vm.StartConversationCommand.Execute(null);
                    vm.CanSendMessage.Should().BeTrue();

                    vm.UserInput = string.Empty;
                    vm.CanSendMessage.Should().BeFalse();
                }
            ),
            Row(
                "converse-start-new-creates-manifest-and-first-prompt-has-schema",
                "新しいフォルダで開始すると mock.json ができ、初回のプロンプトにスキーマと要望が入る",
                [(Ready, Converse), (Conversation, Converse)],
                async h =>
                {
                    var vm = h.CreateApiKeyReady();

                    vm.StartConversationCommand.Execute(null);
                    h.Engines.Should().ContainSingle();
                    File.Exists(Path.Combine(h.MockFolder, MockManifest.ManifestFileName))
                        .Should()
                        .BeTrue();

                    vm.UserInput = "モダンな配色にして";
                    await vm.SendMessageCommand.ExecuteAsync(null);

                    h.Engine.SentPrompts.Should().ContainSingle();
                    h.Engine.SentPrompts[0].Should().Contain("Customer");
                    h.Engine.SentPrompts[0].Should().Contain("モダンな配色にして");
                    vm.UserInput.Should().BeEmpty();
                }
            ),
            Row(
                "converse-start-resume-first-prompt-has-screen-list",
                "既存のモックフォルダで開始すると初回のプロンプトが再開のプロンプト（画面一覧入り）になる",
                [(Ready, Converse), (Conversation, Converse)],
                async h =>
                {
                    var vm = h.CreateApiKeyReady(setMockFolder: false);
                    h.SeedMockFolder();
                    vm.MockFolder = h.MockFolder;

                    vm.StartConversationCommand.Execute(null);
                    vm.UserInput = "続きをお願い";
                    await vm.SendMessageCommand.ExecuteAsync(null);

                    h.Engine.SentPrompts.Should().ContainSingle();
                    h.Engine.SentPrompts[0].Should().Contain("OrderList.html");
                    h.Engine.SentPrompts[0].Should().Contain("続きをお願い");
                }
            ),
            Row(
                "converse-second-send-is-raw-feedback",
                "2 回目の送信はスキーマを含まない生のフィードバックとして送る",
                [(Conversation, Converse)],
                async h =>
                {
                    var vm = h.CreateApiKeyReady();
                    vm.StartConversationCommand.Execute(null);

                    vm.UserInput = "初回の要望";
                    await vm.SendMessageCommand.ExecuteAsync(null);
                    vm.UserInput = "列を減らして";
                    await vm.SendMessageCommand.ExecuteAsync(null);

                    h.Engine.SentPrompts.Should().HaveCount(2);
                    h.Engine.SentPrompts[1].Should().Be("列を減らして");
                    h.Engine.SentPrompts[1].Should().NotContain("Customer");
                }
            ),
            .. new[] { ErChatBackendKind.Codex, ErChatBackendKind.Copilot }.Select(backend =>
                Row(
                    $"converse-{backend.ToString().ToLowerInvariant()}-first-success-then-feedback",
                    $"{backend} の会話も初回は開始のプロンプト、成功の後は生のフィードバックを送る",
                    [(Ready, Converse), (Conversation, Converse)],
                    async h =>
                    {
                        var vm = h.Create();
                        vm.Connection.SelectedBackend = backend;

                        if (backend == ErChatBackendKind.Codex)
                        {
                            vm.ApplyCodexReadiness(true, "ログイン済み", ConnectionHealth.Ready);
                        }
                        else
                        {
                            vm.ApplyCopilotReadiness(
                                true,
                                "ログイン済み",
                                ConnectionHealth.Ready,
                                string.Empty,
                                []
                            );
                        }

                        vm.MockFolder = h.MockFolder;
                        vm.StartConversationCommand.Execute(null);

                        vm.UserInput = "初回の要望";
                        await vm.SendMessageCommand.ExecuteAsync(null);
                        vm.UserInput = "列を減らして";
                        await vm.SendMessageCommand.ExecuteAsync(null);

                        h.Engines.Should().ContainSingle();
                        h.Engine.SentPrompts.Should().HaveCount(2);
                        h.Engine.SentPrompts[0].Should().Contain("Customer");
                        h.Engine.SentPrompts[1].Should().Be("列を減らして");
                    }
                )
            ),
            Row(
                "converse-send-disabled-during-turn",
                "ターンの実行中は送信できない",
                [(Conversation, Converse), (Turn, Converse)],
                async h =>
                {
                    var vm = h.CreateApiKeyReady();
                    vm.StartConversationCommand.Execute(null);
                    vm.UserInput = "要望";
                    vm.CanSendMessage.Should().BeTrue();
                    h.Engine.HoldTurnCompletion = true;

                    await vm.SendMessageCommand.ExecuteAsync(null);
                    vm.UserInput = "次の要望";

                    vm.IsTurnInProgress.Should().BeTrue();
                    vm.CanSendMessage.Should().BeFalse();
                }
            ),
            .. new[]
            {
                ("completed", true, (string?)null),
                ("failed", false, "session failed"),
                ("aborted", false, null),
            }.Select(c =>
                Row(
                    $"converse-streaming-ends-when-turn-{c.Item1}",
                    $"回答の応答中の印はターンの終わり（{c.Item1}）で外れる",
                    [(Turn, Converse)],
                    async h =>
                    {
                        var vm = h.CreateApiKeyReady();
                        var engine = await h.StartHeldTurnAsync(vm);
                        var answer = vm.Messages.Single(m => m.Role == ErChatMessageRole.Assistant);
                        answer.IsStreaming.Should().BeTrue("ターンが終わるまでは応答中");

                        if (c.Item2)
                        {
                            engine.CompleteHeldTurn();
                        }
                        else
                        {
                            engine.FailHeldTurn(c.Item3);
                        }

                        vm.IsTurnInProgress.Should().BeFalse();
                        answer.IsStreaming.Should().BeFalse();
                    }
                )
            ),
            Row(
                "converse-turn-flag-off-ends-streaming",
                "実行中の印が外れれば（送信の例外の経路を含め）回答の応答中の印も外れる",
                [(Turn, Converse)],
                h =>
                {
                    var vm = h.CreateApiKeyReady();
                    var answer = new ErChatMessage
                    {
                        Role = ErChatMessageRole.Assistant,
                        Content = "途中まで",
                        IsStreaming = true,
                    };
                    vm.Messages.Add(answer);
                    vm.IsTurnInProgress = true;

                    vm.IsTurnInProgress = false;

                    answer.IsStreaming.Should().BeFalse();
                }
            ),
            Row(
                "converse-can-change-backend-notified-at-turn-start-and-end",
                "接続方式のタブの可否の変化はターンの開始と終了で通知される",
                [(Ready, Converse)],
                h =>
                {
                    var vm = h.CreateApiKeyReady();
                    var raised = 0;
                    vm.PropertyChanged += (_, e) =>
                    {
                        if (
                            e.PropertyName == nameof(MockGenerationDialogViewModel.CanChangeBackend)
                        )
                        {
                            raised++;
                        }
                    };

                    vm.IsTurnInProgress = true;
                    vm.IsTurnInProgress = false;

                    raised.Should().Be(2);
                }
            ),
            Row(
                "converse-first-send-exception-retries-as-initial-prompt",
                "初回の送信が例外で終わったら、次の送信も初回（スキーマ入りの開始のプロンプト）としてやり直す",
                [(Conversation, Converse)],
                async h =>
                {
                    var vm = h.CreateApiKeyReady();
                    vm.StartConversationCommand.Execute(null);
                    h.Engine.SendException = new InvalidOperationException("boom");

                    vm.UserInput = "初回の要望";
                    await vm.SendMessageCommand.ExecuteAsync(null);

                    vm.IsTurnInProgress.Should().BeFalse();
                    h.Engine.SentPrompts.Should().BeEmpty();

                    h.Engine.SendException = null;
                    vm.UserInput = "再送";
                    await vm.SendMessageCommand.ExecuteAsync(null);

                    h.Engine.SentPrompts.Should().ContainSingle();
                    h.Engine.SentPrompts[0]
                        .Should()
                        .Contain("Customer", "やり直しも開始のプロンプト（スキーマ入り）で送る");
                    h.Engine.SentPrompts[0].Should().Contain("再送");
                }
            ),
            .. new[] { ("failed", (string?)"session failed"), ("interrupted", null) }.Select(c =>
                Row(
                    $"converse-first-turn-{c.Item1}-retries-as-initial-prompt",
                    $"初回のターンが {c.Item1} で終わったら次の送信も初回としてやり直し、成功の後の送信はフィードバックになる",
                    [(Turn, Converse), (Conversation, Converse)],
                    async h =>
                    {
                        var vm = h.CreateApiKeyReady();
                        vm.StartConversationCommand.Execute(null);
                        var engine = h.Engine;
                        engine.HoldTurnCompletion = true;

                        vm.UserInput = "初回の要望";
                        await vm.SendMessageCommand.ExecuteAsync(null);
                        engine.FailHeldTurn(c.Item2);

                        vm.IsTurnInProgress.Should().BeFalse();

                        vm.UserInput = "再送";
                        await vm.SendMessageCommand.ExecuteAsync(null);
                        engine.CompleteHeldTurn();

                        engine.SentPrompts.Should().HaveCount(2);
                        engine
                            .SentPrompts[1]
                            .Should()
                            .Contain(
                                "Customer",
                                "やり直しも開始のプロンプト（スキーマ入り）で送る"
                            );
                        engine.SentPrompts[1].Should().Contain("再送");

                        vm.UserInput = "列を減らして";
                        await vm.SendMessageCommand.ExecuteAsync(null);
                        engine.CompleteHeldTurn();

                        engine
                            .SentPrompts[2]
                            .Should()
                            .Be(
                                "列を減らして",
                                "初回のターンが成功した後はフィードバックとして送る"
                            );
                    }
                )
            ),
            Row(
                "converse-interrupt-command-reaches-engine",
                "ターンの実行中の中断はセッションのエンジンへ届き、中断の完了で実行中が解けて状態の文が中断になる",
                [(Turn, Converse)],
                async h =>
                {
                    var vm = h.CreateApiKeyReady();
                    var engine = await h.StartHeldTurnAsync(vm);
                    vm.InterruptCommand.CanExecute(null).Should().BeTrue();

                    await vm.InterruptCommand.ExecuteAsync(null);

                    engine.InterruptCount.Should().Be(1);
                    engine.FailHeldTurn(null);
                    vm.IsTurnInProgress.Should().BeFalse();
                    vm.StatusMessage.Should().Be(MockStrings.Mock_Interrupted);
                    vm.InterruptCommand.CanExecute(null).Should().BeFalse();
                }
            ),
            Row(
                "converse-restart-disposes-only-previous-session-engine",
                "会話の作り直しで旧セッションのエンジンだけが破棄され、新しいセッションのエンジンは生きている",
                [(Conversation, Converse)],
                async h =>
                {
                    var vm = h.CreateApiKeyReady();
                    vm.StartConversationCommand.Execute(null);
                    var oldEngine = h.Engine;

                    vm.StartConversationCommand.Execute(null);

                    vm.DetachedDisposalForTests.Should()
                        .NotBeNull("切替で旧セッションの破棄が始まること");
                    await vm.DetachedDisposalForTests!;
                    oldEngine
                        .DisposeCount.Should()
                        .Be(1, "差し替えられた旧セッションのエンジンは破棄されること");
                    h.Engine.Should()
                        .NotBeSameAs(oldEngine, "作り直しで新しいエンジンが生成されること");
                    h.Engine.DisposeCount.Should()
                        .Be(0, "新しいセッションのエンジンは破棄されないこと");
                }
            ),
            Row(
                "converse-refresh-diagram-state-notifies-start",
                "図の空・非空が変わった後の再評価（ダイアログが前面に戻ったとき）で会話の開始の可否が通知される",
                [(Ready, Converse)],
                h =>
                {
                    h.Diagram = new ErDiagram();
                    var vm = h.CreateApiKeyReady();
                    vm.StartConversationCommand.CanExecute(null).Should().BeFalse();

                    var raised = 0;
                    vm.StartConversationCommand.CanExecuteChanged += (_, _) => raised++;
                    h.Diagram.Entities.Add(new Entity { TableName = "Customer" });

                    vm.RefreshDiagramState();

                    raised.Should().BeGreaterThan(0);
                    vm.StartConversationCommand.CanExecute(null).Should().BeTrue();
                }
            ),
            Row(
                "converse-blocked-during-mock-generation-with-reason",
                "モックプロジェクトの生成中は会話の開始も送信もできず、止めている理由を出し、可否の変化を通知する",
                [(GenRun, Converse)],
                h =>
                {
                    var vm = h.CreateApiKeyReady();
                    vm.StartConversationCommand.Execute(null);
                    vm.UserInput = "要望";
                    vm.CanStartConversation.Should().BeTrue();
                    vm.CanSendMessage.Should().BeTrue();
                    vm.ConversationDisabledReason.Should().BeNull();

                    var startChanged = 0;
                    var sendChanged = 0;
                    vm.StartConversationCommand.CanExecuteChanged += (_, _) => startChanged++;
                    vm.SendMessageCommand.CanExecuteChanged += (_, _) => sendChanged++;

                    vm.IsMockGenInProgress = true;

                    vm.CanStartConversation.Should().BeFalse();
                    vm.CanSendMessage.Should().BeFalse();
                    vm.StartConversationCommand.CanExecute(null).Should().BeFalse();
                    vm.SendMessageCommand.CanExecute(null).Should().BeFalse();
                    vm.ConversationDisabledReason.Should()
                        .Be(MockStrings.Mock_ConversationBlockedDuringMockGen);
                    startChanged.Should().BeGreaterThan(0);
                    sendChanged.Should().BeGreaterThan(0);

                    vm.IsMockGenInProgress = false;

                    vm.StartConversationCommand.CanExecute(null).Should().BeTrue();
                    vm.SendMessageCommand.CanExecute(null).Should().BeTrue();
                    vm.ConversationDisabledReason.Should().BeNull();
                }
            ),
        ];
}
