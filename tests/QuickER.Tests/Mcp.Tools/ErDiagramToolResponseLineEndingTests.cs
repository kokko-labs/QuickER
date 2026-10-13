using AwesomeAssertions;

namespace QuickER.Tests.Mcp.Tools;

/// <summary>
/// ER 図操作ツールの複数行の応答（get_diagram_summary）が、GUI とファイルの 2 つのホストのどちらでも改行を LF に
/// そろえることを確かめる
/// </summary>
/// <remarks>
/// 同じツールの応答の改行がホストで食い違うと（GUI＝CRLF・MCP＝LF のように）、改行で分けて読む呼び手の結果が
/// ホストで変わる。名前付きクエリの応答は <c>QueryToolCoreInputRuleTests</c> と
/// <c>QueryToolLocalizedFormatterLineEndingTests</c> が見る。
/// </remarks>
public sealed class ErDiagramToolResponseLineEndingTests : TwoHostToolTestBase
{
    [Theory(DisplayName = "get_diagram_summary の応答は、どちらのホストでも改行が LF だけ")]
    [InlineData(HostKind.Gui)]
    [InlineData(HostKind.File)]
    public void Summary_UsesLfOnly(HostKind host)
    {
        Setup(host);
        Arrange(
            "add_relationship",
            new
            {
                source_table = "Customer",
                target_table = "Order",
                source_columns = new[] { "CustomerId" },
                target_columns = new[] { "CustomerId" },
                relationship_type = "OneToMany",
            }
        );
        Arrange(
            "set_unique_constraint",
            new { table_name = "Customer", columns = new[] { "Name" } }
        );
        Arrange("set_entity_property", new { table_name = "Customer", description = "Buyers" });

        var (result, success) = Exec("get_diagram_summary", new { });

        success.Should().BeTrue();
        result.Should().Contain("\n", "複数行の応答を作れている");
        result.Should().NotContain("\r", "改行の書き方をホストで食い違わせない");
    }
}
