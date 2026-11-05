using QuickER.Model;
using QuickER.Tests.TestSupport.Engines;

namespace QuickER.Tests.Gui.Services.FormatRoundTrip;

/// <summary>Id の引き継ぎだけ（DiagramMergeReconciler.Reconcile）のマージの行（DiagramMergeReconcilerTests から移した）</summary>
internal static partial class FormatMergeRows
{
    private static IEnumerable<ScenarioRow<FormatMerge>> ReconcileRows() =>
        [
            .. ReconcileIdRows(),
            .. ReconcileMemoAndDescriptionRows(),
            .. ReconcileQueryRows(),
            .. ReconcileCaseRows(),
        ];

    /// <summary>Id の引き継ぎ（一意制約・主キーの順序・リレーションの参照の追従、曖昧な名前）</summary>
    private static IEnumerable<ScenarioRow<FormatMerge>> ReconcileIdRows() =>
        [
            Merge(
                    FormatMergePath.Reconcile,
                    "merge-reconcile-name-match-adopts-current-ids",
                    "名前一致でエンティティ・列の Id が現在図の Guid へ引き継がれる"
                )
                .Current(() => new ErDiagram { Entities = { Ent("Customer", Col("Id")) } })
                .Imported(() => new ErDiagram { Entities = { Ent("Customer", Col("Id")) } })
                .PreserveMemo()
                .Expect(e =>
                    e.Merged.Count("Structure table *", 1)
                        .Single("Id table Customer = current:Customer")
                        .Count("Structure column *", 1)
                        .Single("Id column Customer.Id = current:Customer.Id")
                ),
            Merge(
                    FormatMergePath.Reconcile,
                    "merge-reconcile-unique-constraint-follows-column-ids",
                    "一意制約の構成列 Guid が列 Id の書換えに追従する"
                )
                .Current(() =>
                    new ErDiagram { Entities = { Ent("Customer", Col("Id"), Col("Code")) } }
                )
                .Imported(() =>
                {
                    // 取込結果は同名・同名列だが Id は新規。制約は取込結果の列 Id を参照している
                    var entity = Ent("Customer", Col("Id"), Col("Code"));
                    entity.UniqueConstraints.Add(
                        new UniqueConstraint
                        {
                            Name = "UQ_Customer_Code",
                            ColumnIds = { entity.Columns[1].Id },
                        }
                    );
                    return new ErDiagram { Entities = { entity } };
                })
                .PreserveMemo()
                .Expect(e =>
                    // 構成列が現在図の列 Guid（＝マージ後の列 Code）を指すときだけ、写しの構成列が名前で引ける
                    e.Merged.Count("UniqueSet *", 1)
                        .Single("UniqueSet Customer(Code)")
                        .Single("Id column Customer.Code = current:Customer.Code")
                ),
            Merge(
                    FormatMergePath.Reconcile,
                    "merge-reconcile-rewrites-relationship-references",
                    "リレーションの参照 Id（両端エンティティ・両端列）が追従書き換えされる"
                )
                .Current(() =>
                    new ErDiagram
                    {
                        Entities = { Ent("Parent", Col("Id")), Ent("Child", Col("ParentId")) },
                    }
                )
                .Imported(() =>
                {
                    var parent = Ent("Parent", Col("Id"));
                    var child = Ent("Child", Col("ParentId"));

                    return new ErDiagram
                    {
                        Entities = { parent, child },
                        Relationships =
                        {
                            new Relationship
                            {
                                SourceEntityId = parent.Id,
                                TargetEntityId = child.Id,
                                ColumnPairs = [new(parent.Columns[0].Id, child.Columns[0].Id)],
                            },
                        },
                    };
                })
                .PreserveMemo()
                .Expect(e =>
                    // 端点・列ペアの鍵はマージ後の図の Id で引く（追従しなければ ? になる）
                    e.Merged.Count("Relationship *", 1)
                        .Single("Relationship Parent->Child#0")
                        .Single("ColumnPairs Parent->Child#0 = Id>ParentId")
                        .Single("Id table Parent = current:Parent")
                        .Single("Id table Child = current:Child")
                        .Single("Id column Parent.Id = current:Parent.Id")
                        .Single("Id column Child.ParentId = current:Child.ParentId")
                ),
            Merge(
                    FormatMergePath.Reconcile,
                    "merge-reconcile-ambiguous-table-name-not-matched",
                    "同名テーブルが複数あるとその名前はマッチ対象外になる"
                )
                .Current(() =>
                    new ErDiagram
                    {
                        Entities = { Ent("Log", Col("Id")), Ent("Log", Col("Message")) },
                    }
                )
                .Imported(() => new ErDiagram { Entities = { Ent("Log", Col("Id")) } })
                .PreserveMemo()
                .Expect(e => e.Merged.Single("Id table Log = imported")),
            Merge(
                    FormatMergePath.Reconcile,
                    "merge-reconcile-ambiguous-column-name-not-matched",
                    "同名列が複数あるとその列名はマッチ対象外になる"
                )
                .Current(() =>
                    new ErDiagram { Entities = { Ent("Customer", Col("Value"), Col("Value")) } }
                )
                .Imported(() => new ErDiagram { Entities = { Ent("Customer", Col("Value")) } })
                .PreserveMemo()
                .Expect(e =>
                    // エンティティ自体は一致するが、曖昧な列名は引き継がれない
                    e.Merged.Single("Id table Customer = current:Customer")
                        .Single("Id column Customer.Value = imported")
                ),
            Merge(
                    FormatMergePath.Reconcile,
                    "merge-reconcile-primary-key-order-follows-column-ids",
                    "主キーの順序 Guid が列 Id の書換えに追従する"
                )
                .Current(() => new ErDiagram { Entities = { Ent("Pair", Col("a"), Col("b")) } })
                .Imported(() =>
                {
                    // 取込結果は同名・同名列だが Id は新規。順序は列宣言順（a → b）とは逆
                    var a = Col("a");
                    var b = Col("b");
                    a.IsPrimaryKey = true;
                    b.IsPrimaryKey = true;
                    var entity = Ent("Pair", a, b);
                    entity.PrimaryKeyColumnIds = [b.Id, a.Id];
                    return new ErDiagram { Entities = { entity } };
                })
                .PreserveMemo()
                .Expect(e =>
                {
                    e.Merged.Single("PrimaryKeyOrder Pair = b,a");
                    e.That.Add(
                        (
                            "主キーの順序が現在図の列 Guid（b, a）を指す",
                            (current, outcome) =>
                                outcome
                                    .Merged.Entities[0]
                                    .PrimaryKeyColumnIds.SequenceEqual([
                                        current.Entities[0].Columns[1].Id,
                                        current.Entities[0].Columns[0].Id,
                                    ])
                        )
                    );
                }),
        ];

    /// <summary>メモの温存と説明の上書き件数</summary>
    private static IEnumerable<ScenarioRow<FormatMerge>> ReconcileMemoAndDescriptionRows() =>
        [
            Merge(
                    FormatMergePath.Reconcile,
                    "merge-reconcile-preserve-memo-keeps-current-memo",
                    "Memo 温存: preserveExistingMemo=true で現在図の Memo を維持する"
                )
                .Current(() =>
                    new ErDiagram
                    {
                        Entities = { Ent("Customer", Col("Id")).WithMemo("重要顧客テーブル") },
                    }
                )
                // 取込結果（DB 取込）は Memo を持たない
                .Imported(() => new ErDiagram { Entities = { Ent("Customer", Col("Id")) } })
                .PreserveMemo()
                .Expect(e => e.Merged.Single("Memo Customer = \"重要顧客テーブル\"")),
            Merge(
                    FormatMergePath.Reconcile,
                    "merge-reconcile-overwrite-memo-uses-imported-memo",
                    "Memo: preserveExistingMemo=false で取込値の Memo を正とする"
                )
                .Current(() =>
                    new ErDiagram { Entities = { Ent("Customer", Col("Id")).WithMemo("旧メモ") } }
                )
                // Excel 定義書は Memo を保持する
                .Imported(() =>
                    new ErDiagram
                    {
                        Entities = { Ent("Customer", Col("Id")).WithMemo("Excel メモ") },
                    }
                )
                .Expect(e => e.Merged.Single("Memo Customer = \"Excel メモ\"")),
            Merge(
                    FormatMergePath.Reconcile,
                    "merge-reconcile-same-descriptions-count-zero",
                    "説明上書き件数: 実差分が無ければ 0 件"
                )
                .Current(() => DescribedCustomer("顧客", ("Id", "主キー")))
                // 取込結果は Id こそ新規だが、説明は現在図と同一
                .Imported(() => DescribedCustomer("顧客", ("Id", "主キー")))
                .PreserveMemo()
                .Expect(e => e.Merged.Single("DescriptionOverwriteCount = 0")),
            Merge(
                    FormatMergePath.Reconcile,
                    "merge-reconcile-changed-descriptions-count-elements",
                    "説明上書き件数: テーブル・列の説明が変わる要素を数える"
                )
                .Current(() =>
                    DescribedCustomer(
                        "手書きしたテーブル説明",
                        ("Id", "手書きした列説明"),
                        ("Name", "変わらない列説明")
                    )
                )
                // 取込結果はテーブル説明が空・Id 列の説明が別文言・Name 列の説明は同一
                .Imported(() =>
                    DescribedCustomer(
                        string.Empty,
                        ("Id", "DB 側の列説明"),
                        ("Name", "変わらない列説明")
                    )
                )
                .PreserveMemo()
                // テーブル 1 件＋Id 列 1 件＝2 件（Name 列は同一なので数えない）
                .Expect(e => e.Merged.Single("DescriptionOverwriteCount = 2")),
            Merge(
                    FormatMergePath.Reconcile,
                    "merge-reconcile-preserve-memo-ignores-memo-difference",
                    "説明上書き件数: Memo 温存が有効なら Memo 差は数えない"
                )
                .Current(() =>
                    new ErDiagram
                    {
                        Entities = { Ent("Customer", Col("Id")).WithMemo("現在図のメモ") },
                    }
                )
                .Imported(() => new ErDiagram { Entities = { Ent("Customer", Col("Id")) } })
                .PreserveMemo()
                // Memo は温存される＝失われないため上書きとして数えない
                .Expect(e => e.Merged.Single("DescriptionOverwriteCount = 0")),
            Merge(
                    FormatMergePath.Reconcile,
                    "merge-reconcile-overwrite-memo-counts-memo-difference",
                    "説明上書き件数: Memo 上書きが有効なら Memo 差も数える"
                )
                .Current(() =>
                    new ErDiagram
                    {
                        Entities = { Ent("Customer", Col("Id")).WithMemo("現在図のメモ") },
                    }
                )
                // Excel 定義書は Memo を持つ＝取込値で上書きされる
                .Imported(() =>
                    new ErDiagram
                    {
                        Entities = { Ent("Customer", Col("Id")).WithMemo("Excel のメモ") },
                    }
                )
                .Expect(e => e.Merged.Single("DescriptionOverwriteCount = 1")),
            Merge(
                    FormatMergePath.Reconcile,
                    "merge-reconcile-unmatched-elements-not-counted",
                    "説明上書き件数: 一致しない要素の説明差は数えない"
                )
                .Current(() =>
                    new ErDiagram
                    {
                        Entities = { Ent("Customer", Col("Id")).WithDescription("顧客") },
                    }
                )
                // 別テーブル名＝一致しない（新規扱い）ため、説明が違っても上書きは起きない
                .Imported(() =>
                    new ErDiagram
                    {
                        Entities = { Ent("Client", Col("Id")).WithDescription("取引先") },
                    }
                )
                .PreserveMemo()
                .Expect(e => e.Merged.Single("DescriptionOverwriteCount = 0")),
        ];

    /// <summary>クエリの生存と壊れ</summary>
    private static IEnumerable<ScenarioRow<FormatMerge>> ReconcileQueryRows() =>
        [
            Merge(
                    FormatMergePath.Reconcile,
                    "merge-reconcile-rename-breaks-query",
                    "リネームは新規扱い（Id 引継されずクエリが壊れる）"
                )
                .Current(() =>
                {
                    var customer = Ent("Customer", Col("Id"));
                    return new ErDiagram
                    {
                        Entities = { customer },
                        Queries =
                        {
                            new QueryDefinition { Name = "GetAll", EntityId = customer.Id },
                        },
                    };
                })
                // 取込結果はテーブルがリネームされている（別名）
                .Imported(() => new ErDiagram { Entities = { Ent("Client", Col("Id")) } })
                .PreserveMemo()
                .Expect(e =>
                    // 別名のため Id は引き継がれない（新規のまま）
                    e.Merged.Single("Id table Client = imported")
                        .None("Surviving *")
                        .Count("Broken *", 1)
                        .Single("Broken GetAll")
                ),
            Merge(
                    FormatMergePath.Reconcile,
                    "merge-reconcile-all-references-resolved-query-survives",
                    "生存クエリ: 全 Guid 参照が解決できると温存される"
                )
                .Current(() =>
                {
                    var column = Col("CustomerId");
                    var order = Ent("Order", column);
                    return new ErDiagram
                    {
                        Entities = { order },
                        Queries =
                        {
                            new QueryDefinition
                            {
                                Name = "GetByCustomer",
                                EntityId = order.Id,
                                Parameters =
                                {
                                    new QueryParameter
                                    {
                                        Name = "customerId",
                                        SourceColumnId = column.Id,
                                    },
                                },
                                OrderBy = { new QueryOrdering { ColumnId = column.Id } },
                            },
                        },
                    };
                })
                .Imported(() => new ErDiagram { Entities = { Ent("Order", Col("CustomerId")) } })
                .PreserveMemo()
                .Expect(e =>
                    e.Merged.Count("Surviving *", 1)
                        .Single("Surviving GetByCustomer")
                        .None("Broken *")
                ),
            Merge(
                    FormatMergePath.Reconcile,
                    "merge-reconcile-lost-column-reference-breaks-query",
                    "壊れクエリ: 参照列（並び順）が失われると壊れる"
                )
                .Current(() =>
                {
                    var column = Col("CreatedAt");
                    var order = Ent("Order", column);
                    return new ErDiagram
                    {
                        Entities = { order },
                        Queries =
                        {
                            new QueryDefinition
                            {
                                Name = "SortByCreatedAt",
                                EntityId = order.Id,
                                OrderBy = { new QueryOrdering { ColumnId = column.Id } },
                            },
                        },
                    };
                })
                // 取込結果ではエンティティは一致するが、並び順が参照する列がリネームされている
                .Imported(() => new ErDiagram { Entities = { Ent("Order", Col("CreationDate")) } })
                .PreserveMemo()
                .Expect(e =>
                    e.Merged.None("Surviving *")
                        .Count("Broken *", 1)
                        .Single("Broken SortByCreatedAt")
                ),
            Merge(
                    FormatMergePath.Reconcile,
                    "merge-reconcile-empty-current-no-matches",
                    "空図 no-op: 一致なしで全新規、クエリだけ残った図でも壊れとして安全に扱う"
                )
                .Current(() =>
                    new ErDiagram
                    {
                        Queries =
                        {
                            new QueryDefinition { Name = "Orphan", EntityId = Guid.NewGuid() },
                        },
                    }
                )
                .Imported(() => new ErDiagram { Entities = { Ent("Customer", Col("Id")) } })
                .PreserveMemo()
                .Expect(e =>
                    // 取込結果は Id 書換えされず（一致なし）、クエリだけ残った図でも壊れとして分類され例外にならない
                    e.Merged.Single("Id table Customer = imported")
                        .None("Surviving *")
                        .Count("Broken *", 1)
                        .Single("Broken Orphan")
                ),
        ];

    /// <summary>大文字小文字だけ違う名前の照合</summary>
    private static IEnumerable<ScenarioRow<FormatMerge>> ReconcileCaseRows() =>
        [
            Merge(
                    FormatMergePath.Reconcile,
                    "merge-reconcile-case-only-difference-matches-when-unique",
                    "大文字小文字だけ違う名前でも双方で一意なら Id を引き継ぎ、表記は取込側になる"
                )
                .Current(() =>
                {
                    var name = Col("CustomerName");
                    var orders = Ent("Orders", name);
                    return new ErDiagram
                    {
                        Entities = { orders },
                        Queries =
                        {
                            new QueryDefinition
                            {
                                Name = "ByName",
                                EntityId = orders.Id,
                                OrderBy = { new QueryOrdering { ColumnId = name.Id } },
                            },
                        },
                    };
                })
                .Imported(() => new ErDiagram { Entities = { Ent("orders", Col("customername")) } })
                .PreserveMemo()
                .Expect(e =>
                    e.Merged.Single("Id table orders = current:Orders")
                        .Single("TableName orders = orders")
                        .Single("Id column orders.customername = current:Orders.CustomerName")
                        .Single("ColumnName orders.customername = customername")
                        .Count("Surviving *", 1)
                        .None("Broken *")
                ),
            Merge(
                    FormatMergePath.Reconcile,
                    "merge-reconcile-case-only-difference-prefers-exact-match",
                    "大文字小文字だけ違う同名が共存しても、序数一致どうしで組む"
                )
                .Current(() =>
                    new ErDiagram { Entities = { Ent("Dup", Col("Id")), Ent("dup", Col("Id")) } }
                )
                .Imported(() =>
                    new ErDiagram { Entities = { Ent("dup", Col("Id")), Ent("Dup", Col("Id")) } }
                )
                .PreserveMemo()
                .Expect(e =>
                    e.Merged.Single("Id table Dup = current:Dup")
                        .Single("Id table dup = current:dup")
                ),
            Merge(
                    FormatMergePath.Reconcile,
                    "merge-reconcile-case-only-difference-ambiguous-is-not-matched",
                    "大文字小文字を無視すると曖昧な名前は組まずに新規扱いにする"
                )
                .Current(() =>
                    new ErDiagram { Entities = { Ent("Dup", Col("Id")), Ent("dup", Col("Id")) } }
                )
                .Imported(() => new ErDiagram { Entities = { Ent("DUP", Col("Id")) } })
                .PreserveMemo()
                .Expect(e => e.Merged.Single("Id table DUP = imported")),
            Merge(
                    FormatMergePath.Reconcile,
                    "merge-reconcile-case-only-duplicates-on-imported-side-do-not-share-current-id",
                    "取込側にだけ大文字小文字違いの同名が共存しても、現在図の Id は序数一致の 1 件だけが引き継ぐ"
                )
                .Current(() => new ErDiagram { Entities = { Ent("Dup", Col("Id")) } })
                .Imported(() =>
                    new ErDiagram { Entities = { Ent("dup", Col("Id")), Ent("Dup", Col("Id")) } }
                )
                .PreserveMemo()
                .Expect(e =>
                {
                    e.Merged.Single("Id table Dup = current:Dup").Single("Id table dup = imported");
                    e.That.Add(
                        (
                            "マージ後のエンティティの Id が重複しない",
                            (_, outcome) =>
                                outcome
                                    .Merged.Entities.Select(entity => entity.Id)
                                    .Distinct()
                                    .Count() == outcome.Merged.Entities.Count
                        )
                    );
                }),
            Merge(
                    FormatMergePath.Reconcile,
                    "merge-reconcile-case-only-duplicate-columns-on-imported-side-do-not-share-current-id",
                    "取込側にだけ大文字小文字違いの同名列が共存しても、現在図の列 Id は序数一致の 1 件だけが引き継ぐ"
                )
                .Current(() => new ErDiagram { Entities = { Ent("T", Col("Dup")) } })
                .Imported(() => new ErDiagram { Entities = { Ent("T", Col("dup"), Col("Dup")) } })
                .PreserveMemo()
                .Expect(e =>
                {
                    e.Merged.Single("Id column T.Dup = current:T.Dup")
                        .Single("Id column T.dup = imported");
                    e.That.Add(
                        (
                            "マージ後の列の Id が重複しない",
                            (_, outcome) =>
                                outcome
                                    .Merged.Entities[0]
                                    .Columns.Select(c => c.Id)
                                    .Distinct()
                                    .Count() == outcome.Merged.Entities[0].Columns.Count
                        )
                    );
                }),
        ];

    /// <summary>名前・型の列（DiagramMergeReconcilerTests.Col と同じ。Id は新規）</summary>
    private static Column Col(string name, string type = "int") =>
        new() { Name = name, DataType = type };

    /// <summary>名前・列群のエンティティ（DiagramMergeReconcilerTests.Ent と同じ。Id は新規）</summary>
    private static Entity Ent(string name, params Column[] columns) =>
        new() { TableName = name, Columns = columns.ToList() };

    /// <summary>エンティティにメモを付ける</summary>
    private static Entity WithMemo(this Entity entity, string memo)
    {
        entity.Memo = memo;
        return entity;
    }

    /// <summary>エンティティに説明を付ける</summary>
    private static Entity WithDescription(this Entity entity, string description)
    {
        entity.Description = description;
        return entity;
    }

    /// <summary>説明付きの Customer（列は名前と説明の組。型は元のテストと同じく指定しない）</summary>
    private static ErDiagram DescribedCustomer(
        string description,
        params (string Name, string Description)[] columns
    ) =>
        new()
        {
            Entities =
            {
                new Entity
                {
                    TableName = "Customer",
                    Description = description,
                    Columns = columns
                        .Select(c => new Column { Name = c.Name, Description = c.Description })
                        .ToList(),
                },
            },
        };
}
