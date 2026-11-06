using AwesomeAssertions;
using OpenAI.Chat;
using QuickER.AI;

namespace QuickER.Tests.AI;

/// <summary>
/// <see cref="OpenAiTurnDriver"/> の User 履歴 → UserChatMessage 変換（画像コンテンツパートの組み立て・
/// PDF 除外・添付なし時の挙動不変）を検証するテストクラス。
/// </summary>
public class OpenAiTurnDriverTests
{
    /// <summary>ストリームで組み立てたツール呼び出しの作業用データを作る</summary>
    private static OpenAiTurnDriver.ToolCallAccumulator Accumulator(string? id, string? name)
    {
        var acc = new OpenAiTurnDriver.ToolCallAccumulator { Id = id, Name = name };
        acc.Arguments.Append("{}");
        return acc;
    }

    /// <summary>
    /// ID の無いツール呼び出し（ストリーミングで ID を付けない OpenAI 互換サーバー）は捨てずに ID を合成し、
    /// 名前の無いものだけを捨てることを検証する。合成した ID は空でなく、呼び出しどうしで重複しない。
    /// </summary>
    [Fact(DisplayName = "ID の無いツール呼び出しは ID を合成し、名前の無いものだけ捨てる")]
    public void BuildToolCalls_SynthesizesMissingIdsAndDropsNameless()
    {
        var calls = OpenAiTurnDriver.BuildToolCalls(
            new[]
            {
                Accumulator("call_given", "add_entity"),
                Accumulator(null, "add_column"),
                Accumulator(string.Empty, "remove_entity"),
                Accumulator("call_nameless", null),
            }
        );

        calls.Select(c => c.Name).Should().Equal("add_entity", "add_column", "remove_entity");
        calls[0].Id.Should().Be("call_given");
        calls[1].Id.Should().StartWith("call_").And.HaveLength("call_".Length + 32);
        calls[2].Id.Should().StartWith("call_").And.HaveLength("call_".Length + 32);
        calls[1].Id.Should().NotBe(calls[2].Id);
    }
}
