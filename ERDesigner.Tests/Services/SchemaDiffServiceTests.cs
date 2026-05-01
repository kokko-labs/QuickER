using System.Collections.Generic;
using ERDesigner.Models;
using ERDesigner.Services;
using FluentAssertions;

namespace ERDesigner.Tests.Services;

/// <summary>
/// <see cref="SchemaDiffService"/> の差分計算ロジックのテスト。
/// </summary>
public class SchemaDiffServiceTests
{
    private static Entity Tbl(string name, params (string Name, string Type, bool Pk)[] cols)
    {
        var e = new Entity { TableName = name };
        foreach (var c in cols)
            e.Columns.Add(new Column { Name = c.Name, DataType = c.Type, IsPrimaryKey = c.Pk });
        return e;
    }

    [Fact(DisplayName = "DB 側に無いテーブルは AddTable になる")]
    public void NewTable_AddTable()
    {
        var live = new List<Entity>();
        var target = new List<Entity> { Tbl("Customer", ("Id", "int", true), ("Name", "nvarchar(50)", false)) };
        var diff = new SchemaDiffService().Compute(live, new List<Relationship>(), target, new List<Relationship>());
        diff.Items.Should().ContainSingle(i => i.Kind == SchemaDiffKind.AddTable && i.TableName == "Customer");
    }

    [Fact(DisplayName = "DB 側に無い列は AddColumn になる")]
    public void NewColumn_AddColumn()
    {
        var live = new List<Entity> { Tbl("Customer", ("Id", "int", true)) };
        var target = new List<Entity> { Tbl("Customer", ("Id", "int", true), ("Name", "nvarchar(50)", false)) };
        var diff = new SchemaDiffService().Compute(live, new List<Relationship>(), target, new List<Relationship>());
        diff.Items.Should().ContainSingle(i => i.Kind == SchemaDiffKind.AddColumn && i.ColumnName == "Name");
    }

    [Fact(DisplayName = "型が変われば AlterColumn になり、既定では未選択")]
    public void TypeChange_AlterColumn_NotSelected()
    {
        var live = new List<Entity> { Tbl("Customer", ("Id", "int", true), ("Name", "nvarchar(50)", false)) };
        var target = new List<Entity> { Tbl("Customer", ("Id", "int", true), ("Name", "nvarchar(100)", false)) };
        var diff = new SchemaDiffService().Compute(live, new List<Relationship>(), target, new List<Relationship>());
        var alter = diff.Items.Should().ContainSingle(i => i.Kind == SchemaDiffKind.AlterColumn).Which;
        alter.IsSelected.Should().BeFalse();
    }

    [Fact(DisplayName = "ER 図側に無い列は DropColumn になり、既定では未選択")]
    public void RemovedColumn_DropColumn_NotSelected()
    {
        var live = new List<Entity> { Tbl("Customer", ("Id", "int", true), ("Old", "int", false)) };
        var target = new List<Entity> { Tbl("Customer", ("Id", "int", true)) };
        var diff = new SchemaDiffService().Compute(live, new List<Relationship>(), target, new List<Relationship>());
        var drop = diff.Items.Should().ContainSingle(i => i.Kind == SchemaDiffKind.DropColumn).Which;
        drop.ColumnName.Should().Be("Old");
        drop.IsSelected.Should().BeFalse();
    }

    [Fact(DisplayName = "ER 図側に無いテーブルは DropTable になる")]
    public void RemovedTable_DropTable()
    {
        var live = new List<Entity> { Tbl("Old", ("Id", "int", true)) };
        var target = new List<Entity>();
        var diff = new SchemaDiffService().Compute(live, new List<Relationship>(), target, new List<Relationship>());
        diff.Items.Should().ContainSingle(i => i.Kind == SchemaDiffKind.DropTable && i.TableName == "Old");
    }

    [Fact(DisplayName = "新しい外部キーは AddForeignKey になる")]
    public void NewRelationship_AddForeignKey()
    {
        var customer = Tbl("Customer", ("Id", "int", true));
        var order = Tbl("Order", ("Id", "int", true), ("Customer_Id", "int", false));
        var target = new List<Entity> { customer, order };
        var rel = new Relationship
        {
            SourceEntityId = customer.Id,
            TargetEntityId = order.Id,
            Type = RelationshipType.OneToMany
        };
        var liveCustomer = Tbl("Customer", ("Id", "int", true));
        var liveOrder = Tbl("Order", ("Id", "int", true), ("Customer_Id", "int", false));
        var live = new List<Entity> { liveCustomer, liveOrder };

        var diff = new SchemaDiffService().Compute(
            live, new List<Relationship>(),
            target, new List<Relationship> { rel });

        var fk = diff.Items.Should().ContainSingle(i => i.Kind == SchemaDiffKind.AddForeignKey).Which;
        fk.ColumnName.Should().Be("Customer_Id");
    }

    [Fact(DisplayName = "差分が無ければ Items は空")]
    public void Identical_NoDiff()
    {
        var live = new List<Entity> { Tbl("A", ("Id", "int", true)) };
        var target = new List<Entity> { Tbl("A", ("Id", "int", true)) };
        var diff = new SchemaDiffService().Compute(live, new List<Relationship>(), target, new List<Relationship>());
        diff.Items.Should().BeEmpty();
    }

    [Fact(DisplayName = "テーブル説明が変わると SetTableDescription になる")]
    public void TableDescriptionChanged_SetTableDescription()
    {
        var live = new List<Entity> { Tbl("Customer", ("Id", "int", true)) };
        live[0].Description = "古い説明";
        var target = new List<Entity> { Tbl("Customer", ("Id", "int", true)) };
        target[0].Description = "新しい顧客テーブル";

        var diff = new SchemaDiffService().Compute(live, new List<Relationship>(), target, new List<Relationship>());

        var item = diff.Items.Should().ContainSingle(i => i.Kind == SchemaDiffKind.SetTableDescription).Which;
        item.NewDescription.Should().Be("新しい顧客テーブル");
        item.OldDescription.Should().Be("古い説明");
        item.IsSelected.Should().BeTrue();
    }

    [Fact(DisplayName = "列の説明が変わると SetColumnDescription になる")]
    public void ColumnDescriptionChanged_SetColumnDescription()
    {
        var live = new List<Entity> { Tbl("Customer", ("Id", "int", true), ("Name", "nvarchar(50)", false)) };
        live[0].Columns[1].Description = "旧";
        var target = new List<Entity> { Tbl("Customer", ("Id", "int", true), ("Name", "nvarchar(50)", false)) };
        target[0].Columns[1].Description = "顧客名";

        var diff = new SchemaDiffService().Compute(live, new List<Relationship>(), target, new List<Relationship>());

        var item = diff.Items.Should().ContainSingle(i => i.Kind == SchemaDiffKind.SetColumnDescription).Which;
        item.ColumnName.Should().Be("Name");
        item.NewDescription.Should().Be("顧客名");
        item.OldDescription.Should().Be("旧");
    }

    [Fact(DisplayName = "説明が同じなら差分にならない")]
    public void SameDescription_NoDiff()
    {
        var live = new List<Entity> { Tbl("Customer", ("Id", "int", true)) };
        live[0].Description = "同じ説明";
        var target = new List<Entity> { Tbl("Customer", ("Id", "int", true)) };
        target[0].Description = "同じ説明";

        var diff = new SchemaDiffService().Compute(live, new List<Relationship>(), target, new List<Relationship>());
        diff.Items.Should().BeEmpty();
    }

    [Fact(DisplayName = "新規テーブル + 列の説明があれば SetTable/Column も同時に出力される")]
    public void NewTableWithDescriptions_EmitsAllSetDescriptions()
    {
        var live = new List<Entity>();
        var target = new List<Entity> { Tbl("Customer", ("Id", "int", true), ("Name", "nvarchar(50)", false)) };
        target[0].Description = "顧客マスタ";
        target[0].Columns[1].Description = "顧客名";

        var diff = new SchemaDiffService().Compute(live, new List<Relationship>(), target, new List<Relationship>());

        diff.Items.Should().ContainSingle(i => i.Kind == SchemaDiffKind.AddTable);
        diff.Items.Should().ContainSingle(i => i.Kind == SchemaDiffKind.SetTableDescription
                                            && i.NewDescription == "顧客マスタ");
        diff.Items.Should().ContainSingle(i => i.Kind == SchemaDiffKind.SetColumnDescription
                                            && i.ColumnName == "Name"
                                            && i.NewDescription == "顧客名");
    }
}
