using System.IO;
using System.Text.Json;
using AwesomeAssertions;
using QuickER.Documents;
using QuickER.Mcp.Tools;
using QuickER.Model;
using QuickER.Tests.TestSupport;

namespace QuickER.Tests.Mcp.Tools;

/// <summary>
/// ファイルベース実行ホスト（<see cref="DocumentErDiagramToolHost"/>）の <c>set_unique_constraint</c> が、同じ列集合の
/// upsert で制約の Id を温存することを検証する。それ以外の意味論は 2 ホストの表
/// （<c>ToolScenarioRows.UniqueConstraint.cs</c>）が見る。
/// </summary>
public sealed class DocumentErDiagramUniqueConstraintToolsTests : IDisposable
{
    /// <summary>各テスト専用の一時ディレクトリ</summary>
    private readonly string _dir = Path.Combine(
        Path.GetTempPath(),
        "quicker-mcp-unique-" + Guid.NewGuid().ToString("N")
    );

    public DocumentErDiagramUniqueConstraintToolsTests()
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

    /// <summary>引数オブジェクトを JsonElement 化してツールを実行する</summary>
    private static (string Result, bool Success) Exec(string tool, string file, object args) =>
        DocumentErDiagramToolHost.Execute(tool, file, JsonSerializer.SerializeToElement(args));

    /// <summary>Customer(CustomerId PK / Email / TenantId / Code) を持つ図ファイルを用意する</summary>
    private string CreateCustomerFile()
    {
        var file = Path.Combine(_dir, "diagram.json");
        DiagramFiles.Create(file, "sqlite");
        Exec("add_entity", file, new { table_name = "Customer" });
        Exec(
            "add_column",
            file,
            new
            {
                table_name = "Customer",
                column_name = "CustomerId",
                data_type = "int",
                is_primary_key = true,
                is_nullable = false,
            }
        );

        foreach (var columnName in new[] { "Email", "TenantId", "Code" })
        {
            Exec(
                "add_column",
                file,
                new
                {
                    table_name = "Customer",
                    column_name = columnName,
                    data_type = "nvarchar(100)",
                    is_nullable = false,
                }
            );
        }

        return file;
    }

    /// <summary>保存済みファイルから Customer エンティティを読み出す</summary>
    private static Entity LoadCustomer(string file) =>
        JsonStorageService
            .Load(file)
            .Schema.Entities.Single(entity => entity.TableName == "Customer");

    [Fact(
        DisplayName = "set_unique_constraint は同じ列集合（順序・大小無視）を upsert して Id を温存する"
    )]
    public void SetUniqueConstraint_SameColumnSet_UpsertsPreservingId()
    {
        var file = CreateCustomerFile();
        Exec(
            "set_unique_constraint",
            file,
            new { table_name = "Customer", columns = new[] { "TenantId", "Code" } }
        );
        var originalId = LoadCustomer(file).UniqueConstraints.Single().Id;

        // 列順を入れ替え・大小も変えて名前つきで再定義する
        var (result, success) = Exec(
            "set_unique_constraint",
            file,
            new
            {
                table_name = "Customer",
                columns = new[] { "code", "tenantid" },
                name = "UQ_Renamed",
            }
        );

        success.Should().BeTrue();
        result.Should().Contain("Updated");

        var customer = LoadCustomer(file);
        var constraint = customer.UniqueConstraints.Should().ContainSingle().Subject;
        constraint.Id.Should().Be(originalId);
        constraint.Name.Should().Be("UQ_Renamed");
        // 列順は最後の呼び出しの宣言順に従う
        constraint
            .ColumnIds.Should()
            .Equal(
                customer.Columns.Single(column => column.Name == "Code").Id,
                customer.Columns.Single(column => column.Name == "TenantId").Id
            );
    }
}
