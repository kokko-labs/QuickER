using System.IO;
using System.Linq;
using System.Text.Json;
using AwesomeAssertions;
using QuickER.Cli;
using QuickER.Mcp.Tools;

namespace QuickER.Tests.Cli;

/// <summary>
/// MCP のクエリ定義ツール（<see cref="DocumentErDiagramToolHost"/> の <c>set_query</c>）で定義した名前付きクエリが、
/// CLI のコード生成ツール（<see cref="CodeGenToolSet"/> の <c>generate_csharp</c>）で Repository のクエリメソッドとして
/// 生成されることを end-to-end で検証する。
/// </summary>
public sealed class McpQueryGenerationIntegrationTests : IDisposable
{
    private readonly string _dir = Path.Combine(
        Path.GetTempPath(),
        "quicker-mcp-query-gen-" + Guid.NewGuid().ToString("N")
    );

    public McpQueryGenerationIntegrationTests()
    {
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (IOException)
        {
            // クリーンアップ失敗はテスト結果に影響させない
        }
    }

    private static (string Result, bool Success) Tool(string tool, string file, object args) =>
        DocumentErDiagramToolHost.Execute(tool, file, JsonSerializer.SerializeToElement(args));

    [Fact(
        DisplayName = "set_query で定義したクエリが generate_csharp でクエリメソッドとして生成される"
    )]
    public void SetQuery_ThenGenerateCSharp_EmitsQueryMethods()
    {
        var file = Path.Combine(_dir, "diagram.json");

        // --- set_query 相当の図を組み立てる（QueryFixture の GetByCustomer / CountByCustomer 相当） ---
        Tool(DocumentErDiagramToolHost.CreateDiagramToolName, file, new { target_dbms = "sqlite" })
            .Success.Should()
            .BeTrue();
        Tool("add_entity", file, new { table_name = "orders" });
        AddColumn(file, "order_id", "int", isPrimaryKey: true);
        AddColumn(file, "customer_id", "int");
        AddColumn(file, "amount", "decimal(10,2)");

        Tool(
            DocumentErDiagramToolHost.SetQueryToolName,
            file,
            new
            {
                table_name = "orders",
                query_name = "GetByCustomer",
                returns = "list",
                condition = "customer_id = @customerId",
                parameters = new[] { new { name = "customerId", type = "int32" } },
                order_by = new[] { new { column = "order_id", descending = true } },
            }
        )
            .Success.Should()
            .BeTrue();

        Tool(
            DocumentErDiagramToolHost.SetQueryToolName,
            file,
            new
            {
                table_name = "orders",
                query_name = "CountByCustomer",
                returns = "count",
                condition = "customer_id = @customerId",
                parameters = new[] { new { name = "customerId", type = "int32" } },
            }
        )
            .Success.Should()
            .BeTrue();

        // --- generate_csharp（QuickER 版 Repository 有効）を実行 ---
        var outDir = Path.Combine(_dir, "out");
        var configPath = Path.Combine(_dir, "quicker.json");
        File.WriteAllText(configPath, """{ "GenerateRepositories": true }""");

        var (result, success) = CodeGenToolSet
            .Create()
            .Execute(
                "generate_csharp",
                JsonSerializer.Serialize(
                    new
                    {
                        file,
                        out_dir = outDir,
                        config = configPath,
                    }
                )
            );

        success.Should().BeTrue(result);

        // --- 生成 C# にクエリメソッド（Async 付与形）が含まれる ---
        var code = string.Join("\n", Directory.GetFiles(outDir, "*.g.cs").Select(File.ReadAllText));
        code.Should().Contain("GetByCustomerAsync");
        code.Should().Contain("CountByCustomerAsync");
    }

    private static void AddColumn(
        string file,
        string name,
        string dataType,
        bool isPrimaryKey = false
    ) =>
        Tool(
            "add_column",
            file,
            new
            {
                table_name = "orders",
                column_name = name,
                data_type = dataType,
                is_primary_key = isPrimaryKey,
                is_nullable = !isPrimaryKey,
            }
        );
}
