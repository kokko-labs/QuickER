using QuickER.Dialects;
using QuickER.Model;
using QuickER.Tests.TestSupport.Engines;

namespace QuickER.Tests.Gui.Services.FormatRoundTrip;

/// <summary>
/// C# リバースのマージ取込の行（CSharpReversePrimaryKeyOrderTests・ReverseMergePostProcessorTests・CSharpReverseUnnamedForeignKeyTests から移した。
/// 経路は製品の CodeReverseCommandService と同じ並び＝Reconcile → ReverseMergePostProcessor の Apply・ApplyPrimaryKeyOrder・ApplyPrimaryKeyIdentityMarks）
/// </summary>
/// <remarks>
/// 元のテストが後処理の 1 段だけを手で組んだ入力（マージ後のエンティティ・リレーション・メタデータの索引）に当てていたものは、
/// 同じ状態になる C# のソースを全体の経路へ通す形に置き換えた（どの入力を使ったかは行ごとのコメントに書く）。
/// </remarks>
internal static partial class FormatMergeRows
{
    private static IEnumerable<ScenarioRow<FormatMerge>> CSharpReverseMergeRows() =>
        [
            .. CSharpPrimaryKeyOrderMergeRows(),
            .. CSharpRelationshipMergeRows(),
            .. CSharpUnnamedForeignKeyMergeRows(),
        ];

    /// <summary>CSharpReversePrimaryKeyOrderTests のマージ取込の規則（古い生成コードでは現在図の順序を引き継ぐ）</summary>
    private static IEnumerable<ScenarioRow<FormatMerge>> CSharpPrimaryKeyOrderMergeRows() =>
        [
            // 引き継がないと宣言順へ戻り、次の DB 同期で主キーの張り直しが出る。生成物から順序の引数だけを消して「古い版の生成コード」を作る
            Merge(
                    FormatMergePath.CSharpReverse,
                    "merge-csharp-old-generated-code-keeps-current-key-order",
                    "古い生成コードをマージ取込しても、現在図の主キーの順序が保たれる"
                )
                .Current(() =>
                    FormatShapeRows.CSharpPrimaryKeyOrderDiagram(["order_no", "tenant_id"])
                )
                .ImportedInput(adapter =>
                    adapter.FromText(
                        WithoutPrimaryKeyOrder(
                            PrimaryKeyOrderSource(adapter, ["order_no", "tenant_id"])
                        )
                    )
                )
                .Settings(PrimaryKeyOrderSettings)
                .Expect(e => e.Merged.Single("PrimaryKeyOrder order_lines = order_no,tenant_id")),
            // コードが順序を書いていれば（TablesWithPrimaryKeyOrder に載れば）、現在図の順序で上書きしない（コードが勝つ）。
            // 元のテストは「コードが順序を書いたがマージ後の主キーの順序は空」の状態を手で組んでいた。全体の経路では、
            // 主キー列と一致しない順序を書いたコード（リバースが警告して採らない）が同じ状態になる
            Merge(
                    FormatMergePath.CSharpReverse,
                    "merge-csharp-code-specified-order-wins",
                    "コードが順序を書いていれば、マージ取込は現在図の順序で上書きしない"
                )
                .Current(() =>
                    FormatShapeRows.CSharpPrimaryKeyOrderDiagram(["order_no", "tenant_id"])
                )
                .ImportedInput(adapter =>
                    adapter.FromText(
                        PrimaryKeyOrderSource(adapter, ["order_no", "tenant_id"])
                            .Replace(
                                FormatRuleRows.GeneratedPrimaryKeyOrderArray,
                                "new[] { \"OrderNo\" }",
                                StringComparison.Ordinal
                            )
                    )
                )
                .Settings(PrimaryKeyOrderSettings)
                .Expect(e =>
                {
                    e.Merged.Single("PrimaryKeyOrder order_lines = tenant_id,order_no");
                    e.That.Add(OrderLinesPrimaryKeyOrderNotExplicit());
                }),
            // 名前で引く辞書を作っていたため ArgumentException になり、GUI のマージ取込がクラッシュ処理へ入っていた。
            // 元のテストはマージ後の主キー列の名前を手で重ねていた。全体の経路では、主キー列の名前が重複する古い生成コード
            // （パーサーが警告つきで通す）が同じ状態になる
            Merge(
                    FormatMergePath.CSharpReverse,
                    "merge-csharp-duplicate-key-names-is-not-carried",
                    "主キー列の名前が重複していれば、マージ取込は例外にせず順序を引き継がない"
                )
                .Current(() =>
                    FormatShapeRows.CSharpPrimaryKeyOrderDiagram(["order_no", "tenant_id"])
                )
                .ImportedInput(adapter =>
                    adapter.FromText(
                        Replaced(
                            WithoutPrimaryKeyOrder(
                                PrimaryKeyOrderSource(adapter, ["order_no", "tenant_id"])
                            ),
                            "[Column(\"order_no\")]",
                            "[Column(\"tenant_id\")]"
                        )
                    )
                )
                .Settings(PrimaryKeyOrderSettings)
                .Expect(e => e.That.Add(OrderLinesPrimaryKeyOrderNotExplicit())),
            // 主キー列の集合が変わっていれば、古い順序は持ち込まない（コードが語った構成の変更）
            Merge(
                    FormatMergePath.CSharpReverse,
                    "merge-csharp-key-set-changed-is-not-carried",
                    "主キー列の集合が変わっていれば、マージ取込は古い順序を持ち込まない"
                )
                .Current(() =>
                    FormatShapeRows.CSharpPrimaryKeyOrderDiagram(["order_no", "tenant_id"])
                )
                .Imported(() =>
                {
                    var diagram = FormatShapeRows.CSharpPrimaryKeyOrderDiagram(null);
                    diagram
                        .Entities[0]
                        .Columns.Single(column => column.Name == "memo")
                        .IsPrimaryKey = true;
                    return diagram;
                })
                .Settings(PrimaryKeyOrderSettings)
                .Expect(e => e.That.Add(OrderLinesPrimaryKeyOrderNotExplicit())),
            // 引き継ぐのは明示された順序だけ。列宣言順のフォールバックを書き込むと順序が固定され、以後の列の並べ替えが
            // 主キーの順序に反映されなくなる。宣言順どおりの主キーには順序を出さない＝コードは順序を書いていない
            Merge(
                    FormatMergePath.CSharpReverse,
                    "merge-csharp-current-order-not-explicit-stays-not-explicit",
                    "現在図が順序を明示していなければ、マージ取込後も未明示のまま"
                )
                .Current(() => FormatShapeRows.CSharpPrimaryKeyOrderDiagram(null))
                .ImportedInput(adapter =>
                {
                    var source = PrimaryKeyOrderSource(adapter, null);

                    return source.Contains("PrimaryKeyOrder =", StringComparison.Ordinal)
                        ? throw new InvalidOperationException(
                            "宣言順どおりの主キーには順序を出さない（コードは順序を書いていない）はず"
                        )
                        : adapter.FromText(source);
                })
                .Settings(PrimaryKeyOrderSettings)
                .Expect(e =>
                {
                    e.Merged.Single("PrimaryKeyOrder order_lines = tenant_id,order_no");
                    e.That.Add(OrderLinesPrimaryKeyOrderNotExplicit());
                }),
        ];

    /// <summary>
    /// ReverseMergePostProcessorTests（未指定の参照アクション・制約名だけを補完する fallback 専用の引継ぎ・多対多の温存・
    /// コードで消えた通常リレーションの非追加）。元のテストはマージ後のエンティティ・リレーション・メタデータの索引を手で組んでいた。
    /// 全体の経路では、同じリレーション（とその指定）をナビゲーションの属性に書いたソースが同じ状態になる
    /// </summary>
    private static IEnumerable<ScenarioRow<FormatMerge>> CSharpRelationshipMergeRows() =>
        [
            // メタデータ索引が無い（＝コードが外部キーメタデータを一切指定していない・旧形式コード）なら、端点一致する現在図の値を全項目温存する
            Merge(
                    FormatMergePath.CSharpReverse,
                    "merge-csharp-carries-over-actions-and-constraint-name-when-code-specifies-nothing",
                    "コード未指定なら OnDelete/OnUpdate/ConstraintName を温存する"
                )
                .Current(CustomersOrdersWithForeignKey)
                .ImportedText(CustomersOrdersCode(string.Empty))
                .Expect(e =>
                    e.Merged.Count("Relationship *", 1)
                        .None("ManyToMany *")
                        .Single("ReferentialAction customers->orders#0 = del=Cascade upd=SetNull")
                        .Single("ConstraintName customers->orders#0 = FK_orders_customers")
                ),
            // コードは OnDelete のみ明示（NoAction＝図の Cascade を意図的に外した状態）。制約名・OnUpdate は未指定なので現在図から補完される
            // （fallback 専用＝コード上で NO ACTION へ戻した変更が現在図の値で握り潰されない）
            Merge(
                    FormatMergePath.CSharpReverse,
                    "merge-csharp-prefers-code-specified-fields-and-fills-only-unspecified",
                    "コード指定フィールドはコードが勝ち、未指定フィールドのみ温存する"
                )
                .Current(CustomersOrdersWithForeignKey)
                .ImportedText(CustomersOrdersCode(", OnDelete = \"NoAction\""))
                .Expect(e =>
                    e.Merged.Count("Relationship *", 1)
                        .Single("ReferentialAction customers->orders#0 = del=NoAction upd=SetNull")
                        .Single("ConstraintName customers->orders#0 = FK_orders_customers")
                ),
            Merge(
                    FormatMergePath.CSharpReverse,
                    "merge-csharp-keeps-code-values-when-all-fields-specified",
                    "コードが全フィールド指定なら現在図の値で上書きしない"
                )
                .Current(CustomersOrdersWithForeignKey)
                .ImportedText(
                    CustomersOrdersCode(
                        ", ConstraintName = \"FK_orders_customers_v2\", OnDelete = \"SetDefault\", OnUpdate = \"NoAction\""
                    )
                )
                .Expect(e =>
                    e.Merged.Count("Relationship *", 1)
                        .Single("ConstraintName customers->orders#0 = FK_orders_customers_v2")
                        .Single(
                            "ReferentialAction customers->orders#0 = del=SetDefault upd=NoAction"
                        )
                ),
            // 現在図の多対多は、両端エンティティが生存していれば結果へ温存される
            Merge(
                    FormatMergePath.CSharpReverse,
                    "merge-csharp-preserves-many-to-many-when-both-entities-survive",
                    "両端生存の多対多は温存される"
                )
                .Current(() => CustomersOrdersWith(ManyToMany))
                .ImportedText(TablesCode(includeOrders: true))
                .Expect(e =>
                    e.Merged.Count("RelationshipType *", 1)
                        .Single("RelationshipType * = ManyToMany")
                ),
            // マージ結果には customers しか残っていない（orders はコードから消えた）
            Merge(
                    FormatMergePath.CSharpReverse,
                    "merge-csharp-drops-many-to-many-when-one-entity-missing",
                    "片端が消えた多対多は温存されない"
                )
                .Current(() => CustomersOrdersWith(ManyToMany))
                .ImportedText(TablesCode(includeOrders: false))
                .Expect(e => e.Merged.None("RelationshipType *")),
            // 現在図は「列ペアを持たない多対多」だけを持つ。コード由来の 1 対多は型メタ欠落の列を端点に持つため列ペアを解決できず退化し、
            // 退化したリレーション（列ペアなし）と端点キーが一致し得るため、多対多を補完元にしてはならない。
            // 1 対多（コード由来）＋多対多（温存）の 2 本で、1 対多は多対多のメタデータを継承しない
            Merge(
                    FormatMergePath.CSharpReverse,
                    "merge-csharp-does-not-fill-from-many-to-many-when-endpoints-degenerate",
                    "同エンティティ間の多対多は通常リレーションの補完元にならない"
                )
                .Current(() =>
                    CustomersOrdersWith(
                        (customers, orders) =>
                            new Relationship
                            {
                                SourceEntityId = customers.Id,
                                TargetEntityId = orders.Id,
                                Type = RelationshipType.ManyToMany,
                                ConstraintName = "FK_many_to_many",
                                OnDelete = ForeignKeyReferentialAction.Cascade,
                                OnUpdate = ForeignKeyReferentialAction.SetDefault,
                            }
                    )
                )
                .ImportedText(DegeneratedOneToManyCode)
                .Expect(e =>
                {
                    e.Merged.Count("RelationshipType *", 2)
                        .Single("RelationshipType * = OneToMany")
                        .Single("RelationshipType * = ManyToMany");
                    e.That.Add(
                        (
                            "1 対多は多対多の制約名・参照アクションを引き継がない（名前なし・NoAction）",
                            (_, outcome) =>
                                outcome.Merged.Relationships.Single(r =>
                                    r.Type == RelationshipType.OneToMany
                                )
                                    is var oneToMany
                                && oneToMany.ConstraintName is null
                                && oneToMany.OnDelete == ForeignKeyReferentialAction.NoAction
                                && oneToMany.OnUpdate == ForeignKeyReferentialAction.NoAction
                        )
                    );
                }),
            // コード側（マージ結果）にはリレーションが無い＝この 1 対多は消えた
            Merge(
                    FormatMergePath.CSharpReverse,
                    "merge-csharp-does-not-re-add-removed-normal-relationship",
                    "コードで消えた 1 対多は結果に含まれない"
                )
                .Current(() =>
                    CustomersOrdersWith(
                        (customers, orders) =>
                            new Relationship
                            {
                                SourceEntityId = customers.Id,
                                TargetEntityId = orders.Id,
                                ColumnPairs = [new(customers.Columns[0].Id, orders.Columns[0].Id)],
                                Type = RelationshipType.OneToMany,
                                ConstraintName = "FK_orders_customers",
                            }
                    )
                )
                .ImportedText(TablesCode(includeOrders: true))
                .Expect(e => e.Merged.None("RelationshipType *")),
        ];

    /// <summary>
    /// CSharpReverseUnnamedForeignKeyTests の GUI のマージ取込（元のテストは CodeReverseCommandService を一時ファイルとスタブのホストで通していた。
    /// 経路の CSharpReverse は同じ並び）。現在図は修正前のリバースで作った図と同じ形（同じ組に名前の無い 2 本）
    /// </summary>
    private static IEnumerable<ScenarioRow<FormatMerge>> CSharpUnnamedForeignKeyMergeRows() =>
        [
            Merge(
                    FormatMergePath.CSharpReverse,
                    "merge-csharp-current-with-duplicate-unnamed-references-gets-distinct-names",
                    "GUI マージで現在図の名前が null に戻っても、DDL の外部キー名は重ならない"
                )
                .Current(() => ShipBillCurrent())
                .ImportedText(FormatRuleRows.ShipBillSource())
                .Expect(e =>
                {
                    var (description, predicate) = FormatRuleRows.DistinctForeignKeyNamesInDdl(2);
                    e.That.Add((description, (_, outcome) => predicate(outcome.Merged)));
                }),
            Merge(
                    FormatMergePath.CSharpReverse,
                    "merge-csharp-names-supplied-by-current-diagram-are-kept",
                    "GUI マージで現在図から補完した名前は変えない"
                )
                .Current(() => ShipBillCurrent("FK_CurrentShip", "FK_CurrentBill"))
                .ImportedText(FormatRuleRows.ShipBillSource())
                .Expect(e =>
                    e.That.Add(
                        (
                            "子の列 ship_customer_id のリレーションが FK_CurrentShip、bill_customer_id のリレーションが FK_CurrentBill",
                            (_, outcome) =>
                                FormatRuleRows.ConstraintNameOfChild(
                                    outcome.Merged,
                                    "ship_customer_id"
                                ) == "FK_CurrentShip"
                                && FormatRuleRows.ConstraintNameOfChild(
                                    outcome.Merged,
                                    "bill_customer_id"
                                ) == "FK_CurrentBill"
                        )
                    )
                ),
        ];

    /// <summary>CSharpReversePrimaryKeyOrderTests の生成のオプションと方言（SQL Server）</summary>
    private static FormatSettings PrimaryKeyOrderSettings =>
        new(FormatRuleRows.PrimaryKeyOrderOptions, DialectNames.SqlServer);

    /// <summary>order_lines の図（主キーの順序を明示するか）から生成したソース</summary>
    private static string PrimaryKeyOrderSource(FormatAdapter adapter, string[]? order)
    {
        using var artifact = adapter.Export(
            FormatShapeRows.CSharpPrimaryKeyOrderDiagram(order),
            PrimaryKeyOrderSettings
        );
        return artifact.Text;
    }

    /// <summary>生成物から主キーの順序の属性だけを消す（順序の引数を持たない＝古い版の生成コード）</summary>
    private static string WithoutPrimaryKeyOrder(string source)
    {
        var old = Replaced(
            source,
            $"[DbTableMeta(PrimaryKeyOrder = {FormatRuleRows.GeneratedPrimaryKeyOrderArray})]",
            string.Empty
        );

        return old.Contains("PrimaryKeyOrder =", StringComparison.Ordinal)
            ? throw new InvalidOperationException("順序の引数が残っている")
            : old;
    }

    /// <summary>ソースの断片を置き換える（断片が無ければ入力を組めない）</summary>
    private static string Replaced(string source, string fragment, string replacement) =>
        source.Contains(fragment, StringComparison.Ordinal)
            ? source.Replace(fragment, replacement, StringComparison.Ordinal)
            : throw new InvalidOperationException($"ソースに {fragment} が無い");

    /// <summary>マージ後の order_lines の主キーの順序が明示されていない（PrimaryKeyColumnIds が空）</summary>
    private static (
        string,
        Func<ErDiagram, FormatMergeOutcome, bool>
    ) OrderLinesPrimaryKeyOrderNotExplicit() =>
        (
            "マージ後の order_lines の主キーの順序を明示しない（PrimaryKeyColumnIds が空）",
            (_, outcome) => outcome.Merged.Entities.Single().PrimaryKeyColumnIds.Count == 0
        );

    /// <summary>
    /// customers(customer_id) と orders(customer_id) の現在図（ReverseMergePostProcessorTests.Customer・Order。列は名前だけ）に、
    /// 両端を受けて作るリレーションを足す
    /// </summary>
    private static ErDiagram CustomersOrdersWith(
        params Func<Entity, Entity, Relationship>[] relationships
    )
    {
        var customers = new Entity
        {
            TableName = "customers",
            Columns = { new Column { Name = "customer_id" } },
        };
        var orders = new Entity
        {
            TableName = "orders",
            Columns = { new Column { Name = "customer_id" } },
        };
        var diagram = new ErDiagram { Entities = { customers, orders } };

        foreach (var relationship in relationships)
        {
            diagram.Relationships.Add(relationship(customers, orders));
        }

        return diagram;
    }

    /// <summary>
    /// customers.customer_id → orders.customer_id の 1 対多（Cascade / SetNull・FK_orders_customers）を持つ現在図
    /// （ReverseMergePostProcessorTests.CurrentDiagram）
    /// </summary>
    private static ErDiagram CustomersOrdersWithForeignKey() =>
        CustomersOrdersWith(
            (customers, orders) =>
                new Relationship
                {
                    SourceEntityId = customers.Id,
                    TargetEntityId = orders.Id,
                    ColumnPairs = [new(customers.Columns[0].Id, orders.Columns[0].Id)],
                    Type = RelationshipType.OneToMany,
                    OnDelete = ForeignKeyReferentialAction.Cascade,
                    OnUpdate = ForeignKeyReferentialAction.SetNull,
                    ConstraintName = "FK_orders_customers",
                }
        );

    /// <summary>customers と orders の間の多対多（列ペアなし）</summary>
    private static Relationship ManyToMany(Entity customers, Entity orders) =>
        new()
        {
            SourceEntityId = customers.Id,
            TargetEntityId = orders.Id,
            Type = RelationshipType.ManyToMany,
        };

    /// <summary>
    /// customers.customer_id → orders.customer_id の 1 対多を両側のナビゲーションに書いたコード
    /// （ReverseMergePostProcessorTests.CodeRelationship。追加の名前付き引数でメタデータの指定を刻む）
    /// </summary>
    private static string CustomersOrdersCode(string extraArguments) =>
        $$"""
            using System.Collections.Generic;

            namespace Sample;

            [Table("customers")]
            public partial class CustomerEntity
            {
                [Column("customer_id")]
                [DbColumnMeta("int32")]
                public int CustomerId { get; set; }

                [NavigationReference("customers", "customer_id", "orders", "customer_id", true, true, false{{extraArguments}})]
                public ICollection<OrderEntity> Orders { get; set; }
            }

            [Table("orders")]
            public partial class OrderEntity
            {
                [Column("customer_id")]
                [DbColumnMeta("int32")]
                public int CustomerId { get; set; }

                [NavigationReference("customers", "customer_id", "orders", "customer_id", false, false, true{{extraArguments}})]
                public CustomerEntity Customer { get; set; }
            }
            """;

    /// <summary>リレーションを持たない customers（と orders）のコード</summary>
    private static string TablesCode(bool includeOrders) =>
        """
            namespace Sample;

            [Table("customers")]
            public partial class CustomerEntity
            {
                [Column("customer_id")]
                [DbColumnMeta("int32")]
                public int CustomerId { get; set; }
            }

            """
        + (
            includeOrders
                ? """
                    [Table("orders")]
                    public partial class OrderEntity
                    {
                        [Column("customer_id")]
                        [DbColumnMeta("int32")]
                        public int CustomerId { get; set; }
                    }

                    """
                : string.Empty
        );

    /// <summary>
    /// 型メタの無い customers.customer_id を端点に持つため列ペアを解決できず、列ペアなしに退化する 1 対多のコード
    /// （型メタ欠落の列を端点に持つ場合に起きる）
    /// </summary>
    private const string DegeneratedOneToManyCode = """
        using System.Collections.Generic;

        namespace Sample;

        [Table("customers")]
        public partial class CustomerEntity
        {
            [Column("customer_id")]
            public object CustomerId { get; set; }

            [NavigationReference("customers", "customer_id", "orders", "customer_id", true, true, false)]
            public ICollection<OrderEntity> Orders { get; set; }
        }

        [Table("orders")]
        public partial class OrderEntity
        {
            [Column("customer_id")]
            [DbColumnMeta("int32")]
            public int CustomerId { get; set; }

            [NavigationReference("customers", "customer_id", "orders", "customer_id", false, false, true)]
            public CustomerEntity Customer { get; set; }
        }
        """;

    /// <summary>
    /// customers と、customers を 2 本（出荷先・請求先）参照する orders の現在図（CSharpReverseUnnamedForeignKeyTests.CurrentDiagram）
    /// </summary>
    private static ErDiagram ShipBillCurrent(string? shipName = null, string? billName = null)
    {
        var customerId = new Column
        {
            Name = "customer_id",
            DataType = "int",
            IsPrimaryKey = true,
            IsNullable = false,
        };
        var customers = new Entity { TableName = "customers", Columns = { customerId } };
        var shipCustomerId = new Column
        {
            Name = "ship_customer_id",
            DataType = "int",
            IsNullable = false,
            IsForeignKey = true,
        };
        var billCustomerId = new Column
        {
            Name = "bill_customer_id",
            DataType = "int",
            IsNullable = false,
            IsForeignKey = true,
        };
        var orders = new Entity
        {
            TableName = "orders",
            Columns =
            {
                new Column
                {
                    Name = "order_id",
                    DataType = "int",
                    IsPrimaryKey = true,
                    IsNullable = false,
                },
                shipCustomerId,
                billCustomerId,
            },
        };

        return new ErDiagram
        {
            TargetDbms = DialectNames.SqlServer,
            Entities = { customers, orders },
            Relationships =
            {
                new Relationship
                {
                    SourceEntityId = customers.Id,
                    TargetEntityId = orders.Id,
                    Type = RelationshipType.OneToMany,
                    ColumnPairs = [new RelationshipColumnPair(customerId.Id, shipCustomerId.Id)],
                    ConstraintName = shipName,
                },
                new Relationship
                {
                    SourceEntityId = customers.Id,
                    TargetEntityId = orders.Id,
                    Type = RelationshipType.OneToMany,
                    ColumnPairs = [new RelationshipColumnPair(customerId.Id, billCustomerId.Id)],
                    ConstraintName = billName,
                },
            },
        };
    }
}
