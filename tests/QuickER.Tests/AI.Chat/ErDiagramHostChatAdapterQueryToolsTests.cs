using AwesomeAssertions;
using QuickER.AI.Chat;
using QuickER.Mcp.Tools;
using QuickER.Model;
using QuickER.Tests.TestDoubles;

namespace QuickER.Tests.AI.Chat;

/// <summary>
/// <see cref="ErDiagramHostChatAdapter"/> の入口（名前付きクエリツール以外の素通り・読めない引数・根がオブジェクトでない引数）を
/// 検証するテストクラス。
/// </summary>
/// <remarks>
/// 名前付きクエリツール（set_query / list_queries / remove_query）を共有コア <see cref="QueryToolCore"/> で処理して成功時のみ
/// <see cref="QuickER.Extensibility.IErDiagramHost.ReplaceQueries"/> へ書き戻すことは、ツールの行
/// （<c>ToolScenarioRows</c> の名前付きクエリの行・GUI はこのアダプタを通る）が確かめる。
/// </remarks>
public class ErDiagramHostChatAdapterQueryToolsTests
{
    /// <summary>Order テーブル（PK + FK 候補列）を 1 つ持つ図を構築する</summary>
    private static ErDiagram BuildDiagram(out Entity order)
    {
        order = new Entity { TableName = "Order" };
        order.Columns.Add(
            new Column
            {
                Name = "OrderId",
                DataType = "int",
                IsPrimaryKey = true,
            }
        );
        order.Columns.Add(new Column { Name = "CustomerId", DataType = "int" });

        return new ErDiagram { Entities = { order } };
    }

    /// <summary>指定図を返すスタブホストとアダプタを組み立てる</summary>
    private static (ErDiagramHostChatAdapter Adapter, StubErDiagramHost Host) CreateAdapter(
        ErDiagram diagram
    )
    {
        var host = new StubErDiagramHost { DiagramToReturn = diagram };

        return (new ErDiagramHostChatAdapter(host), host);
    }

    /// <summary>非クエリツールはホストの ExecuteTool へ素通りすることを検証する</summary>
    [Fact(DisplayName = "非クエリツールは ExecuteTool へ素通りする")]
    public void NonQueryTool_PassesThroughToExecuteTool()
    {
        var diagram = BuildDiagram(out _);
        var host = new StubErDiagramHost
        {
            DiagramToReturn = diagram,
            ToolResultToReturn = ("added", true),
        };
        var adapter = new ErDiagramHostChatAdapter(host);

        var (result, success) = adapter.ToolHost.Execute(
            "add_entity",
            """{"table_name":"Customer"}"""
        );

        result.Should().Be("added");
        success.Should().BeTrue();
        host.LastToolName.Should().Be("add_entity");
        host.LastArgumentsJson.Should().Be("""{"table_name":"Customer"}""");
        host.LastReplacedQueries.Should().BeNull();
    }

    /// <summary>不正な引数 JSON はローカライズ済みエラーで失敗し、ホストへ委譲しないことを検証する</summary>
    [Fact(DisplayName = "set_query の不正引数 JSON はエラーで失敗する")]
    public void SetQuery_InvalidJson_Fails()
    {
        var diagram = BuildDiagram(out _);
        var (adapter, host) = CreateAdapter(diagram);

        var (_, success) = adapter.ToolHost.Execute(QueryToolCore.SetQueryToolName, "{not json");

        success.Should().BeFalse();
        host.LastReplacedQueries.Should().BeNull();
    }
}
