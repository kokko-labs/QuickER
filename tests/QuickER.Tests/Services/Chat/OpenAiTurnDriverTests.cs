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

    /// <summary>テキスト添付は本文末尾へインライン展開され、テキストパート 1 件になることを検証する</summary>
    [Fact(DisplayName = "テキスト添付は本文へインライン展開される")]
    public void ToUserMessage_TextAttachment_InlinedIntoBody()
    {
        var body = System.Text.Encoding.UTF8.GetBytes("col1,col2\n1,2");
        var item = new ChatHistoryItem(
            ChatHistoryRole.User,
            "このデータを見て",
            Attachments: new[]
            {
                new ChatAttachment("data.csv", ChatAttachmentKind.Text, "text/plain", body),
            }
        );

        var message = OpenAiTurnDriver.ToUserMessage(item);

        // 画像パートは無く、テキスト 1 件（本文＋インライン展開）
        message.Content.Should().ContainSingle();
        message.Content[0].Kind.Should().Be(ChatMessageContentPartKind.Text);
        message.Content[0].Text.Should().Contain("このデータを見て");
        message.Content[0].Text.Should().Contain("【添付ファイル: data.csv】");
        message.Content[0].Text.Should().Contain("col1,col2");
    }

    /// <summary>画像＋テキスト混在では画像はパート・テキストは本文へインラインされることを検証する</summary>
    [Fact(DisplayName = "画像＋テキスト混在は画像パート＋インライン本文")]
    public void ToUserMessage_ImageAndText_MixesCorrectly()
    {
        byte[] pngData = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
        var textData = System.Text.Encoding.UTF8.GetBytes("メモ本文");
        var item = new ChatHistoryItem(
            ChatHistoryRole.User,
            "両方見て",
            Attachments: new[]
            {
                new ChatAttachment("f.png", ChatAttachmentKind.Image, "image/png", pngData),
                new ChatAttachment("m.txt", ChatAttachmentKind.Text, "text/plain", textData),
            }
        );

        var message = OpenAiTurnDriver.ToUserMessage(item);

        message.Content.Should().HaveCount(2);
        message.Content[0].Kind.Should().Be(ChatMessageContentPartKind.Image);
        message.Content[1].Kind.Should().Be(ChatMessageContentPartKind.Text);
        message.Content[1].Text.Should().Contain("【添付ファイル: m.txt】");
        message.Content[1].Text.Should().Contain("メモ本文");
    }
}
