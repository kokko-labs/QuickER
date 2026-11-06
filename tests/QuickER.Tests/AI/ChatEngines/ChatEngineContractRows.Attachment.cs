using AwesomeAssertions;
using QuickER.AI;
using QuickER.Tests.TestSupport.Engines;
using static QuickER.Tests.AI.ChatEngines.ChatEngineCapability;
using AiStrings = QuickER.AI.Resources.Strings;

namespace QuickER.Tests.AI.ChatEngines;

internal static partial class ChatEngineContractRows
{
    /// <summary>添付（受け付ける範囲の外は送らずに分かる形で断り、範囲の中はバックエンドへ届ける）</summary>
    private static IEnumerable<ScenarioRow<ChatContractScenario>> Attachment() =>
        [
            Row(
                "attachment-outside-support-is-refused",
                "受け付けない種類の添付は送らずに断る（例外か、理由の分かるターンの失敗）",
                ChatToolHostKind.None,
                async c =>
                {
                    var attachment = AttachmentOf(
                        AttachmentKinds.First(kind =>
                            !c.Adapter.ExpectedAttachmentSupport.Allows(kind)
                        )
                    );
                    var script = ChatTurnScript.Of(new ChatTurnEnding.Success()) with
                    {
                        Attachments = [attachment],
                    };

                    if (c.Adapter.Has(RefusesAttachmentAsTurnFailure))
                    {
                        await c.Rig.PlayTurnAsync("これは？", script, c.CancellationToken);
                        c.SingleCompletion(
                            false,
                            string.Format(
                                AiStrings.Chat_UnsupportedAttachment,
                                attachment.FileName,
                                attachment.Kind
                            )
                        );
                    }
                    else
                    {
                        await c
                            .Rig.Invoking(rig =>
                                rig.PlayTurnAsync("これは？", script, c.CancellationToken)
                            )
                            .Should()
                            .ThrowAsync<NotSupportedException>();
                    }

                    c.Rig.SendsReceived.Should().Be(0, "断った添付の送信はバックエンドへ届かない");
                },
                RefusesSomeAttachmentKind
            ),
            Row(
                "attachment-inside-support-reaches-backend",
                "受け付ける種類の添付は送信に同梱してバックエンドへ届ける",
                ChatToolHostKind.None,
                async c =>
                {
                    var attachment = AttachmentOf(
                        AttachmentKinds.First(kind =>
                            c.Adapter.ExpectedAttachmentSupport.Allows(kind)
                        )
                    );

                    await c.Rig.PlayTurnAsync(
                        "これは？",
                        ChatTurnScript.Of(new ChatTurnEnding.Success()) with
                        {
                            Attachments = [attachment],
                        },
                        c.CancellationToken
                    );

                    c.Rig.SendsReceived.Should().Be(1);
                    c.Rig.AttachmentsReceived.Should().Be(1);
                    c.SingleCompletion(true, null);
                },
                AcceptsAttachments
            ),
        ];

    /// <summary>添付の種類を選ぶ順（元のテストの画像・PDF の順）</summary>
    private static IReadOnlyList<ChatAttachmentKind> AttachmentKinds =>
        [
            ChatAttachmentKind.Image,
            ChatAttachmentKind.Pdf,
            ChatAttachmentKind.Text,
            ChatAttachmentKind.Binary,
        ];

    /// <summary>種類ごとの添付（元のテストの a.png・a.pdf と同じ形）</summary>
    private static ChatAttachment AttachmentOf(ChatAttachmentKind kind) =>
        kind switch
        {
            ChatAttachmentKind.Image => new("a.png", kind, "image/png", [1, 2]),
            ChatAttachmentKind.Pdf => new("a.pdf", kind, "application/pdf", [1, 2]),
            ChatAttachmentKind.Text => new("a.txt", kind, "text/plain", [0x41, 0x42]),
            _ => new("a.bin", kind, "application/octet-stream", [1, 2]),
        };
}
