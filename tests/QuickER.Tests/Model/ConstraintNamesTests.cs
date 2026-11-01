using AwesomeAssertions;
using QuickER.Dialects;
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
            .ForeignKey(" dbo.Orders", "dbo.Customers", IdentifierLengthLimit.Unlimited)
            .Should()
            .Be("FK_dbo_Orders_dbo_Customers");
    }

    [Fact(DisplayName = "主キーの既定名は PK_{テーブル}（安全化）")]
    public void PrimaryKey_ComposesSafeName()
    {
        ConstraintNames
            .PrimaryKey(" dbo.Orders ", IdentifierLengthLimit.Unlimited)
            .Should()
            .Be("PK_dbo_Orders");
    }

    /// <summary>
    /// 一意制約の合成名も同じ安全化を通す（DDL・生成コード・ツールの要約表示が同じ名前を作る）
    /// </summary>
    [Fact(DisplayName = "一意制約の合成名もテーブル名と列名を安全化する")]
    public void UniqueConstraintSynthesizeName_UsesSafeName()
    {
        UniqueConstraint
            .SynthesizeName(
                " dbo.Orders",
                ["order code", "tenant"],
                IdentifierLengthLimit.Unlimited
            )
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
        var direct = UniqueConstraint.SynthesizeName(
            "dbo.Orders",
            ["code"],
            IdentifierLengthLimit.Unlimited
        );
        var presanitized = UniqueConstraint.SynthesizeName(
            ConstraintNames.SafeName("dbo.Orders"),
            [ConstraintNames.SafeName("code")],
            IdentifierLengthLimit.Unlimited
        );

        presanitized.Should().Be(direct);
    }

    [Fact(DisplayName = "衝突しない外部キー名は、既定名が空いていれば既定名のまま")]
    public void UniqueForeignKey_DefaultNameIsFree_ReturnsDefault()
    {
        ConstraintNames
            .UniqueForeignKey(
                "Orders",
                "Customers",
                ["BillingCustomerId"],
                ["PK_Orders"],
                IdentifierLengthLimit.Unlimited
            )
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
                ["FK_Orders_Customers"],
                IdentifierLengthLimit.Unlimited
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
                ["FK_Orders_Customers"],
                IdentifierLengthLimit.Unlimited
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
                ],
                IdentifierLengthLimit.Unlimited
            )
            .Should()
            .Be("FK_Orders_Customers_CustomerId_3");
    }

    [Fact(DisplayName = "衝突しない外部キー名は、列が無ければ既定名に番号を足す")]
    public void UniqueForeignKey_NoColumns_AppendsNumberToDefault()
    {
        ConstraintNames
            .UniqueForeignKey(
                "Orders",
                "Customers",
                [],
                ["FK_Orders_Customers"],
                IdentifierLengthLimit.Unlimited
            )
            .Should()
            .Be("FK_Orders_Customers_2");
    }

    [Fact(DisplayName = "衝突しない外部キー名は、大文字小文字だけ違う名前も衝突とみなす")]
    public void UniqueForeignKey_IgnoresCase()
    {
        ConstraintNames
            .UniqueForeignKey(
                "Orders",
                "Customers",
                ["ShipTo"],
                ["fk_orders_customers"],
                IdentifierLengthLimit.Unlimited
            )
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
                new Relationship
                {
                    SourceEntityId = customers.Id,
                    TargetEntityId = orders.Id,
                    ColumnPairs = [new RelationshipColumnPair(customerId.Id, code.Id)],
                },
                new Relationship
                {
                    SourceEntityId = customers.Id,
                    TargetEntityId = orders.Id,
                    ConstraintName = "FK_Ship",
                },
                // 名前を持たない多対多は DDL が外部キーを作らないので数えない（名前を持つ関係は外部キーを作らなくても数える）
                new Relationship
                {
                    SourceEntityId = customers.Id,
                    TargetEntityId = notes.Id,
                    Type = RelationshipType.ManyToMany,
                },
            },
        };

        ConstraintNames
            .CollectExisting(diagram, IdentifierLengthLimits.ForDbms(diagram.TargetDbms))
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
                new Relationship
                {
                    SourceEntityId = customers.Id,
                    TargetEntityId = orders.Id,
                    ColumnPairs = [new RelationshipColumnPair(customers.Columns[0].Id, code.Id)],
                },
            },
        };

        var tableConstraints = ConstraintNames.CollectTableConstraints(
            diagram.Entities,
            diagram.Relationships,
            IdentifierLengthLimit.Unlimited
        );

        tableConstraints
            .Should()
            .BeEquivalentTo("PK_Customers", "PK_dbo_Orders", "UQ_dbo_Orders_code");
        tableConstraints.Contains("pk_customers").Should().BeTrue("大文字小文字を区別しない");
        ConstraintNames
            .CollectExisting(diagram, IdentifierLengthLimits.ForDbms(diagram.TargetDbms))
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
            .CollectExisting(
                new ErDiagram { Entities = { customers } },
                IdentifierLengthLimits.SqlServer
            )
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

        ConstraintNames.NameUnnamedForeignKeys(
            [customers, orders],
            [relationship],
            IdentifierLengthLimit.Unlimited
        );

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

        ConstraintNames.NameUnnamedForeignKeys(
            [customers, orders],
            [bill, ship],
            IdentifierLengthLimit.Unlimited
        );

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

        ConstraintNames.NameUnnamedForeignKeys(
            [customers, orders],
            [unnamed, named],
            IdentifierLengthLimit.Unlimited
        );

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

        ConstraintNames.NameUnnamedForeignKeys(
            [customers, orders],
            [relationship],
            IdentifierLengthLimit.Unlimited
        );

        relationship.ConstraintName.Should().Be("FK_orders_customers_bill_customer_id");
    }

    [Fact(DisplayName = "外部キーの実効名は、保存された名前が無ければ既定名・あればそのまま")]
    public void EffectiveForeignKey_FallsBackToDefaultOnlyWhenUnnamed()
    {
        ConstraintNames
            .EffectiveForeignKey(null, "orders", "customers", IdentifierLengthLimit.Unlimited)
            .Should()
            .Be("FK_orders_customers");
        ConstraintNames
            .EffectiveForeignKey("  ", " orders ", "dbo.customers", IdentifierLengthLimit.Unlimited)
            .Should()
            .Be("FK_orders_dbo_customers");
        ConstraintNames
            .EffectiveForeignKey(
                "fk_custom",
                "orders",
                "customers",
                IdentifierLengthLimit.Unlimited
            )
            .Should()
            .Be("fk_custom");
    }

    /// <summary>外部キーを作らない名前の無い関係（多対多・列ペア無し・列を解決できない）を組み立てる</summary>
    /// <param name="kind">種類（ManyToMany / NoPairs / UnresolvedColumn）</param>
    private static Relationship NonForeignKeyRelationship(
        string kind,
        Entity customers,
        Entity orders
    ) =>
        kind switch
        {
            "ManyToMany" => new Relationship
            {
                SourceEntityId = customers.Id,
                TargetEntityId = orders.Id,
                Type = RelationshipType.ManyToMany,
            },
            "NoPairs" => new Relationship
            {
                SourceEntityId = customers.Id,
                TargetEntityId = orders.Id,
            },
            "UnresolvedColumn" => new Relationship
            {
                SourceEntityId = customers.Id,
                TargetEntityId = orders.Id,
                ColumnPairs = [new RelationshipColumnPair(customers.Columns[0].Id, Guid.NewGuid())],
            },
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };

    /// <summary>
    /// 外部キーを作らない名前の無い関係が先に並んでも、同じ組の本物の外部キーは既定名を取ることを検証する
    /// （DDL はその関係に外部キーを作らないので、既定名を取らせると本物の外部キーが一意化した名前へずれる）
    /// </summary>
    /// <param name="kind">外部キーを作らない関係の種類</param>
    [Theory(DisplayName = "外部キーを作らない名前の無い関係は既定名を取らず、名前も求めない")]
    [InlineData("ManyToMany")]
    [InlineData("NoPairs")]
    [InlineData("UnresolvedColumn")]
    public void ResolveForeignKeyNames_NonForeignKeyRelationship_DoesNotTakeDefault(string kind)
    {
        var (customers, orders) = BuildCustomersAndOrders();
        var other = NonForeignKeyRelationship(kind, customers, orders);
        var bill = Link(customers, orders, "bill_customer_id");
        var ship = Link(customers, orders, "ship_customer_id");

        var names = ConstraintNames.ResolveForeignKeyNames(
            [customers, orders],
            [other, bill, ship],
            IdentifierLengthLimit.Unlimited
        );

        names.Should().NotContainKey(other);
        names[bill].Should().Be("FK_orders_customers");
        names[ship].Should().Be("FK_orders_customers_ship_customer_id");

        ConstraintNames.NameUnnamedForeignKeys(
            [customers, orders],
            [other, bill, ship],
            IdentifierLengthLimit.Unlimited
        );

        other.ConstraintName.Should().BeNull();
        bill.ConstraintName.Should().BeNull("既定名のままなので名前を書き込まない");
        ship.ConstraintName.Should().Be("FK_orders_customers_ship_customer_id");
    }

    /// <summary>名前を持つ多対多は、外部キーを作らなくても名前を予約することを検証する</summary>
    [Fact(DisplayName = "名前を持つ多対多は外部キーを作らなくても名前を予約する")]
    public void ResolveForeignKeyNames_NamedManyToMany_StillReservesName()
    {
        var (customers, orders) = BuildCustomersAndOrders();
        var manyToMany = new Relationship
        {
            SourceEntityId = customers.Id,
            TargetEntityId = orders.Id,
            Type = RelationshipType.ManyToMany,
            ConstraintName = "FK_orders_customers",
        };
        var bill = Link(customers, orders, "bill_customer_id");

        var names = ConstraintNames.ResolveForeignKeyNames(
            [customers, orders],
            [manyToMany, bill],
            IdentifierLengthLimit.Unlimited
        );

        names[bill].Should().Be("FK_orders_customers_bill_customer_id");
    }

    /// <summary>既存の制約名の収集も、外部キーを作らない名前の無い関係の既定名を数えないことを検証する</summary>
    /// <param name="kind">外部キーを作らない関係の種類</param>
    [Theory(DisplayName = "既存の制約名は外部キーを作らない名前の無い関係の既定名を数えない")]
    [InlineData("ManyToMany")]
    [InlineData("NoPairs")]
    [InlineData("UnresolvedColumn")]
    public void CollectExisting_NonForeignKeyRelationship_IsNotCounted(string kind)
    {
        var (customers, orders) = BuildCustomersAndOrders();
        var diagram = new ErDiagram
        {
            Entities = { customers, orders },
            Relationships = { NonForeignKeyRelationship(kind, customers, orders) },
        };

        ConstraintNames
            .CollectExisting(diagram, IdentifierLengthLimits.ForDbms(diagram.TargetDbms))
            .Should()
            .BeEquivalentTo("PK_customers", "PK_orders");
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

        ConstraintNames.NameUnnamedForeignKeys(
            [customers, orders],
            [dangling, relationship],
            IdentifierLengthLimit.Unlimited
        );

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
            [bill, ship, named],
            IdentifierLengthLimit.Unlimited
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
        var resolved = ConstraintNames.ResolveForeignKeyNames(
            [customers, orders],
            [bill, ship],
            IdentifierLengthLimit.Unlimited
        );

        ConstraintNames.NameUnnamedForeignKeys(
            [customers, orders],
            [bill, ship],
            IdentifierLengthLimit.Unlimited
        );

        ConstraintNames
            .EffectiveForeignKey(
                bill.ConstraintName,
                orders.TableName,
                customers.TableName,
                IdentifierLengthLimit.Unlimited
            )
            .Should()
            .Be(resolved[bill]);
        ship.ConstraintName.Should().Be(resolved[ship]);
    }

    [Fact(
        DisplayName = "UniqueForeignKey: 番号の候補が上限まで埋まっていれば、回り続けずに例外で知らせる（その手前の番号は使える）"
    )]
    public void UniqueForeignKey_StopsAtCandidateLimit()
    {
        var max = ConstraintNames.MaxNumberedCandidates;
        var taken = new List<string> { "FK_c_p" };
        taken.AddRange(Enumerable.Range(2, max - 2).Select(n => $"FK_c_p_{n}"));

        ConstraintNames
            .UniqueForeignKey("c", "p", [], taken, IdentifierLengthLimit.Unlimited)
            .Should()
            .Be($"FK_c_p_{max}");

        taken.Add($"FK_c_p_{max}");
        var act = () =>
            ConstraintNames.UniqueForeignKey("c", "p", [], taken, IdentifierLengthLimit.Unlimited);

        act.Should().Throw<InvalidOperationException>();
    }

    /// <summary>
    /// 一意制約の名前の図全体の解決の 1 行（表の並び・制約・名前を持つ外部キー・上限と、期待する名前）
    /// </summary>
    /// <param name="Id">行の名前</param>
    /// <param name="Tables">表ごとに「表名, (制約名 or null, 構成列名…)…」。構成列はその表に作る</param>
    /// <param name="ForeignKeyName">図にある名前を持つ外部キーの名前（無ければ null）</param>
    /// <param name="Limit">識別子の上限（0 は上限なし）</param>
    /// <param name="Expected">表の並び・制約の並びの名前（解決しない制約は null）</param>
    public sealed record UniqueNameRow(
        string Id,
        (string Table, (string? Name, string[] Columns)[] Constraints)[] Tables,
        string? ForeignKeyName,
        int Limit,
        string?[] Expected
    )
    {
        public override string ToString() => Id;
    }

    private static readonly IdentifierLengthLimit Limit30 = new(
        IdentifierLengthUnit.Characters,
        30
    );

    /// <summary>上限 30 を超える合成名の、縮める前の形（表 bbbb… の列 xxxx…）</summary>
    private const string LongUnshortened = "UQ_bbbbbbbbbbbbbbbbbbbb_xxxxxxxxxxxxxxxxxxxx";

    public static TheoryData<UniqueNameRow> UniqueNameRows =>
        new()
        {
            // ぶつからない図は SynthesizeName のまま
            new UniqueNameRow("no-collision", [("t", [(null, ["x"])])], null, 0, ["UQ_t_x"]),
            // ほかの表の名前を持つ制約とぶつかる（同期計画の種 386 の形）
            new UniqueNameRow(
                "other-table-named",
                [("a", [("UQ_b_x", ["y"])]), ("b", [(null, ["x"])])],
                null,
                0,
                ["UQ_b_x", "UQ_b_x_2"]
            ),
            // 名前を持つ制約は並びに依らず先に予約する（後ろの表の名前でも避ける）
            new UniqueNameRow(
                "later-table-named",
                [("b", [(null, ["x"])]), ("a", [("UQ_b_x", ["y"])])],
                null,
                0,
                ["UQ_b_x_2", "UQ_b_x"]
            ),
            // 名前の比較は大文字小文字を区別しない（SchemaNames の規則）
            new UniqueNameRow(
                "case-insensitive",
                [("a", [("uq_B_X", ["y"])]), ("b", [(null, ["x"])])],
                null,
                0,
                ["uq_B_X", "UQ_b_x_2"]
            ),
            // 名前の無い制約どうしの合成名がぶつかる（a_b.c と a.b_c）＝後ろが番号つき
            new UniqueNameRow(
                "two-unnamed",
                [("a_b", [(null, ["c"])]), ("a", [(null, ["b_c"])])],
                null,
                0,
                ["UQ_a_b_c", "UQ_a_b_c_2"]
            ),
            // 名前を持つ外部キーとぶつかる（MySQL の外部キーの索引・スキーマ単位の制約名）
            new UniqueNameRow(
                "foreign-key-name",
                [("b", [(null, ["x"])])],
                "UQ_b_x",
                0,
                ["UQ_b_x_2"]
            ),
            // 同じ表の名前を持つ制約とぶつかる
            new UniqueNameRow(
                "same-table-named",
                [("b", [("UQ_b_x", ["y"]), (null, ["x"])])],
                null,
                0,
                ["UQ_b_x", "UQ_b_x_2"]
            ),
            // 構成列を解決できない制約は名前を決めない（DDL が出さない）
            new UniqueNameRow("unresolvable", [("t", [(null, [])])], null, 0, [null]),
            // 上限のある方言では、縮めた合成名がほかの名前とぶつかれば、番号つきの名前も上限に収める
            new UniqueNameRow(
                "limit",
                [
                    ("a", [(Limit30.Fit(LongUnshortened), ["y"])]),
                    ("bbbbbbbbbbbbbbbbbbbb", [(null, ["xxxxxxxxxxxxxxxxxxxx"])]),
                ],
                null,
                30,
                [Limit30.Fit(LongUnshortened), Limit30.Fit(LongUnshortened + "_2")]
            ),
        };

    [Theory(DisplayName = "一意制約の名前の図全体の解決: ぶつかる合成名だけに番号を足す")]
    [MemberData(nameof(UniqueNameRows))]
    public void ResolveUniqueConstraintNames_NumbersOnlyCollidingSynthesizedNames(UniqueNameRow row)
    {
        var limit = row.Limit == 0 ? IdentifierLengthLimit.Unlimited : Limit30;
        var entities = new List<Entity>();
        var constraints = new List<UniqueConstraint>();

        foreach (var (table, tableConstraints) in row.Tables)
        {
            var entity = new Entity
            {
                TableName = table,
                Columns =
                {
                    new Column { Name = "id", IsPrimaryKey = true },
                },
            };

            foreach (var (name, columns) in tableConstraints)
            {
                var constraint = new UniqueConstraint { Name = name };

                foreach (var columnName in columns)
                {
                    var column = new Column { Name = columnName };
                    entity.Columns.Add(column);
                    constraint.ColumnIds.Add(column.Id);
                }

                entity.UniqueConstraints.Add(constraint);
                constraints.Add(constraint);
            }

            entities.Add(entity);
        }

        var relationships = row.ForeignKeyName is null
            ? new List<Relationship>()
            : [new Relationship { ConstraintName = row.ForeignKeyName }];

        var names = ConstraintNames.ResolveUniqueConstraintNames(entities, relationships, limit);

        constraints
            .Select(constraint => names.TryGetValue(constraint, out var name) ? name : null)
            .Should()
            .Equal(row.Expected);
        names.Values.Should().OnlyContain(name => limit.Accepts(name));

        // 既存の制約名の集合も同じ名前を数える（ツールの手作成の外部キー名が避ける）
        var existing = ConstraintNames.CollectExisting(
            new ErDiagram { Entities = entities, Relationships = relationships },
            limit
        );
        if (names.Count > 0)
        {
            existing.Should().Contain(names.Values);
        }
    }

    [Fact(DisplayName = "外部キーの名前の解決は、図全体で決めた一意制約の名前を避ける")]
    public void ResolveForeignKeyNames_AvoidsResolvedUniqueNames()
    {
        // 名前の無い一意制約の番号つきの名前と、名前の無い外部キーの既定名は先頭（UQ_ / FK_）で分かれるので、
        // 予約に入っていることを CollectTableConstraints で確かめる
        var b = new Entity
        {
            TableName = "b",
            Columns =
            {
                new Column { Name = "id", IsPrimaryKey = true },
                new Column { Name = "x" },
            },
        };
        b.UniqueConstraints.Add(new UniqueConstraint { ColumnIds = { b.Columns[1].Id } });
        var relationship = new Relationship { ConstraintName = "UQ_b_x" };

        ConstraintNames
            .CollectTableConstraints([b], [relationship], IdentifierLengthLimit.Unlimited)
            .Should()
            .Contain("UQ_b_x_2")
            .And.NotContain("UQ_b_x");
    }
}
