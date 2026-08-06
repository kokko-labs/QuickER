using System.IO;
using System.Text.Json;
using AwesomeAssertions;
using QuickER.Documents;
using QuickER.Mcp.Tools;
using QuickER.Model;

namespace QuickER.Tests.Mcp.Tools;

/// <summary>
/// ファイルベース実行ホスト（<see cref="DocumentErDiagramToolHost"/>）の一意制約ツール
/// （<c>set_unique_constraint</c> / <c>remove_unique_constraint</c>）と、
/// <c>remove_column</c> の一意制約カスケード削除を検証する。
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
        Exec(DocumentErDiagramToolHost.CreateDiagramToolName, file, new { target_dbms = "sqlite" })
            .Success.Should()
            .BeTrue();
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

    [Fact(DisplayName = "set_unique_constraint は単一列の一意制約を追加する")]
    public void SetUniqueConstraint_AddsSingleColumnConstraint()
    {
        var file = CreateCustomerFile();

        var (result, success) = Exec(
            "set_unique_constraint",
            file,
            new { table_name = "Customer", columns = new[] { "Email" } }
        );

        success.Should().BeTrue();
        result.Should().Contain("Email");

        var customer = LoadCustomer(file);
        var constraint = customer.UniqueConstraints.Should().ContainSingle().Subject;
        constraint.Name.Should().BeNull();
        constraint
            .ColumnIds.Should()
            .Equal(customer.Columns.Single(column => column.Name == "Email").Id);
    }

    [Fact(DisplayName = "set_unique_constraint は複合制約を宣言順どおり追加する")]
    public void SetUniqueConstraint_AddsCompositeConstraintInDeclaredOrder()
    {
        var file = CreateCustomerFile();

        var (_, success) = Exec(
            "set_unique_constraint",
            file,
            new
            {
                table_name = "Customer",
                columns = new[] { "TenantId", "Code" },
                name = "UQ_Customer_Tenant",
            }
        );

        success.Should().BeTrue();

        var customer = LoadCustomer(file);
        var constraint = customer.UniqueConstraints.Should().ContainSingle().Subject;
        constraint.Name.Should().Be("UQ_Customer_Tenant");
        constraint
            .ColumnIds.Should()
            .Equal(
                customer.Columns.Single(column => column.Name == "TenantId").Id,
                customer.Columns.Single(column => column.Name == "Code").Id
            );
    }

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

    [Fact(DisplayName = "set_unique_constraint は名前の省略で既存の名前を解除する")]
    public void SetUniqueConstraint_OmittedName_ClearsExistingName()
    {
        var file = CreateCustomerFile();
        Exec(
            "set_unique_constraint",
            file,
            new
            {
                table_name = "Customer",
                columns = new[] { "Email" },
                name = "UQ_Named",
            }
        );

        var (_, success) = Exec(
            "set_unique_constraint",
            file,
            new { table_name = "Customer", columns = new[] { "Email" } }
        );

        success.Should().BeTrue();
        LoadCustomer(file).UniqueConstraints.Single().Name.Should().BeNull();
    }

    [Fact(DisplayName = "set_unique_constraint は存在しない列名をエラーにし図を変更しない")]
    public void SetUniqueConstraint_UnknownColumn_Fails()
    {
        var file = CreateCustomerFile();

        var (result, success) = Exec(
            "set_unique_constraint",
            file,
            new { table_name = "Customer", columns = new[] { "Email", "NoSuchColumn" } }
        );

        success.Should().BeFalse();
        result.Should().Contain("NoSuchColumn");
        LoadCustomer(file).UniqueConstraints.Should().BeEmpty();
    }

    [Fact(DisplayName = "set_unique_constraint は列の重複指定・空配列をエラーにする")]
    public void SetUniqueConstraint_InvalidColumns_Fails()
    {
        var file = CreateCustomerFile();

        Exec(
            "set_unique_constraint",
            file,
            new { table_name = "Customer", columns = new[] { "Email", "email" } }
        )
            .Success.Should()
            .BeFalse();
        Exec(
            "set_unique_constraint",
            file,
            new { table_name = "Customer", columns = Array.Empty<string>() }
        )
            .Success.Should()
            .BeFalse();
        Exec("set_unique_constraint", file, new { table_name = "Customer" })
            .Success.Should()
            .BeFalse();

        LoadCustomer(file).UniqueConstraints.Should().BeEmpty();
    }

    [Fact(DisplayName = "remove_unique_constraint は列集合で特定した制約を削除する")]
    public void RemoveUniqueConstraint_RemovesMatchingConstraint()
    {
        var file = CreateCustomerFile();
        Exec(
            "set_unique_constraint",
            file,
            new { table_name = "Customer", columns = new[] { "Email" } }
        );
        Exec(
            "set_unique_constraint",
            file,
            new { table_name = "Customer", columns = new[] { "TenantId", "Code" } }
        );

        var (_, success) = Exec(
            "remove_unique_constraint",
            file,
            new { table_name = "Customer", columns = new[] { "Code", "TenantId" } }
        );

        success.Should().BeTrue();

        var customer = LoadCustomer(file);
        customer.UniqueConstraints.Should().ContainSingle();
        customer
            .UniqueConstraints[0]
            .ColumnIds.Should()
            .Equal(customer.Columns.Single(column => column.Name == "Email").Id);
    }

    [Fact(DisplayName = "remove_unique_constraint は列集合が一致しなければエラーにする")]
    public void RemoveUniqueConstraint_NoExactMatch_Fails()
    {
        var file = CreateCustomerFile();
        Exec(
            "set_unique_constraint",
            file,
            new { table_name = "Customer", columns = new[] { "TenantId", "Code" } }
        );

        var (result, success) = Exec(
            "remove_unique_constraint",
            file,
            new { table_name = "Customer", columns = new[] { "TenantId" } }
        );

        success.Should().BeFalse();
        result.Should().Contain("no unique constraint");
        LoadCustomer(file).UniqueConstraints.Should().ContainSingle();
    }

    [Fact(DisplayName = "remove_column は削除列を含む一意制約を制約ごと削除する")]
    public void RemoveColumn_CascadesToUniqueConstraints()
    {
        var file = CreateCustomerFile();
        Exec(
            "set_unique_constraint",
            file,
            new { table_name = "Customer", columns = new[] { "Email" } }
        );
        Exec(
            "set_unique_constraint",
            file,
            new { table_name = "Customer", columns = new[] { "TenantId", "Code" } }
        );

        var (_, success) = Exec(
            "remove_column",
            file,
            new { table_name = "Customer", column_name = "TenantId" }
        );

        success.Should().BeTrue();

        var customer = LoadCustomer(file);
        // TenantId を含む複合制約は消え、Email 単独の制約だけが残る（列を 1 つ失った制約を残さない）
        customer.UniqueConstraints.Should().ContainSingle();
        customer
            .UniqueConstraints[0]
            .ColumnIds.Should()
            .Equal(customer.Columns.Single(column => column.Name == "Email").Id);
    }

    [Fact(DisplayName = "get_diagram_summary は一意制約（名前 or 合成名・構成列）を出力する")]
    public void GetDiagramSummary_ListsUniqueConstraints()
    {
        var file = CreateCustomerFile();
        Exec(
            "set_unique_constraint",
            file,
            new { table_name = "Customer", columns = new[] { "Email" } }
        );
        Exec(
            "set_unique_constraint",
            file,
            new
            {
                table_name = "Customer",
                columns = new[] { "TenantId", "Code" },
                name = "UQ_Tenant_Code",
            }
        );

        var (result, success) = Exec("get_diagram_summary", file, new { });

        success.Should().BeTrue();
        result.Should().Contain("Unique constraints:");
        // 名前なしは DDL 生成と同じ合成名で表示する
        result.Should().Contain("UQ_Customer_Email (Email)");
        result.Should().Contain("UQ_Tenant_Code (TenantId, Code)");
    }
}
