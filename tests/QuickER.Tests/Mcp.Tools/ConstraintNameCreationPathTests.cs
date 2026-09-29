using System.IO;
using System.Text.Json;
using AwesomeAssertions;
using QuickER.Documents;
using QuickER.Mcp.Tools;
using QuickER.Model;
using QuickER.Provider.SqlServer;
using QuickER.Services;
using QuickER.ViewModels;

namespace QuickER.Tests.Mcp.Tools;

/// <summary>
/// 制約の既定名が、作成・表示の経路（GUI の手作成・内蔵チャットのツール・MCP のツール・DDL）で揃うことを検証するテストクラス
/// </summary>
/// <remarks>
/// <para>
/// GUI の手作成は <c>FK_{安全化した子}_{安全化した親}</c> を付けるのに、ツール 2 系統は素のテーブル名を連結していたため、
/// <c>dbo.Orders</c> のようなスキーマ付きの名前では作成経路によって制約名が割れていた。
/// 一意制約の合成名も同様に、DDL は安全化してから合成するのに、ツールの要約表示は素の名前から合成していた。
/// 規則は <see cref="ConstraintNames"/> と <see cref="UniqueConstraint.SynthesizeName"/> にまとめてある。
/// </para>
/// <para>
/// 既存の <c>ErDiagramToolHostParityTests</c> は 2 つのツールホストどうしを比べるので、両方が同じように素の連結へ
/// 戻っても緑のままになる。ここでは GUI の手作成と DDL を基準に、名前そのものを照合する。
/// </para>
/// </remarks>
public sealed class ConstraintNameCreationPathTests : IDisposable
{
    private const string Parent = "dbo.Customer";
    private const string Child = "dbo.Order";

    private readonly string _dir = Path.Combine(
        Path.GetTempPath(),
        "quicker-constraint-name-" + Guid.NewGuid().ToString("N")
    );

    public ConstraintNameCreationPathTests()
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
            // 後始末の失敗はテスト結果に影響させない
        }
    }

    /// <summary>親子 2 テーブル（子は一意制約の候補列 code を持つ）を作るツール呼び出し</summary>
    private static (string Tool, object Args)[] TableSteps() =>
        [
            ("add_entity", new { table_name = Parent }),
            (
                "add_column",
                new
                {
                    table_name = Parent,
                    column_name = "CustomerId",
                    data_type = "int",
                    is_primary_key = true,
                    is_nullable = false,
                }
            ),
            ("add_entity", new { table_name = Child }),
            (
                "add_column",
                new
                {
                    table_name = Child,
                    column_name = "OrderId",
                    data_type = "int",
                    is_primary_key = true,
                    is_nullable = false,
                }
            ),
            (
                "add_column",
                new
                {
                    table_name = Child,
                    column_name = "CustomerId",
                    data_type = "int",
                    is_primary_key = false,
                    is_nullable = false,
                }
            ),
            (
                "add_column",
                new
                {
                    table_name = Child,
                    column_name = "code",
                    data_type = "nvarchar(20)",
                    is_primary_key = false,
                    is_nullable = false,
                }
            ),
        ];

    private static (string Tool, object Args) AddRelationship() =>
        (
            "add_relationship",
            new
            {
                source_table = Parent,
                target_table = Child,
                relationship_type = "OneToMany",
            }
        );

    private static (string Tool, object Args) AddUnique() =>
        ("set_unique_constraint", new { table_name = Child, columns = new[] { "code" } });

    private static JsonElement Json(object args) => JsonSerializer.SerializeToElement(args);

    /// <summary>内蔵チャットのツールを実行した ViewModel を返す</summary>
    private static MainViewModel RunGuiTools(params (string Tool, object Args)[] steps)
    {
        var vm = new MainViewModel();

        foreach (var (tool, args) in steps)
        {
            ErDiagramDynamicTools
                .Execute(tool, Json(args), vm)
                .Success.Should()
                .BeTrue($"GUI: {tool}");
        }

        return vm;
    }

    /// <summary>MCP のツールを実行したファイルのパスを返す</summary>
    private string RunDocumentTools(params (string Tool, object Args)[] steps)
    {
        var file = Path.Combine(_dir, Guid.NewGuid().ToString("N") + ".json");
        DocumentErDiagramToolHost
            .Execute(
                DocumentErDiagramToolHost.CreateDiagramToolName,
                file,
                Json(new { target_dbms = "sqlserver" })
            )
            .Success.Should()
            .BeTrue();

        foreach (var (tool, args) in steps)
        {
            DocumentErDiagramToolHost
                .Execute(tool, file, Json(args))
                .Success.Should()
                .BeTrue($"MCP: {tool}");
        }

        return file;
    }

    [Fact(
        DisplayName = "スキーマ付きのテーブル名で、GUI の手作成・内蔵チャット・MCP が同じ外部キー名を付ける"
    )]
    public void ForeignKeyName_IsTheSameOnEveryCreationPath()
    {
        const string expected = "FK_dbo_Order_dbo_Customer";

        // GUI の手作成（リレーション作成モードで親 → 子の順にクリック）
        var manual = RunGuiTools(TableSteps());
        manual.StartAddOneToManyCommand.Execute(null);
        manual.OnEntityClicked(manual.Entities.Single(entity => entity.TableName == Parent));
        manual.OnEntityClicked(manual.Entities.Single(entity => entity.TableName == Child));

        // 内蔵チャット・MCP のツール
        var chat = RunGuiTools([.. TableSteps(), AddRelationship()]);
        var mcp = JsonStorageService
            .Load(RunDocumentTools([.. TableSteps(), AddRelationship()]))
            .Schema;

        manual.Relationships.Single().ConstraintName.Should().Be(expected, "GUI の手作成");
        chat.Relationships.Single().ConstraintName.Should().Be(expected, "内蔵チャットのツール");
        mcp.Relationships.Single().ConstraintName.Should().Be(expected, "MCP のツール");
    }

    [Fact(
        DisplayName = "スキーマ付きのテーブル名で、ツールの要約が一意制約の名前を DDL と同じ名前で示す"
    )]
    public void UniqueConstraintName_InSummaryMatchesDdl()
    {
        (string Tool, object Args)[] steps = [.. TableSteps(), AddUnique()];
        var chat = RunGuiTools(steps);
        var file = RunDocumentTools(steps);

        var ddl = new SqlServerDdlGenerator().Build(JsonStorageService.Load(file).Schema);
        const string expected = "UQ_dbo_Order_code";

        ddl.Should().Contain($"[{expected}]", "DDL は安全化した名前で一意制約を作る");
        ErDiagramDynamicTools
            .Execute("get_diagram_summary", Json(new { }), chat)
            .Result.Should()
            .Contain(expected, "内蔵チャットの要約");
        DocumentErDiagramToolHost
            .Execute("get_diagram_summary", file, Json(new { }))
            .Result.Should()
            .Contain(expected, "MCP の要約");
    }
}
