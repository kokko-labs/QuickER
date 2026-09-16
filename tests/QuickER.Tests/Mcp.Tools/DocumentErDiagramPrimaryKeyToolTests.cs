using System.IO;
using System.Text.Json;
using AwesomeAssertions;
using QuickER.Documents;
using QuickER.Mcp.Tools;
using QuickER.Model;

namespace QuickER.Tests.Mcp.Tools;

/// <summary>
/// ファイルベース実行ホスト（<see cref="DocumentErDiagramToolHost"/>）の主キーツール
/// （<c>set_primary_key</c>）を検証する。膜（<see cref="Column.IsPrimaryKey"/>）と
/// 順序（<see cref="Entity.PrimaryKeyColumnIds"/>）の両方が指定どおりに書かれること、
/// 失敗時にファイルが一切変わらないことを固定する。
/// </summary>
public sealed class DocumentErDiagramPrimaryKeyToolTests : IDisposable
{
    /// <summary>各テスト専用の一時ディレクトリ</summary>
    private readonly string _dir = Path.Combine(
        Path.GetTempPath(),
        "quicker-mcp-pk-" + Guid.NewGuid().ToString("N")
    );

    public DocumentErDiagramPrimaryKeyToolTests()
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

    /// <summary>
    /// OrderLine(OrderLineId PK / OrderId / LineNo / Memo〔NULL 許容〕) を持つ図ファイルを用意する
    /// </summary>
    private string CreateOrderLineFile()
    {
        var file = Path.Combine(_dir, "diagram.json");
        Exec(DocumentErDiagramToolHost.CreateDiagramToolName, file, new { target_dbms = "sqlite" })
            .Success.Should()
            .BeTrue();
        Exec("add_entity", file, new { table_name = "OrderLine" });
        Exec(
            "add_column",
            file,
            new
            {
                table_name = "OrderLine",
                column_name = "OrderLineId",
                data_type = "int",
                is_primary_key = true,
                is_nullable = false,
            }
        );

        foreach (var columnName in new[] { "OrderId", "LineNo" })
        {
            Exec(
                "add_column",
                file,
                new
                {
                    table_name = "OrderLine",
                    column_name = columnName,
                    data_type = "int",
                    is_nullable = false,
                }
            );
        }

        // NULL 許容列（主キーに含めると NOT NULL へ正規化されることの検証に使う）
        Exec(
            "add_column",
            file,
            new
            {
                table_name = "OrderLine",
                column_name = "Memo",
                data_type = "nvarchar(100)",
                is_nullable = true,
            }
        );

        return file;
    }

    /// <summary>保存済みファイルから OrderLine エンティティを読み出す</summary>
    private static Entity LoadOrderLine(string file) =>
        JsonStorageService
            .Load(file)
            .Schema.Entities.Single(entity => entity.TableName == "OrderLine");

    /// <summary>指定名の列の Id を引く</summary>
    private static Guid IdOf(Entity entity, string columnName) =>
        entity.Columns.Single(column => column.Name == columnName).Id;

    [Fact(DisplayName = "set_primary_key は単一列の主キーを設定する")]
    public void SetPrimaryKey_SetsSingleColumnKey()
    {
        var file = CreateOrderLineFile();

        var (result, success) = Exec(
            "set_primary_key",
            file,
            new { table_name = "OrderLine", columns = new[] { "OrderId" } }
        );

        success.Should().BeTrue();
        result.Should().Be("Set primary key on table 'OrderLine' (columns: OrderId).");

        var entity = LoadOrderLine(file);
        entity
            .Columns.Where(column => column.IsPrimaryKey)
            .Select(column => column.Name)
            .Should()
            .Equal("OrderId");
        entity.PrimaryKeyColumnIds.Should().Equal(IdOf(entity, "OrderId"));
    }

    [Fact(DisplayName = "set_primary_key は列宣言順と逆順の複合主キーを指定順どおり記録する")]
    public void SetPrimaryKey_CompositeReversedOrder_IsRecordedAsGiven()
    {
        var file = CreateOrderLineFile();

        // 列宣言順は OrderLineId → OrderId → LineNo なので、逆順の指定は宣言順と食い違う
        var (_, success) = Exec(
            "set_primary_key",
            file,
            new { table_name = "OrderLine", columns = new[] { "LineNo", "OrderId" } }
        );

        success.Should().BeTrue();

        var entity = LoadOrderLine(file);
        entity
            .GetPrimaryKeyColumnsInOrder()
            .Select(column => column.Name)
            .Should()
            .Equal("LineNo", "OrderId");
        entity.PrimaryKeyColumnIds.Should().Equal(IdOf(entity, "LineNo"), IdOf(entity, "OrderId"));
        entity.GetReorderedPrimaryKeyColumnNames().Should().Equal("LineNo", "OrderId");
    }

    [Fact(DisplayName = "set_primary_key は 3 列の主キーの並びを全体として記録する")]
    public void SetPrimaryKey_ThreeColumns_RecordsWholeOrder()
    {
        var file = CreateOrderLineFile();

        var (_, success) = Exec(
            "set_primary_key",
            file,
            new { table_name = "OrderLine", columns = new[] { "LineNo", "OrderLineId", "OrderId" } }
        );

        success.Should().BeTrue();

        var entity = LoadOrderLine(file);
        // 部分でなく全体（3 列すべて）が並びとして残る
        entity
            .PrimaryKeyColumnIds.Should()
            .Equal(IdOf(entity, "LineNo"), IdOf(entity, "OrderLineId"), IdOf(entity, "OrderId"));
        entity
            .GetPrimaryKeyColumnsInOrder()
            .Select(column => column.Name)
            .Should()
            .Equal("LineNo", "OrderLineId", "OrderId");
    }

    [Fact(DisplayName = "set_primary_key は旧主キー列を外すが NULL 許容は据え置く")]
    public void SetPrimaryKey_DroppedColumn_KeepsNullability()
    {
        var file = CreateOrderLineFile();

        var (_, success) = Exec(
            "set_primary_key",
            file,
            new { table_name = "OrderLine", columns = new[] { "OrderId" } }
        );

        success.Should().BeTrue();

        var dropped = LoadOrderLine(file).Columns.Single(column => column.Name == "OrderLineId");
        dropped.IsPrimaryKey.Should().BeFalse();
        // 主キーから外れただけで NULL 許容へは倒さない
        dropped.IsNullable.Should().BeFalse();
    }

    [Fact(DisplayName = "set_primary_key は NULL 許容列を主キーに含めると NOT NULL へ正規化する")]
    public void SetPrimaryKey_NullableColumn_BecomesNotNull()
    {
        var file = CreateOrderLineFile();
        LoadOrderLine(file)
            .Columns.Single(column => column.Name == "Memo")
            .IsNullable.Should()
            .BeTrue();

        var (_, success) = Exec(
            "set_primary_key",
            file,
            new { table_name = "OrderLine", columns = new[] { "Memo" } }
        );

        success.Should().BeTrue();

        var memo = LoadOrderLine(file).Columns.Single(column => column.Name == "Memo");
        memo.IsPrimaryKey.Should().BeTrue();
        memo.IsNullable.Should().BeFalse();
    }

    [Fact(DisplayName = "set_primary_key は列名の大文字小文字を無視して照合する")]
    public void SetPrimaryKey_ColumnNames_AreMatchedCaseInsensitively()
    {
        var file = CreateOrderLineFile();

        var (_, success) = Exec(
            "set_primary_key",
            file,
            new { table_name = "OrderLine", columns = new[] { "orderid", "LINENO" } }
        );

        success.Should().BeTrue();

        var entity = LoadOrderLine(file);
        entity.PrimaryKeyColumnIds.Should().Equal(IdOf(entity, "OrderId"), IdOf(entity, "LineNo"));
    }

    [Fact(DisplayName = "set_primary_key は不正な引数をエラーにしファイルを変更しない")]
    public void SetPrimaryKey_InvalidArguments_FailWithoutTouchingFile()
    {
        var file = CreateOrderLineFile();
        var before = File.ReadAllBytes(file);

        var cases = new (string Case, object Args)[]
        {
            ("table_name 欠落", new { columns = new[] { "OrderId" } }),
            ("テーブル不在", new { table_name = "NoSuchTable", columns = new[] { "OrderId" } }),
            ("columns 欠落", new { table_name = "OrderLine" }),
            ("columns が空配列", new { table_name = "OrderLine", columns = Array.Empty<string>() }),
            ("存在しない列", new { table_name = "OrderLine", columns = new[] { "NoSuchColumn" } }),
            (
                "同じ列の重複",
                new { table_name = "OrderLine", columns = new[] { "OrderId", "orderid" } }
            ),
        };

        foreach (var (caseName, args) in cases)
        {
            var (result, success) = Exec("set_primary_key", file, args);

            success.Should().BeFalse(caseName);
            result.Should().NotBeEmpty(caseName);
            // 失敗した呼び出しはファイルを 1 バイトも変えない（Mutate が成功時のみ保存する）
            File.ReadAllBytes(file).SequenceEqual(before).Should().BeTrue(caseName);
        }
    }

    [Fact(DisplayName = "get_diagram_summary は set_primary_key で設定した主キー順を注記する")]
    public void GetDiagramSummary_ReportsPrimaryKeyOrderSetByTool()
    {
        var file = CreateOrderLineFile();
        Exec(
            "set_primary_key",
            file,
            new { table_name = "OrderLine", columns = new[] { "LineNo", "OrderId" } }
        )
            .Success.Should()
            .BeTrue();

        var (result, success) = Exec("get_diagram_summary", file, new { });

        success.Should().BeTrue();
        result.Should().Contain("Primary key order: LineNo, OrderId");
    }
}
