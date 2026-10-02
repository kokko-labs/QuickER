using System;
using System.Globalization;
using System.Linq;
using AwesomeAssertions;
using ClosedXML.Excel;
using QuickER.Model;
using QuickER.Services;
using static QuickER.Tests.Gui.Services.ImportedConstraintNameAssertions;

namespace QuickER.Tests.Gui.Services;

/// <summary>
/// Excel 定義書の取込（<see cref="TableDefinitionDocumentImporter"/>）が、制約名欄が空のリレーションの
/// 外部キー名を決める規則を検証する
/// </summary>
/// <remarks>
/// 制約名欄が空のリレーションは既定名 <c>FK_{子}_{親}</c> で DDL に出るため、同じテーブルの組に 2 行あると
/// 同じ名前が並び、DDL の 2 本目が失敗する（重複の検査は構成列まで比べるので、列が違えば 2 行とも通る）。
/// 既定名がふさがった分だけ一意な名前を付け、制約名欄に書かれた名前は変えないことを、
/// 実際に生成した SQL Server の DDL で確かめる。
/// </remarks>
public class TableDefinitionConstraintNameTests
{
    /// <summary>図を定義書へ書き出して読み戻す（制約名欄を書き換える処理を挟める）</summary>
    private static ErDiagram RoundTrip(
        ErDiagram source,
        Action<IXLWorksheet>? editRelationshipSheet = null
    )
    {
        using var workbook = TableDefinitionDocumentExporter.BuildWorkbook(
            source,
            CultureInfo.InvariantCulture
        );

        if (editRelationshipSheet is not null)
        {
            workbook
                .DefinedNames.TryGetValue(
                    TableDefinitionDocumentLayout.RelationshipsDefinedName,
                    out var defined
                )
                .Should()
                .BeTrue();
            editRelationshipSheet(defined!.Ranges.First().Worksheet);
        }

        return TableDefinitionDocumentImporter.Load(workbook);
    }

    /// <summary>リレーション一覧の制約名欄（2 列目）を、データ行の先頭から指定行数だけ空にする</summary>
    private static Action<IXLWorksheet> ClearConstraintNames(int rows) =>
        sheet =>
        {
            for (var offset = 0; offset < rows; offset++)
            {
                var cell = sheet.Cell(
                    TableDefinitionDocumentLayout.RelationshipDataStartRow + offset,
                    2
                );
                cell.GetString().Should().NotBeEmpty("書き換える前は名前が入っている");
                cell.Value = string.Empty;
            }
        };

    [Fact(
        DisplayName = "同じテーブルの組の名前の無いリレーション 2 本は、往復すると 2 本目だけが名前を得る"
    )]
    public void RoundTrip_SamePairTwiceWithoutNames_SecondGetsUniqueName()
    {
        var source = BuildCustomersAndOrders();
        AddLink(source, "bill_customer_id");
        AddLink(source, "ship_customer_id");

        var imported = RoundTrip(source);

        Describe(imported)
            .Should()
            .Equal(
                ("bill_customer_id", null),
                ("ship_customer_id", "FK_orders_customers_ship_customer_id")
            );
        ShouldHaveDistinctDdlConstraintNames(imported);
    }

    [Fact(
        DisplayName = "制約名欄を空にした手書きの定義書でも、同じ組の 2 行目に子の列名つきの名前を付ける"
    )]
    public void Import_HandWrittenEmptyNames_SecondGetsUniqueName()
    {
        var source = BuildCustomersAndOrders();
        AddLink(source, "bill_customer_id", "fk_bill");
        AddLink(source, "ship_customer_id", "fk_ship");

        var imported = RoundTrip(source, ClearConstraintNames(2));

        Describe(imported)
            .Should()
            .Equal(
                ("bill_customer_id", null),
                ("ship_customer_id", "FK_orders_customers_ship_customer_id")
            );
        ShouldHaveDistinctDdlConstraintNames(imported);
    }

    [Fact(DisplayName = "名前の無いリレーションが 1 本だけなら名前を持たないまま")]
    public void RoundTrip_SingleUnnamed_StaysUnnamed()
    {
        var source = BuildCustomersAndOrders();
        AddLink(source, "bill_customer_id");

        var imported = RoundTrip(source);

        Describe(imported).Should().Equal(("bill_customer_id", null));
        ShouldHaveDistinctDdlConstraintNames(imported);
    }

    [Fact(
        DisplayName = "制約名欄の名前が既定名をふさいでいれば、名前の無い行に名前を付け、書かれた名前は変えない"
    )]
    public void Import_NamedRowTakesDefault_UnnamedIsRenamed()
    {
        var source = BuildCustomersAndOrders();
        AddLink(source, "bill_customer_id");
        AddLink(source, "ship_customer_id", "FK_orders_customers");

        var imported = RoundTrip(source);

        Describe(imported)
            .Should()
            .Equal(
                ("bill_customer_id", "FK_orders_customers_bill_customer_id"),
                ("ship_customer_id", "FK_orders_customers")
            );
        ShouldHaveDistinctDdlConstraintNames(imported);
    }
}
