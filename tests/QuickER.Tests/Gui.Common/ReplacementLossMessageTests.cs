using AwesomeAssertions;
using QuickER.Gui.Abstractions;

namespace QuickER.Tests.Gui.Common;

/// <summary>
/// 取込による図の置き換えの確認文へ付ける内訳（<see cref="ReplacementLossMessage"/>）を検証するテストクラス
/// </summary>
/// <remarks>
/// DB 取込・C# コードの取込・Excel 定義書のマージ取込が共有する。<see cref="DialogItemList"/> のテストと同じ
/// フォルダに置く（同じアセンブリの、ダイアログ本文を組み立てる部品）。
/// </remarks>
public class ReplacementLossMessageTests
{
    /// <summary>テスト用の書式</summary>
    private static readonly ReplacementLossTexts Texts = new(
        "Broken:" + "\n" + "{0}",
        "Overwrite {0}",
        "+{0} more"
    );

    /// <summary>失うものが無ければ確認文をそのまま返す</summary>
    [Fact(DisplayName = "失うものが無ければ確認文をそのまま返す")]
    public void Build_NoLoss_ReturnsMessage()
    {
        ReplacementLossMessage.Build("Replace?", [], 0, Texts).Should().Be("Replace?");
    }

    /// <summary>壊れるクエリと説明の上書きを、空行で区切ってこの順に付ける</summary>
    [Fact(DisplayName = "壊れるクエリと説明の上書きを空行で区切って付ける")]
    public void Build_BothLosses_AppendsInOrder()
    {
        var nl = Environment.NewLine;

        ReplacementLossMessage
            .Build("Replace?", ["a", "b"], 3, Texts)
            .Should()
            .Be(
                "Replace?"
                    + nl
                    + nl
                    + "Broken:"
                    + "\n"
                    + "- a"
                    + nl
                    + "- b"
                    + nl
                    + nl
                    + "Overwrite 3"
            );
    }

    /// <summary>クエリ名の一覧は上限で畳む（標準のメッセージボックスへ載るため）</summary>
    [Fact(DisplayName = "クエリ名の一覧は上限で畳む")]
    public void Build_ManyQueries_FoldsList()
    {
        var names = Enumerable.Range(1, DialogItemList.MaxItems + 5).Select(n => $"q{n}").ToList();

        var message = ReplacementLossMessage.Build("Replace?", names, 0, Texts);

        message.Should().Contain("- q30").And.NotContain("- q31").And.EndWith("+5 more");
    }
}
