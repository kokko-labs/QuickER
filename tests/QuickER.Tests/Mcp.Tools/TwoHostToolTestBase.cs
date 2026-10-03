using System.IO;
using System.Text.Json;
using AwesomeAssertions;
using QuickER.Documents;
using QuickER.Mcp.Tools;
using QuickER.Model;
using QuickER.Services;
using QuickER.ViewModels;

namespace QuickER.Tests.Mcp.Tools;

/// <summary>
/// 同じツール呼び出しを GUI 側 <see cref="ErDiagramDynamicTools"/>（＋<see cref="MainViewModel"/>）と
/// MCP の <see cref="DocumentErDiagramToolHost"/>（＋一時ファイル）のどちらか一方へ流すテストの共通部品。
/// </summary>
/// <remarks>
/// 派生クラスのテストは <see cref="HostKind"/> を引数に取る Theory にして、両ホストへ同じ表明を流す
/// （パリティテストは成否と最終的な図の一致しか見ないので、「両方とも拒否する」「両方ともクエリを消す」などは
/// 派生クラスで固定する）。
/// </remarks>
public abstract class TwoHostToolTestBase : IDisposable
{
    /// <summary>ツールを実行するホストの種類</summary>
    public enum HostKind
    {
        /// <summary>GUI 側（<see cref="ErDiagramDynamicTools"/>＋<see cref="MainViewModel"/>）</summary>
        Gui,

        /// <summary>MCP 側（<see cref="DocumentErDiagramToolHost"/>＋一時ファイル）</summary>
        File,
    }

    /// <summary>各テスト専用の一時ディレクトリ</summary>
    private readonly string _dir = Path.Combine(
        Path.GetTempPath(),
        "quicker-two-host-" + Guid.NewGuid().ToString("N")
    );

    /// <summary>GUI 側ホストの ViewModel</summary>
    private readonly MainViewModel _vm = new();

    /// <summary>MCP 側ホストの図ファイル</summary>
    private readonly string _file;

    /// <summary>このテストで使うホスト</summary>
    private HostKind _host;

    protected TwoHostToolTestBase()
    {
        Directory.CreateDirectory(_dir);
        _file = Path.Combine(_dir, "diagram.json");
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

    /// <summary>選んだホストでツールを実行する</summary>
    protected (string Result, bool Success) Exec(string tool, object args)
    {
        var element = JsonSerializer.SerializeToElement(args);

        return _host == HostKind.Gui
            ? ErDiagramDynamicTools.Execute(tool, element, _vm)
            : DocumentErDiagramToolHost.Execute(tool, _file, element);
    }

    /// <summary>選んだホストの現在の図</summary>
    protected ErDiagram Schema =>
        _host == HostKind.Gui ? _vm.ToDiagramModel() : JsonStorageService.Load(_file).Schema;

    /// <summary>選んだホストで成功を確かめながらツールを実行する（前提の組み立て用）</summary>
    protected void Arrange(string tool, object args)
    {
        var (result, success) = Exec(tool, args);
        success.Should().BeTrue($"arrange step {tool} must succeed: {result}");
    }

    /// <summary>
    /// ホストを選び、Customer(CustomerId PK, Name) と Order(OrderId PK, CustomerId, ShipToCustomerId) の図を用意する
    /// </summary>
    protected void Setup(HostKind host)
    {
        _host = host;

        if (host == HostKind.File)
        {
            DocumentErDiagramToolHost
                .Execute(
                    DocumentErDiagramToolHost.CreateDiagramToolName,
                    _file,
                    JsonSerializer.SerializeToElement(new { target_dbms = "sqlserver" })
                )
                .Success.Should()
                .BeTrue();
        }

        Arrange("add_entity", new { table_name = "Customer" });
        Arrange(
            "add_column",
            new
            {
                table_name = "Customer",
                column_name = "CustomerId",
                data_type = "int",
                is_primary_key = true,
            }
        );
        Arrange(
            "add_column",
            new
            {
                table_name = "Customer",
                column_name = "Name",
                data_type = "nvarchar(50)",
            }
        );
        Arrange("add_entity", new { table_name = "Order" });
        Arrange(
            "add_column",
            new
            {
                table_name = "Order",
                column_name = "OrderId",
                data_type = "int",
                is_primary_key = true,
            }
        );
        Arrange(
            "add_column",
            new
            {
                table_name = "Order",
                column_name = "CustomerId",
                data_type = "int",
            }
        );
        Arrange(
            "add_column",
            new
            {
                table_name = "Order",
                column_name = "ShipToCustomerId",
                data_type = "int",
            }
        );
    }

    /// <summary>図の中のテーブルを名前（大文字小文字まで一致）で引く</summary>
    protected Entity EntityNamed(string tableName) =>
        Schema.Entities.Single(entity => entity.TableName == tableName);

    /// <summary>選んだホストの図へ名前付きクエリを足す（クエリはツールの対象外の経路で作られるので、図へ直接入れる）</summary>
    /// <param name="tableName">クエリを定義するテーブル名</param>
    /// <param name="queryName">クエリ名</param>
    protected void SeedQuery(string tableName, string queryName)
    {
        var query = new QueryDefinition
        {
            EntityId = EntityNamed(tableName).Id,
            Name = queryName,
            Returns = QueryReturnShape.List,
        };

        if (_host == HostKind.Gui)
        {
            _vm.Queries.Add(query);

            return;
        }

        var document = JsonStorageService.Load(_file);
        document.Schema.Queries.Add(query);
        JsonStorageService.SaveAtomic(_file, document);
    }

    /// <summary>選んだホストの図にある名前付きクエリの名前（図の中の並び順）</summary>
    protected List<string> QueryNames =>
        _host == HostKind.Gui
            ? _vm.Queries.Select(query => query.Name).ToList()
            : JsonStorageService.Load(_file).Schema.Queries.Select(query => query.Name).ToList();
}
