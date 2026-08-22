using AwesomeAssertions;
using QuickER.AI.Chat;
using QuickER.Mcp.Tools;
using QuickER.Model;
using QuickER.Tests.TestDoubles;

namespace QuickER.Tests.AI.Chat;

/// <summary>
/// <see cref="ErDiagramHostChatAdapter"/> が名前付きクエリツール（set_query / list_queries / remove_query）を
/// アダプタ層で捕捉し、共有コア <see cref="QueryToolCore"/> で処理して成功時のみ
/// <see cref="QuickER.Extensibility.IErDiagramHost.ReplaceQueries"/> へ書き戻すことを検証するテストクラス。
/// </summary>
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

    /// <summary>set_query 成功で新規クエリが ReplaceQueries に反映されることを検証する</summary>
    [Fact(DisplayName = "set_query 成功は ReplaceQueries に反映される")]
    public void SetQuery_Success_ReflectsInReplaceQueries()
    {
        var diagram = BuildDiagram(out _);
        var (adapter, host) = CreateAdapter(diagram);

        var (_, success) = adapter.ToolHost.Execute(
            QueryToolCore.SetQueryToolName,
            """{"table_name":"Order","query_name":"GetById","returns":"single"}"""
        );

        success.Should().BeTrue();
        host.LastReplacedQueries.Should().NotBeNull();
        host.LastReplacedQueries!.Should().ContainSingle(query => query.Name == "GetById");
    }

    /// <summary>set_query の upsert が既存クエリの Id を温存して置換することを検証する</summary>
    [Fact(DisplayName = "set_query の upsert は既存クエリの Id を温存する")]
    public void SetQuery_Upsert_PreservesId()
    {
        var diagram = BuildDiagram(out var order);
        var existingId = Guid.NewGuid();
        diagram.Queries.Add(
            new QueryDefinition
            {
                Id = existingId,
                EntityId = order.Id,
                Name = "GetById",
                Returns = QueryReturnShape.List,
            }
        );
        var (adapter, host) = CreateAdapter(diagram);

        var (_, success) = adapter.ToolHost.Execute(
            QueryToolCore.SetQueryToolName,
            """{"table_name":"Order","query_name":"getbyid","returns":"single"}"""
        );

        success.Should().BeTrue();
        var replaced = host.LastReplacedQueries!.Single(query =>
            string.Equals(query.Name, "GetById", StringComparison.OrdinalIgnoreCase)
        );
        replaced.Id.Should().Be(existingId, "upsert は照合一致時に Id を温存する");
        replaced.Returns.Should().Be(QueryReturnShape.Single, "戻り形は新しい定義で置換される");
    }

    /// <summary>set_query の検証失敗時は ReplaceQueries を呼ばない（全か無か）ことを検証する</summary>
    [Fact(DisplayName = "set_query の検証失敗は ReplaceQueries を呼ばない")]
    public void SetQuery_ValidationFailure_DoesNotCallReplaceQueries()
    {
        var diagram = BuildDiagram(out _);
        var (adapter, host) = CreateAdapter(diagram);

        // returns=scalar は scalar_type が必須 → 検証失敗
        var (_, success) = adapter.ToolHost.Execute(
            QueryToolCore.SetQueryToolName,
            """{"table_name":"Order","query_name":"Bad","returns":"scalar"}"""
        );

        success.Should().BeFalse();
        host.LastReplacedQueries.Should().BeNull("検証失敗時は書き戻さない");
        diagram.Queries.Should().BeEmpty("図のクエリは変更されない");
    }

    /// <summary>存在しないテーブルへの set_query は失敗し ReplaceQueries を呼ばないことを検証する</summary>
    [Fact(DisplayName = "set_query で存在しないテーブルは失敗する")]
    public void SetQuery_UnknownTable_Fails()
    {
        var diagram = BuildDiagram(out _);
        var (adapter, host) = CreateAdapter(diagram);

        var (_, success) = adapter.ToolHost.Execute(
            QueryToolCore.SetQueryToolName,
            """{"table_name":"NoSuchTable","query_name":"X","returns":"list"}"""
        );

        success.Should().BeFalse();
        host.LastReplacedQueries.Should().BeNull();
    }

    /// <summary>list_queries が現在の図のクエリを要約して返し、書き戻しを行わないことを検証する</summary>
    [Fact(DisplayName = "list_queries はクエリ要約を返し書き戻さない")]
    public void ListQueries_ReturnsSummary_WithoutReplace()
    {
        var diagram = BuildDiagram(out var order);
        diagram.Queries.Add(
            new QueryDefinition
            {
                EntityId = order.Id,
                Name = "GetById",
                Returns = QueryReturnShape.Single,
            }
        );
        var (adapter, host) = CreateAdapter(diagram);

        var (result, success) = adapter.ToolHost.Execute(QueryToolCore.ListQueriesToolName, "{}");

        success.Should().BeTrue();
        result.Should().Contain("Order");
        result.Should().Contain("GetById");
        host.LastReplacedQueries.Should().BeNull("読み取り系は書き戻さない");
    }

    /// <summary>remove_query 成功で対象クエリが ReplaceQueries から除かれることを検証する</summary>
    [Fact(DisplayName = "remove_query 成功は ReplaceQueries に反映される")]
    public void RemoveQuery_Success_ReflectsInReplaceQueries()
    {
        var diagram = BuildDiagram(out var order);
        diagram.Queries.Add(
            new QueryDefinition
            {
                EntityId = order.Id,
                Name = "GetById",
                Returns = QueryReturnShape.Single,
            }
        );
        var (adapter, host) = CreateAdapter(diagram);

        var (_, success) = adapter.ToolHost.Execute(
            QueryToolCore.RemoveQueryToolName,
            """{"table_name":"Order","query_name":"GetById"}"""
        );

        success.Should().BeTrue();
        host.LastReplacedQueries.Should().NotBeNull();
        host.LastReplacedQueries!.Should().BeEmpty();
    }

    /// <summary>remove_query で存在しないクエリを指定すると失敗し書き戻さないことを検証する</summary>
    [Fact(DisplayName = "remove_query で不在クエリは失敗し書き戻さない")]
    public void RemoveQuery_NotFound_DoesNotCallReplaceQueries()
    {
        var diagram = BuildDiagram(out _);
        var (adapter, host) = CreateAdapter(diagram);

        var (_, success) = adapter.ToolHost.Execute(
            QueryToolCore.RemoveQueryToolName,
            """{"table_name":"Order","query_name":"Missing"}"""
        );

        success.Should().BeFalse();
        host.LastReplacedQueries.Should().BeNull();
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
