using System.Text;
using AwesomeAssertions;
using QuickER.AI.Mock;

namespace QuickER.Tests.AI.Mock;

/// <summary>
/// <see cref="MockProgressWriter"/> が、改行で終わらない断片の後の行を新しい行から書くことを検証するテストクラス。
/// </summary>
public class MockProgressWriterTests
{
    /// <summary>改行で終わらない文章の後の行は、改行を 1 つ入れてから書くことを検証する</summary>
    [Fact(DisplayName = "改行で終わらない文章の後の行は新しい行から始まる")]
    public void WriteLine_AfterTextWithoutNewline_StartsOnNewLine()
    {
        var output = new StringBuilder();
        var writer = new MockProgressWriter(text => output.Append(text));

        writer.Write("了解しました。");
        writer.WriteLine("[2/7] 画面");

        output.ToString().Should().Be("了解しました。\n[2/7] 画面\n");
    }

    /// <summary>改行で終わる文章の後と、最初の行では、改行を重ねないことを検証する</summary>
    [Fact(DisplayName = "改行で終わる文章の後と最初の行では改行を重ねない")]
    public void WriteLine_AtLineStart_DoesNotAddNewline()
    {
        var output = new StringBuilder();
        var writer = new MockProgressWriter(text => output.Append(text));

        writer.WriteLine("最初の行");
        writer.Write("文章\n");
        writer.WriteLine("次の行");
        writer.WriteLine("その次の行");

        output.ToString().Should().Be("最初の行\n文章\n次の行\nその次の行\n");
    }

    /// <summary>空の断片は行の位置を変えないことを検証する</summary>
    [Fact(DisplayName = "空の断片は行の位置を変えない")]
    public void Write_Empty_KeepsPosition()
    {
        var output = new StringBuilder();
        var writer = new MockProgressWriter(text => output.Append(text));

        writer.Write("途中");
        writer.Write(string.Empty);
        writer.WriteLine("行");

        output.ToString().Should().Be("途中\n行\n");
    }
}
