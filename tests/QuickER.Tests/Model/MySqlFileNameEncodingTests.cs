using AwesomeAssertions;
using QuickER.Model;

namespace QuickER.Tests.Model;

/// <summary>
/// MySQL の表の名前のファイル名への符号化の長さ（<see cref="MySqlFileNameEncoding"/>）を検証するテストクラス
/// </summary>
/// <remarks>
/// 範囲の出典との一致（MySQL のソースの表）と、実 MySQL が作るファイル名との一致は、Integration の
/// <c>IdentifierLengthLimitIntegrationTests</c> が範囲の境界の文字ごとに確かめる。ここでは 1・3・5 バイトの 3 区分を確かめる。
/// </remarks>
public class MySqlFileNameEncodingTests
{
    [Theory(DisplayName = "EncodedLength: ASCII の英数字と _ は 1、ほかの ASCII は 5")]
    [InlineData('a', 1)]
    [InlineData('Z', 1)]
    [InlineData('0', 1)]
    [InlineData('_', 1)]
    [InlineData('#', 5)]
    [InlineData('.', 5)]
    [InlineData(' ', 5)]
    [InlineData('@', 5)]
    [InlineData('?', 5)]
    public void EncodedLength_Ascii(char c, int expected)
    {
        MySqlFileNameEncoding.EncodedLength(c).Should().Be(expected);
    }

    [Theory(DisplayName = "EncodedLength: 範囲の両端は 3、範囲のすぐ外は 5")]
    [InlineData(0x00BF, 5)]
    [InlineData(0x00C0, 3)]
    [InlineData(0x00D6, 3)]
    [InlineData(0x00D7, 5)]
    [InlineData(0x0130, 5)]
    [InlineData(0x0585, 3)]
    [InlineData(0x0586, 5)]
    [InlineData(0x1E9C, 5)]
    [InlineData(0x2160, 3)]
    [InlineData(0x217F, 3)]
    [InlineData(0x2180, 5)]
    [InlineData(0x24B5, 5)]
    [InlineData(0x24B6, 3)]
    [InlineData(0x24E9, 3)]
    [InlineData(0x24EA, 5)]
    [InlineData(0xFF20, 5)]
    [InlineData(0xFF21, 3)]
    [InlineData(0xFF5A, 3)]
    [InlineData(0xFF5B, 5)]
    [InlineData(0x8868, 5)]
    [InlineData(0x1F600, 5)]
    public void EncodedLength_Ranges(int codePoint, int expected)
    {
        MySqlFileNameEncoding.EncodedLength(codePoint).Should().Be(expected);
    }

    [Fact(
        DisplayName = "EncodedLength: 名前の長さは文字ごとの長さの和（サロゲート対は 1 文字として 5）"
    )]
    public void EncodedLength_SumsRunes()
    {
        MySqlFileNameEncoding.EncodedLength("aÄ表😀").Should().Be(1 + 3 + 5 + 5);
    }
}
