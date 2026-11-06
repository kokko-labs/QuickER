using System.Globalization;
using ClosedXML.Excel;
using QuickER.Model;
using QuickER.Resources;
using QuickER.Services;
using QuickER.Tests.TestSupport.Engines;

namespace QuickER.Tests.Gui.Services.FormatRoundTrip;

/// <summary>Excel 定義書の取込の規則の行（出力したブックをセルで書き換えて取り込む）</summary>
internal static partial class FormatRuleRows
{
    private static IEnumerable<ScenarioRow<FormatRule>> Excel() =>
        [
            .. ExcelFormatVersionRules(),
            .. ExcelWorkbookStructureRules(),
            .. ExcelColumnRowRules(),
            .. ExcelRelationshipRowRules(),
        ];

    /// <summary>TableDefinitionDocumentImporterTests の書式バージョン・カルチャ・対象 DBMS の規則</summary>
    private static IEnumerable<ScenarioRow<FormatRule>> ExcelFormatVersionRules() =>
        [
            // この版より新しい書式バージョンのブックは取り込まない（図ファイルの IsNewerFormat と同じ理由で断る）
            ExcelRule(
                    "excel-rule-newer-format-version-is-rejected",
                    "新しい書式バージョンの定義書は取り込まない"
                )
                .Workbook(ExcelSampleWorkbook(workbook => SetFormatVersion(workbook, "2")))
                .Rejects(InvalidData("*2*")),
            // 断る根拠は「この版より新しいと分かること」だけで、分からないものは役割タグの検査に委ねる
            .. new (string Slug, string? Version)[]
            {
                ("null", null),
                ("empty", string.Empty),
                ("v2", "v2"),
            }.Select(item =>
                ExcelRule(
                        $"excel-rule-unknown-format-version-is-accepted-{item.Slug}",
                        "書式バージョンが無い・読めない定義書は従来どおり取り込む"
                            + $"（{item.Version ?? "null"}）"
                    )
                    .Workbook(
                        ExcelSampleWorkbook(workbook => SetFormatVersion(workbook, item.Version))
                    )
                    .Expect(e => ExcelSampleDiagram(e.Imported))
                    .Row()
            ),
            ExcelRule("excel-rule-japanese-workbook", "ja カルチャ出力もカルチャ非依存で取込できる")
                .Workbook(ExcelSampleWorkbook(culture: new CultureInfo("ja")))
                .Expect(e => ExcelSampleDiagram(e.Imported)),
            ExcelRule("excel-rule-sheet-rename", "一覧シートをリネームしても取込できる")
                .Workbook(
                    ExcelSampleWorkbook(workbook =>
                    {
                        // ユーザーが一覧シートをリネームする状況を再現（定義名はセル参照なので追随する）
                        RoleSheet(workbook, TableDefinitionDocumentLayout.SummaryDefinedName).Name =
                            "Renamed Summary";
                        RoleSheet(
                            workbook,
                            TableDefinitionDocumentLayout.RelationshipsDefinedName
                        ).Name = "Renamed Relationships";
                    })
                )
                .Expect(e => ExcelSampleDiagram(e.Imported)),
            ExcelRule("excel-rule-restores-target-dbms", "対象 DBMS を往復取込で復元できる")
                .Workbook(
                    ExcelSampleWorkbook(diagram: () =>
                    {
                        var source = FormatShapeRows.ExcelCategoryProductDiagram();
                        source.TargetDbms = "sqlite";
                        return source;
                    })
                )
                .Expect(e => e.ImportedDiagram.Add(TargetDbmsIs("sqlite"))),
            ExcelRule(
                    "excel-rule-missing-target-dbms-property-keeps-default",
                    "対象 DBMS プロパティが無くても既定値で取込できる"
                )
                .Workbook(
                    // ユーザーがプロパティを削除した状況を再現（表示セルは残っていても取込は既定値へフォールバック）
                    ExcelSampleWorkbook(workbook =>
                        workbook.CustomProperties.Delete(
                            TableDefinitionDocumentLayout.TargetDbmsPropertyName
                        )
                    )
                )
                .Expect(e => e.ImportedDiagram.Add(TargetDbmsIs(new ErDiagram().TargetDbms))),
        ];

    /// <summary>TableDefinitionDocumentImporterTests の役割タグ・詳細シートの規則</summary>
    private static IEnumerable<ScenarioRow<FormatRule>> ExcelWorkbookStructureRules() =>
        [
            ExcelRule("excel-rule-missing-role-tag", "役割タグが無いと取り込みをエラーにする")
                .Workbook(
                    // テーブル一覧タグを剥がすと必須役割が解決できずエラーになる
                    ExcelSampleWorkbook(workbook =>
                        workbook.DefinedNames.Delete(
                            TableDefinitionDocumentLayout.SummaryDefinedName
                        )
                    )
                )
                .Rejects(InvalidData(Strings.TableDoc_MissingRoleTag)),
            ExcelRule(
                    "excel-rule-missing-detail-sheet",
                    "詳細シートが不足していると取り込みをエラーにする"
                )
                .Workbook(
                    ExcelSampleWorkbook(
                        // 唯一の詳細シートを削除すると一覧 1 件・詳細 0 件で件数不一致になる
                        workbook => workbook.Worksheet("Users").Delete(),
                        diagram: () =>
                            new ErDiagram
                            {
                                Entities =
                                {
                                    new Entity
                                    {
                                        TableName = "Users",
                                        Columns =
                                        {
                                            new Column
                                            {
                                                Name = "Id",
                                                DataType = "int",
                                                IsPrimaryKey = true,
                                                IsNullable = false,
                                            },
                                        },
                                    },
                                },
                            }
                    )
                )
                .Rejects(InvalidData(Strings.TableDoc_CountMismatch)),
        ];

    /// <summary>
    /// TableDefinitionDocumentImporterTests のカラムの行の規則（src/QuickER.Gui/Services/CLAUDE.md「Excel 定義書の取込：名前が空の行」）。
    /// 出力の見出しの最後の列が定数と一致することは、形の行 excel-exported-workbook の Excel の出力の期待が見る
    /// </summary>
    private static IEnumerable<ScenarioRow<FormatRule>> ExcelColumnRowRules() =>
        [
            // 黙ってテーブルの終わりとみなすと、その行と後ろの行のカラムが消える。No. の列は数えない
            .. Enumerable
                .Range(3, 6)
                .Select(filledColumn =>
                    ExcelRule(
                            $"excel-rule-row-without-column-name-but-other-content-{filledColumn}",
                            "カラム名が空でほかのセルに値がある行は、テーブルと行を名指しして断る"
                                + $"（{filledColumn} 列目）"
                        )
                        .Workbook(
                            ExcelSampleWorkbook(workbook =>
                                workbook
                                    .Worksheet("Category")
                                    .Cell(
                                        TableDefinitionDocumentLayout.DetailColumnDataStartRow + 2,
                                        filledColumn
                                    )
                                    .Value = "x"
                            )
                        )
                        .Rejects(
                            InvalidData(
                                string.Format(
                                    Strings.TableDoc_ColumnMissingName,
                                    "Category",
                                    TableDefinitionDocumentLayout.DetailColumnDataStartRow + 2
                                )
                            )
                        )
                        .Row()
                ),
            // 従来はそこで表が終わり、その行と後ろのカラムが黙って消えていた
            ExcelRule(
                    "excel-rule-middle-row-without-column-name",
                    "途中のカラムの名前だけを消した行は、テーブルと行を名指しして断る"
                )
                .Workbook(
                    ExcelSampleWorkbook(workbook =>
                        workbook
                            .Worksheet("Category")
                            .Cell(TableDefinitionDocumentLayout.DetailColumnDataStartRow, 2)
                            .Value = string.Empty
                    )
                )
                .Rejects(
                    InvalidData(
                        string.Format(
                            Strings.TableDoc_ColumnMissingName,
                            "Category",
                            TableDefinitionDocumentLayout.DetailColumnDataStartRow
                        )
                    )
                ),
            // すべてが空の行より下の行は読まない
            .. new[] { true, false }.Select(numberOnly =>
                ExcelRule(
                        numberOnly
                            ? "excel-rule-row-with-only-number-ends-table"
                            : "excel-rule-empty-row-ends-table",
                        "No. だけの行・すべて空の行は表の終わりとして受ける"
                            + (numberOnly ? "（No. だけの行）" : "（すべて空の行）")
                    )
                    .Workbook(
                        ExcelSampleWorkbook(workbook =>
                        {
                            var category = workbook.Worksheet("Category");
                            var row = TableDefinitionDocumentLayout.DetailColumnDataStartRow + 2;

                            if (numberOnly)
                            {
                                category.Cell(row, 1).Value = 3;
                            }

                            // 表の終わりより下の行は読まない
                            category.Cell(row + 1, 2).Value = "Ignored";
                            category.Cell(row + 1, 4).Value = "int";
                        })
                    )
                    .Expect(e =>
                        e.Imported.Single("ColumnOrder Category = CategoryId,CategoryName")
                    )
                    .Row()
            ),
        ];

    /// <summary>
    /// リレーション一覧の行の規則（TableDefinitionDocumentImporterTests・TableDefinitionRoundTripTests・
    /// TableDefinitionCompositeForeignKeyTests・TableDefinitionConstraintNameTests）
    /// </summary>
    private static IEnumerable<ScenarioRow<FormatRule>> ExcelRelationshipRowRules() =>
        [
            ExcelRule(
                    "excel-rule-relationship-column-not-found",
                    "リレーション一覧の参照カラムが存在しないと取り込みをエラーにする"
                )
                .Workbook(
                    // 参照元カラム（列4）をデータ開始行で実在しない名前へ改変する
                    ExcelSampleWorkbook(workbook =>
                        RoleSheet(workbook, TableDefinitionDocumentLayout.RelationshipsDefinedName)
                            .Cell(TableDefinitionDocumentLayout.RelationshipDataStartRow, 4)
                            .Value = "MissingColumn"
                    )
                )
                .Rejects(
                    InvalidData(
                        string.Format(
                            Strings.TableDoc_RelChildColumnNotFound,
                            "Product",
                            "MissingColumn"
                        )
                    )
                ),
            ExcelRule(
                    "excel-rule-relationship-with-one-column-cell-empty-is-rejected",
                    "Excel 取込: 参照元列・参照先列の片方だけが空の行は従来どおり断る"
                )
                .Workbook(
                    ExcelSampleWorkbook(
                        workbook =>
                            RoleSheet(
                                    workbook,
                                    TableDefinitionDocumentLayout.RelationshipsDefinedName
                                )
                                .Cell(TableDefinitionDocumentLayout.RelationshipDataStartRow, 4)
                                .Value = string.Empty,
                        diagram: () =>
                        {
                            var diagram = FormatShapeRows.ExcelCustomersOrdersDiagram();
                            diagram.Relationships.Add(
                                new Relationship
                                {
                                    SourceEntityId = diagram.Entities[0].Id,
                                    TargetEntityId = diagram.Entities[1].Id,
                                    Type = RelationshipType.OneToMany,
                                    ColumnPairs =
                                    [
                                        new RelationshipColumnPair(
                                            diagram.Entities[0].Columns[0].Id,
                                            diagram.Entities[1].Columns[1].Id
                                        ),
                                    ],
                                }
                            );
                            return diagram;
                        }
                    )
                )
                .Rejects(InvalidData("*")),
            ExcelRule(
                    "excel-rule-composite-column-count-mismatch",
                    "Excel 取込は参照元列と参照先列の数が違う行をエラーにする"
                )
                .Workbook(
                    // 参照先列（親側）だけを 1 列に減らし、数の不一致を作る
                    ExcelSampleWorkbook(
                        workbook =>
                            RelationshipRow(workbook, "FK_TenantUser_TenantRegion").Cell(6).Value =
                                "TenantId",
                        diagram: FormatShapeRows.ExcelTenantDiagram
                    )
                )
                .Rejects(InvalidData("*")),
            ExcelRule(
                    "excel-rule-identical-relationship-rows-are-both-read",
                    "Excel 取込は構成列まで同一のリレーション行も 2 本とも読む（出力は同じ列ペアの 2 本を 2 行で書く。DBML・Mermaid と同じ扱い）"
                )
                .Workbook(
                    // 請求先の行の参照元列を配送先と同じにし、構成列まで同一の 2 行を作る
                    ExcelSampleWorkbook(
                        workbook =>
                            RelationshipRow(workbook, "FK_Orders_BillToAddress").Cell(4).Value =
                                "ShipToAddressId",
                        diagram: FormatShapeRows.ExcelAddressOrdersDiagram
                    )
                )
                .Expect(e =>
                    // 明示した名前は変えない（取込の共通の規則）
                    e.Imported.Count("RelationshipType *", 2)
                        .Single("ColumnPairs Address->Orders#0 = AddressId>ShipToAddressId")
                        .Single("ConstraintName Address->Orders#0 = FK_Orders_ShipToAddress")
                        .Single("ColumnPairs Address->Orders#1 = AddressId>ShipToAddressId")
                        .Single("ConstraintName Address->Orders#1 = FK_Orders_BillToAddress")
                ),
            // 制約名欄が空のリレーションは既定名で DDL に出るため、同じテーブルの組に 2 行あると 2 本目が失敗する
            ExcelRule(
                    "excel-rule-hand-written-empty-names-second-gets-unique-name",
                    "制約名欄を空にした手書きの定義書でも、同じ組の 2 行目に子の列名つきの名前を付ける"
                )
                .Workbook(
                    ExcelSampleWorkbook(
                        workbook =>
                        {
                            // リレーション一覧の制約名欄（2 列目）を、データ行の先頭から 2 行だけ空にする
                            var sheet = RoleSheet(
                                workbook,
                                TableDefinitionDocumentLayout.RelationshipsDefinedName
                            );

                            for (var offset = 0; offset < 2; offset++)
                            {
                                var cell = sheet.Cell(
                                    TableDefinitionDocumentLayout.RelationshipDataStartRow + offset,
                                    2
                                );

                                if (cell.GetString().Length == 0)
                                {
                                    throw new InvalidOperationException(
                                        "書き換える前は名前が入っている"
                                    );
                                }

                                cell.Value = string.Empty;
                            }
                        },
                        diagram: () =>
                            FormatShapeRows.CustomersAndOrders(
                                ("bill_customer_id", "fk_bill"),
                                ("ship_customer_id", "fk_ship")
                            ),
                        culture: CultureInfo.InvariantCulture
                    )
                )
                .Expect(e =>
                {
                    e.Imported.Count("RelationshipType *", 2)
                        .Single("ColumnPairs customers->orders#0 = id>bill_customer_id")
                        .Single("ConstraintName customers->orders#0 = -")
                        .Single("ColumnPairs customers->orders#1 = id>ship_customer_id")
                        .Single(
                            "ConstraintName customers->orders#1 = FK_orders_customers_ship_customer_id"
                        );
                    e.ImportedDiagram.Add(DistinctDdlConstraintNames());
                }),
        ];

    /// <summary>Excel の規則の行を始める</summary>
    private static FormatRuleBuilder ExcelRule(string id, string description) =>
        new(nameof(DiagramExportFormat.Excel), id, description);

    /// <summary>
    /// 図を定義書へ出力し、書き換えてから渡すブック（図の既定は Category / Product のサンプル図・カルチャの既定は定義書の言語）
    /// </summary>
    private static Func<XLWorkbook> ExcelSampleWorkbook(
        Action<XLWorkbook>? edit = null,
        Func<ErDiagram>? diagram = null,
        CultureInfo? culture = null
    ) =>
        () =>
        {
            var workbook = TableDefinitionDocumentExporter.BuildWorkbook(
                (diagram ?? FormatShapeRows.ExcelCategoryProductDiagram)(),
                culture ?? ExcelAdapter.DocumentCulture
            );
            edit?.Invoke(workbook);
            return workbook;
        };

    /// <summary>Category / Product のサンプル図を取り込んだ図の期待（元の AssertSampleDiagram と同じ範囲）</summary>
    private static void ExcelSampleDiagram(FormatLineCheck imported) =>
        imported
            .Count("Structure table *", 2)
            .Count("RelationshipType *", 1)
            .Single("TableDescription Category = \"カテゴリ\"")
            .Single("Memo Category = \"分類用\"")
            .Single("ForeignKeyMarker Product.CategoryId")
            .Single("ConstraintName * = FK_Product_Category")
            .Single("RelationshipType * = OneToMany")
            .Single("ReferentialAction * = del=Cascade upd=NoAction");

    /// <summary>ブックの書式バージョンのカスタムプロパティを差し替える（null なら消す）</summary>
    private static void SetFormatVersion(XLWorkbook workbook, string? value)
    {
        workbook.CustomProperties.Delete(TableDefinitionDocumentLayout.FormatVersionPropertyName);

        if (value is not null)
        {
            workbook.CustomProperties.Add(
                TableDefinitionDocumentLayout.FormatVersionPropertyName,
                value
            );
        }
    }

    /// <summary>役割タグ（定義名）からシートを引く（無ければ入力を組めない）</summary>
    private static IXLWorksheet RoleSheet(XLWorkbook workbook, string definedName) =>
        workbook.DefinedNames.TryGetValue(definedName, out var defined)
            ? defined.Ranges.First().Worksheet
            : throw new InvalidOperationException($"役割タグ {definedName} が無い");

    /// <summary>リレーション一覧で制約名欄（2 列目）が指定の名前の行</summary>
    private static IXLRow RelationshipRow(XLWorkbook workbook, string constraintName)
    {
        var sheet = RoleSheet(workbook, TableDefinitionDocumentLayout.RelationshipsDefinedName);
        var row = TableDefinitionDocumentLayout.RelationshipDataStartRow;

        var last = sheet.LastRowUsed()?.RowNumber() ?? 0;

        while (sheet.Cell(row, 2).GetString() != constraintName)
        {
            row++;

            if (row > last)
            {
                throw new InvalidOperationException($"制約名 {constraintName} の行が無い");
            }
        }

        return sheet.Row(row);
    }

    /// <summary>取り込んだ図の対象 DBMS</summary>
    private static (string, Func<ErDiagram, bool>) TargetDbmsIs(string dbms) =>
        ($"取り込んだ図の対象 DBMS が {dbms}", diagram => diagram.TargetDbms == dbms);
}
