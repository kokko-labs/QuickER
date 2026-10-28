using System.IO;
using System.Text.Json;
using AwesomeAssertions;
using QuickER.Documents;
using QuickER.Mcp.Tools;
using QuickER.Tests.TestSupport;

namespace QuickER.Tests.Mcp.Tools;

/// <summary>
/// ファイル実行ホスト（<see cref="DocumentErDiagramToolHost"/>）が、リレーションから外れた子列の外部キー印を落とすことを
/// 3 つの削除経路（<c>remove_relationship</c> / 親の <c>remove_entity</c> / 親列の <c>remove_column</c>）で検証する。
/// </summary>
/// <remarks>
/// 規則は GUI（<c>MainViewModel.ApplyRelationshipColumnRules</c>）と同じ＝リレーションの列ペアの子列だった列は、
/// どの列ペアにも現れなくなったら印を落とす。列ペアにいない列へ手で付けた印は残す。
/// GUI 側の対は <c>ErDiagramDynamicToolsForeignKeyMarkTests</c>（同じシナリオ <see cref="SetupSteps"/> を流す）。
/// </remarks>
public sealed class DocumentErDiagramForeignKeyMarkTests : IDisposable
{
    /// <summary>各テスト専用の一時ディレクトリ</summary>
    private readonly string _dir = Path.Combine(
        Path.GetTempPath(),
        "quicker-mcp-fkmark-" + Guid.NewGuid().ToString("N")
    );

    public DocumentErDiagramForeignKeyMarkTests()
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

    /// <summary>主キー列の追加引数</summary>
    private static object PrimaryKey(string table, string column, string type = "int") =>
        new
        {
            table_name = table,
            column_name = column,
            data_type = type,
            is_primary_key = true,
            is_nullable = false,
        };

    /// <summary>非キー列の追加引数</summary>
    private static object Plain(string table, string column, string type = "int") =>
        new
        {
            table_name = table,
            column_name = column,
            data_type = type,
            is_nullable = false,
        };

    /// <summary>
    /// 両ホスト共通の準備手順。Customer(CustomerId) と Region(RegionId, Code の複合主キー) と Account(AccountId) が
    /// Order を参照する。Order.CustomerId は Customer と Account の 2 本から参照される（共有の子列）。
    /// </summary>
    /// <remarks>
    /// リレーション: Customer→Order（CustomerId→CustomerId・既定名）／Customer→Order（CustomerId→BillToCustomerId・
    /// FK_BillTo）／Region→Order（RegionId→RegionId, Code→RegionCode の複合）／Account→Order（AccountId→CustomerId）
    /// </remarks>
    internal static readonly (string Tool, object Args)[] SetupSteps =
    [
        ("add_entity", new { table_name = "Customer" }),
        ("add_column", PrimaryKey("Customer", "CustomerId")),
        ("add_entity", new { table_name = "Region" }),
        ("add_column", PrimaryKey("Region", "RegionId")),
        ("add_column", PrimaryKey("Region", "Code", "nvarchar(10)")),
        ("add_entity", new { table_name = "Account" }),
        ("add_column", PrimaryKey("Account", "AccountId")),
        ("add_entity", new { table_name = "Order" }),
        ("add_column", PrimaryKey("Order", "OrderId")),
        ("add_column", Plain("Order", "CustomerId")),
        ("add_column", Plain("Order", "BillToCustomerId")),
        ("add_column", Plain("Order", "RegionId")),
        ("add_column", Plain("Order", "RegionCode", "nvarchar(10)")),
        (
            "add_relationship",
            new
            {
                source_table = "Customer",
                source_columns = new[] { "CustomerId" },
                target_table = "Order",
                target_columns = new[] { "CustomerId" },
                relationship_type = "OneToMany",
            }
        ),
        (
            "add_relationship",
            new
            {
                source_table = "Customer",
                source_columns = new[] { "CustomerId" },
                target_table = "Order",
                target_columns = new[] { "BillToCustomerId" },
                relationship_type = "OneToMany",
                constraint_name = "FK_BillTo",
            }
        ),
        (
            "add_relationship",
            new
            {
                source_table = "Region",
                source_columns = new[] { "RegionId", "Code" },
                target_table = "Order",
                target_columns = new[] { "RegionId", "RegionCode" },
                relationship_type = "OneToMany",
            }
        ),
        (
            "add_relationship",
            new
            {
                source_table = "Account",
                source_columns = new[] { "AccountId" },
                target_table = "Order",
                target_columns = new[] { "CustomerId" },
                relationship_type = "OneToMany",
            }
        ),
    ];

    /// <summary>引数オブジェクトを JsonElement 化してツールを実行する</summary>
    private static (string Result, bool Success) Exec(string tool, string file, object args) =>
        DocumentErDiagramToolHost.Execute(tool, file, JsonSerializer.SerializeToElement(args));

    /// <summary>準備手順を流した図ファイルを作る</summary>
    private string CreateFile()
    {
        var file = Path.Combine(_dir, "diagram.json");
        DiagramFiles.Create(file, "sqlserver");

        foreach (var (tool, args) in SetupSteps)
        {
            Exec(tool, file, args).Success.Should().BeTrue($"{tool} must succeed");
        }

        return file;
    }

    /// <summary>Order の列名 → 外部キー印</summary>
    private static Dictionary<string, bool> OrderMarks(string file) =>
        JsonStorageService
            .Load(file)
            .Schema.Entities.Single(entity => entity.TableName == "Order")
            .Columns.ToDictionary(column => column.Name, column => column.IsForeignKey);

    [Fact(DisplayName = "準備した図では列ペアの子列すべてに外部キー印が立つ")]
    public void Setup_MarksEveryChildColumn()
    {
        var marks = OrderMarks(CreateFile());

        marks
            .Should()
            .BeEquivalentTo(
                new Dictionary<string, bool>
                {
                    ["OrderId"] = false,
                    ["CustomerId"] = true,
                    ["BillToCustomerId"] = true,
                    ["RegionId"] = true,
                    ["RegionCode"] = true,
                }
            );
    }

    [Fact(DisplayName = "remove_relationship は外れた子列の外部キー印を落とす")]
    public void RemoveRelationship_ReleasesChildColumnMark()
    {
        var file = CreateFile();

        Exec(
            "remove_relationship",
            file,
            new
            {
                source_table = "Customer",
                target_table = "Order",
                constraint_name = "FK_BillTo",
            }
        )
            .Success.Should()
            .BeTrue();

        var marks = OrderMarks(file);
        marks["BillToCustomerId"].Should().BeFalse();
        marks["CustomerId"].Should().BeTrue();
        marks["RegionId"].Should().BeTrue();
    }

    [Fact(DisplayName = "親テーブルの remove_entity は子テーブルに残る列の外部キー印を落とす")]
    public void RemoveParentEntity_ReleasesChildColumnMarks()
    {
        var file = CreateFile();

        Exec("remove_entity", file, new { table_name = "Region" }).Success.Should().BeTrue();

        var marks = OrderMarks(file);
        marks["RegionId"].Should().BeFalse();
        marks["RegionCode"].Should().BeFalse();
        marks["CustomerId"].Should().BeTrue();
        marks["BillToCustomerId"].Should().BeTrue();
    }

    [Fact(DisplayName = "親列の remove_column は複合外部キーの子列すべての印を落とす")]
    public void RemoveParentColumn_ReleasesEveryCompositeChildColumnMark()
    {
        var file = CreateFile();

        Exec("remove_column", file, new { table_name = "Region", column_name = "Code" })
            .Success.Should()
            .BeTrue();

        var marks = OrderMarks(file);
        marks["RegionId"]
            .Should()
            .BeFalse("the remaining pair is cleared along with the removed one");
        marks["RegionCode"].Should().BeFalse();
        marks["CustomerId"].Should().BeTrue();
    }

    [Fact(DisplayName = "別のリレーションが使い続けている子列の外部キー印は残す")]
    public void RemoveRelationship_KeepsMarkOfColumnStillUsedByAnother()
    {
        var file = CreateFile();

        Exec("remove_entity", file, new { table_name = "Account" }).Success.Should().BeTrue();

        // Order.CustomerId は Customer→Order の列ペアにも現れる
        OrderMarks(file)["CustomerId"].Should().BeTrue();
    }

    [Fact(DisplayName = "どの列ペアにも現れない列へ手で付けた外部キー印は残す")]
    public void ManualMarkOutsideAnyPair_IsKept()
    {
        var file = CreateFile();

        // DB 取込や GUI の手動チェックで付いた印に相当する（ツールには印だけを立てる操作が無いのでファイルを直接書く）
        var document = JsonStorageService.Load(file);
        var order = document.Schema.Entities.Single(entity => entity.TableName == "Order");
        order.Columns.Add(
            new QuickER.Model.Column
            {
                Name = "LegacyRef",
                DataType = "int",
                IsNullable = true,
                IsForeignKey = true,
            }
        );
        JsonStorageService.SaveAtomic(file, document);

        Exec("remove_entity", file, new { table_name = "Region" }).Success.Should().BeTrue();

        OrderMarks(file)["LegacyRef"].Should().BeTrue();
    }
}
