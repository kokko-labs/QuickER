using FluentAssertions;
using OpenAI.Chat;
using QuickER.AI;

namespace QuickER.Tests.Services.Chat;

/// <summary>
/// <see cref="OpenAiTurnDriver"/> の User 履歴 → UserChatMessage 変換（画像コンテンツパートの組み立て・
/// PDF 除外・添付なし時の挙動不変）を検証するテストクラス。
/// </summary>
public class OpenAiTurnDriverTests
{
    /// <summary>画像添付付き User 項目が image コンテンツパート＋テキストパートになることを検証する</summary>
    [Fact(DisplayName = "画像添付は image コンテンツパートになる")]
    public void ToUserMessage_ImageAttachment_ProducesImagePart()
    {
        byte[] pngData = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
        var item = new ChatHistoryItem(
            ChatHistoryRole.User,
            "この図を参考に",
            Attachments: new[]
            {
                new ChatAttachment("figure.png", ChatAttachmentKind.Image, "image/png", pngData),
            }
        );

        var message = OpenAiTurnDriver.ToUserMessage(item);

        message.Content.Should().HaveCount(2);
        message.Content[0].Kind.Should().Be(ChatMessageContentPartKind.Image);
        message.Content[0].ImageBytesMediaType.Should().Be("image/png");
        message.Content[0].ImageBytes.ToArray().Should().Equal(pngData);
        message.Content[1].Kind.Should().Be(ChatMessageContentPartKind.Text);
    }

    /// <summary>PDF 添付は無視され（Images のみ対応）、テキストのみのメッセージになることを検証する</summary>
    [Fact(DisplayName = "PDF 添付は除外されテキストのみになる")]
    public void ToUserMessage_PdfAttachment_IsExcluded()
    {
        var pdfData = "%PDF-1.7"u8.ToArray();
        var item = new ChatHistoryItem(
            ChatHistoryRole.User,
            "この仕様書に沿って",
            Attachments: new[]
            {
                new ChatAttachment("spec.pdf", ChatAttachmentKind.Pdf, "application/pdf", pdfData),
            }
        );

        var message = OpenAiTurnDriver.ToUserMessage(item);

        // 画像パートは無く、テキスト 1 件のみ（PDF は組み立て対象外）
        message.Content.Should().NotContain(part => part.Kind == ChatMessageContentPartKind.Image);
    }

    /// <summary>添付なし User 項目は従来どおりテキストメッセージになることを検証する（挙動不変）</summary>
    [Fact(DisplayName = "添付なし User はテキストメッセージのまま")]
    public void ToUserMessage_NoAttachments_ProducesText()
    {
        var item = new ChatHistoryItem(ChatHistoryRole.User, "こんにちは");

        var message = OpenAiTurnDriver.ToUserMessage(item);

        message.Content.Should().ContainSingle();
        message.Content[0].Kind.Should().Be(ChatMessageContentPartKind.Text);
        message.Content[0].Text.Should().Be("こんにちは");
    }
}
