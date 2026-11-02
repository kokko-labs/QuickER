using System.Collections.Generic;
using AwesomeAssertions;
using QuickER.Model;
using QuickER.Provider;
using QuickER.Tests.TestSupport;

namespace QuickER.Tests.Provider;

/// <summary>
/// <see cref="SchemaDiffService"/> の差分の項目が図のどのオブジェクトを運ぶか（オブジェクトの同一性）を検証するテストクラス
/// </summary>
/// <remarks>
/// 差分の項目の種類・選択・説明などの中身は、同期計画の期待の行（Integration の <c>SyncPlanExpectationRows.Diff*.cs</c>）が
/// 持つ。期待の行の書式はオブジェクトの同一性を書けないので、項目が図のオブジェクトそのもの（複製でない）を指すことと、
/// 差分が図を書き換えないことだけをここで確かめる。
/// </remarks>
public class SchemaDiffServiceTests
{
    /// <summary>名前と (列名, 型, 主キー) の組からテスト用エンティティを生成する</summary>
    private static Entity Tbl(string name, params (string Name, string Type, bool Pk)[] cols)
    {
        var e = new Entity { TableName = name };

        foreach (var c in cols)
        {
            e.Columns.Add(
                new Column
                {
                    Name = c.Name,
                    DataType = c.Type,
                    IsPrimaryKey = c.Pk,
                    IsNullable = !c.Pk,
                }
            );
        }

        return e;
    }

    /// <summary>オブジェクトの同一性を確かめる形</summary>
    public static TheoryData<string> DiagramObjectShapes() =>
        new()
        {
            "reorder-native",
            "primary-key-added",
            "same-spelling-alter-column",
            "case-only-live-spelling",
        };

    /// <summary>
    /// 差分の項目が図のオブジェクトそのものを運び（綴りを揃えた表・列は DB の綴りの複製）、図そのものを書き換えないことを
    /// 形ごとに検証する
    /// </summary>
    /// <remarks>
    /// reorder-native・primary-key-added：項目の <see cref="SchemaDiffItem.Entity"/> は図の表そのもの。
    /// same-spelling-alter-column：揃える組が無い表の列の変更は、図の表と列のオブジェクトそのもの。
    /// case-only-live-spelling：大文字小文字だけ違う綴りで組んだ列の変更の列は DB の綴りの複製で、図の表・列の名前は変わらない。
    /// </remarks>
    [Theory(DisplayName = "差分の項目は図のオブジェクトを運び、図そのものは書き換えない")]
    [MemberData(nameof(DiagramObjectShapes))]
    public void Items_CarryDiagramObjects(string shape)
    {
        switch (shape)
        {
            case "reorder-native":
            {
                var live = Tbl(
                    "Customer",
                    ("Id", "int", true),
                    ("Name", "nvarchar(50)", false),
                    ("Email", "nvarchar(50)", false)
                );
                var target = Tbl(
                    "Customer",
                    ("Id", "int", true),
                    ("Email", "nvarchar(50)", false),
                    ("Name", "nvarchar(50)", false)
                );

                new SchemaDiffService()
                    .Compute(
                        [live],
                        new List<Relationship>(),
                        [target],
                        new List<Relationship>(),
                        TestDialectCapabilities.Neutral with
                        {
                            ColumnReorder = ColumnReorderMode.Native,
                        }
                    )
                    .Items.Should()
                    .ContainSingle(i => i.Kind == SchemaDiffKind.ReorderColumns)
                    .Which.Entity.Should()
                    .BeSameAs(target);
                break;
            }

            case "primary-key-added":
            {
                var live = Tbl("Customer", ("Id", "int", false), ("Name", "int", false));
                live.Columns.ForEach(c => c.IsNullable = false);
                var target = Tbl("Customer", ("Id", "int", true), ("Name", "int", false));
                target.Columns.ForEach(c => c.IsNullable = false);

                new SchemaDiffService()
                    .Compute(
                        [live],
                        new List<Relationship>(),
                        [target],
                        new List<Relationship>(),
                        TestDialectCapabilities.Neutral
                    )
                    .Items.Should()
                    .ContainSingle()
                    .Which.Entity.Should()
                    .BeSameAs(target);
                break;
            }

            case "same-spelling-alter-column":
            {
                var live = Tbl("orders", ("id", "int", true), ("name", "varchar(50)", false));
                var target = Tbl("orders", ("id", "int", true), ("name", "varchar(100)", false));

                var alter = new SchemaDiffService()
                    .Compute([live], [], [target], [], TestDialectCapabilities.Neutral)
                    .Items.Should()
                    .ContainSingle(i => i.Kind == SchemaDiffKind.AlterColumn)
                    .Which;
                alter.Entity.Should().BeSameAs(target);
                alter.Column.Should().BeSameAs(target.Columns[1]);
                break;
            }

            case "case-only-live-spelling":
            {
                var liveOrders = Tbl(
                    "ORDERS",
                    ("ID", "NUMBER(10)", true),
                    ("NAME", "VARCHAR2(50)", false)
                );
                var liveCustomers = Tbl("CUSTOMERS", ("ID", "NUMBER(10)", true));
                var targetOrders = Tbl(
                    "Orders",
                    ("Id", "NUMBER(10)", true),
                    ("Name", "VARCHAR2(100)", false),
                    ("CustomerId", "NUMBER(10)", false),
                    ("NewCol", "NUMBER(10)", false)
                );
                var targetCustomers = Tbl("Customers", ("Id", "NUMBER(10)", true));
                var targetItems = Tbl("Items", ("Id", "NUMBER(10)", true));
                targetOrders.Description = "orders";
                var relationship = new Relationship
                {
                    SourceEntityId = targetCustomers.Id,
                    TargetEntityId = targetOrders.Id,
                    Type = RelationshipType.OneToMany,
                    ColumnPairs = [new(targetCustomers.Columns[0].Id, targetOrders.Columns[2].Id)],
                    ConstraintName = "FK_Orders_Customers",
                };

                var alter = new SchemaDiffService()
                    .Compute(
                        [liveOrders, liveCustomers],
                        [],
                        [targetOrders, targetCustomers, targetItems],
                        [relationship],
                        TestDialectCapabilities.Neutral
                    )
                    .Items.Should()
                    .ContainSingle(i => i.Kind == SchemaDiffKind.AlterColumn)
                    .Which;
                alter.Column!.Name.Should().Be("NAME");

                // 図そのものは書き換えない
                targetOrders.TableName.Should().Be("Orders");
                targetOrders.Columns[1].Name.Should().Be("Name");
                break;
            }

            default:
                throw new System.ArgumentOutOfRangeException(nameof(shape), shape, null);
        }
    }
}
