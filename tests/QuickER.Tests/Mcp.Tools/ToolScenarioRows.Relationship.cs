namespace QuickER.Tests.Mcp.Tools;

internal static partial class ToolScenarioRows
{
    /// <summary>add_relationship / remove_relationship の引数（列と制約名は null なら渡さない・種別は OneToMany）</summary>
    private static Dictionary<string, object> RelationshipArgs(
        string source,
        string target,
        string[]? sourceColumns = null,
        string[]? targetColumns = null,
        string? constraintName = null
    )
    {
        var args = new Dictionary<string, object>
        {
            ["source_table"] = source,
            ["target_table"] = target,
            ["relationship_type"] = "OneToMany",
        };

        if (sourceColumns is not null)
        {
            args["source_columns"] = sourceColumns;
        }

        if (targetColumns is not null)
        {
            args["target_columns"] = targetColumns;
        }

        if (constraintName is not null)
        {
            args["constraint_name"] = constraintName;
        }

        return args;
    }

    /// <summary>remove_relationship の引数（種別は渡さない）</summary>
    private static Dictionary<string, object> RemoveRelationshipArgs(
        string source,
        string target,
        string? constraintName = null
    )
    {
        var args = new Dictionary<string, object>
        {
            ["source_table"] = source,
            ["target_table"] = target,
        };

        if (constraintName is not null)
        {
            args["constraint_name"] = constraintName;
        }

        return args;
    }

    /// <summary>Customer(CustomerId PK) と Order(OrderId PK, CustomerId) の図（どれも NOT NULL）</summary>
    private static IEnumerable<(string Tool, object Args)> ParentChildTables =>
        [
            ("add_entity", new { table_name = "Customer" }),
            ("add_column", ColumnArgs("Customer", "CustomerId", "int", pk: true, nullable: false)),
            ("add_entity", new { table_name = "Order" }),
            ("add_column", ColumnArgs("Order", "OrderId", "int", pk: true, nullable: false)),
            ("add_column", ColumnArgs("Order", "CustomerId", "int", pk: false, nullable: false)),
        ];

    /// <summary>SQLite の図に <see cref="ParentChildTables"/> を作る</summary>
    private static ToolRow SqliteParentChild(ToolRow row) =>
        row.Dialect("sqlite").Base(ParentChildTables);

    /// <summary>
    /// 親 Customer(CustomerId PK) と、子 Order(OrderId PK と <paramref name="childColumn"/>)（<paramref name="nullable"/> の列）
    /// </summary>
    private static IEnumerable<(string Tool, object Args)> CustomerWithOrderColumn(
        string childColumn,
        bool nullable
    ) =>
        [
            ("add_entity", new { table_name = "Customer" }),
            ("add_column", ColumnArgs("Customer", "CustomerId", "int", pk: true, nullable: false)),
            ("add_entity", new { table_name = "Order" }),
            ("add_column", ColumnArgs("Order", "OrderId", "int", pk: true, nullable: false)),
            ("add_column", ColumnArgs("Order", childColumn, "int", pk: false, nullable: nullable)),
        ];

    /// <summary>
    /// 複合主キーの親 TenantRegion(TenantId, RegionCode) と、主キー TenantUserId と <paramref name="childColumns"/>
    /// （nvarchar(10)・NOT NULL）を持つ子 TenantUser
    /// </summary>
    private static IEnumerable<(string Tool, object Args)> TenantTables(
        params string[] childColumns
    )
    {
        yield return ("add_entity", new { table_name = "TenantRegion" });
        yield return (
            "add_column",
            ColumnArgs("TenantRegion", "TenantId", "int", pk: true, nullable: false)
        );
        yield return (
            "add_column",
            ColumnArgs("TenantRegion", "RegionCode", "nvarchar(10)", pk: true, nullable: false)
        );
        yield return ("add_entity", new { table_name = "TenantUser" });
        yield return (
            "add_column",
            ColumnArgs("TenantUser", "TenantUserId", "int", pk: true, nullable: false)
        );

        foreach (var column in childColumns)
        {
            yield return (
                "add_column",
                ColumnArgs("TenantUser", column, "nvarchar(10)", pk: false, nullable: false)
            );
        }
    }

    /// <summary>
    /// 親 Customers(CustomerId) と、役割の違う外部キー列 2 本（請求先・配送先）と一意制約の候補列 code を持つ子 Orders
    /// （すべて int・NOT NULL）を指定の方言で作る
    /// </summary>
    private static ToolRow BillingAndShipping(ToolRow row, string dialect)
    {
        row.Dialect(dialect)
            .Arrange("add_entity", new { table_name = "Customers" })
            .Arrange("add_entity", new { table_name = "Orders" });

        foreach (
            var (table, column, isKey) in new[]
            {
                ("Customers", "CustomerId", true),
                ("Orders", "OrderId", true),
                ("Orders", "BillingCustomerId", false),
                ("Orders", "ShippingCustomerId", false),
                ("Orders", "code", false),
            }
        )
        {
            row.Arrange("add_column", ColumnArgs(table, column, "int", pk: isKey, nullable: false));
        }

        return row;
    }

    /// <summary>Customers → Orders の外部キー（<paramref name="constraintName"/> は null なら渡さない）</summary>
    private static Dictionary<string, object> CustomerReference(
        string childColumn,
        string? constraintName = null
    ) =>
        RelationshipArgs(
            "Customers",
            "Orders",
            ["CustomerId"],
            [childColumn],
            constraintName: constraintName
        );

    /// <summary>
    /// add_relationship の列の解決（明示・命名規則・複合・主キーの実効順）・拒否・制約名の付け方と、remove_relationship の選び方
    /// </summary>
    private static IEnumerable<ToolRow> RelationshipRows()
    {
        // ---------------- 列の明示・推測 ----------------

        yield return Row(
                "relationship-add-explicit-columns-are-used-sqlserver",
                "add_relationship で明示指定したカラムがそのまま使用される"
            )
            .Base(CustomerWithOrderColumn("OwnerId", nullable: true))
            .Call(
                "add_relationship",
                RelationshipArgs("Customer", "Order", ["CustomerId"], ["OwnerId"]),
                e =>
                    e.Ok()
                        .Schema.Kind(
                            "rel",
                            "rel Customer->Order OneToMany * pairs=(CustomerId>OwnerId)"
                        )
            );

        yield return SqliteParentChild(
                Row(
                    "relationship-add-resolves-target-column-by-convention-sqlite",
                    "add_relationship はカラム省略時に命名規則で FK 列を解決する"
                )
            )
            .Call(
                "add_relationship",
                RelationshipArgs("Customer", "Order"),
                e =>
                    e.Ok()
                        .Schema.Kind(
                            "rel",
                            "rel Customer->Order OneToMany * pairs=(CustomerId>CustomerId)"
                        )
                        .Schema.Single("col Order.CustomerId * fk *")
            );

        yield return Row(
                "relationship-add-no-likely-column-leaves-target-unassigned-sqlserver",
                "add_relationship でカラム省略かつ推測不能な場合は参照先列が未割当となる"
            )
            .Base(CustomerWithOrderColumn("Quantity", nullable: true))
            .Call(
                "add_relationship",
                RelationshipArgs("Customer", "Order"),
                e =>
                    e.Ok()
                        .Schema.Kind("rel", "rel Customer->Order OneToMany * pairs=()")
                        .Schema.Single("col Order.Quantity * null nofk *")
            );

        // ---------------- 存在しない列・不正な列配列 ----------------

        yield return Row(
                "relationship-add-unknown-target-column-is-rejected-sqlserver",
                "add_relationship で存在しない target_columns を指定するとエラーになる"
            )
            .Base(CustomerWithOrderColumn("OwnerId", nullable: true))
            .Call(
                "add_relationship",
                RelationshipArgs("Customer", "Order", ["CustomerId"], ["NoSuchColumn"]),
                e => e.Fails().Result.Contains("NoSuchColumn").Schema.Kind("rel")
            );

        yield return SqliteParentChild(
                Row(
                    "relationship-add-unknown-target-column-is-rejected-sqlite",
                    "add_relationship は存在しない target_columns をエラーにする"
                )
            )
            .Call(
                "add_relationship",
                RelationshipArgs("Customer", "Order", ["CustomerId"], ["NoSuchColumn"]),
                e => e.Fails().Result.Contains("NoSuchColumn").Schema.Kind("rel")
            );

        yield return Row(
                "relationship-add-invalid-column-arrays-are-rejected-sqlserver",
                "add_relationship は列配列の長さ不一致と片側のみの指定をエラーにする"
            )
            .Base(TenantTables("TenantRef", "RegionRef"))
            .Call(
                "add_relationship",
                RelationshipArgs(
                    "TenantRegion",
                    "TenantUser",
                    ["TenantId", "RegionCode"],
                    ["TenantRef"]
                ),
                e => e.Fails().Schema.Kind("rel")
            )
            .Call(
                "add_relationship",
                RelationshipArgs("TenantRegion", "TenantUser", ["TenantId"]),
                e => e.Fails().Schema.Kind("rel")
            )
            .Call(
                "add_relationship",
                RelationshipArgs(
                    "TenantRegion",
                    "TenantUser",
                    ["TenantId", "TenantId"],
                    ["TenantRef", "RegionRef"]
                ),
                e => e.Fails().Schema.Kind("rel")
            );

        yield return SqliteParentChild(
                Row(
                    "relationship-add-invalid-column-arrays-are-rejected-sqlite",
                    "add_relationship は列配列の長さ不一致・片側のみの指定をエラーにする"
                )
            )
            .Call(
                "add_relationship",
                RelationshipArgs("Customer", "Order", ["CustomerId"], ["CustomerId", "OrderId"]),
                e => e.Fails().Schema.Kind("rel")
            )
            .Call(
                "add_relationship",
                RelationshipArgs("Customer", "Order", targetColumns: ["CustomerId"]),
                e => e.Fails().Schema.Kind("rel")
            );

        // ---------------- 複合外部キー ----------------

        yield return Row(
                "relationship-add-composite-columns-registers-ordered-pairs-sqlserver",
                "add_relationship は複合外部キーを宣言順の列ペアで登録する"
            )
            .Base(TenantTables("TenantRef", "RegionRef"))
            .Call(
                "add_relationship",
                RelationshipArgs(
                    "TenantRegion",
                    "TenantUser",
                    ["TenantId", "RegionCode"],
                    ["TenantRef", "RegionRef"]
                ),
                e =>
                    e.Ok()
                        .Schema.Kind(
                            "rel",
                            "rel TenantRegion->TenantUser OneToMany * pairs=(TenantId>TenantRef,RegionCode>RegionRef)"
                        )
                        .Schema.Single(
                            "col TenantUser.TenantRef * fk *",
                            "col TenantUser.RegionRef * fk *"
                        )
            );

        yield return SqliteParentChild(
                Row(
                    "relationship-add-composite-columns-registers-ordered-pairs-sqlite",
                    "add_relationship は複合外部キーを宣言順の列ペアで登録する"
                )
            )
            .Arrange(
                "add_column",
                ColumnArgs("Customer", "RegionCode", "nvarchar(10)", nullable: false)
            )
            .Arrange(
                "add_column",
                ColumnArgs("Order", "RegionCode", "nvarchar(10)", nullable: false)
            )
            .Call(
                "add_relationship",
                RelationshipArgs(
                    "Customer",
                    "Order",
                    ["CustomerId", "RegionCode"],
                    ["CustomerId", "RegionCode"]
                ),
                e =>
                    e.Ok()
                        .Schema.Kind(
                            "rel",
                            "rel Customer->Order OneToMany * pairs=(CustomerId>CustomerId,RegionCode>RegionCode)"
                        )
                        .Schema.Single("col Order.CustomerId * fk *", "col Order.RegionCode * fk *")
            );

        yield return Row(
                "relationship-add-omitted-columns-pairs-every-primary-key-column-sqlserver",
                "add_relationship は列省略時に親 PK 全列を自動ペア化する"
            )
            .Base(TenantTables("TenantId", "RegionCode"))
            .Call(
                "add_relationship",
                RelationshipArgs("TenantRegion", "TenantUser"),
                e =>
                    e.Ok()
                        .Schema.Kind(
                            "rel",
                            "rel TenantRegion->TenantUser OneToMany * pairs=(TenantId>TenantId,RegionCode>RegionCode)"
                        )
            );

        // 複合主キーの各列が、親テーブル名由来の名前の子列を取り違えずに同名の子列へ付く
        foreach (var dialect in new[] { "sqlserver", "sqlite" })
        {
            foreach (
                var (shape, parentTable, firstKey, secondKey) in new[]
                {
                    ("orders", "Orders", "OrderId", "Revision"),
                    ("projects", "Projects", "TenantId", "ProjectId"),
                }
            )
            {
                yield return Row(
                        $"relationship-add-composite-key-auto-pairs-each-column-with-same-name-{shape}-{dialect}",
                        "add_relationship の複合主キーの自動ペア化は各列を同名の子列へ付ける"
                    )
                    .Dialect(dialect)
                    .Arrange("add_entity", new { table_name = parentTable })
                    .Arrange("add_entity", new { table_name = "Child" })
                    .Arrange(
                        "add_column",
                        ColumnArgs(parentTable, firstKey, "int", pk: true, nullable: false)
                    )
                    .Arrange(
                        "add_column",
                        ColumnArgs(parentTable, secondKey, "int", pk: true, nullable: false)
                    )
                    .Arrange(
                        "add_column",
                        ColumnArgs("Child", "ChildId", "int", pk: true, nullable: false)
                    )
                    .Arrange(
                        "add_column",
                        ColumnArgs("Child", firstKey, "int", pk: false, nullable: false)
                    )
                    .Arrange(
                        "add_column",
                        ColumnArgs("Child", secondKey, "int", pk: false, nullable: false)
                    )
                    .Call(
                        "add_relationship",
                        RelationshipArgs(parentTable, "Child"),
                        e =>
                            e.Ok()
                                .Schema.Kind(
                                    "rel",
                                    $"rel {parentTable}->Child OneToMany * pairs=({firstKey}>{firstKey},{secondKey}>{secondKey})"
                                )
                    );
            }
        }

        // 親の主キーの実効順（列宣言順の逆）に従って自動ペア化する
        yield return Row(
                "relationship-add-auto-pairs-follow-primary-key-order-sqlite",
                "add_relationship の自動ペア化は親主キーの実効順に従う"
            )
            .Dialect("sqlite")
            .Base(TenantTables())
            .Arrange("add_column", ColumnArgs("TenantUser", "TenantId", "int", nullable: false))
            .Arrange(
                "add_column",
                ColumnArgs("TenantUser", "RegionCode", "nvarchar(10)", nullable: false)
            )
            .Arrange(
                "set_primary_key",
                new { table_name = "TenantRegion", columns = new[] { "RegionCode", "TenantId" } }
            )
            .Call(
                "add_relationship",
                RelationshipArgs("TenantRegion", "TenantUser"),
                e =>
                    e.Ok()
                        .Schema.Kind(
                            "rel",
                            "rel TenantRegion->TenantUser OneToMany * pairs=(RegionCode>RegionCode,TenantId>TenantId)"
                        )
            );

        // ---------------- 制約名（役割の違う外部キー・指定・衝突） ----------------

        foreach (var dialect in new[] { "sqlserver", "sqlite" })
        {
            yield return BillingAndShipping(
                    Row(
                        $"relationship-add-second-role-gets-distinct-name-and-removes-by-name-{dialect}",
                        "add_relationship は役割の違う 2 本目に衝突しない制約名を付け、名前で 1 本ずつ消せる"
                    ),
                    dialect
                )
                .Call(
                    "add_relationship",
                    CustomerReference("BillingCustomerId"),
                    e => e.Ok().Result.Contains("FK_Orders_Customers")
                )
                .Call(
                    "add_relationship",
                    CustomerReference("ShippingCustomerId"),
                    e =>
                        e.Ok()
                            .Result.Contains("FK_Orders_Customers_ShippingCustomerId")
                            .Schema.Kind(
                                "rel",
                                "rel Customers->Orders OneToMany name=FK_Orders_Customers *",
                                "rel Customers->Orders OneToMany name=FK_Orders_Customers_ShippingCustomerId *"
                            )
                )
                .Call(
                    "remove_relationship",
                    RemoveRelationshipArgs(
                        "Customers",
                        "Orders",
                        "FK_Orders_Customers_ShippingCustomerId"
                    ),
                    e =>
                        e.Ok()
                            .Schema.Kind(
                                "rel",
                                "rel Customers->Orders OneToMany name=FK_Orders_Customers *"
                            )
                )
                .Call(
                    "remove_relationship",
                    RemoveRelationshipArgs("Customers", "Orders", "FK_Orders_Customers"),
                    e => e.Ok().Schema.Kind("rel")
                );

            yield return BillingAndShipping(
                    Row(
                        $"relationship-add-explicit-constraint-name-is-trimmed-and-used-{dialect}",
                        "add_relationship は指定した constraint_name を前後の空白を除いて使う"
                    ),
                    dialect
                )
                .Call(
                    "add_relationship",
                    CustomerReference("BillingCustomerId", "  FK_Bill  "),
                    e =>
                        e.Ok()
                            .Result.Contains("FK_Bill")
                            .Schema.Kind("rel", "rel Customers->Orders OneToMany name=FK_Bill *")
                );

            yield return BillingAndShipping(
                    Row(
                        $"relationship-add-blank-constraint-name-is-treated-as-omitted-{dialect}",
                        "add_relationship は空白だけの constraint_name を省略として扱う"
                    ),
                    dialect
                )
                .Call(
                    "add_relationship",
                    CustomerReference("BillingCustomerId", "   "),
                    e =>
                        e.Ok()
                            .Schema.Kind(
                                "rel",
                                "rel Customers->Orders OneToMany name=FK_Orders_Customers *"
                            )
                );

            // 既存の外部キー・主キー・一意制約と（大文字小文字を無視して）同じ名前の指定は、図を変えずに拒否する
            foreach (
                var (shape, constraintName) in new[]
                {
                    ("foreign-key", "fk_orders_customers"),
                    ("primary-key", "PK_Customers"),
                    ("unique-constraint", "uq_orders_code"),
                }
            )
            {
                yield return BillingAndShipping(
                        Row(
                            $"relationship-add-conflicting-constraint-name-is-rejected-{shape}-{dialect}",
                            "add_relationship は既存の制約名と衝突する constraint_name を拒否する"
                        ),
                        dialect
                    )
                    .Arrange("add_relationship", CustomerReference("BillingCustomerId"))
                    .Arrange(
                        "set_unique_constraint",
                        new { table_name = "Orders", columns = new[] { "code" } }
                    )
                    .Call(
                        "add_relationship",
                        CustomerReference("ShippingCustomerId", constraintName),
                        e =>
                            e.Fails()
                                .Result.Contains(constraintName)
                                .Schema.Kind(
                                    "rel",
                                    "rel Customers->Orders OneToMany name=FK_Orders_Customers *"
                                )
                    );
            }
        }

        // ---------------- remove_relationship ----------------

        yield return Row(
                "relationship-remove-multiple-matches-requires-constraint-name-sqlserver",
                "remove_relationship は複数一致時に候補を挙げてエラーにする"
            )
            .Base(TenantTables("TenantRef", "RegionRef"))
            .Arrange(
                "add_relationship",
                RelationshipArgs("TenantRegion", "TenantUser", ["TenantId"], ["TenantRef"])
            )
            .Arrange(
                "add_relationship",
                RelationshipArgs(
                    "TenantRegion",
                    "TenantUser",
                    ["RegionCode"],
                    ["RegionRef"],
                    constraintName: "FK_TenantUser_TenantRegion_Region"
                )
            )
            .Call(
                "remove_relationship",
                RemoveRelationshipArgs("TenantRegion", "TenantUser"),
                e =>
                    e.Fails()
                        .Result.Contains("FK_TenantUser_TenantRegion_Region")
                        .Schema.Kind(
                            "rel",
                            "rel TenantRegion->TenantUser *",
                            "rel TenantRegion->TenantUser *"
                        )
            )
            .Call(
                "remove_relationship",
                RemoveRelationshipArgs(
                    "TenantRegion",
                    "TenantUser",
                    "FK_TenantUser_TenantRegion_Region"
                ),
                e =>
                    e.Ok()
                        .Schema.Kind(
                            "rel",
                            "rel TenantRegion->TenantUser OneToMany name=FK_TenantUser_TenantRegion *"
                        )
            );

        yield return SqliteParentChild(
                Row(
                    "relationship-remove-multiple-matches-requires-constraint-name-sqlite",
                    "remove_relationship は複数一致時に候補を挙げてエラーにする"
                )
            )
            .Arrange("add_column", ColumnArgs("Order", "BillingCustomerId", "int", nullable: false))
            .Arrange("add_relationship", RelationshipArgs("Customer", "Order"))
            .Arrange(
                "add_relationship",
                RelationshipArgs(
                    "Customer",
                    "Order",
                    ["CustomerId"],
                    ["BillingCustomerId"],
                    constraintName: "FK_Order_Customer_Billing"
                )
            )
            .Call(
                "remove_relationship",
                RemoveRelationshipArgs("Customer", "Order"),
                e =>
                    e.Fails()
                        .Result.Contains("FK_Order_Customer_Billing")
                        .Schema.Kind("rel", "rel Customer->Order *", "rel Customer->Order *")
            )
            .Call(
                "remove_relationship",
                RemoveRelationshipArgs("Customer", "Order", "FK_Order_Customer_Billing"),
                e =>
                    e.Ok()
                        .Schema.Kind(
                            "rel",
                            "rel Customer->Order OneToMany name=FK_Order_Customer *"
                        )
            );

        yield return SqliteParentChild(
                Row(
                    "relationship-remove-removes-relationship-between-endpoints-sqlite",
                    "remove_relationship は指定した端点間のリレーションを削除する"
                )
            )
            .Arrange("add_relationship", RelationshipArgs("Customer", "Order"))
            .Call(
                "remove_relationship",
                RemoveRelationshipArgs("Customer", "Order"),
                e => e.Ok().Schema.Kind("rel")
            );
    }
}
