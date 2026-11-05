using QuickER.Model;
using QuickER.Services;
using QuickER.Tests.TestSupport.Engines;

namespace QuickER.Tests.Gui.Services.FormatRoundTrip;

/// <summary>
/// Excel 定義書のマージ取込の行（TableDefinitionRoundTripTests・DiagramImportServiceTests・MainViewModelMergeImportTests から移した。
/// 経路は製品の DiagramImportService.ImportExcelMerging と同じ並び＝Reconcile → TableDefinitionMergePostProcessor）
/// </summary>
internal static partial class FormatMergeRows
{
    private static IEnumerable<ScenarioRow<FormatMerge>> ExcelMergeRows() =>
        [.. ExcelInheritanceRows(), .. ExcelTargetDbmsRows(), .. ExcelReimportRows()];

    /// <summary>定義書が運べない情報を現在図から引き継ぐ（TableDefinitionRoundTripTests）</summary>
    private static IEnumerable<ScenarioRow<FormatMerge>> ExcelInheritanceRows() =>
        [
            // 素の取込で落ちていることは、形の行 excel-plain-import-drops-computed-and-unique-order-name の Excel の期待が見る
            Merge(
                    FormatMergePath.Excel,
                    "merge-excel-inherits-computed-and-unique-order-and-name",
                    "Excel のマージ取込: 計算列の指定と一意制約の順序・名前を現在図から引き継ぐ"
                )
                .Current(FormatShapeRows.ExcelComputedAndUniqueDiagram)
                .Expect(e =>
                    e.Merged.Single("Computed orders.a")
                        .None("Computed orders.b")
                        .That(
                            "orders の一意制約が (b, a)＝UQ_orders_b_a・(customer_id)＝名前なしの順",
                            lines =>
                                FormatShapeRows.InOrder(
                                    "UniqueOrder ",
                                    "UniqueOrder orders(a,b) = b,a",
                                    "UniqueOrder orders(customer_id) = customer_id"
                                )(lines)
                                && FormatShapeRows.InOrder(
                                    "UniqueName ",
                                    "UniqueName orders(a,b) = UQ_orders_b_a",
                                    "UniqueName orders(customer_id) = -"
                                )(lines)
                        )
                ),
            // 素の取込で落ちていることは、形の行 excel-plain-import-drops-identity の Excel の期待が見る
            Merge(
                    FormatMergePath.Excel,
                    "merge-excel-inherits-identity",
                    "Excel のマージ取込: 採番列の指定を現在図から引き継ぐ"
                )
                .Current(FormatShapeRows.ExcelIdentityDiagram)
                .Expect(e =>
                    e.Merged.Single("IdentityNonKey orders.b").None("IdentityNonKey orders.a")
                ),
            // 素の取込で列の並び順へ戻ることは、形の行 excel-plain-import-drops-primary-key-order の Excel の期待が見る。
            // 置換の確認を出さないこと（入口の配線）は TableDefinitionRoundTripTests.MergeImport_InheritsPrimaryKeyOrder に残す
            Merge(
                    FormatMergePath.Excel,
                    "merge-excel-inherits-primary-key-order",
                    "Excel のマージ取込: 主キー列の集合が同じなら現在図の主キーの順序を引き継ぐ"
                )
                .Current(FormatShapeRows.ExcelReorderedPrimaryKeyDiagram)
                .Expect(e => e.Merged.Single("PrimaryKeyOrder orders = b,a")),
            Merge(
                    FormatMergePath.Excel,
                    "merge-excel-different-primary-key-set-keeps-imported-order",
                    "Excel のマージ取込: 主キー列の集合が現在図と違えば取込値の主キーの順序のまま"
                )
                .Current(FormatShapeRows.ExcelReorderedPrimaryKeyDiagram)
                .Imported(() =>
                {
                    // 定義書側で customer_id も主キーに加えた
                    var diagram = FormatShapeRows.ExcelReorderedPrimaryKeyDiagram();
                    var orders = diagram.Entities[1];
                    orders.Columns.Single(column => column.Name == "customer_id").IsPrimaryKey =
                        true;
                    orders.PrimaryKeyColumnIds = [];
                    return diagram;
                })
                .Expect(e =>
                {
                    e.Merged.Single("PrimaryKeyOrder orders = customer_id,a,b");
                    e.That.Add(OrdersPrimaryKeyOrderNotExplicit());
                }),
            Merge(
                    FormatMergePath.Excel,
                    "merge-excel-current-order-not-explicit-stays-not-explicit",
                    "Excel のマージ取込: 現在図が主キーの順序を明示していなければ明示しないまま"
                )
                .Current(() =>
                {
                    var diagram = FormatShapeRows.ExcelReorderedPrimaryKeyDiagram();
                    diagram.Entities[1].PrimaryKeyColumnIds = [];
                    return diagram;
                })
                .Expect(e =>
                {
                    e.Merged.Single("PrimaryKeyOrder orders = a,b");
                    e.That.Add(OrdersPrimaryKeyOrderNotExplicit());
                }),
            Merge(
                    FormatMergePath.Excel,
                    "merge-excel-different-unique-column-set-keeps-imported",
                    "Excel のマージ取込: 構成列の集合が現在図と違う一意制約は取込値のまま"
                )
                .Current(FormatShapeRows.ExcelComputedAndUniqueDiagram)
                .Imported(() =>
                {
                    // 取込側の orders は元のテストと同じく名前と型だけの列で、一意制約 (customer_id, a) だけを持つ
                    var diagram = FormatShapeRows.ExcelCustomersOrdersDiagram();
                    var orders = diagram.Entities[1];
                    orders.Columns = orders
                        .Columns.Select(column => new Column
                        {
                            Name = column.Name,
                            DataType = column.DataType,
                        })
                        .ToList();
                    orders.UniqueConstraints.Add(
                        new UniqueConstraint
                        {
                            ColumnIds = [orders.Columns[1].Id, orders.Columns[2].Id],
                        }
                    );
                    return diagram;
                })
                .Expect(e =>
                    e.Merged.Count("UniqueSet *", 1)
                        .Single("UniqueOrder orders(a,customer_id) = customer_id,a")
                        .Single("UniqueName orders(a,customer_id) = -")
                        .Single("Id column orders.customer_id = current:orders.customer_id")
                        .Single("Id column orders.a = current:orders.a")
                        .Single("Computed orders.a")
                ),
        ];

    /// <summary>
    /// 対象 DBMS は定義書の宣言に従い、宣言が無ければ現在の図の方言を保つ
    /// （DiagramImportServiceTests.ImportExcel_TargetDbms_FollowsDeclarationOrKeepsCurrent。取込に渡す方言は製品と同じく現在の図の方言）
    /// </summary>
    private static IEnumerable<ScenarioRow<FormatMerge>> ExcelTargetDbmsRows() =>
        [
            Merge(
                    FormatMergePath.Excel,
                    "merge-excel-target-dbms-follows-declaration",
                    "Excel のマージ取込: 定義書が方言を宣言していればそれを採り、宣言が無ければ現在の図の方言を保つ（宣言 sqlite）"
                )
                .Current(() => PostgreSqlCustomer())
                // 現在図と同じ構造の定義書（宣言だけを変える）
                .Imported(() =>
                {
                    var exported = PostgreSqlCustomer();
                    exported.TargetDbms = "sqlite";
                    return exported;
                })
                .TargetDbms("postgresql")
                .Expect(e => e.That.Add(MergedTargetDbmsIs("sqlite"))),
            Merge(
                    FormatMergePath.Excel,
                    "merge-excel-target-dbms-missing-keeps-current",
                    "Excel のマージ取込: 定義書が方言を宣言していればそれを採り、宣言が無ければ現在の図の方言を保つ（宣言なし）"
                )
                .Current(() => PostgreSqlCustomer())
                .ImportedInput(_ =>
                {
                    var exported = PostgreSqlCustomer();
                    exported.TargetDbms = "sqlite";
                    var workbook = TableDefinitionDocumentExporter.BuildWorkbook(
                        exported,
                        ExcelAdapter.DocumentCulture
                    );
                    // 他のアプリで保存し直してカスタムプロパティが落ちたブックを再現する
                    workbook.CustomProperties.Delete(
                        TableDefinitionDocumentLayout.TargetDbmsPropertyName
                    );
                    return new FormatArtifact(ExcelAdapter.Describe(workbook), workbook);
                })
                .TargetDbms("postgresql")
                .Expect(e => e.That.Add(MergedTargetDbmsIs("postgresql"))),
        ];

    /// <summary>VM 経由の Excel 再取込のマージの規則（MainViewModelMergeImportTests）</summary>
    private static IEnumerable<ScenarioRow<FormatMerge>> ExcelReimportRows() =>
        [
            // 現在図を Excel 定義書へ書き出し、そのまま取り込む（テーブル・列名が一致＝マージ対象）
            Merge(
                    FormatMergePath.Excel,
                    "merge-excel-reimport-survives-query",
                    "Excel 再取込経路: 一致するクエリが生存する"
                )
                .Current(() =>
                {
                    // 現在図に、エンティティと列を Guid 参照する名前付きクエリを持たせる（AddEntityCommand が作る表）
                    var diagram = NewTableDiagram();
                    var entity = diagram.Entities[0];
                    diagram.Queries.Add(
                        new QueryDefinition
                        {
                            Name = "SortById",
                            EntityId = entity.Id,
                            OrderBy = { new QueryOrdering { ColumnId = entity.Columns[0].Id } },
                        }
                    );
                    return diagram;
                })
                .Expect(e =>
                    // 取込結果は新規 Guid だがマージで Id が現在図へ寄るため、クエリの参照が解決し生存する
                    e.Merged.Count("Surviving *", 1)
                        .Single("Surviving SortById")
                        .Count("Structure table *", 1)
                        .Single("Id table NewTable = current:NewTable")
                ),
            // 確認が出て件数が載ること・拒否で図が変わらないこと（入口の配線）は MainViewModelMergeImportTests に残す
            Merge(
                    FormatMergePath.Excel,
                    "merge-excel-description-overwrite-counts",
                    "Excel 再取込経路: 説明の上書きがあると確認が出て件数が載る（件数）"
                )
                .Current(() =>
                {
                    var diagram = NewTableDiagram();
                    diagram.Entities[0].Description = "手書きしたテーブル説明";
                    return diagram;
                })
                // 説明が空の状態で書き出した定義書
                .Imported(NewTableDiagram)
                .Expect(e => e.Merged.Single("DescriptionOverwriteCount = 1")),
        ];

    /// <summary>マージ後の orders の主キーの順序が明示されていない（PrimaryKeyColumnIds が空）</summary>
    private static (
        string,
        Func<ErDiagram, FormatMergeOutcome, bool>
    ) OrdersPrimaryKeyOrderNotExplicit() =>
        (
            "マージ後の orders の主キーの順序を明示しない（PrimaryKeyColumnIds が空）",
            (_, outcome) =>
                outcome
                    .Merged.Entities.Single(entity => entity.TableName == "orders")
                    .PrimaryKeyColumnIds.Count == 0
        );

    /// <summary>マージ後の図の対象 DBMS</summary>
    private static (string, Func<ErDiagram, FormatMergeOutcome, bool>) MergedTargetDbmsIs(
        string dbms
    ) => ($"マージ後の図の対象 DBMS が {dbms}", (_, outcome) => outcome.Merged.TargetDbms == dbms);

    /// <summary>PostgreSQL の図の Customer（CustomerId int PK）だけの図</summary>
    private static ErDiagram PostgreSqlCustomer() =>
        new()
        {
            TargetDbms = "postgresql",
            Entities =
            [
                new Entity
                {
                    TableName = "Customer",
                    Columns =
                    [
                        new Column
                        {
                            Name = "CustomerId",
                            DataType = "int",
                            IsPrimaryKey = true,
                            IsNullable = false,
                        },
                    ],
                },
            ],
        };

    /// <summary>MainViewModel の AddEntityCommand が足す表（NewTable・ID int PK）だけの図</summary>
    private static ErDiagram NewTableDiagram() =>
        new()
        {
            Entities =
            {
                new Entity
                {
                    TableName = "NewTable",
                    Columns =
                    {
                        new Column
                        {
                            Name = "ID",
                            DataType = "int",
                            IsPrimaryKey = true,
                            IsNullable = false,
                        },
                    },
                },
            },
        };
}
