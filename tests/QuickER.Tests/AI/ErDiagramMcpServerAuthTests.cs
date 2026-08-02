using AwesomeAssertions;
using QuickER.AI;

namespace QuickER.Tests.AI;

/// <summary><see cref="ErDiagramMcpServer"/> の Bearer トークン照合（固定時間比較）を検証するテストクラス</summary>
public class ErDiagramMcpServerAuthTests
{
    private const string Token = "0123456789ABCDEF0123456789ABCDEF";

    [Fact]
    public void IsAuthorized_正しいBearerトークンなら許可する()
    {
        ErDiagramMcpServer.IsAuthorized($"Bearer {Token}", Token).Should().BeTrue();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Bearer ")]
    [InlineData("Bearer 0123456789ABCDEF0123456789ABCDE0")] // 末尾 1 文字違い
    [InlineData("bearer 0123456789ABCDEF0123456789ABCDEF")] // スキーム名の大文字小文字違い
    [InlineData("0123456789ABCDEF0123456789ABCDEF")] // スキーム名なし
    [InlineData("Bearer 0123456789ABCDEF0123456789ABCDEF ")] // 末尾に余分な空白
    public void IsAuthorized_不一致のヘッダーは拒否する(string? header)
    {
        ErDiagramMcpServer.IsAuthorized(header, Token).Should().BeFalse();
    }
}
