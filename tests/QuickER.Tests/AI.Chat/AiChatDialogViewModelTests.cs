using AwesomeAssertions;
using QuickER.AI;

namespace QuickER.Tests.AI.Chat;

/// <summary>
/// チャットの吹き出しのモデル（<see cref="ErChatMessage"/>）の既定を検証するテストクラス
/// </summary>
/// <remarks>
/// ダイアログの VM（<see cref="QuickER.AI.Chat.AiChatDialogViewModel"/>）の振る舞いは、操作の列の表
/// （<see cref="ChatDialogTests"/>）が持つ。ここに残すのは VM を使わないモデルの既定だけ。
/// </remarks>
public class AiChatDialogViewModelTests
{
    /// <summary>ストリーミングを経ずに作った回答は、最初から応答済み（Markdown 表示の対象）であることを検証する</summary>
    [Fact(DisplayName = "ストリーミングを経ずに作った回答は最初から応答済み")]
    public void AssistantMessage_NotStreamed_IsNotStreaming()
    {
        new ErChatMessage { Role = ErChatMessageRole.Assistant, Content = "完成した回答" }
            .IsStreaming.Should()
            .BeFalse();
    }
}
