using AwesomeAssertions;
using QuickER.Model;

namespace QuickER.Tests.Model;

/// <summary>
/// 制約名の安全化と既定名の合成（<see cref="ConstraintNames"/>）の規則そのものを検証するテストクラス
/// </summary>
/// <remarks>
/// DDL 生成・DB 同期・GUI・内蔵チャット・MCP のツールが共有する規則なので、ここで規則を固定し、
/// 各経路が同じ名前を作ることは経路ごとのテストが確かめる。
/// </remarks>
public class ConstraintNamesTests
{
    [Theory(DisplayName = "SafeName は前後の空白を除いてから . と空白を _ へ置き換える")]
    [InlineData("Orders", "Orders")]
    [InlineData(" Orders", "Orders")]
    [InlineData("Orders  ", "Orders")]
    [InlineData("dbo.Orders", "dbo_Orders")]
    [InlineData("Order Details", "Order_Details")]
    [InlineData("  dbo.Order Details ", "dbo_Order_Details")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void SafeName_TrimsThenReplaces(string? name, string expected)
    {
        ConstraintNames.SafeName(name).Should().Be(expected);
    }

    /// <summary>安全化済みの名前をもう一度通しても変わらない（先に安全化してから合成する経路と結果が揃う）</summary>
    [Theory(DisplayName = "SafeName は冪等")]
    [InlineData(" dbo.Order Details ")]
    [InlineData("Orders")]
    public void SafeName_IsIdempotent(string name)
    {
        var once = ConstraintNames.SafeName(name);

        ConstraintNames.SafeName(once).Should().Be(once);
    }

    [Fact(DisplayName = "外部キーの既定名は FK_{子}_{親}（両方を安全化）")]
    public void ForeignKey_ComposesSafeNames()
    {
        ConstraintNames
            .ForeignKey(" dbo.Orders", "dbo.Customers")
            .Should()
            .Be("FK_dbo_Orders_dbo_Customers");
    }

    [Fact(DisplayName = "主キーの既定名は PK_{テーブル}（安全化）")]
    public void PrimaryKey_ComposesSafeName()
    {
        ConstraintNames.PrimaryKey(" dbo.Orders ").Should().Be("PK_dbo_Orders");
    }

    /// <summary>
    /// 一意制約の合成名も同じ安全化を通す（DDL・生成コード・ツールの要約表示が同じ名前を作る）
    /// </summary>
    [Fact(DisplayName = "一意制約の合成名もテーブル名と列名を安全化する")]
    public void UniqueConstraintSynthesizeName_UsesSafeName()
    {
        UniqueConstraint
            .SynthesizeName(" dbo.Orders", ["order code", "tenant"])
            .Should()
            .Be("UQ_dbo_Orders_order_code_tenant");
    }

    /// <summary>
    /// 先に方言の安全化を通してから合成する経路（DDL 生成）と、素の名前から合成する経路（生成コード・ツール）が
    /// 同じ名前になる
    /// </summary>
    [Fact(DisplayName = "一意制約の合成名は、先に安全化しても結果が同じ")]
    public void UniqueConstraintSynthesizeName_AgreesWithPresanitizedInput()
    {
        var direct = UniqueConstraint.SynthesizeName("dbo.Orders", ["code"]);
        var presanitized = UniqueConstraint.SynthesizeName(
            ConstraintNames.SafeName("dbo.Orders"),
            [ConstraintNames.SafeName("code")]
        );

        presanitized.Should().Be(direct);
    }

    [Fact(DisplayName = "衝突しない外部キー名は、既定名が空いていれば既定名のまま")]
    public void UniqueForeignKey_DefaultNameIsFree_ReturnsDefault()
    {
        ConstraintNames
            .UniqueForeignKey("Orders", "Customers", ["BillingCustomerId"], ["PK_Orders"])
            .Should()
            .Be("FK_Orders_Customers");
    }

    [Fact(DisplayName = "衝突しない外部キー名は、既定名がふさがっていれば子の列名を足す")]
    public void UniqueForeignKey_DefaultNameTaken_AppendsChildColumns()
    {
        ConstraintNames
            .UniqueForeignKey(
                "Orders",
                "Customers",
                ["ShippingCustomerId"],
                ["FK_Orders_Customers"]
            )
            .Should()
            .Be("FK_Orders_Customers_ShippingCustomerId");
    }

    /// <summary>列名は宣言順に <c>_</c> でつなぎ、それぞれ安全化する</summary>
    [Fact(DisplayName = "衝突しない外部キー名に足す列名は宣言順につなぎ、安全化する")]
    public void UniqueForeignKey_AppendsSafeColumnNamesInOrder()
    {
        ConstraintNames
            .UniqueForeignKey(
                "Orders",
                "Customers",
                ["tenant id", "Ship.Code"],
                ["FK_Orders_Customers"]
            )
            .Should()
            .Be("FK_Orders_Customers_tenant_id_Ship_Code");
    }

    [Fact(DisplayName = "衝突しない外部キー名は、列名を足してもふさがっていれば番号を足す")]
    public void UniqueForeignKey_ColumnNameAlsoTaken_AppendsNumber()
    {
        ConstraintNames
            .UniqueForeignKey(
                "Orders",
                "Customers",
                ["CustomerId"],
                [
                    "FK_Orders_Customers",
                    "FK_Orders_Customers_CustomerId",
                    "FK_Orders_Customers_CustomerId_2",
                ]
            )
            .Should()
            .Be("FK_Orders_Customers_CustomerId_3");
    }

    [Fact(DisplayName = "衝突しない外部キー名は、列が無ければ既定名に番号を足す")]
    public void UniqueForeignKey_NoColumns_AppendsNumberToDefault()
    {
        ConstraintNames
            .UniqueForeignKey("Orders", "Customers", [], ["FK_Orders_Customers"])
            .Should()
            .Be("FK_Orders_Customers_2");
    }

    [Fact(DisplayName = "衝突しない外部キー名は、大文字小文字だけ違う名前も衝突とみなす")]
    public void UniqueForeignKey_IgnoresCase()
    {
        ConstraintNames
            .UniqueForeignKey("Orders", "Customers", ["ShipTo"], ["fk_orders_customers"])
            .Should()
            .Be("FK_Orders_Customers_ShipTo");
    }

    /// <summary>既存の制約名は、外部キー・主キー・一意制約を DDL が付ける名前で集める</summary>
    [Fact(DisplayName = "既存の制約名は外部キー・主キー・一意制約を DDL の名前で集める")]
    public void CollectExisting_GathersEveryConstraintKind()
    {
        var customerId = new Column { Name = "CustomerId", IsPrimaryKey = true };
        var customers = new Entity { TableName = "Customers", Columns = { customerId } };
        var code = new Column { Name = "code" };
        var memo = new Column { Name = "memo" };
        var orders = new Entity
        {
            TableName = "dbo.Orders",
            Columns =
            {
                new Column { Name = "OrderId", IsPrimaryKey = true },
                code,
                memo,
            },
            UniqueConstraints =
            {
                new UniqueConstraint { ColumnIds = { code.Id } },
                new UniqueConstraint { Name = "UQ_Named", ColumnIds = { memo.Id } },
                // 構成列を解決できない一意制約は DDL が出さないので数えない
                new UniqueConstraint { ColumnIds = { Guid.NewGuid() } },
            },
        };
        // 主キー列を持たないテーブルは主キー制約を作らない
        var notes = new Entity { TableName = "Notes", Columns = { new Column { Name = "Body" } } };
        var diagram = new ErDiagram
        {
            Entities = { customers, orders, notes },
            Relationships =
            {
                new Relationship { SourceEntityId = customers.Id, TargetEntityId = orders.Id },
                new Relationship
                {
                    SourceEntityId = customers.Id,
                    TargetEntityId = orders.Id,
                    ConstraintName = "FK_Ship",
                },
            },
        };

        ConstraintNames
            .CollectExisting(diagram)
            .Should()
            .BeEquivalentTo(
                "FK_dbo_Orders_Customers",
                "FK_Ship",
                "PK_Customers",
                "PK_dbo_Orders",
                "UQ_dbo_Orders_code",
                "UQ_Named"
            );
    }

    [Fact(DisplayName = "既存の制約名の集合は大文字小文字を区別しない")]
    public void CollectExisting_IsCaseInsensitive()
    {
        var customers = new Entity
        {
            TableName = "Customers",
            Columns =
            {
                new Column { Name = "CustomerId", IsPrimaryKey = true },
            },
        };

        ConstraintNames
            .CollectExisting(new ErDiagram { Entities = { customers } })
            .Contains("pk_customers")
            .Should()
            .BeTrue();
    }
}
