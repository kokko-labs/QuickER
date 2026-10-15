using System.IO;
using System.Text.Json;
using AwesomeAssertions;
using QuickER.Dialects;
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

    /// <summary>
    /// 既定名が別の組と同じになる 2 本目（子 <c>A_B</c>・親 <c>C</c> の後の、子 <c>A</c>・親 <c>B_C</c>＝どちらも
    /// <c>FK_A_B_C</c>）に、GUI の手作成・内蔵チャット・MCP が同じ一意な名前を付け、DDL の制約名が重複しないことを検証する
    /// </summary>
    [Fact(
        DisplayName = "別の組と既定名が同じになる外部キーに、GUI の手作成・内蔵チャット・MCP が同じ一意な名前を付ける"
    )]
    public void UniqueForeignKeyName_IsTheSameOnEveryCreationPath()
    {
        (string Tool, object Args)[] tables =
        [
            .. KeyedTable("C", "CId"),
            .. KeyedTable("A_B", "A_BId", "CId"),
            .. KeyedTable("B_C", "B_CId"),
            .. KeyedTable("A", "AId", "B_CId"),
        ];
        (string Tool, object Args)[] relationships =
        [
            (
                "add_relationship",
                new
                {
                    source_table = "C",
                    target_table = "A_B",
                    relationship_type = "OneToMany",
                }
            ),
            (
                "add_relationship",
                new
                {
                    source_table = "B_C",
                    target_table = "A",
                    relationship_type = "OneToMany",
                }
            ),
        ];
        string[] expected = ["FK_A_B_C", "FK_A_B_C_B_CId"];

        // GUI の手作成（リレーション作成モードで親 → 子の順にクリック）
        var manual = RunGuiTools(tables);

        foreach (var (parent, child) in new[] { ("C", "A_B"), ("B_C", "A") })
        {
            manual.StartAddOneToManyCommand.Execute(null);
            manual.OnEntityClicked(manual.Entities.Single(entity => entity.TableName == parent));
            manual.OnEntityClicked(manual.Entities.Single(entity => entity.TableName == child));
        }

        // 内蔵チャット・MCP のツール
        var chat = RunGuiTools([.. tables, .. relationships]);
        var mcp = JsonStorageService.Load(RunDocumentTools([.. tables, .. relationships])).Schema;

        manual
            .Relationships.Select(relationship => relationship.ConstraintName)
            .Should()
            .Equal(expected, "GUI の手作成");
        chat.Relationships.Select(relationship => relationship.ConstraintName)
            .Should()
            .Equal(expected, "内蔵チャットのツール");
        mcp.Relationships.Select(relationship => relationship.ConstraintName)
            .Should()
            .Equal(expected, "MCP のツール");

        var ddl = new SqlServerDdlGenerator().Build(manual.ToDiagramModel());

        foreach (var name in expected)
        {
            ddl.Should().Contain($"[{name}]", "DDL は付けた名前で外部キーを作る");
        }
    }

    /// <summary>int の主キー 1 列と、残りの int 列を持つテーブルを作るツール呼び出し</summary>
    private static (string Tool, object Args)[] KeyedTable(
        string tableName,
        string keyColumn,
        params string[] otherColumns
    ) =>
        [
            ("add_entity", new { table_name = tableName }),
            (
                "add_column",
                new
                {
                    table_name = tableName,
                    column_name = keyColumn,
                    data_type = "int",
                    is_primary_key = true,
                    is_nullable = false,
                }
            ),
            .. otherColumns.Select(column =>
                (
                    "add_column",
                    (object)
                        new
                        {
                            table_name = tableName,
                            column_name = column,
                            data_type = "int",
                            is_primary_key = false,
                            is_nullable = false,
                        }
                )
            ),
        ];

    /// <summary>
    /// ツールが衝突の判定に使う既存の制約名（<see cref="ConstraintNames.CollectExisting"/>）が、DDL が実際に付ける
    /// 名前と一致し、役割の違う 2 本目の外部キーが DDL でも別の名前になることを検証する
    /// </summary>
    [Fact(
        DisplayName = "スキーマ付きのテーブル名で、ツールが数える既存の制約名は DDL が付ける名前と一致する"
    )]
    public void ExistingConstraintNames_MatchDdl()
    {
        var file = RunDocumentTools([
            .. TableSteps(),
            (
                "add_column",
                new
                {
                    table_name = Child,
                    column_name = "ShipToCustomerId",
                    data_type = "int",
                    is_primary_key = false,
                    is_nullable = false,
                }
            ),
            AddRelationship(),
            (
                "add_relationship",
                new
                {
                    source_table = Parent,
                    source_columns = new[] { "CustomerId" },
                    target_table = Child,
                    target_columns = new[] { "ShipToCustomerId" },
                    relationship_type = "OneToMany",
                }
            ),
            AddUnique(),
        ]);
        var schema = JsonStorageService.Load(file).Schema;
        var ddl = new SqlServerDdlGenerator().Build(schema);
        var existing = ConstraintNames.CollectExisting(
            schema,
            IdentifierLengthLimits.ForDbms(schema.TargetDbms)
        );

        existing
            .Should()
            .BeEquivalentTo(
                "FK_dbo_Order_dbo_Customer",
                "FK_dbo_Order_dbo_Customer_ShipToCustomerId",
                "PK_dbo_Customer",
                "PK_dbo_Order",
                "UQ_dbo_Order_code"
            );

        foreach (var name in existing)
        {
            ddl.Should().Contain($"[{name}]", "ツールが数える名前は DDL が付ける名前と同じ");
        }
    }
}
