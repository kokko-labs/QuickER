using Anthropic.Models.Messages;
using FluentAssertions;
using QuickER.AI;
using QuickER.Services.Chat;

namespace QuickER.Tests.Services.Chat;

/// <summary><see cref="AnthropicChatTurnDriver"/> の中立履歴 → Anthropic メッセージ変換を検証するテストクラス</summary>
public class AnthropicChatTurnDriverTests
{
    /// <summary>System 役割の履歴項目が結合され system プロンプト文字列になることを検証する</summary>
    [Fact(DisplayName = "ExtractSystemPrompt は System 役割の項目を結合する")]
    public void ExtractSystemPrompt_JoinsSystemItems()
    {
        var history = new List<ChatHistoryItem>
        {
            new(ChatHistoryRole.System, "ルール1"),
            new(ChatHistoryRole.User, "やあ"),
            new(ChatHistoryRole.System, "ルール2"),
        };

        var system = AnthropicChatTurnDriver.ExtractSystemPrompt(history);

        system.Should().Be("ルール1\n\nルール2");
    }

    /// <summary>System 項目はメッセージ列から除外され、それ以外の役割が順に変換されることを検証する</summary>
    [Fact(DisplayName = "ToMessageParams は System を除外し残りをメッセージへ変換する")]
    public void ToMessageParams_ExcludesSystemAndMapsRest()
    {
        var history = new List<ChatHistoryItem>
        {
            new(ChatHistoryRole.System, "システム指示"),
            new(ChatHistoryRole.User, "本のテーブルを作って"),
            new(
                ChatHistoryRole.Assistant,
                string.Empty,
                new[]
                {
                    new ChatToolCallRequest("call_1", "add_entity", "{\"table_name\":\"Book\"}"),
                }
            ),
            new(ChatHistoryRole.Tool, "テーブル 'Book' を追加しました。", ToolCallId: "call_1"),
        };

        var messages = AnthropicChatTurnDriver.ToMessageParams(history);

        // System を除いた User / Assistant(tool) / Tool の 3 件が積まれる
        messages.Should().HaveCount(3);
    }

    /// <summary>ツール呼び出しを伴わないアシスタント発話が、単純なテキストメッセージへ変換されることを検証する</summary>
    [Fact(DisplayName = "ToMessageParams はツール無しアシスタント発話をテキストメッセージにする")]
    public void ToMessageParams_PlainAssistant_ProducesSingleMessage()
    {
        var history = new List<ChatHistoryItem>
        {
            new(ChatHistoryRole.User, "こんにちは"),
            new(ChatHistoryRole.Assistant, "こんにちは、何をしますか？"),
        };

        var messages = AnthropicChatTurnDriver.ToMessageParams(history);

        messages.Should().HaveCount(2);
    }

    /// <summary>画像添付付き User 項目が image(base64) ブロック＋テキストブロックの content 配列になることを検証する</summary>
    [Fact(DisplayName = "画像添付は Base64ImageSource の image ブロックになる")]
    public void ToUserParam_ImageAttachment_ProducesImageBlock()
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

        var param = AnthropicChatTurnDriver.ToUserParam(item);

        // content はブロック配列（画像＋テキスト）になる
        var blocks = param.Content.Value.As<IReadOnlyList<ContentBlockParam>>();
        blocks.Should().HaveCount(2);
        blocks[0].Value.Should().BeOfType<ImageBlockParam>();

        var image = (ImageBlockParam)blocks[0].Value!;
        var source = image.Source.Value.As<Base64ImageSource>();
        source.Data.Should().Be(Convert.ToBase64String(pngData));
        source.MediaType.Value().Should().Be(MediaType.ImagePng);

        blocks[1].Value.Should().BeOfType<TextBlockParam>();
    }

    /// <summary>PDF 添付付き User 項目が Base64PdfSource の document ブロックになることを検証する</summary>
    [Fact(DisplayName = "PDF 添付は Base64PdfSource の document ブロックになる")]
    public void ToUserParam_PdfAttachment_ProducesDocumentBlock()
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

        var param = AnthropicChatTurnDriver.ToUserParam(item);

        var blocks = param.Content.Value.As<IReadOnlyList<ContentBlockParam>>();
        blocks[0].Value.Should().BeOfType<DocumentBlockParam>();

        var document = (DocumentBlockParam)blocks[0].Value!;
        var source = document.Source.Value.As<Base64PdfSource>();
        source.Data.Should().Be(Convert.ToBase64String(pdfData));
    }

    /// <summary>添付が無い User 項目は従来どおり単純なテキストメッセージになることを検証する（挙動不変）</summary>
    [Fact(DisplayName = "添付なし User はテキストメッセージのまま")]
    public void ToUserParam_NoAttachments_ProducesPlainText()
    {
        var item = new ChatHistoryItem(ChatHistoryRole.User, "こんにちは");

        var param = AnthropicChatTurnDriver.ToUserParam(item);

        // content は文字列（ブロック配列ではない）
        param.Content.Value.Should().BeOfType<string>().Which.Should().Be("こんにちは");
    }

    /// <summary>添付付き履歴が再構築（ToMessageParams 経由）でも image ブロックを保持することを検証する（毎ターン再送）</summary>
    [Fact(DisplayName = "履歴再構築でも添付ブロックが保持される")]
    public void ToMessageParams_WithAttachment_RebuildsImageBlock()
    {
        byte[] pngData = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
        var history = new List<ChatHistoryItem>
        {
            new(ChatHistoryRole.System, "指示"),
            new(
                ChatHistoryRole.User,
                "図を見て",
                Attachments: new[]
                {
                    new ChatAttachment("a.png", ChatAttachmentKind.Image, "image/png", pngData),
                }
            ),
        };

        var messages = AnthropicChatTurnDriver.ToMessageParams(history);

        messages.Should().ContainSingle();
        var blocks = messages[0].Content.Value.As<IReadOnlyList<ContentBlockParam>>();
        blocks[0].Value.Should().BeOfType<ImageBlockParam>();
    }
}
