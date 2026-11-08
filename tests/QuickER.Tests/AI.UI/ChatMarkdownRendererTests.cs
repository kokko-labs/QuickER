using AwesomeAssertions;
using QuickER.Tests.TestSupport.Engines;

namespace QuickER.Tests.AI.UI;

/// <summary>
/// AI の回答の Markdown 表示の表（要素 × 入力の形。リンクは http / https だけ・画像を読み込まない・HTML を解釈しない・
/// 入れ子の上限を超えたらその吹き出しだけテキスト表示へ。表示の行は Markdown の構文が対応する要素になること）
/// </summary>
public sealed class ChatMarkdownRendererTests
{
    /// <summary>行の Id</summary>
    public static TheoryData<string> Ids() => ChatMarkdownRows.Table.Ids();

    [Theory(
        DisplayName = "Markdown 表示の表：開けるリンクは http / https だけで、画像・HTML・深い入れ子を安全に表示する"
    )]
    [MemberData(nameof(Ids))]
    public void Row_Holds(string id) => ChatMarkdownRows.Table[id].Input.Body();

    [Fact(DisplayName = "網羅：要素 × 入力の形と、リンクの要素ごとの開けるリンク・文字の両方")]
    public void Coverage_MatchesDeclaredGaps()
    {
        var counter = new CoverageCounter(ChatMarkdownDimensions.Table);

        foreach (var row in ChatMarkdownRows.Table.Where(row => row.Input.Element is not null))
        {
            ChatMarkdownDimensions.Observe(
                counter,
                row.Input.Element!.Value,
                row.Input.Form!.Value
            );
        }

        counter.Report().AssertGaps(ChatMarkdownDimensions.Gaps, ChatMarkdownDimensions.Scope);
        ChatMarkdownRows.OutcomeCoverageProblems().Should().BeEmpty();
    }
}
