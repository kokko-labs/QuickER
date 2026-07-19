using System.Collections.Generic;
using FluentAssertions;
using QuickER.Model;
using QuickER.Provider;
using QuickER.Services;
using QuickER.SqlServer;
using ProviderStrings = QuickER.Provider.Resources.Strings;

namespace QuickER.Tests.Provider;

/// <summary><see cref="SchemaDiffService"/> のテーブル・列・外部キー・説明の差分計算を検証するテストクラス</summary>
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

    /// <summary>DB に存在しないテーブルが AddTable として検出されることを検証する</summary>
    [Fact(DisplayName = "DB 側に無いテーブルは AddTable になる")]
    public void NewTable_AddTable()
    {
        var live = new List<Entity>();
        var target = new List<Entity>
        {
            Tbl("Customer", ("Id", "int", true), ("Name", "nvarchar(50)", false)),
        };

        var diff = new SchemaDiffService().Compute(
            live,
            new List<Relationship>(),
            target,
            new List<Relationship>()
        );
        diff.Items.Should()
            .ContainSingle(i => i.Kind == SchemaDiffKind.AddTable && i.TableName == "Customer");
    }

    /// <summary>DB に存在しない列が AddColumn として検出されることを検証する</summary>
    [Fact(DisplayName = "DB 側に無い列は AddColumn になる")]
    public void NewColumn_AddColumn()
    {
        var live = new List<Entity> { Tbl("Customer", ("Id", "int", true)) };
        var target = new List<Entity>
        {
            Tbl("Customer", ("Id", "int", true), ("Name", "nvarchar(50)", false)),
        };

        var diff = new SchemaDiffService().Compute(
            live,
            new List<Relationship>(),
            target,
            new List<Relationship>()
        );
        diff.Items.Should()
            .ContainSingle(i => i.Kind == SchemaDiffKind.AddColumn && i.ColumnName == "Name");
    }

    /// <summary>列の型変更が AlterColumn として検出され、既定では未選択であることを検証する</summary>
    [Fact(DisplayName = "型が変われば AlterColumn になり、既定では未選択")]
    public void TypeChange_AlterColumn_NotSelected()
    {
        var live = new List<Entity>
        {
            Tbl("Customer", ("Id", "int", true), ("Name", "nvarchar(50)", false)),
        };

        var target = new List<Entity>
        {
            Tbl("Customer", ("Id", "int", true), ("Name", "nvarchar(100)", false)),
        };

        var diff = new SchemaDiffService().Compute(
            live,
            new List<Relationship>(),
            target,
            new List<Relationship>()
        );
        var alter = diff
            .Items.Should()
            .ContainSingle(i => i.Kind == SchemaDiffKind.AlterColumn)
            .Which;
        alter.IsSelected.Should().BeFalse();
    }

    /// <summary>NULL 許容の変更が AlterColumn として検出され、説明に NULL 許容と記載されることを検証する</summary>
    [Fact(DisplayName = "NULL 許容が変われば AlterColumn になる")]
    public void NullabilityChange_AlterColumn_NotSelected()
    {
        var live = new List<Entity>
        {
            Tbl("Customer", ("Id", "int", true), ("Name", "nvarchar(50)", false)),
        };
        live[0].Columns[1].IsNullable = true;
        var target = new List<Entity>
        {
            Tbl("Customer", ("Id", "int", true), ("Name", "nvarchar(50)", false)),
        };
        target[0].Columns[1].IsNullable = false;

        var diff = new SchemaDiffService().Compute(
            live,
            new List<Relationship>(),
            target,
            new List<Relationship>()
        );
        var alter = diff
            .Items.Should()
            .ContainSingle(i => i.Kind == SchemaDiffKind.AlterColumn)
            .Which;
        alter.ColumnName.Should().Be("Name");
        alter.IsSelected.Should().BeFalse();

        // 製品コードと同じ resx キーから期待値を組み立て、カルチャに依らず完全一致で検証する
        // （型は変わらず NULL 許容のみ変化するため、変更点は NullableChange の 1 件のみ）
        var expectedNullableChange = string.Format(
            ProviderStrings.Diff_NullableChange,
            ProviderStrings.Diff_Nullable_Allow,
            ProviderStrings.Diff_Nullable_Deny
        );
        var expectedDescription =
            string.Format(ProviderStrings.Diff_ColumnChangePrefix, "Customer", "Name")
            + expectedNullableChange;
        alter.Description.Should().Be(expectedDescription);
    }

    /// <summary>ER 図に存在しない列が DropColumn として検出され、既定では未選択であることを検証する</summary>
    [Fact(DisplayName = "ER 図側に無い列は DropColumn になり、既定では未選択")]
    public void RemovedColumn_DropColumn_NotSelected()
    {
        var live = new List<Entity> { Tbl("Customer", ("Id", "int", true), ("Old", "int", false)) };
        var target = new List<Entity> { Tbl("Customer", ("Id", "int", true)) };
        var diff = new SchemaDiffService().Compute(
            live,
            new List<Relationship>(),
            target,
            new List<Relationship>()
        );
        var drop = diff
            .Items.Should()
            .ContainSingle(i => i.Kind == SchemaDiffKind.DropColumn)
            .Which;
        drop.ColumnName.Should().Be("Old");
        drop.IsSelected.Should().BeFalse();
    }

    /// <summary>ER 図に存在しないテーブルが DropTable として検出されることを検証する</summary>
    [Fact(DisplayName = "ER 図側に無いテーブルは DropTable になる")]
    public void RemovedTable_DropTable()
    {
        var live = new List<Entity> { Tbl("Old", ("Id", "int", true)) };
        var target = new List<Entity>();
        var diff = new SchemaDiffService().Compute(
            live,
            new List<Relationship>(),
            target,
            new List<Relationship>()
        );
        diff.Items.Should()
            .ContainSingle(i => i.Kind == SchemaDiffKind.DropTable && i.TableName == "Old");
    }

    /// <summary>ER 図のみに存在するリレーションが AddForeignKey として検出され、FK 列が解決されることを検証する</summary>
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
            Type = RelationshipType.OneToMany,
        };

        var liveCustomer = Tbl("Customer", ("Id", "int", true));
        var liveOrder = Tbl("Order", ("Id", "int", true), ("Customer_Id", "int", false));
        var live = new List<Entity> { liveCustomer, liveOrder };

        var diff = new SchemaDiffService().Compute(
            live,
            new List<Relationship>(),
            target,
            new List<Relationship> { rel }
        );

        var fk = diff
            .Items.Should()
            .ContainSingle(i => i.Kind == SchemaDiffKind.AddForeignKey)
            .Which;
        fk.ColumnName.Should().Be("Customer_Id");
    }

    /// <summary>同一テーブル間で FK 列が変わると DropForeignKey と AddForeignKey が両方出力されることを検証する</summary>
    [Fact(DisplayName = "同一テーブル間で FK 列が変わると DropForeignKey と AddForeignKey になる")]
    public void ForeignKeyColumnChanged_EmitsDropAndAdd()
    {
        var customerLive = Tbl("Customer", ("Id", "int", true));
        var orderLive = Tbl(
            "Order",
            ("Id", "int", true),
            ("CustomerId1", "int", false),
            ("CustomerId2", "int", false)
        );
        var liveRel = new Relationship
        {
            SourceEntityId = customerLive.Id,
            TargetEntityId = orderLive.Id,
            Type = RelationshipType.OneToMany,
            SourceColumnId = customerLive.Columns[0].Id,
            TargetColumnId = orderLive.Columns[1].Id,
            ConstraintName = "FK_Order_CustomerId1",
        };

        var customerTarget = Tbl("Customer", ("Id", "int", true));
        var orderTarget = Tbl(
            "Order",
            ("Id", "int", true),
            ("CustomerId1", "int", false),
            ("CustomerId2", "int", false)
        );
        var targetRel = new Relationship
        {
            SourceEntityId = customerTarget.Id,
            TargetEntityId = orderTarget.Id,
            Type = RelationshipType.OneToMany,
            SourceColumnId = customerTarget.Columns[0].Id,
            TargetColumnId = orderTarget.Columns[2].Id,
        };

        var diff = new SchemaDiffService().Compute(
            new List<Entity> { customerLive, orderLive },
            new List<Relationship> { liveRel },
            new List<Entity> { customerTarget, orderTarget },
            new List<Relationship> { targetRel }
        );

        diff.Items.Should()
            .ContainSingle(i =>
                i.Kind == SchemaDiffKind.DropForeignKey
                && i.ForeignKeyName == "FK_Order_CustomerId1"
            );
        diff.Items.Should()
            .ContainSingle(i =>
                i.Kind == SchemaDiffKind.AddForeignKey && i.ColumnName == "CustomerId2"
            );
    }

    /// <summary>参照アクション（ON DELETE/UPDATE）の変更で再作成（Drop+Add）が出力されることを検証する</summary>
    [Fact(DisplayName = "参照アクションが変わると DropForeignKey と AddForeignKey になる")]
    public void ForeignKeyReferentialActionChanged_EmitsDropAndAdd()
    {
        var parentLive = Tbl("Parent", ("Id", "int", true));
        var childLive = Tbl("Child", ("Id", "int", true), ("ParentId", "int", false));
        var liveRel = new Relationship
        {
            SourceEntityId = parentLive.Id,
            TargetEntityId = childLive.Id,
            Type = RelationshipType.OneToMany,
            SourceColumnId = parentLive.Columns[0].Id,
            TargetColumnId = childLive.Columns[1].Id,
            ConstraintName = "FK_Child_Parent",
            OnDelete = ForeignKeyReferentialAction.NoAction,
            OnUpdate = ForeignKeyReferentialAction.NoAction,
        };

        var parentTarget = Tbl("Parent", ("Id", "int", true));
        var childTarget = Tbl("Child", ("Id", "int", true), ("ParentId", "int", false));
        var targetRel = new Relationship
        {
            SourceEntityId = parentTarget.Id,
            TargetEntityId = childTarget.Id,
            Type = RelationshipType.OneToMany,
            SourceColumnId = parentTarget.Columns[0].Id,
            TargetColumnId = childTarget.Columns[1].Id,
            ConstraintName = "FK_Child_Parent",
            OnDelete = ForeignKeyReferentialAction.Cascade,
            OnUpdate = ForeignKeyReferentialAction.SetNull,
        };

        var diff = new SchemaDiffService().Compute(
            new List<Entity> { parentLive, childLive },
            new List<Relationship> { liveRel },
            new List<Entity> { parentTarget, childTarget },
            new List<Relationship> { targetRel }
        );

        diff.Items.Should()
            .ContainSingle(i =>
                i.Kind == SchemaDiffKind.DropForeignKey && i.ForeignKeyName == "FK_Child_Parent"
            );
        diff.Items.Should()
            .ContainSingle(i =>
                i.Kind == SchemaDiffKind.AddForeignKey
                && i.Relationship!.OnDelete == ForeignKeyReferentialAction.Cascade
                && i.Relationship.OnUpdate == ForeignKeyReferentialAction.SetNull
            );
    }

    /// <summary>DB と ER 図が同一なら差分項目が空になることを検証する</summary>
    [Fact(DisplayName = "差分が無ければ Items は空")]
    public void Identical_NoDiff()
    {
        var live = new List<Entity> { Tbl("A", ("Id", "int", true)) };
        var target = new List<Entity> { Tbl("A", ("Id", "int", true)) };
        var diff = new SchemaDiffService().Compute(
            live,
            new List<Relationship>(),
            target,
            new List<Relationship>()
        );
        diff.Items.Should().BeEmpty();
    }

    /// <summary>テーブル説明の変更が SetTableDescription として検出され、既定で選択されることを検証する</summary>
    [Fact(DisplayName = "テーブル説明が変わると SetTableDescription になる")]
    public void TableDescriptionChanged_SetTableDescription()
    {
        var live = new List<Entity> { Tbl("Customer", ("Id", "int", true)) };
        live[0].Description = "古い説明";
        var target = new List<Entity> { Tbl("Customer", ("Id", "int", true)) };
        target[0].Description = "新しい顧客テーブル";

        var diff = new SchemaDiffService().Compute(
            live,
            new List<Relationship>(),
            target,
            new List<Relationship>()
        );

        var item = diff
            .Items.Should()
            .ContainSingle(i => i.Kind == SchemaDiffKind.SetTableDescription)
            .Which;
        item.NewDescription.Should().Be("新しい顧客テーブル");
        item.OldDescription.Should().Be("古い説明");
        item.IsSelected.Should().BeTrue();
    }

    /// <summary>列説明の変更が SetColumnDescription として検出されることを検証する</summary>
    [Fact(DisplayName = "列の説明が変わると SetColumnDescription になる")]
    public void ColumnDescriptionChanged_SetColumnDescription()
    {
        var live = new List<Entity>
        {
            Tbl("Customer", ("Id", "int", true), ("Name", "nvarchar(50)", false)),
        };

        live[0].Columns[1].Description = "旧";
        var target = new List<Entity>
        {
            Tbl("Customer", ("Id", "int", true), ("Name", "nvarchar(50)", false)),
        };

        target[0].Columns[1].Description = "顧客名";

        var diff = new SchemaDiffService().Compute(
            live,
            new List<Relationship>(),
            target,
            new List<Relationship>()
        );

        var item = diff
            .Items.Should()
            .ContainSingle(i => i.Kind == SchemaDiffKind.SetColumnDescription)
            .Which;
        item.ColumnName.Should().Be("Name");
        item.NewDescription.Should().Be("顧客名");
        item.OldDescription.Should().Be("旧");
    }

    /// <summary>説明が同一なら差分項目を生成しないことを検証する</summary>
    [Fact(DisplayName = "説明が同じなら差分にならない")]
    public void SameDescription_NoDiff()
    {
        var live = new List<Entity> { Tbl("Customer", ("Id", "int", true)) };
        live[0].Description = "同じ説明";
        var target = new List<Entity> { Tbl("Customer", ("Id", "int", true)) };
        target[0].Description = "同じ説明";

        var diff = new SchemaDiffService().Compute(
            live,
            new List<Relationship>(),
            target,
            new List<Relationship>()
        );
        diff.Items.Should().BeEmpty();
    }

    /// <summary>新規テーブルに説明があれば AddTable と併せて説明設定差分も同時出力されることを検証する</summary>
    [Fact(DisplayName = "新規テーブル + 列の説明があれば SetTable/Column も同時に出力される")]
    public void NewTableWithDescriptions_EmitsAllSetDescriptions()
    {
        var live = new List<Entity>();
        var target = new List<Entity>
        {
            Tbl("Customer", ("Id", "int", true), ("Name", "nvarchar(50)", false)),
        };

        target[0].Description = "顧客マスタ";
        target[0].Columns[1].Description = "顧客名";

        var diff = new SchemaDiffService().Compute(
            live,
            new List<Relationship>(),
            target,
            new List<Relationship>()
        );

        diff.Items.Should().ContainSingle(i => i.Kind == SchemaDiffKind.AddTable);
        diff.Items.Should()
            .ContainSingle(i =>
                i.Kind == SchemaDiffKind.SetTableDescription && i.NewDescription == "顧客マスタ"
            );
        diff.Items.Should()
            .ContainSingle(i =>
                i.Kind == SchemaDiffKind.SetColumnDescription
                && i.ColumnName == "Name"
                && i.NewDescription == "顧客名"
            );
    }
}
