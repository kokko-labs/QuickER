using AwesomeAssertions;
using QuickER.AI;
using QuickER.AI.Chat;
using QuickER.Tests.TestDoubles;
using QuickER.Tests.TestSupport.Engines;
using static QuickER.Tests.AI.Chat.ChatDialogOp;
using static QuickER.Tests.AI.Chat.ChatDialogState;
using ChatStrings = QuickER.AI.Chat.Resources.Strings;

namespace QuickER.Tests.AI.Chat;

internal static partial class ChatDialogRows
{
    /// <summary>接続方式の切替と可否（TryChangeBackend・タブ・Initialize・派生の可否）</summary>
    private static IEnumerable<ScenarioRow<ChatDialogCase>> BackendRows() =>
        [
            Row(
                "backend-default-is-api-key",
                "既定の接続方式は API キーである",
                [(Idle, SwitchBackend)],
                h =>
                {
                    var vm = h.Create();

                    vm.Connection.SelectedBackend.Should().Be(ErChatBackendKind.ApiKey);
                    vm.Connection.IsApiKeyBackend.Should().BeTrue();
                    vm.Connection.IsCodexBackend.Should().BeFalse();
                }
            ),
            Row(
                "backend-switch-during-turn-is-rejected-without-confirm",
                "ターン実行中のタブ切替は拒否され確認も出ない",
                [(TurnInProgress, SwitchBackend)],
                h =>
                {
                    var dialogs = new StubDialogService();
                    h.Dialogs = dialogs;
                    var vm = h.Create();
                    vm.IsTurnInProgress = true;

                    var result = h.TryChangeBackend(vm, ErChatBackendKind.Codex);

                    result.Should().BeFalse();
                    vm.Connection.SelectedBackend.Should().Be(ErChatBackendKind.ApiKey);
                    dialogs.ConfirmMessages.Should().BeEmpty("ターン実行中は確認より前に拒否する");
                    vm.StatusMessage.Should().Be(ChatStrings.Chat_SwitchBackendBlockedDuringTurn);
                }
            ),
            Row(
                "backend-openai-requires-api-key-to-start",
                "OpenAI 接続は API キー入力で会話開始可能になる",
                [(Idle, ChangeProvider)],
                h =>
                {
                    var vm = h.Create();

                    vm.Connection.ApiProvider = AiProvider.OpenAI;
                    vm.Connection.ApiKey = string.Empty;
                    vm.CanStartConversation.Should().BeFalse();

                    vm.Connection.ApiKey = "sk-test";
                    vm.CanStartConversation.Should().BeTrue();
                }
            ),
            Row(
                "backend-local-llm-needs-no-key",
                "Local LLM 接続は API キー未入力でも会話開始可能",
                [(Idle, ChangeProvider)],
                h =>
                {
                    var vm = h.Create();

                    vm.Connection.ApiProvider = AiProvider.LocalLlm;
                    vm.CanStartConversation.Should().BeTrue();
                    vm.Connection.ShowEndpoint.Should().BeTrue();

                    // キー欄は表示される（ローカル LLM でもキーは任意で受け付ける）
                    vm.Connection.ShowApiKey.Should().BeTrue();
                }
            ),
            Row(
                "backend-send-requires-started-conversation-and-input",
                "送信は会話開始済みかつ入力ありのときのみ可能",
                [(Idle, ChangeProvider)],
                h =>
                {
                    var vm = h.Create();

                    vm.Connection.ApiProvider = AiProvider.LocalLlm;
                    vm.UserInput = "本のテーブルを作って";

                    // 会話未開始のうちは送信不可
                    vm.CanSendMessage.Should().BeFalse();
                }
            ),
            Row(
                "backend-switch-to-codex-updates-flags",
                "Codex へ切り替えると IsCodexBackend が true になる",
                [(Idle, SwitchBackend)],
                h =>
                {
                    var vm = h.Create();

                    vm.Connection.SelectedBackend = ErChatBackendKind.Codex;
                    vm.Connection.IsCodexBackend.Should().BeTrue();
                    vm.Connection.IsApiKeyBackend.Should().BeFalse();
                }
            ),
            Row(
                "backend-switch-without-conversation-skips-confirm",
                "会話なしなら確認せず接続方式を切り替える",
                [(Idle, SwitchBackend)],
                h =>
                {
                    var dialogs = new StubDialogService();
                    h.Dialogs = dialogs;
                    var vm = h.Create();

                    var result = h.TryChangeBackend(vm, ErChatBackendKind.Codex);

                    result.Should().BeTrue();
                    vm.Connection.SelectedBackend.Should().Be(ErChatBackendKind.Codex);
                    dialogs.ConfirmMessages.Should().BeEmpty();
                }
            ),
            Row(
                "backend-switch-with-conversation-confirmed-clears-and-switches",
                "会話あり＋確認OKならクリアして切り替える",
                [(Conversation, SwitchBackend)],
                h =>
                {
                    var dialogs = new StubDialogService { ConfirmResult = true };
                    h.Dialogs = dialogs;
                    var vm = h.Create();
                    vm.Messages.Add(
                        new ErChatMessage { Role = ErChatMessageRole.User, Content = "こんにちは" }
                    );

                    var result = h.TryChangeBackend(vm, ErChatBackendKind.Codex);

                    result.Should().BeTrue();
                    vm.Connection.SelectedBackend.Should().Be(ErChatBackendKind.Codex);
                    vm.HasConversation.Should().BeFalse();
                    dialogs.ConfirmMessages.Should().ContainSingle();
                }
            ),
            Row(
                "backend-switch-with-conversation-cancelled-keeps-backend-and-conversation",
                "会話あり＋確認キャンセルなら切り替えず会話を維持する",
                [(Conversation, SwitchBackend)],
                h =>
                {
                    var dialogs = new StubDialogService { ConfirmResult = false };
                    h.Dialogs = dialogs;
                    var vm = h.Create();
                    vm.Messages.Add(
                        new ErChatMessage { Role = ErChatMessageRole.User, Content = "こんにちは" }
                    );

                    var result = h.TryChangeBackend(vm, ErChatBackendKind.Codex);

                    result.Should().BeFalse();
                    vm.Connection.SelectedBackend.Should().Be(ErChatBackendKind.ApiKey);
                    vm.HasConversation.Should().BeTrue();
                    dialogs.ConfirmMessages.Should().ContainSingle();
                }
            ),
            Row(
                "backend-start-conversation-is-disabled-during-turn",
                "ターン実行中は新しい会話を開始できない",
                [(Idle, ChangeProvider), (TurnInProgress, Turn)],
                h =>
                {
                    var vm = h.Create();

                    // API キー無しでも会話開始可能なローカル LLM で readiness を整える
                    vm.Connection.ApiProvider = AiProvider.LocalLlm;
                    vm.CanStartConversation.Should().BeTrue();
                    vm.StartConversationCommand.CanExecute(null).Should().BeTrue();

                    vm.IsTurnInProgress = true;

                    vm.CanStartConversation.Should().BeFalse();
                    vm.StartConversationCommand.CanExecute(null).Should().BeFalse();

                    vm.IsTurnInProgress = false;

                    vm.CanStartConversation.Should().BeTrue();
                    vm.StartConversationCommand.CanExecute(null).Should().BeTrue();
                }
            ),
            Row(
                "backend-interrupt-command-stays-enabled-during-turn",
                "ターン実行中も中断コマンドは有効のまま",
                [(TurnInProgress, Turn)],
                h =>
                {
                    var vm = h.Create();
                    vm.IsTurnInProgress = true;

                    vm.InterruptCommand.CanExecute(null).Should().BeTrue();
                }
            ),
            Row(
                "backend-api-key-change-notifies-can-start-conversation",
                "Connection.ApiKey 変更で親の CanStartConversation が通知される",
                [(Idle, ChangeProvider)],
                h =>
                {
                    var vm = h.Create();
                    vm.Connection.ApiProvider = AiProvider.OpenAI;

                    var raised = new List<string>();
                    vm.PropertyChanged += (_, e) =>
                    {
                        if (e.PropertyName is not null)
                        {
                            raised.Add(e.PropertyName);
                        }
                    };

                    vm.Connection.ApiKey = "sk-test";

                    raised.Should().Contain(nameof(AiChatDialogViewModel.CanStartConversation));
                }
            ),
            Row(
                "backend-connection-settings-not-editable-during-turn",
                "ターン実行中は接続設定を編集できない",
                [(TurnInProgress, ChangeProvider)],
                h =>
                {
                    var vm = h.Create();

                    vm.CanEditConnectionSettings.Should().BeTrue();

                    vm.IsTurnInProgress = true;
                    vm.CanEditConnectionSettings.Should().BeFalse();

                    vm.IsTurnInProgress = false;
                    vm.CanEditConnectionSettings.Should().BeTrue();
                }
            ),
            Row(
                "backend-can-change-backend-matches-rejection",
                "切り替えられない各状態でタブの可否が偽になり TryChangeBackend も確認なしで断る",
                [(TurnInProgress, SwitchBackend), (Starting, SwitchBackend)],
                async h =>
                {
                    var dialogs = new StubDialogService { ConfirmResult = true };
                    h.Dialogs = dialogs;
                    var vm = h.Create();

                    vm.CanChangeBackend.Should().BeTrue("待機中は切り替えられる");

                    // ターン実行中
                    vm.Messages.Add(
                        new ErChatMessage { Role = ErChatMessageRole.User, Content = "q" }
                    );
                    vm.IsTurnInProgress = true;
                    vm.CanChangeBackend.Should().BeFalse("ターン実行中は切り替えられない");
                    h.TryChangeBackend(vm, ErChatBackendKind.Codex).Should().BeFalse();
                    vm.IsTurnInProgress = false;
                    vm.CanChangeBackend.Should().BeTrue();

                    // 会話の開始中
                    vm.Connection.SelectedBackend = ErChatBackendKind.Copilot;
                    var gate = new TaskCompletionSource();
                    h.Copilot.StartSessionGate = gate.Task;
                    var starting = vm.StartConversationCommand.ExecuteAsync(null);
                    vm.Messages.Add(
                        new ErChatMessage { Role = ErChatMessageRole.User, Content = "q" }
                    );
                    vm.CanChangeBackend.Should().BeFalse("会話の開始中は切り替えられない");
                    h.TryChangeBackend(vm, ErChatBackendKind.ApiKey).Should().BeFalse();

                    dialogs
                        .ConfirmMessages.Should()
                        .BeEmpty("切り替えられない状態では確認より前に断る");
                    vm.Connection.SelectedBackend.Should().Be(ErChatBackendKind.Copilot);

                    gate.SetResult();
                    await starting;
                    vm.CanChangeBackend.Should().BeTrue("会話の開始が終われば切り替えられる");
                }
            ),
            Row(
                "backend-can-change-backend-raises-property-changed",
                "タブの可否の変化はターンと会話の開始の出入りで通知される",
                [(TurnInProgress, SwitchBackend), (Starting, SwitchBackend)],
                async h =>
                {
                    var vm = h.Create();

                    var raised = 0;
                    vm.PropertyChanged += (_, e) =>
                    {
                        if (e.PropertyName == nameof(AiChatDialogViewModel.CanChangeBackend))
                        {
                            raised++;
                        }
                    };

                    vm.IsTurnInProgress = true;
                    vm.IsTurnInProgress = false;
                    raised.Should().Be(2);

                    vm.Connection.SelectedBackend = ErChatBackendKind.Copilot;
                    var gate = new TaskCompletionSource();
                    h.Copilot.StartSessionGate = gate.Task;
                    var starting = vm.StartConversationCommand.ExecuteAsync(null);
                    raised.Should().Be(3);
                    gate.SetResult();
                    await starting;
                    raised.Should().Be(4);
                }
            ),
            Row(
                "backend-initialize-switches-to-initial-backend-and-starts-check",
                "Initialize は前回の接続方式へ切り替えてタブへ中継し接続確認を起動する",
                [(Idle, SwitchBackend), (Idle, Persist), (Idle, Connect)],
                h =>
                {
                    var first = h.Create();
                    h.TryChangeBackend(first, ErChatBackendKind.Codex).Should().BeTrue();
                    first.SaveSettings();

                    var client = new FakeCodexAppServerClient();
                    h.Codex = client;
                    var restored = h.Create();
                    var relayed = new List<ErChatBackendKind>();
                    restored.BackendSelection.PropertyChanged += (_, _) =>
                        relayed.Add(restored.BackendSelection.Selected);

                    restored
                        .Connection.SelectedBackend.Should()
                        .Be(ErChatBackendKind.ApiKey, "生成時には切り替えない");
                    client.StartCount.Should().Be(0, "生成時には接続確認を起動しない");

                    restored.Initialize();

                    restored.Connection.SelectedBackend.Should().Be(ErChatBackendKind.Codex);
                    relayed.Should().Equal(ErChatBackendKind.Codex);
                    client.StartCount.Should().Be(1, "切り替えた方式の接続確認を起動する");
                }
            ),
            Row(
                "backend-codex-provider-change-notifies-auth-presentation",
                "Codex のプロバイダー変更で状態ドットとログアウトの表示が通知される",
                [(Idle, ChangeProvider)],
                h =>
                {
                    var vm = h.Create();

                    var raised = new List<string>();
                    vm.PropertyChanged += (_, e) =>
                    {
                        if (e.PropertyName is not null)
                        {
                            raised.Add(e.PropertyName);
                        }
                    };

                    vm.Connection.CodexModelProvider = "notify-test-provider";

                    raised
                        .Should()
                        .Contain([
                            nameof(AiChatDialogViewModel.CodexStatusLevel),
                            nameof(AiChatDialogViewModel.ShowCodexLogout),
                            nameof(AiChatDialogViewModel.ShowCodexLoginPanel),
                        ]);
                }
            ),
        ];
}
