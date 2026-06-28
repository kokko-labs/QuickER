using QuickER.Services.Chat;
using FluentAssertions;

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
}
