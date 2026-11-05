using QuickER.Dialects;
using QuickER.Resources;
using QuickER.Tests.TestSupport.Engines;

namespace QuickER.Tests.Gui.Services.FormatRoundTrip;

/// <summary>Mermaid の取込の規則の行（手書きの Mermaid の文字列を取り込む）</summary>
internal static partial class FormatRuleRows
{
    private static IEnumerable<ScenarioRow<FormatRule>> Mermaid() =>
        [.. MermaidBasicRules(), .. MermaidConstraintNameRules(), .. MermaidOtherRules()];

    /// <summary>MermaidTests の取込の規則</summary>
    private static IEnumerable<ScenarioRow<FormatRule>> MermaidBasicRules() =>
        [
            MermaidRule(
                    "mermaid-rule-parses-entities-and-relationships",
                    "Mermaid 読込でエンティティとリレーションを復元できる"
                )
                .Text(
                    Nl(
                        "erDiagram",
                        "    Customer {",
                        "        int CustomerId PK",
                        "        nvarchar(100) CustomerName",
                        "    }",
                        "    Orders {",
                        "        int OrderId PK",
                        "        int CustomerId FK",
                        "    }",
                        "    Customer ||--o{ Orders : FK_Orders_Customer"
                    )
                )
                .Expect(e =>
                    e.Imported.Count("Structure table *", 2)
                        .Count("RelationshipType *", 1)
                        .Single("Structure table Customer")
                        .Single("PrimaryKey Customer.CustomerId")
                        .Single("Structure table Orders")
                        .Single("ForeignKeyMarker Orders.CustomerId")
                        .Single("RelationshipType Customer->Orders#0 = OneToMany")
                        .Single("ConstraintName Customer->Orders#0 = FK_Orders_Customer")
                        .Single("ColumnPairs Customer->Orders#0 = CustomerId>CustomerId")
                ),
            MermaidRule(
                    "mermaid-rule-composite-primary-key-parent-pairs-every-key-column",
                    "Mermaid 取込は親の複合 PK を列ごとにペア化する"
                )
                .Text(
                    Nl(
                        "erDiagram",
                        "    TenantRegion {",
                        "        int TenantId PK",
                        "        nvarchar(10) RegionCode PK",
                        "    }",
                        "    TenantUser {",
                        "        int TenantUserId PK",
                        "        int TenantId",
                        "        nvarchar(10) RegionCode",
                        "    }",
                        "    TenantRegion ||--o{ TenantUser : FK_TenantUser_TenantRegion"
                    )
                )
                .Expect(e =>
                    e.Imported.Count("RelationshipType *", 1)
                        .Single(
                            "ColumnPairs TenantRegion->TenantUser#0 = TenantId>TenantId,RegionCode>RegionCode"
                        )
                        .Single("ForeignKeyMarker TenantUser.TenantId")
                        .Single("ForeignKeyMarker TenantUser.RegionCode")
                ),
            .. new[]
            {
                ("Orders", "OrderId", "Revision"),
                ("Projects", "TenantId", "ProjectId"),
            }.Select(item =>
                MermaidRule(
                        $"mermaid-rule-composite-primary-key-pairs-each-column-with-same-name-{item.Item1.ToLowerInvariant()}",
                        $"Mermaid 取込の複合主キーの自動ペア化は各列を同名の子列へ付ける（{item.Item1}）"
                    )
                    .Text(
                        Nl(
                            "erDiagram",
                            $"    {item.Item1} {{",
                            $"        int {item.Item2} PK",
                            $"        int {item.Item3} PK",
                            "    }",
                            "    Child {",
                            "        int ChildId PK",
                            $"        int {item.Item2}",
                            $"        int {item.Item3}",
                            "    }",
                            $"    {item.Item1} ||--o{{ Child : FK_Child_{item.Item1}"
                        )
                    )
                    .Expect(e =>
                        e.Imported.Count("RelationshipType *", 1)
                            .Single(
                                $"ColumnPairs {item.Item1}->Child#0 = {item.Item2}>{item.Item2},{item.Item3}>{item.Item3}"
                            )
                    )
                    .Row()
            ),
            MermaidRule(
                    "mermaid-rule-parse-error-line-number",
                    "Mermaid 取込の解析エラーに行番号が付く"
                )
                .Text(
                    Nl(
                        "erDiagram",
                        "    Customer {",
                        "        int CustomerId PK",
                        "        broken",
                        "    }"
                    )
                )
                .Rejects(
                    InvalidData(
                        string.Format(
                            Strings.Import_LineDiagnostic,
                            4,
                            string.Format(Strings.Mermaid_ColumnParseError, "Customer", "broken")
                        )
                    )
                ),
            MermaidRule(
                    "mermaid-rule-missing-closing-brace-points-to-block-start",
                    "Mermaid 取込の未閉じブロックはブロック開始行を指す"
                )
                .Text(Nl("erDiagram", "", "    Customer {", "        int CustomerId PK"))
                .Rejects(
                    InvalidData(
                        string.Format(
                            Strings.Import_LineDiagnostic,
                            3,
                            string.Format(Strings.Mermaid_MissingClosingBrace, "Customer")
                        )
                    )
                ),
            MermaidRule(
                    "mermaid-rule-whole-file-diagnostic-has-no-line-number",
                    "Mermaid 取込のファイル全体の診断には行番号を付けない"
                )
                .Text("erDiagram")
                .Rejects(InvalidData(Strings.Mermaid_NoEntities)),
        ];

    /// <summary>CUSTOMER・ORDER・INVOICE の 3 テーブルと、与えたリレーション行の Mermaid（MermaidConstraintNameTests.BuildText）</summary>
    private static string MermaidCustomerOrderInvoice(params string[] relationshipLines) =>
        Lf([
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
            .. relationshipLines,
        ]);

    /// <summary>制約名の行（親と子の表名と制約名）がこの並びと一致する</summary>
    private static Func<IReadOnlyList<string>, bool> ConstraintNamesInOrder(
        params string[] lines
    ) =>
        FormatShapeRows.InOrder(
            "ConstraintName ",
            [.. lines.Select(line => "ConstraintName " + line)]
        );

    /// <summary>MermaidConstraintNameTests の取込の規則（ラベルから制約名を決める）</summary>
    private static IEnumerable<ScenarioRow<FormatRule>> MermaidConstraintNameRules() =>
        [
            MermaidRule(
                    "mermaid-rule-repeated-label-is-not-adopted",
                    "同じラベルが複数のリレーションに付いていれば、どれも制約名に採らない"
                )
                .Text(
                    MermaidCustomerOrderInvoice(
                        "    CUSTOMER ||--o{ ORDER : has",
                        "    CUSTOMER ||--o{ INVOICE : HAS",
                        "    ORDER ||--o{ INVOICE : has"
                    )
                )
                .Expect(e =>
                {
                    e.Imported.That(
                        "制約名の行が CUSTOMER->ORDER・CUSTOMER->INVOICE・ORDER->INVOICE の順にどれも名前なし",
                        ConstraintNamesInOrder(
                            "CUSTOMER->ORDER#0 = -",
                            "CUSTOMER->INVOICE#0 = -",
                            "ORDER->INVOICE#0 = -"
                        )
                    );
                    e.ImportedDiagram.Add(DistinctDdlConstraintNames());
                }),
            .. new[]
            {
                ("relates-relates", "relates", "relates"),
                ("has-has", "has", "has"),
                ("relates-quoted-bills-to", "relates", "\"bills to\""),
            }.Select(item =>
                MermaidRule(
                        $"mermaid-rule-same-pair-twice-without-names-second-gets-unique-name-{item.Item1}",
                        $"同じ親子の組に名前の無いリレーションが 2 本残れば、2 本目に衝突しない名前を付ける（{item.Item2} / {item.Item3}）"
                    )
                    .Text(
                        MermaidCustomerOrderInvoice(
                            $"    CUSTOMER ||--o{{ ORDER : {item.Item2}",
                            $"    CUSTOMER ||--o{{ ORDER : {item.Item3}"
                        )
                    )
                    .Expect(e =>
                    {
                        e.Imported.That(
                            "制約名の行が CUSTOMER->ORDER の名前なしと、2 本目の子の列名つきの名前の順",
                            ConstraintNamesInOrder(
                                "CUSTOMER->ORDER#0 = -",
                                "CUSTOMER->ORDER#1 = FK_ORDER_CUSTOMER_bill_to_customer_id"
                            )
                        );
                        e.ImportedDiagram.Add(DistinctDdlConstraintNames());
                    })
                    .Row()
            ),
            MermaidRule(
                    "mermaid-rule-label-colliding-with-table-constraint-is-not-adopted",
                    "主キー・一意制約の名前と衝突するラベルは制約名に採らない"
                )
                .Text(MermaidCustomerOrderInvoice("    CUSTOMER ||--o{ ORDER : pk_customer"))
                .Expect(e =>
                {
                    e.Imported.That(
                        "制約名の行が CUSTOMER->ORDER の名前なしだけ",
                        ConstraintNamesInOrder("CUSTOMER->ORDER#0 = -")
                    );
                    e.ImportedDiagram.Add(DistinctDdlConstraintNames());
                }),
            .. new[]
            {
                ("quoted-word", "\"places\""),
                ("quoted-phrase", "\"places order\""),
                ("bare-phrase", "places order"),
            }.Select(item =>
                MermaidRule(
                        $"mermaid-rule-phrase-label-is-not-adopted-{item.Item1}",
                        $"引用つきや複数語のラベルは動詞句として扱い、制約名に採らない（{item.Item2}）"
                    )
                    .Text(MermaidCustomerOrderInvoice($"    CUSTOMER ||--o{{ ORDER : {item.Item2}"))
                    .Expect(e =>
                        e.Imported.That(
                            "制約名の行が CUSTOMER->ORDER の名前なしだけ",
                            ConstraintNamesInOrder("CUSTOMER->ORDER#0 = -")
                        )
                    )
                    .Row()
            ),
            MermaidRule(
                    "mermaid-rule-adopted-label-takes-default-name-unnamed-gets-unique-name",
                    "採ったラベルが名前の無いリレーションの既定名と同じなら、名前の無いほうに衝突しない名前を付ける"
                )
                .Text(
                    // 名前の無いリレーションがファイルの先にあっても、採ったラベルを先に予約する
                    MermaidCustomerOrderInvoice(
                        "    CUSTOMER ||--o{ INVOICE : relates",
                        "    ORDER ||--o{ INVOICE : FK_INVOICE_CUSTOMER"
                    )
                )
                .Expect(e =>
                {
                    e.Imported.That(
                        "制約名の行が CUSTOMER->INVOICE の一意化した名前・ORDER->INVOICE の採ったラベルの順",
                        ConstraintNamesInOrder(
                            "CUSTOMER->INVOICE#0 = FK_INVOICE_CUSTOMER_customer_id",
                            "ORDER->INVOICE#0 = FK_INVOICE_CUSTOMER"
                        )
                    );
                    e.ImportedDiagram.Add(DistinctDdlConstraintNames());
                }),
            MermaidRule(
                    "mermaid-rule-uniqueness-is-counted-among-candidates-only",
                    "一意かどうかは名前の候補になるラベルだけで数える"
                )
                .Text(
                    MermaidCustomerOrderInvoice(
                        "    CUSTOMER ||--o{ ORDER : has",
                        "    CUSTOMER ||--o{ INVOICE : \"has\""
                    )
                )
                .Expect(e =>
                {
                    e.Imported.That(
                        "制約名の行が CUSTOMER->ORDER の has・CUSTOMER->INVOICE の名前なしの順",
                        ConstraintNamesInOrder("CUSTOMER->ORDER#0 = has", "CUSTOMER->INVOICE#0 = -")
                    );
                    e.ImportedDiagram.Add(DistinctDdlConstraintNames());
                }),
            MermaidRule(
                    "mermaid-rule-placeholder-label-is-not-adopted-regardless-of-case",
                    "ラベル relates は大文字小文字を問わず制約名に採らない"
                )
                .Text(MermaidCustomerOrderInvoice("    CUSTOMER ||--o{ ORDER : Relates"))
                .Expect(e =>
                    e.Imported.That(
                        "制約名の行が CUSTOMER->ORDER の名前なしだけ",
                        ConstraintNamesInOrder("CUSTOMER->ORDER#0 = -")
                    )
                ),
        ];

    /// <summary>MermaidUniqueConstraintTests・ImportLongDefaultNameTests の Mermaid の取込の規則</summary>
    private static IEnumerable<ScenarioRow<FormatRule>> MermaidOtherRules() =>
        [
            MermaidRule(
                    "mermaid-rule-uk-marker-restores-single-column-constraint",
                    "Mermaid 取込は UK 標識をその 1 列の一意制約として復元する"
                )
                .Text(
                    Nl(
                        "erDiagram",
                        "    Customer {",
                        "        int CustomerId PK",
                        "        nvarchar_100 Email UK",
                        "        int TenantId",
                        "    }"
                    )
                )
                .Expect(e =>
                    e.Imported.Count("Structure table *", 1)
                        .Count("UniqueSet *", 1)
                        .Single("UniqueSet Customer(Email)")
                        .Single("UniqueName Customer(Email) = -")
                ),
            MermaidRule(
                    "mermaid-rule-carries-target-dbms",
                    "Mermaid: 返す図の対象 DBMS は取込先の対象 DBMS（名前を付けるときの上限もこれ）"
                )
                .Text(
                    $$"""
                    erDiagram
                        {{LongParentName}} {
                            int id PK
                        }
                    """
                )
                .TargetDbms(DialectNames.PostgreSql)
                .Expect(e =>
                    e.ImportedDiagram.Add(
                        ("対象 DBMS が postgresql", d => d.TargetDbms == "postgresql")
                    )
                ),
        ];
}
