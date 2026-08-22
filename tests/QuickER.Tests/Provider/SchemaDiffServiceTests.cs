using System;
using System.Collections.Generic;
using System.Linq;
using AwesomeAssertions;
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
            // 構成列は列ペアで明示する（命名規約による推測フォールバックは廃止された）
            ColumnPairs = [new(customer.Columns[0].Id, order.Columns[1].Id)],
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
            ColumnPairs = [new(customerLive.Columns[0].Id, orderLive.Columns[1].Id)],
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
            ColumnPairs = [new(customerTarget.Columns[0].Id, orderTarget.Columns[2].Id)],
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
            ColumnPairs = [new(parentLive.Columns[0].Id, childLive.Columns[1].Id)],
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
            ColumnPairs = [new(parentTarget.Columns[0].Id, childTarget.Columns[1].Id)],
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

    // ---------------- ケーパビリティによる方言調整 ----------------

    /// <summary>SupportsDescriptions=false の方言（SQLite）では説明差分を一切生成しないことを検証する</summary>
    [Fact(DisplayName = "説明非対応方言では説明差分を生成しない")]
    public void CapabilitiesWithoutDescriptions_SuppressesDescriptionDiffs()
    {
        // 既存テーブルの説明変更・列説明変更・新規テーブルの説明の 3 経路をまとめて確認する
        var live = new List<Entity>
        {
            Tbl("Customer", ("Id", "int", true), ("Name", "nvarchar(50)", false)),
        };
        live[0].Description = "旧テーブル説明";
        live[0].Columns[1].Description = "旧列説明";

        var target = new List<Entity>
        {
            Tbl("Customer", ("Id", "int", true), ("Name", "nvarchar(50)", false)),
            Tbl("Order", ("Id", "int", true)),
        };
        target[0].Description = "新テーブル説明";
        target[0].Columns[1].Description = "新列説明";
        target[1].Description = "新規テーブルの説明";

        var capabilities = new SyncDialectCapabilities { SupportsDescriptions = false };

        var diff = new SchemaDiffService().Compute(
            live,
            new List<Relationship>(),
            target,
            new List<Relationship>(),
            capabilities
        );

        // 説明差分はゼロ・構造差分（新規テーブル Order）は生成される
        diff.Items.Should().NotContain(i => i.Kind == SchemaDiffKind.SetTableDescription);
        diff.Items.Should().NotContain(i => i.Kind == SchemaDiffKind.SetColumnDescription);
        diff.Items.Should()
            .ContainSingle(i => i.Kind == SchemaDiffKind.AddTable && i.TableName == "Order");
    }

    /// <summary>
    /// PersistsForeignKeyConstraintNames=false の方言（SQLite）では、制約名だけが異なる FK（合成名 live vs 無名 target）を
    /// 同一とみなし、Drop+Add の誤検出を出さないことを検証する。
    /// </summary>
    [Fact(DisplayName = "FK 制約名を永続化しない方言では名前差だけの FK 差分は出ない")]
    public void CapabilitiesWithoutFkConstraintNames_SuppressesNameOnlyForeignKeyDiff()
    {
        var parentLive = Tbl("Parent", ("Id", "int", true));
        var childLive = Tbl("Child", ("Id", "int", true), ("ParentId", "int", false));
        // live 側は取込時の合成制約名を持つ
        var liveRel = new Relationship
        {
            SourceEntityId = parentLive.Id,
            TargetEntityId = childLive.Id,
            Type = RelationshipType.OneToMany,
            ColumnPairs = [new(parentLive.Columns[0].Id, childLive.Columns[1].Id)],
            ConstraintName = "FK_Child_Parent_0",
        };

        var parentTarget = Tbl("Parent", ("Id", "int", true));
        var childTarget = Tbl("Child", ("Id", "int", true), ("ParentId", "int", false));
        // target 側は無名（手動作成のリレーション）
        var targetRel = new Relationship
        {
            SourceEntityId = parentTarget.Id,
            TargetEntityId = childTarget.Id,
            Type = RelationshipType.OneToMany,
            ColumnPairs = [new(parentTarget.Columns[0].Id, childTarget.Columns[1].Id)],
            ConstraintName = null,
        };

        var capabilities = new SyncDialectCapabilities
        {
            PersistsForeignKeyConstraintNames = false,
        };

        var diff = new SchemaDiffService().Compute(
            new List<Entity> { parentLive, childLive },
            new List<Relationship> { liveRel },
            new List<Entity> { parentTarget, childTarget },
            new List<Relationship> { targetRel },
            capabilities
        );

        // 制約名以外は同一のため、名前差だけでは FK 差分を出さない
        diff.Items.Should().NotContain(i => i.Kind == SchemaDiffKind.AddForeignKey);
        diff.Items.Should().NotContain(i => i.Kind == SchemaDiffKind.DropForeignKey);
    }

    /// <summary>
    /// 既定（capabilities なし）では制約名を含めて比較するため、名前差だけの FK でも Drop+Add が出る
    /// （上のケーパビリティ抑止との対照）ことを検証する。
    /// </summary>
    [Fact(DisplayName = "既定では FK 制約名の差で Drop+Add が出る")]
    public void DefaultCapabilities_EmitsFkDiffOnConstraintNameChange()
    {
        var parentLive = Tbl("Parent", ("Id", "int", true));
        var childLive = Tbl("Child", ("Id", "int", true), ("ParentId", "int", false));
        var liveRel = new Relationship
        {
            SourceEntityId = parentLive.Id,
            TargetEntityId = childLive.Id,
            Type = RelationshipType.OneToMany,
            ColumnPairs = [new(parentLive.Columns[0].Id, childLive.Columns[1].Id)],
            ConstraintName = "FK_Old",
        };

        var parentTarget = Tbl("Parent", ("Id", "int", true));
        var childTarget = Tbl("Child", ("Id", "int", true), ("ParentId", "int", false));
        var targetRel = new Relationship
        {
            SourceEntityId = parentTarget.Id,
            TargetEntityId = childTarget.Id,
            Type = RelationshipType.OneToMany,
            ColumnPairs = [new(parentTarget.Columns[0].Id, childTarget.Columns[1].Id)],
            ConstraintName = "FK_New",
        };

        var diff = new SchemaDiffService().Compute(
            new List<Entity> { parentLive, childLive },
            new List<Relationship> { liveRel },
            new List<Entity> { parentTarget, childTarget },
            new List<Relationship> { targetRel }
        );

        diff.Items.Should().Contain(i => i.Kind == SchemaDiffKind.DropForeignKey);
        diff.Items.Should().Contain(i => i.Kind == SchemaDiffKind.AddForeignKey);
    }

    // ---------------- 列順変更（ReorderColumns）の生成 ----------------

    /// <summary>列順のみ入れ替えた図（Native/Rebuild 方言）</summary>
    private static (List<Entity> Live, List<Entity> Target) ReorderScenario()
    {
        var live = new List<Entity>
        {
            Tbl(
                "Customer",
                ("Id", "int", true),
                ("Name", "nvarchar(50)", false),
                ("Email", "nvarchar(50)", false)
            ),
        };
        var target = new List<Entity>
        {
            Tbl(
                "Customer",
                ("Id", "int", true),
                ("Email", "nvarchar(50)", false),
                ("Name", "nvarchar(50)", false)
            ),
        };
        return (live, target);
    }

    /// <summary>Native 方言では列順変更が選択可能な ReorderColumns 項目として生成され、既定で未選択であることを検証する</summary>
    [Fact(DisplayName = "Native 方言では ReorderColumns が生成され既定で未選択")]
    public void Reorder_NativeDialect_GeneratesUnselectedReorderItem()
    {
        var (live, target) = ReorderScenario();

        var diff = new SchemaDiffService().Compute(
            live,
            new List<Relationship>(),
            target,
            new List<Relationship>(),
            new SyncDialectCapabilities { ColumnReorder = ColumnReorderMode.Native }
        );

        var reorder = diff
            .Items.Should()
            .ContainSingle(i => i.Kind == SchemaDiffKind.ReorderColumns)
            .Which;
        reorder.TableName.Should().Be("Customer");
        reorder.Entity.Should().BeSameAs(target[0]);
        reorder.IsSelected.Should().BeFalse();
        reorder.IsSelectable.Should().BeTrue();
        reorder.IsDestructive.Should().BeFalse();
    }

    /// <summary>Rebuild 方言でも ReorderColumns 項目が生成されることを検証する</summary>
    [Fact(DisplayName = "Rebuild 方言でも ReorderColumns が生成される")]
    public void Reorder_RebuildDialect_GeneratesReorderItem()
    {
        var (live, target) = ReorderScenario();

        var diff = new SchemaDiffService().Compute(
            live,
            new List<Relationship>(),
            target,
            new List<Relationship>(),
            new SyncDialectCapabilities
            {
                SupportsAlterColumn = false,
                SupportsForeignKeyAlter = false,
                SupportsDescriptions = false,
                PersistsForeignKeyConstraintNames = false,
                ColumnReorder = ColumnReorderMode.Rebuild,
            }
        );

        diff.Items.Should().Contain(i => i.Kind == SchemaDiffKind.ReorderColumns);
    }

    /// <summary>非対応方言（None）および capabilities 省略時は ReorderColumns を生成しないことを検証する</summary>
    [Fact(DisplayName = "None 方言・capabilities 省略時は ReorderColumns を生成しない")]
    public void Reorder_NoneDialectOrNoCapabilities_DoesNotGenerate()
    {
        var (live, target) = ReorderScenario();
        var service = new SchemaDiffService();

        // capabilities 省略（既定）
        var diffDefault = service.Compute(
            live,
            new List<Relationship>(),
            target,
            new List<Relationship>()
        );
        diffDefault.Items.Should().NotContain(i => i.Kind == SchemaDiffKind.ReorderColumns);

        // 明示的な None
        var diffNone = service.Compute(
            live,
            new List<Relationship>(),
            target,
            new List<Relationship>(),
            new SyncDialectCapabilities { ColumnReorder = ColumnReorderMode.None }
        );
        diffNone.Items.Should().NotContain(i => i.Kind == SchemaDiffKind.ReorderColumns);
    }

    // ---------------- 主キー変更（AlterPrimaryKey）の検出 ----------------

    /// <summary>
    /// 主キー指定だけを差し替えたテーブルを組み立てる（型・NULL 許容は固定＝主キー以外の差分を起こさない）。
    /// </summary>
    /// <param name="columns">列名（この順序がそのまま列定義順＝主キーの順序判定にも使われる）</param>
    /// <param name="pkColumns">主キーにする列名</param>
    private static Entity PkTbl(string name, string[] columns, params string[] pkColumns)
    {
        var e = new Entity { TableName = name };

        foreach (var c in columns)
        {
            e.Columns.Add(
                new Column
                {
                    Name = c,
                    DataType = "int",
                    IsNullable = false,
                    IsPrimaryKey = pkColumns.Contains(c, StringComparer.OrdinalIgnoreCase),
                }
            );
        }

        return e;
    }

    /// <summary>主キーが無かったテーブルへ主キーを付ける変更が AlterPrimaryKey になることを検証する</summary>
    [Fact(DisplayName = "主キーの追加は AlterPrimaryKey になり、既定では未選択かつ破壊的")]
    public void PrimaryKeyAdded_AlterPrimaryKey_NotSelected()
    {
        var live = new List<Entity> { PkTbl("Customer", ["Id", "Name"]) };
        var target = new List<Entity> { PkTbl("Customer", ["Id", "Name"], "Id") };

        var diff = new SchemaDiffService().Compute(
            live,
            new List<Relationship>(),
            target,
            new List<Relationship>()
        );

        // 主キー以外は同一のため、差分は AlterPrimaryKey の 1 件だけになる
        var item = diff.Items.Should().ContainSingle().Which;
        item.Kind.Should().Be(SchemaDiffKind.AlterPrimaryKey);
        item.TableName.Should().Be("Customer");
        item.Entity.Should().BeSameAs(target[0]);
        item.IsSelected.Should().BeFalse();
        item.IsSelectable.Should().BeTrue();
        item.IsDestructive.Should().BeTrue();

        // 製品コードと同じ resx キーから期待値を組み立て、カルチャに依らず完全一致で検証する
        item.Description.Should()
            .Be(
                string.Format(
                    ProviderStrings.Diff_AlterPrimaryKey,
                    "Customer",
                    ProviderStrings.Diff_PrimaryKey_None,
                    "Id"
                )
            );
    }

    /// <summary>主キーの解除が AlterPrimaryKey になり、変更後表記が「なし」になることを検証する</summary>
    [Fact(DisplayName = "主キーの解除は AlterPrimaryKey になる")]
    public void PrimaryKeyRemoved_AlterPrimaryKey()
    {
        var live = new List<Entity> { PkTbl("Customer", ["Id", "Name"], "Id") };
        var target = new List<Entity> { PkTbl("Customer", ["Id", "Name"]) };

        var diff = new SchemaDiffService().Compute(
            live,
            new List<Relationship>(),
            target,
            new List<Relationship>()
        );

        var item = diff
            .Items.Should()
            .ContainSingle(i => i.Kind == SchemaDiffKind.AlterPrimaryKey)
            .Which;
        item.Description.Should()
            .Be(
                string.Format(
                    ProviderStrings.Diff_AlterPrimaryKey,
                    "Customer",
                    "Id",
                    ProviderStrings.Diff_PrimaryKey_None
                )
            );
    }

    /// <summary>単一主キーから複合主キーへの構成変更が 1 件の AlterPrimaryKey になることを検証する</summary>
    [Fact(DisplayName = "単一主キー → 複合主キーの構成変更は 1 件の AlterPrimaryKey になる")]
    public void PrimaryKeyCompositionChanged_AlterPrimaryKey()
    {
        var live = new List<Entity> { PkTbl("OrderLine", ["OrderId", "LineNo"], "OrderId") };
        var target = new List<Entity>
        {
            PkTbl("OrderLine", ["OrderId", "LineNo"], "OrderId", "LineNo"),
        };

        var diff = new SchemaDiffService().Compute(
            live,
            new List<Relationship>(),
            target,
            new List<Relationship>()
        );

        var item = diff.Items.Should().ContainSingle().Which;
        item.Kind.Should().Be(SchemaDiffKind.AlterPrimaryKey);
        item.Description.Should()
            .Be(
                string.Format(
                    ProviderStrings.Diff_AlterPrimaryKey,
                    "OrderLine",
                    "OrderId",
                    "OrderId, LineNo"
                )
            );
    }

    /// <summary>複合主キーの順序変更（構成列は同じ）も AlterPrimaryKey として検出されることを検証する</summary>
    [Fact(DisplayName = "複合主キーの順序変更も AlterPrimaryKey になる")]
    public void PrimaryKeyOrderChanged_AlterPrimaryKey()
    {
        // 列定義順が主キーの順序になる（live: OrderId, LineNo → target: LineNo, OrderId）
        var live = new List<Entity>
        {
            PkTbl("OrderLine", ["OrderId", "LineNo"], "OrderId", "LineNo"),
        };
        var target = new List<Entity>
        {
            PkTbl("OrderLine", ["LineNo", "OrderId"], "OrderId", "LineNo"),
        };

        var diff = new SchemaDiffService().Compute(
            live,
            new List<Relationship>(),
            target,
            new List<Relationship>()
        );

        var item = diff
            .Items.Should()
            .ContainSingle(i => i.Kind == SchemaDiffKind.AlterPrimaryKey)
            .Which;
        item.Description.Should()
            .Be(
                string.Format(
                    ProviderStrings.Diff_AlterPrimaryKey,
                    "OrderLine",
                    "OrderId, LineNo",
                    "LineNo, OrderId"
                )
            );
    }

    /// <summary>主キー構成が同一なら（列名の大文字小文字差を含めて）AlterPrimaryKey を生成しないことを検証する</summary>
    [Fact(DisplayName = "主キー構成が同じなら AlterPrimaryKey は出ない")]
    public void SamePrimaryKey_NoAlterPrimaryKey()
    {
        var live = new List<Entity>
        {
            PkTbl("OrderLine", ["OrderId", "LineNo"], "OrderId", "LineNo"),
        };
        // 列名の大文字小文字だけが異なる（列差分と同じ規則で同一とみなす）
        var target = new List<Entity>
        {
            PkTbl("OrderLine", ["orderid", "lineno"], "orderid", "lineno"),
        };

        var diff = new SchemaDiffService().Compute(
            live,
            new List<Relationship>(),
            target,
            new List<Relationship>()
        );

        diff.Items.Should().NotContain(i => i.Kind == SchemaDiffKind.AlterPrimaryKey);
    }

    // ---------------- 一意制約（UNIQUE）の差分 ----------------

    /// <summary>エンティティへ一意制約を足す（構成列は列名で引き当てる）</summary>
    private static Entity WithUnique(Entity entity, string? name, params string[] columnNames)
    {
        entity.UniqueConstraints.Add(
            new UniqueConstraint
            {
                Name = name,
                ColumnIds = columnNames
                    .Select(n => entity.Columns.Single(c => c.Name == n).Id)
                    .ToList(),
            }
        );
        return entity;
    }

    /// <summary>Code / Kind 列を持つ Customer テーブルを生成する（一意制約テストの共通土台）</summary>
    private static Entity CustomerTable() =>
        Tbl(
            "Customer",
            ("Id", "int", true),
            ("Code", "nvarchar(20)", false),
            ("Kind", "nvarchar(10)", false)
        );

    /// <summary>図にだけ在る一意制約が AddUniqueConstraint として検出され、既定で選択されることを検証する</summary>
    [Fact(DisplayName = "図にだけ在る一意制約は AddUniqueConstraint になり既定で選択される")]
    public void UniqueConstraintOnlyInTarget_AddUniqueConstraint_SelectedByDefault()
    {
        var live = new List<Entity> { CustomerTable() };
        var target = new List<Entity> { WithUnique(CustomerTable(), name: null, "Code") };

        var diff = new SchemaDiffService().Compute(
            live,
            new List<Relationship>(),
            target,
            new List<Relationship>()
        );

        var item = diff
            .Items.Should()
            .ContainSingle(i => i.Kind == SchemaDiffKind.AddUniqueConstraint)
            .Which;
        item.TableName.Should().Be("Customer");
        item.UniqueConstraintColumns.Should().Equal("Code");
        item.IsSelected.Should().BeTrue();
        item.IsDestructive.Should().BeFalse();
        item.Description.Should()
            .Be(string.Format(ProviderStrings.Diff_AddUniqueConstraint, "Customer", "Code"));
    }

    /// <summary>DB にだけ在る一意制約が DropUniqueConstraint（既定未選択・破壊的）になることを検証する</summary>
    [Fact(DisplayName = "DB にだけ在る一意制約は DropUniqueConstraint になり既定で未選択")]
    public void UniqueConstraintOnlyInLive_DropUniqueConstraint_NotSelected()
    {
        var live = new List<Entity> { WithUnique(CustomerTable(), "UQ_Legacy", "Code", "Kind") };
        var target = new List<Entity> { CustomerTable() };

        var diff = new SchemaDiffService().Compute(
            live,
            new List<Relationship>(),
            target,
            new List<Relationship>()
        );

        var item = diff
            .Items.Should()
            .ContainSingle(i => i.Kind == SchemaDiffKind.DropUniqueConstraint)
            .Which;
        // DROP には DB 側の実名が要る（レンダラーはこの名前をそのまま使う）
        item.UniqueConstraintName.Should().Be("UQ_Legacy");
        item.UniqueConstraintColumns.Should().Equal("Code", "Kind");
        item.IsSelected.Should().BeFalse();
        item.IsDestructive.Should().BeTrue();
        item.Description.Should()
            .Be(string.Format(ProviderStrings.Diff_DropUniqueConstraint, "Customer", "Code, Kind"));
    }

    /// <summary>制約名だけが違う（構成列は同じ）一意制約では差分が出ないことを検証する</summary>
    /// <remarks>
    /// 図側の制約名は未設定（null＝合成名）が普通で、SQLite に至っては実名を持たない。
    /// 名前を比較に含めると恒常的な Drop＋Add の誤検出になるため、照合は列集合だけで行う。
    /// </remarks>
    [Fact(DisplayName = "制約名の差だけでは一意制約の差分にならない")]
    public void UniqueConstraintNameDiffersOnly_NoDiff()
    {
        var live = new List<Entity> { WithUnique(CustomerTable(), "UQ_From_Db", "Code") };
        var target = new List<Entity> { WithUnique(CustomerTable(), name: null, "Code") };

        var diff = new SchemaDiffService().Compute(
            live,
            new List<Relationship>(),
            target,
            new List<Relationship>()
        );

        diff.Items.Should()
            .NotContain(i =>
                i.Kind == SchemaDiffKind.AddUniqueConstraint
                || i.Kind == SchemaDiffKind.DropUniqueConstraint
            );
    }

    /// <summary>構成列の並び順・大文字小文字の差だけでは一意制約の差分にならないことを検証する</summary>
    [Fact(DisplayName = "構成列の順序・大文字小文字の差だけでは一意制約の差分にならない")]
    public void UniqueConstraintColumnOrderOrCaseDiffersOnly_NoDiff()
    {
        var live = new List<Entity> { WithUnique(CustomerTable(), "UQ_A", "Code", "Kind") };
        var targetTable = Tbl(
            "Customer",
            ("Id", "int", true),
            ("code", "nvarchar(20)", false),
            ("kind", "nvarchar(10)", false)
        );
        var target = new List<Entity> { WithUnique(targetTable, name: null, "kind", "code") };

        var diff = new SchemaDiffService().Compute(
            live,
            new List<Relationship>(),
            target,
            new List<Relationship>()
        );

        diff.Items.Should()
            .NotContain(i =>
                i.Kind == SchemaDiffKind.AddUniqueConstraint
                || i.Kind == SchemaDiffKind.DropUniqueConstraint
            );
    }

    /// <summary>構成列が空・解決不能な一意制約は差分対象から外れることを検証する</summary>
    [Fact(DisplayName = "構成列が解決できない一意制約は差分にならない")]
    public void UniqueConstraintWithUnresolvableColumns_NoDiff()
    {
        var targetTable = CustomerTable();
        // 空の制約と、このエンティティに存在しない列を指す制約（いずれも DDL 生成側も無視する）
        targetTable.UniqueConstraints.Add(new UniqueConstraint());
        targetTable.UniqueConstraints.Add(new UniqueConstraint { ColumnIds = [Guid.NewGuid()] });

        var diff = new SchemaDiffService().Compute(
            new List<Entity> { CustomerTable() },
            new List<Relationship>(),
            new List<Entity> { targetTable },
            new List<Relationship>()
        );

        diff.Items.Should().NotContain(i => i.Kind == SchemaDiffKind.AddUniqueConstraint);
    }
}
