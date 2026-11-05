using System.Text.RegularExpressions;
using QuickER.Model;
using QuickER.Services;
using QuickER.Tests.TestSupport.Engines;

namespace QuickER.Tests.Gui.Services.FormatRoundTrip;

/// <summary>Mermaid の往復のテストから移した形の行</summary>
internal static partial class FormatShapeRows
{
    private static IEnumerable<ScenarioRow<FormatShape>> MermaidRows() =>
        [
            // MermaidTests
            // DbmlTests.SaveAndLoad_RoundTrip の往復の部分も同じ図・同じ表明（ファイルの入口は元のメソッドに残す）
            Shape(
                    "mermaid-save-load",
                    "Mermaid ファイルの SaveTo と Load を往復できる／DBML ファイルの SaveTo と Load を往復できる"
                )
                .Build(MermaidParentChildDiagram)
                .Expect(
                    e =>
                        e.Imported.Count("Structure table *", 2)
                            .Count("RelationshipType *", 1)
                            .Single("Structure table Parent")
                            .Single("Structure table Child")
                            .Single("ConstraintName * = FK_Child_Parent"),
                    nameof(DiagramExportFormat.Mermaid),
                    nameof(DiagramExportFormat.Dbml)
                ),
            Shape(
                    "mermaid-parenthesized-types",
                    "decimal(10,2) や nvarchar(100) は出力→読込のラウンドトリップで元の型名に復元できる"
                )
                .Build(MermaidProductDiagram)
                .Expect(
                    e =>
                    {
                        e.Output.Contains("decimal_10_2").Contains("nvarchar_100");
                        e.Imported.Single("Type Product.Price = decimal(10,2)")
                            .Single("Type Product.ProductName = nvarchar(100)");
                    },
                    nameof(DiagramExportFormat.Mermaid)
                ),
            // MermaidColumnNameTests
            Shape("mermaid-folded-column-collision-deterministic", "衝突を含む出力は決定的")
                .Build(() => MermaidOrdersDiagram("a b", "a_b", "a-b"))
                .Expect(
                    e =>
                        e.Output.That(
                            "同じ図を組み直して出力しても同じテキスト",
                            lines =>
                                lines[0]
                                == MermaidExporter.Build(MermaidOrdersDiagram("a b", "a_b", "a-b"))
                        ),
                    nameof(DiagramExportFormat.Mermaid)
                ),
            Shape("mermaid-table-name-with-space", "空白を含むテーブル名は畳まず往復する")
                .Build(MermaidTableNameWithSpaceDiagram)
                .Expect(
                    e =>
                        e.Imported.Count("Structure table *", 1)
                            .Single("Structure table Order Details"),
                    nameof(DiagramExportFormat.Mermaid)
                ),
            // MermaidConstraintNameTests
            Shape(
                    "mermaid-unnamed-relationships-relates",
                    "名前の無いリレーションは relates を経由して往復しても名前を持たず、制約名も重複しない"
                )
                .Build(MermaidUnnamedRelationshipsDiagram)
                .DistinctDdlConstraintNames()
                .Expect(
                    e =>
                    {
                        e.Output.Contains($": {MermaidExporter.UnnamedRelationshipLabel}");
                        e.Imported.Count("RelationshipType *", 2)
                            .That(
                                "制約名の行が CUSTOMER->ORDER・CUSTOMER->INVOICE の順にどちらも名前なし",
                                InOrder(
                                    "ConstraintName ",
                                    "ConstraintName CUSTOMER->ORDER#0 = -",
                                    "ConstraintName CUSTOMER->INVOICE#0 = -"
                                )
                            );
                    },
                    nameof(DiagramExportFormat.Mermaid)
                ),
            // MermaidFormatFidelityTests
            Shape(
                    "mermaid-empty-data-type",
                    "Mermaid 往復: 型が空の列は占位トークンで書き、名前と主キー印を保つ"
                )
                .Build(() =>
                {
                    var diagram = MermaidSingleTableDiagram("items", "int");
                    diagram.Entities[0].Columns[0].DataType = string.Empty;
                    return diagram;
                })
                .Expect(
                    e =>
                    {
                        e.Output.Contains("_ id PK");
                        e.Imported.Single("ColumnOrder items = id,*")
                            .Single("Type items.id")
                            .Single("PrimaryKey items.id");
                        e.OmissionsExclude.Add(ExportOmissionKind.ColumnTypeNormalized);
                    },
                    nameof(DiagramExportFormat.Mermaid)
                ),
            .. new[] { ("Order Details", "Order Lines"), ("orders (2024)", "lines") }.Select(
                names =>
                    Shape(
                            $"mermaid-quoted-entity-name-{Slug(names.Item1)}",
                            $"Mermaid 往復: 空白や括弧を含むテーブル名は引用して書き、リレーションごと戻る（{names.Item1} / {names.Item2}）"
                        )
                        .Build(() => MermaidParentChildByNameDiagram(names.Item1, names.Item2))
                        .Expect(
                            e =>
                            {
                                e.Output.Contains($"\"{names.Item1}\" {{");
                                e.Imported.That(
                                        $"表が {names.Item1}・{names.Item2} の順",
                                        InOrder(
                                            "Structure table ",
                                            $"Structure table {names.Item1}",
                                            $"Structure table {names.Item2}"
                                        )
                                    )
                                    .Count("RelationshipType *", 1)
                                    .Single($"Relationship {names.Item1}->*");
                                e.OmissionsExclude.Add(ExportOmissionKind.TableNameNormalized);
                            },
                            nameof(DiagramExportFormat.Mermaid)
                        )
                        .Row()
            ),
            // MermaidRelationshipLabelTests
            .. new[]
            {
                ("underscore", "FK_orders_customers"),
                ("hyphen-dot", "fk-orders.customer"),
                ("japanese", "外部キー_注文"),
            }.Select(item =>
                Shape(
                        $"mermaid-plain-constraint-label-{item.Item1}",
                        $"素で書ける一意の制約名は引用せずに書き、往復するので告知しない（{item.Item2}）"
                    )
                    .Build(() => MermaidLabelDiagram(item.Item2))
                    .Expect(
                        e =>
                        {
                            e.Output.That(
                                "関係行（-- を含む行）が素書きの 1 行",
                                TrimmedLinesContaining(
                                    "--",
                                    $"customers ||--o{{ orders : {item.Item2}"
                                )
                            );
                            e.Imported.Count("RelationshipType *", 1)
                                .Single($"ConstraintName * = {item.Item2}");
                            e.OmissionsExclude.Add(ExportOmissionKind.RelationshipConstraintName);
                        },
                        nameof(DiagramExportFormat.Mermaid)
                    )
                    .Row()
            ),
            Shape(
                    "mermaid-identifying-relationship",
                    "識別関係（子の主キー列を外部キーに含む）は出力して読み戻しても列ペアを保つ"
                )
                .Build(MermaidIdentifyingDiagram)
                .Expect(
                    e =>
                        e.Imported.Single("Structure table OrderLines")
                            .Count("RelationshipType *", 1)
                            .Matches(@"(?m)^ColumnPairs .*->OrderLines#0 = [^,\n]*>OrderId$", 1),
                    nameof(DiagramExportFormat.Mermaid)
                ),
            // MermaidUniqueConstraintTests
            Shape("mermaid-single-column-unique", "Mermaid は単一列の一意制約を往復で保持する")
                .Build(() =>
                {
                    var (diagram, customer) = CustomerUniqueDiagram();
                    customer.UniqueConstraints.Add(
                        new UniqueConstraint { ColumnIds = [customer.Columns[1].Id] }
                    );
                    return diagram;
                })
                .Expect(
                    e =>
                    {
                        e.Imported.Count("Structure table *", 1)
                            .Count("UniqueSet *", 1)
                            .Single("UniqueSet Customer(Email)");
                        // MermaidUniqueConstraintTests.Export_SingleColumnConstraint_WritesUkMarker（同じ図）
                        e.Output.Contains("nvarchar_100 Email UK");
                    },
                    nameof(DiagramExportFormat.Mermaid)
                )
                // DbmlUniqueConstraintTests.Export_SingleColumnUnnamedConstraint_WritesColumnSetting（同じ図）
                .Expect(
                    e =>
                        e.Output.Contains("Email nvarchar(100) [unique, not null]")
                            .Absent("Indexes {"),
                    nameof(DiagramExportFormat.Dbml)
                ),
            .. MermaidOutputRows(),
            .. MermaidOmissionRows(),
        ];

    /// <summary>Mermaid の型トークン（ATTRIBUTE_WORD）の形（MermaidFormatFidelityTests.MermaidTypeToken）</summary>
    private static Regex MermaidTypeToken => new(@"^[*A-Za-z_À-￿][A-Za-z0-9\-_\[\]\(\)\.,À-￿*]*$");

    /// <summary>出力の Orders テーブルの属性名（int で始まる行の 2 語目）の並びが期待と一致する（MermaidColumnNameTests.AttributeNames）</summary>
    private static Func<IReadOnlyList<string>, bool> MermaidAttributeNames(
        params string[] expected
    ) =>
        lines =>
            string.Join("\n", lines)
                .Replace("\r\n", "\n")
                .Split('\n')
                .Select(line => line.Trim())
                .Where(line => line.StartsWith("int ", StringComparison.Ordinal))
                .Select(line => line.Split(' ', StringSplitOptions.RemoveEmptyEntries)[1])
                .SequenceEqual(expected, StringComparer.Ordinal);

    /// <summary>出力の value 列の行がちょうど 1 つで、その型が Mermaid の型トークンの形（MermaidFormatFidelityTests）</summary>
    private static bool ValueTypeIsMermaidToken(IReadOnlyList<string> lines)
    {
        var valueLines = string.Join("\n", lines)
            .Split('\n')
            .Select(line => line.Trim())
            .Where(line => line.EndsWith(" value", StringComparison.Ordinal))
            .ToList();

        return valueLines.Count == 1 && MermaidTypeToken.IsMatch(valueLines[0].Split(' ')[0]);
    }

    /// <summary>Mermaid の出力の記法の行（フェーズ 2 / 4 で単発のテストから移した）</summary>
    private static IEnumerable<ScenarioRow<FormatShape>> MermaidOutputRows() =>
        [
            // MermaidColumnNameTests
            .. new[]
            {
                ("space", "Order Date", "Order_Date"),
                ("space-lower", "unit price", "unit_price"),
                ("hyphen", "order-date", "order_date"),
                ("japanese-space", "受注 日", "受注_日"),
            }.Select(item =>
                Shape(
                        $"mermaid-folded-column-name-{item.Item1}",
                        $"属性名に書けない文字は _ へ畳まれる（{item.Item2}）"
                    )
                    .Build(() => MermaidOrdersDiagram(item.Item2))
                    .Expect(
                        e =>
                        {
                            e.Output.That(
                                $"属性名の並びが [{item.Item3}]",
                                MermaidAttributeNames(item.Item3)
                            );

                            // MermaidColumnNameTests.DetectOmissions_ReportsNormalizedColumnName（Order Date の図）
                            if (item.Item1 == "space")
                            {
                                e.OmissionsInclude.Add(ExportOmissionKind.ColumnNameNormalized);
                            }
                        },
                        nameof(DiagramExportFormat.Mermaid)
                    )
                    .Row()
            ),
            .. new[]
            {
                ("underscore", "order_date"),
                ("pascal", "OrderDate"),
                ("japanese", "受注日"),
            }.Select(item =>
                Shape(
                        $"mermaid-writable-column-name-{item.Item1}",
                        $"そのまま書ける列名は変えない（{item.Item2}）"
                    )
                    .Build(() => MermaidOrdersDiagram(item.Item2))
                    .Expect(
                        e =>
                        {
                            e.Output.That(
                                $"属性名の並びが [{item.Item2}]",
                                MermaidAttributeNames(item.Item2)
                            );

                            // MermaidColumnNameTests.DetectOmissions_IgnoresWritableColumnNames（order_date の図）
                            if (item.Item1 == "underscore")
                            {
                                e.OmissionsExclude.Add(ExportOmissionKind.ColumnNameNormalized);
                            }
                        },
                        nameof(DiagramExportFormat.Mermaid)
                    )
                    .Row()
            ),
            // MermaidColumnNameTests.Export_FoldedNameCollision_GetsSuffix・RoundTrip_FoldedColumnName_IsReadable（同じ図）
            Shape(
                    "mermaid-folded-column-name-collision-suffix",
                    "畳んだ名前が衝突したら連番を付ける／畳んだ列名は読み戻せる（行が壊れない）"
                )
                .Build(() => MermaidOrdersDiagram("Order Date", "Order_Date"))
                .Expect(
                    e =>
                    {
                        // 畳む必要のない Order_Date が元の名前を保ち、畳んだほうに連番が付く
                        e.Output.That(
                            "属性名の並びが [Order_Date_2, Order_Date]",
                            MermaidAttributeNames("Order_Date_2", "Order_Date")
                        );
                        e.Imported.Count("Structure table *", 1)
                            .Single("ColumnOrder Orders = Order_Date_2,Order_Date");
                    },
                    nameof(DiagramExportFormat.Mermaid)
                ),
            Shape(
                    "mermaid-folded-column-name-multiple-collisions",
                    "連番は宣言順で決まり 3 つ以上でも重複しない"
                )
                .Build(() => MermaidOrdersDiagram("Order Date", "Order-Date", "Order.Date"))
                .Expect(
                    e =>
                        e.Output.That(
                            "属性名の並びが [Order_Date, Order_Date_2, Order_Date_3]",
                            MermaidAttributeNames("Order_Date", "Order_Date_2", "Order_Date_3")
                        ),
                    nameof(DiagramExportFormat.Mermaid)
                ),
            // MermaidFormatFidelityTests
            .. new[]
            {
                ("int", "int", false),
                ("varchar-50", "varchar(50)", false),
                ("decimal-10-2", "decimal(10,2)", false),
                ("integer-array", "integer[]", false),
                ("sys-geography", "sys.geography", false),
                ("nvarchar-max", "nvarchar(max)", true),
                ("double-precision", "double precision", true),
                ("decimal-10-2-spaced", "decimal(10, 2)", true),
                ("quoted-mytype", "\"MyType\"", true),
                ("enum-a-b", "enum('a','b')", true),
                ("varchar-20-array", "varchar(20)[]", true),
            }.Select(item =>
                Shape(
                        $"mermaid-data-type-token-{item.Item1}",
                        $"Mermaid 出力: 型は Mermaid の型トークンとして書け、元へ戻らない書き換えだけを告知する（{item.Item2}）"
                    )
                    .Build(() => MermaidSingleTableDiagram("items", item.Item2))
                    .Expect(
                        e =>
                        {
                            e.Output.That(
                                "value の行がちょうど 1 つで、型が Mermaid の型トークンの形",
                                ValueTypeIsMermaidToken
                            );
                            e.Imported.Single("ColumnOrder items = id,value");

                            if (item.Item3)
                            {
                                e.OmissionsInclude.Add(ExportOmissionKind.ColumnTypeNormalized);
                            }
                            else
                            {
                                e.OmissionsExclude.Add(ExportOmissionKind.ColumnTypeNormalized);
                                e.Imported.Single($"Type items.value = {item.Item2}");
                            }

                            // MermaidFormatFidelityTests の計算列・採番列の告知の、印の無いとき（int の図）
                            if (item.Item1 == "int")
                            {
                                e.OmissionsExclude.Add(ExportOmissionKind.ComputedColumn);
                                e.OmissionsExclude.Add(ExportOmissionKind.IdentityColumn);
                            }
                        },
                        nameof(DiagramExportFormat.Mermaid)
                    )
                    .Row()
            ),
            .. new[]
            {
                ("orders", "orders"),
                ("japanese", "受注"),
                ("dbo-orders", "dbo.Orders"),
                ("order-items", "order-items"),
            }.Select(item =>
                Shape(
                        $"mermaid-plain-entity-name-{item.Item1}",
                        $"Mermaid 出力: 素で書けるテーブル名は従来どおり素で書く（{item.Item2}）"
                    )
                    .Build(() => MermaidSingleTableDiagram(item.Item2, "int"))
                    .Expect(
                        e => e.Output.Contains($"    {item.Item2} {{"),
                        nameof(DiagramExportFormat.Mermaid)
                    )
                    .Row()
            ),
            Shape(
                    "mermaid-unquotable-entity-name-folded",
                    "Mermaid 出力: 引用にも書けない文字は畳んで告知し、衝突したら番号を付けて読み戻せる"
                )
                .Build(() =>
                {
                    var diagram = MermaidParentChildByNameDiagram("a\"b c", "a_b c");
                    diagram.Entities.Add(
                        MermaidSingleTableDiagram(@"100% \ done", "int").Entities[0]
                    );
                    return diagram;
                })
                .Expect(
                    e =>
                    {
                        e.Imported.That(
                                "表が a_b c_2・a_b c・100_ _ done の順",
                                InOrder(
                                    "Structure table ",
                                    "Structure table a_b c_2",
                                    "Structure table a_b c",
                                    "Structure table 100_ _ done"
                                )
                            )
                            .Count("RelationshipType *", 1);
                        e.OmissionsInclude.Add(ExportOmissionKind.TableNameNormalized);
                    },
                    nameof(DiagramExportFormat.Mermaid)
                ),
            // MermaidUniqueConstraintTests（単一列は mermaid-single-column-unique の Mermaid の期待）
            Shape(
                    "mermaid-composite-unique-and-key-priority",
                    "Mermaid 出力は複合制約を出さず、PK / FK を UK より優先する"
                )
                .Build(() =>
                {
                    var (diagram, customer) = CustomerUniqueDiagram();
                    // 複合制約（出力対象外）
                    customer.UniqueConstraints.Add(
                        new UniqueConstraint
                        {
                            ColumnIds = [customer.Columns[2].Id, customer.Columns[3].Id],
                        }
                    );
                    // PK 列・FK 列も一意制約の構成列にする（標識は PK / FK が優先される）
                    customer.Columns[1].IsForeignKey = true;
                    customer.UniqueConstraints.Add(
                        new UniqueConstraint { ColumnIds = [customer.Columns[0].Id] }
                    );
                    customer.UniqueConstraints.Add(
                        new UniqueConstraint { ColumnIds = [customer.Columns[1].Id] }
                    );
                    return diagram;
                })
                .Expect(
                    e =>
                        e.Output.Contains("int CustomerId PK")
                            .Contains("nvarchar_100 Email FK")
                            .Absent("UK"),
                    nameof(DiagramExportFormat.Mermaid)
                ),
            // MermaidRelationshipLabelTests
            Shape(
                    "mermaid-quoted-constraint-label-space",
                    "空白を含む制約名は引用して書き、出力を読み戻せる（構文が壊れない）"
                )
                .Build(() => MermaidLabelDiagram("FK orders customer"))
                .Expect(
                    e =>
                    {
                        e.Output.That(
                            "関係行（-- を含む行）が引用した 1 行",
                            TrimmedLinesContaining(
                                "--",
                                "customers ||--o{ orders : \"FK orders customer\""
                            )
                        );
                        e.Imported.Count("RelationshipType *", 1)
                            .Single("RelationshipType customers->orders#0 = OneToMany")
                            // 取込は引用されたラベルを制約名に採らない＝名前は落ちるので告知する
                            .None("ConstraintName * = FK orders customer");
                        e.OmissionsInclude.Add(ExportOmissionKind.RelationshipConstraintName);
                    },
                    nameof(DiagramExportFormat.Mermaid)
                ),
            .. new[]
            {
                ("parentheses", "FK(orders)", "\"FK(orders)\""),
                ("double-quote", "FK\"orders", "\"FK_orders\""),
                ("colon", "FK:orders", "\"FK:orders\""),
            }.Select(item =>
                Shape(
                        $"mermaid-quoted-constraint-label-{item.Item1}",
                        $"素で書けない記号を含む制約名は引用し、引用の中に書けない \" は _ へ畳む（{item.Item2}）"
                    )
                    .Build(() => MermaidLabelDiagram(item.Item2))
                    .Expect(
                        e =>
                        {
                            e.Output.That(
                                "関係行（-- を含む行）が引用した 1 行",
                                TrimmedLinesContaining(
                                    "--",
                                    $"customers ||--o{{ orders : {item.Item3}"
                                )
                            );
                            e.Imported.Count("RelationshipType *", 1);
                            e.OmissionsInclude.Add(ExportOmissionKind.RelationshipConstraintName);
                        },
                        nameof(DiagramExportFormat.Mermaid)
                    )
                    .Row()
            ),
        ];

    /// <summary>Mermaid の告知の行（フェーズ 2 / 4 で単発のテストから移した）</summary>
    private static IEnumerable<ScenarioRow<FormatShape>> MermaidOmissionRows() =>
        [
            // MermaidTests
            Shape(
                    "mermaid-omissions-unsupported-information",
                    "Mermaid の欠落は表現できない情報をすべて挙げる"
                )
                .Build(MermaidOmissionDiagram)
                .Expect(
                    e =>
                        e.OmissionsExact = [
                            ExportOmissionKind.TableDescription,
                            ExportOmissionKind.TableMemo,
                            ExportOmissionKind.ColumnDescription,
                            ExportOmissionKind.ColumnNullability,
                            ExportOmissionKind.CompositeUniqueConstraint,
                            ExportOmissionKind.UniqueConstraintName,
                            ExportOmissionKind.ForeignKeyColumnPairs,
                            ExportOmissionKind.ReferentialAction,
                            ExportOmissionKind.NamedQuery,
                        ],
                    nameof(DiagramExportFormat.Mermaid)
                ),
            Shape(
                    "mermaid-omissions-ignore-empty-content",
                    "Mermaid の欠落は中身が無ければ挙げない"
                )
                .Build(() =>
                    new ErDiagram
                    {
                        Entities =
                        {
                            new Entity
                            {
                                TableName = "Customer",
                                Columns =
                                {
                                    new Column { Name = "Code", DataType = "int" },
                                },
                            },
                        },
                    }
                )
                .Expect(e => e.OmissionsExact = [], nameof(DiagramExportFormat.Mermaid)),
            // MermaidFormatFidelityTests（計算列・採番列の告知の、印の有るとき。無いときは mermaid-data-type-token-int）
            Shape(
                    "mermaid-computed-column",
                    "Mermaid の欠落: 計算列があるときだけ計算列の指定を告知する"
                )
                .Build(() =>
                {
                    var diagram = MermaidSingleTableDiagram("items", "int");
                    diagram.Entities[0].Columns[1].IsComputed = true;
                    return diagram;
                })
                .Expect(
                    e => e.OmissionsInclude.Add(ExportOmissionKind.ComputedColumn),
                    nameof(DiagramExportFormat.Mermaid)
                ),
            Shape(
                    "mermaid-identity-column",
                    "Mermaid の欠落: 採番列があるときだけ採番列の指定を告知する"
                )
                .Build(() =>
                {
                    var diagram = MermaidSingleTableDiagram("items", "int");
                    diagram.Entities[0].Columns[1].IsIdentity = true;
                    return diagram;
                })
                .Expect(
                    e => e.OmissionsInclude.Add(ExportOmissionKind.IdentityColumn),
                    nameof(DiagramExportFormat.Mermaid)
                ),
            // MermaidFormatFidelityTests.DetectOmissions_UniqueOnForeignKeyColumn（同じ図で、value が外部キーの印を持たないときと持つとき）
            .. new[] { false, true }.Select(isForeignKey =>
                Shape(
                        isForeignKey
                            ? "mermaid-unique-on-foreign-key-column"
                            : "mermaid-unique-on-key-and-plain-columns",
                        "Mermaid の欠落: 外部キー列の単一列 UNIQUE は告知し、主キー列・通常列の UNIQUE は告知しない"
                            + (isForeignKey ? "（外部キー列）" : "（主キー列・通常列）")
                    )
                    .Build(() =>
                    {
                        var diagram = MermaidSingleTableDiagram("items", "int");
                        var entity = diagram.Entities[0];
                        var key = entity.Columns[0];
                        var value = entity.Columns[1];
                        entity.UniqueConstraints.Add(new UniqueConstraint { ColumnIds = [key.Id] });
                        entity.UniqueConstraints.Add(
                            new UniqueConstraint { ColumnIds = [value.Id] }
                        );
                        value.IsForeignKey = isForeignKey;
                        return diagram;
                    })
                    .Expect(
                        e =>
                        {
                            if (isForeignKey)
                            {
                                e.OmissionsInclude.Add(
                                    ExportOmissionKind.UniqueConstraintOnForeignKeyColumn
                                );
                                e.Output.Contains("int value FK");
                            }
                            else
                            {
                                e.OmissionsExclude.Add(
                                    ExportOmissionKind.UniqueConstraintOnForeignKeyColumn
                                );
                                e.Output.Contains("int value UK");
                            }
                        },
                        nameof(DiagramExportFormat.Mermaid)
                    )
                    .Row()
            ),
            // MermaidRelationshipLabelTests
            Shape(
                    "mermaid-unnamed-relationships-not-reported",
                    "名前の無いリレーションだけなら制約名の欠落を告知しない"
                )
                .Build(() => MermaidLabelDiagram(null, " "))
                .Expect(
                    e => e.OmissionsExclude.Add(ExportOmissionKind.RelationshipConstraintName),
                    nameof(DiagramExportFormat.Mermaid)
                ),
            .. new (string Id, string First, string? Second)[]
            {
                ("duplicate", "FK_same", "FK_same"),
                ("relates", "relates", null),
                ("primary-key-name", "PK_customers", null),
            }.Select(item =>
                Shape(
                        $"mermaid-plain-constraint-name-not-read-back-{item.Id}",
                        $"素で書けても取込で戻らない制約名（重複・relates・主キーと同名）も告知する（{item.First}）"
                    )
                    .Build(() =>
                        item.Second is null
                            ? MermaidLabelDiagram(item.First)
                            : MermaidLabelDiagram(item.First, item.Second)
                    )
                    .Expect(
                        e => e.OmissionsInclude.Add(ExportOmissionKind.RelationshipConstraintName),
                        nameof(DiagramExportFormat.Mermaid)
                    )
                    .Row()
            ),
            // MermaidPrimaryKeyOrderTests
            Shape(
                    "mermaid-primary-key-order-reordered",
                    "Mermaid: 列の並びと食い違う主キーの順序は欠落として告知する"
                )
                .Build(() => MermaidNullableKeyOrderDiagram(["order_no", "tenant_id"]))
                .Expect(
                    e => e.OmissionsExact = [ExportOmissionKind.PrimaryKeyOrder],
                    nameof(DiagramExportFormat.Mermaid)
                ),
            Shape(
                    "mermaid-primary-key-order-unstated",
                    "Mermaid: 順序を明示していない主キーは告知しない"
                )
                .Build(() => MermaidNullableKeyOrderDiagram(order: null))
                .Expect(e => e.OmissionsExact = [], nameof(DiagramExportFormat.Mermaid)),
            Shape(
                    "mermaid-primary-key-order-pinned-in-column-order",
                    "Mermaid: 列の並びどおりに明示した主キーは告知しない"
                )
                .Build(() => MermaidNullableKeyOrderDiagram(["tenant_id", "order_no"]))
                .Expect(e => e.OmissionsExact = [], nameof(DiagramExportFormat.Mermaid)),
        ];

    /// <summary>Mermaid で落ちる情報を一通り含む図（説明・メモ・複合制約・列ペア・アクション・クエリ。MermaidTests.BuildOmissionDiagram）</summary>
    private static ErDiagram MermaidOmissionDiagram()
    {
        var parent = new Entity
        {
            TableName = "Customer",
            Description = "顧客マスタ",
            Memo = "打ち合わせメモ",
            Columns =
            {
                new Column
                {
                    Name = "CustomerId",
                    DataType = "int",
                    IsPrimaryKey = true,
                    IsNullable = false,
                    Description = "顧客 ID",
                },
                new Column
                {
                    Name = "TenantId",
                    DataType = "int",
                    IsNullable = false,
                },
            },
        };
        parent.UniqueConstraints.Add(
            new UniqueConstraint
            {
                Name = "UQ_Customer_Tenant",
                ColumnIds = [parent.Columns[0].Id, parent.Columns[1].Id],
            }
        );

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
                    OnDelete = ForeignKeyReferentialAction.Cascade,
                },
            ],
            Queries =
            {
                new QueryDefinition { Name = "ById", EntityId = parent.Id },
            },
        };
    }

    /// <summary>主キー 2 列（tenant_id → order_no の宣言順・NULL 許可）のテーブル（MermaidPrimaryKeyOrderTests.Diagram）</summary>
    private static ErDiagram MermaidNullableKeyOrderDiagram(string[]? order)
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
                    IsNullable = true,
                },
                new Column
                {
                    Name = "order_no",
                    DataType = "int",
                    IsPrimaryKey = true,
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

    /// <summary>
    /// Parent(ParentId) → Child(ChildId / ParentId FK) の 1 対多（MermaidTests.SaveAndLoad_RoundTrip が MainViewModel で組む図と同じ形）
    /// </summary>
    /// <remarks>
    /// 元は AddEntityCommand（NewTable / ID int PK NOT NULL）を 2 回実行して名前を変え、列を足し、StartAddOneToMany で張って列ペアと制約名を差し替えている。
    /// </remarks>
    private static ErDiagram MermaidParentChildDiagram()
    {
        var parentId = new Column
        {
            Name = "ParentId",
            DataType = "int",
            IsPrimaryKey = true,
            IsNullable = false,
        };
        var parent = new Entity { TableName = "Parent", Columns = { parentId } };
        var childParentId = new Column
        {
            Name = "ParentId",
            DataType = "int",
            IsForeignKey = true,
            IsNullable = false,
        };
        var child = new Entity
        {
            TableName = "Child",
            Columns =
            {
                new Column
                {
                    Name = "ChildId",
                    DataType = "int",
                    IsPrimaryKey = true,
                    IsNullable = false,
                },
                childParentId,
            },
        };

        return new ErDiagram
        {
            Entities = { parent, child },
            Relationships =
            {
                new Relationship
                {
                    SourceEntityId = parent.Id,
                    TargetEntityId = child.Id,
                    Type = RelationshipType.OneToMany,
                    ColumnPairs = [new RelationshipColumnPair(parentId.Id, childParentId.Id)],
                    ConstraintName = "FK_Child_Parent",
                },
            },
        };
    }

    /// <summary>
    /// Product(Price decimal(10,2) / ProductName nvarchar(100)) の図（MermaidTests.Export_Import_NormalizesAndDenormalizesDataTypes が MainViewModel で組む図と同じ形）
    /// </summary>
    private static ErDiagram MermaidProductDiagram() =>
        new()
        {
            Entities =
            {
                new Entity
                {
                    TableName = "Product",
                    Columns =
                    {
                        new Column
                        {
                            Name = "Price",
                            DataType = "decimal(10,2)",
                            IsNullable = false,
                        },
                        new Column
                        {
                            Name = "ProductName",
                            DataType = "nvarchar(100)",
                            IsNullable = false,
                        },
                    },
                },
            },
        };

    /// <summary>指定した列名を持つ 1 テーブルの図（MermaidColumnNameTests.BuildDiagram）</summary>
    private static ErDiagram MermaidOrdersDiagram(params string[] columnNames) =>
        new()
        {
            Entities =
            {
                new Entity
                {
                    TableName = "Orders",
                    Columns = columnNames
                        .Select(name => new Column { Name = name, DataType = "int" })
                        .ToList(),
                },
            },
        };

    /// <summary>空白を含むテーブル名の 1 テーブルの図（MermaidColumnNameTests.RoundTrip_TableNameWithSpace_IsPreserved）</summary>
    private static ErDiagram MermaidTableNameWithSpaceDiagram() =>
        new()
        {
            Entities =
            {
                new Entity
                {
                    TableName = "Order Details",
                    Columns =
                    {
                        new Column { Name = "id", DataType = "int" },
                    },
                },
            },
        };

    /// <summary>
    /// CUSTOMER・ORDER・INVOICE の Mermaid を取り込み、リレーションの名前を外した図（MermaidConstraintNameTests.RoundTrip_UnnamedRelationships_StayUnnamed と同じ手順）
    /// </summary>
    private static ErDiagram MermaidUnnamedRelationshipsDiagram()
    {
        var text = string.Join(
            "\n",
            [
                "erDiagram",
                "    CUSTOMER {",
                "        int customer_id PK",
                "    }",
                "    ORDER {",
                "        int order_id PK",
                "        int customer_id FK",
                "        int bill_to_customer_id FK",
                "    }",
                "    INVOICE {",
                "        int invoice_id PK",
                "        int customer_id FK",
                "        int order_id FK",
                "    }",
                "    CUSTOMER ||--o{ ORDER : named",
                "    CUSTOMER ||--o{ INVOICE : named2",
            ]
        );
        var diagram = MermaidImporter.Parse(text, "sqlserver");

        foreach (var relationship in diagram.Relationships)
        {
            relationship.ConstraintName = null;
        }

        return diagram;
    }

    /// <summary>1 テーブル（主キー id と、指定した型の列 value）の図（MermaidFormatFidelityTests.BuildSingleTable）</summary>
    private static ErDiagram MermaidSingleTableDiagram(string tableName, string dataType) =>
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
                            Name = "id",
                            DataType = "int",
                            IsPrimaryKey = true,
                            IsNullable = false,
                        },
                        new Column { Name = "value", DataType = dataType },
                    ],
                },
            ],
        };

    /// <summary>テーブル名を指定した親子 2 テーブルと列ペアの無いリレーション 1 本の図（MermaidFormatFidelityTests.BuildParentChild）</summary>
    private static ErDiagram MermaidParentChildByNameDiagram(string parentName, string childName)
    {
        var parent = MermaidSingleTableDiagram(parentName, "int").Entities[0];
        var child = MermaidSingleTableDiagram(childName, "int").Entities[0];

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
                },
            ],
        };
    }

    /// <summary>customers(id) と orders(id, customer_id) に、与えた制約名のリレーションを張った図（MermaidRelationshipLabelTests.BuildDiagram）</summary>
    private static ErDiagram MermaidLabelDiagram(params string?[] constraintNames)
    {
        var customerId = new Column
        {
            Name = "id",
            DataType = "int",
            IsPrimaryKey = true,
            IsNullable = false,
        };
        var customers = new Entity { TableName = "customers", Columns = { customerId } };
        var orderCustomerId = new Column
        {
            Name = "customer_id",
            DataType = "int",
            IsForeignKey = true,
        };
        var orders = new Entity
        {
            TableName = "orders",
            Columns =
            {
                new Column
                {
                    Name = "id",
                    DataType = "int",
                    IsPrimaryKey = true,
                    IsNullable = false,
                },
                orderCustomerId,
            },
        };

        return new ErDiagram
        {
            Entities = [customers, orders],
            Relationships = constraintNames
                .Select(name => new Relationship
                {
                    SourceEntityId = customers.Id,
                    TargetEntityId = orders.Id,
                    Type = RelationshipType.OneToMany,
                    ConstraintName = name,
                    ColumnPairs = { new RelationshipColumnPair(customerId.Id, orderCustomerId.Id) },
                })
                .ToList(),
        };
    }

    /// <summary>識別関係（子の主キー列を外部キーに含む）の図（MermaidRelationshipLabelTests.IdentifyingRelationship_KeepsColumnPairOnRoundTrip）</summary>
    private static ErDiagram MermaidIdentifyingDiagram()
    {
        var orderId = new Column
        {
            Name = "OrderId",
            DataType = "int",
            IsPrimaryKey = true,
            IsNullable = false,
        };
        var orders = new Entity { TableName = "Orders", Columns = { orderId } };
        var lineOrderId = new Column
        {
            Name = "OrderId",
            DataType = "int",
            IsPrimaryKey = true,
            IsForeignKey = true,
            IsNullable = false,
        };
        var lines = new Entity
        {
            TableName = "OrderLines",
            Columns =
            {
                lineOrderId,
                new Column
                {
                    Name = "LineNo",
                    DataType = "int",
                    IsPrimaryKey = true,
                    IsNullable = false,
                },
            },
        };

        return new ErDiagram
        {
            Entities = [orders, lines],
            Relationships =
            [
                new Relationship
                {
                    SourceEntityId = orders.Id,
                    TargetEntityId = lines.Id,
                    Type = RelationshipType.OneToMany,
                    ColumnPairs = { new RelationshipColumnPair(orderId.Id, lineOrderId.Id) },
                },
            ],
        };
    }
}
