using System.Text.RegularExpressions;
using ClosedXML.Excel;
using QuickER.Model;
using QuickER.Resources;
using QuickER.Services;
using QuickER.Tests.TestSupport.Engines;

namespace QuickER.Tests.Gui.Services.FormatRoundTrip;

/// <summary>Excel 定義書の往復のテストから移した形の行</summary>
internal static partial class FormatShapeRows
{
    private static IEnumerable<ScenarioRow<FormatShape>> TableDefinitionRows() =>
        [
            // TableDefinitionDocumentImporterTests（Load_RoundTripsExportedWorkbook と Load_CurrentFormatVersion_IsAccepted は同じ図・同じ表明）
            Shape("excel-exported-workbook", "このアプリが出力した定義書をそのまま再取込できる")
                .Build(ExcelCategoryProductDiagram)
                .Expect(
                    e =>
                        e.Imported.Count("Structure table *", 2)
                            .Count("RelationshipType *", 1)
                            .Single("TableDescription Category = \"カテゴリ\"")
                            .Single("Memo Category = \"分類用\"")
                            .Single("ForeignKeyMarker Product.CategoryId")
                            .Single("ConstraintName * = FK_Product_Category")
                            .Single("RelationshipType * = OneToMany")
                            .Single("ReferentialAction * = del=Cascade upd=NoAction"),
                    nameof(DiagramExportFormat.Excel)
                )
                .Expect(
                    e =>
                        // TableDefinitionDocumentImporterTests.Load_RowWithoutColumnNameButOtherContent_Throws の前提：
                        // 判定する列の数は、出力するカラムの表の列の数と同じ（Category の見出し行の最後のセルの列）
                        e.Output.That(
                            "Category の見出し行の最後のセルの列が TableDefinitionDocumentLayout.DetailColumnCount",
                            LastUsedColumnIs(
                                "Category",
                                TableDefinitionDocumentLayout.DetailColumnHeaderRow,
                                TableDefinitionDocumentLayout.DetailColumnCount
                            )
                        ),
                    nameof(DiagramExportFormat.Excel)
                ),
            // TableDefinitionRoundTripTests
            Shape(
                    "excel-pairless-relationships",
                    "Excel 往復: 列の対応を持たないリレーションは列ペアなしのまま読み戻せる（同じ組に 2 本でも）"
                )
                .Build(() =>
                {
                    var diagram = ExcelCustomersOrdersDiagram();

                    foreach (
                        var type in new[] { RelationshipType.OneToMany, RelationshipType.OneToOne }
                    )
                    {
                        diagram.Relationships.Add(
                            new Relationship
                            {
                                SourceEntityId = diagram.Entities[0].Id,
                                TargetEntityId = diagram.Entities[1].Id,
                                Type = type,
                            }
                        );
                    }

                    return diagram;
                })
                .Expect(
                    e =>
                        e.Imported.Count("RelationshipType *", 2)
                            .That(
                                "リレーションの種別が OneToMany・OneToOne の順",
                                InOrder(
                                    "RelationshipType ",
                                    "RelationshipType customers->orders#0 = OneToMany",
                                    "RelationshipType customers->orders#1 = OneToOne"
                                )
                            )
                            .None("ColumnPairs * = *")
                            .None("ForeignKeyMarker *"),
                    nameof(DiagramExportFormat.Excel)
                ),
            Shape("excel-table-without-columns", "Excel 往復: 列の無いテーブルを読み戻せる")
                .Build(() =>
                {
                    var diagram = ExcelCustomersOrdersDiagram();
                    diagram.Entities.Add(new Entity { TableName = "empty_table" });
                    return diagram;
                })
                .Expect(
                    e =>
                        e.Imported.Single("Structure table empty_table")
                            .None("Structure column empty_table.*"),
                    nameof(DiagramExportFormat.Excel)
                ),
            // TableDefinitionCompositeForeignKeyTests
            Shape("excel-composite-foreign-key", "Excel 定義書は複合外部キーを往復で復元する")
                .Build(ExcelTenantDiagram)
                .Expect(
                    e =>
                    {
                        e.Imported.Single("Structure table TenantRegion")
                            .Single("Structure table TenantUser")
                            .Single("ForeignKeyMarker TenantUser.TenantRef")
                            .Single("ForeignKeyMarker TenantUser.RegionRef");
                        e.ImportedDiagram.Add(
                            PairsOfNamed(
                                "FK_TenantUser_TenantRegion",
                                "TenantId>TenantRef,RegionCode>RegionRef"
                            )
                        );
                        e.ImportedDiagram.Add(PairCountOfNamed("FK_TenantUser_Plan", 1));
                    },
                    nameof(DiagramExportFormat.Excel)
                )
                .Expect(
                    e =>
                    {
                        // TableDefinitionCompositeForeignKeyTests.HtmlExport_WritesCompositeColumnsInOneRow：
                        // 参照元列（子）・参照先列（親）とも構成列を宣言順で並べ、詳細のキー欄は複合の構成列すべてに同じ番号（単一 FK は別番号）
                        e.Output.Contains("<td>TenantRef, RegionRef</td>")
                            .Contains("<td>TenantId, RegionCode</td>")
                            .Contains("<td>FK1</td>")
                            .Contains("<td>FK2</td>");
                    },
                    nameof(DiagramExportFormat.Html)
                ),
            Shape(
                    "excel-parallel-foreign-keys",
                    "Excel 定義書は同じ親子間の複数の外部キーを往復で復元する"
                )
                .Build(ExcelAddressOrdersDiagram)
                .Expect(
                    e =>
                    {
                        e.Imported.Single("Structure table Address")
                            .Single("Structure table Orders")
                            .Count("RelationshipType *", 2)
                            .Single("ForeignKeyMarker Orders.ShipToAddressId")
                            .Single("ForeignKeyMarker Orders.BillToAddressId");
                        e.ImportedDiagram.Add(
                            (
                                "制約名と子列の組が (FK_Orders_ShipToAddress, ShipToAddressId)・(FK_Orders_BillToAddress, BillToAddressId)（順不同）",
                                d =>
                                    d.Relationships.All(r => r.ColumnPairs.Count == 1)
                                    && d.Relationships.Select(r =>
                                            $"{r.ConstraintName}|{ColumnName(d, r.ColumnPairs[0].TargetColumnId)}"
                                        )
                                        .Order(StringComparer.Ordinal)
                                        .SequenceEqual(
                                            [
                                                "FK_Orders_BillToAddress|BillToAddressId",
                                                "FK_Orders_ShipToAddress|ShipToAddressId",
                                            ],
                                            StringComparer.Ordinal
                                        )
                            )
                        );
                        e.ImportedDiagram.Add(
                            (
                                "参照先はどちらも親の主キー AddressId",
                                d =>
                                    d
                                        .Entities.FirstOrDefault(entity =>
                                            entity.TableName == "Address"
                                        )
                                        ?.Columns.FirstOrDefault(c => c.Name == "AddressId")
                                        ?.Id
                                        is { } addressId
                                    && d.Relationships.All(r =>
                                        r.ColumnPairs.Count == 1
                                        && r.ColumnPairs[0].SourceColumnId == addressId
                                    )
                            )
                        );
                    },
                    nameof(DiagramExportFormat.Excel)
                ),
            // TableDefinitionConstraintNameTests
            Shape(
                    "excel-unnamed-same-pair",
                    "同じテーブルの組の名前の無いリレーション 2 本は、往復すると 2 本目だけが名前を得る"
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
                    nameof(DiagramExportFormat.Excel)
                ),
            Shape("excel-single-unnamed", "名前の無いリレーションが 1 本だけなら名前を持たないまま")
                .Build(() => CustomersAndOrders(("bill_customer_id", null)))
                .DistinctDdlConstraintNames()
                .Expect(
                    e =>
                        e.Imported.Count("RelationshipType *", 1)
                            .Single("ColumnPairs customers->orders#0 = id>bill_customer_id")
                            .Single("ConstraintName customers->orders#0 = -"),
                    nameof(DiagramExportFormat.Excel)
                ),
            Shape(
                    "excel-named-row-takes-default",
                    "制約名欄の名前が既定名をふさいでいれば、名前の無い行に名前を付け、書かれた名前は変えない"
                )
                .Build(() =>
                    CustomersAndOrders(
                        ("bill_customer_id", null),
                        ("ship_customer_id", "FK_orders_customers")
                    )
                )
                .DistinctDdlConstraintNames()
                .Expect(
                    e =>
                        e.Imported.Count("RelationshipType *", 2)
                            .Single("ColumnPairs customers->orders#0 = id>bill_customer_id")
                            .Single(
                                "ConstraintName customers->orders#0 = FK_orders_customers_bill_customer_id"
                            )
                            .Single("ColumnPairs customers->orders#1 = id>ship_customer_id")
                            .Single("ConstraintName customers->orders#1 = FK_orders_customers"),
                    nameof(DiagramExportFormat.Excel)
                ),
            // TableDefinitionUniqueConstraintTests
            Shape(
                    "excel-unique-constraints",
                    "Excel 定義書は一意制約（複合・複数件）を往復で復元する"
                )
                .Build(ExcelCategoryProductUniqueDiagram)
                .Expect(
                    e =>
                        e.Imported.Count("UniqueSet Product(*", 2)
                            .Single("UniqueSet Product(Sku)")
                            .Single("UniqueSet Product(CategoryId,Code)")
                            .Count("UniqueName Product(*) = -", 2)
                            .Single("PrimaryKey Product.ProductId")
                            .Single("ForeignKeyMarker Product.CategoryId"),
                    nameof(DiagramExportFormat.Excel)
                )
                .Expect(
                    e =>
                    {
                        // TableDefinitionUniqueConstraintTests.HtmlExport_WritesUniqueConstraintLabels：
                        // 単独列の制約は UQ1、FK 列を含む複合制約は FK1/UQ2 と UQ2（同番＝同じ制約）
                        e.Output.Contains("<td>UQ1</td>")
                            .Contains("<td>FK1/UQ2</td>")
                            .Contains("<td>UQ2</td>");
                    },
                    nameof(DiagramExportFormat.Html)
                ),
            .. TableDefinitionOutputRows(),
            .. TableDefinitionPlainImportRows(),
        ];

    /// <summary>
    /// Excel・HTML の定義書の出力の記法（TableDefinitionDocumentExporterTests・TableDefinitionHtmlExporterTests・
    /// TableDefinitionUniqueConstraintTests から移した。Excel のセルは <c>{シート}!{セル}={値}</c> の行）
    /// </summary>
    private static IEnumerable<ScenarioRow<FormatShape>> TableDefinitionOutputRows() =>
        [
            Shape(
                    "excel-html-user-order",
                    "定義書（Excel・HTML）は User / Order の 2 テーブルと 1 リレーションを決められた位置と記法で書く"
                )
                .Build(TableDocUserOrderDiagram)
                .Expect(
                    e =>
                    {
                        // BuildWorkbook_CreatesSheetsInExpectedOrder：エンティティは名前昇順（Order → User）。一覧 2 シートに続いて詳細シートが並ぶ
                        e.Output.That(
                            "シートが テーブル一覧・リレーション一覧・Order・User の順",
                            SheetOrderIs(
                                En(nameof(Strings.TableDoc_Sheet_Summary)),
                                En(nameof(Strings.TableDoc_Sheet_Relationships)),
                                "Order",
                                "User"
                            )
                        );
                        // BuildWorkbook_WritesSummarySheet：行1=タイトル・行3=ヘッダ・行4〜=データ。説明・備考はユーザーデータどおり
                        var summary = En(nameof(Strings.TableDoc_Sheet_Summary));
                        e.Output.Contains(
                                ExcelCell(summary, "A1", En(nameof(Strings.TableDoc_Sheet_Summary)))
                            )
                            .Contains(
                                ExcelCell(summary, "A3", En(nameof(Strings.TableDoc_Header_No)))
                            )
                            .Contains(
                                ExcelCell(
                                    summary,
                                    "B3",
                                    En(nameof(Strings.TableDoc_Header_TableName))
                                )
                            )
                            .Contains(
                                ExcelCell(
                                    summary,
                                    "C3",
                                    En(nameof(Strings.TableDoc_Header_Description))
                                )
                            )
                            .Contains(
                                ExcelCell(summary, "D3", En(nameof(Strings.TableDoc_Header_Memo)))
                            )
                            .Contains(ExcelCell(summary, "B4", "Order"))
                            .Contains(ExcelCell(summary, "B5", "User"))
                            .Contains(ExcelCell(summary, "C5", "利用者"))
                            .Contains(ExcelCell(summary, "D5", "業務利用"));
                        // BuildWorkbook_RelationshipTableCellsLinkToDetailSheets：データ行4の参照元（列3）・参照先（列5）は文字列値がテーブル名のまま。
                        // Build_ProducesNavigationAnchorsAndRelationshipContent：リレーションの種別（列7）・削除時（列8）
                        var relationships = En(nameof(Strings.TableDoc_Sheet_Relationships));
                        e.Output.Contains(ExcelCell(relationships, "C4", "Order"))
                            .Contains(ExcelCell(relationships, "E4", "User"))
                            .Contains(ExcelCell(relationships, "G4", "N:1"))
                            .Contains(ExcelCell(relationships, "H4", "CASCADE"));
                        // BuildWorkbook_WritesDetailSheet：行1 タイトルと戻りリンク・行2 説明（Order は説明なし）・
                        // 行3 見出し・行4〜 データ（キー・必須・参照先）。日本語の説明は無加工
                        e.Output.Contains(ExcelCell("Order", "A1", "Order"))
                            .Contains(
                                ExcelCell("Order", "H1", En(nameof(Strings.TableDoc_BackToSummary)))
                            )
                            .That("Order の説明の行（A2）が空", CellIsEmpty("Order", "A2"))
                            .Contains(
                                ExcelCell(
                                    "Order",
                                    "B3",
                                    En(nameof(Strings.TableDoc_Header_ColumnName))
                                )
                            )
                            .Contains(ExcelCell("Order", "B4", "Id"))
                            .Contains(ExcelCell("Order", "F4", "PK"))
                            .Contains(ExcelCell("Order", "B5", "UserId"))
                            .Contains(ExcelCell("Order", "F5", "FK1"))
                            .Contains(
                                ExcelCell("Order", "E5", En(nameof(Strings.TableDoc_RequiredMark)))
                            )
                            .Contains(ExcelCell("Order", "G5", "User.Id"))
                            .Contains(ExcelCell("User", "A2", "利用者"))
                            .Contains(ExcelCell("User", "C4", "主キー"))
                            .Contains(
                                ExcelCell("User", "E4", En(nameof(Strings.TableDoc_RequiredMark)))
                            );
                    },
                    nameof(DiagramExportFormat.Excel)
                )
                .Expect(
                    e =>
                    {
                        // Build_ProducesNavigationAnchorsAndRelationshipContent：固定サイドバーと各テーブル詳細セクション（名前昇順 Order=1 / User=2）・
                        // テーブル一覧からの詳細アンカーリンク・概要（対象 DBMS）・リレーション表とキー表記（Excel 版の対応セルと同じ値）・参照先のリンク
                        e.Output.Contains("<nav id=\"sidebar\">")
                            .Contains("id=\"table-1\"")
                            .Contains("id=\"table-2\"")
                            .Contains("<h2>1. Order</h2>")
                            .Contains("<h2>2. User</h2>")
                            .Contains("<a href=\"#table-1\">Order</a>")
                            .Contains("<a href=\"#table-2\">User</a>")
                            .Contains(En(nameof(Strings.TableDoc_Cover_TargetDbms)))
                            .Contains("<dd>sqlserver</dd>")
                            .Contains("<td>N:1</td>")
                            .Contains("<td>CASCADE</td>")
                            .Contains("<td>FK1</td>")
                            .Contains("<a href=\"#table-2\">User.Id</a>");
                        // Build_OmitsSystemNameCreatedDateMemoAndBackLink：タイトルは文書名のみ・概要にシステム名と作成日が無い・
                        // 戻りリンクが無い・リレーション一覧は 9 列・各テーブルのカラム表は 7 列（備考列なし）
                        e.Output.Contains(
                                $"<title>{En(nameof(Strings.TableDoc_DocumentTitle))}</title>"
                            )
                            .That(
                                "概要（header#overview）に System Name・Created Date を含まない",
                                lines =>
                                    HtmlSection(lines[0], "<header id=\"overview\">", "</header>")
                                        is { } overview
                                    && !overview.Contains("System Name", StringComparison.Ordinal)
                                    && !overview.Contains("Created Date", StringComparison.Ordinal)
                            )
                            .Absent("class=\"back-link\"")
                            .Absent(En(nameof(Strings.TableDoc_BackToSummary)))
                            .That(
                                "リレーション一覧（section#relationship-list）の見出しセルが 9 個",
                                HtmlHeaderCellCount(
                                    "<section id=\"relationship-list\">",
                                    "</section>",
                                    9
                                )
                            )
                            .That(
                                "1 つ目のテーブル詳細（section#table-1）の見出しセルが 7 個",
                                HtmlHeaderCellCount(
                                    "<section class=\"table-detail\" id=\"table-1\">",
                                    "</section>",
                                    7
                                )
                            );
                        // Build_IsSelfContained：外部リソース参照を一切含まない
                        e.Output.Absent("src=")
                            .Absent("<link")
                            .Absent("http://")
                            .Absent("https://");
                    },
                    nameof(DiagramExportFormat.Html)
                ),
            // TableDefinitionDocumentExporterTests.BuildWorkbook_WritesTargetDbms（カスタムプロパティの値は元のテストに残す）。
            // C# は元の図の方言と別に SQL Server で生成する（SQLite の int の綴りの既知の失敗 P3-8-J と同じ根を重ねない）
            Shape(
                    "excel-target-dbms-sqlite",
                    "BuildWorkbook: 対象 DBMS を一覧タイトル直下とカスタムプロパティへ出力する（一覧タイトル直下）"
                )
                .Build(() =>
                {
                    var diagram = TableDocUserOrderDiagram();
                    diagram.TargetDbms = "sqlite";
                    return diagram;
                })
                .Settings(new FormatSettings(CSharpDialect: "sqlserver"))
                .Expect(
                    e =>
                        e.Output.Contains(
                            ExcelCell(
                                En(nameof(Strings.TableDoc_Sheet_Summary)),
                                $"A{TableDefinitionDocumentLayout.SummaryDbmsRow}",
                                $"{En(nameof(Strings.TableDoc_Cover_TargetDbms))}: sqlite"
                            )
                        ),
                    nameof(DiagramExportFormat.Excel)
                ),
            // TableDefinitionHtmlExporterTests.Build_EscapesUserData
            Shape("html-escapes-user-data", "Build: ユーザーデータがエスケープされる")
                .Build(() =>
                    new ErDiagram
                    {
                        Entities =
                        {
                            new Entity
                            {
                                TableName = "A<B>&\"C\"",
                                Description = "<script>alert(1)</script>",
                                Columns =
                                {
                                    new Column { Name = "Id", DataType = "int" },
                                },
                            },
                        },
                    }
                )
                .Expect(
                    e =>
                        // 生の危険文字列は現れず、エスケープ済みの表現が現れる
                        e.Output.Absent("<script>")
                            .Absent("A<B>")
                            .Contains("&lt;script&gt;alert(1)&lt;/script&gt;")
                            .Contains("A&lt;B&gt;&amp;&quot;C&quot;"),
                    nameof(DiagramExportFormat.Html)
                ),
            // TableDefinitionHtmlExporterTests.Build_ProducesUniqueAnchorIds
            Shape("html-unique-anchor-ids", "Build: アンカー id が一意になる")
                .Build(() =>
                    new ErDiagram
                    {
                        Entities =
                        {
                            OneColumnEntity("A B"),
                            OneColumnEntity("A_B"),
                            OneColumnEntity("AB"),
                            OneColumnEntity("a b"),
                        },
                    }
                )
                .Expect(
                    e =>
                        // すべての id 属性値を抽出し、table-1〜4 を含み重複がない
                        e.Output.That(
                            "id 属性値が table-1〜table-4 を含み、すべて相異なる",
                            lines =>
                            {
                                var ids = Regex
                                    .Matches(lines[0], "id=\"([^\"]+)\"")
                                    .Select(match => match.Groups[1].Value)
                                    .ToList();

                                return new[] { "table-1", "table-2", "table-3", "table-4" }.All(
                                        ids.Contains
                                    )
                                    && ids.Distinct(StringComparer.Ordinal).Count() == ids.Count;
                            }
                        ),
                    nameof(DiagramExportFormat.Html)
                ),
            // TableDefinitionUniqueConstraintTests.Export_WithoutUniqueConstraints_KeepsExistingLabels
            Shape("html-without-unique-constraints", "一意制約が無い図のキー表記は PK / FK1 のみ")
                .Build(() =>
                {
                    var diagram = ExcelCategoryProductUniqueDiagram();
                    diagram
                        .Entities.Single(entity => entity.TableName == "Product")
                        .UniqueConstraints.Clear();
                    return diagram;
                })
                .Expect(
                    e => e.Output.Contains("<td>PK</td>").Contains("<td>FK1</td>").Absent("UQ"),
                    nameof(DiagramExportFormat.Html)
                ),
        ];

    /// <summary>
    /// 素の取込は定義書が運ばない計算列・採番列・一意制約の順序と名前・主キーの順序を落とす
    /// （TableDefinitionRoundTripTests のマージ取込の対照。マージ取込が引き継ぐことはマージの行 merge-excel-* が見る）
    /// </summary>
    private static IEnumerable<ScenarioRow<FormatShape>> TableDefinitionPlainImportRows() =>
        [
            Shape(
                    "excel-plain-import-drops-computed-and-unique-order-name",
                    "Excel の素の取込: 計算列の指定と一意制約の順序・名前は落ちる"
                )
                .Build(ExcelComputedAndUniqueDiagram)
                .Expect(
                    e =>
                        e.Imported.Single("Structure column orders.a")
                            .None("Computed orders.a")
                            .That(
                                "1 つ目の一意制約は名前なしで、構成列が a, b の順",
                                lines =>
                                    lines.FirstOrDefault(line =>
                                        line.StartsWith("UniqueOrder ", StringComparison.Ordinal)
                                    ) == "UniqueOrder orders(a,b) = a,b"
                                    && lines.FirstOrDefault(line =>
                                        line.StartsWith("UniqueName ", StringComparison.Ordinal)
                                    ) == "UniqueName orders(a,b) = -"
                            ),
                    nameof(DiagramExportFormat.Excel)
                ),
            Shape("excel-plain-import-drops-identity", "Excel の素の取込: 採番列の指定は落ちる")
                .Build(ExcelIdentityDiagram)
                .Expect(
                    e =>
                        e.Imported.Single("Structure column orders.b")
                            .None("IdentityNonKey orders.b"),
                    nameof(DiagramExportFormat.Excel)
                ),
            Shape(
                    "excel-plain-import-drops-primary-key-order",
                    "Excel の素の取込: 複合主キーの順序は列の並び順へ戻る"
                )
                .Build(ExcelReorderedPrimaryKeyDiagram)
                .Expect(
                    e =>
                    {
                        e.Imported.Single("PrimaryKeyOrder orders = a,b");
                        e.ImportedDiagram.Add(
                            (
                                "orders の主キーの順序を明示しない（PrimaryKeyColumnIds が空）",
                                d =>
                                    d.Entities.Single(entity =>
                                        entity.TableName == "orders"
                                    ).PrimaryKeyColumnIds.Count == 0
                            )
                        );
                    },
                    nameof(DiagramExportFormat.Excel)
                ),
        ];

    /// <summary>定義書の言語（en）で固定文言を引く</summary>
    private static string En(string key) =>
        Strings.ResourceManager.GetString(key, ExcelAdapter.DocumentCulture)!;

    /// <summary>Excel の出力の写しのセルの行（<c>{シート}!{セル}={値}</c> と行末の改行）</summary>
    private static string ExcelCell(string sheet, string address, string value) =>
        $"{sheet}!{address}={value}\n";

    /// <summary>Excel の出力の写しに現れるシートの並びが期待と一致する</summary>
    private static Func<IReadOnlyList<string>, bool> SheetOrderIs(params string[] expected) =>
        lines =>
            lines[0]
                .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(line => line[..line.IndexOf('!', StringComparison.Ordinal)])
                .Distinct(StringComparer.Ordinal)
                .SequenceEqual(expected, StringComparer.Ordinal);

    /// <summary>Excel の出力の写しで、セルが無いか値が空</summary>
    private static Func<IReadOnlyList<string>, bool> CellIsEmpty(string sheet, string address) =>
        lines =>
            !lines[0]
                .Split('\n')
                .Any(line =>
                    line.StartsWith($"{sheet}!{address}=", StringComparison.Ordinal)
                    && line.Length > $"{sheet}!{address}=".Length
                );

    /// <summary>Excel の出力の写しで、シートの指定の行の最後のセルの列番号が期待と一致する</summary>
    private static Func<IReadOnlyList<string>, bool> LastUsedColumnIs(
        string sheet,
        int row,
        int column
    ) =>
        lines =>
            lines[0]
                .Split('\n')
                .Select(line => Regex.Match(line, $"^{Regex.Escape(sheet)}!([A-Z]+){row}="))
                .Where(match => match.Success)
                .Select(match => XLHelper.GetColumnNumberFromLetter(match.Groups[1].Value))
                .DefaultIfEmpty(0)
                .Max() == column;

    /// <summary>HTML の開始タグから直後の終了タグまでの区間（セクションは入れ子にならない前提。無ければ null）</summary>
    private static string? HtmlSection(string html, string startTag, string endTag)
    {
        var start = html.IndexOf(startTag, StringComparison.Ordinal);

        if (start < 0)
        {
            return null;
        }

        var end = html.IndexOf(endTag, start, StringComparison.Ordinal);
        return end > start ? html.Substring(start, end - start + endTag.Length) : null;
    }

    /// <summary>HTML の区間の見出しセル（<c>&lt;th&gt;</c>）の数が期待と一致する</summary>
    private static Func<IReadOnlyList<string>, bool> HtmlHeaderCellCount(
        string startTag,
        string endTag,
        int count
    ) =>
        lines =>
            HtmlSection(lines[0], startTag, endTag) is { } section
            && Regex.Matches(section, "<th>").Count == count;

    /// <summary>1 列（Id int）だけのエンティティ（TableDefinitionHtmlExporterTests.MakeEntity）</summary>
    private static Entity OneColumnEntity(string tableName) =>
        new()
        {
            TableName = tableName,
            Columns =
            {
                new Column { Name = "Id", DataType = "int" },
            },
        };

    /// <summary>
    /// User（説明・メモ）/ Order の 2 テーブルと 1 リレーション（Cascade・制約名）の図
    /// （TableDefinitionHtmlExporterTests.BuildSampleDiagram と、TableDefinitionDocumentExporterTests.BuildSampleDiagram が MainViewModel で組む図と同じ形）
    /// </summary>
    private static ErDiagram TableDocUserOrderDiagram()
    {
        var userId = new Column
        {
            Name = "Id",
            DataType = "int",
            IsPrimaryKey = true,
            IsNullable = false,
            Description = "主キー",
        };
        var user = new Entity
        {
            TableName = "User",
            Description = "利用者",
            Memo = "業務利用",
            Columns =
            {
                userId,
                new Column
                {
                    Name = "Name",
                    DataType = "nvarchar(50)",
                    IsNullable = false,
                    Description = "氏名",
                },
            },
        };
        var orderUserId = new Column
        {
            Name = "UserId",
            DataType = "int",
            IsForeignKey = true,
            IsNullable = false,
        };
        var order = new Entity
        {
            TableName = "Order",
            Columns =
            {
                new Column
                {
                    Name = "Id",
                    DataType = "int",
                    IsPrimaryKey = true,
                    IsNullable = false,
                },
                orderUserId,
            },
        };

        return new ErDiagram
        {
            Entities = { user, order },
            Relationships =
            {
                new Relationship
                {
                    SourceEntityId = user.Id,
                    TargetEntityId = order.Id,
                    Type = RelationshipType.OneToMany,
                    ColumnPairs = [new(userId.Id, orderUserId.Id)],
                    ConstraintName = "FK_Order_User",
                    OnDelete = ForeignKeyReferentialAction.Cascade,
                },
            },
        };
    }

    /// <summary>
    /// orders.a を計算列にし、構成列の順序が列の並びと違う名前付き一意制約 (b, a) と名前なしの (customer_id) を持つ図
    /// （TableDefinitionRoundTripTests.BuildCurrentWithComputedAndUnique）
    /// </summary>
    internal static ErDiagram ExcelComputedAndUniqueDiagram()
    {
        var diagram = ExcelCustomersOrdersDiagram();
        var orders = diagram.Entities[1];
        orders.Columns[2].IsComputed = true;
        orders.UniqueConstraints.Add(
            new UniqueConstraint
            {
                Name = "UQ_orders_b_a",
                ColumnIds = [orders.Columns[3].Id, orders.Columns[2].Id],
            }
        );
        orders.UniqueConstraints.Add(new UniqueConstraint { ColumnIds = [orders.Columns[1].Id] });

        return diagram;
    }

    /// <summary>orders.b を採番列にした図（TableDefinitionRoundTripTests.MergeImport_InheritsIdentity の現在図）</summary>
    internal static ErDiagram ExcelIdentityDiagram()
    {
        var diagram = ExcelCustomersOrdersDiagram();
        diagram.Entities[1].Columns.Single(column => column.Name == "b").IsIdentity = true;
        return diagram;
    }

    /// <summary>
    /// orders の主キーを (b, a)＝列の並びと逆の順序で明示した図（TableDefinitionRoundTripTests.BuildCurrentWithReorderedPrimaryKey）
    /// </summary>
    internal static ErDiagram ExcelReorderedPrimaryKeyDiagram()
    {
        var diagram = ExcelCustomersOrdersDiagram();
        var orders = diagram.Entities[1];
        var a = orders.Columns.Single(column => column.Name == "a");
        var b = orders.Columns.Single(column => column.Name == "b");
        orders.Columns[0].IsPrimaryKey = false;
        a.IsPrimaryKey = true;
        a.IsNullable = false;
        b.IsPrimaryKey = true;
        b.IsNullable = false;
        orders.PrimaryKeyColumnIds = [b.Id, a.Id];

        return diagram;
    }

    /// <summary>列の名前を Id で引く（引けなければ ?）</summary>
    private static string ColumnName(ErDiagram diagram, Guid id) =>
        diagram.Entities.SelectMany(entity => entity.Columns).FirstOrDefault(c => c.Id == id)?.Name
        ?? "?";

    /// <summary>
    /// Category（親）と Product（子・FK）を持つサンプル図（TableDefinitionDocumentImporterTests.BuildSampleDiagram が MainViewModel で組む図と同じ形）
    /// </summary>
    internal static ErDiagram ExcelCategoryProductDiagram()
    {
        var parent = new Entity
        {
            TableName = "Category",
            Description = "カテゴリ",
            Memo = "分類用",
            Columns =
            {
                new Column
                {
                    Name = "CategoryId",
                    DataType = "int",
                    IsPrimaryKey = true,
                    IsNullable = false,
                    Description = "主キー",
                },
                new Column
                {
                    Name = "CategoryName",
                    DataType = "nvarchar(50)",
                    IsNullable = false,
                    Description = "名称",
                },
            },
        };
        var child = new Entity
        {
            TableName = "Product",
            Description = "商品",
            Columns =
            {
                new Column
                {
                    Name = "ProductId",
                    DataType = "int",
                    IsPrimaryKey = true,
                    IsNullable = false,
                },
                new Column
                {
                    Name = "CategoryId",
                    DataType = "int",
                    IsForeignKey = true,
                    IsNullable = false,
                },
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
                    ColumnPairs = [new(parent.Columns[0].Id, child.Columns[1].Id)],
                    ConstraintName = "FK_Product_Category",
                    OnDelete = ForeignKeyReferentialAction.Cascade,
                    OnUpdate = ForeignKeyReferentialAction.NoAction,
                },
            },
        };
    }

    /// <summary>親 customers(id) と子 orders(id, customer_id, a, b) の図（TableDefinitionRoundTripTests.BuildDiagram）</summary>
    internal static ErDiagram ExcelCustomersOrdersDiagram()
    {
        var customers = new Entity
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
        var orders = new Entity
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
                new Column { Name = "a", DataType = "int" },
                new Column { Name = "b", DataType = "int" },
            ],
        };

        return new ErDiagram { Entities = [customers, orders], TargetDbms = "sqlserver" };
    }

    /// <summary>複合 PK の親 TenantRegion と、複合 FK＋単一 FK を持つ子 TenantUser の図（TableDefinitionCompositeForeignKeyTests.BuildDiagram）</summary>
    internal static ErDiagram ExcelTenantDiagram()
    {
        var tenantId = new Column
        {
            Name = "TenantId",
            DataType = "int",
            IsPrimaryKey = true,
            IsNullable = false,
        };
        var regionCode = new Column
        {
            Name = "RegionCode",
            DataType = "nvarchar(10)",
            IsPrimaryKey = true,
            IsNullable = false,
        };
        var parent = new Entity { TableName = "TenantRegion", Columns = { tenantId, regionCode } };

        var planId = new Column
        {
            Name = "PlanId",
            DataType = "int",
            IsPrimaryKey = true,
            IsNullable = false,
        };
        var plan = new Entity { TableName = "Plan", Columns = { planId } };

        var userId = new Column
        {
            Name = "TenantUserId",
            DataType = "int",
            IsPrimaryKey = true,
            IsNullable = false,
        };
        var tenantRef = new Column
        {
            Name = "TenantRef",
            DataType = "int",
            IsForeignKey = true,
            IsNullable = false,
        };
        var regionRef = new Column
        {
            Name = "RegionRef",
            DataType = "nvarchar(10)",
            IsForeignKey = true,
            IsNullable = false,
        };
        var planRef = new Column
        {
            Name = "PlanId",
            DataType = "int",
            IsForeignKey = true,
            IsNullable = false,
        };
        var child = new Entity
        {
            TableName = "TenantUser",
            Columns = { userId, tenantRef, regionRef, planRef },
        };

        return new ErDiagram
        {
            Entities = { parent, plan, child },
            Relationships =
            {
                new Relationship
                {
                    SourceEntityId = parent.Id,
                    TargetEntityId = child.Id,
                    Type = RelationshipType.OneToMany,
                    ColumnPairs =
                    [
                        new(tenantId.Id, tenantRef.Id),
                        new(regionCode.Id, regionRef.Id),
                    ],
                    ConstraintName = "FK_TenantUser_TenantRegion",
                },
                new Relationship
                {
                    SourceEntityId = plan.Id,
                    TargetEntityId = child.Id,
                    Type = RelationshipType.OneToMany,
                    ColumnPairs = [new(planId.Id, planRef.Id)],
                    ConstraintName = "FK_TenantUser_Plan",
                },
            },
        };
    }

    /// <summary>同じ親子の間に 2 本の外部キー（配送先・請求先）を持つ図（TableDefinitionCompositeForeignKeyTests.BuildTwoForeignKeysBetweenSameTablesDiagram）</summary>
    internal static ErDiagram ExcelAddressOrdersDiagram()
    {
        var addressId = new Column
        {
            Name = "AddressId",
            DataType = "int",
            IsPrimaryKey = true,
            IsNullable = false,
        };
        var address = new Entity { TableName = "Address", Columns = { addressId } };

        var orderId = new Column
        {
            Name = "OrderId",
            DataType = "int",
            IsPrimaryKey = true,
            IsNullable = false,
        };
        var shipTo = new Column
        {
            Name = "ShipToAddressId",
            DataType = "int",
            IsForeignKey = true,
            IsNullable = false,
        };
        var billTo = new Column
        {
            Name = "BillToAddressId",
            DataType = "int",
            IsForeignKey = true,
            IsNullable = false,
        };
        var order = new Entity { TableName = "Orders", Columns = { orderId, shipTo, billTo } };

        return new ErDiagram
        {
            Entities = { address, order },
            Relationships =
            {
                new Relationship
                {
                    SourceEntityId = address.Id,
                    TargetEntityId = order.Id,
                    Type = RelationshipType.OneToMany,
                    ColumnPairs = [new(addressId.Id, shipTo.Id)],
                    ConstraintName = "FK_Orders_ShipToAddress",
                },
                new Relationship
                {
                    SourceEntityId = address.Id,
                    TargetEntityId = order.Id,
                    Type = RelationshipType.OneToMany,
                    ColumnPairs = [new(addressId.Id, billTo.Id)],
                    ConstraintName = "FK_Orders_BillToAddress",
                },
            },
        };
    }

    /// <summary>Category（親）と Product（子・FK＋一意制約 2 件）の図（TableDefinitionUniqueConstraintTests.BuildDiagram）</summary>
    internal static ErDiagram ExcelCategoryProductUniqueDiagram()
    {
        var categoryId = new Column
        {
            Name = "CategoryId",
            DataType = "int",
            IsPrimaryKey = true,
            IsNullable = false,
        };
        var category = new Entity { TableName = "Category", Columns = { categoryId } };

        var productId = new Column
        {
            Name = "ProductId",
            DataType = "int",
            IsPrimaryKey = true,
            IsNullable = false,
        };
        var sku = new Column
        {
            Name = "Sku",
            DataType = "nvarchar(20)",
            IsNullable = false,
        };
        var productCategoryId = new Column
        {
            Name = "CategoryId",
            DataType = "int",
            IsForeignKey = true,
            IsNullable = false,
        };
        var code = new Column
        {
            Name = "Code",
            DataType = "nvarchar(20)",
            IsNullable = false,
        };
        var product = new Entity
        {
            TableName = "Product",
            Columns = { productId, sku, productCategoryId, code },
            UniqueConstraints =
            {
                new UniqueConstraint { ColumnIds = [sku.Id] },
                new UniqueConstraint
                {
                    Name = "UQ_Product_Category_Code",
                    ColumnIds = [productCategoryId.Id, code.Id],
                },
            },
        };

        return new ErDiagram
        {
            Entities = { category, product },
            Relationships =
            {
                new Relationship
                {
                    SourceEntityId = category.Id,
                    TargetEntityId = product.Id,
                    Type = RelationshipType.OneToMany,
                    ColumnPairs = [new(categoryId.Id, productCategoryId.Id)],
                    ConstraintName = "FK_Product_Category",
                },
            },
        };
    }
}
