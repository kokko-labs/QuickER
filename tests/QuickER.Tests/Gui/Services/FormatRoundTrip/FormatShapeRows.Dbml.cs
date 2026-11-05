using QuickER.Dialects;
using QuickER.Model;
using QuickER.Services;
using QuickER.Tests.TestSupport.Engines;

namespace QuickER.Tests.Gui.Services.FormatRoundTrip;

/// <summary>DBML の往復のテストから移した形の行</summary>
internal static partial class FormatShapeRows
{
    private static IEnumerable<ScenarioRow<FormatShape>> DbmlRows() =>
        [
            // DbmlTests
            Shape("dbml-composite-foreign-key", "DBML は複合外部キーを複合 Ref 構文で往復できる")
                .Build(DbmlCompositeDiagram)
                .Expect(
                    e =>
                    {
                        e.Output.Contains(
                            "Ref FK_TenantUser_TenantRegion: TenantRegion.(TenantId, RegionCode) < TenantUser.(TenantRef, RegionRef)"
                        );
                        e.Imported.Single("Structure table TenantRegion")
                            .Single("Structure table TenantUser")
                            .Count("RelationshipType *", 1)
                            .Single(
                                "ColumnPairs TenantRegion->TenantUser#0 = TenantId>TenantRef,RegionCode>RegionRef"
                            )
                            .Single("ForeignKeyMarker TenantUser.TenantRef")
                            .Single("ForeignKeyMarker TenantUser.RegionRef");
                    },
                    nameof(DiagramExportFormat.Dbml)
                ),
            Shape("dbml-mixed-foreign-keys", "DBML は単一列と複合外部キーの混在を往復できる")
                .Build(DbmlMixedForeignKeysDiagram)
                .Expect(
                    e =>
                    {
                        e.Output.Contains(
                            "Ref FK_TenantAudit_TenantUser: TenantUser.TenantUserId < TenantAudit.TenantUserId"
                        );
                        e.ImportedDiagram.Add(PairCountOfNamed("FK_TenantUser_TenantRegion", 2));
                        e.ImportedDiagram.Add(PairCountOfNamed("FK_TenantAudit_TenantUser", 1));
                    },
                    nameof(DiagramExportFormat.Dbml)
                ),
            Shape("dbml-table-description", "DBML 往復: テーブルの説明を Note 行で保持する")
                .Build(() =>
                {
                    var diagram = DbmlOneToManyDiagram(
                        ForeignKeyReferentialAction.NoAction,
                        ForeignKeyReferentialAction.NoAction
                    );
                    diagram.Entities[0].Description = "顧客マスタ";
                    return diagram;
                })
                .Expect(
                    e =>
                    {
                        e.Output.Contains("Note: '顧客マスタ'");
                        e.Imported.Single("TableDescription Customer = \"顧客マスタ\"");
                    },
                    nameof(DiagramExportFormat.Dbml)
                ),
            Shape("dbml-referential-actions", "DBML 往復: 参照アクションを設定ブロックで保持する")
                .Build(() =>
                    DbmlOneToManyDiagram(
                        ForeignKeyReferentialAction.Cascade,
                        ForeignKeyReferentialAction.SetNull
                    )
                )
                .Expect(
                    e =>
                    {
                        e.Output.Contains(
                            "Ref FK_Orders_Customer: Customer.CustomerId < Orders.CustomerId [delete: cascade, update: set null]"
                        );
                        e.Imported.Count("RelationshipType *", 1)
                            .Single("ReferentialAction * = del=Cascade upd=SetNull")
                            .Single("ConstraintName * = FK_Orders_Customer");
                    },
                    nameof(DiagramExportFormat.Dbml)
                ),
            // DbmlUnsupportedSyntaxTests
            Shape("dbml-description-with-url", "DBML 往復: URL を含む説明が保たれる")
                .Build(() => DbmlDescriptionDiagram("see https://example.com/docs"))
                .Expect(
                    e =>
                        e.Imported.Count("Structure table *", 1)
                            .Single("TableDescription t = \"see https://example.com/docs\""),
                    nameof(DiagramExportFormat.Dbml)
                ),
            // DbmlRelationshipEndpointTests
            Shape(
                    "dbml-many-to-many-with-columns",
                    "DBML 出力: 列のあるテーブル間の多対多は従来どおり先頭列で書き、告知しない"
                )
                .Build(() =>
                {
                    var diagram = DbmlParentChildDiagram();
                    diagram.Relationships.Add(
                        new Relationship
                        {
                            SourceEntityId = diagram.Entities[0].Id,
                            TargetEntityId = diagram.Entities[1].Id,
                            Type = RelationshipType.ManyToMany,
                            ColumnPairs = [],
                        }
                    );
                    return diagram;
                })
                .Expect(
                    e =>
                    {
                        e.Output.Contains("Ref: customers.id <> orders.id");
                        e.OmissionsExclude.Add(ExportOmissionKind.RelationshipWithoutColumns);
                        e.Imported.Count("RelationshipType *", 1)
                            .Single("RelationshipType * = ManyToMany");
                    },
                    nameof(DiagramExportFormat.Dbml)
                ),
            // DbmlQuotedIdentifierTests
            .. new[]
            {
                ("spaces", "Order Details", "Order Date"),
                ("hyphens", "order-details", "order-date"),
                ("dot-and-space", "Order.Details", "unit price"),
            }.Select(names =>
                Shape(
                        $"dbml-quoted-names-{names.Item1}",
                        $"DBML 往復: 空白や記号を含むテーブル名・列名が保たれる（{names.Item2} / {names.Item3}）"
                    )
                    .Build(() => DbmlQuotedDiagram(names.Item2, names.Item3, "date"))
                    .Expect(
                        e =>
                            e.Imported.Count("Structure table *", 1)
                                .Single($"Structure table {names.Item2}")
                                .Single($"Structure column {names.Item2}.{names.Item3}")
                                .Single($"Type {names.Item2}.{names.Item3} = date"),
                        nameof(DiagramExportFormat.Dbml)
                    )
                    .Row()
            ),
            Shape("dbml-quoted-name-double-quote", "DBML 往復: 名前に含まれる二重引用符も保たれる")
                .Build(() => DbmlQuotedDiagram(@"we""ird", @"a""b\c", "int"))
                .Expect(
                    e =>
                        e.Imported.Count("Structure table *", 1)
                            .Single(@"Structure table we""ird")
                            .Single(@"Structure column we""ird.a""b\c"),
                    nameof(DiagramExportFormat.Dbml)
                ),
            Shape("dbml-quoted-japanese-names", "DBML 往復: 日本語の名前は引用して書き、元に戻る")
                .Build(() => DbmlQuotedDiagram("受注明細", "受注日", "date"))
                .Expect(
                    e =>
                    {
                        e.Output.Contains("Table \"受注明細\" {").Contains("\"受注日\" date [");
                        e.Imported.Count("Structure table *", 1)
                            .Single("Structure table 受注明細")
                            .Single("Structure column 受注明細.受注日");
                    },
                    nameof(DiagramExportFormat.Dbml)
                ),
            .. new[]
            {
                ("plain", "FK_Orders_Customer", "Ref FK_Orders_Customer: "),
                ("space", "FK Orders Customer", "Ref \"FK Orders Customer\": "),
                ("dotted", "dbo.FK_Orders", "Ref \"dbo.FK_Orders\": "),
                ("japanese", "外部キー_受注", "Ref \"外部キー_受注\": "),
                ("escaped", @"FK ""q"" \x", @"Ref ""FK \""q\"" \\x"": "),
            }.Select(item =>
                Shape(
                        $"dbml-constraint-name-ref-{item.Item1}",
                        $"DBML 往復: 制約名は名前付き Ref の名前として、必要なら引用して保たれる（{item.Item2}）"
                    )
                    .Build(() => DbmlRelationshipDiagram(item.Item2))
                    .Expect(
                        e =>
                        {
                            e.Output.Contains(item.Item3);
                            e.Imported.Count("RelationshipType *", 1)
                                .Single($"ConstraintName * = {item.Item2}");
                        },
                        nameof(DiagramExportFormat.Dbml)
                    )
                    .Row()
            ),
            Shape(
                    "dbml-quoted-relationship-column-pairs",
                    "DBML 往復: 空白を含む名前のリレーションも列対応ごと保たれる"
                )
                .Build(DbmlQuotedRelationshipDiagram)
                .Expect(
                    e =>
                        e.Imported.Single("Structure table Order Header")
                            .Single("Structure table Order Details")
                            .Count("RelationshipType *", 1)
                            .Single("ConstraintName * = FK_OrderDetails_OrderHeader")
                            .Single(
                                "ColumnPairs Order Header->Order Details#0 = Order Id>Order Id"
                            ),
                    nameof(DiagramExportFormat.Dbml)
                ),
            Shape(
                    "dbml-quoted-composite-unique",
                    "DBML 往復: 空白を含む名前の複合一意制約も保たれる"
                )
                .Build(DbmlQuotedCompositeUniqueDiagram)
                .Expect(
                    e =>
                        e.Imported.Count("Structure table *", 1)
                            .Count("UniqueSet *", 1)
                            .Single("UniqueName Order Details(Customer Code,Order Date) = UQ Order")
                            .Single(
                                "UniqueOrder Order Details(Customer Code,Order Date) = Order Date,Customer Code"
                            ),
                    nameof(DiagramExportFormat.Dbml)
                ),
            .. new[]
            {
                ("double-precision", "double precision"),
                ("timestamp", "timestamp without time zone"),
            }.Select(item =>
                Shape(
                        $"dbml-quoted-type-with-spaces-{item.Item1}",
                        $"DBML 往復: 空白を含む型は囲まれて保たれる（{item.Item2}）"
                    )
                    .Build(() => DbmlQuotedDiagram("t", "c", item.Item2))
                    .Expect(
                        e =>
                        {
                            e.Output.Contains($"c \"{item.Item2}\" [");
                            e.Imported.Count("Structure table *", 1)
                                .Single($"Type t.c = {item.Item2}");
                        },
                        nameof(DiagramExportFormat.Dbml)
                    )
                    .Row()
            ),
            Shape(
                    "dbml-quoted-array-type",
                    "DBML 往復: 配列型の角括弧が設定ブロックと取り違えられない"
                )
                .Build(() => DbmlQuotedDiagram("t", "tags", "integer[]"))
                .Expect(
                    e =>
                    {
                        e.Output.Contains("tags \"integer[]\" [");
                        e.Imported.Count("Structure table *", 1).Single("Type t.tags = integer[]");
                    },
                    nameof(DiagramExportFormat.Dbml)
                ),
            // DbmlLiteralFidelityTests
            .. new[] { "\"MyType\"", "my\"type", "\"od;d\"(10)" }.Select(dataType =>
                Shape(
                        $"dbml-type-with-double-quote-{Slug(dataType)}",
                        $"DBML 往復: 二重引用符を含む型と、その後ろの設定が保たれる（{dataType}）"
                    )
                    .Build(() => DbmlSingleColumnDiagram(dataType, description: "note text"))
                    .Expect(
                        e =>
                            e.Imported.Single($"Type items.value = {dataType}")
                                .Single("ColumnDescription items.value = \"note text\"")
                                .Single("Nullability items.value = notnull"),
                        nameof(DiagramExportFormat.Dbml)
                    )
                    .Row()
            ),
            Shape(
                    "dbml-composite-foreign-key-brackets-in-names",
                    "DBML 往復: 閉じ括弧・角括弧を含む列名と制約名の複合外部キーが保たれる"
                )
                .Build(DbmlBracketCompositeDiagram)
                .Expect(
                    e =>
                        e.Imported.Count("RelationshipType *", 1)
                            .Single("Structure table parents")
                            .Single("Structure table children")
                            .Single("ConstraintName * = FK]1")
                            .Single("ColumnPairs parents->children#0 = a)>x),b>y"),
                    nameof(DiagramExportFormat.Dbml)
                ),
            // DbmlPrimaryKeyOrderTests
            Shape(
                    "dbml-reordered-primary-key",
                    "DBML: 列の並びと食い違う主キーの順序は、書き出し → 取り込みで戻る"
                )
                .Build(() => DbmlPrimaryKeyOrderDiagram(["order_no", "tenant_id"]))
                .Expect(
                    e =>
                    {
                        e.Output.Contains("  tenant_id int [not null]")
                            .Contains("  order_no int [not null]")
                            .Contains("    (order_no, tenant_id) [pk]");
                        e.Imported.Count("Structure table *", 1)
                            .Single("PrimaryKeyOrder order_lines = order_no,tenant_id")
                            .Single("Nullability order_lines.order_no = notnull")
                            .Single("Nullability order_lines.tenant_id = notnull")
                            .None("UniqueSet *");
                    },
                    nameof(DiagramExportFormat.Dbml)
                ),
            // DbmlConstraintNameTests
            Shape(
                    "dbml-unnamed-relationships",
                    "名前の無いリレーション 1 本は書き出して読み戻しても名前を持たず、2 本目だけが名前を得る"
                )
                .Build(() =>
                    CustomersAndOrders(("bill_customer_id", null), ("ship_customer_id", null))
                )
                .DistinctDdlConstraintNames()
                .Expect(
                    e =>
                        e.Imported.Count("RelationshipType *", 2)
                            .Single("ColumnPairs customers->orders#0 = id>bill_customer_id")
                            .Single("ConstraintName customers->orders#0 = -")
                            .Single("ColumnPairs customers->orders#1 = id>ship_customer_id")
                            .Single(
                                "ConstraintName customers->orders#1 = FK_orders_customers_ship_customer_id"
                            ),
                    nameof(DiagramExportFormat.Dbml)
                ),
            // DbmlUniqueConstraintTests
            Shape(
                    "dbml-unique-constraints",
                    "DBML は一意制約（単一列・複合・名前付き）を往復で保持する"
                )
                .Build(() =>
                {
                    var (diagram, customer) = CustomerUniqueDiagram();
                    customer.UniqueConstraints.Add(
                        new UniqueConstraint { ColumnIds = [customer.Columns[1].Id] }
                    );
                    customer.UniqueConstraints.Add(
                        new UniqueConstraint
                        {
                            Name = "UQ_Customer_Tenant",
                            ColumnIds = [customer.Columns[2].Id, customer.Columns[3].Id],
                        }
                    );
                    return diagram;
                })
                .Expect(
                    e =>
                        e.Imported.Count("Structure table *", 1)
                            .Count("UniqueSet *", 2)
                            .Single("UniqueName Customer(Email) = -")
                            .Single("UniqueName Customer(Code,TenantId) = UQ_Customer_Tenant"),
                    nameof(DiagramExportFormat.Dbml)
                ),
            // DbmlKeywordColumnNameTests
            Shape("dbml-keyword-column-names", "DBML: キーワードと同じ名前の列も往復できる")
                .Build(DbmlKeywordColumnsDiagram)
                .Expect(
                    e =>
                        e.Imported.Count("Structure table *", 1)
                            .Single("ColumnOrder Audit = AuditId,note,records,checks")
                            .Single("Type Audit.AuditId = int")
                            .Single("Type Audit.note = varchar(200)")
                            .Single("Type Audit.records = int")
                            .Single("Type Audit.checks = int")
                            .Single("Nullability Audit.AuditId = notnull")
                            .Single("Nullability Audit.note = null")
                            .Single("Nullability Audit.records = notnull")
                            .Single("Nullability Audit.checks = null"),
                    nameof(DiagramExportFormat.Dbml)
                ),
            // ExportNameInjectionTests
            Shape(
                    "dbml-backslash-constraint-names",
                    "DBML の一意制約名・外部キー制約名もバックスラッシュを往復できる"
                )
                .Build(DbmlBackslashNamesDiagram)
                .Expect(
                    e =>
                        e.Imported.Count("UniqueSet customers(*", 1)
                            .Single(@"UniqueName customers(*) = UQ_weird\")
                            .Count("RelationshipType *", 1)
                            .Single(@"ConstraintName * = FK_weird\"),
                    nameof(DiagramExportFormat.Dbml)
                ),
            // ImportLongDefaultNameTests
            Shape(
                    "dbml-long-default-name-postgresql",
                    "DBML: 2 本目の外部キーに付ける名前は、取込先の対象 DBMS（PostgreSQL の 63 バイト）に収める"
                )
                .Build(() => DbmlLongNameDiagram("postgresql"))
                .Expect(
                    e =>
                    {
                        e.ImportedDiagram.Add(
                            ("対象 DBMS が postgresql", d => d.TargetDbms == "postgresql")
                        );
                        e.ImportedDiagram.Add(
                            (
                                "既定名のままの 1 本目は名前を持たない",
                                d =>
                                    d.Relationships.Count >= 2
                                    && d.Relationships[0].ConstraintName is null
                            )
                        );
                        e.ImportedDiagram.Add(
                            (
                                "2 本目の名前が PostgreSQL の上限に収まり、DDL が付ける名前と同じ",
                                d =>
                                    d.Relationships.Count >= 2
                                    && d.Relationships[1].ConstraintName is { } second
                                    && IdentifierLengthLimits.PostgreSql.Fits(second)
                                    && second
                                        == ConstraintNames.ResolveForeignKeyNames(
                                            d.Entities,
                                            d.Relationships,
                                            IdentifierLengthLimits.PostgreSql
                                        )[d.Relationships[1]]
                            )
                        );
                    },
                    nameof(DiagramExportFormat.Dbml)
                ),
            Shape(
                    "dbml-long-default-name-sqlite",
                    "DBML: 上限の無い対象 DBMS（SQLite）では縮めない"
                )
                .Build(() => DbmlLongNameDiagram("sqlite"))
                .Expect(
                    e =>
                        e.ImportedDiagram.Add(
                            (
                                "2 本目の名前が縮めない FK_{子}_{親}_ship_id",
                                d =>
                                    d.Relationships.Count >= 2
                                    && d.Relationships[1].ConstraintName
                                        == $"FK_{LongChild}_{LongParent}_ship_id"
                            )
                        ),
                    nameof(DiagramExportFormat.Dbml)
                ),
            .. DbmlOutputRows(),
            .. DbmlOmissionRows(),
        ];

    /// <summary>DBML の出力の記法の行（フェーズ 2 / 4 で単発のテストから移した）</summary>
    private static IEnumerable<ScenarioRow<FormatShape>> DbmlOutputRows() =>
        [
            // DbmlTests.Export_BuildsDbmlText・MermaidTests.Export_BuildsErDiagramText（同じ図）
            Shape(
                    "dbml-export-table-and-ref",
                    "DBML 出力で Table と Ref を生成できる／Mermaid 出力で erDiagram 記法を生成できる"
                )
                .Build(ExportCustomerOrdersDiagram)
                .Expect(
                    e =>
                        e.Output.Contains("Table Customer {")
                            .Contains("CustomerId int [pk, not null]")
                            // 外部キーの印は列の設定でなく Ref 行の子側の列として運ぶ（DBML に値なしの ref 設定は無い）
                            .Contains("  CustomerId int [not null]")
                            .Absent("ref,")
                            .Contains(
                                "Ref FK_Orders_Customer: Customer.CustomerId < Orders.CustomerId"
                            ),
                    nameof(DiagramExportFormat.Dbml)
                )
                .Expect(
                    e =>
                        e.Output.Contains("erDiagram")
                            .Contains("Customer {")
                            .Contains("int CustomerId PK")
                            .Contains("int CustomerId FK")
                            .Contains("Customer ||--o{ Orders : FK_Orders_Customer"),
                    nameof(DiagramExportFormat.Mermaid)
                ),
            // DbmlTests.Export_DefaultReferentialActions_AreOmitted・DetectOmissions_IgnoresEmptyContent（同じ図）
            Shape(
                    "dbml-default-referential-actions-omitted",
                    "DBML 出力: 既定の参照アクションは書かない／DBML の欠落は中身が無ければ挙げない"
                )
                .Build(() =>
                    DbmlOneToManyDiagram(
                        ForeignKeyReferentialAction.NoAction,
                        ForeignKeyReferentialAction.NoAction
                    )
                )
                .Expect(
                    e =>
                    {
                        e.Output.Absent("delete:").Absent("update:");
                        // 設定が 1 つも無ければ設定ブロックごと書かない
                        e.OutputLines.Single(
                            "Ref FK_Orders_Customer: Customer.CustomerId < Orders.CustomerId"
                        );
                        e.OmissionsExact = [];
                    },
                    nameof(DiagramExportFormat.Dbml)
                ),
            // DbmlRelationshipEndpointTests
            .. new[] { RelationshipType.OneToMany, RelationshipType.OneToOne }.Select(type =>
                Shape(
                        $"dbml-pairless-relationship-omitted-{Slug(type.ToString())}",
                        $"DBML 出力: 列ペアの無い 1 対 1・1 対多は Ref 行を書かず告知し、往復で外部キーを作らない（{type}）"
                    )
                    .Build(() => DbmlParentChildWith(type, withPair: false))
                    .Expect(
                        e =>
                        {
                            e.Output.Absent("Ref");
                            e.OmissionsInclude.Add(ExportOmissionKind.RelationshipWithoutColumns);
                            e.Imported.None("RelationshipType *").None("ForeignKeyMarker *");
                        },
                        nameof(DiagramExportFormat.Dbml)
                    )
                    .Row()
            ),
            Shape(
                    "dbml-unresolvable-pair-omitted",
                    "DBML 出力: 解決できない列を含む列ペアのリレーションも Ref 行を書かず告知する"
                )
                .Build(() =>
                {
                    var diagram = DbmlParentChildWith(RelationshipType.OneToMany, withPair: true);
                    diagram.Relationships[0].ColumnPairs[0].TargetColumnId = Guid.NewGuid();
                    return diagram;
                })
                .Expect(
                    e =>
                    {
                        e.Output.Absent("Ref");
                        e.OmissionsInclude.Add(ExportOmissionKind.RelationshipWithoutColumns);
                    },
                    nameof(DiagramExportFormat.Dbml)
                ),
            .. new[] { RelationshipType.ManyToMany, RelationshipType.OneToMany }.Select(type =>
                Shape(
                        $"dbml-relationship-to-table-without-columns-{Slug(type.ToString())}",
                        $"DBML 出力: 列の無いテーブルにつながるリレーションでも例外にならず、Ref 行を書かず告知する（{type}）"
                    )
                    .Build(() =>
                    {
                        var diagram = DbmlParentChildDiagram();
                        diagram.Entities[0].Columns.Clear();
                        AddDbmlParentChildRelationship(diagram, type, withPair: false);
                        return diagram;
                    })
                    .Expect(
                        e =>
                        {
                            e.Output.Absent("Ref");
                            e.OmissionsInclude.Add(ExportOmissionKind.RelationshipWithoutColumns);
                        },
                        nameof(DiagramExportFormat.Dbml)
                    )
                    .Row()
            ),
            Shape(
                    "dbml-paired-relationship-written",
                    "DBML 出力: 列ペアのあるリレーションは書き出し、告知しない（対照）"
                )
                .Build(() => DbmlParentChildWith(RelationshipType.OneToMany, withPair: true))
                .Expect(
                    e =>
                    {
                        e.Output.Contains("Ref: customers.id < orders.customer_id");
                        e.OmissionsExclude.Add(ExportOmissionKind.RelationshipWithoutColumns);
                    },
                    nameof(DiagramExportFormat.Dbml)
                ),
            // DbmlQuotedIdentifierTests
            Shape(
                    "dbml-plain-names-not-quoted",
                    "DBML 出力: 英数字とアンダースコアだけの名前は囲まない"
                )
                .Build(() => DbmlQuotedDiagram("order_details", "order_date", "date"))
                .Expect(
                    e =>
                        e.Output.Contains("Table order_details {")
                            .Contains("order_date date [")
                            .Absent("\""),
                    nameof(DiagramExportFormat.Dbml)
                ),
            .. new[] { "varchar(255)", "decimal(10,2)" }.Select(dataType =>
                Shape(
                        $"dbml-type-with-parentheses-not-quoted-{Slug(dataType)}",
                        $"DBML 出力: 括弧つきの型は囲まない（{dataType}）"
                    )
                    .Build(() => DbmlQuotedDiagram("t", "c", dataType))
                    .Expect(
                        e => e.Output.Contains($"c {dataType} ["),
                        nameof(DiagramExportFormat.Dbml)
                    )
                    .Row()
            ),
            // DbmlLiteralFidelityTests
            Shape(
                    "dbml-description-ending-with-backslash",
                    "DBML 往復: バックスラッシュで終わる列の説明が保たれる"
                )
                .Build(() => DbmlSingleColumnDiagram("varchar(10)", description: @"C:\"))
                .Expect(
                    e => e.Imported.Single(@"ColumnDescription items.value = ""C:\"""),
                    nameof(DiagramExportFormat.Dbml)
                ),
            // DbmlPrimaryKeyOrderTests.Build_KeyInColumnOrder_IsUnchanged（順序の明示の無い図と、列の並びどおりに明示した図）
            Shape(
                    "dbml-primary-key-in-column-order-unstated",
                    "DBML: 列の並びどおりの主キーは従来どおり列ごとの pk で書く（順序の明示なし）"
                )
                .Build(() => DbmlPrimaryKeyOrderDiagram(order: null))
                .Expect(
                    e =>
                        e.Output.Contains("  tenant_id int [pk, not null]")
                            .Absent("[pk]")
                            .Absent("Indexes"),
                    nameof(DiagramExportFormat.Dbml)
                ),
            Shape(
                    "dbml-primary-key-in-column-order-pinned",
                    "DBML: 列の並びどおりの主キーは従来どおり列ごとの pk で書く（列の並びどおりに明示）"
                )
                .Build(() => DbmlPrimaryKeyOrderDiagram(["tenant_id", "order_no"]))
                .Expect(
                    e =>
                        e.Output.That(
                            "出力が順序を明示していない図の出力と同じ",
                            lines =>
                                lines[0]
                                == DbmlExporter.Build(DbmlPrimaryKeyOrderDiagram(order: null))
                        ),
                    nameof(DiagramExportFormat.Dbml)
                ),
            // DbmlUniqueConstraintTests（名前なし単一列は mermaid-single-column-unique の DBML の期待）
            Shape(
                    "dbml-composite-and-named-unique-indexes",
                    "DBML 出力は複合制約・名前付き制約を Indexes ブロックへ書く"
                )
                .Build(() =>
                {
                    var (diagram, customer) = CustomerUniqueDiagram();
                    customer.UniqueConstraints.Add(
                        new UniqueConstraint
                        {
                            ColumnIds = [customer.Columns[2].Id, customer.Columns[3].Id],
                        }
                    );
                    customer.UniqueConstraints.Add(
                        new UniqueConstraint
                        {
                            Name = "UQ_Customer_Email",
                            ColumnIds = [customer.Columns[1].Id],
                        }
                    );
                    return diagram;
                })
                .Expect(
                    e =>
                        e.Output.Contains("Indexes {")
                            .Contains("(TenantId, Code) [unique]")
                            .Contains("(Email) [unique, name: 'UQ_Customer_Email']")
                            // 名前付き単一列はカラム設定ではなく索引として出す（設定側は名前を持てないため）
                            .Absent("Email nvarchar(100) [unique"),
                    nameof(DiagramExportFormat.Dbml)
                ),
        ];

    /// <summary>DBML の告知の行（フェーズ 2 / 4 で単発のテストから移した）</summary>
    private static IEnumerable<ScenarioRow<FormatShape>> DbmlOmissionRows() =>
        [
            // DbmlTests
            Shape("dbml-omissions-memo-and-named-query", "DBML の欠落はメモと名前付きクエリだけ")
                .Build(() =>
                {
                    var diagram = DbmlOneToManyDiagram(
                        ForeignKeyReferentialAction.Cascade,
                        ForeignKeyReferentialAction.SetNull
                    );
                    var customer = diagram.Entities[0];
                    customer.Description = "顧客マスタ";
                    customer.Memo = "打ち合わせメモ";
                    customer.Columns[0].Description = "顧客 ID";
                    customer.UniqueConstraints.Add(
                        new UniqueConstraint
                        {
                            Name = "UQ_Customer",
                            ColumnIds = [customer.Columns[0].Id],
                        }
                    );
                    diagram.Queries.Add(
                        new QueryDefinition { Name = "ById", EntityId = customer.Id }
                    );
                    return diagram;
                })
                .Expect(
                    e =>
                        e.OmissionsExact = [
                            ExportOmissionKind.TableMemo,
                            ExportOmissionKind.NamedQuery,
                        ],
                    nameof(DiagramExportFormat.Dbml)
                ),
            Shape(
                    "dbml-foreign-key-marker-outside-relationships",
                    "DBML の欠落: リレーションに属さない列の外部キー印を挙げる"
                )
                .Build(() =>
                {
                    var diagram = DbmlOneToManyDiagram(
                        ForeignKeyReferentialAction.NoAction,
                        ForeignKeyReferentialAction.NoAction
                    );
                    diagram
                        .Entities[1]
                        .Columns.Add(
                            new Column
                            {
                                Name = "ShipperId",
                                DataType = "int",
                                IsForeignKey = true,
                                IsNullable = true,
                            }
                        );
                    return diagram;
                })
                .Expect(
                    e =>
                    {
                        e.OmissionsExact = [ExportOmissionKind.ForeignKeyMarkerWithoutRelationship];
                        e.Imported.Single("Structure column Orders.ShipperId")
                            .None("ForeignKeyMarker Orders.ShipperId")
                            .Single("ForeignKeyMarker Orders.CustomerId");
                    },
                    nameof(DiagramExportFormat.Dbml)
                ),
            .. new[]
            {
                ("many-to-many", RelationshipType.ManyToMany, true),
                ("pairless-one-to-many", RelationshipType.OneToMany, false),
            }.Select(item =>
                Shape(
                        $"dbml-relationship-without-child-columns-carries-no-marker-{item.Item1}",
                        $"DBML の欠落: 多対多・列ペアなしのリレーションは外部キー印を運ばない（{item.Item2}）"
                    )
                    .Build(() =>
                    {
                        var diagram = DbmlOneToManyDiagram(
                            ForeignKeyReferentialAction.NoAction,
                            ForeignKeyReferentialAction.NoAction
                        );
                        var relationship = diagram.Relationships[0];
                        relationship.Type = item.Item2;

                        if (!item.Item3)
                        {
                            relationship.ColumnPairs = [];
                        }

                        return diagram;
                    })
                    .Expect(
                        e =>
                            e.OmissionsInclude.Add(
                                ExportOmissionKind.ForeignKeyMarkerWithoutRelationship
                            ),
                        nameof(DiagramExportFormat.Dbml)
                    )
                    .Row()
            ),
            Shape(
                    "dbml-primary-key-child-column-is-carried",
                    "DBML の欠落: 子側の列なら主キー列の外部キー印も運ばれる"
                )
                .Build(() =>
                {
                    var diagram = DbmlOneToManyDiagram(
                        ForeignKeyReferentialAction.NoAction,
                        ForeignKeyReferentialAction.NoAction
                    );
                    var parent = diagram.Entities[0];
                    var orders = diagram.Entities[1];
                    var orderId = orders.Columns[0];
                    orderId.IsForeignKey = true;
                    diagram.Relationships.Add(
                        new Relationship
                        {
                            SourceEntityId = parent.Id,
                            TargetEntityId = orders.Id,
                            Type = RelationshipType.OneToOne,
                            ColumnPairs =
                            [
                                new RelationshipColumnPair(parent.Columns[0].Id, orderId.Id),
                            ],
                        }
                    );
                    return diagram;
                })
                .Expect(
                    e =>
                    {
                        e.OmissionsExact = [];
                        e.Imported.Single("ForeignKeyMarker Orders.OrderId");
                    },
                    nameof(DiagramExportFormat.Dbml)
                ),
            // DbmlLiteralFidelityTests（計算列・採番列の告知。同じ図で、印の無いときと有るとき）
            Shape(
                    "dbml-single-column-int",
                    "DBML の欠落: 計算列・採番列が無ければ計算列・採番列の指定を告知しない"
                )
                .Build(() => DbmlSingleColumnDiagram("int"))
                .Expect(
                    e =>
                    {
                        e.OmissionsExclude.Add(ExportOmissionKind.ComputedColumn);
                        e.OmissionsExclude.Add(ExportOmissionKind.IdentityColumn);
                    },
                    nameof(DiagramExportFormat.Dbml)
                ),
            Shape("dbml-computed-column", "DBML の欠落: 計算列があるときだけ計算列の指定を告知する")
                .Build(() =>
                {
                    var diagram = DbmlSingleColumnDiagram("int");
                    diagram.Entities[0].Columns[1].IsComputed = true;
                    return diagram;
                })
                .Expect(
                    e => e.OmissionsInclude.Add(ExportOmissionKind.ComputedColumn),
                    nameof(DiagramExportFormat.Dbml)
                ),
            Shape("dbml-identity-column", "DBML の欠落: 採番列があるときだけ採番列の指定を告知する")
                .Build(() =>
                {
                    var diagram = DbmlSingleColumnDiagram("int");
                    diagram.Entities[0].Columns[1].IsIdentity = true;
                    return diagram;
                })
                .Expect(
                    e => e.OmissionsInclude.Add(ExportOmissionKind.IdentityColumn),
                    nameof(DiagramExportFormat.Dbml)
                ),
        ];

    /// <summary>
    /// Customer(CustomerId PK / CustomerName) → Orders(OrderId PK / CustomerId FK) の 1 対多
    /// （DbmlTests.Export_BuildsDbmlText・MermaidTests.Export_BuildsErDiagramText が MainViewModel で組む図と同じ形）
    /// </summary>
    private static ErDiagram ExportCustomerOrdersDiagram()
    {
        var customer = new Entity
        {
            TableName = "Customer",
            Columns =
            {
                new Column
                {
                    Name = "CustomerId",
                    DataType = "int",
                    IsPrimaryKey = true,
                    IsNullable = false,
                },
                new Column
                {
                    Name = "CustomerName",
                    DataType = "nvarchar(100)",
                    IsNullable = false,
                },
            },
        };
        var order = new Entity
        {
            TableName = "Orders",
            Columns =
            {
                new Column
                {
                    Name = "OrderId",
                    DataType = "int",
                    IsPrimaryKey = true,
                    IsNullable = false,
                },
                new Column
                {
                    Name = "CustomerId",
                    DataType = "int",
                    IsForeignKey = true,
                    IsNullable = false,
                },
            },
        };

        return new ErDiagram
        {
            Entities = { customer, order },
            Relationships =
            {
                new Relationship
                {
                    SourceEntityId = customer.Id,
                    TargetEntityId = order.Id,
                    Type = RelationshipType.OneToMany,
                    ColumnPairs =
                    [
                        new RelationshipColumnPair(customer.Columns[0].Id, order.Columns[1].Id),
                    ],
                    ConstraintName = "FK_Orders_Customer",
                },
            },
        };
    }

    /// <summary>親 customers(id) と子 orders(id, customer_id) にリレーションを 1 本足した図（DbmlRelationshipEndpointTests.BuildParentChild + AddRelationship）</summary>
    private static ErDiagram DbmlParentChildWith(RelationshipType type, bool withPair)
    {
        var diagram = DbmlParentChildDiagram();
        AddDbmlParentChildRelationship(diagram, type, withPair);
        return diagram;
    }

    /// <summary>親子の間にリレーションを 1 本足す（DbmlRelationshipEndpointTests.AddRelationship）</summary>
    private static void AddDbmlParentChildRelationship(
        ErDiagram diagram,
        RelationshipType type,
        bool withPair
    )
    {
        var parent = diagram.Entities[0];
        var child = diagram.Entities[1];
        diagram.Relationships.Add(
            new Relationship
            {
                SourceEntityId = parent.Id,
                TargetEntityId = child.Id,
                Type = type,
                ColumnPairs = withPair
                    ? [new RelationshipColumnPair(parent.Columns[0].Id, child.Columns[^1].Id)]
                    : [],
            }
        );
    }

    /// <summary>複合 PK (TenantId, RegionCode) の親と、複合外部キーを持つ子からなる図（DbmlTests.BuildCompositeDiagram）</summary>
    private static ErDiagram DbmlCompositeDiagram()
    {
        var parent = new Entity
        {
            TableName = "TenantRegion",
            Columns =
            {
                new Column
                {
                    Name = "TenantId",
                    DataType = "int",
                    IsPrimaryKey = true,
                    IsNullable = false,
                },
                new Column
                {
                    Name = "RegionCode",
                    DataType = "nvarchar(10)",
                    IsPrimaryKey = true,
                    IsNullable = false,
                },
            },
        };
        var child = new Entity
        {
            TableName = "TenantUser",
            Columns =
            {
                new Column
                {
                    Name = "TenantUserId",
                    DataType = "int",
                    IsPrimaryKey = true,
                    IsNullable = false,
                },
                new Column
                {
                    Name = "TenantRef",
                    DataType = "int",
                    IsNullable = false,
                },
                new Column
                {
                    Name = "RegionRef",
                    DataType = "nvarchar(10)",
                    IsNullable = false,
                },
            },
        };

        return new ErDiagram
        {
            Entities = [parent, child],
            Relationships =
            [
                new Relationship
                {
                    SourceEntityId = parent.Id,
                    TargetEntityId = child.Id,
                    Type = RelationshipType.OneToMany,
                    ColumnPairs =
                    [
                        new RelationshipColumnPair(parent.Columns[0].Id, child.Columns[1].Id),
                        new RelationshipColumnPair(parent.Columns[1].Id, child.Columns[2].Id),
                    ],
                    ConstraintName = "FK_TenantUser_TenantRegion",
                },
            ],
        };
    }

    /// <summary>複合外部キーの図に、単一列の外部キー（TenantUser → TenantAudit）を 1 本足した図（DbmlTests.MixedForeignKeys_RoundTrip）</summary>
    private static ErDiagram DbmlMixedForeignKeysDiagram()
    {
        var diagram = DbmlCompositeDiagram();
        var child = diagram.Entities.Single(entity => entity.TableName == "TenantUser");
        var audit = new Entity
        {
            TableName = "TenantAudit",
            Columns =
            {
                new Column
                {
                    Name = "TenantAuditId",
                    DataType = "int",
                    IsPrimaryKey = true,
                    IsNullable = false,
                },
                new Column
                {
                    Name = "TenantUserId",
                    DataType = "int",
                    IsNullable = false,
                },
            },
        };
        diagram.Entities.Add(audit);
        diagram.Relationships.Add(
            new Relationship
            {
                SourceEntityId = child.Id,
                TargetEntityId = audit.Id,
                Type = RelationshipType.OneToMany,
                ColumnPairs =
                [
                    new RelationshipColumnPair(
                        child.Columns.Single(column => column.Name == "TenantUserId").Id,
                        audit.Columns.Single(column => column.Name == "TenantUserId").Id
                    ),
                ],
                ConstraintName = "FK_TenantAudit_TenantUser",
            }
        );

        return diagram;
    }

    /// <summary>親 Customer(CustomerId PK) → 子 Orders(OrderId PK / CustomerId FK) の 1 対多図（DbmlTests.BuildOneToManyDiagram）</summary>
    private static ErDiagram DbmlOneToManyDiagram(
        ForeignKeyReferentialAction onDelete,
        ForeignKeyReferentialAction onUpdate
    )
    {
        var parent = new Entity
        {
            TableName = "Customer",
            Columns =
            {
                new Column
                {
                    Name = "CustomerId",
                    DataType = "int",
                    IsPrimaryKey = true,
                    IsNullable = false,
                },
            },
        };
        var child = new Entity
        {
            TableName = "Orders",
            Columns =
            {
                new Column
                {
                    Name = "OrderId",
                    DataType = "int",
                    IsPrimaryKey = true,
                    IsNullable = false,
                },
                new Column
                {
                    Name = "CustomerId",
                    DataType = "int",
                    IsForeignKey = true,
                    IsNullable = false,
                },
            },
        };

        return new ErDiagram
        {
            Entities = [parent, child],
            Relationships =
            [
                new Relationship
                {
                    SourceEntityId = parent.Id,
                    TargetEntityId = child.Id,
                    Type = RelationshipType.OneToMany,
                    ColumnPairs =
                    [
                        new RelationshipColumnPair(parent.Columns[0].Id, child.Columns[1].Id),
                    ],
                    ConstraintName = "FK_Orders_Customer",
                    OnDelete = onDelete,
                    OnUpdate = onUpdate,
                },
            ],
        };
    }

    /// <summary>説明を持つ 1 テーブルの図（DbmlUnsupportedSyntaxTests.RoundTrip_DescriptionWithUrl_IsPreserved）</summary>
    private static ErDiagram DbmlDescriptionDiagram(string description) =>
        new()
        {
            Entities =
            [
                new Entity
                {
                    TableName = "t",
                    Description = description,
                    Columns =
                    [
                        new Column
                        {
                            Name = "id",
                            DataType = "int",
                            IsPrimaryKey = true,
                            IsNullable = false,
                        },
                    ],
                },
            ],
        };

    /// <summary>親 customers(id) と子 orders(id, customer_id) の図（DbmlRelationshipEndpointTests.BuildParentChild）</summary>
    private static ErDiagram DbmlParentChildDiagram()
    {
        var parent = new Entity
        {
            TableName = "customers",
            Columns =
            [
                new Column
                {
                    Name = "id",
                    DataType = "int",
                    IsPrimaryKey = true,
                    IsNullable = false,
                },
            ],
        };
        var child = new Entity
        {
            TableName = "orders",
            Columns =
            [
                new Column
                {
                    Name = "id",
                    DataType = "int",
                    IsPrimaryKey = true,
                    IsNullable = false,
                },
                new Column { Name = "customer_id", DataType = "int" },
            ],
        };

        return new ErDiagram { Entities = [parent, child] };
    }

    /// <summary>1 テーブル 2 列の最小の図（DbmlQuotedIdentifierTests.BuildDiagram）</summary>
    private static ErDiagram DbmlQuotedDiagram(
        string tableName,
        string columnName,
        string dataType,
        string keyColumnName = "id"
    ) =>
        new()
        {
            Entities =
            [
                new Entity
                {
                    TableName = tableName,
                    Columns =
                    [
                        new Column
                        {
                            Name = keyColumnName,
                            DataType = "int",
                            IsPrimaryKey = true,
                            IsNullable = false,
                        },
                        new Column
                        {
                            Name = columnName,
                            DataType = dataType,
                            IsNullable = true,
                        },
                    ],
                },
            ],
        };

    /// <summary>親 p(id) → 子 c(pid) の 1 対多を、指定の制約名で組み立てる（DbmlQuotedIdentifierTests.BuildRelationshipDiagram）</summary>
    private static ErDiagram DbmlRelationshipDiagram(string constraintName)
    {
        var parentKey = new Column
        {
            Name = "id",
            DataType = "int",
            IsPrimaryKey = true,
            IsNullable = false,
        };
        var childKey = new Column
        {
            Name = "pid",
            DataType = "int",
            IsForeignKey = true,
            IsNullable = false,
        };
        var parent = new Entity { TableName = "p", Columns = [parentKey] };
        var child = new Entity { TableName = "c", Columns = [childKey] };

        return new ErDiagram
        {
            Entities = [parent, child],
            Relationships =
            [
                new Relationship
                {
                    SourceEntityId = parent.Id,
                    TargetEntityId = child.Id,
                    Type = RelationshipType.OneToMany,
                    ColumnPairs = [new(parentKey.Id, childKey.Id)],
                    ConstraintName = constraintName,
                },
            ],
        };
    }

    /// <summary>空白を含む名前の親子とリレーション（DbmlQuotedIdentifierTests.RoundTrip_RelationshipWithQuotedNames_KeepsColumnPairs）</summary>
    private static ErDiagram DbmlQuotedRelationshipDiagram()
    {
        var parentKey = new Column
        {
            Name = "Order Id",
            DataType = "int",
            IsPrimaryKey = true,
            IsNullable = false,
        };
        var parent = new Entity { TableName = "Order Header", Columns = [parentKey] };
        var childKey = new Column
        {
            Name = "Line Id",
            DataType = "int",
            IsPrimaryKey = true,
            IsNullable = false,
        };
        var childRef = new Column
        {
            Name = "Order Id",
            DataType = "int",
            IsForeignKey = true,
            IsNullable = false,
        };
        var child = new Entity { TableName = "Order Details", Columns = [childKey, childRef] };

        return new ErDiagram
        {
            Entities = [parent, child],
            Relationships =
            [
                new Relationship
                {
                    SourceEntityId = parent.Id,
                    TargetEntityId = child.Id,
                    Type = RelationshipType.OneToMany,
                    ColumnPairs = [new(parentKey.Id, childRef.Id)],
                    ConstraintName = "FK_OrderDetails_OrderHeader",
                },
            ],
        };
    }

    /// <summary>空白を含む名前の複合一意制約を持つ図（DbmlQuotedIdentifierTests.RoundTrip_CompositeUniqueConstraintWithQuotedNames_IsPreserved）</summary>
    private static ErDiagram DbmlQuotedCompositeUniqueDiagram()
    {
        var first = new Column
        {
            Name = "Order Date",
            DataType = "date",
            IsNullable = false,
        };
        var second = new Column
        {
            Name = "Customer Code",
            DataType = "nvarchar(10)",
            IsNullable = false,
        };
        var entity = new Entity
        {
            TableName = "Order Details",
            Columns = [first, second],
            UniqueConstraints =
            {
                new UniqueConstraint { Name = "UQ Order", ColumnIds = [first.Id, second.Id] },
            },
        };

        return new ErDiagram { Entities = [entity] };
    }

    /// <summary>1 テーブル（主キー id と、型・説明・NULL 許可を指定した列 value）の図（DbmlLiteralFidelityTests.BuildSingleColumnDiagram）</summary>
    private static ErDiagram DbmlSingleColumnDiagram(
        string dataType,
        string? description = null,
        bool isNullable = false
    ) =>
        new()
        {
            Entities =
            [
                new Entity
                {
                    TableName = "items",
                    Columns =
                    [
                        new Column
                        {
                            Name = "id",
                            DataType = "int",
                            IsPrimaryKey = true,
                            IsNullable = false,
                        },
                        new Column
                        {
                            Name = "value",
                            DataType = dataType,
                            Description = description ?? string.Empty,
                            IsNullable = isNullable,
                        },
                    ],
                    UniqueConstraints = [],
                },
            ],
        };

    /// <summary>閉じ括弧・角括弧を含む列名と制約名の複合外部キーの図（DbmlLiteralFidelityTests.RoundTrip_CompositeForeignKeyWithBracketsInNames_IsPreserved）</summary>
    private static ErDiagram DbmlBracketCompositeDiagram()
    {
        var parent = new Entity
        {
            TableName = "parents",
            Columns =
            [
                new Column
                {
                    Name = "a)",
                    DataType = "int",
                    IsPrimaryKey = true,
                    IsNullable = false,
                },
                new Column
                {
                    Name = "b",
                    DataType = "int",
                    IsPrimaryKey = true,
                    IsNullable = false,
                },
            ],
        };
        var child = new Entity
        {
            TableName = "children",
            Columns =
            [
                new Column { Name = "x)", DataType = "int" },
                new Column { Name = "y", DataType = "int" },
            ],
        };

        return new ErDiagram
        {
            Entities = [parent, child],
            Relationships =
            [
                new Relationship
                {
                    SourceEntityId = parent.Id,
                    TargetEntityId = child.Id,
                    Type = RelationshipType.OneToMany,
                    ConstraintName = "FK]1",
                    ColumnPairs =
                    [
                        new RelationshipColumnPair(parent.Columns[0].Id, child.Columns[0].Id),
                        new RelationshipColumnPair(parent.Columns[1].Id, child.Columns[1].Id),
                    ],
                },
            ],
        };
    }

    /// <summary>主キー 2 列（tenant_id → order_no の宣言順）＋通常列のテーブル（DbmlPrimaryKeyOrderTests.Diagram）</summary>
    private static ErDiagram DbmlPrimaryKeyOrderDiagram(string[]? order)
    {
        var entity = new Entity
        {
            TableName = "order_lines",
            Columns =
            {
                new Column
                {
                    Name = "tenant_id",
                    DataType = "int",
                    IsPrimaryKey = true,
                    IsNullable = false,
                },
                new Column
                {
                    Name = "order_no",
                    DataType = "int",
                    IsPrimaryKey = true,
                    IsNullable = false,
                },
                new Column
                {
                    Name = "memo",
                    DataType = "varchar(50)",
                    IsNullable = true,
                },
            },
        };

        if (order is not null)
        {
            entity.PrimaryKeyColumnIds = order
                .Select(name => entity.Columns.Single(column => column.Name == name).Id)
                .ToList();
        }

        return new ErDiagram { Entities = { entity } };
    }

    /// <summary>DBML のキーワードと同じ名前の列を並べた図（DbmlKeywordColumnNameTests.BuildDiagram）</summary>
    private static ErDiagram DbmlKeywordColumnsDiagram() =>
        new()
        {
            Entities =
            {
                new Entity
                {
                    TableName = "Audit",
                    Columns =
                    {
                        new Column
                        {
                            Name = "AuditId",
                            DataType = "int",
                            IsPrimaryKey = true,
                            IsNullable = false,
                        },
                        new Column
                        {
                            Name = "note",
                            DataType = "varchar(200)",
                            IsNullable = true,
                        },
                        new Column
                        {
                            Name = "records",
                            DataType = "int",
                            IsNullable = false,
                        },
                        new Column
                        {
                            Name = "checks",
                            DataType = "int",
                            IsNullable = true,
                        },
                    },
                },
            },
        };

    /// <summary>バックスラッシュで終わる一意制約名・外部キー制約名の図（ExportNameInjectionTests.Dbml_BackslashInConstraintNames_RoundTrips）</summary>
    private static ErDiagram DbmlBackslashNamesDiagram()
    {
        var customer = new Entity
        {
            TableName = "customers",
            Columns =
            [
                new Column
                {
                    Name = "customer_id",
                    DataType = "int",
                    IsPrimaryKey = true,
                    IsNullable = false,
                },
                new Column
                {
                    Name = "code",
                    DataType = "nvarchar(20)",
                    IsNullable = false,
                },
            ],
        };
        var order = new Entity
        {
            TableName = "orders",
            Columns =
            [
                new Column
                {
                    Name = "order_id",
                    DataType = "int",
                    IsPrimaryKey = true,
                    IsNullable = false,
                },
                new Column
                {
                    Name = "customer_id",
                    DataType = "int",
                    IsForeignKey = true,
                    IsNullable = false,
                },
            ],
        };
        customer.UniqueConstraints.Add(
            new UniqueConstraint { Name = @"UQ_weird\", ColumnIds = [customer.Columns[1].Id] }
        );

        return new ErDiagram
        {
            Entities = [customer, order],
            Relationships =
            [
                new Relationship
                {
                    SourceEntityId = customer.Id,
                    TargetEntityId = order.Id,
                    Type = RelationshipType.OneToMany,
                    ConstraintName = @"FK_weird\",
                    ColumnPairs = [new(customer.Columns[0].Id, order.Columns[1].Id)],
                },
            ],
        };
    }

    /// <summary>60 文字の親テーブル名（ImportLongDefaultNameTests）</summary>
    private static string LongParent => new('p', 60);

    /// <summary>60 文字の子テーブル名（ImportLongDefaultNameTests）</summary>
    private static string LongChild => new('c', 60);

    /// <summary>
    /// 長い名前の親子に名前の無い外部キーを 2 本張った図（ImportLongDefaultNameTests の DBML を同じ対象 DBMS で取り込み、
    /// 取込が付けた名前を外したもの＝往復で取込が名前を付け直す）
    /// </summary>
    private static ErDiagram DbmlLongNameDiagram(string targetDbms)
    {
        var text = $$"""
            Table {{LongParent}} {
              id int [pk]
            }

            Table {{LongChild}} {
              id int [pk]
              bill_id int
              ship_id int
            }

            Ref: {{LongChild}}.bill_id > {{LongParent}}.id
            Ref: {{LongChild}}.ship_id > {{LongParent}}.id
            """;
        var diagram = DbmlImporter.Parse(text, targetDbms);

        foreach (var relationship in diagram.Relationships)
        {
            relationship.ConstraintName = null;
        }

        return diagram;
    }
}
