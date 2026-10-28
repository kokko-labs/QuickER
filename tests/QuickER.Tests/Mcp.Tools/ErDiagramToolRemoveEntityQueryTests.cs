using AwesomeAssertions;
using QuickER.Mcp.Tools;
using QuickER.Services;
using QuickER.Tests.TestSupport;

namespace QuickER.Tests.Mcp.Tools;

/// <summary>
/// <c>remove_entity</c> がそのテーブルの名前付きクエリも取り除くことを、GUI 側 <see cref="ErDiagramDynamicTools"/> と
/// MCP の <see cref="DocumentErDiagramToolHost"/> の両方で検証する。
/// </summary>
/// <remarks>
/// クエリはテーブル単位で定義されるので、テーブルを消したあとに残すと <c>remove_query</c>（テーブル名で照合）でも
/// クエリ定義ダイアログでも触れない孤児になる。パリティテストの射影はクエリを含まないので、ここで両ホストを固定する。
/// GUI の Undo での復元は <c>EntityQueryRemovalCommandTests</c> が見る。
/// </remarks>
public sealed class ErDiagramToolRemoveEntityQueryTests : TwoHostToolTestBase
{
    [Theory(
        DisplayName = "remove_entity はそのテーブルの名前付きクエリも取り除き、応答で名指しする"
    )]
    [InlineData(ToolHostKind.Gui)]
    [InlineData(ToolHostKind.File)]
    public void RemoveEntity_RemovesQueriesOfThatTable(ToolHostKind host)
    {
        Setup(host);
        SeedQuery("Order", "FindByCustomer");
        SeedQuery("Customer", "FindByName");
        SeedQuery("Order", "CountAll");

        var (result, success) = Exec("remove_entity", new { table_name = "Order" });

        success.Should().BeTrue();
        result.Should().Contain("FindByCustomer").And.Contain("CountAll");
        QueryNames.Should().Equal("FindByName");
    }

    [Theory(
        DisplayName = "remove_entity は他のテーブルのクエリを残し、クエリが無ければ従来の応答のまま"
    )]
    [InlineData(ToolHostKind.Gui)]
    [InlineData(ToolHostKind.File)]
    public void RemoveEntity_WithoutQueries_KeepsOtherTablesQueries(ToolHostKind host)
    {
        Setup(host);
        SeedQuery("Customer", "FindByName");

        var (result, success) = Exec("remove_entity", new { table_name = "Order" });

        success.Should().BeTrue();
        result.Should().NotContain("FindByName");
        QueryNames.Should().Equal("FindByName");
    }

    [Fact(DisplayName = "remove_entity（MCP）はクエリが無いとき従来と同じ文言を返す")]
    public void RemoveEntity_File_WithoutQueries_KeepsPreviousMessage()
    {
        Setup(ToolHostKind.File);

        Exec("remove_entity", new { table_name = "Order" })
            .Result.Should()
            .Be("Removed table 'Order'.");
    }
}
