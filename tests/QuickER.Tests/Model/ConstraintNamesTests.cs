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

    /// <summary>
    /// テーブルの制約名は、<see cref="ConstraintNames.CollectExisting"/> から外部キーだけを除いたものと一致する
    /// （主キー・一意制約の規則を写さず共有していることの確認）
    /// </summary>
    [Fact(DisplayName = "テーブルの制約名は既存の制約名から外部キーを除いたものと同じ")]
    public void CollectTableConstraints_EqualsCollectExistingWithoutRelationships()
    {
        var customers = new Entity
        {
            TableName = "Customers",
            Columns =
            {
                new Column { Name = "CustomerId", IsPrimaryKey = true },
            },
        };
        var code = new Column { Name = "code" };
        var orders = new Entity
        {
            TableName = "dbo.Orders",
            Columns =
            {
                new Column { Name = "OrderId", IsPrimaryKey = true },
                code,
            },
            UniqueConstraints = { new UniqueConstraint { ColumnIds = { code.Id } } },
        };
        var diagram = new ErDiagram
        {
            Entities = { customers, orders },
            Relationships =
            {
                new Relationship { SourceEntityId = customers.Id, TargetEntityId = orders.Id },
            },
        };

        var tableConstraints = ConstraintNames.CollectTableConstraints(diagram.Entities);

        tableConstraints
            .Should()
            .BeEquivalentTo("PK_Customers", "PK_dbo_Orders", "UQ_dbo_Orders_code");
        tableConstraints.Contains("pk_customers").Should().BeTrue("大文字小文字を区別しない");
        ConstraintNames
            .CollectExisting(diagram)
            .Except(tableConstraints)
            .Should()
            .BeEquivalentTo("FK_dbo_Orders_Customers");
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

    /// <summary>customers（主キー id）と orders（bill_customer_id / ship_customer_id）の 2 テーブル</summary>
    private static (Entity Customers, Entity Orders) BuildCustomersAndOrders()
    {
        var customers = new Entity
        {
            TableName = "customers",
            Columns =
            {
                new Column { Name = "id", IsPrimaryKey = true },
            },
        };
        var orders = new Entity
        {
            TableName = "orders",
            Columns =
            {
                new Column { Name = "id", IsPrimaryKey = true },
                new Column { Name = "bill_customer_id" },
                new Column { Name = "ship_customer_id" },
            },
        };

        return (customers, orders);
    }

    /// <summary>customers.id → orders の指定列の 1 対多リレーション</summary>
    private static Relationship Link(
        Entity customers,
        Entity orders,
        string childColumn,
        string? name = null
    ) =>
        new()
        {
            SourceEntityId = customers.Id,
            TargetEntityId = orders.Id,
            ConstraintName = name,
            ColumnPairs =
            [
                new RelationshipColumnPair(
                    customers.Columns[0].Id,
                    orders.Columns.Single(column => column.Name == childColumn).Id
                ),
            ],
        };

    [Fact(DisplayName = "名前の無いリレーションは、既定名が空いていれば名前を持たないまま")]
    public void NameUnnamedForeignKeys_DefaultNameFree_StaysUnnamed()
    {
        var (customers, orders) = BuildCustomersAndOrders();
        var relationship = Link(customers, orders, "bill_customer_id");

        ConstraintNames.NameUnnamedForeignKeys([customers, orders], [relationship]);

        relationship.ConstraintName.Should().BeNull();
    }

    [Fact(
        DisplayName = "同じテーブルの組に名前の無いリレーションが 2 本あれば、2 本目に子の列名つきの名前を付ける"
    )]
    public void NameUnnamedForeignKeys_SamePairTwice_SecondGetsUniqueName()
    {
        var (customers, orders) = BuildCustomersAndOrders();
        var bill = Link(customers, orders, "bill_customer_id");
        var ship = Link(customers, orders, "ship_customer_id");

        ConstraintNames.NameUnnamedForeignKeys([customers, orders], [bill, ship]);

        bill.ConstraintName.Should().BeNull("渡された順で先のリレーションが既定名を取る");
        ship.ConstraintName.Should().Be("FK_orders_customers_ship_customer_id");
    }

    [Fact(
        DisplayName = "名前を持つリレーションが既定名をふさいでいれば、名前の無い側に名前を付け、名前を持つ側は変えない"
    )]
    public void NameUnnamedForeignKeys_ExplicitNameTakesDefault_UnnamedIsRenamed()
    {
        var (customers, orders) = BuildCustomersAndOrders();
        var unnamed = Link(customers, orders, "bill_customer_id");
        // 名前を持つリレーションが後ろにあっても、先に予約される
        var named = Link(customers, orders, "ship_customer_id", "fk_ORDERS_customers");

        ConstraintNames.NameUnnamedForeignKeys([customers, orders], [unnamed, named]);

        unnamed.ConstraintName.Should().Be("FK_orders_customers_bill_customer_id");
        named.ConstraintName.Should().Be("fk_ORDERS_customers");
    }

    [Fact(
        DisplayName = "主キー・一意制約の名前が既定名をふさいでいれば、名前の無い側に名前を付ける"
    )]
    public void NameUnnamedForeignKeys_TableConstraintTakesDefault_UnnamedIsRenamed()
    {
        var (customers, orders) = BuildCustomersAndOrders();
        orders.UniqueConstraints.Add(
            new UniqueConstraint
            {
                Name = "FK_orders_customers",
                ColumnIds = { orders.Columns[1].Id },
            }
        );
        var relationship = Link(customers, orders, "bill_customer_id");

        ConstraintNames.NameUnnamedForeignKeys([customers, orders], [relationship]);

        relationship.ConstraintName.Should().Be("FK_orders_customers_bill_customer_id");
    }

    [Fact(DisplayName = "外部キーの実効名は、保存された名前が無ければ既定名・あればそのまま")]
    public void EffectiveForeignKey_FallsBackToDefaultOnlyWhenUnnamed()
    {
        ConstraintNames
            .EffectiveForeignKey(null, "orders", "customers")
            .Should()
            .Be("FK_orders_customers");
        ConstraintNames
            .EffectiveForeignKey("  ", " orders ", "dbo.customers")
            .Should()
            .Be("FK_orders_dbo_customers");
        ConstraintNames
            .EffectiveForeignKey("fk_custom", "orders", "customers")
            .Should()
            .Be("fk_custom");
    }

    [Fact(DisplayName = "列ペアの無いリレーションは、既定名がふさがっていれば番号を足す")]
    public void NameUnnamedForeignKeys_NoColumnPairs_AppendsNumber()
    {
        var (customers, orders) = BuildCustomersAndOrders();
        var first = new Relationship { SourceEntityId = customers.Id, TargetEntityId = orders.Id };
        var second = new Relationship { SourceEntityId = customers.Id, TargetEntityId = orders.Id };

        ConstraintNames.NameUnnamedForeignKeys([customers, orders], [first, second]);

        first.ConstraintName.Should().BeNull();
        second.ConstraintName.Should().Be("FK_orders_customers_2");
    }

    [Fact(DisplayName = "親子を引けないリレーションは飛ばし、既定名も予約しない")]
    public void NameUnnamedForeignKeys_UnresolvableRelationship_IsSkipped()
    {
        var (customers, orders) = BuildCustomersAndOrders();
        var dangling = new Relationship
        {
            SourceEntityId = Guid.NewGuid(),
            TargetEntityId = orders.Id,
        };
        var relationship = Link(customers, orders, "bill_customer_id");

        ConstraintNames.NameUnnamedForeignKeys([customers, orders], [dangling, relationship]);

        dangling.ConstraintName.Should().BeNull();
        relationship.ConstraintName.Should().BeNull();
    }

    [Fact(
        DisplayName = "外部キー名の解決は、名前の無い同じ組の 2 本目を一意化し、図は書き換えない"
    )]
    public void ResolveForeignKeyNames_SamePairTwice_UniqueWithoutMutation()
    {
        var (customers, orders) = BuildCustomersAndOrders();
        var bill = Link(customers, orders, "bill_customer_id");
        var ship = Link(customers, orders, "ship_customer_id");
        var named = Link(customers, orders, "ship_customer_id", name: "FK_custom");

        var names = ConstraintNames.ResolveForeignKeyNames(
            [customers, orders],
            [bill, ship, named]
        );

        names[bill].Should().Be("FK_orders_customers");
        names[ship].Should().Be("FK_orders_customers_ship_customer_id");
        names[named].Should().Be("FK_custom");
        bill.ConstraintName.Should().BeNull();
        ship.ConstraintName.Should().BeNull("名前を求めるだけで、図には書き込まない");
    }

    [Fact(
        DisplayName = "名前を図へ書き込む NameUnnamedForeignKeys は、外部キー名の解決と同じ名前を付ける"
    )]
    public void NameUnnamedForeignKeys_AgreesWithResolveForeignKeyNames()
    {
        var (customers, orders) = BuildCustomersAndOrders();
        var bill = Link(customers, orders, "bill_customer_id");
        var ship = Link(customers, orders, "ship_customer_id");
        var resolved = ConstraintNames.ResolveForeignKeyNames([customers, orders], [bill, ship]);

        ConstraintNames.NameUnnamedForeignKeys([customers, orders], [bill, ship]);

        ConstraintNames
            .EffectiveForeignKey(bill.ConstraintName, orders.TableName, customers.TableName)
            .Should()
            .Be(resolved[bill]);
        ship.ConstraintName.Should().Be(resolved[ship]);
    }
}
