using AwesomeAssertions;
using QuickER.AI;
using QuickER.AI.Mock;
using QuickER.Tests.TestSupport.Engines;
using static QuickER.Tests.AI.Mock.Dialog.MockDialogOp;
using static QuickER.Tests.AI.Mock.Dialog.MockDialogState;
using AiStrings = QuickER.AI.Resources.Strings;
using AiUiStrings = QuickER.AI.UI.Resources.Strings;
using MockStrings = QuickER.AI.Mock.Resources.Strings;

namespace QuickER.Tests.AI.Mock.Dialog;

internal static partial class MockDialogRows
{
    /// <summary>接続方式の切替・接続の確認の反映・モデル・添付・設定の保存とモデルの履歴</summary>
    private static IEnumerable<ScenarioRow<MockDialogCase>> BackendRows() =>
        [
            Row(
                "backend-api-provider-change-notifies-backend-ready",
                "接続タブの API キーのプロバイダーを変えると、親の IsBackendReady の変更が通知される",
                [(Ready, Backend)],
                h =>
                {
                    var vm = h.CreateApiKeyReady();
                    vm.Connection.ApiProvider = AiProvider.OpenAI;
                    var raised = new List<string>();
                    vm.PropertyChanged += (_, e) =>
                    {
                        if (e.PropertyName is not null)
                        {
                            raised.Add(e.PropertyName);
                        }
                    };

                    vm.Connection.ApiProvider = AiProvider.LocalLlm;

                    raised.Should().Contain(nameof(MockGenerationDialogViewModel.IsBackendReady));
                }
            ),
            Row(
                "backend-attachment-support-claude-code-all-kinds-codex-none",
                "Claude Code の添付の範囲は全種別、Codex は添付なし",
                [(Ready, Backend)],
                h =>
                {
                    var vm = h.CreateApiKeyReady();

                    vm.Connection.SelectedBackend = ErChatBackendKind.ClaudeCode;
                    vm.Attachments.Support.Should()
                        .Be(
                            AttachmentSupport.Images
                                | AttachmentSupport.Pdf
                                | AttachmentSupport.Text
                                | AttachmentSupport.Binary
                        );

                    vm.Connection.SelectedBackend = ErChatBackendKind.Codex;
                    vm.Attachments.Support.Should().Be(AttachmentSupport.None);
                }
            ),
            Row(
                "backend-attachment-support-copilot-images-only",
                "Copilot の添付の範囲は画像だけ",
                [(Ready, Backend)],
                h =>
                {
                    var vm = h.CreateApiKeyReady();

                    vm.Connection.SelectedBackend = ErChatBackendKind.Copilot;

                    vm.Attachments.Support.Should().Be(AttachmentSupport.Images);
                }
            ),
            Row(
                "backend-codex-cli-missing-sets-guidance",
                "Codex の未検出の状態（要約・状態の点・インストールの案内）が VM へ届き、案内の行が出る",
                [(Ready, Backend)],
                h =>
                {
                    var vm = h.CreateApiKeyReady();
                    vm.ShowCodexGuidance.Should().BeFalse("初期状態では案内の行を出さない");

                    vm.ApplyCodexReadiness(
                        ready: false,
                        AiStrings.Codex_Status_NotFound,
                        ConnectionHealth.NeedsAction,
                        AiStrings.Codex_Guidance_Install
                    );

                    vm.CodexAccountSummary.Should().Be(AiStrings.Codex_Status_NotFound);
                    vm.CodexStatusLevel.Should().Be(ConnectionHealth.NeedsAction);
                    vm.CodexGuidance.Should().Be(AiStrings.Codex_Guidance_Install);
                    vm.ShowCodexGuidance.Should().BeTrue();
                }
            ),
            Row(
                "backend-ready-before-folder-still-cannot-start",
                "モックフォルダを選ぶ前に接続方式を切り替えて接続が整っても、フォルダが無ければ会話を開始できない",
                [(Idle, Backend)],
                h =>
                {
                    var vm = h.Create();

                    h.TryChangeBackend(vm, ErChatBackendKind.ClaudeCode).Should().BeTrue();
                    vm.ApplyClaudeCodeReadiness(
                        true,
                        "ログイン済み",
                        ConnectionHealth.Ready,
                        string.Empty
                    );

                    vm.IsBackendReady.Should().BeTrue();
                    vm.CanStartConversation.Should().BeFalse();

                    vm.MockFolder = h.MockFolder;
                    vm.CanStartConversation.Should().BeTrue();
                }
            ),
            Row(
                "backend-codex-status-message-updates-status",
                "Codex の確認の状態の通知が VM の状態の文へ届く",
                [(Ready, Backend)],
                h =>
                {
                    var vm = h.CreateApiKeyReady();

                    vm.ApplyCodexStatusMessage("接続に失敗しました: boom");

                    vm.StatusMessage.Should().Be("接続に失敗しました: boom");
                }
            ),
            Row(
                "backend-codex-empty-status-message-keeps-status",
                "Codex の空の状態の通知は状態の文を上書きしない",
                [(Ready, Backend)],
                h =>
                {
                    var vm = h.CreateApiKeyReady();

                    vm.ApplyCodexStatusMessage("先の通知");
                    vm.ApplyCodexStatusMessage("   ");

                    vm.StatusMessage.Should().Be("先の通知");
                }
            ),
            Row(
                "backend-copilot-readiness-drives-start-and-connection-state",
                "Copilot の可否は確認の結果で決まり、状態・案内・モデルの候補が接続タブへ流れる",
                [(Ready, Backend)],
                h =>
                {
                    var vm = h.CreateApiKeyReady();
                    vm.Connection.SelectedBackend = ErChatBackendKind.Copilot;

                    vm.IsBackendReady.Should().BeFalse();
                    vm.CanStartConversation.Should().BeFalse();

                    vm.ApplyCopilotReadiness(
                        true,
                        "ログイン済み (octocat)",
                        ConnectionHealth.Ready,
                        string.Empty,
                        ["gpt-5", "claude-sonnet-4.5"]
                    );

                    vm.IsBackendReady.Should().BeTrue();
                    vm.CanStartConversation.Should().BeTrue();
                    vm.Connection.CopilotStatusSummary.Should().Be("ログイン済み (octocat)");
                    vm.Connection.CopilotStatusLevel.Should().Be(ConnectionHealth.Ready);
                    vm.Connection.CopilotModelCandidates.Select(c => c.Name)
                        .Should()
                        .Contain(["gpt-5", "claude-sonnet-4.5"]);
                }
            ),
            Row(
                "backend-copilot-conversation-uses-copilot-factory",
                "Copilot の会話は Copilot のエンジンの作り方で始まる",
                [(Ready, Converse)],
                h =>
                {
                    var vm = h.Create();
                    vm.Connection.SelectedBackend = ErChatBackendKind.Copilot;
                    vm.ApplyCopilotReadiness(
                        true,
                        "ログイン済み",
                        ConnectionHealth.Ready,
                        string.Empty,
                        []
                    );
                    vm.MockFolder = h.MockFolder;

                    vm.StartConversationCommand.Execute(null);

                    h.Engines.Should().ContainSingle();
                }
            ),
            Row(
                "backend-switch-during-turn-is-refused-and-turn-still-completes",
                "ターンの実行中の接続タブの切替は断られ、セッションを切り離さないので、後から届く完了で実行中が解ける",
                [(Turn, Backend)],
                async h =>
                {
                    var vm = h.CreateApiKeyReady();
                    var engine = await h.StartHeldTurnAsync(vm);

                    h.TryChangeBackend(vm, ErChatBackendKind.Codex).Should().BeFalse();

                    vm.Connection.SelectedBackend.Should().Be(ErChatBackendKind.ApiKey);
                    vm.StatusMessage.Should().Be(MockStrings.Mock_SwitchBackendBlockedDuringTurn);
                    vm.DetachedDisposalForTests.Should().BeNull("セッションを切り離していない");
                    engine.DisposeCount.Should().Be(0);

                    engine.CompleteHeldTurn();

                    vm.IsTurnInProgress.Should().BeFalse();
                }
            ),
            Row(
                "backend-switch-outside-turn-resets-conversation",
                "ターンの外なら接続タブを切り替えて会話をリセットし、旧セッションのエンジンを破棄する",
                [(Conversation, Backend)],
                async h =>
                {
                    var vm = h.CreateApiKeyReady();
                    vm.StartConversationCommand.Execute(null);
                    vm.UserInput = "要望";
                    await vm.SendMessageCommand.ExecuteAsync(null);
                    vm.Messages.Should().NotBeEmpty();

                    h.TryChangeBackend(vm, ErChatBackendKind.Codex).Should().BeTrue();

                    vm.Connection.SelectedBackend.Should().Be(ErChatBackendKind.Codex);
                    vm.Messages.Should().BeEmpty();
                    vm.DetachedDisposalForTests.Should()
                        .NotBeNull("旧セッションは切り離して破棄する");
                    await vm.DetachedDisposalForTests!;
                    h.Engine.DisposeCount.Should().Be(1);
                }
            ),
            Row(
                "backend-can-change-matches-try-change-rejection",
                "ターンの実行中はタブの可否が偽で TryChangeBackend も断り、ターンが終われば切り替えられる",
                [(Ready, Backend), (Turn, Backend), (Conversation, Backend)],
                async h =>
                {
                    var vm = h.CreateApiKeyReady();
                    vm.CanChangeBackend.Should().BeTrue("待機中は切り替えられる");

                    var engine = await h.StartHeldTurnAsync(vm);
                    vm.CanChangeBackend.Should().BeFalse("ターンの実行中は切り替えられない");
                    h.TryChangeBackend(vm, ErChatBackendKind.Codex).Should().BeFalse();
                    engine.CompleteHeldTurn();
                    vm.CanChangeBackend.Should().BeTrue("ターンが終われば切り替えられる");
                }
            ),
            Row(
                "backend-switch-allowed-during-mock-generation",
                "モックプロジェクトの生成は開始時の接続方式を控えて使うので、生成中も接続タブを切り替えられる",
                [(GenRun, Backend)],
                async h =>
                {
                    var vm = h.Create();
                    await h.PrepareGenerationAsync(vm);
                    var run = h.StartHeldGeneration(vm);

                    vm.CanChangeBackend.Should().BeTrue("生成中は切り替えられる");
                    h.TryChangeBackend(vm, ErChatBackendKind.Codex).Should().BeTrue();

                    h.Generator.Release();
                    await run;

                    h.Generator.CapturedBackend.Should().Be(ErChatBackendKind.ClaudeCode);
                    vm.IsMockGenInProgress.Should().BeFalse();
                }
            ),
            Row(
                "backend-model-change-during-conversation-reaches-session-engine",
                "会話中に Claude Code のモデル・Codex のモデルとプロバイダーを変えると会話のエンジンへ流れ、切り離した旧エンジンへは流れない",
                [(Ready, Backend), (Conversation, Backend)],
                h =>
                {
                    h.UseRealCliEngines();
                    var vm = h.Create();
                    vm.MockFolder = h.MockFolder;

                    vm.Connection.SelectedBackend = ErChatBackendKind.ClaudeCode;
                    vm.ApplyClaudeCodeReadiness(true, "ok", ConnectionHealth.Ready, string.Empty);
                    vm.Connection.ClaudeCodeModel = "model-a";
                    vm.StartConversationCommand.Execute(null);
                    var claudeCode = h.ClaudeCodeEngine!;
                    claudeCode.Model.Should().Be("model-a");

                    vm.Connection.ClaudeCodeModel = "model-b";
                    claudeCode.Model.Should().Be("model-b");

                    vm.Connection.SelectedBackend = ErChatBackendKind.Codex;
                    vm.ApplyCodexReadiness(true, "ok", ConnectionHealth.Ready);
                    vm.StartConversationCommand.Execute(null);
                    h.CodexEngine.Should().NotBeNull();

                    vm.Connection.CodexModel = "codex-model-x";
                    vm.Connection.CodexModelProvider = "custom-provider";
                    h.CodexEngine!.Model.Should().Be("codex-model-x");
                    h.CodexEngine.ModelProvider.Should().Be("custom-provider");

                    vm.Connection.ClaudeCodeModel = "model-c";
                    claudeCode.Model.Should().Be("model-b");
                }
            ),
            Row(
                "attach-send-passes-to-engine-and-clears",
                "添付つきの送信はエンジンへ渡り、送信後に添付が消え、利用者の吹き出しに添付の要約が載る",
                [(Conversation, Attach)],
                async h =>
                {
                    var vm = h.Create();
                    vm.Connection.SelectedBackend = ErChatBackendKind.ClaudeCode;
                    vm.ApplyClaudeCodeReadiness(
                        true,
                        "ログイン済み",
                        ConnectionHealth.Ready,
                        string.Empty
                    );
                    vm.MockFolder = h.MockFolder;
                    vm.StartConversationCommand.Execute(null);

                    vm.Attachments.AddClipboardImage(PngBytes(), DateTime.Now);
                    vm.Attachments.Items.Should().HaveCount(1);

                    vm.UserInput = "この画面イメージで作って";
                    await vm.SendMessageCommand.ExecuteAsync(null);

                    h.Engine.SentAttachments.Should().ContainSingle();
                    h.Engine.SentAttachments[0].Should().HaveCount(1);
                    vm.Attachments.Items.Should().BeEmpty();
                    vm.Messages.Should()
                        .ContainSingle(m => m.Role == ErChatMessageRole.User && m.HasAttachments);
                }
            ),
            Row(
                "attach-switch-to-unsupported-backend-clears",
                "添付の非対応の接続方式（Codex）へ切り替えると送信待ちの添付が消える",
                [(Ready, Attach), (Ready, Backend)],
                h =>
                {
                    var vm = h.CreateApiKeyReady();
                    vm.Connection.SelectedBackend = ErChatBackendKind.ClaudeCode;
                    vm.Attachments.AddClipboardImage(PngBytes(), DateTime.Now);
                    vm.Attachments.Items.Should().HaveCount(1);

                    vm.Connection.SelectedBackend = ErChatBackendKind.Codex;

                    vm.Attachments.Items.Should().BeEmpty();
                }
            ),
            .. new[] { ("failed", (string?)"session failed"), ("interrupted", null) }.Select(c =>
                Row(
                    $"attach-kept-on-{c.Item1}-turn-cleared-on-success",
                    $"{c.Item1} で終わったターンでは添付が残り、成功したターンで消える",
                    [(Turn, Attach), (Conversation, Attach)],
                    async h =>
                    {
                        var vm = h.Create();
                        vm.Connection.SelectedBackend = ErChatBackendKind.ClaudeCode;
                        vm.ApplyClaudeCodeReadiness(
                            true,
                            "ログイン済み",
                            ConnectionHealth.Ready,
                            string.Empty
                        );
                        vm.MockFolder = h.MockFolder;
                        vm.StartConversationCommand.Execute(null);
                        h.Engine.HoldTurnCompletion = true;

                        vm.Attachments.AddClipboardImage(PngBytes(), DateTime.Now);
                        vm.UserInput = "この画面イメージで作って";
                        await vm.SendMessageCommand.ExecuteAsync(null);
                        h.Engine.SentAttachments.Should()
                            .ContainSingle()
                            .Which.Should()
                            .HaveCount(1);

                        h.Engine.FailHeldTurn(c.Item2);

                        vm.IsTurnInProgress.Should().BeFalse();
                        vm.Attachments.Items.Should()
                            .HaveCount(1, "送れなかった添付は送り直せるように残す");

                        vm.UserInput = "もう一度";
                        await vm.SendMessageCommand.ExecuteAsync(null);
                        h.Engine.CompleteHeldTurn();

                        vm.Attachments.Items.Should().BeEmpty("成功したターンで送った添付は消す");
                    }
                )
            ),
            Row(
                "attach-refused-during-turn",
                "ターンの実行中は添付を足せず、理由を状態の文へ出す（送信待ちの添付は変わらない）",
                [(Turn, Attach)],
                async h =>
                {
                    var vm = h.Create();
                    vm.Connection.SelectedBackend = ErChatBackendKind.ClaudeCode;
                    vm.ApplyClaudeCodeReadiness(
                        true,
                        "ログイン済み",
                        ConnectionHealth.Ready,
                        string.Empty
                    );
                    vm.MockFolder = h.MockFolder;
                    await h.StartHeldTurnAsync(vm);

                    vm.Attachments.IsEnabled.Should().BeFalse();
                    vm.Attachments.AddClipboardImage(PngBytes(), DateTime.Now);

                    vm.Attachments.Items.Should().BeEmpty();
                    vm.StatusMessage.Should().Be(AiUiStrings.Attachment_DisabledTurnInProgress);
                }
            ),
            Row(
                "persist-selected-backend-restored-on-next-load",
                "設定の保存が接続タブを保存し、次に作った VM の InitialBackend に戻る（保存が無い初回は API キー）",
                [(Idle, Persist)],
                h =>
                {
                    var vm = h.Create();
                    vm.Connection.InitialBackend.Should().Be(ErChatBackendKind.ApiKey);

                    vm.Connection.SelectedBackend = ErChatBackendKind.ClaudeCode;
                    vm.SaveSettings();

                    var restored = h.Create();
                    restored.Connection.InitialBackend.Should().Be(ErChatBackendKind.ClaudeCode);
                }
            ),
            Row(
                "persist-successful-local-llm-turn-records-model",
                "ローカル LLM の成功したターンで使ったモデルが候補と設定の履歴へ記録される",
                [(Conversation, Persist)],
                async h =>
                {
                    var vm = h.CreateApiKeyReady();
                    vm.Connection.ApiModel = "qwen3.6:35b";

                    vm.StartConversationCommand.Execute(null);
                    vm.UserInput = "管理画面を作って";
                    await vm.SendMessageCommand.ExecuteAsync(null);

                    vm.Connection.ApiModelCandidates.Select(c => c.Name)
                        .Should()
                        .Contain("qwen3.6:35b");
                    h.Store()
                        .Load()
                        .ApiModelHistory.ModelsFor("localllm")
                        .Should()
                        .Contain("qwen3.6:35b");
                }
            ),
            Row(
                "persist-model-history-save-failure-keeps-turn-successful",
                "成功したターンのモデルの履歴の保存に失敗しても、ターンは成功のまま（エラーのメッセージを出さず）、理由が状態の文に出る",
                [(Conversation, Persist)],
                async h =>
                {
                    h.BlockSettingsFolder();
                    var vm = h.CreateApiKeyReady();
                    var save = vm.SaveSettings;
                    save.Should().Throw<Exception>("この構成では設定の保存が実際に落ちる");

                    vm.Connection.ApiModel = "qwen3.6:35b";
                    vm.StartConversationCommand.Execute(null);
                    vm.UserInput = "管理画面を作って";

                    var send = () => vm.SendMessageCommand.ExecuteAsync(null);
                    await send.Should().NotThrowAsync();

                    vm.IsTurnInProgress.Should().BeFalse();
                    vm.StatusMessage.Should()
                        .StartWith(FormatPrefix(MockStrings.Mock_ModelHistorySaveFailedFormat));
                    var errorPrefix = FormatPrefix(MockStrings.Mock_ErrorSystemFormat);
                    vm.Messages.Should()
                        .NotContain(m =>
                            m.Role == ErChatMessageRole.System
                            && m.Content.StartsWith(errorPrefix, StringComparison.Ordinal)
                        );
                }
            ),
            Row(
                "persist-save-and-hide-failure-hides-then-reports-error",
                "× で閉じるときの設定の保存が失敗しても先に隠し、隠した後にエラーを 1 回知らせ、会話はそのまま残す",
                [(Conversation, Persist)],
                h =>
                {
                    h.BlockSettingsFolder();
                    var vm = h.CreateApiKeyReady();
                    vm.StartConversationCommand.Execute(null);
                    var messages = vm.Messages.Count;
                    int? errorsWhenHidden = null;

                    vm.SaveSettingsAndHide(() => errorsWhenHidden = h.Dialogs.ErrorMessages.Count);

                    errorsWhenHidden.Should().Be(0, "隠すのはエラーを知らせる前");
                    h.Dialogs.ErrorMessages.Should().ContainSingle();
                    vm.StatusMessage.Should()
                        .StartWith(FormatPrefix(MockStrings.Mock_SettingsSaveFailedFormat));
                    vm.Messages.Should().HaveCount(messages);
                    vm.CanSendMessage.Should().BeFalse();
                    vm.UserInput = "要望";
                    vm.CanSendMessage.Should().BeTrue("会話は残っている");
                }
            ),
        ];
}
