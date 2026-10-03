using AwesomeAssertions;
using QuickER.Services;
using static QuickER.Tests.Gui.Services.ImportedConstraintNameAssertions;

namespace QuickER.Tests.Gui.Services;

/// <summary>
/// DBML 取込（<see cref="DbmlImporter"/>）が名前の無い <c>Ref</c> の外部キー名を決める規則を検証する
/// </summary>
/// <remarks>
/// 名前の無い <c>Ref</c> は既定名 <c>FK_{子}_{親}</c> で DDL に出るため、同じテーブルの組に 2 本あると
/// 同じ名前が並び、DDL の 2 本目が失敗する。既定名がふさがった分だけ一意な名前を付け、
/// 名前付き <c>Ref</c> の名前は変えないことを、実際に生成した SQL Server の DDL で確かめる。
/// </remarks>
public class DbmlConstraintNameTests
{
    /// <summary>customers・orders の 2 テーブルと、与えた Ref 行の DBML</summary>
    private static string BuildText(params string[] refLines) =>
        string.Join(
            "\n",
            [
                "Table customers {",
                "  id int [pk]",
                "}",
                "",
                "Table orders {",
                "  id int [pk]",
                "  bill_customer_id int",
                "  ship_customer_id int",
                "}",
                "",
                .. refLines,
            ]
        );

    [Fact(
        DisplayName = "同じテーブルの組に名前の無い Ref が 2 本あれば、2 本目に子の列名つきの名前を付ける"
    )]
    public void Import_SamePairTwiceWithoutNames_SecondGetsUniqueName()
    {
        var diagram = DbmlImporter.Parse(
            BuildText(
                "Ref: customers.id < orders.bill_customer_id",
                "Ref: customers.id < orders.ship_customer_id"
            )
        );

        Describe(diagram)
            .Should()
            .Equal(
                ("bill_customer_id", null),
                ("ship_customer_id", "FK_orders_customers_ship_customer_id")
            );
        ShouldHaveDistinctDdlConstraintNames(diagram);
    }

    [Fact(DisplayName = "名前の無い Ref が 1 本だけなら名前を持たないまま")]
    public void Import_SingleUnnamedRef_StaysUnnamed()
    {
        var diagram = DbmlImporter.Parse(BuildText("Ref: customers.id < orders.bill_customer_id"));

        Describe(diagram).Should().Equal(("bill_customer_id", null));
        ShouldHaveDistinctDdlConstraintNames(diagram);
    }

    [Fact(
        DisplayName = "名前付き Ref が既定名をふさいでいれば、名前の無い Ref に名前を付け、名前付きの名前は変えない"
    )]
    public void Import_NamedRefTakesDefault_UnnamedIsRenamed()
    {
        var diagram = DbmlImporter.Parse(
            BuildText(
                "Ref: customers.id < orders.bill_customer_id",
                "Ref FK_orders_customers: customers.id < orders.ship_customer_id"
            )
        );

        Describe(diagram)
            .Should()
            .Equal(
                ("bill_customer_id", "FK_orders_customers_bill_customer_id"),
                ("ship_customer_id", "FK_orders_customers")
            );
        ShouldHaveDistinctDdlConstraintNames(diagram);
    }

    [Fact(DisplayName = "名前付き Ref の名前は、名前の無い Ref と並んでもそのまま")]
    public void Import_NamedRefs_KeepTheirNames()
    {
        var diagram = DbmlImporter.Parse(
            BuildText(
                "Ref fk_bill: customers.id < orders.bill_customer_id",
                "Ref: customers.id < orders.ship_customer_id"
            )
        );

        Describe(diagram)
            .Should()
            .Equal(("bill_customer_id", "fk_bill"), ("ship_customer_id", null));
        ShouldHaveDistinctDdlConstraintNames(diagram);
    }

    [Fact(
        DisplayName = "名前の無いリレーション 1 本は書き出して読み戻しても名前を持たず、2 本目だけが名前を得る"
    )]
    public void RoundTrip_UnnamedRelationships()
    {
        var source = BuildCustomersAndOrders();
        AddLink(source, "bill_customer_id");
        AddLink(source, "ship_customer_id");

        var imported = DbmlImporter.Parse(DbmlExporter.Build(source));

        Describe(imported)
            .Should()
            .Equal(
                ("bill_customer_id", null),
                ("ship_customer_id", "FK_orders_customers_ship_customer_id")
            );
        ShouldHaveDistinctDdlConstraintNames(imported);
    }
}
