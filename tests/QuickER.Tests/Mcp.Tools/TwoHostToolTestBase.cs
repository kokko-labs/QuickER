using AwesomeAssertions;
using QuickER.Mcp.Tools;
using QuickER.Model;
using QuickER.Services;
using QuickER.Tests.TestSupport;
using QuickER.ViewModels;

namespace QuickER.Tests.Mcp.Tools;

/// <summary>
/// 同じツール呼び出しを GUI 側 <see cref="ErDiagramDynamicTools"/>（＋<see cref="MainViewModel"/>）と
/// MCP の <see cref="DocumentErDiagramToolHost"/>（＋一時ファイル）のどちらか一方へ流すテストの共通部品。
/// </summary>
/// <remarks>
/// 派生クラスのテストは <see cref="ToolHostKind"/> を引数に取る Theory にして、両ホストへ同じ表明を流す
/// （パリティテストは成否と最終的な図の一致しか見ないので、「両方とも拒否する」「両方ともクエリを消す」などは
/// 派生クラスで固定する）。ホストの切り替えとホストごとの図の扱いは <see cref="TwoHostToolHarness"/> のセッションが持つ。
/// </remarks>
public abstract class TwoHostToolTestBase : IDisposable
{
    /// <summary>このテストで使うホストのセッション（<see cref="Setup"/> で開く）</summary>
    private ToolHostSession? _session;

    public void Dispose() => _session?.Dispose();

    /// <summary>選んだホストのセッション</summary>
    private ToolHostSession Session =>
        _session ?? throw new InvalidOperationException("Setup でホストを選んでから使う");

    /// <summary>選んだホストでツールを実行する</summary>
    protected (string Result, bool Success) Exec(string tool, object args) =>
        Session.Exec(tool, args);

    /// <summary>選んだホストの現在の図</summary>
    protected ErDiagram Schema => Session.Schema;

    /// <summary>選んだホストで成功を確かめながらツールを実行する（前提の組み立て用）</summary>
    protected void Arrange(string tool, object args)
    {
        var (result, success) = Exec(tool, args);
        success.Should().BeTrue($"arrange step {tool} must succeed: {result}");
    }

    /// <summary>
    /// ホストを選び、Customer(CustomerId PK, Name) と Order(OrderId PK, CustomerId, ShipToCustomerId) の図を用意する
    /// </summary>
    protected void Setup(ToolHostKind host)
    {
        _session = TwoHostToolHarness.Open(host, fileTargetDbms: "sqlserver");

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
    protected void SeedQuery(string tableName, string queryName) =>
        Session.AddQuery(
            new QueryDefinition
            {
                EntityId = EntityNamed(tableName).Id,
                Name = queryName,
                Returns = QueryReturnShape.List,
            }
        );

    /// <summary>
    /// 選んだホストの図のリレーションの制約名をすべて消す（名前の無いリレーションはツールでは作れず、取込・古いファイル・
    /// 手編集でしか生じないので、図へ直接入れる）
    /// </summary>
    protected void ClearConstraintNames() => Session.ClearConstraintNames();

    /// <summary>選んだホストの図にある名前付きクエリの名前（図の中の並び順）</summary>
    protected List<string> QueryNames => Session.QueryNames;
}
