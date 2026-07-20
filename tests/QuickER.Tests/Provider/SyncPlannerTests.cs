using System.Linq;
using FluentAssertions;
using QuickER.Model;
using QuickER.Provider;
using Xunit;

namespace QuickER.Tests.Provider;

/// <summary><see cref="SyncPlanner"/> が差分項目から組み立てる実行計画（選択フィルタ・固定順序・グループ化）を検証する</summary>
public class SyncPlannerTests
{
    /// <summary>指定種別・選択状態の差分項目を生成する小さなヘルパー</summary>
    private static SchemaDiffItem Item(
        SchemaDiffKind kind,
        bool selected = true,
        string table = "T"
    ) =>
        new()
        {
            Kind = kind,
            TableName = table,
            IsSelected = selected,
        };

    /// <summary>未選択（IsSelected=false）の項目が計画から除外されることを検証する</summary>
    [Fact(DisplayName = "未選択項目は計画から除外される")]
    public void Unselected_AreExcluded()
    {
        var plan = new SyncPlanner().BuildPlan(
            [
                Item(SchemaDiffKind.AddTable, selected: false),
                Item(SchemaDiffKind.AddColumn, selected: true),
            ],
            new SyncDialectCapabilities()
        );

        plan.Sections.Should().ContainSingle();
        plan.Sections[0].Kind.Should().Be(SchemaDiffKind.AddColumn);
    }

    /// <summary>入力が乱順でもセクションが固定順序（依存関係で失敗しない順）で並ぶことを検証する</summary>
    [Fact(DisplayName = "セクションは固定順序で並ぶ")]
    public void Sections_AreInFixedOrder()
    {
        // わざと逆順寄りに投入する
        var plan = new SyncPlanner().BuildPlan(
            [
                Item(SchemaDiffKind.SetColumnDescription),
                Item(SchemaDiffKind.SetTableDescription),
                Item(SchemaDiffKind.AddForeignKey),
                Item(SchemaDiffKind.DropTable),
                Item(SchemaDiffKind.DropColumn),
                Item(SchemaDiffKind.DropForeignKey),
                Item(SchemaDiffKind.AlterColumn),
                Item(SchemaDiffKind.AddColumn),
                Item(SchemaDiffKind.AddTable),
            ],
            new SyncDialectCapabilities()
        );

        plan.Sections.Select(s => s.Kind)
            .Should()
            .Equal(
                SchemaDiffKind.AddTable,
                SchemaDiffKind.AddColumn,
                SchemaDiffKind.AlterColumn,
                SchemaDiffKind.DropForeignKey,
                SchemaDiffKind.DropColumn,
                SchemaDiffKind.DropTable,
                SchemaDiffKind.AddForeignKey,
                SchemaDiffKind.SetTableDescription,
                SchemaDiffKind.SetColumnDescription
            );
    }

    /// <summary>同一セクション内は入力の出現順を保持することを検証する</summary>
    [Fact(DisplayName = "セクション内は入力の出現順を保持する")]
    public void WithinSection_PreservesInputOrder()
    {
        var a = Item(SchemaDiffKind.AddColumn, table: "A");
        var b = Item(SchemaDiffKind.AddColumn, table: "B");
        var c = Item(SchemaDiffKind.AddColumn, table: "C");

        var plan = new SyncPlanner().BuildPlan([a, b, c], new SyncDialectCapabilities());

        plan.Sections.Should().ContainSingle();
        plan.Sections[0].Items.Should().Equal(a, b, c);
    }

    /// <summary>該当項目の無い種別はセクションとして現れない（空セクションを含まない）ことを検証する</summary>
    [Fact(DisplayName = "空セクションは含まれない")]
    public void EmptySections_AreOmitted()
    {
        var plan = new SyncPlanner().BuildPlan(
            [Item(SchemaDiffKind.AddTable)],
            new SyncDialectCapabilities()
        );

        plan.Sections.Should().ContainSingle();
        plan.Sections[0].Kind.Should().Be(SchemaDiffKind.AddTable);
    }

    /// <summary>RebuildTable は選択されていても計画から除外される（情報表示専用・SQL 生成対象外）ことを検証する</summary>
    [Fact(DisplayName = "RebuildTable は選択されていても除外される")]
    public void RebuildTable_IsAlwaysExcluded()
    {
        var plan = new SyncPlanner().BuildPlan(
            [
                Item(SchemaDiffKind.RebuildTable, selected: true),
                Item(SchemaDiffKind.AddTable, selected: true),
            ],
            new SyncDialectCapabilities()
        );

        plan.Sections.Should().ContainSingle();
        plan.Sections[0].Kind.Should().Be(SchemaDiffKind.AddTable);
        plan.Sections.Should().NotContain(s => s.Kind == SchemaDiffKind.RebuildTable);
    }

    /// <summary>すべて未選択なら計画が空（IsEmpty）になることを検証する</summary>
    [Fact(DisplayName = "全未選択なら計画は空になる")]
    public void AllUnselected_ResultsInEmptyPlan()
    {
        var plan = new SyncPlanner().BuildPlan(
            [
                Item(SchemaDiffKind.AddTable, selected: false),
                Item(SchemaDiffKind.DropTable, selected: false),
            ],
            new SyncDialectCapabilities()
        );

        plan.IsEmpty.Should().BeTrue();
        plan.Sections.Should().BeEmpty();
    }

    /// <summary>
    /// 逐次 DDL 方言（ALTER・FK 変更が可能）ではケーパビリティ差が計画へ影響せず、再構築も生じないことを検証する。
    /// </summary>
    [Fact(DisplayName = "逐次 DDL 方言はケーパビリティ差で計画が変わらない")]
    public void NonRebuildDialect_ProducesSameSectionsRegardlessOfOtherCapabilities()
    {
        SchemaDiffItem[] items =
        [
            Item(SchemaDiffKind.AddTable),
            Item(SchemaDiffKind.AlterColumn),
            Item(SchemaDiffKind.SetTableDescription),
        ];

        var planner = new SyncPlanner();
        var defaultPlan = planner.BuildPlan(items, new SyncDialectCapabilities());

        // SupportsDescriptions=false だけでは rebuild 方言にならない（ALTER・FK 変更は可能なまま）
        var variantPlan = planner.BuildPlan(
            items,
            new SyncDialectCapabilities { SupportsDescriptions = false }
        );

        variantPlan
            .Sections.Select(s => s.Kind)
            .Should()
            .Equal(defaultPlan.Sections.Select(s => s.Kind));
        variantPlan.Rebuilds.Should().BeEmpty();
    }

    // ---------------- テーブル再構築（rebuild）方言の集約 ----------------

    /// <summary>ALTER COLUMN も FK の後付けもできない再構築方言のケーパビリティ</summary>
    private static readonly SyncDialectCapabilities RebuildCaps = new()
    {
        SupportsAlterColumn = false,
        SupportsForeignKeyAlter = false,
        SupportsDescriptions = false,
        PersistsForeignKeyConstraintNames = false,
        ColumnReorder = ColumnReorderMode.Rebuild,
    };

    /// <summary>単純な非 NULL 主キー列 id を生成する</summary>
    private static Column PkId() =>
        new()
        {
            Name = "id",
            DataType = "INT",
            IsPrimaryKey = true,
            IsNullable = false,
        };

    /// <summary>NULL 許容の通常列を生成する</summary>
    private static Column Col(string name, string type) =>
        new()
        {
            Name = name,
            DataType = type,
            IsNullable = true,
        };

    /// <summary>再構築方言で context を渡さないと InvalidOperationException になることを検証する</summary>
    [Fact(DisplayName = "再構築方言で context 省略は例外")]
    public void RebuildDialect_NullContext_Throws()
    {
        var act = () =>
            new SyncPlanner().BuildPlan([Item(SchemaDiffKind.AlterColumn)], RebuildCaps);

        act.Should().Throw<InvalidOperationException>();
    }

    /// <summary>AlterColumn が live 定義に選択差分のみを適用した合成テーブル再構築になることを検証する</summary>
    [Fact(DisplayName = "AlterColumn は合成スキーマのテーブル再構築になる")]
    public void AlterColumn_ProducesRebuildWithSynthesizedDefinition()
    {
        var liveNote = Col("note", "TEXT");
        var live = new Entity { TableName = "orders", Columns = { PkId(), liveNote } };
        var newNote = new Column
        {
            Name = "note",
            DataType = "NVARCHAR(200)",
            IsNullable = false,
        };

        var plan = new SyncPlanner().BuildPlan(
            [
                new SchemaDiffItem
                {
                    Kind = SchemaDiffKind.AlterColumn,
                    TableName = "orders",
                    ColumnName = "note",
                    Column = newNote,
                    OldColumn = liveNote,
                    IsSelected = true,
                },
            ],
            RebuildCaps,
            new SyncPlanContext { LiveEntities = [live] }
        );

        plan.Sections.Should().BeEmpty();
        var rb = plan.Rebuilds.Should().ContainSingle().Which;
        rb.CreateOnly.Should().BeFalse();
        rb.TableName.Should().Be("orders");
        rb.NewDefinition.Columns.Should().HaveCount(2);
        rb.NewDefinition.Columns.Single(c => c.Name == "note")
            .DataType.Should()
            .Be("NVARCHAR(200)");
        // live と合成後の両方に存在する列のみが移送対象になる
        rb.CopyColumns.Should().Equal("id", "note");
    }

    /// <summary>未選択の差分が合成スキーマへ紛れ込まない（選択された変更のみ適用される）ことを検証する</summary>
    [Fact(DisplayName = "未選択の変更は合成スキーマへ紛れ込まない")]
    public void Rebuild_UnselectedChangesDoNotLeakIntoSynthesis()
    {
        var live = new Entity
        {
            TableName = "t",
            Columns = { PkId(), Col("a", "INT"), Col("b", "INT") },
        };

        var plan = new SyncPlanner().BuildPlan(
            [
                new SchemaDiffItem
                {
                    Kind = SchemaDiffKind.AlterColumn,
                    TableName = "t",
                    ColumnName = "a",
                    Column = Col("a", "BIGINT"),
                    IsSelected = true,
                },
                // b の削除は未選択 → 合成後も b が残る
                new SchemaDiffItem
                {
                    Kind = SchemaDiffKind.DropColumn,
                    TableName = "t",
                    ColumnName = "b",
                    Column = Col("b", "INT"),
                    IsSelected = false,
                },
            ],
            RebuildCaps,
            new SyncPlanContext { LiveEntities = [live] }
        );

        var rb = plan.Rebuilds.Should().ContainSingle().Which;
        rb.NewDefinition.Columns.Select(c => c.Name).Should().Equal("id", "a", "b");
        rb.NewDefinition.Columns.Single(c => c.Name == "a").DataType.Should().Be("BIGINT");
    }

    /// <summary>再構築対象テーブルの選択済み AddColumn が末尾へ畳み込まれ、セクションに残らないことを検証する</summary>
    [Fact(DisplayName = "再構築対象の AddColumn は末尾へ畳み込まれる")]
    public void Rebuild_FoldsSelectedAddColumnOnRebuildTable()
    {
        var live = new Entity { TableName = "t", Columns = { PkId(), Col("a", "INT") } };

        var plan = new SyncPlanner().BuildPlan(
            [
                new SchemaDiffItem
                {
                    Kind = SchemaDiffKind.AlterColumn,
                    TableName = "t",
                    ColumnName = "a",
                    Column = Col("a", "BIGINT"),
                    IsSelected = true,
                },
                new SchemaDiffItem
                {
                    Kind = SchemaDiffKind.AddColumn,
                    TableName = "t",
                    ColumnName = "c",
                    Column = Col("c", "TEXT"),
                    IsSelected = true,
                },
            ],
            RebuildCaps,
            new SyncPlanContext { LiveEntities = [live] }
        );

        plan.Sections.Should().BeEmpty();
        var rb = plan.Rebuilds.Should().ContainSingle().Which;
        rb.NewDefinition.Columns.Select(c => c.Name).Should().Equal("id", "a", "c");
        // 新規列 c は live に無いため移送対象外
        rb.CopyColumns.Should().Equal("id", "a");
    }

    /// <summary>再構築対象でないテーブルの AddColumn は通常のセクション（ADD COLUMN）に残ることを検証する</summary>
    [Fact(DisplayName = "非再構築テーブルの AddColumn はセクションに残る")]
    public void AddColumn_OnNonRebuildTable_StaysAsSection()
    {
        var plan = new SyncPlanner().BuildPlan(
            [
                new SchemaDiffItem
                {
                    Kind = SchemaDiffKind.AddColumn,
                    TableName = "t",
                    ColumnName = "c",
                    Column = Col("c", "TEXT"),
                    IsSelected = true,
                },
            ],
            RebuildCaps,
            new SyncPlanContext()
        );

        plan.Rebuilds.Should().BeEmpty();
        plan.Sections.Should().ContainSingle();
        plan.Sections[0].Kind.Should().Be(SchemaDiffKind.AddColumn);
    }

    /// <summary>新規テーブルへの FK が CreateOnly 再構築へインライン化され、セクションから外れることを検証する</summary>
    [Fact(DisplayName = "新規テーブルへの FK は CreateOnly にインライン化される")]
    public void AddTable_WithForeignKey_BecomesCreateOnlyWithInlineFk()
    {
        var parent = new Entity { TableName = "customer", Columns = { PkId() } };
        var child = new Entity
        {
            TableName = "orders",
            Columns = { PkId(), Col("customer_id", "INT") },
        };
        var rel = new Relationship
        {
            SourceEntityId = parent.Id,
            TargetEntityId = child.Id,
            SourceColumnId = parent.Columns[0].Id,
            TargetColumnId = child.Columns[1].Id,
        };

        var plan = new SyncPlanner().BuildPlan(
            [
                new SchemaDiffItem
                {
                    Kind = SchemaDiffKind.AddTable,
                    TableName = "orders",
                    Entity = child,
                    IsSelected = true,
                },
                new SchemaDiffItem
                {
                    Kind = SchemaDiffKind.AddForeignKey,
                    TableName = "orders",
                    ColumnName = "customer_id",
                    ParentEntity = parent,
                    ChildEntity = child,
                    Relationship = rel,
                    IsSelected = true,
                },
            ],
            RebuildCaps,
            new SyncPlanContext()
        );

        // AddTable も AddForeignKey もセクションには出ない（CreateOnly へ畳まれる）
        plan.Sections.Should().BeEmpty();
        var rb = plan.Rebuilds.Should().ContainSingle().Which;
        rb.CreateOnly.Should().BeTrue();
        rb.TableName.Should().Be("orders");
        rb.CopyColumns.Should().BeEmpty();
        var fk = rb.ForeignKeys.Should().ContainSingle().Which;
        fk.ChildColumn.Should().Be("customer_id");
        fk.ParentTable.Should().Be("customer");
        fk.ParentColumn.Should().Be("id");
    }

    /// <summary>既存テーブルの FK 集合が「live − 選択 Drop ＋ 選択 Add」で合成されることを検証する</summary>
    [Fact(DisplayName = "既存テーブルの FK 集合は live から Drop を除き Add を足す")]
    public void ExistingTable_ForeignKeySetIsSynthesizedFromLiveMinusDropPlusAdd()
    {
        var customer = new Entity { TableName = "customer", Columns = { PkId() } };
        var supplier = new Entity { TableName = "supplier", Columns = { PkId() } };
        var refCol = Col("ref_id", "INT");
        var orders = new Entity { TableName = "orders", Columns = { PkId(), refCol } };

        // live FK: orders.ref_id -> customer.id
        var liveRel = new Relationship
        {
            SourceEntityId = customer.Id,
            TargetEntityId = orders.Id,
            SourceColumnId = customer.Columns[0].Id,
            TargetColumnId = refCol.Id,
            ConstraintName = "FK_orders_customer_0",
        };
        var context = new SyncPlanContext
        {
            LiveEntities = [customer, supplier, orders],
            LiveRelationships = [liveRel],
        };

        // 目標 FK: orders.ref_id -> supplier.id
        var addRel = new Relationship
        {
            SourceEntityId = supplier.Id,
            TargetEntityId = orders.Id,
            SourceColumnId = supplier.Columns[0].Id,
            TargetColumnId = refCol.Id,
        };

        var plan = new SyncPlanner().BuildPlan(
            [
                new SchemaDiffItem
                {
                    Kind = SchemaDiffKind.DropForeignKey,
                    TableName = "orders",
                    ParentEntity = customer,
                    ChildEntity = orders,
                    Relationship = liveRel,
                    ForeignKeyName = "FK_orders_customer_0",
                    IsSelected = true,
                },
                new SchemaDiffItem
                {
                    Kind = SchemaDiffKind.AddForeignKey,
                    TableName = "orders",
                    ColumnName = "ref_id",
                    ParentEntity = supplier,
                    ChildEntity = orders,
                    Relationship = addRel,
                    IsSelected = true,
                },
            ],
            RebuildCaps,
            context
        );

        plan.Sections.Should().BeEmpty();
        var rb = plan.Rebuilds.Should().ContainSingle().Which;
        // live の customer FK は削除され、supplier FK だけが残る
        var fk = rb.ForeignKeys.Should().ContainSingle().Which;
        fk.ChildColumn.Should().Be("ref_id");
        fk.ParentTable.Should().Be("supplier");
        fk.ParentColumn.Should().Be("id");
    }

    /// <summary>複数テーブルにまたがる再構築がテーブル単位でグループ化されることを検証する</summary>
    [Fact(DisplayName = "再構築はテーブル単位でグループ化される")]
    public void Rebuild_GroupsByTable()
    {
        var t1 = new Entity { TableName = "t1", Columns = { PkId(), Col("a", "INT") } };
        var t2 = new Entity { TableName = "t2", Columns = { PkId(), Col("b", "INT") } };

        var plan = new SyncPlanner().BuildPlan(
            [
                new SchemaDiffItem
                {
                    Kind = SchemaDiffKind.AlterColumn,
                    TableName = "t1",
                    ColumnName = "a",
                    Column = Col("a", "BIGINT"),
                    IsSelected = true,
                },
                new SchemaDiffItem
                {
                    Kind = SchemaDiffKind.DropColumn,
                    TableName = "t2",
                    ColumnName = "b",
                    Column = Col("b", "INT"),
                    IsSelected = true,
                },
            ],
            RebuildCaps,
            new SyncPlanContext { LiveEntities = [t1, t2] }
        );

        plan.Rebuilds.Select(r => r.TableName).Should().BeEquivalentTo("t1", "t2");
        plan.Rebuilds.Single(r => r.TableName == "t2")
            .NewDefinition.Columns.Select(c => c.Name)
            .Should()
            .Equal("id");
    }

    /// <summary>
    /// 新規テーブルの AddTable が未選択のまま、そのテーブルへの AddForeignKey だけが選択された場合に、
    /// 例外にせず（UI のチェック操作でクラッシュさせず）該当項目をセクションへ残すことを検証する。
    /// live に存在しないテーブルは合成の土台が無いため再構築できず、レンダラーのスキップコメントに委ねる。
    /// </summary>
    [Fact(DisplayName = "AddTable 未選択の新規テーブルへの FK は例外にせずセクションへ残す")]
    public void AddForeignKey_ToUnselectedNewTable_StaysInSectionsWithoutThrowing()
    {
        var parent = new Entity { TableName = "customers", Columns = { PkId() } };
        var newChild = new Entity
        {
            TableName = "orders",
            Columns = { PkId(), Col("customer_id", "INT") },
        };

        var fkItem = new SchemaDiffItem
        {
            Kind = SchemaDiffKind.AddForeignKey,
            TableName = "orders",
            ColumnName = "customer_id",
            ParentEntity = parent,
            ChildEntity = newChild,
            IsSelected = true,
        };

        // orders は live に存在せず、AddTable も未選択（＝計画に含まれない）
        var plan = new SyncPlanner().BuildPlan(
            [Item(SchemaDiffKind.AddTable, selected: false, table: "orders"), fkItem],
            RebuildCaps,
            new SyncPlanContext { LiveEntities = [parent] }
        );

        plan.Rebuilds.Should().BeEmpty();
        var section = plan.Sections.Should().ContainSingle().Which;
        section.Kind.Should().Be(SchemaDiffKind.AddForeignKey);
        section.Items.Should().Equal(fkItem);
    }

    /// <summary>
    /// 新規テーブル（CreateOnly）への FK のうち解決できないもの（参照列不明）は畳み込まず、
    /// セクションへ残してレンダラーのスキップコメントに委ねることを検証する。
    /// </summary>
    [Fact(DisplayName = "解決できない FK は CreateOnly へ畳まずセクションへ残す")]
    public void UnresolvableForeignKey_OnNewTable_StaysInSections()
    {
        // 親に主キーが無く、FK 列も未指定 → 解決不能
        var orphanParent = new Entity { TableName = "no_pk", Columns = { Col("code", "INT") } };
        var newChild = new Entity { TableName = "orders", Columns = { PkId() } };

        var addTable = new SchemaDiffItem
        {
            Kind = SchemaDiffKind.AddTable,
            TableName = "orders",
            Entity = newChild,
            IsSelected = true,
        };
        var unresolvableFk = new SchemaDiffItem
        {
            Kind = SchemaDiffKind.AddForeignKey,
            TableName = "orders",
            ColumnName = null,
            ParentEntity = orphanParent,
            ChildEntity = newChild,
            IsSelected = true,
        };

        var plan = new SyncPlanner().BuildPlan(
            [addTable, unresolvableFk],
            RebuildCaps,
            new SyncPlanContext()
        );

        var rb = plan.Rebuilds.Should().ContainSingle().Which;
        rb.CreateOnly.Should().BeTrue();
        rb.ForeignKeys.Should().BeEmpty();
        rb.SourceItems.Should().Equal(addTable);

        var section = plan.Sections.Should().ContainSingle().Which;
        section.Kind.Should().Be(SchemaDiffKind.AddForeignKey);
        section.Items.Should().Equal(unresolvableFk);
    }
}
