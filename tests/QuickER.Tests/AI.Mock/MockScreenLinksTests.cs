using AwesomeAssertions;
using QuickER.AI.Mock;

namespace QuickER.Tests.AI.Mock;

/// <summary>
/// <see cref="MockScreenLinks"/>（相対画面リンクの抽出と画面 ID の導出の単一正本）を検証するテストクラス。
/// </summary>
public class MockScreenLinksTests
{
    [Theory(DisplayName = "相対リンクのリンク先から画面ファイル名を取り出す")]
    [InlineData("B.html", "B.html")]
    [InlineData("./B.html", "B.html")]
    [InlineData("sub/B.html", "B.html")]
    [InlineData("sub\\B.html", "B.html")]
    [InlineData("../B.html", "B.html")]
    public void TryGetScreenFile_ReturnsLastSegment(string href, string expected)
    {
        MockScreenLinks.TryGetScreenFile(href, out var file).Should().BeTrue();
        file.Should().Be(expected);
    }

    [Theory(DisplayName = "絶対 URL と空のファイル名は画面リンクとして扱わない")]
    [InlineData("https://example.com/B.html")]
    [InlineData("file://server/share/B.html")]
    [InlineData("sub/")]
    public void TryGetScreenFile_RejectsNonScreenLinks(string href)
    {
        MockScreenLinks.TryGetScreenFile(href, out _).Should().BeFalse();
    }

    [Theory(DisplayName = "画面 ID は拡張子を除いた最後のセグメント")]
    [InlineData("OrderList.html", "OrderList")]
    [InlineData("sub/OrderList.html", "OrderList")]
    [InlineData("a.b.html", "a.b")]
    [InlineData(".html", ".html")]
    public void ScreenId_StripsExtension(string file, string expected)
    {
        MockScreenLinks.ScreenId(file).Should().Be(expected);
    }

    [Theory(DisplayName = "リンクの正規表現は閉じの引用符が開きと同じときだけ一致する")]
    [InlineData("<a href=\"B.html\">", true)]
    [InlineData("<a href='B.html'>", true)]
    [InlineData("<a href=\"B.html#x\">", true)]
    [InlineData("<a href=\"B.html'>", false)]
    [InlineData("<a href='B.html\">", false)]
    public void RelativeScreenLink_RequiresMatchingQuotes(string html, bool expected)
    {
        MockScreenLinks.RelativeScreenLink.IsMatch(html).Should().Be(expected);
    }
}
