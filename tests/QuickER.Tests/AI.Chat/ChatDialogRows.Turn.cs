using AwesomeAssertions;
using QuickER.AI;
using QuickER.AI.Chat;
using QuickER.Tests.TestSupport.Engines;
using static QuickER.Tests.AI.Chat.ChatDialogOp;
using static QuickER.Tests.AI.Chat.ChatDialogState;
using ChatStrings = QuickER.AI.Chat.Resources.Strings;

namespace QuickER.Tests.AI.Chat;

internal static partial class ChatDialogRows
{
    /// <summary>会話の開始・送信・ターンの終わり・メッセージ・ストリーミング・自動整列・構築</summary>
    private static IEnumerable<ScenarioRow<ChatDialogCase>> TurnRows() =>
        [
            Row(
                "turn-constructs-with-chat-host-stub-without-main-view-model",
                "MainViewModel 無しでも IErDiagramChatHost 注入で構築できる",
                [(Idle, Turn)],
                h =>
                {
                    var host = new RecordingChatHost();
                    h.Host = host;

                    var vm = h.Create();

                    // ツール実行 seam は host 抽象から取得され、MainViewModel 具象には依存しない
                    vm.Should().NotBeNull();
                    host.ToolHostAccessed.Should().BeTrue();
                }
            ),
            Row(
                "turn-has-conversation-only-with-user-or-assistant-message",
                "HasConversation はユーザー／アシスタント発言で true になる",
                [(Idle, Turn), (Conversation, Turn)],
                h =>
                {
                    var vm = h.Create();

                    vm.HasConversation.Should().BeFalse();

                    // システムメッセージだけでは会話とみなさない
                    vm.Messages.Add(
                        new ErChatMessage { Role = ErChatMessageRole.System, Content = "案内" }
                    );
                    vm.HasConversation.Should().BeFalse();

                    vm.Messages.Add(
                        new ErChatMessage { Role = ErChatMessageRole.User, Content = "こんにちは" }
                    );
                    vm.HasConversation.Should().BeTrue();
                }
            ),
            Row(
                "turn-clear-conversation-empties-messages",
                "ClearConversation は会話を空にする",
                [(Conversation, Turn)],
                h =>
                {
                    var vm = h.Create();
                    vm.Messages.Add(
                        new ErChatMessage { Role = ErChatMessageRole.User, Content = "こんにちは" }
                    );
                    vm.Messages.Add(
                        new ErChatMessage { Role = ErChatMessageRole.Assistant, Content = "どうぞ" }
                    );

                    vm.ClearConversation();

                    vm.Messages.Should().BeEmpty();
                    vm.HasConversation.Should().BeFalse();
                }
            ),
            Row(
                "turn-copilot-model-selection-flows-into-session-options",
                "Copilot のモデル選択がセッション生成へ渡る",
                [(Idle, Connect), (Conversation, Turn)],
                h =>
                {
                    var copilot = h.Copilot;
                    var vm = h.Create();

                    vm.Connection.SelectedBackend = ErChatBackendKind.Copilot;
                    vm.Connection.CopilotModel = "claude-sonnet-4.5";

                    vm.StartConversationCommand.Execute(null);

                    copilot.LastSessionOptions.Should().NotBeNull();
                    copilot.LastSessionOptions!.Model.Should().Be("claude-sonnet-4.5");
                }
            ),
            Row(
                "turn-copilot-interrupt-calls-runtime-abort",
                "Copilot の中断がランタイムへ伝わる",
                [(TurnInProgress, Turn)],
                h =>
                {
                    var copilot = h.Copilot;
                    var vm = h.Create();

                    vm.Connection.SelectedBackend = ErChatBackendKind.Copilot;
                    vm.StartConversationCommand.Execute(null);
                    vm.UserInput = "本のテーブルを作って";
                    vm.SendMessageCommand.Execute(null);

                    vm.InterruptCommand.Execute(null);

                    copilot.AbortCallCount.Should().Be(1);

                    // 中断によるアイドル復帰はエラーではなく「中断」として完了する
                    copilot.RaiseIdle(aborted: true);
                    vm.IsTurnInProgress.Should().BeFalse();
                }
            ),
            Row(
                "turn-start-conversation-while-starting-blocks-send-start-and-switch",
                "会話の開始を待つ間は送信・新しい会話・タブ切替を止める",
                [(Starting, Turn), (Starting, SwitchBackend)],
                async h =>
                {
                    var copilot = h.Copilot;
                    var vm = h.Create();

                    vm.Connection.SelectedBackend = ErChatBackendKind.Copilot;
                    await vm.StartConversationCommand.ExecuteAsync(null);
                    vm.UserInput = "次の質問";
                    vm.CanSendMessage.Should().BeTrue();

                    var gate = new TaskCompletionSource();
                    copilot.StartSessionGate = gate.Task;
                    var starting = vm.StartConversationCommand.ExecuteAsync(null);

                    vm.CanSendMessage.Should().BeFalse();
                    vm.SendMessageCommand.CanExecute(null).Should().BeFalse();
                    vm.CanStartConversation.Should().BeFalse();
                    h.TryChangeBackend(vm, ErChatBackendKind.ApiKey).Should().BeFalse();
                    vm.Connection.SelectedBackend.Should().Be(ErChatBackendKind.Copilot);
                    vm.StatusMessage.Should()
                        .Be(ChatStrings.Chat_SwitchBackendBlockedWhileStarting);

                    gate.SetResult();
                    await starting;

                    vm.CanSendMessage.Should().BeTrue();
                    vm.CanStartConversation.Should().BeTrue();
                }
            ),
            Row(
                "turn-starting-allows-api-provider-change",
                "特性の固定（文書に定めが無い今の振る舞い）：会話の開始を待つ間も API キー接続のプロバイダーは変えられ、開始は Copilot のまま終わって送信できる",
                [(Starting, ChangeProvider)],
                async h =>
                {
                    // 根拠：src/QuickER.AI.Chat/AiChatDialogViewModel.cs:118（CanEditConnectionSettings はターンの実行中だけを見る）
                    var vm = h.Create();
                    var (starting, gate) = h.BeginCopilotStart(vm);

                    vm.CanEditConnectionSettings.Should().BeTrue();
                    vm.Connection.ApiProvider = AiProvider.LocalLlm;

                    gate.SetResult();
                    await starting;

                    vm.Connection.ApiProvider.Should().Be(AiProvider.LocalLlm);
                    vm.Connection.SelectedBackend.Should().Be(ErChatBackendKind.Copilot);
                    vm.UserInput = "本のテーブルを作って";
                    vm.CanSendMessage.Should().BeTrue();
                }
            ),
            Row(
                "turn-starting-allows-copilot-recheck",
                "特性の固定（文書に定めが無い今の振る舞い）：会話の開始を待つ間も Copilot の再確認は走ってモデルの一覧を取り直し、開始は終わって送信できる",
                [(Starting, Connect)],
                async h =>
                {
                    // 根拠：src/QuickER.AI.UI/BackendConnectionChecks.cs:54-64（RunAsync に開始待ちの門が無い）
                    var copilot = h.Copilot;
                    var vm = h.Create();
                    var (starting, gate) = h.BeginCopilotStart(vm);
                    copilot.Models = ["recheck-model"];

                    await vm.CopilotRefreshCommand.ExecuteAsync(null);

                    vm.Connection.CopilotAvailableModels.Should().Equal("recheck-model");

                    gate.SetResult();
                    await starting;

                    vm.UserInput = "本のテーブルを作って";
                    vm.CanSendMessage.Should().BeTrue();
                }
            ),
            Row(
                "turn-tool-activity-after-assistant-text-starts-new-tool-bubble",
                "アシスタントの文を挟んだあとのツール活動は新しい吹き出しに出る",
                [(TurnInProgress, Turn)],
                h =>
                {
                    var copilot = h.Copilot;
                    var vm = h.Create();
                    ChatDialogHarness.StartCopilotTurn(vm);

                    copilot.RaiseToolCall("r1", "add_entity", "{}");
                    copilot.RaiseDelta("テーブルを追加しました。");
                    copilot.RaiseToolCall("r2", "add_column", "{}");

                    vm.Messages.Select(m => m.Role)
                        .Should()
                        .EndWith([
                            ErChatMessageRole.User,
                            ErChatMessageRole.ToolCall,
                            ErChatMessageRole.Assistant,
                            ErChatMessageRole.ToolCall,
                        ]);
                }
            ),
            .. new[] { "completed", "failed", "aborted" }.Select(outcome =>
                Row(
                    $"turn-assistant-messages-stop-streaming-when-turn-ends-{outcome}",
                    "回答の応答中の印はターンの終わり（完了・失敗・中断）で外れる",
                    [(TurnInProgress, Turn)],
                    h =>
                    {
                        var copilot = h.Copilot;
                        var vm = h.Create();
                        ChatDialogHarness.StartCopilotTurn(vm);

                        copilot.RaiseDelta("テーブルを");
                        copilot.RaiseToolCall("r1", "add_entity", "{}");
                        copilot.RaiseDelta("追加しました。");

                        var answers = vm
                            .Messages.Where(m => m.Role == ErChatMessageRole.Assistant)
                            .ToList();
                        answers.Should().HaveCount(2);
                        answers
                            .Should()
                            .OnlyContain(m => m.IsStreaming, "ターンが終わるまでは応答中");

                        switch (outcome)
                        {
                            case "completed":
                                copilot.RaiseIdle();
                                break;

                            case "failed":
                                copilot.RaiseError("session failed");
                                break;

                            default:
                                copilot.RaiseIdle(aborted: true);
                                break;
                        }

                        vm.IsTurnInProgress.Should().BeFalse();
                        answers.Should().OnlyContain(m => !m.IsStreaming);
                    }
                )
            ),
            Row(
                "turn-turn-in-progress-off-stops-streaming",
                "実行中フラグが外れると回答の応答中の印も外れる",
                [(TurnInProgress, Turn)],
                h =>
                {
                    var vm = h.Create();
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
                "turn-failed-turn-from-empty-diagram-arranges-created-tables",
                "空の図から始まったターンが失敗してもテーブルができていれば自動整列する",
                [(TurnInProgress, Turn)],
                h =>
                {
                    var host = new RecordingChatHost { IsEmpty = true };
                    h.Host = host;
                    var copilot = h.Copilot;
                    var vm = h.Create();

                    ChatDialogHarness.StartCopilotTurn(vm);
                    host.IsEmpty = false;

                    copilot.RaiseError("session failed");

                    host.AutoArrangeCount.Should().Be(1);
                }
            ),
            Row(
                "turn-interrupted-turn-from-empty-diagram-arranges-created-tables",
                "空の図から始まったターンを中断してもテーブルができていれば自動整列する",
                [(TurnInProgress, Turn)],
                h =>
                {
                    var host = new RecordingChatHost { IsEmpty = true };
                    h.Host = host;
                    var copilot = h.Copilot;
                    var vm = h.Create();

                    ChatDialogHarness.StartCopilotTurn(vm);
                    host.IsEmpty = false;

                    vm.InterruptCommand.Execute(null);
                    copilot.RaiseIdle(aborted: true);

                    host.AutoArrangeCount.Should().Be(1);
                }
            ),
        ];
}
