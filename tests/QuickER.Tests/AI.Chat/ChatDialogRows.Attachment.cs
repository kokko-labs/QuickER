using AwesomeAssertions;
using QuickER.AI;
using QuickER.AI.Chat;
using QuickER.AI.UI;
using QuickER.Tests.TestSupport.Engines;
using static QuickER.Tests.AI.Chat.ChatDialogOp;
using static QuickER.Tests.AI.Chat.ChatDialogState;
using ChatStrings = QuickER.AI.Chat.Resources.Strings;

namespace QuickER.Tests.AI.Chat;

internal static partial class ChatDialogRows
{
    /// <summary>添付の範囲・プロバイダーの切替の告知・ターンの終わりの添付</summary>
    private static IEnumerable<ScenarioRow<ChatDialogCase>> AttachmentRows() =>
        [
            Row(
                "attachment-support-tracks-api-provider",
                "添付範囲はプロバイダーに追従する",
                [(Idle, ChangeProvider), (Idle, Attach)],
                h =>
                {
                    var vm = h.Create();

                    vm.Connection.ApiProvider = AiProvider.OpenAI;
                    vm.Attachments.Support.Should()
                        .Be(AttachmentSupport.Images | AttachmentSupport.Text);

                    vm.Connection.ApiProvider = AiProvider.Claude;
                    vm.Attachments.Support.Should()
                        .Be(
                            AttachmentSupport.Images
                                | AttachmentSupport.Pdf
                                | AttachmentSupport.Text
                        );

                    // ローカル LLM は OpenAI 互換 API のため OpenAI と同じ範囲
                    vm.Connection.ApiProvider = AiProvider.LocalLlm;
                    vm.Attachments.Support.Should()
                        .Be(AttachmentSupport.Images | AttachmentSupport.Text);
                }
            ),
            Row(
                "attachment-support-claude-code-is-all-kinds",
                "Claude Code では添付範囲が全種別",
                [(Idle, SwitchBackend), (Idle, Attach)],
                h =>
                {
                    var vm = h.Create();

                    vm.Connection.SelectedBackend = ErChatBackendKind.ClaudeCode;
                    vm.Attachments.Support.Should()
                        .Be(
                            AttachmentSupport.Images
                                | AttachmentSupport.Pdf
                                | AttachmentSupport.Text
                                | AttachmentSupport.Binary
                        );
                }
            ),
            Row(
                "attachment-switch-to-narrower-provider-clears-unsupported",
                "添付範囲が狭いプロバイダーへ切替で対応外の添付をクリア",
                [(Idle, ChangeProvider), (Idle, Attach)],
                h =>
                {
                    var pdfPath = h.WriteFile("spec.pdf", PdfBytes());
                    var vm = h.Create();

                    vm.Connection.ApiProvider = AiProvider.Claude;
                    vm.Attachments.AddFiles(new[] { pdfPath });
                    vm.Attachments.Items.Should().HaveCount(1);

                    vm.Connection.ApiProvider = AiProvider.LocalLlm;

                    vm.Attachments.Items.Should().BeEmpty();
                }
            ),
            Row(
                "attachment-provider-switch-during-conversation-with-unsupported-notifies",
                "会話中のプロバイダー切替で対応範囲外の添付があれば告知する",
                [(Conversation, ChangeProvider), (Conversation, Attach)],
                h =>
                {
                    var vm = h.Create();

                    vm.Connection.ApiProvider = AiProvider.Claude;
                    vm.Connection.ApiKey = "sk-test";
                    vm.StartConversationCommand.Execute(null);

                    InjectApiKeyEngineHistoryItem(
                        vm,
                        new ChatHistoryItem(
                            ChatHistoryRole.User,
                            "見て",
                            Attachments:
                            [
                                new ChatAttachment(
                                    "spec.pdf",
                                    ChatAttachmentKind.Pdf,
                                    "application/pdf",
                                    PdfBytes()
                                ),
                            ]
                        )
                    );

                    // Claude（PDF 対応）→ OpenAI（PDF 非対応）
                    vm.Connection.ApiProvider = AiProvider.OpenAI;

                    vm.Messages.Should()
                        .Contain(m =>
                            m.Role == ErChatMessageRole.System
                            && m.Content == ChatStrings.Chat_AttachmentsDroppedOnProviderSwitch
                        );
                    vm.StatusMessage.Should()
                        .Be(ChatStrings.Chat_AttachmentsDroppedOnProviderSwitch);
                }
            ),
            Row(
                "attachment-provider-switch-during-turn-does-not-notify",
                "ターン実行中のプロバイダー切替は添付の告知判定を行わない",
                [(TurnInProgress, ChangeProvider), (TurnInProgress, Attach)],
                h =>
                {
                    var vm = h.Create();

                    vm.Connection.ApiProvider = AiProvider.Claude;
                    vm.Connection.ApiKey = "sk-test";
                    vm.StartConversationCommand.Execute(null);

                    InjectApiKeyEngineHistoryItem(
                        vm,
                        new ChatHistoryItem(
                            ChatHistoryRole.User,
                            "見て",
                            Attachments:
                            [
                                new ChatAttachment(
                                    "spec.pdf",
                                    ChatAttachmentKind.Pdf,
                                    "application/pdf",
                                    PdfBytes()
                                ),
                            ]
                        )
                    );

                    vm.IsTurnInProgress = true;
                    vm.Connection.ApiProvider = AiProvider.OpenAI;

                    vm.Messages.Should()
                        .NotContain(m =>
                            m.Role == ErChatMessageRole.System
                            && m.Content == ChatStrings.Chat_AttachmentsDroppedOnProviderSwitch
                        );
                }
            ),
            Row(
                "attachment-provider-switch-during-conversation-without-attachments-does-not-notify",
                "会話中のプロバイダー切替で添付が無ければ告知しない",
                [(Conversation, ChangeProvider), (Conversation, Attach)],
                h =>
                {
                    var vm = h.Create();

                    vm.Connection.ApiProvider = AiProvider.Claude;
                    vm.Connection.ApiKey = "sk-test";
                    vm.StartConversationCommand.Execute(null);

                    // Claude → OpenAI（添付が無いので落ちるものが無い）
                    vm.Connection.ApiProvider = AiProvider.OpenAI;

                    vm.Messages.Should()
                        .NotContain(m =>
                            m.Role == ErChatMessageRole.System
                            && m.Content == ChatStrings.Chat_AttachmentsDroppedOnProviderSwitch
                        );
                }
            ),
            Row(
                "attachment-provider-switch-before-conversation-does-not-notify",
                "会話開始前のプロバイダー切替は告知しない",
                [(Idle, ChangeProvider), (Idle, Attach)],
                h =>
                {
                    var vm = h.Create();

                    vm.Connection.ApiProvider = AiProvider.Claude;

                    // 会話未開始のまま切り替える
                    vm.Connection.ApiProvider = AiProvider.OpenAI;

                    vm.Messages.Should()
                        .NotContain(m =>
                            m.Role == ErChatMessageRole.System
                            && m.Content == ChatStrings.Chat_AttachmentsDroppedOnProviderSwitch
                        );
                }
            ),
            Row(
                "attachment-support-copilot-is-images-only",
                "Copilot では添付範囲が画像のみ",
                [(Idle, SwitchBackend), (Idle, Attach)],
                h =>
                {
                    var vm = h.Create();

                    vm.Connection.SelectedBackend = ErChatBackendKind.Copilot;

                    vm.Attachments.Support.Should().Be(AttachmentSupport.Images);
                    vm.Attachments.Support.Allows(ChatAttachmentKind.Pdf).Should().BeFalse();
                    vm.Attachments.Support.Allows(ChatAttachmentKind.Text).Should().BeFalse();
                }
            ),
            Row(
                "attachment-kept-on-failed-turn-cleared-on-success",
                "失敗したターンでは添付が残り成功したターンで消える",
                [(TurnInProgress, Attach), (TurnInProgress, Turn)],
                h =>
                {
                    var copilot = h.Copilot;
                    var vm = h.Create();

                    vm.Connection.SelectedBackend = ErChatBackendKind.Copilot;
                    vm.Attachments.AddClipboardImage(PngBytes(), DateTime.Now);
                    ChatDialogHarness.StartCopilotTurn(vm);
                    copilot.Sends.Should().ContainSingle().Which.AttachmentCount.Should().Be(1);

                    copilot.RaiseError("session failed");

                    vm.IsTurnInProgress.Should().BeFalse();
                    vm.Attachments.HasAttachments.Should()
                        .BeTrue("送れなかった添付は送り直せるように残す");

                    vm.UserInput = "もう一度";
                    vm.SendMessageCommand.Execute(null);
                    copilot.RaiseIdle();

                    vm.Attachments.HasAttachments.Should()
                        .BeFalse("成功したターンで送った添付は消す");
                }
            ),
            Row(
                "attachment-kept-on-interrupted-turn",
                "中断したターンでは添付が残る",
                [(TurnInProgress, Attach), (TurnInProgress, Turn)],
                h =>
                {
                    var copilot = h.Copilot;
                    var vm = h.Create();

                    vm.Connection.SelectedBackend = ErChatBackendKind.Copilot;
                    vm.Attachments.AddClipboardImage(PngBytes(), DateTime.Now);
                    ChatDialogHarness.StartCopilotTurn(vm);

                    vm.InterruptCommand.Execute(null);
                    copilot.RaiseIdle(aborted: true);

                    vm.IsTurnInProgress.Should().BeFalse();
                    vm.Attachments.HasAttachments.Should().BeTrue();
                }
            ),
        ];
}
