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
    /// <summary>
    /// 安全化の規則と冪等（安全化済みの名前をもう一度通しても変わらない＝先に安全化してから合成する経路と結果が揃う）を、同じ行で確かめる
    /// </summary>
    /// <remarks>
    /// 既定名の合成（<c>FK_{子}_{親}</c>・<c>PK_{表}</c>・一意制約の合成名）が安全化した名前を使うことは、方言の DDL の表の行
    /// <c>ddl-constraint-names-schema-qualified-trimmed-table</c>・<c>ddl-unique-unnamed-synthesized-name-is-safe</c> が出力の制約名で固定する。
    /// </remarks>
    [Theory(DisplayName = "SafeName は前後の空白を除いてから . と空白を _ へ置き換え、冪等")]
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
        ConstraintNames.SafeName(expected).Should().Be(expected, "冪等");
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

    /// <summary>
    /// 衝突しない外部キー名の 1 行（子・親・子の列・予約済みの名前と、期待する名前）
    /// </summary>
    /// <param name="Id">行の名前</param>
    /// <param name="Columns">子の列（宣言順）</param>
    /// <param name="Taken">予約済みの名前</param>
    /// <param name="Expected">期待する名前</param>
    public sealed record UniqueForeignKeyRow(
        string Id,
        string[] Columns,
        string[] Taken,
        string Expected
    )
    {
        public override string ToString() => Id;
    }

    /// <summary>子 Orders・親 Customers の衝突しない外部キー名の行（上限なし）</summary>
    public static TheoryData<UniqueForeignKeyRow> UniqueForeignKeyRows =>
        new()
        {
            // 既定名が空いていれば既定名のまま
            new UniqueForeignKeyRow(
                "default-name-free",
                ["BillingCustomerId"],
                ["PK_Orders"],
                "FK_Orders_Customers"
            ),
            // 既定名がふさがっていれば子の列名を足す
            new UniqueForeignKeyRow(
                "default-name-taken-appends-child-columns",
                ["ShippingCustomerId"],
                ["FK_Orders_Customers"],
                "FK_Orders_Customers_ShippingCustomerId"
            ),
            // 足す列名は宣言順につなぎ、それぞれ安全化する
            new UniqueForeignKeyRow(
                "appends-safe-column-names-in-order",
                ["tenant id", "Ship.Code"],
                ["FK_Orders_Customers"],
                "FK_Orders_Customers_tenant_id_Ship_Code"
            ),
            // 列名を足してもふさがっていれば番号を足す
            new UniqueForeignKeyRow(
                "column-name-also-taken-appends-number",
                ["CustomerId"],
                [
                    "FK_Orders_Customers",
                    "FK_Orders_Customers_CustomerId",
                    "FK_Orders_Customers_CustomerId_2",
                ],
                "FK_Orders_Customers_CustomerId_3"
            ),
            // 列が無ければ既定名に番号を足す
            new UniqueForeignKeyRow(
                "no-columns-appends-number-to-default",
                [],
                ["FK_Orders_Customers"],
                "FK_Orders_Customers_2"
            ),
            // 安全化で空になる列名は足さない（列が無いのと同じく番号を足す。末尾が _ の名前にしない）
            new UniqueForeignKeyRow(
                "blank-column-name-is-skipped",
                ["  "],
                ["FK_Orders_Customers"],
                "FK_Orders_Customers_2"
            ),
            // 大文字小文字だけ違う名前も衝突とみなす
            new UniqueForeignKeyRow(
                "ignores-case",
                ["ShipTo"],
                ["fk_orders_customers"],
                "FK_Orders_Customers_ShipTo"
            ),
        };

    [Theory(
        DisplayName = "衝突しない外部キー名は、既定名・子の列名つき・番号つきの順に空いている名前を選ぶ"
    )]
    [MemberData(nameof(UniqueForeignKeyRows))]
    public void UniqueForeignKey_PicksFirstFreeName(UniqueForeignKeyRow row)
    {
        ConstraintNames
            .UniqueForeignKey(
                "Orders",
                "Customers",
                row.Columns,
                row.Taken,
                IdentifierLengthLimit.Unlimited
            )
            .Should()
            .Be(row.Expected);
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

    // 既定名が空いていれば名前を持たないまま・同じ組の 2 本目に子の列名つきの名前を付ける（NameUnnamedForeignKeys が図へ書く名前）は、
    // 形式の往復のエンジンの規則の行 dbml-rule-single-unnamed-ref-stays-unnamed・dbml-rule-same-pair-twice-without-names-second-gets-unique-name
    // が同じ図（customers・orders の bill / ship）の取込で固定する（取込は NameUnnamedForeignKeys を通る）

    /// <remarks>
    /// 残す理由（P3-6）：名前を持つ側の綴りが既定名と大文字小文字だけ違っても既定名をふさぐこと（予約の大小無視）と、図へ ConstraintName を
    /// 書き込む挙動（移した先：同じ綴りの形は形式の往復のエンジンの規則の行 <c>dbml-rule-named-ref-takes-default-unnamed-is-renamed</c>）
    /// </remarks>
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

    /// <remarks>
    /// 残す理由（P3-6）：NameUnnamedForeignKeys が図へ ConstraintName を書き込む挙動（取込の経路）。DDL が付ける名前は方言の DDL の表の行
    /// <c>ddl-fk-unnamed-avoids-unique-constraint-name</c> へ移した
    /// </remarks>
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

    /// <summary>保存された名前（null・空白だけ・名前あり）ごとの外部キーの実効名（既定名は前後の空白と . を安全化する）</summary>
    [Theory(DisplayName = "外部キーの実効名は、保存された名前が無ければ既定名・あればそのまま")]
    [InlineData(null, "orders", "customers", "FK_orders_customers")]
    [InlineData("  ", " orders ", "dbo.customers", "FK_orders_dbo_customers")]
    [InlineData("fk_custom", "orders", "customers", "fk_custom")]
    public void EffectiveForeignKey_FallsBackToDefaultOnlyWhenUnnamed(
        string? stored,
        string child,
        string parent,
        string expected
    )
    {
        ConstraintNames
            .EffectiveForeignKey(stored, child, parent, IdentifierLengthLimit.Unlimited)
            .Should()
            .Be(expected);
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
    /// <remarks>
    /// 残す理由（P3-6）：外部キー名の解決の戻りにその関係が無いことと、NameUnnamedForeignKeys がその関係と 1 本目に名前を書かず 2 本目にだけ
    /// 書くこと（純粋関数の契約）。同じ組の本物の外部キーが DDL で既定名を取り 2 本目が子の列名つきになることは、方言の DDL の表の行
    /// <c>ddl-fk-unnamed-non-fk-relationship-first-*</c>（3 種）へ移した
    /// </remarks>
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

        ConstraintNames.NameUnnamedForeignKeys(
            [customers, orders],
            [other, bill, ship],
            IdentifierLengthLimit.Unlimited
        );

        other.ConstraintName.Should().BeNull();
        bill.ConstraintName.Should().BeNull("既定名のままなので名前を書き込まない");
        ship.ConstraintName.Should().Be("FK_orders_customers_ship_customer_id");
    }

    // 名前を持つ多対多が外部キーを作らなくても名前を予約することは、方言の DDL の表の行 ddl-fk-named-many-to-many-reserves-name が固定する

    /// <summary>
    /// 既存の制約名の収集が、外部キーを作らない名前の無い関係・名前を持つ関係・親か子の表が図に無い関係の既定名を数えないことを検証する
    /// </summary>
    /// <param name="kind">
    /// 関係の種類（外部キーを作らない ManyToMany / NoPairs / UnresolvedColumn、列ペアを解決できる名前つきの Named、
    /// 列ペアを持つが親・子の表が図に無い ParentMissing / ChildMissing）
    /// </param>
    /// <param name="extra">主キーの名前のほかに数える名前（無ければ null）</param>
    /// <remarks>
    /// Named は名前だけを数え、既定名 <c>FK_orders_customers</c> を数えない（名前を持つ関係の既定名の計算へ落ちない）。
    /// ParentMissing / ChildMissing は列ペアを持たせて、表の引き当てを飛ばしたときに既定名の組み立てまで届く形にする。
    /// </remarks>
    [Theory(
        DisplayName = "既存の制約名は、外部キーを作らない関係・名前を持つ関係・表が図に無い関係の既定名を数えない"
    )]
    [InlineData("ManyToMany", null)]
    [InlineData("NoPairs", null)]
    [InlineData("UnresolvedColumn", null)]
    [InlineData("Named", "fk_named")]
    [InlineData("ParentMissing", null)]
    [InlineData("ChildMissing", null)]
    public void CollectExisting_NonForeignKeyRelationship_IsNotCounted(string kind, string? extra)
    {
        var (customers, orders) = BuildCustomersAndOrders();
        var relationship = kind switch
        {
            "Named" => Link(customers, orders, "bill_customer_id", "fk_named"),
            "ParentMissing" => WithEnds(
                Link(customers, orders, "bill_customer_id"),
                Guid.NewGuid(),
                orders.Id
            ),
            "ChildMissing" => WithEnds(
                Link(customers, orders, "bill_customer_id"),
                customers.Id,
                Guid.NewGuid()
            ),
            _ => NonForeignKeyRelationship(kind, customers, orders),
        };
        var diagram = new ErDiagram
        {
            Entities = { customers, orders },
            Relationships = { relationship },
        };
        string[] expected = extra is null
            ? ["PK_customers", "PK_orders"]
            : ["PK_customers", "PK_orders", extra];

        ConstraintNames
            .CollectExisting(diagram, IdentifierLengthLimits.ForDbms(diagram.TargetDbms))
            .Should()
            .BeEquivalentTo(expected);
    }

    /// <summary>リレーションの親・子の表の Id を差し替える（列ペアはそのまま）</summary>
    private static Relationship WithEnds(Relationship relationship, Guid source, Guid target)
    {
        relationship.SourceEntityId = source;
        relationship.TargetEntityId = target;

        return relationship;
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

    /// <remarks>
    /// 残す理由（P3-6）：名前を求めるだけで図の ConstraintName に書かないこと（副作用なし）。求めた 3 つの名前は方言の DDL の表の行
    /// <c>ddl-fk-unnamed-same-pair-twice-with-named-third</c> へ移した
    /// </remarks>
    [Fact(DisplayName = "外部キー名の解決は名前を求めるだけで、図は書き換えない")]
    public void ResolveForeignKeyNames_SamePairTwice_UniqueWithoutMutation()
    {
        var (customers, orders) = BuildCustomersAndOrders();
        var bill = Link(customers, orders, "bill_customer_id");
        var ship = Link(customers, orders, "ship_customer_id");
        var named = Link(customers, orders, "ship_customer_id", name: "FK_custom");

        ConstraintNames.ResolveForeignKeyNames(
            [customers, orders],
            [bill, ship, named],
            IdentifierLengthLimit.Unlimited
        );

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
            // 名前の無い制約どうしの合成名がぶつかる（a_b.c・a.b_c・a.b の c）＝後ろほど番号つき。
            // 3 つ目は 2 つ目が選んだ番号つきの名前も避ける（番号つきの名前も予約に加える）
            new UniqueNameRow(
                "three-unnamed",
                [("a_b", [(null, ["c"])]), ("a", [(null, ["b_c"])]), ("a.b", [(null, ["c"])])],
                null,
                0,
                ["UQ_a_b_c", "UQ_a_b_c_2", "UQ_a_b_c_3"]
            ),
            // 空白だけの名前は名前の無い制約と同じく合成名になる
            new UniqueNameRow(
                "blank-name-is-synthesized",
                [("t", [("  ", ["x"])])],
                null,
                0,
                ["UQ_t_x"]
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
        var diagram = new ErDiagram { Entities = entities, Relationships = relationships };
        var existing = ConstraintNames.CollectExisting(diagram, limit);
        if (names.Count > 0)
        {
            existing.Should().Contain(names.Values);
        }

        // 一意制約の名前の付け直し（IsTaken）：解決した名前は新しい制約にはふさがり、その制約自身には空いている
        // （名前を持たない・空白だけの名前の制約は、自分の合成名を予約から外す）
        foreach (var (constraint, name) in names)
        {
            ConstraintNames
                .IsTaken(diagram, name, null, limit)
                .Should()
                .BeTrue($"{name} は新しい制約にはふさがっている");
            ConstraintNames
                .IsTaken(diagram, name, constraint.Id, limit)
                .Should()
                .BeFalse($"{name} はその制約自身には空いている");
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
