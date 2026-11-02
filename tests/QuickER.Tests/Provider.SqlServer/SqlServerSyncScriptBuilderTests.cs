using AwesomeAssertions;
using QuickER.Model;
using QuickER.Provider;
using QuickER.Provider.SqlServer;
using QuickER.Services;
using QuickER.Tests.TestSupport;

namespace QuickER.Tests.Provider.SqlServer;

/// <summary>
/// <see cref="SqlServerSyncScriptBuilder"/> が差分から生成する T-SQL のうち、実 DB で流しても観測できない文字列の形を検証するテストクラス
/// </summary>
/// <remarks>
/// 実 DB で流して結果を観測できる文の形は、同期計画の期待の行（<c>SyncPlanExpectationRows.RealSqlServer.cs</c> の
/// <c>realdb-script-*</c> など）が実 SQL Server で流して見る。
/// </remarks>
public class SqlServerSyncScriptBuilderTests
{
    /// <summary>差分項目からプランナー経由で同期スクリプトを生成する（新 API へのアダプタ）</summary>
    private static string BuildScript(ISyncScriptBuilder builder, params SchemaDiffItem[] items) =>
        builder.Build(new SyncPlanner().BuildPlan(items, TestDialectCapabilities.Neutral));

    /// <summary>AddTable が文の区切りの GO を出すことを検証する（実行器はこの行でバッチを分ける）</summary>
    [Fact(DisplayName = "AddTable は CREATE TABLE と PK を含む")]
    public void AddTable_GeneratesCreate()
    {
        var e = new Entity { TableName = "Customer" };
        e.Columns.Add(
            new Column
            {
                Name = "Id",
                DataType = "int",
                IsPrimaryKey = true,
                IsNullable = false,
            }
        );
        e.Columns.Add(
            new Column
            {
                Name = "Name",
                DataType = "nvarchar(50)",
                IsNullable = true,
            }
        );
        var item = new SchemaDiffItem
        {
            Kind = SchemaDiffKind.AddTable,
            TableName = "Customer",
            Entity = e,
            IsSelected = true,
        };

        var sql = BuildScript(new SqlServerSyncScriptBuilder(), new[] { item });
        sql.Should().Contain("GO");
    }

    /// <summary>
    /// 修飾の無い表の説明は、表と同じ既定スキーマを <c>SCHEMA_NAME()</c> で求めて指す（文ごとに GO で区切るので
    /// 文ごとに宣言する）。修飾つきの表は既定スキーマの変数を使わない（対照）。
    /// </summary>
    /// <remarks>
    /// 既定スキーマが dbo の接続では <c>SCHEMA_NAME()</c> と <c>N'dbo'</c> の違いが結果に出ないので、文字列で見る。
    /// </remarks>
    [Fact(
        DisplayName = "修飾の無い表の説明は既定スキーマ（SCHEMA_NAME()）を指し、修飾つきはスキーマ名を書く"
    )]
    public void Description_OfUnqualifiedTable_UsesDefaultSchema()
    {
        var unqualified = new SchemaDiffItem
        {
            Kind = SchemaDiffKind.SetColumnDescription,
            TableName = "Customer",
            ColumnName = "Name",
            NewDescription = "顧客名",
            IsSelected = true,
        };
        var qualified = new SchemaDiffItem
        {
            Kind = SchemaDiffKind.SetTableDescription,
            TableName = "sales.Order",
            NewDescription = "注文",
            IsSelected = true,
        };

        var sql = BuildScript(new SqlServerSyncScriptBuilder(), unqualified, qualified);
        var batches = sql.Replace("\r\n", "\n").Split("\nGO\n");
        var customer = batches.Single(b => b.Contains("@level1name=N'Customer'"));
        var order = batches.Single(b => b.Contains("@level1name=N'Order'"));

        customer.Should().Contain("DECLARE @default_schema sysname = SCHEMA_NAME();");
        customer.Should().Contain("@level0name=@default_schema");
        customer.Should().NotContain("N'dbo'");
        order.Should().NotContain("@default_schema");
    }

    // ---------------- 主キー変更（AlterPrimaryKey） ----------------

    /// <summary>指定の主キー列を持つ target エンティティを組み立てる</summary>
    private static Entity PkTarget(string table, params string[] pkColumns)
    {
        var e = new Entity { TableName = table };
        e.Columns.Add(
            new Column
            {
                Name = "Memo",
                DataType = "nvarchar(50)",
                IsNullable = true,
            }
        );

        foreach (var name in pkColumns)
        {
            e.Columns.Add(
                new Column
                {
                    Name = name,
                    DataType = "int",
                    IsPrimaryKey = true,
                    IsNullable = false,
                }
            );
        }

        return e;
    }

    /// <summary>主キー変更の差分項目を生成する（Entity＝新しい主キー構成の源）</summary>
    private static SchemaDiffItem AlterPk(string table, Entity target) =>
        new()
        {
            Kind = SchemaDiffKind.AlterPrimaryKey,
            TableName = table,
            Entity = target,
            IsSelected = true,
        };

    // ---------------- 動的 SQL のリテラルエスケープ ----------------

    /// <summary>制約名不明の外部キー削除でも、動的 SQL のテーブル名がリテラル用にエスケープされることを検証する</summary>
    /// <remarks>
    /// SQL Server は外部キーの名前を保存するので、差分から作る外部キーの削除は必ず名前を持つ（名前の無い削除は手で組んだ項目でしか通らない）。
    /// </remarks>
    [Fact(DisplayName = "DropForeignKey の動的 SQL はテーブル名の ' を二重化する")]
    public void DropForeignKey_TableNameWithQuote_IsEscapedInDynamicSql()
    {
        var item = new SchemaDiffItem
        {
            Kind = SchemaDiffKind.DropForeignKey,
            TableName = "o'rders",
            ParentEntity = new Entity { TableName = "Customer" },
            ChildEntity = new Entity { TableName = "o'rders" },
            IsSelected = true,
        };

        var sql = BuildScript(new SqlServerSyncScriptBuilder(), new[] { item });

        sql.Should()
            .Contain(
                "DECLARE @sql nvarchar(max) = N'ALTER TABLE [o''rders] DROP CONSTRAINT ' + QUOTENAME(@fk);"
            );
        sql.Should().Contain("OBJECT_ID(N'[o''rders]')");
        sql.Should().NotContain("N'ALTER TABLE [o'rders]");
        sql.Should().NotContain("OBJECT_ID(N'[o'rders]')");
    }

    // ---------------- 実行時にカタログから読んだ制約名の埋め込み ----------------

    /// <summary>
    /// 主キー解除の逆引きが、テーブル名だけで <c>sys.tables</c> を引く形でないことを検証する。
    /// </summary>
    /// <remarks>
    /// テーブル名だけで <c>sys.tables</c> を引くと、別スキーマの同名テーブルの主キー名を拾う。
    /// </remarks>
    [Fact(DisplayName = "AlterPrimaryKey の逆引きは OBJECT_ID でスキーマ込みの 1 テーブルに絞る")]
    public void AlterPrimaryKey_Lookup_IsScopedByObjectId()
    {
        var sql = BuildScript(
            new SqlServerSyncScriptBuilder(),
            new[] { AlterPk("sales.Orders", PkTarget("sales.Orders", "Id")) }
        );

        sql.Should().NotContain("sys.tables");
        sql.Should().NotContain("t.name");
    }

    /// <summary>
    /// 制約名不明の外部キー削除の逆引きが、子・親とも <c>OBJECT_ID</c> でスキーマ込みに絞られることを検証する。
    /// </summary>
    [Fact(DisplayName = "DropForeignKey の逆引きは子・親とも OBJECT_ID でスキーマ込みに絞る")]
    public void DropForeignKey_Lookup_IsScopedByObjectId()
    {
        var item = new SchemaDiffItem
        {
            Kind = SchemaDiffKind.DropForeignKey,
            TableName = "sales.Lines",
            ParentEntity = new Entity { TableName = "sales.Orders" },
            ChildEntity = new Entity { TableName = "sales.Lines" },
            IsSelected = true,
        };

        var sql = BuildScript(new SqlServerSyncScriptBuilder(), new[] { item });

        sql.Should().Contain("WHERE fk.parent_object_id = OBJECT_ID(N'[sales].[Lines]')");
        sql.Should().Contain("  AND fk.referenced_object_id = OBJECT_ID(N'[sales].[Orders]');");
        sql.Should().NotContain("sys.tables");
    }

    /// <summary>
    /// 実行時にカタログから読んだ制約名（<c>@pk</c> / <c>@fk</c>）が、動的 SQL へ必ず <c>QUOTENAME</c> を通して埋められ、
    /// 生の連結が残らないことを検証する。
    /// </summary>
    /// <remarks>
    /// 制約名は図の外（DB）から来る値で、<c>]</c> を含み得る。<c>'[' + @pk + ']'</c> のように括弧で挟むだけでは
    /// <c>]</c> が括弧を閉じ、以降が SQL のコードとして解釈される。
    /// </remarks>
    [Fact(DisplayName = "実行時に読んだ制約名は QUOTENAME を通して動的 SQL へ埋める")]
    public void RuntimeConstraintName_IsAlwaysQuotenamed()
    {
        var dropForeignKey = new SchemaDiffItem
        {
            Kind = SchemaDiffKind.DropForeignKey,
            TableName = "Lines",
            ParentEntity = new Entity { TableName = "Orders" },
            ChildEntity = new Entity { TableName = "Lines" },
            IsSelected = true,
        };

        var sql = BuildScript(
            new SqlServerSyncScriptBuilder(),
            AlterPk("Orders", PkTarget("Orders", "Id")),
            dropForeignKey
        );

        sql.Should().Contain("QUOTENAME(@fk)");
        // 変数を QUOTENAME を通さずに文字列へ連結している箇所が無いこと
        sql.Should().NotContain("+ @pk");
        sql.Should().NotContain("+ @fk");
        sql.Should().NotContain("EXEC(");
    }
}
