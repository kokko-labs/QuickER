using System.Text.Json;
using System.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.Extensions.AI;
using ModelContextProtocol.Protocol;
using QuickER.Mcp;

namespace QuickER.Tests.Mcp;

/// <summary>
/// ツール実行の成否を MCP プロトコルの応答へ載せる <see cref="DelegatingToolFunction"/> の検証。
/// </summary>
/// <remarks>
/// 失敗をテキストとしてだけ返すと、外部エージェントは応答本文を読まない限り成否を判断できない。
/// MCP はツール実行エラーを <c>isError</c> で表す規約のため、失敗経路はそれを立てた
/// <see cref="CallToolResult"/> を返す（成功経路は従来どおり結果テキストのまま）。
/// </remarks>
public class DelegatingToolFunctionTests
{
    /// <summary>引数を取らない最小の入力スキーマ</summary>
    private static JsonElement EmptySchema =>
        JsonSerializer.SerializeToElement(
            new Dictionary<string, object?>
            {
                ["type"] = "object",
                ["properties"] = new Dictionary<string, object?>(),
            }
        );

    private static DelegatingToolFunction Create(string result, bool success) =>
        new("probe", "probe tool", EmptySchema, (_, _) => (result, success));

    /// <summary>成功時は結果テキストをそのまま返す（応答の形を変えない）ことを検証する</summary>
    [Fact(DisplayName = "DelegatingToolFunction: 成功は結果テキストをそのまま返す")]
    public async Task Invoke_Success_ReturnsPlainText()
    {
        var function = Create("Added entity 'Customer'.", success: true);

        var result = await function.InvokeAsync(
            new AIFunctionArguments(),
            TestContext.Current.CancellationToken
        );

        result.Should().Be("Added entity 'Customer'.");
    }

    /// <summary>失敗時は <c>isError</c> を立てた結果を返し、本文は従来のエラーテキストのままであることを検証する</summary>
    [Fact(DisplayName = "DelegatingToolFunction: 失敗は isError を立てて返す")]
    public async Task Invoke_Failure_SetsIsError()
    {
        var function = Create("Diagram file not found: x.json.", success: false);

        var result = await function.InvokeAsync(
            new AIFunctionArguments(),
            TestContext.Current.CancellationToken
        );

        var callResult = result.Should().BeOfType<CallToolResult>().Subject;
        callResult.IsError.Should().BeTrue("MCP はツール実行の失敗を isError で表す");
        callResult
            .Content.OfType<TextContentBlock>()
            .Select(c => c.Text)
            .Should()
            .Equal("Diagram file not found: x.json.");
    }
}
