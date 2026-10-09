using System;
using System.Collections.Generic;
using System.Linq;
using AwesomeAssertions;
using QuickER.Model;
using QuickER.Provider;
using QuickER.Provider.SqlServer;
using Xunit;
using ProviderStrings = QuickER.Provider.Resources.Strings;

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
    [Fact(DisplayName = "セクションは固定順序で並ぶ（主キー変更は 2 フェーズ）")]
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
                AlterPk("T", new Entity { TableName = "T", Columns = { PkId() } }),
                Item(SchemaDiffKind.DropForeignKey),
                Item(SchemaDiffKind.AlterColumn),
                Item(SchemaDiffKind.AddColumn),
                Item(SchemaDiffKind.AddTable),
            ],
            new SyncDialectCapabilities()
        );

        // DropForeignKey が AlterColumn より先なのは意図的:
        // FK 依存列の型変更を通すため、先に FK を外しておく必要がある（SQL Server は Msg 5074 で失敗する）
        // AlterPrimaryKey は 2 フェーズ: 解除は AlterColumn の前（旧 PK 列の NULL 許容化を通すため）・
        // 付与は AlterColumn の後（新 PK 列の NOT NULL 化を先に済ませるため）。いずれも DropColumn より前
        plan.Sections.Select(s => s.Kind)
            .Should()
            .Equal(
                SchemaDiffKind.AddTable,
                SchemaDiffKind.AddColumn,
                SchemaDiffKind.DropForeignKey,
                SchemaDiffKind.AlterPrimaryKey,
                SchemaDiffKind.AlterColumn,
                SchemaDiffKind.AlterPrimaryKey,
                SchemaDiffKind.DropColumn,
                SchemaDiffKind.DropTable,
                SchemaDiffKind.AddForeignKey,
                SchemaDiffKind.SetTableDescription,
                SchemaDiffKind.SetColumnDescription
            );

        // フェーズは主キー変更セクションだけが持ち、他は既定の None のまま
        plan.Sections[3].PrimaryKeyPhase.Should().Be(PrimaryKeyPhase.Drop);
        plan.Sections[5].PrimaryKeyPhase.Should().Be(PrimaryKeyPhase.Add);
        plan.Sections.Where(s => s.Kind != SchemaDiffKind.AlterPrimaryKey)
            .Should()
            .OnlyContain(s => s.PrimaryKeyPhase == PrimaryKeyPhase.None);
    }

    /// <summary>主キーの解除のみ（新主キー列ゼロ）では付与フェーズのセクションが生じないことを検証する</summary>
    [Fact(DisplayName = "主キー解除のみなら付与フェーズのセクションは出ない")]
    public void AlterPrimaryKey_DropOnly_OmitsAddPhaseSection()
    {
        // target に主キー列が無い＝主キーの解除のみ
        var target = new Entity { TableName = "T", Columns = { Col("id", "INT") } };

        var plan = new SyncPlanner().BuildPlan(
            [AlterPk("T", target)],
            new SyncDialectCapabilities()
        );

        var section = plan.Sections.Should().ContainSingle().Which;
        section.Kind.Should().Be(SchemaDiffKind.AlterPrimaryKey);
        section.PrimaryKeyPhase.Should().Be(PrimaryKeyPhase.Drop);
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
            ColumnPairs = [new(parent.Columns[0].Id, child.Columns[1].Id)],
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
                    ForeignKeyColumnPairs = [new("id", "customer_id")],
                    ParentEntity = parent,
                    ChildEntity = child,
                    Relationship = rel,
                    IsSelected = true,
                },
            ],
            RebuildCaps,
            new SyncPlanContext { LiveEntities = [parent] }
        );

        // AddTable も AddForeignKey もセクションには出ない（CreateOnly へ畳まれる）
        plan.Sections.Should().BeEmpty();
        var rb = plan.Rebuilds.Should().ContainSingle().Which;
        rb.CreateOnly.Should().BeTrue();
        rb.TableName.Should().Be("orders");
        rb.CopyColumns.Should().BeEmpty();
        var fk = rb.ForeignKeys.Should().ContainSingle().Which;
        fk.ChildColumns.Should().Equal("customer_id");
        fk.ParentTable.Should().Be("customer");
        fk.ParentColumns.Should().Equal("id");
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
            ColumnPairs = [new(customer.Columns[0].Id, refCol.Id)],
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
            ColumnPairs = [new(supplier.Columns[0].Id, refCol.Id)],
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
                    ForeignKeyColumnPairs = [new("id", "ref_id")],
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
        fk.ChildColumns.Should().Equal("ref_id");
        fk.ParentTable.Should().Be("supplier");
        fk.ParentColumns.Should().Equal("id");
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
    /// 例外にせず（UI のチェック操作でクラッシュさせず）、その外部キーを計画から外して名指しすることを検証する。
    /// </summary>
    [Fact(DisplayName = "AddTable 未選択の新規テーブルへの FK は例外にせず計画から外して警告する")]
    public void AddForeignKey_ToUnselectedNewTable_IsExcludedWithWarning()
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
            ForeignKeyColumnPairs = [new("id", "customer_id")],
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
        plan.Sections.Should().BeEmpty();
        var warning = plan.Warnings.Should().ContainSingle().Which;
        warning.Kind.Should().Be(SyncPlanWarningKind.SelectedItemRequiresUnselectedItem);
        warning.TableName.Should().Be("orders");
        warning.Detail.Should().Be("FK_orders_customers (orders)");
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
            new SyncPlanContext { LiveEntities = [orphanParent] }
        );

        var rb = plan.Rebuilds.Should().ContainSingle().Which;
        rb.CreateOnly.Should().BeTrue();
        rb.ForeignKeys.Should().BeEmpty();
        rb.SourceItems.Should().Equal(addTable);

        var section = plan.Sections.Should().ContainSingle().Which;
        section.Kind.Should().Be(SchemaDiffKind.AddForeignKey);
        section.Items.Should().Equal(unresolvableFk);
    }

    // ---------------- 列順変更（ReorderColumns） ----------------

    /// <summary>ネイティブ列順変更方言（MySQL 相当）のケーパビリティ</summary>
    private static readonly SyncDialectCapabilities NativeCaps = new()
    {
        ColumnReorder = ColumnReorderMode.Native,
    };

    /// <summary>指定名・順のエンティティを組み立てる（列は NULL 許容の通常列）</summary>
    private static Entity Ent(string table, params string[] cols)
    {
        var e = new Entity { TableName = table };

        foreach (var c in cols)
        {
            e.Columns.Add(Col(c, "INT"));
        }

        return e;
    }

    /// <summary>ReorderColumns 差分項目を生成する</summary>
    private static SchemaDiffItem Reorder(string table, Entity target, bool selected = true) =>
        new()
        {
            Kind = SchemaDiffKind.ReorderColumns,
            TableName = table,
            Entity = target,
            IsSelected = selected,
        };

    // ---- rebuild 方言（SQLite） ----

    /// <summary>SQLite で ReorderColumns 単独選択が CreateOnly=false のテーブル再構築になり、target 順へ並ぶことを検証する</summary>
    [Fact(DisplayName = "SQLite: ReorderColumns 単独選択で target 順の再構築になる")]
    public void Rebuild_ReorderColumns_ProducesTargetOrderedRebuild()
    {
        var live = new Entity
        {
            TableName = "t",
            Columns = { PkId(), Col("a", "INT"), Col("b", "INT"), Col("c", "INT") },
        };
        var target = Ent("t", "id", "c", "a", "b");

        var plan = new SyncPlanner().BuildPlan(
            [Reorder("t", target)],
            RebuildCaps,
            new SyncPlanContext { LiveEntities = [live] }
        );

        plan.Sections.Should().BeEmpty();
        plan.Reorders.Should().BeEmpty(); // rebuild 方言はネイティブ並べ替え計画を作らない
        var rb = plan.Rebuilds.Should().ContainSingle().Which;
        rb.CreateOnly.Should().BeFalse();
        rb.NewDefinition.Columns.Select(c => c.Name).Should().Equal("id", "c", "a", "b");
    }

    /// <summary>SQLite で未選択 Drop の残存列が、並べ替え後の NewDefinition 末尾へ回ることを検証する</summary>
    [Fact(DisplayName = "SQLite: 未選択 Drop の残存列は並べ替え後の末尾へ回る")]
    public void Rebuild_ReorderColumns_LeftoverColumnGoesToEnd()
    {
        var live = new Entity
        {
            TableName = "t",
            Columns = { PkId(), Col("a", "INT"), Col("b", "INT"), Col("leftover", "INT") },
        };
        // target には leftover が無い（＝その DropColumn は未選択のまま渡さない）
        var target = Ent("t", "id", "b", "a");

        var plan = new SyncPlanner().BuildPlan(
            [Reorder("t", target)],
            RebuildCaps,
            new SyncPlanContext { LiveEntities = [live] }
        );

        var rb = plan.Rebuilds.Should().ContainSingle().Which;
        // target 列（id,b,a）を目標順に並べ、target に無い leftover は元の相対順のまま末尾へ
        rb.NewDefinition.Columns.Select(c => c.Name).Should().Equal("id", "b", "a", "leftover");
    }

    // ---- Native 方言（MySQL） ----

    /// <summary>Native 方言で単純な入れ替えが最小 1 移動（LIS 不動）＋正しい AFTER になることを検証する</summary>
    [Fact(DisplayName = "MySQL: 単純な並べ替えは最小 1 移動＋AFTER 指定になる")]
    public void Native_Reorder_ProducesMinimalMovesWithAfter()
    {
        // live: id,a,b,c → target: id,c,a,b（c を id の直後へ 1 回動かせば済む＝LIS は id,a,b）
        var live = Ent("t", "id", "a", "b", "c");
        var target = Ent("t", "id", "c", "a", "b");

        var plan = new SyncPlanner().BuildPlan(
            [Reorder("t", target)],
            NativeCaps,
            new SyncPlanContext { LiveEntities = [live] }
        );

        plan.Rebuilds.Should().BeEmpty();
        var reorder = plan.Reorders.Should().ContainSingle().Which;
        reorder.TableName.Should().Be("t");
        var move = reorder.Moves.Should().ContainSingle().Which;
        move.Column.Name.Should().Be("c");
        move.AfterColumn.Should().Be("id");
    }

    /// <summary>Native 方言で先頭へ動かす列は AfterColumn=null（FIRST）になることを検証する</summary>
    [Fact(DisplayName = "MySQL: 先頭へ動かす列は FIRST（AfterColumn=null）")]
    public void Native_Reorder_MoveToFront_ProducesFirst()
    {
        // live: a,b,c → target: c,a,b（c を先頭へ）
        var live = Ent("t", "a", "b", "c");
        var target = Ent("t", "c", "a", "b");

        var plan = new SyncPlanner().BuildPlan(
            [Reorder("t", target)],
            NativeCaps,
            new SyncPlanContext { LiveEntities = [live] }
        );

        var move = plan.Reorders.Single().Moves.Should().ContainSingle().Which;
        move.Column.Name.Should().Be("c");
        move.AfterColumn.Should().BeNull();
    }

    /// <summary>Native 方言で、移動列の定義は未選択 AlterColumn を無視し live 定義を用いることを検証する</summary>
    [Fact(DisplayName = "MySQL: 移動列の定義は未選択 Alter を無視し live 定義になる")]
    public void Native_Reorder_MovedColumnUsesLiveDefinitionWhenAlterUnselected()
    {
        // live: id(INT), a(INT), b(INT) → target: b, id, a（b を先頭へ動かす＝b が確実に移動列）。
        // b の型変更 Alter は未選択
        var live = new Entity
        {
            TableName = "t",
            Columns = { Col("id", "INT"), Col("a", "INT"), Col("b", "INT") },
        };
        var target = Ent("t", "b", "id", "a");

        var unselectedAlter = new SchemaDiffItem
        {
            Kind = SchemaDiffKind.AlterColumn,
            TableName = "t",
            ColumnName = "b",
            Column = Col("b", "BIGINT"),
            IsSelected = false,
        };

        var plan = new SyncPlanner().BuildPlan(
            [Reorder("t", target), unselectedAlter],
            NativeCaps,
            new SyncPlanContext { LiveEntities = [live] }
        );

        var move = plan.Reorders.Single().Moves.Should().ContainSingle().Which;
        move.Column.Name.Should().Be("b");
        // 未選択の Alter（BIGINT）は反映されず live 定義（INT）で動かす
        move.Column.DataType.Should().Be("INT");
    }

    /// <summary>Native 方言で、選択済み AlterColumn がある移動列は新定義を用いることを検証する</summary>
    [Fact(DisplayName = "MySQL: 移動列の定義は選択済み Alter があれば新定義になる")]
    public void Native_Reorder_MovedColumnUsesAlteredDefinitionWhenSelected()
    {
        // b を先頭へ動かす（＝b が確実に移動列）
        var live = new Entity
        {
            TableName = "t",
            Columns = { Col("id", "INT"), Col("a", "INT"), Col("b", "INT") },
        };
        var target = Ent("t", "b", "id", "a");

        var selectedAlter = new SchemaDiffItem
        {
            Kind = SchemaDiffKind.AlterColumn,
            TableName = "t",
            ColumnName = "b",
            Column = Col("b", "BIGINT"),
            IsSelected = true,
        };

        var plan = new SyncPlanner().BuildPlan(
            [Reorder("t", target), selectedAlter],
            NativeCaps,
            new SyncPlanContext { LiveEntities = [live] }
        );

        var move = plan.Reorders.Single().Moves.Should().ContainSingle().Which;
        move.Column.Name.Should().Be("b");
        move.AfterColumn.Should().BeNull();
        move.Column.DataType.Should().Be("BIGINT");
    }

    /// <summary>Native 方言で、選択済み AddColumn の列も実効列順に含めて並べ替えられることを検証する</summary>
    [Fact(DisplayName = "MySQL: 選択済み AddColumn の列も並べ替え対象になる")]
    public void Native_Reorder_IncludesSelectedAddedColumn()
    {
        // live: id,a ＋ 追加 x → 実効: id,a,x。target: x,id,a（追加列 x を先頭へ動かす＝x が確実に移動列）
        var live = new Entity { TableName = "t", Columns = { Col("id", "INT"), Col("a", "INT") } };
        var target = Ent("t", "x", "id", "a");

        var addX = new SchemaDiffItem
        {
            Kind = SchemaDiffKind.AddColumn,
            TableName = "t",
            ColumnName = "x",
            Column = Col("x", "TEXT"),
            IsSelected = true,
        };

        var plan = new SyncPlanner().BuildPlan(
            [Reorder("t", target), addX],
            NativeCaps,
            new SyncPlanContext { LiveEntities = [live] }
        );

        var move = plan.Reorders.Single().Moves.Should().ContainSingle().Which;
        move.Column.Name.Should().Be("x");
        move.AfterColumn.Should().BeNull();
        move.Column.DataType.Should().Be("TEXT"); // AddColumn の新定義で動かす
    }

    /// <summary>Native 方言で既に目標順なら移動が生じず並べ替え計画が空になることを検証する</summary>
    [Fact(DisplayName = "MySQL: 既に目標順なら並べ替え計画は空")]
    public void Native_Reorder_AlreadyOrdered_ProducesNoPlan()
    {
        var live = Ent("t", "id", "a", "b");
        var target = Ent("t", "id", "a", "b");

        var plan = new SyncPlanner().BuildPlan(
            [Reorder("t", target)],
            NativeCaps,
            new SyncPlanContext { LiveEntities = [live] }
        );

        plan.Reorders.Should().BeEmpty();
    }

    /// <summary>Native 方言で live に無いテーブルの ReorderColumns は黙って落ちることを検証する</summary>
    [Fact(DisplayName = "MySQL: live に無いテーブルの ReorderColumns は無視される")]
    public void Native_Reorder_UnknownTable_IsDropped()
    {
        var target = Ent("ghost", "id", "b", "a");

        var plan = new SyncPlanner().BuildPlan(
            [Reorder("ghost", target)],
            NativeCaps,
            new SyncPlanContext { LiveEntities = [] }
        );

        plan.Reorders.Should().BeEmpty();
        plan.IsEmpty.Should().BeTrue();
    }

    /// <summary>Native 方言で ReorderColumns が選択されており context=null なら例外になることを検証する</summary>
    [Fact(DisplayName = "MySQL: ReorderColumns 選択かつ context 省略は例外")]
    public void Native_Reorder_NullContext_Throws()
    {
        var target = Ent("t", "id", "b", "a");

        var act = () => new SyncPlanner().BuildPlan([Reorder("t", target)], NativeCaps);

        act.Should().Throw<InvalidOperationException>();
    }

    // ---------------- 主キー変更（AlterPrimaryKey）と依存 FK の自動 DROP → 再 ADD ----------------

    /// <summary>
    /// FK 参加列・主キー／一意制約の構成列の型変更に、制約の外し直しが必要な方言（SQL Server 相当）のケーパビリティ
    /// </summary>
    private static readonly SyncDialectCapabilities FkRebuildCaps = new()
    {
        AlterColumnRequiresForeignKeyRebuild = true,
        AlterColumnRequiresKeyConstraintRebuild = true,
    };

    /// <summary>customer(id) ← orders(customer_id) の live スキーマと、その FK リレーションを組み立てる</summary>
    private static (
        Entity Customer,
        Entity Orders,
        Relationship Rel,
        SyncPlanContext Context
    ) LiveFkScenario()
    {
        var customer = new Entity { TableName = "customer", Columns = { PkId() } };
        var orders = new Entity
        {
            TableName = "orders",
            Columns = { PkId(), Col("customer_id", "INT") },
        };
        var rel = new Relationship
        {
            SourceEntityId = customer.Id,
            TargetEntityId = orders.Id,
            ColumnPairs = [new(customer.Columns[0].Id, orders.Columns[1].Id)],
            ConstraintName = "FK_orders_customer",
        };

        return (
            customer,
            orders,
            rel,
            new SyncPlanContext { LiveEntities = [customer, orders], LiveRelationships = [rel] }
        );
    }

    /// <summary>AlterPrimaryKey 差分項目を生成する（target＝新しい主キー構成の源）</summary>
    private static SchemaDiffItem AlterPk(string table, Entity target, bool selected = true) =>
        new()
        {
            Kind = SchemaDiffKind.AlterPrimaryKey,
            TableName = table,
            Entity = target,
            IsSelected = selected,
        };

    /// <summary>
    /// 主キーを変更するテーブルを参照している live FK が、自動 DROP（先頭側）＋再 ADD（末尾側）として
    /// 計画へ注入され、レンダラーがそのまま SQL 化できるフィールドで埋まることを検証する。
    /// </summary>
    [Fact(DisplayName = "主キー変更: 参照している live FK が自動で外れて再作成される")]
    public void AlterPrimaryKey_InjectsImplicitForeignKeyDropAndReAdd()
    {
        var (customer, orders, rel, context) = LiveFkScenario();
        // 新しい主キーの列 code は live に在る（無ければ主キーの変更は「選んでいない列の追加」に依存して外れる）
        customer.Columns.Add(
            new Column
            {
                Name = "code",
                DataType = "INT",
                IsNullable = false,
            }
        );
        // 新しい主キー構成（code を主キーにする）
        var targetCustomer = new Entity
        {
            TableName = "customer",
            Columns =
            {
                Col("id", "INT"),
                new Column
                {
                    Name = "code",
                    DataType = "INT",
                    IsPrimaryKey = true,
                    IsNullable = false,
                },
            },
        };

        var plan = new SyncPlanner().BuildPlan(
            [AlterPk("customer", targetCustomer)],
            new SyncDialectCapabilities(),
            context
        );

        // 依存 FK の自動 DROP は主キー解除より前・自動 ADD は主キー付与より後に来る
        // （主キーが存在しない区間の外側で FK を外して戻す）
        plan.Sections.Select(s => s.Kind)
            .Should()
            .Equal(
                SchemaDiffKind.DropForeignKey,
                SchemaDiffKind.AlterPrimaryKey,
                SchemaDiffKind.AlterPrimaryKey,
                SchemaDiffKind.AddForeignKey
            );
        plan.Sections[1].PrimaryKeyPhase.Should().Be(PrimaryKeyPhase.Drop);
        plan.Sections[2].PrimaryKeyPhase.Should().Be(PrimaryKeyPhase.Add);

        var drop = plan
            .Sections.Single(s => s.Kind == SchemaDiffKind.DropForeignKey)
            .Items.Should()
            .ContainSingle()
            .Which;
        drop.TableName.Should().Be("orders");
        drop.ChildEntity.Should().BeSameAs(orders);
        drop.ParentEntity.Should().BeSameAs(customer);
        drop.Relationship.Should().BeSameAs(rel);
        drop.ForeignKeyName.Should().Be("FK_orders_customer");
        drop.IsSelected.Should().BeTrue();

        var add = plan
            .Sections.Single(s => s.Kind == SchemaDiffKind.AddForeignKey)
            .Items.Should()
            .ContainSingle()
            .Which;
        add.TableName.Should().Be("orders");
        add.ColumnName.Should().Be("customer_id");
        add.ChildEntity.Should().BeSameAs(orders);
        add.ParentEntity.Should().BeSameAs(customer);
        add.Relationship.Should().BeSameAs(rel);

        // 説明は自動再作成用の専用文言（カルチャに依らず resx キーから組み立てて照合する）
        add.Description.Should()
            .Be(string.Format(ProviderStrings.Diff_AutoForeignKeyRebuild, "FK_orders_customer"));
    }

    /// <summary>主キーを変更するテーブル自身が子側の FK（外向き）は自動 DROP の対象外であることを検証する</summary>
    [Fact(DisplayName = "主キー変更: 自テーブルから出ている FK は自動で外さない")]
    public void AlterPrimaryKey_DoesNotTouchOutgoingForeignKeys()
    {
        var (_, _, _, context) = LiveFkScenario();
        var targetOrders = new Entity
        {
            TableName = "orders",
            Columns = { Col("id", "INT"), Col("customer_id", "INT") },
        };

        // 主キーを変えるのは子テーブル orders 自身（customer を参照する外向き FK は影響を受けない）
        var plan = new SyncPlanner().BuildPlan(
            [AlterPk("orders", targetOrders)],
            new SyncDialectCapabilities(),
            context
        );

        plan.Sections.Select(s => s.Kind).Should().Equal(SchemaDiffKind.AlterPrimaryKey);
    }

    /// <summary>
    /// FK 参加列の型変更に FK の外し直しが必要な方言では、選択済み AlterColumn の列に紐づく live FK が
    /// 自動 DROP ＋ 再 ADD として注入されることを検証する。
    /// </summary>
    [Fact(DisplayName = "AlterColumn: capability が真の方言では依存 FK が自動で外れて再作成される")]
    public void AlterColumn_WithCapability_InjectsImplicitForeignKeyRebuild()
    {
        var (_, _, _, context) = LiveFkScenario();

        // 型は同じまま NULL 許容だけを変える（両端の型が食い違う変更は外し直さない＝別のテスト）
        var alter = new SchemaDiffItem
        {
            Kind = SchemaDiffKind.AlterColumn,
            TableName = "orders",
            ColumnName = "customer_id",
            Column = new Column
            {
                Name = "customer_id",
                DataType = "INT",
                IsNullable = false,
            },
            IsSelected = true,
        };

        var plan = new SyncPlanner().BuildPlan([alter], FkRebuildCaps, context);

        plan.Sections.Select(s => s.Kind)
            .Should()
            .Equal(
                SchemaDiffKind.DropForeignKey,
                SchemaDiffKind.AlterColumn,
                SchemaDiffKind.AddForeignKey
            );
    }

    /// <summary>親側（被参照列）の型変更でも依存 FK が自動 DROP ＋ 再 ADD になることを検証する</summary>
    [Fact(DisplayName = "AlterColumn: 親側の被参照列の変更でも依存 FK が外れて再作成される")]
    public void AlterColumn_OnParentColumn_InjectsImplicitForeignKeyRebuild()
    {
        var (_, _, _, context) = LiveFkScenario();

        // 両端を同じ型へ変える（片側だけだと付け直しが必ず失敗するため外し直さない）
        var alterParent = new SchemaDiffItem
        {
            Kind = SchemaDiffKind.AlterColumn,
            TableName = "customer",
            ColumnName = "id",
            Column = Col("id", "BIGINT"),
            IsSelected = true,
        };
        var alterChild = new SchemaDiffItem
        {
            Kind = SchemaDiffKind.AlterColumn,
            TableName = "orders",
            ColumnName = "customer_id",
            Column = Col("customer_id", "BIGINT"),
            IsSelected = true,
        };

        var plan = new SyncPlanner().BuildPlan([alterParent, alterChild], FkRebuildCaps, context);

        plan.Sections.Should().Contain(s => s.Kind == SchemaDiffKind.DropForeignKey);
        plan.Sections.Should().Contain(s => s.Kind == SchemaDiffKind.AddForeignKey);
    }

    /// <summary>capability が偽の方言では AlterColumn だけでは FK を自動で外さないことを検証する</summary>
    [Fact(DisplayName = "AlterColumn: capability が偽の方言では FK を自動で外さない")]
    public void AlterColumn_WithoutCapability_DoesNotInjectForeignKeyRebuild()
    {
        var (_, _, _, context) = LiveFkScenario();
        var alter = new SchemaDiffItem
        {
            Kind = SchemaDiffKind.AlterColumn,
            TableName = "orders",
            ColumnName = "customer_id",
            Column = Col("customer_id", "BIGINT"),
            IsSelected = true,
        };

        var plan = new SyncPlanner().BuildPlan([alter], new SyncDialectCapabilities(), context);

        plan.Sections.Select(s => s.Kind).Should().Equal(SchemaDiffKind.AlterColumn);
    }

    /// <summary>
    /// ユーザーが同じ FK の DropForeignKey を明示選択している場合、自動 DROP を重複させず、
    /// かつ再 ADD もしない（削除の意図を尊重する）ことを検証する。
    /// </summary>
    [Fact(DisplayName = "明示的に DROP された FK は自動 DROP も再作成もしない")]
    public void ExplicitlyDroppedForeignKey_IsNeitherDuplicatedNorReAdded()
    {
        var (customer, orders, rel, context) = LiveFkScenario();
        var targetCustomer = new Entity { TableName = "customer", Columns = { Col("id", "INT") } };
        var explicitDrop = new SchemaDiffItem
        {
            Kind = SchemaDiffKind.DropForeignKey,
            TableName = "orders",
            Entity = orders,
            ParentEntity = customer,
            ChildEntity = orders,
            Relationship = rel,
            ForeignKeyName = "FK_orders_customer",
            IsSelected = true,
        };

        var plan = new SyncPlanner().BuildPlan(
            [AlterPk("customer", targetCustomer), explicitDrop],
            new SyncDialectCapabilities(),
            context
        );

        // DropForeignKey はユーザーの明示選択 1 件のみ・再 ADD セクションは生じない
        plan.Sections.Select(s => s.Kind)
            .Should()
            .Equal(SchemaDiffKind.DropForeignKey, SchemaDiffKind.AlterPrimaryKey);
        plan.Sections.Single(s => s.Kind == SchemaDiffKind.DropForeignKey)
            .Items.Should()
            .Equal(explicitDrop);
    }

    /// <summary>live 情報（context）が無ければ FK の自動注入を行わない（防御）ことを検証する</summary>
    [Fact(DisplayName = "context 省略時は FK の自動注入をしない")]
    public void NullContext_SkipsImplicitForeignKeyInjection()
    {
        var targetCustomer = new Entity { TableName = "customer", Columns = { Col("id", "INT") } };

        var plan = new SyncPlanner().BuildPlan(
            [AlterPk("customer", targetCustomer)],
            FkRebuildCaps
        );

        plan.Sections.Select(s => s.Kind).Should().Equal(SchemaDiffKind.AlterPrimaryKey);
    }

    // ---------------- 計画時の警告（SyncPlan.Warnings） ----------------

    /// <summary>
    /// 主キー変更で被参照列が新しい主キーから外れる場合、自動再作成する FK について
    /// 「候補キーでなくなる恐れがある」警告が積まれることを検証する（実行はブロックしない）。
    /// </summary>
    [Fact(DisplayName = "主キー変更: 被参照列が新主キーから外れると候補キー喪失の警告が積まれる")]
    public void AlterPrimaryKey_ReferencedColumnLeavesPrimaryKey_AddsWarning()
    {
        var (customer, _, _, context) = LiveFkScenario();
        // 新しい主キーの列 code は live に在る（無ければ主キーの変更は「選んでいない列の追加」に依存して外れる）
        customer.Columns.Add(
            new Column
            {
                Name = "code",
                DataType = "INT",
                IsNullable = false,
            }
        );
        // 新しい主キーは code のみ＝FK が参照する customer.id は主キーから外れる
        var targetCustomer = new Entity
        {
            TableName = "customer",
            Columns =
            {
                Col("id", "INT"),
                new Column
                {
                    Name = "code",
                    DataType = "INT",
                    IsPrimaryKey = true,
                    IsNullable = false,
                },
            },
        };

        var plan = new SyncPlanner().BuildPlan(
            [AlterPk("customer", targetCustomer)],
            new SyncDialectCapabilities(),
            context
        );

        var warning = plan.Warnings.Should().ContainSingle().Which;
        warning.Kind.Should().Be(SyncPlanWarningKind.ForeignKeyRebuildMayLoseCandidateKey);
        warning.TableName.Should().Be("orders");
        warning.Detail.Should().Be("FK_orders_customer");

        // 一意制約は取り込んでいないため断定できず、警告に留める（FK の自動 DROP → 再 ADD は出る）
        plan.Sections.Should().Contain(s => s.Kind == SchemaDiffKind.AddForeignKey);
    }

    /// <summary>
    /// 被参照列が新しい主キーに残っていても、他の列と複合になるなら候補キーの根拠を失うため
    /// 警告が積まれることを検証する（(id) → (id, code) の主キー拡張）。
    /// </summary>
    /// <remarks>
    /// 注入する FK は常に単列参照のため、複合主キーは参照先の一意性を保証しない
    /// （4 方言中 3 方言で再 ADD が実行時に失敗する）。
    /// </remarks>
    [Fact(
        DisplayName = "主キー変更: 被参照列が複合主キーの一部になると候補キー喪失の警告が積まれる"
    )]
    public void AlterPrimaryKey_ReferencedColumnJoinsCompositePrimaryKey_AddsWarning()
    {
        var (customer, _, _, context) = LiveFkScenario();
        // 新しい主キーの列 code は live に在る（無ければ主キーの変更は「選んでいない列の追加」に依存して外れる）
        customer.Columns.Add(
            new Column
            {
                Name = "code",
                DataType = "INT",
                IsNullable = false,
            }
        );
        // id を主キーに残したまま code を加える（複合主キー化）＝ customer.id 単独の一意性は保証されなくなる
        var targetCustomer = new Entity
        {
            TableName = "customer",
            Columns =
            {
                PkId(),
                new Column
                {
                    Name = "code",
                    DataType = "INT",
                    IsPrimaryKey = true,
                    IsNullable = false,
                },
            },
        };

        var plan = new SyncPlanner().BuildPlan(
            [AlterPk("customer", targetCustomer)],
            new SyncDialectCapabilities(),
            context
        );

        var warning = plan.Warnings.Should().ContainSingle().Which;
        warning.Kind.Should().Be(SyncPlanWarningKind.ForeignKeyRebuildMayLoseCandidateKey);
        warning.TableName.Should().Be("orders");
        plan.Sections.Should().Contain(s => s.Kind == SchemaDiffKind.AddForeignKey);
    }

    /// <summary>新しい主キーが被参照列ちょうど 1 列なら警告が積まれないことを検証する</summary>
    /// <remarks>
    /// 旧主キー (id, code) の先頭の列だけを参照する外部キーは、参照先の索引の先頭の列を要する方言（MySQL の非標準キー）で
    /// 旧主キーに支えられて作れる形なので、その方言の能力で計画する（ほかの方言では、この外部キーは取り込まない
    /// 一意索引に支えられていて主キーの解除に巻き込まれず、外し直さない）。
    /// </remarks>
    [Fact(DisplayName = "主キー変更: 新主キーが被参照列 1 列ちょうどなら警告は積まれない")]
    public void AlterPrimaryKey_NewPrimaryKeyIsExactlyReferencedColumn_AddsNoWarning()
    {
        // live は複合主キー (id, code)・FK は customer.id を参照する
        var code = new Column
        {
            Name = "code",
            DataType = "INT",
            IsPrimaryKey = true,
            IsNullable = false,
        };
        var customer = new Entity { TableName = "customer", Columns = { PkId(), code } };
        var orders = new Entity
        {
            TableName = "orders",
            Columns = { PkId(), Col("customer_id", "INT") },
        };
        var rel = new Relationship
        {
            SourceEntityId = customer.Id,
            TargetEntityId = orders.Id,
            ColumnPairs = [new(customer.Columns[0].Id, orders.Columns[1].Id)],
            ConstraintName = "FK_orders_customer",
        };
        var context = new SyncPlanContext
        {
            LiveEntities = [customer, orders],
            LiveRelationships = [rel],
        };

        // 主キーを (id) へ縮小する＝被参照列 id が単独で候補キーになる
        var targetCustomer = new Entity
        {
            TableName = "customer",
            Columns = { PkId(), Col("code", "INT") },
        };

        var plan = new SyncPlanner().BuildPlan(
            [AlterPk("customer", targetCustomer)],
            new SyncDialectCapabilities { ForeignKeyRequiresLeadingIndex = true },
            context
        );

        plan.Warnings.Should().BeEmpty();
        plan.Sections.Should().Contain(s => s.Kind == SchemaDiffKind.AddForeignKey);
    }

    // ---------------- 複合外部キーの同期（列ペアが正本になった後の挙動） ----------------

    /// <summary>
    /// 複合外部キーと、同じ子テーブルが持つ無関係な単列 FK を含む live を組み立てる。
    /// </summary>
    /// <remarks>
    /// 意味モデルが複合外部キーを表現できるため、live のリレーションは全構成列を保持する。
    /// 以降のテストは「複合外部キーがガードで止められず、全構成列のまま同期対象になる」ことを固定する。
    /// </remarks>
    private static SyncPlanContext CompositeFkScenario()
    {
        var parent = new Entity { TableName = "parent", Columns = { PkId(), Col("code", "INT") } };
        var vendor = new Entity { TableName = "vendor", Columns = { PkId() } };
        var child = new Entity
        {
            TableName = "child",
            Columns =
            {
                PkId(),
                Col("parent_id", "INT"),
                Col("order_no", "INT"),
                Col("vendor_id", "INT"),
                // 複合外部キーにも他の FK にも関与しない列（巻き添えにならないことの担保に使う）
                Col("memo", "TEXT"),
            },
        };
        var composite = new Relationship
        {
            SourceEntityId = parent.Id,
            TargetEntityId = child.Id,
            // 複合構成（parent.id → child.parent_id / parent.code → child.order_no）をそのまま保持する
            ColumnPairs =
            [
                new(parent.Columns[0].Id, child.Columns[1].Id),
                new(parent.Columns[1].Id, child.Columns[2].Id),
            ],
            ConstraintName = "FK_child_parent",
        };
        var simple = new Relationship
        {
            SourceEntityId = vendor.Id,
            TargetEntityId = child.Id,
            ColumnPairs = [new(vendor.Columns[0].Id, child.Columns[3].Id)],
            ConstraintName = "FK_child_vendor",
        };

        return new SyncPlanContext
        {
            LiveEntities = [parent, vendor, child],
            LiveRelationships = [composite, simple],
        };
    }

    /// <summary>
    /// 複合外部キーが参照している親テーブルの主キー変更が、全構成列を保った暗黙の DROP → 再 ADD として
    /// 計画へ入ることを検証する（劣化時代のガード撤去で止まらなくなったことの固定）。
    /// </summary>
    [Fact(DisplayName = "複合外部キー: 参照先テーブルの主キー変更は全構成列のまま計画へ入る")]
    public void AlterPrimaryKey_OnCompositeForeignKeyParent_RebuildsAllColumnPairs()
    {
        var context = CompositeFkScenario();

        // live の主キーを (id, code) にする＝複合外部キーは旧主キーに支えられている（主キーの解除に巻き込まれる）
        var liveCode = context.LiveEntities.Single(e => e.TableName == "parent").Columns[1];
        liveCode.IsPrimaryKey = true;
        liveCode.IsNullable = false;

        // 主キーを (code, id) へ並べ替える。並びを問わない方言では被参照列集合 (id, code) は候補キーであり続ける
        var targetParent = new Entity
        {
            TableName = "parent",
            Columns =
            {
                PkId(),
                new Column
                {
                    Name = "code",
                    DataType = "INT",
                    IsPrimaryKey = true,
                    IsNullable = false,
                },
            },
        };
        targetParent.PrimaryKeyColumnIds = [targetParent.Columns[1].Id, targetParent.Columns[0].Id];

        var plan = new SyncPlanner().BuildPlan(
            [AlterPk("parent", targetParent)],
            new SyncDialectCapabilities { ReferencedKeyRequiresSameColumnOrder = false },
            context
        );

        // 複合外部キーが暗黙の DROP → 再 ADD として注入される
        var drop = plan
            .Sections.Single(s => s.Kind == SchemaDiffKind.DropForeignKey)
            .Items.Should()
            .ContainSingle()
            .Which;
        drop.ForeignKeyName.Should().Be("FK_child_parent");

        var add = plan
            .Sections.Single(s => s.Kind == SchemaDiffKind.AddForeignKey)
            .Items.Should()
            .ContainSingle()
            .Which;

        // 単列へ縮まず、全構成列がそのまま再作成される
        add.ForeignKeyColumnPairs.Select(p => (p.ParentColumn, p.ChildColumn))
            .Should()
            .Equal(("id", "parent_id"), ("code", "order_no"));

        // 被参照列集合 (id, code) は同期後の主キーとちょうど一致するため候補キー喪失の警告も出ない
        plan.Warnings.Should().BeEmpty();
    }

    /// <summary>
    /// 複合外部キーと無関係な親テーブル（同じ子テーブルが持つ別 FK の参照先）の主キー変更では、
    /// 複合外部キーを巻き込まないことを検証する。
    /// </summary>
    [Fact(
        DisplayName = "複合外部キー: 無関係な FK の参照先テーブルの主キー変更は複合 FK を巻き込まない"
    )]
    public void AlterPrimaryKey_OnUnrelatedParent_LeavesCompositeForeignKeyAlone()
    {
        var context = CompositeFkScenario();
        // 主キー構成は変えない（差分項目の有無だけを見る合成ケース）＝候補キー喪失の警告も出ない
        var targetVendor = new Entity { TableName = "vendor", Columns = { PkId() } };

        var plan = new SyncPlanner().BuildPlan(
            [AlterPk("vendor", targetVendor)],
            new SyncDialectCapabilities(),
            context
        );

        plan.Sections.Select(s => s.Kind)
            .Should()
            .Equal(
                SchemaDiffKind.DropForeignKey,
                SchemaDiffKind.AlterPrimaryKey,
                SchemaDiffKind.AlterPrimaryKey,
                SchemaDiffKind.AddForeignKey
            );
        plan.Warnings.Should().BeEmpty();

        // 自動で外して戻すのは無関係な単列 FK だけ（複合外部キーは触らない）
        plan.Sections.Single(s => s.Kind == SchemaDiffKind.AddForeignKey)
            .Items.Should()
            .ContainSingle()
            .Which.Relationship!.ConstraintName.Should()
            .Be("FK_child_vendor");
    }

    /// <summary>
    /// FK 参加列の型変更に FK の外し直しが必要な方言では、複合外部キーの構成列（第 1 列・副構成列とも）の
    /// 定義変更が、全構成列を保った暗黙の DROP → 再 ADD を伴って計画へ入ることを検証する。
    /// </summary>
    /// <remarks>
    /// 構成列はどれを変えても外部キー全体が作り直される。列ペアが正本になったため、その作り直しは
    /// 単列へ縮まない——劣化時代のガードが止めていたのはこの縮退で、その前提そのものが無くなった。
    /// </remarks>
    [Theory(DisplayName = "複合外部キー: 構成列の定義変更は全構成列のままの FK 再作成を伴う")]
    [InlineData("parent_id", "id")]
    [InlineData("order_no", "code")]
    public void AlterColumn_OnCompositeForeignKeyColumn_RebuildsAllColumnPairs(
        string childColumn,
        string parentColumn
    )
    {
        var context = CompositeFkScenario();

        // 1 組の両端を同じ型へ変える（片側だけだと付け直しが必ず失敗するため外し直さない）
        var alterChild = new SchemaDiffItem
        {
            Kind = SchemaDiffKind.AlterColumn,
            TableName = "child",
            ColumnName = childColumn,
            Column = Col(childColumn, "BIGINT"),
            IsSelected = true,
        };
        var alterParent = new SchemaDiffItem
        {
            Kind = SchemaDiffKind.AlterColumn,
            TableName = "parent",
            ColumnName = parentColumn,
            Column = Col(parentColumn, "BIGINT"),
            IsSelected = true,
        };

        var plan = new SyncPlanner().BuildPlan([alterChild, alterParent], FkRebuildCaps, context);

        plan.Sections.Single(s => s.Kind == SchemaDiffKind.AlterColumn)
            .Items.Should()
            .Equal(alterChild, alterParent);

        var add = plan
            .Sections.Single(s => s.Kind == SchemaDiffKind.AddForeignKey)
            .Items.Should()
            .ContainSingle()
            .Which;
        add.Relationship!.ConstraintName.Should().Be("FK_child_parent");
        add.ForeignKeyColumnPairs.Select(p => (p.ParentColumn, p.ChildColumn))
            .Should()
            .Equal(("id", "parent_id"), ("code", "order_no"));

        plan.Warnings.Should().BeEmpty();
    }

    /// <summary>
    /// 複合外部キーに関与しない列の定義変更は、FK の作り直しを一切伴わないことを検証する。
    /// </summary>
    [Fact(DisplayName = "複合外部キー: 無関係な列の定義変更は FK を作り直さない")]
    public void AlterColumn_OnUnrelatedColumn_DoesNotRebuildForeignKey()
    {
        var context = CompositeFkScenario();
        var alter = new SchemaDiffItem
        {
            Kind = SchemaDiffKind.AlterColumn,
            TableName = "child",
            ColumnName = "memo",
            Column = Col("memo", "NVARCHAR(50)"),
            IsSelected = true,
        };

        var plan = new SyncPlanner().BuildPlan([alter], FkRebuildCaps, context);

        plan.Sections.Should().ContainSingle().Which.Items.Should().Equal(alter);
        plan.Warnings.Should().BeEmpty();
    }

    /// <summary>
    /// FK の外し直しが不要な方言では、複合外部キーの構成列の定義変更でも FK を作り直さないことを検証する。
    /// </summary>
    [Fact(
        DisplayName = "複合外部キー: capability が偽の方言では構成列の変更でも FK を作り直さない"
    )]
    public void AlterColumn_OnCompositeForeignKeyColumn_DoesNotRebuildWhenCapabilityIsFalse()
    {
        var context = CompositeFkScenario();
        var alter = new SchemaDiffItem
        {
            Kind = SchemaDiffKind.AlterColumn,
            TableName = "child",
            ColumnName = "parent_id",
            Column = Col("parent_id", "BIGINT"),
            IsSelected = true,
        };

        var plan = new SyncPlanner().BuildPlan([alter], new SyncDialectCapabilities(), context);

        plan.Sections.Should().ContainSingle().Which.Items.Should().Equal(alter);
        plan.Warnings.Should().BeEmpty();
    }

    /// <summary>
    /// rebuild 方言（SQLite）で、複合外部キーの子テーブルも通常どおり再構築されることを検証する。
    /// </summary>
    /// <remarks>
    /// 劣化時代は「再構築すると複合外部キーが単列へ作り替えられる」としてこのテーブルの再構築を止めていた。
    /// 合成後の定義が全構成列を保つようになったため、ブロックは不要になっている。
    /// </remarks>
    [Fact(DisplayName = "SQLite: 複合外部キーの子テーブルも通常どおり再構築される")]
    public void Rebuild_CompositeForeignKeyChildTable_IsRebuiltWithAllColumnPairs()
    {
        var orders = new Entity
        {
            TableName = "orders",
            Columns = { PkId(), Col("line_no", "INT") },
        };
        var orderLine = new Entity
        {
            TableName = "order_line",
            Columns =
            {
                PkId(),
                Col("order_id", "INT"),
                Col("line_no", "INT"),
                Col("note", "TEXT"),
            },
        };
        var composite = new Relationship
        {
            SourceEntityId = orders.Id,
            TargetEntityId = orderLine.Id,
            ColumnPairs =
            [
                new(orders.Columns[0].Id, orderLine.Columns[1].Id),
                new(orders.Columns[1].Id, orderLine.Columns[2].Id),
            ],
            ConstraintName = "FK_order_line_orders",
        };
        var memo = new Entity { TableName = "memo", Columns = { PkId(), Col("note", "TEXT") } };

        var childAlter = new SchemaDiffItem
        {
            Kind = SchemaDiffKind.AlterColumn,
            TableName = "order_line",
            ColumnName = "note",
            Column = Col("note", "INT"),
            IsSelected = true,
        };
        var otherAlter = new SchemaDiffItem
        {
            Kind = SchemaDiffKind.AlterColumn,
            TableName = "memo",
            ColumnName = "note",
            Column = Col("note", "INT"),
            IsSelected = true,
        };

        var plan = new SyncPlanner().BuildPlan(
            [childAlter, otherAlter],
            RebuildCaps,
            new SyncPlanContext
            {
                LiveEntities = [orders, orderLine, memo],
                LiveRelationships = [composite],
            }
        );

        // 両テーブルとも再構築される（複合外部キーによるブロックは無い）
        plan.Rebuilds.Select(r => r.TableName).Should().BeEquivalentTo("order_line", "memo");

        // 再構築後の定義でも外部キーは全構成列を保つ
        var rebuild = plan.Rebuilds.Single(r => r.TableName == "order_line");
        var fk = rebuild.ForeignKeys.Should().ContainSingle().Which;
        fk.ConstraintName.Should().Be("FK_order_line_orders");
        fk.ChildColumns.Should().Equal("order_id", "line_no");
        fk.ParentTable.Should().Be("orders");
        fk.ParentColumns.Should().Equal("id", "line_no");

        // 全項目が再構築へ畳まれるためセクションは残らない
        plan.Sections.Should().BeEmpty();
        plan.Warnings.Should().BeEmpty();
    }

    /// <summary>
    /// rebuild 方言（SQLite）では AlterPrimaryKey がテーブル再構築へ畳まれ、
    /// 合成後の定義に target の主キー構成が反映されることを検証する。
    /// </summary>
    [Fact(
        DisplayName = "SQLite: AlterPrimaryKey は再構築へ畳まれ NewDefinition に PK が反映される"
    )]
    public void Rebuild_AlterPrimaryKey_IsFoldedIntoRebuild()
    {
        var live = new Entity
        {
            TableName = "t",
            Columns = { PkId(), Col("code", "INT"), Col("note", "TEXT") },
        };
        // target: id は主キーでなくなり、code が主キーになる
        var target = new Entity
        {
            TableName = "t",
            Columns =
            {
                Col("id", "INT"),
                new Column
                {
                    Name = "code",
                    DataType = "INT",
                    IsPrimaryKey = true,
                    IsNullable = false,
                },
                Col("note", "TEXT"),
            },
        };

        var plan = new SyncPlanner().BuildPlan(
            [AlterPk("t", target)],
            RebuildCaps,
            new SyncPlanContext { LiveEntities = [live] }
        );

        plan.Sections.Should().BeEmpty();
        var rb = plan.Rebuilds.Should().ContainSingle().Which;
        rb.CreateOnly.Should().BeFalse();
        rb.TableName.Should().Be("t");
        rb.NewDefinition.Columns.Single(c => c.Name == "code").IsPrimaryKey.Should().BeTrue();
        rb.NewDefinition.Columns.Single(c => c.Name == "id").IsPrimaryKey.Should().BeFalse();
        // 主キー変更は列集合を変えないため、全列がデータ移送対象のまま
        rb.CopyColumns.Should().Equal("id", "code", "note");
    }

    // ---------------- 主キー変更の 3 フェーズ出力（レンダラー経由で順序を固定する） ----------------

    /// <summary>
    /// 主キー変更と旧主キー列の NULL 許容化（AlterColumn）を同時に選択したとき、生成スクリプト上で
    /// 「主キー解除 → 列定義変更 → 主キー付与」の順になることを SQL Server レンダラー経由で検証する。
    /// </summary>
    /// <remarks>
    /// DROP と ADD を 1 セクションから連続出力する単一フェーズでは、主キー制約が残ったまま旧主キー列を
    /// NULL 許容へ変更することになり SQL Server では Msg 5074 → 4922 で失敗する（PostgreSQL / MySQL /
    /// Oracle も同種のエラーで、MySQL / Oracle は部分適用のまま止まる）。順序が唯一の解のためテストで固定する。
    /// </remarks>
    [Fact(
        DisplayName = "主キー変更＋旧 PK 列の NULL 許容化は PK DROP → ALTER COLUMN → PK ADD の順"
    )]
    public void AlterPrimaryKey_WithAlterColumn_RendersDropThenAlterThenAdd()
    {
        // 旧主キー列 old_id を NULL 許容へ変更しつつ、主キーを code へ移す
        var target = new Entity
        {
            TableName = "T",
            Columns =
            {
                Col("old_id", "int"),
                new Column
                {
                    Name = "code",
                    DataType = "int",
                    IsPrimaryKey = true,
                    IsNullable = false,
                },
            },
        };
        var alterColumn = new SchemaDiffItem
        {
            Kind = SchemaDiffKind.AlterColumn,
            TableName = "T",
            ColumnName = "old_id",
            Column = Col("old_id", "int"),
            IsSelected = true,
        };

        var plan = new SyncPlanner().BuildPlan(
            [AlterPk("T", target), alterColumn],
            new SyncDialectCapabilities()
        );
        var sql = new SqlServerSyncScriptBuilder().Build(plan);

        var dropPrimaryKey = sql.IndexOf(
            "DROP CONSTRAINT ' + QUOTENAME(@pk);",
            StringComparison.Ordinal
        );
        var alterColumnAt = sql.IndexOf("ALTER COLUMN [old_id]", StringComparison.Ordinal);
        var addPrimaryKey = sql.IndexOf("ADD CONSTRAINT [PK_T]", StringComparison.Ordinal);

        dropPrimaryKey.Should().BeGreaterThan(-1);
        alterColumnAt.Should().BeGreaterThan(dropPrimaryKey);
        addPrimaryKey.Should().BeGreaterThan(alterColumnAt);
    }

    /// <summary>
    /// 主キー変更を含まない計画の出力にフェーズ表記が混ざらないことを固定する
    /// （フェーズを持たないセクションの見出しは種別名のみ）。
    /// </summary>
    [Fact(DisplayName = "主キー変更を含まない計画にフェーズ表記は出ない")]
    public void PlanWithoutPrimaryKeyChange_RendersUnchangedBytes()
    {
        var addColumn = new SchemaDiffItem
        {
            Kind = SchemaDiffKind.AddColumn,
            TableName = "T",
            ColumnName = "Email",
            Column = new Column
            {
                Name = "Email",
                DataType = "nvarchar(200)",
                IsNullable = false,
            },
            IsSelected = true,
        };
        var alterColumn = new SchemaDiffItem
        {
            Kind = SchemaDiffKind.AlterColumn,
            TableName = "T",
            ColumnName = "Memo",
            Column = Col("Memo", "nvarchar(50)"),
            IsSelected = true,
        };

        var plan = new SyncPlanner().BuildPlan(
            [alterColumn, addColumn],
            new SyncDialectCapabilities()
        );
        var sql = new SqlServerSyncScriptBuilder().Build(plan);

        var expected = string.Join(
            Environment.NewLine,
            "-- ===== AddColumn (1 items) =====",
            "ALTER TABLE [T] ADD [Email] nvarchar(200) NOT NULL;",
            "GO",
            "",
            "-- ===== AlterColumn (1 items) =====",
            "ALTER TABLE [T] ALTER COLUMN [Memo] nvarchar(50) NULL;",
            "GO",
            "",
            ""
        );

        sql.Should().Be(expected);
        plan.Sections.Should().OnlyContain(s => s.PrimaryKeyPhase == PrimaryKeyPhase.None);
    }

    /// <summary>None 方言では ReorderColumns 項目が渡されても計画から自然に消えることを検証する</summary>
    [Fact(DisplayName = "None 方言では ReorderColumns は計画から消える")]
    public void NoneDialect_ReorderColumns_DisappearsFromPlan()
    {
        var target = Ent("t", "id", "b", "a");

        var plan = new SyncPlanner().BuildPlan(
            [Reorder("t", target)],
            new SyncDialectCapabilities()
        );

        plan.Reorders.Should().BeEmpty();
        plan.Sections.Should().BeEmpty();
        plan.IsEmpty.Should().BeTrue();
    }

    // ---------------- 一意制約（UNIQUE）の同期 ----------------

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

    /// <summary>一意制約の差分項目を生成する</summary>
    private static SchemaDiffItem UniqueItem(
        SchemaDiffKind kind,
        string table,
        string? name,
        string[] columns,
        bool selected = true
    ) =>
        new()
        {
            Kind = kind,
            TableName = table,
            UniqueConstraintName = name,
            UniqueConstraintColumns = columns,
            IsSelected = selected,
        };

    /// <summary>
    /// 一意制約の解除は FK 解除の直後（列・主キーの変更より前）、追加は FK 追加の直前に並ぶことを検証する。
    /// </summary>
    [Fact(DisplayName = "一意制約セクションは解除＝FK 解除直後・追加＝FK 追加直前に並ぶ")]
    public void UniqueConstraintSections_AreInFixedOrder()
    {
        var plan = new SyncPlanner().BuildPlan(
            [
                Item(SchemaDiffKind.AddForeignKey),
                UniqueItem(SchemaDiffKind.AddUniqueConstraint, "T", null, ["code"]),
                Item(SchemaDiffKind.AlterColumn),
                UniqueItem(SchemaDiffKind.DropUniqueConstraint, "T", "uq_old", ["memo"]),
                Item(SchemaDiffKind.DropForeignKey),
            ],
            new SyncDialectCapabilities()
        );

        plan.Sections.Select(s => s.Kind)
            .Should()
            .Equal(
                SchemaDiffKind.DropForeignKey,
                SchemaDiffKind.DropUniqueConstraint,
                SchemaDiffKind.AlterColumn,
                SchemaDiffKind.AddUniqueConstraint,
                SchemaDiffKind.AddForeignKey
            );
    }

    /// <summary>
    /// 被参照列に自然キーの一意制約が残る場合、主キーを付け替えても候補キー喪失の警告が出ないことを検証する。
    /// </summary>
    /// <remarks>
    /// 「自然キー UNIQUE ＋ 代理キー PK」への移行は実務で頻出する。旧実装は一意制約を知らず
    /// 「新主キーが被参照列 1 列ちょうど」だけを根拠にしていたため、この構成で必ず誤警告していた。
    /// </remarks>
    [Fact(DisplayName = "候補キー証明: 被参照列に UNIQUE が残るなら主キー付け替えでも警告しない")]
    public void AlterPrimaryKey_ReferencedColumnKeptUnique_AddsNoWarning()
    {
        var (customer, orders, rel, _) = LiveFkScenario();
        // live: customer(id) が主キー兼 FK 被参照列。加えて自然キー code に UNIQUE が張られている
        customer.Columns.Add(Col("code", "INT"));
        WithUnique(customer, "UQ_customer_id", "id");
        var context = new SyncPlanContext
        {
            LiveEntities = [customer, orders],
            LiveRelationships = [rel],
        };

        // 主キーを code へ付け替える（被参照列 id は主キーから外れるが UNIQUE で候補キーのまま）
        var targetCustomer = new Entity
        {
            TableName = "customer",
            Columns =
            {
                Col("id", "INT"),
                new Column
                {
                    Name = "code",
                    DataType = "INT",
                    IsPrimaryKey = true,
                    IsNullable = false,
                },
            },
        };

        var plan = new SyncPlanner().BuildPlan(
            [AlterPk("customer", targetCustomer)],
            new SyncDialectCapabilities(),
            context
        );

        plan.Warnings.Should().BeEmpty();
        // 依存 FK の自動 DROP → 再 ADD 自体は行う
        plan.Sections.Should().Contain(s => s.Kind == SchemaDiffKind.AddForeignKey);
    }

    /// <summary>
    /// 一意制約が在っても構成列が被参照列と一致しなければ、候補キーの根拠にならず警告が出ることを検証する。
    /// </summary>
    [Fact(DisplayName = "候補キー証明: UNIQUE が被参照列と一致しなければ警告する")]
    public void AlterPrimaryKey_UniqueConstraintDoesNotCoverReferencedColumn_AddsWarning()
    {
        var (customer, orders, rel, _) = LiveFkScenario();
        customer.Columns.Add(Col("code", "INT"));
        // UNIQUE は (id, code) の複合＝id 単独の一意性は保証しない
        WithUnique(customer, "UQ_customer_id_code", "id", "code");
        var context = new SyncPlanContext
        {
            LiveEntities = [customer, orders],
            LiveRelationships = [rel],
        };

        var targetCustomer = new Entity
        {
            TableName = "customer",
            Columns =
            {
                Col("id", "INT"),
                new Column
                {
                    Name = "code",
                    DataType = "INT",
                    IsPrimaryKey = true,
                    IsNullable = false,
                },
            },
        };

        var plan = new SyncPlanner().BuildPlan(
            [AlterPk("customer", targetCustomer)],
            new SyncDialectCapabilities(),
            context
        );

        var warning = plan.Warnings.Should().ContainSingle().Which;
        warning.Kind.Should().Be(SyncPlanWarningKind.ForeignKeyRebuildMayLoseCandidateKey);
        warning.TableName.Should().Be("orders");
    }

    /// <summary>
    /// 候補キーの根拠にする一意制約が同じ同期で削除される場合は、証明が成立せず警告が出ることを検証する。
    /// </summary>
    /// <remarks>
    /// 「同期後の UNIQUE 集合」は live −選択済み Drop ＋選択済み Add で厳密に合成する。
    /// live に在るだけで証明材料にすると、消えると分かっている制約を根拠にしてしまう。
    /// </remarks>
    [Fact(DisplayName = "候補キー証明: 根拠の UNIQUE を同時に削除するなら警告する")]
    public void AlterPrimaryKey_UniqueConstraintDroppedInSameSync_AddsWarning()
    {
        var (customer, orders, rel, _) = LiveFkScenario();
        customer.Columns.Add(Col("code", "INT"));
        WithUnique(customer, "UQ_customer_id", "id");
        var context = new SyncPlanContext
        {
            LiveEntities = [customer, orders],
            LiveRelationships = [rel],
        };

        var targetCustomer = new Entity
        {
            TableName = "customer",
            Columns =
            {
                Col("id", "INT"),
                new Column
                {
                    Name = "code",
                    DataType = "INT",
                    IsPrimaryKey = true,
                    IsNullable = false,
                },
            },
        };

        var plan = new SyncPlanner().BuildPlan(
            [
                AlterPk("customer", targetCustomer),
                UniqueItem(
                    SchemaDiffKind.DropUniqueConstraint,
                    "customer",
                    "UQ_customer_id",
                    ["id"]
                ),
            ],
            new SyncDialectCapabilities(),
            context
        );

        plan.Warnings.Should()
            .Contain(w => w.Kind == SyncPlanWarningKind.ForeignKeyRebuildMayLoseCandidateKey);
    }

    /// <summary>
    /// 未選択の一意制約追加は候補キーの証明材料にならない（＝警告が出る）ことを検証する。
    /// </summary>
    [Fact(DisplayName = "候補キー証明: 未選択の UNIQUE 追加はあてにしない")]
    public void AlterPrimaryKey_UnselectedUniqueConstraintAdd_AddsWarning()
    {
        var (customer, orders, rel, _) = LiveFkScenario();
        customer.Columns.Add(Col("code", "INT"));
        var context = new SyncPlanContext
        {
            LiveEntities = [customer, orders],
            LiveRelationships = [rel],
        };

        var targetCustomer = new Entity
        {
            TableName = "customer",
            Columns =
            {
                Col("id", "INT"),
                new Column
                {
                    Name = "code",
                    DataType = "INT",
                    IsPrimaryKey = true,
                    IsNullable = false,
                },
            },
        };

        var plan = new SyncPlanner().BuildPlan(
            [
                AlterPk("customer", targetCustomer),
                UniqueItem(
                    SchemaDiffKind.AddUniqueConstraint,
                    "customer",
                    null,
                    ["id"],
                    selected: false
                ),
            ],
            new SyncDialectCapabilities(),
            context
        );

        plan.Warnings.Should()
            .Contain(w => w.Kind == SyncPlanWarningKind.ForeignKeyRebuildMayLoseCandidateKey);
    }

    /// <summary>
    /// SQL Server 相当の方言では、定義変更する列に張られた live の一意制約が自動 DROP → 再 ADD されることを検証する。
    /// </summary>
    [Fact(DisplayName = "一意制約: 構成列の定義変更で自動 DROP → 再 ADD が注入される")]
    public void AlterColumn_OnUniqueConstraintColumn_InjectsImplicitDropAndReAdd()
    {
        var customer = new Entity
        {
            TableName = "customer",
            Columns = { PkId(), Col("code", "INT") },
        };
        WithUnique(customer, "UQ_customer_code", "code");
        var context = new SyncPlanContext { LiveEntities = [customer] };

        var plan = new SyncPlanner().BuildPlan(
            [
                new SchemaDiffItem
                {
                    Kind = SchemaDiffKind.AlterColumn,
                    TableName = "customer",
                    ColumnName = "code",
                    Column = Col("code", "BIGINT"),
                    IsSelected = true,
                },
            ],
            FkRebuildCaps,
            context
        );

        plan.Sections.Select(s => s.Kind)
            .Should()
            .Equal(
                SchemaDiffKind.DropUniqueConstraint,
                SchemaDiffKind.AlterColumn,
                SchemaDiffKind.AddUniqueConstraint
            );

        var drop = plan
            .Sections.Single(s => s.Kind == SchemaDiffKind.DropUniqueConstraint)
            .Items.Should()
            .ContainSingle()
            .Which;
        drop.UniqueConstraintName.Should().Be("UQ_customer_code");
        drop.UniqueConstraintColumns.Should().Equal("code");
        drop.Description.Should()
            .Be(string.Format(ProviderStrings.Diff_AutoUniqueConstraintRebuild, "code"));

        plan.Sections.Single(s => s.Kind == SchemaDiffKind.AddUniqueConstraint)
            .Items.Should()
            .ContainSingle()
            .Which.UniqueConstraintColumns.Should()
            .Equal("code");
    }

    /// <summary>
    /// 明示的に DROP を選択した一意制約は自動 DROP を重複させず、再 ADD もしないことを検証する。
    /// </summary>
    [Fact(DisplayName = "一意制約: 明示 DROP したものは自動 DROP も再作成もしない")]
    public void ExplicitlyDroppedUniqueConstraint_IsNeitherDuplicatedNorReAdded()
    {
        var customer = new Entity
        {
            TableName = "customer",
            Columns = { PkId(), Col("code", "INT") },
        };
        WithUnique(customer, "UQ_customer_code", "code");
        var context = new SyncPlanContext { LiveEntities = [customer] };

        var explicitDrop = UniqueItem(
            SchemaDiffKind.DropUniqueConstraint,
            "customer",
            "UQ_customer_code",
            ["code"]
        );

        var plan = new SyncPlanner().BuildPlan(
            [
                explicitDrop,
                new SchemaDiffItem
                {
                    Kind = SchemaDiffKind.AlterColumn,
                    TableName = "customer",
                    ColumnName = "code",
                    Column = Col("code", "BIGINT"),
                    IsSelected = true,
                },
            ],
            FkRebuildCaps,
            context
        );

        plan.Sections.Select(s => s.Kind)
            .Should()
            .Equal(SchemaDiffKind.DropUniqueConstraint, SchemaDiffKind.AlterColumn);
        plan.Sections.Single(s => s.Kind == SchemaDiffKind.DropUniqueConstraint)
            .Items.Should()
            .Equal(explicitDrop);
    }

    /// <summary>
    /// 外部キーが参照している列の一意制約を削除すると、外部キーが壊れうる警告が積まれることを検証する。
    /// </summary>
    [Fact(DisplayName = "一意制約: 被参照列の UNIQUE 削除は FK 破壊の警告を積む")]
    public void DropUniqueConstraint_OnReferencedColumn_AddsWarning()
    {
        var (customer, orders, rel, _) = LiveFkScenario();
        WithUnique(customer, "UQ_customer_id", "id");
        var context = new SyncPlanContext
        {
            LiveEntities = [customer, orders],
            LiveRelationships = [rel],
        };

        var plan = new SyncPlanner().BuildPlan(
            [UniqueItem(SchemaDiffKind.DropUniqueConstraint, "customer", "UQ_customer_id", ["id"])],
            new SyncDialectCapabilities(),
            context
        );

        var warning = plan.Warnings.Should().ContainSingle().Which;
        warning.Kind.Should().Be(SyncPlanWarningKind.UniqueConstraintDropMayBreakForeignKey);
        warning.TableName.Should().Be("customer");
        warning.Detail.Should().Be("FK_orders_customer");

        // 警告は実行をブロックしない（DROP 自体は計画に残る）
        plan.Sections.Should()
            .ContainSingle()
            .Which.Kind.Should()
            .Be(SchemaDiffKind.DropUniqueConstraint);
    }

    /// <summary>被参照列と関係ない一意制約の削除では FK 破壊の警告が積まれないことを検証する</summary>
    [Fact(DisplayName = "一意制約: 被参照列と無関係な UNIQUE 削除は警告しない")]
    public void DropUniqueConstraint_OnUnrelatedColumn_AddsNoWarning()
    {
        var (customer, orders, rel, _) = LiveFkScenario();
        customer.Columns.Add(Col("code", "INT"));
        WithUnique(customer, "UQ_customer_code", "code");
        var context = new SyncPlanContext
        {
            LiveEntities = [customer, orders],
            LiveRelationships = [rel],
        };

        var plan = new SyncPlanner().BuildPlan(
            [
                UniqueItem(
                    SchemaDiffKind.DropUniqueConstraint,
                    "customer",
                    "UQ_customer_code",
                    ["code"]
                ),
            ],
            new SyncDialectCapabilities(),
            context
        );

        plan.Warnings.Should().BeEmpty();
    }

    /// <summary>
    /// 再構築方言（SQLite）では一意制約の追加・削除が合成スキーマへ畳み込まれることを検証する。
    /// </summary>
    [Fact(DisplayName = "再構築方言: 一意制約の追加・削除は合成スキーマへ畳まれる")]
    public void RebuildDialect_UniqueConstraintChanges_AreFoldedIntoSynthesizedSchema()
    {
        var live = new Entity
        {
            TableName = "product",
            Columns = { PkId(), Col("sku", "TEXT"), Col("legacy", "TEXT") },
        };
        WithUnique(live, name: null, "legacy");

        var plan = new SyncPlanner().BuildPlan(
            [
                UniqueItem(SchemaDiffKind.DropUniqueConstraint, "product", null, ["legacy"]),
                UniqueItem(SchemaDiffKind.AddUniqueConstraint, "product", null, ["sku"]),
            ],
            RebuildCaps,
            new SyncPlanContext { LiveEntities = [live] }
        );

        // 一意制約の変更だけでもテーブル再構築が起きる（セクションへは残らない）
        plan.Sections.Should().BeEmpty();
        var rebuild = plan.Rebuilds.Should().ContainSingle().Which;
        rebuild.CreateOnly.Should().BeFalse();

        // 合成後は legacy の制約が消え、sku の制約が加わる（構成列は合成後の列 ID で解決できる）
        var constraint = rebuild.NewDefinition.UniqueConstraints.Should().ContainSingle().Which;
        constraint
            .ColumnIds.Select(id => rebuild.NewDefinition.Columns.Single(c => c.Id == id).Name)
            .Should()
            .Equal("sku");
    }

    // ---------------- 再構築で失われる列レベル属性の警告 ----------------

    /// <summary>1 列だけの単純な live テーブル（列型変更で再構築を誘発するための土台）</summary>
    private static Entity RebuildTargetLive() =>
        new() { TableName = "items", Columns = { PkId(), Col("note", "TEXT") } };

    /// <summary>note 列の型変更（＝再構築トリガー）1 件を作る</summary>
    private static SchemaDiffItem AlterNoteColumn() =>
        new()
        {
            Kind = SchemaDiffKind.AlterColumn,
            TableName = "items",
            ColumnName = "note",
            Column = Col("note", "VARCHAR(200)"),
            OldColumn = Col("note", "TEXT"),
            IsSelected = true,
        };

    /// <summary>再構築対象テーブルの live DDL に属性があれば、種別を列挙した警告を積むことを検証する</summary>
    [Fact(DisplayName = "再構築: live DDL の列属性を警告する")]
    public void Rebuild_WarnsAboutDroppedColumnAttributes()
    {
        var plan = new SyncPlanner().BuildPlan(
            [AlterNoteColumn()],
            RebuildCaps,
            new SyncPlanContext
            {
                LiveEntities = [RebuildTargetLive()],
                TableCreateSql = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["items"] =
                        "CREATE TABLE items (id INTEGER PRIMARY KEY AUTOINCREMENT, note TEXT DEFAULT 'x')",
                },
            }
        );

        var warning = plan
            .Warnings.Should()
            .ContainSingle(w => w.Kind == SyncPlanWarningKind.TableRebuildDropsColumnAttribute)
            .Which;
        warning.TableName.Should().Be("items");
        warning.Detail.Should().Be("AUTOINCREMENT, DEFAULT");
    }

    /// <summary>属性を持たない live DDL では警告を出さないことを検証する（過剰警告で本当の喪失を埋もれさせない）</summary>
    [Fact(DisplayName = "再構築: 属性のない DDL では警告しない")]
    public void Rebuild_DoesNotWarnWhenNoAttributes()
    {
        var plan = new SyncPlanner().BuildPlan(
            [AlterNoteColumn()],
            RebuildCaps,
            new SyncPlanContext
            {
                LiveEntities = [RebuildTargetLive()],
                TableCreateSql = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["items"] = "CREATE TABLE items (id INTEGER, note TEXT, PRIMARY KEY(id))",
                },
            }
        );

        plan.Rebuilds.Should().ContainSingle();
        plan.Warnings.Should().BeEmpty();
    }

    /// <summary>再構築されないテーブルの属性は警告しないことを検証する（対象テーブルに限る）</summary>
    [Fact(DisplayName = "再構築: 対象外テーブルの属性は警告しない")]
    public void Rebuild_DoesNotWarnForUnrelatedTables()
    {
        var plan = new SyncPlanner().BuildPlan(
            [AlterNoteColumn()],
            RebuildCaps,
            new SyncPlanContext
            {
                LiveEntities =
                [
                    RebuildTargetLive(),
                    new Entity { TableName = "others", Columns = { PkId() } },
                ],
                TableCreateSql = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["items"] = "CREATE TABLE items (id INTEGER, note TEXT, PRIMARY KEY(id))",
                    ["others"] = "CREATE TABLE others (id INTEGER PRIMARY KEY AUTOINCREMENT)",
                },
            }
        );

        plan.Warnings.Should().BeEmpty();
    }

    /// <summary>新規テーブル作成（CreateOnly）では警告しないことを検証する（失う元の定義が無い）</summary>
    [Fact(DisplayName = "再構築: 新規テーブル作成では警告しない")]
    public void Rebuild_DoesNotWarnForCreateOnly()
    {
        var added = new Entity { TableName = "fresh", Columns = { PkId() } };
        var plan = new SyncPlanner().BuildPlan(
            [
                new SchemaDiffItem
                {
                    Kind = SchemaDiffKind.AddTable,
                    TableName = "fresh",
                    Entity = added,
                    IsSelected = true,
                },
            ],
            RebuildCaps,
            new SyncPlanContext
            {
                TableCreateSql = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["fresh"] = "CREATE TABLE fresh (id INTEGER PRIMARY KEY AUTOINCREMENT)",
                },
            }
        );

        plan.Rebuilds.Should().ContainSingle().Which.CreateOnly.Should().BeTrue();
        plan.Warnings.Should().BeEmpty();
    }

    /// <summary>DDL を取得できない方言（TableCreateSql が空）では警告が 1 件も出ないことを検証する</summary>
    [Fact(DisplayName = "再構築: live DDL が無ければ警告しない")]
    public void Rebuild_DoesNotWarnWithoutLiveDdl()
    {
        var plan = new SyncPlanner().BuildPlan(
            [AlterNoteColumn()],
            RebuildCaps,
            new SyncPlanContext { LiveEntities = [RebuildTargetLive()] }
        );

        plan.Rebuilds.Should().ContainSingle();
        plan.Warnings.Should().BeEmpty();
    }

    // ---------------- 再構築時の主キー順序（PrimaryKeyColumnIds）の張り直し ----------------

    /// <summary>主キー 2 列（a・b）と通常列 memo を持つテーブルを作る（主キーの順序はテスト側で与える）</summary>
    private static Entity PairTable(string[] pkOrder)
    {
        var a = new Column
        {
            Name = "a",
            DataType = "INT",
            IsPrimaryKey = pkOrder.Contains("a"),
            IsNullable = false,
        };
        var b = new Column
        {
            Name = "b",
            DataType = "INT",
            IsPrimaryKey = pkOrder.Contains("b"),
            IsNullable = false,
        };
        var entity = new Entity { TableName = "pair", Columns = { a, b, Col("memo", "TEXT") } };

        entity.PrimaryKeyColumnIds = pkOrder
            .Select(name => entity.Columns.First(c => c.Name == name).Id)
            .ToList();

        return entity;
    }

    /// <summary>
    /// 選択された AlterPrimaryKey により、合成後定義の主キー順序が target の実効順（列名写像）へ張り直ることを検証する。
    /// </summary>
    /// <remarks>
    /// target は live とは別の図＝列 Guid の空間が違うため、live 由来の順序リストをそのまま残すと
    /// 上書きした主キーの膜と順序が食い違う（実効順が黙って列宣言順へ落ちる）。
    /// </remarks>
    [Fact(
        DisplayName = "再構築: 選択された AlterPrimaryKey は主キー順序を target の実効順へ張り直す"
    )]
    public void Rebuild_SelectedAlterPrimaryKey_RemapsPrimaryKeyOrder()
    {
        // live の主キー順は (a, b)、target は (b, a)
        var live = PairTable(["a", "b"]);
        var target = PairTable(["b", "a"]);

        var plan = new SyncPlanner().BuildPlan(
            [
                new SchemaDiffItem
                {
                    Kind = SchemaDiffKind.AlterPrimaryKey,
                    TableName = "pair",
                    Entity = target,
                    IsSelected = true,
                },
            ],
            RebuildCaps,
            new SyncPlanContext { LiveEntities = [live] }
        );

        var rb = plan.Rebuilds.Should().ContainSingle().Which;
        var newDef = rb.NewDefinition;

        // 順序リストは合成後定義の列 Guid を指す（live・target いずれの Guid でもない）
        newDef
            .PrimaryKeyColumnIds.Should()
            .Equal(
                newDef.Columns.First(c => c.Name == "b").Id,
                newDef.Columns.First(c => c.Name == "a").Id
            );
        newDef.GetPrimaryKeyColumnsInOrder().Select(c => c.Name).Should().Equal("b", "a");
    }

    /// <summary>AlterPrimaryKey が未選択なら主キーの順序が live の複製のまま残ることを検証する</summary>
    [Fact(DisplayName = "再構築: AlterPrimaryKey が未選択なら主キー順序は live のまま")]
    public void Rebuild_UnselectedAlterPrimaryKey_KeepsLivePrimaryKeyOrder()
    {
        // live の主キー順は (b, a)。target の (a, b) は選択されていないので適用されない
        var live = PairTable(["b", "a"]);
        var target = PairTable(["a", "b"]);

        var plan = new SyncPlanner().BuildPlan(
            [
                new SchemaDiffItem
                {
                    Kind = SchemaDiffKind.AlterPrimaryKey,
                    TableName = "pair",
                    Entity = target,
                    IsSelected = false,
                },
                // 再構築そのものは別の選択済み差分（通常列の型変更）で起こす
                new SchemaDiffItem
                {
                    Kind = SchemaDiffKind.AlterColumn,
                    TableName = "pair",
                    ColumnName = "memo",
                    Column = Col("memo", "NVARCHAR(200)"),
                    IsSelected = true,
                },
            ],
            RebuildCaps,
            new SyncPlanContext { LiveEntities = [live] }
        );

        var rb = plan.Rebuilds.Should().ContainSingle().Which;
        rb.NewDefinition.GetPrimaryKeyColumnsInOrder().Select(c => c.Name).Should().Equal("b", "a");
    }

    // ---------------- 再構築の合成は live の列を保つ（図は別 Guid 空間） ----------------

    /// <summary>
    /// AlterColumn は live の列オブジェクト（Id）を保って型・NULL 許容・計算列だけを写し、
    /// live の一意制約・主キー順序が合成後も解決できることを検証する。
    /// </summary>
    /// <remarks>
    /// 差分項目の列は図側（live と別の Guid）。差し替えると live の一意制約・主キー順序の列 ID が
    /// 宙に浮き、一意制約が再構築後の表から消え、複合主キーの順序も変わる。
    /// </remarks>
    [Fact(DisplayName = "再構築: AlterColumn は live の列 ID を保ち一意制約と主キー順序を壊さない")]
    public void Rebuild_AlterColumn_KeepsLiveColumnIdentity()
    {
        var live = WithUnique(PairTable(["a", "b"]), null, "a");
        var liveA = live.Columns.Single(c => c.Name == "a");

        var plan = new SyncPlanner().BuildPlan(
            [
                new SchemaDiffItem
                {
                    Kind = SchemaDiffKind.AlterColumn,
                    TableName = "pair",
                    ColumnName = "a",
                    // 図側の列（別 Guid）。型・NULL 許容・計算列が変わる
                    Column = new Column
                    {
                        Name = "a",
                        DataType = "BIGINT",
                        IsPrimaryKey = true,
                        IsNullable = false,
                        IsComputed = true,
                    },
                    IsSelected = true,
                },
            ],
            RebuildCaps,
            new SyncPlanContext { LiveEntities = [live] }
        );

        var newDef = plan.Rebuilds.Should().ContainSingle().Which.NewDefinition;
        var a = newDef.Columns.Single(c => c.Name == "a");
        a.Id.Should().Be(liveA.Id);
        a.DataType.Should().Be("BIGINT");
        a.IsComputed.Should().BeTrue();
        newDef.GetPrimaryKeyColumnsInOrder().Select(c => c.Name).Should().Equal("a", "b");
        newDef.UniqueConstraints.Should().ContainSingle().Which.ColumnIds.Should().Equal(liveA.Id);
    }

    /// <summary>AlterPrimaryKey を選ばない限り、AlterColumn・AddColumn は図側の主キー指定を持ち込まない</summary>
    [Fact(
        DisplayName = "再構築: AlterPrimaryKey 未選択なら AlterColumn・AddColumn は主キーを変えない"
    )]
    public void Rebuild_ColumnChangesWithoutAlterPrimaryKey_KeepLivePrimaryKey()
    {
        var live = new Entity { TableName = "items", Columns = { PkId(), Col("code", "TEXT") } };

        var plan = new SyncPlanner().BuildPlan(
            [
                new SchemaDiffItem
                {
                    Kind = SchemaDiffKind.AlterColumn,
                    TableName = "items",
                    ColumnName = "code",
                    Column = new Column
                    {
                        Name = "code",
                        DataType = "VARCHAR(20)",
                        IsPrimaryKey = true,
                        IsNullable = false,
                    },
                    IsSelected = true,
                },
                new SchemaDiffItem
                {
                    Kind = SchemaDiffKind.AddColumn,
                    TableName = "items",
                    ColumnName = "k2",
                    Column = new Column
                    {
                        Name = "k2",
                        DataType = "TEXT",
                        IsPrimaryKey = true,
                        IsNullable = false,
                    },
                    IsSelected = true,
                },
            ],
            RebuildCaps,
            new SyncPlanContext { LiveEntities = [live] }
        );

        var newDef = plan.Rebuilds.Should().ContainSingle().Which.NewDefinition;
        newDef.GetPrimaryKeyColumnsInOrder().Select(c => c.Name).Should().Equal("id");
        newDef.Columns.Single(c => c.Name == "k2").IsNullable.Should().BeFalse();
    }

    /// <summary>AlterPrimaryKey を選べば、追加列も主キーの構成へ入る（主キーを決めるのは AlterPrimaryKey）</summary>
    [Fact(DisplayName = "再構築: AlterPrimaryKey を選べば追加列も主キーになる")]
    public void Rebuild_AddColumnWithAlterPrimaryKey_JoinsPrimaryKey()
    {
        var live = new Entity { TableName = "items", Columns = { PkId(), Col("note", "TEXT") } };
        var target = new Entity
        {
            TableName = "items",
            Columns =
            {
                PkId(),
                Col("note", "TEXT"),
                new Column
                {
                    Name = "k2",
                    DataType = "TEXT",
                    IsPrimaryKey = true,
                    IsNullable = false,
                },
            },
        };

        var plan = new SyncPlanner().BuildPlan(
            [
                new SchemaDiffItem
                {
                    Kind = SchemaDiffKind.AddColumn,
                    TableName = "items",
                    ColumnName = "k2",
                    Column = target.Columns[2],
                    IsSelected = true,
                },
                AlterPk("items", target),
            ],
            RebuildCaps,
            new SyncPlanContext { LiveEntities = [live] }
        );

        plan.Rebuilds.Should()
            .ContainSingle()
            .Which.NewDefinition.GetPrimaryKeyColumnsInOrder()
            .Select(c => c.Name)
            .Should()
            .Equal("id", "k2");
    }

    // ---------------- 再構築での候補キー喪失の警告 ----------------

    /// <summary>主キー id・自然キー code の親と、id を参照する子の live スキーマを作る</summary>
    private static (Entity Parent, Entity Child, Relationship Fk) CandidateKeyScenario()
    {
        var parent = new Entity
        {
            TableName = "parent",
            Columns =
            {
                PkId(),
                new Column
                {
                    Name = "code",
                    DataType = "TEXT",
                    IsNullable = false,
                },
            },
        };
        var child = new Entity
        {
            TableName = "child",
            Columns = { PkId(), Col("parent_id", "INT") },
        };
        var fk = new Relationship
        {
            SourceEntityId = parent.Id,
            TargetEntityId = child.Id,
            ColumnPairs = { new RelationshipColumnPair(parent.Columns[0].Id, child.Columns[1].Id) },
        };

        return (parent, child, fk);
    }

    /// <summary>親の主キーを id から code へ付け替える AlterPrimaryKey（図側の親）を作る</summary>
    private static SchemaDiffItem MovePrimaryKeyToCode()
    {
        var target = new Entity
        {
            TableName = "parent",
            Columns =
            {
                Col("id", "INT"),
                new Column
                {
                    Name = "code",
                    DataType = "TEXT",
                    IsPrimaryKey = true,
                    IsNullable = false,
                },
            },
        };

        return AlterPk("parent", target);
    }

    /// <summary>
    /// 再構築の方言でも、参照される主キーの付け替えで被参照列が候補キーでなくなる外部キーを警告することを検証する。
    /// </summary>
    [Fact(DisplayName = "再構築: 参照される主キーの付け替えで候補キーを失う外部キーを警告する")]
    public void Rebuild_AlterReferencedPrimaryKey_WarnsCandidateKeyLoss()
    {
        var (parent, child, fk) = CandidateKeyScenario();

        var plan = new SyncPlanner().BuildPlan(
            [MovePrimaryKeyToCode()],
            RebuildCaps,
            new SyncPlanContext { LiveEntities = [parent, child], LiveRelationships = [fk] }
        );

        var warning = plan
            .Warnings.Should()
            .ContainSingle(w => w.Kind == SyncPlanWarningKind.ForeignKeyRebuildMayLoseCandidateKey)
            .Which;
        warning.TableName.Should().Be("child");
        warning.Detail.Should().Be("FK_child_parent");
    }

    /// <summary>
    /// 被参照列に一意制約がある（候補キーのまま）・子表を削除する場合は、再構築の方言でも警告しないことを検証する。
    /// </summary>
    [Fact(
        DisplayName = "再構築: 被参照列に一意制約がある・子表を削除するなら候補キー喪失を警告しない"
    )]
    public void Rebuild_AlterReferencedPrimaryKey_NoWarningWhenProvable()
    {
        var (parent, child, fk) = CandidateKeyScenario();
        var parentWithUnique = WithUnique(parent, null, "id");

        var withUnique = new SyncPlanner().BuildPlan(
            [MovePrimaryKeyToCode()],
            RebuildCaps,
            new SyncPlanContext
            {
                LiveEntities = [parentWithUnique, child],
                LiveRelationships = [fk],
            }
        );

        var childDropped = new SyncPlanner().BuildPlan(
            [
                MovePrimaryKeyToCode(),
                new SchemaDiffItem
                {
                    Kind = SchemaDiffKind.DropTable,
                    TableName = "child",
                    Entity = child,
                    IsSelected = true,
                },
            ],
            RebuildCaps,
            new SyncPlanContext { LiveEntities = [parent, child], LiveRelationships = [fk] }
        );

        withUnique.Warnings.Should().BeEmpty();
        childDropped.Warnings.Should().BeEmpty();
    }

    // ---------------- 外部キーの作り直し（AlterForeignKey） ----------------

    /// <summary>
    /// <see cref="LiveFkScenario"/> の外部キーの ON DELETE だけを Cascade へ変えた図と差分を取り、
    /// 外部キー変更の項目を選んだ状態で返す。
    /// </summary>
    private static SchemaDiffItem SelectedForeignKeyAlter(
        SyncPlanContext context,
        SyncDialectCapabilities? capabilities = null
    )
    {
        var targetEntities = context.LiveEntities.Select(e => e.Clone(preserveId: true)).ToList();
        var liveRel = context.LiveRelationships[0];
        var targetRel = new Relationship
        {
            SourceEntityId = liveRel.SourceEntityId,
            TargetEntityId = liveRel.TargetEntityId,
            ColumnPairs = [.. liveRel.ColumnPairs.Select(p => p.Clone())],
            ConstraintName = liveRel.ConstraintName,
            OnDelete = ForeignKeyReferentialAction.Cascade,
        };

        var alter = new SchemaDiffService()
            .Compute(
                context.LiveEntities,
                context.LiveRelationships,
                targetEntities,
                [targetRel],
                capabilities
            )
            .Items.Single(i => i.Kind == SchemaDiffKind.AlterForeignKey);
        alter.IsSelected = true;
        return alter;
    }

    /// <summary>選んだ外部キー変更は、外す側の DROP と付ける側の ADD の 2 セクションへ展開されることを検証する</summary>
    [Fact(DisplayName = "外部キー変更: 選択すると旧定義の DROP と新定義の ADD へ展開される")]
    public void AlterForeignKey_Selected_ExpandsToDropAndAdd()
    {
        var (_, _, _, context) = LiveFkScenario();

        var plan = new SyncPlanner().BuildPlan(
            [SelectedForeignKeyAlter(context)],
            new SyncDialectCapabilities(),
            context
        );

        var drop = plan
            .Sections.Single(s => s.Kind == SchemaDiffKind.DropForeignKey)
            .Items.Should()
            .ContainSingle()
            .Which;
        drop.ForeignKeyName.Should().Be("FK_orders_customer");
        drop.Relationship!.OnDelete.Should().Be(ForeignKeyReferentialAction.NoAction);

        plan.Sections.Single(s => s.Kind == SchemaDiffKind.AddForeignKey)
            .Items.Should()
            .ContainSingle()
            .Which.Relationship!.OnDelete.Should()
            .Be(ForeignKeyReferentialAction.Cascade);
    }

    /// <summary>選んでいない外部キー変更は計画に何も出さない（既定のまま実行しても名前の重複で失敗しない）</summary>
    [Fact(DisplayName = "外部キー変更: 未選択なら計画は空")]
    public void AlterForeignKey_Unselected_ProducesEmptyPlan()
    {
        var (_, _, _, context) = LiveFkScenario();
        var alter = SelectedForeignKeyAlter(context);
        alter.IsSelected = false;

        new SyncPlanner()
            .BuildPlan([alter], new SyncDialectCapabilities(), context)
            .IsEmpty.Should()
            .BeTrue();
    }

    /// <summary>
    /// 外部キー変更の DROP は明示的な削除として扱われ、主キー変更に伴う暗黙の外し直しと重複せず、
    /// 旧い定義を付け直さないことを検証する。
    /// </summary>
    [Fact(DisplayName = "外部キー変更: 主キー変更の暗黙の外し直しと重複せず旧定義を付け直さない")]
    public void AlterForeignKey_WithReferencedPrimaryKeyChange_DoesNotDuplicate()
    {
        var (customer, _, _, context) = LiveFkScenario();

        var plan = new SyncPlanner().BuildPlan(
            [
                SelectedForeignKeyAlter(context),
                AlterPk("customer", customer.Clone(preserveId: false)),
            ],
            new SyncDialectCapabilities(),
            context
        );

        plan.Sections.Single(s => s.Kind == SchemaDiffKind.DropForeignKey)
            .Items.Should()
            .ContainSingle();
        plan.Sections.Single(s => s.Kind == SchemaDiffKind.AddForeignKey)
            .Items.Should()
            .ContainSingle()
            .Which.Relationship!.OnDelete.Should()
            .Be(ForeignKeyReferentialAction.Cascade);
    }

    // ---------------- 主キー・外部キーの暗黙の外し直し（SQL Server・MySQL） ----------------

    /// <summary>MySQL 相当のケーパビリティ（外部キーは外し直す・主キー／一意制約は外さない・自表の外部キーも外し直す）</summary>
    private static readonly SyncDialectCapabilities MySqlLikeCaps = new()
    {
        ColumnReorder = ColumnReorderMode.Native,
        AlterColumnRequiresForeignKeyRebuild = true,
        ForeignKeyRequiresLeadingIndex = true,
    };

    /// <summary>
    /// 主キーの構成列の型を変えると、主キー変更を選んでいなくても、同じ構成の主キーを外して付け直す項目が
    /// 「解除 → 列定義変更 → 付与」の 3 フェーズで入ることを検証する（SQL Server の Msg 5074 対策）。
    /// </summary>
    [Fact(DisplayName = "暗黙の主キー: 主キー列の型変更で同じ構成の主キーを外して付け直す")]
    public void AlterColumn_OnPrimaryKeyColumn_InjectsImplicitPrimaryKeyRebuild()
    {
        var live = PairTable(["b", "a"]);
        var alter = new SchemaDiffItem
        {
            Kind = SchemaDiffKind.AlterColumn,
            TableName = "pair",
            ColumnName = "a",
            Column = new Column
            {
                Name = "a",
                DataType = "BIGINT",
                IsPrimaryKey = true,
                IsNullable = false,
            },
            IsSelected = true,
        };

        var plan = new SyncPlanner().BuildPlan(
            [alter],
            FkRebuildCaps,
            new SyncPlanContext { LiveEntities = [live] }
        );

        plan.Sections.Select(s => (s.Kind, s.PrimaryKeyPhase))
            .Should()
            .Equal(
                (SchemaDiffKind.AlterPrimaryKey, PrimaryKeyPhase.Drop),
                (SchemaDiffKind.AlterColumn, PrimaryKeyPhase.None),
                (SchemaDiffKind.AlterPrimaryKey, PrimaryKeyPhase.Add)
            );

        // 付け直す主キーは live の構成・順序のまま
        plan.Sections.Last()
            .Items.Should()
            .ContainSingle()
            .Which.Entity!.GetPrimaryKeyColumnsInOrder()
            .Select(c => c.Name)
            .Should()
            .Equal("b", "a");
    }

    /// <summary>主キー変更を選んでいれば、その変更が解除と付与を行うので暗黙の主キー項目は足さない</summary>
    [Fact(DisplayName = "暗黙の主キー: 主キー変更を選んでいれば重ねて注入しない")]
    public void AlterColumn_OnPrimaryKeyColumnWithAlterPrimaryKey_DoesNotDuplicate()
    {
        var live = PairTable(["a", "b"]);
        var alter = new SchemaDiffItem
        {
            Kind = SchemaDiffKind.AlterColumn,
            TableName = "pair",
            ColumnName = "a",
            Column = Col("a", "BIGINT"),
            IsSelected = true,
        };

        var plan = new SyncPlanner().BuildPlan(
            [alter, AlterPk("pair", PairTable(["b", "a"]))],
            FkRebuildCaps,
            new SyncPlanContext { LiveEntities = [live] }
        );

        plan.Sections.Where(s => s.Kind == SchemaDiffKind.AlterPrimaryKey)
            .Should()
            .OnlyContain(s => s.Items.Count == 1);
    }

    /// <summary>主キー・一意制約を外さなくても列を変えられる方言（MySQL）では、暗黙の主キー・一意制約を注入しない</summary>
    [Fact(DisplayName = "暗黙の主キー: MySQL 相当では主キー・一意制約を外し直さない")]
    public void AlterColumn_OnKeyColumns_MySqlLike_DoesNotRebuildKeys()
    {
        var live = WithUnique(PairTable(["a", "b"]), "UQ_pair_memo", "memo");

        var plan = new SyncPlanner().BuildPlan(
            [
                new SchemaDiffItem
                {
                    Kind = SchemaDiffKind.AlterColumn,
                    TableName = "pair",
                    ColumnName = "a",
                    Column = Col("a", "BIGINT"),
                    IsSelected = true,
                },
                new SchemaDiffItem
                {
                    Kind = SchemaDiffKind.AlterColumn,
                    TableName = "pair",
                    ColumnName = "memo",
                    Column = Col("memo", "VARCHAR(20)"),
                    IsSelected = true,
                },
            ],
            MySqlLikeCaps,
            new SyncPlanContext { LiveEntities = [live] }
        );

        plan.Sections.Select(s => s.Kind).Should().Equal(SchemaDiffKind.AlterColumn);
    }

    /// <summary>
    /// 外部キーの片側の列だけ型を変えると、同期後に両端の型が食い違うため外部キーを外し直さず
    /// （＝列定義変更の時点で失敗させる）、その外部キーを警告で名指しすることを検証する。
    /// </summary>
    [Theory(DisplayName = "型の食い違い: 片側だけの型変更では外部キーを外し直さず警告する")]
    [InlineData("orders", "customer_id")]
    [InlineData("customer", "id")]
    public void AlterColumn_OneSideOfForeignKey_WarnsAndDoesNotInject(string table, string column)
    {
        var (_, _, _, context) = LiveFkScenario();

        var plan = new SyncPlanner().BuildPlan(
            [
                new SchemaDiffItem
                {
                    Kind = SchemaDiffKind.AlterColumn,
                    TableName = table,
                    ColumnName = column,
                    Column = Col(column, "BIGINT"),
                    IsSelected = true,
                },
            ],
            MySqlLikeCaps,
            context
        );

        plan.Sections.Select(s => s.Kind).Should().Equal(SchemaDiffKind.AlterColumn);
        var warning = plan
            .Warnings.Should()
            .ContainSingle(w => w.Kind == SyncPlanWarningKind.ForeignKeyColumnTypeMismatch)
            .Which;
        warning.TableName.Should().Be("orders");
        warning.Detail.Should().Be("FK_orders_customer");
    }

    /// <summary>
    /// 両端の型が綴りだけ違う同じ型（SQL Server の <c>numeric(18,0)</c> と <c>numeric(18)</c>）なら、方言の
    /// 正規化（<see cref="SyncDialectCapabilities.NormalizeTypeNotation"/>）を通して同じ型とみなし、外部キーを
    /// 外し直して警告しないことを検証する。正規化を通さない capabilities では食い違いとして警告する（対照）。
    /// </summary>
    [Fact(DisplayName = "型の食い違い: 綴りだけ違う同じ型は方言の正規化を通して一致とみなす")]
    public void AlterColumn_BothSidesWithEquivalentNotation_UsesDialectNormalization()
    {
        var (_, _, _, context) = LiveFkScenario();
        List<SchemaDiffItem> Items() =>
            [
                new SchemaDiffItem
                {
                    Kind = SchemaDiffKind.AlterColumn,
                    TableName = "orders",
                    ColumnName = "customer_id",
                    Column = Col("customer_id", "numeric(18,0)"),
                    IsSelected = true,
                },
                new SchemaDiffItem
                {
                    Kind = SchemaDiffKind.AlterColumn,
                    TableName = "customer",
                    ColumnName = "id",
                    Column = Col("id", "numeric(18)"),
                    IsSelected = true,
                },
            ];

        var normalized = new SyncPlanner().BuildPlan(
            Items(),
            FkRebuildCaps with
            {
                NormalizeTypeNotation = SqlServerTypeNotation.Normalize,
            },
            context
        );
        var raw = new SyncPlanner().BuildPlan(Items(), FkRebuildCaps, context);

        normalized.Warnings.Should().BeEmpty();
        normalized
            .Sections.Select(s => s.Kind)
            .Should()
            .Contain(SchemaDiffKind.DropForeignKey)
            .And.Contain(SchemaDiffKind.AddForeignKey);
        raw.Warnings.Should()
            .ContainSingle(w => w.Kind == SyncPlanWarningKind.ForeignKeyColumnTypeMismatch);
    }

    /// <summary>
    /// 外部キーの両端の文字列型の長さの違いを許す方言（MySQL）では、片側の長さだけを変えても食い違いとみなさず、
    /// 外部キーを外して付け直すことを検証する（食い違いとみなすと外し直さず、列の変更がエラー 1832 で失敗した）。
    /// 長さの違いを許さない方言では従来どおり食い違いとして警告する（対照）。
    /// </summary>
    [Fact(
        DisplayName = "型の食い違い: 文字列型の長さの違いを許す方言では、片側の長さだけの変更でも外し直す"
    )]
    public void AlterColumn_StringLengthOnOneSide_RebuildsWhereLengthMayDiffer()
    {
        var (customer, orders, _, context) = LiveFkScenario();
        customer.Columns[0].DataType = "varchar(50)";
        orders.Columns[1].DataType = "varchar(50)";
        SchemaDiffItem[] Items() =>
            [
                new SchemaDiffItem
                {
                    Kind = SchemaDiffKind.AlterColumn,
                    TableName = "orders",
                    ColumnName = "customer_id",
                    Column = Col("customer_id", "varchar(100)"),
                    IsSelected = true,
                },
            ];

        var mySql = new SyncPlanner().BuildPlan(
            Items(),
            MySqlLikeCaps with
            {
                ForeignKeyAllowsStringLengthDifference = true,
            },
            context
        );
        var strict = new SyncPlanner().BuildPlan(Items(), MySqlLikeCaps, context);

        mySql.Warnings.Should().BeEmpty();
        mySql
            .Sections.Select(s => s.Kind)
            .Should()
            .Equal(
                SchemaDiffKind.DropForeignKey,
                SchemaDiffKind.AlterColumn,
                SchemaDiffKind.AddForeignKey
            );
        strict
            .Warnings.Should()
            .ContainSingle(w => w.Kind == SyncPlanWarningKind.ForeignKeyColumnTypeMismatch);
    }

    /// <summary>外部キーの両端に並べられる型の判定（長さだけの違いを許すのは同じ文字列型で、括弧の後ろの指定も同じとき）</summary>
    [Theory(DisplayName = "外部キーの両端の型: 長さの違いを許すのは同じ文字列型どうしだけ")]
    [InlineData("varchar(50)", "VARCHAR(100)", true)]
    [InlineData("char(2)", "char(10)", true)]
    [InlineData("varbinary(16)", "varbinary(32)", true)]
    [InlineData("varchar(50)", "char(50)", false)]
    [InlineData("varchar(50) CHARACTER SET latin1", "varchar(100)", false)]
    [InlineData("int", "bigint", false)]
    [InlineData("decimal(10,2)", "decimal(12,2)", false)]
    public void AreForeignKeyCompatibleTypes_AllowsStringLengthOnly(
        string left,
        string right,
        bool expected
    )
    {
        var lenient = new SyncDialectCapabilities { ForeignKeyAllowsStringLengthDifference = true };

        lenient.AreForeignKeyCompatibleTypes(left, right).Should().Be(expected);
        new SyncDialectCapabilities()
            .AreForeignKeyCompatibleTypes(left, right)
            .Should()
            .Be(left.Equals(right, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>両端を同じ型へ変えれば外部キーを外して付け直し、警告は出さない（大文字小文字の違いは同じ型）</summary>
    [Fact(DisplayName = "型の食い違い: 両端を同じ型へ変えれば外し直して警告しない")]
    public void AlterColumn_BothSidesOfForeignKey_InjectsWithoutWarning()
    {
        var (_, _, _, context) = LiveFkScenario();

        var plan = new SyncPlanner().BuildPlan(
            [
                new SchemaDiffItem
                {
                    Kind = SchemaDiffKind.AlterColumn,
                    TableName = "orders",
                    ColumnName = "customer_id",
                    Column = Col("customer_id", "bigint"),
                    IsSelected = true,
                },
                new SchemaDiffItem
                {
                    Kind = SchemaDiffKind.AlterColumn,
                    TableName = "customer",
                    ColumnName = "id",
                    Column = Col("id", "BIGINT"),
                    IsSelected = true,
                },
            ],
            MySqlLikeCaps,
            context
        );

        plan.Sections.Select(s => s.Kind)
            .Should()
            .Equal(
                SchemaDiffKind.DropForeignKey,
                SchemaDiffKind.AlterColumn,
                SchemaDiffKind.AddForeignKey
            );
        plan.Warnings.Should().BeEmpty();
    }

    /// <summary>
    /// 子表・構成列が同じ計画で消える外部キーは、主キー変更に巻き込まれても外すだけで付け直さないことを検証する
    /// （付け直すと消えた表・列を参照して失敗する。外すほうは、残っていると主キーの解除・列の削除が拒否されるので要る）。
    /// </summary>
    [Theory(
        DisplayName = "消える端: 子表の削除・子列の削除と重なる外部キーは外すだけで付け直さない"
    )]
    [InlineData(SchemaDiffKind.DropTable)]
    [InlineData(SchemaDiffKind.DropColumn)]
    public void AlterPrimaryKey_WithDroppedChildEndpoint_DoesNotInject(SchemaDiffKind dropKind)
    {
        var (customer, orders, _, context) = LiveFkScenario();
        var drop = new SchemaDiffItem
        {
            Kind = dropKind,
            TableName = "orders",
            ColumnName = dropKind == SchemaDiffKind.DropColumn ? "customer_id" : null,
            Entity = orders,
            IsSelected = true,
        };

        var plan = new SyncPlanner().BuildPlan(
            [AlterPk("customer", customer.Clone(preserveId: false)), drop],
            FkRebuildCaps,
            context
        );

        plan.Sections.Single(s => s.Kind == SchemaDiffKind.DropForeignKey)
            .Items.Should()
            .ContainSingle()
            .Which.ForeignKeyName.Should()
            .Be("FK_orders_customer");
        plan.Sections.Should().NotContain(s => s.Kind == SchemaDiffKind.AddForeignKey);
        plan.Warnings.Should().BeEmpty();
    }

    /// <summary>
    /// 選んだ表・列の削除で端が消える live の外部キーは、外部キーの削除を選んでいなくても外すだけの項目として
    /// 計画に入る（表・列の削除より前のセクション・付け直さない）ことを、消える端の種類ごとに検証する。
    /// </summary>
    [Theory(DisplayName = "消える端: 表・列の削除だけを選んでも、端の消える外部キーを先に外す")]
    [InlineData("customer", null)]
    [InlineData("orders", null)]
    [InlineData("orders", "customer_id")]
    [InlineData("customer", "id")]
    public void DropEndpointOnly_InjectsForeignKeyDrop(string table, string? column)
    {
        var (customer, orders, _, context) = LiveFkScenario();
        var drop = new SchemaDiffItem
        {
            Kind = column is null ? SchemaDiffKind.DropTable : SchemaDiffKind.DropColumn,
            TableName = table,
            ColumnName = column,
            Entity = table == "customer" ? customer : orders,
            IsSelected = true,
        };

        var plan = new SyncPlanner().BuildPlan([drop], FkRebuildCaps, context);

        plan.Sections.Select(s => s.Kind).Should().Equal(SchemaDiffKind.DropForeignKey, drop.Kind);
        plan.Sections[0]
            .Items.Should()
            .ContainSingle()
            .Which.ForeignKeyName.Should()
            .Be("FK_orders_customer");
        plan.Warnings.Should().BeEmpty();
    }

    /// <summary>
    /// 親と子の表の削除だけを選ぶと、外部キーを先に外すので、親の表が先に並んでも DROP の順序に依存しないことを検証する。
    /// 外部キーの削除を明示的に選んでいれば、同じ外部キーを 2 回外さない。
    /// </summary>
    [Theory(DisplayName = "消える端: 親と子の表を消すとき外部キーを 1 回だけ先に外す")]
    [InlineData(false)]
    [InlineData(true)]
    public void DropParentAndChild_DropsForeignKeyOnceBeforeTables(bool selectForeignKeyDrop)
    {
        var (customer, orders, rel, context) = LiveFkScenario();
        List<SchemaDiffItem> items =
        [
            new()
            {
                Kind = SchemaDiffKind.DropTable,
                TableName = "customer",
                Entity = customer,
                IsSelected = true,
            },
            new()
            {
                Kind = SchemaDiffKind.DropTable,
                TableName = "orders",
                Entity = orders,
                IsSelected = true,
            },
            new()
            {
                Kind = SchemaDiffKind.DropForeignKey,
                TableName = "orders",
                Entity = orders,
                ParentEntity = customer,
                ChildEntity = orders,
                Relationship = rel,
                ForeignKeyName = rel.ConstraintName,
                ForeignKeyColumnPairs = [new("id", "customer_id")],
                IsSelected = selectForeignKeyDrop,
            },
        ];

        var plan = new SyncPlanner().BuildPlan(items, FkRebuildCaps, context);

        plan.Sections.Select(s => s.Kind)
            .Should()
            .Equal(SchemaDiffKind.DropForeignKey, SchemaDiffKind.DropTable);
        plan.Sections[0].Items.Should().ContainSingle();
    }

    /// <summary>
    /// 再構築の方言では、親の表だけを消すと子の表を作り直して外部キーを外し、子の表を消すときはその表を作り直さない
    /// ことを検証する。
    /// </summary>
    [Theory(
        DisplayName = "消える端: 再構築の方言は親の削除で子を作り直し、子の削除では作り直さない"
    )]
    [InlineData("customer", true)]
    [InlineData("orders", false)]
    public void Rebuild_DropTable_RebuildsSurvivingChildOnly(
        string droppedTable,
        bool rebuildsChild
    )
    {
        var (customer, orders, _, context) = LiveFkScenario();

        var plan = new SyncPlanner().BuildPlan(
            [
                new SchemaDiffItem
                {
                    Kind = SchemaDiffKind.DropTable,
                    TableName = droppedTable,
                    Entity = droppedTable == "customer" ? customer : orders,
                    IsSelected = true,
                },
            ],
            RebuildCaps,
            context
        );

        if (rebuildsChild)
        {
            var rebuild = plan.Rebuilds.Should().ContainSingle().Which;
            rebuild.TableName.Should().Be("orders");
            rebuild.ForeignKeys.Should().BeEmpty();
        }
        else
        {
            plan.Rebuilds.Should().BeEmpty();
        }

        plan.Sections.Select(s => s.Kind).Should().Equal(SchemaDiffKind.DropTable);
    }

    // ---------------- 選んでいない変更への依存（外して名指しする・主キー列の NOT NULL 化） ----------------

    /// <summary>live の orders へ足す（差分では AddColumn になる）列の外部キーの追加項目を作る</summary>
    private static SchemaDiffItem AddForeignKeyOnNewColumn(
        Entity customer,
        Entity orders,
        string column = "customer_ref"
    )
    {
        var target = orders.Clone(preserveId: false);
        target.Columns.Add(Col(column, "INT"));

        return new SchemaDiffItem
        {
            Kind = SchemaDiffKind.AddForeignKey,
            TableName = "orders",
            ColumnName = column,
            Entity = target,
            ParentEntity = customer,
            ChildEntity = target,
            Relationship = new Relationship(),
            ForeignKeyName = "FK_orders_customer_ref",
            ForeignKeyColumnPairs = [new("id", column)],
            IsSelected = true,
        };
    }

    /// <summary>
    /// 選んだ外部キーの追加が、選んでいない列の追加を前提にするなら計画から外して名指しし、列の追加も選んでいれば
    /// そのまま実行することを、逐次 DDL と再構築の両方の方言で検証する。
    /// </summary>
    [Theory(DisplayName = "依存: 選んでいない列の追加を前提にする外部キーの追加は外して警告する")]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void AddForeignKey_OnUnselectedNewColumn_IsExcludedWithWarning(
        bool rebuildDialect,
        bool selectAddColumn
    )
    {
        var (customer, orders, _, context) = LiveFkScenario();
        var addForeignKey = AddForeignKeyOnNewColumn(customer, orders);
        var addColumn = new SchemaDiffItem
        {
            Kind = SchemaDiffKind.AddColumn,
            TableName = "orders",
            ColumnName = "customer_ref",
            Entity = addForeignKey.ChildEntity,
            Column = Col("customer_ref", "INT"),
            IsSelected = selectAddColumn,
        };

        var plan = new SyncPlanner().BuildPlan(
            [addColumn, addForeignKey],
            rebuildDialect ? RebuildCaps : FkRebuildCaps,
            context
        );

        if (selectAddColumn)
        {
            plan.Warnings.Should().BeEmpty();
            var planned = plan
                .Sections.SelectMany(s => s.Items)
                .Concat(plan.Rebuilds.SelectMany(r => r.SourceItems));
            planned.Should().Contain(addForeignKey);
        }
        else
        {
            plan.IsEmpty.Should().BeTrue();
            var warning = plan.Warnings.Should().ContainSingle().Which;
            warning.Kind.Should().Be(SyncPlanWarningKind.SelectedItemRequiresUnselectedItem);
            warning.TableName.Should().Be("orders");
            warning.Detail.Should().Be("FK_orders_customer_ref (orders.customer_ref)");
        }
    }

    /// <summary>
    /// 外部キーの作り直し（AlterForeignKey）が選んでいない列の追加を前提にするなら、削除と追加をまとめて外す
    /// （削除だけが走って外部キーが消えたまま残らない）ことを検証する。
    /// </summary>
    [Fact(DisplayName = "依存: 選んでいない列の追加を前提にする外部キーの作り直しは削除ごと外す")]
    public void AlterForeignKey_OnUnselectedNewColumn_IsExcludedAsAWhole()
    {
        var (customer, orders, rel, context) = LiveFkScenario();
        var add = AddForeignKeyOnNewColumn(customer, orders);
        var alter = new SchemaDiffItem
        {
            Kind = SchemaDiffKind.AlterForeignKey,
            TableName = "orders",
            ColumnName = add.ColumnName,
            Entity = add.Entity,
            ParentEntity = customer,
            ChildEntity = add.ChildEntity,
            Relationship = add.Relationship,
            ForeignKeyName = "FK_orders_customer",
            ForeignKeyColumnPairs = add.ForeignKeyColumnPairs,
            ReplacedForeignKey = new SchemaDiffItem
            {
                Kind = SchemaDiffKind.DropForeignKey,
                TableName = "orders",
                Entity = orders,
                ParentEntity = customer,
                ChildEntity = orders,
                Relationship = rel,
                ForeignKeyName = "FK_orders_customer",
                ForeignKeyColumnPairs = [new("id", "customer_id")],
            },
            IsSelected = true,
        };

        var plan = new SyncPlanner().BuildPlan([alter], FkRebuildCaps, context);

        plan.IsEmpty.Should().BeTrue();
        plan.Warnings.Should()
            .ContainSingle()
            .Which.Detail.Should()
            .Be("FK_orders_customer (orders.customer_ref)");
    }

    /// <summary>
    /// 主キーの変更が選んでいない列の追加を前提にするなら外して名指しする（SQLite の再構築は無い列を読み飛ばし、
    /// 部分的な主キーのまま成功していた）。一意制約の追加も同じ規則で外すことを検証する。
    /// </summary>
    [Theory(
        DisplayName = "依存: 選んでいない列の追加を前提にする主キーの変更・一意制約の追加は外して警告する"
    )]
    [InlineData(false)]
    [InlineData(true)]
    public void PrimaryKeyAndUniqueOnUnselectedNewColumn_AreExcludedWithWarning(bool rebuildDialect)
    {
        var (customer, _, _, context) = LiveFkScenario();
        customer.Columns.Add(Col("name", "INT"));
        var target = customer.Clone(preserveId: false);
        target.Columns.Add(
            new Column
            {
                Name = "code",
                DataType = "INT",
                IsNullable = false,
            }
        );
        target.Columns.Single(c => c.Name == "id").IsPrimaryKey = false;
        target.Columns.Single(c => c.Name == "name").IsPrimaryKey = true;
        target.Columns.Single(c => c.Name == "code").IsPrimaryKey = true;

        var plan = new SyncPlanner().BuildPlan(
            [
                AlterPk("customer", target),
                new SchemaDiffItem
                {
                    Kind = SchemaDiffKind.AddUniqueConstraint,
                    TableName = "customer",
                    Entity = target,
                    UniqueConstraintColumns = ["code"],
                    IsSelected = true,
                },
            ],
            rebuildDialect ? RebuildCaps : FkRebuildCaps,
            context
        );

        plan.IsEmpty.Should().BeTrue();
        plan.Warnings.Select(w => w.Detail)
            .Should()
            .BeEquivalentTo("PRIMARY KEY (customer.code)", "UQ_customer_code (customer.code)");
    }

    /// <summary>
    /// 一意制約の作り直しが選んでいない列の追加を前提にするときは、削除と追加をまとめて外して警告することを検証する
    /// （展開の前に判定する＝削除だけが走って一意制約が消えたまま残らない）。
    /// </summary>
    [Theory(
        DisplayName = "依存: 選んでいない列の追加を前提にする一意制約の作り直しは、削除ごと外して警告する"
    )]
    [InlineData(false)]
    [InlineData(true)]
    public void AlterUniqueOnUnselectedNewColumn_IsExcludedWithWarning(bool rebuildDialect)
    {
        var (customer, _, _, context) = LiveFkScenario();
        customer.Columns.Add(Col("name", "INT"));
        WithUnique(customer, "UQ_customer_key", "name");

        var plan = new SyncPlanner().BuildPlan(
            [
                new SchemaDiffItem
                {
                    Kind = SchemaDiffKind.AlterUniqueConstraint,
                    TableName = "customer",
                    Entity = customer,
                    UniqueConstraintName = "UQ_customer_key",
                    UniqueConstraintColumns = ["name", "code"],
                    ReplacedUniqueConstraint = UniqueItem(
                        SchemaDiffKind.DropUniqueConstraint,
                        "customer",
                        "UQ_customer_key",
                        ["name"]
                    ),
                    IsSelected = true,
                },
            ],
            rebuildDialect ? RebuildCaps : FkRebuildCaps,
            context
        );

        plan.IsEmpty.Should().BeTrue();
        plan.Warnings.Should()
            .ContainSingle(w => w.Kind == SyncPlanWarningKind.SelectedItemRequiresUnselectedItem)
            .Which.Detail.Should()
            .Be("UQ_customer_key (customer.code)");
    }

    /// <summary>
    /// 主キーの変更で新しく主キーに加わる NULL 許容の列は、列の NOT NULL 化を選んでいなくても live の型のまま
    /// NOT NULL にし、利用者が同じ列の変更を選んでいれば重ねて足さないことを検証する。
    /// </summary>
    [Theory(
        DisplayName = "主キー列の NOT NULL 化: 新しく主キーに加わる NULL 許容の列は暗黙に NOT NULL にする"
    )]
    [InlineData(false)]
    [InlineData(true)]
    public void AlterPrimaryKey_WithNullableNewKeyColumn_InjectsNotNull(bool selectAlterColumn)
    {
        var (customer, _, _, context) = LiveFkScenario();
        customer.Columns.Add(Col("code", "INT"));
        var target = customer.Clone(preserveId: false);
        var code = target.Columns.Single(c => c.Name == "code");
        code.IsPrimaryKey = true;
        code.IsNullable = false;
        target.Columns.Single(c => c.Name == "id").IsPrimaryKey = true;
        List<SchemaDiffItem> items = [AlterPk("customer", target)];

        if (selectAlterColumn)
        {
            items.Add(
                new SchemaDiffItem
                {
                    Kind = SchemaDiffKind.AlterColumn,
                    TableName = "customer",
                    ColumnName = "code",
                    Entity = target,
                    Column = code,
                    OldColumn = customer.Columns.Single(c => c.Name == "code"),
                    IsSelected = true,
                }
            );
        }

        var plan = new SyncPlanner().BuildPlan(items, FkRebuildCaps, context);

        var alter = plan
            .Sections.Single(s => s.Kind == SchemaDiffKind.AlterColumn)
            .Items.Should()
            .ContainSingle()
            .Which;
        alter.ColumnName.Should().Be("code");
        alter.Column!.IsNullable.Should().BeFalse();
        alter.Column.DataType.Should().Be("INT");
        plan.Warnings.Should()
            .NotContain(w => w.Kind == SyncPlanWarningKind.SelectedItemRequiresUnselectedItem);
    }

    /// <summary>
    /// 主キーと同じ列集合の一意制約を持てない方言では、同期後も主キーに残る列集合への一意制約の追加を、主キーの変更を
    /// 選んでいなければ外して名指しし、主キーを別の列へ移す変更を選んでいれば実行することを検証する。
    /// 持てる方言では主キーの変更を選ばなくても実行する。
    /// </summary>
    [Theory(
        DisplayName = "依存: 同期後の主キーと同じ列集合の一意制約は、持てない方言では外して警告する"
    )]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    [InlineData(false, false, false)]
    public void AddUniqueOnStayingPrimaryKey_IsExcludedWhereUnsupported(
        bool rebuildDialect,
        bool selectPrimaryKeyChange,
        bool excluded
    )
    {
        var (customer, _, _, context) = LiveFkScenario();
        customer.Columns.Add(
            new Column
            {
                Name = "code",
                DataType = "INT",
                IsNullable = false,
            }
        );
        var target = customer.Clone(preserveId: false);
        target.Columns.Single(c => c.Name == "id").IsPrimaryKey = false;
        target.Columns.Single(c => c.Name == "code").IsPrimaryKey = true;
        var addUnique = new SchemaDiffItem
        {
            Kind = SchemaDiffKind.AddUniqueConstraint,
            TableName = "customer",
            Entity = target,
            UniqueConstraintColumns = ["id"],
            IsSelected = true,
        };
        var capabilities = rebuildDialect
            ? RebuildCaps with
            {
                SupportsUniqueConstraintOnPrimaryKey = false,
            }
            : FkRebuildCaps;

        var plan = new SyncPlanner().BuildPlan(
            [addUnique, AlterPk("customer", target, selected: selectPrimaryKeyChange)],
            capabilities,
            context
        );

        if (excluded)
        {
            plan.IsEmpty.Should().BeTrue();
            plan.Warnings.Should()
                .ContainSingle()
                .Which.Detail.Should()
                .Be("UQ_customer_id (PRIMARY KEY)");
        }
        else
        {
            plan.Warnings.Should()
                .NotContain(w => w.Kind == SyncPlanWarningKind.SelectedItemRequiresUnselectedItem);
            plan.Sections.SelectMany(s => s.Items)
                .Concat(plan.Rebuilds.SelectMany(r => r.SourceItems))
                .Should()
                .Contain(addUnique);
        }
    }

    /// <summary>
    /// 同期後も主キーに残る列を NULL 許容にする変更は、主キーから外す変更を選んでいなければ外して名指しする。
    /// 型の変更も含むなら、NULL 許容化だけを外して型の変更は残すことを検証する。
    /// </summary>
    [Theory(DisplayName = "依存: 主キーに残る列の NULL 許容化は外し、型の変更は残す")]
    [InlineData("INT", false)]
    [InlineData("BIGINT", true)]
    public void AlterColumn_NullableOnStayingPrimaryKey_DropsOnlyNullability(
        string newType,
        bool keepsTypeChange
    )
    {
        var (customer, _, _, context) = LiveFkScenario();
        var liveId = customer.Columns.Single(c => c.Name == "id");
        var item = new SchemaDiffItem
        {
            Kind = SchemaDiffKind.AlterColumn,
            TableName = "customer",
            ColumnName = "id",
            Entity = customer,
            Column = new Column
            {
                Name = "id",
                DataType = newType,
                IsNullable = true,
            },
            OldColumn = liveId,
            IsSelected = true,
        };

        var plan = new SyncPlanner().BuildPlan([item], new SyncDialectCapabilities(), context);

        plan.Warnings.Should()
            .ContainSingle(w => w.Kind == SyncPlanWarningKind.SelectedItemRequiresUnselectedItem)
            .Which.Detail.Should()
            .Be("id (PRIMARY KEY)");

        if (keepsTypeChange)
        {
            var kept = plan
                .Sections.Single(s => s.Kind == SchemaDiffKind.AlterColumn)
                .Items.Should()
                .ContainSingle()
                .Which;
            kept.Column!.DataType.Should().Be("BIGINT");
            kept.Column.IsNullable.Should().BeFalse();
        }
        else
        {
            plan.Sections.Should().NotContain(s => s.Kind == SchemaDiffKind.AlterColumn);
        }
    }

    /// <summary>
    /// MySQL では、主キーを変える表自身の外部キーのうち子列が旧主キーの先頭に乗るものも外して付け直す
    /// （旧主キーがその外部キーを支える唯一の索引だと DROP PRIMARY KEY がエラー 1553 になる）ことを検証する。
    /// </summary>
    [Fact(DisplayName = "自表の外部キー: MySQL 相当では旧主キーの先頭に乗る外部キーを外し直す")]
    public void AlterPrimaryKey_OwnForeignKeyOnLeadingColumns_InjectsForMySqlLike()
    {
        var parent = new Entity { TableName = "p", Columns = { PkId() } };
        var other = new Entity { TableName = "q", Columns = { PkId() } };
        var link = new Entity
        {
            TableName = "link",
            Columns =
            {
                new Column
                {
                    Name = "p_id",
                    DataType = "INT",
                    IsPrimaryKey = true,
                    IsNullable = false,
                },
                new Column
                {
                    Name = "x",
                    DataType = "INT",
                    IsPrimaryKey = true,
                    IsNullable = false,
                },
                Col("q_id", "INT"),
            },
        };
        var leading = new Relationship
        {
            SourceEntityId = parent.Id,
            TargetEntityId = link.Id,
            ColumnPairs = [new(parent.Columns[0].Id, link.Columns[0].Id)],
            ConstraintName = "FK_link_p",
        };
        var notLeading = new Relationship
        {
            SourceEntityId = other.Id,
            TargetEntityId = link.Id,
            ColumnPairs = [new(other.Columns[0].Id, link.Columns[2].Id)],
            ConstraintName = "FK_link_q",
        };
        var context = new SyncPlanContext
        {
            LiveEntities = [parent, other, link],
            LiveRelationships = [leading, notLeading],
        };

        // 主キーを (p_id, x) から (x, p_id) へ並べ替える
        var target = link.Clone(preserveId: true);
        target.PrimaryKeyColumnIds = [target.Columns[1].Id, target.Columns[0].Id];

        var mySqlPlan = new SyncPlanner().BuildPlan(
            [AlterPk("link", target)],
            MySqlLikeCaps,
            context
        );
        var otherPlan = new SyncPlanner().BuildPlan(
            [AlterPk("link", target)],
            FkRebuildCaps,
            context
        );

        mySqlPlan
            .Sections.Single(s => s.Kind == SchemaDiffKind.DropForeignKey)
            .Items.Select(i => i.ForeignKeyName)
            .Should()
            .Equal("FK_link_p");
        otherPlan.Sections.Should().NotContain(s => s.Kind == SchemaDiffKind.DropForeignKey);
    }

    /// <summary>再構築の方言では、外部キー変更で子表の外部キーが新しい定義の 1 本だけになることを検証する</summary>
    [Fact(
        DisplayName = "外部キー変更: 再構築の方言では子表の外部キーが新しい定義の 1 本だけになる"
    )]
    public void AlterForeignKey_RebuildDialect_ReplacesForeignKey()
    {
        var (_, _, _, context) = LiveFkScenario();

        var plan = new SyncPlanner().BuildPlan(
            [SelectedForeignKeyAlter(context, RebuildCaps)],
            RebuildCaps,
            context
        );

        plan.Rebuilds.Should()
            .ContainSingle()
            .Which.ForeignKeys.Should()
            .ContainSingle()
            .Which.OnDelete.Should()
            .Be(ForeignKeyReferentialAction.Cascade);
    }

    // ---------------- 同じ列の外部キー 2 本・外部キーを支える索引 ----------------

    /// <summary>
    /// customer(id) ← orders(customer_id) に、同じ列ペアの外部キー <c>FK_a</c> と、<paramref name="secondParent"/> を指す
    /// 外部キー <c>FK_b</c> を持つ live を組み立てる（<paramref name="secondParent"/> が null なら FK_b も customer を指す）。
    /// </summary>
    private static (
        Entity Customer,
        Entity Orders,
        Relationship RelA,
        Relationship RelB,
        SyncPlanContext Context
    ) TwoForeignKeysOnSameColumnScenario(Entity? secondParent = null)
    {
        var customer = new Entity { TableName = "customer", Columns = { PkId() } };
        var orders = new Entity
        {
            TableName = "orders",
            Columns = { PkId(), Col("customer_id", "INT") },
        };
        var relA = new Relationship
        {
            SourceEntityId = customer.Id,
            TargetEntityId = orders.Id,
            ColumnPairs = [new(customer.Columns[0].Id, orders.Columns[1].Id)],
            ConstraintName = "FK_a",
        };
        var parentB = secondParent ?? customer;
        var relB = new Relationship
        {
            SourceEntityId = parentB.Id,
            TargetEntityId = orders.Id,
            ColumnPairs = [new(parentB.Columns[0].Id, orders.Columns[1].Id)],
            ConstraintName = "FK_b",
        };
        List<Entity> entities = secondParent is null
            ? [customer, orders]
            : [customer, secondParent, orders];

        return (
            customer,
            orders,
            relA,
            relB,
            new SyncPlanContext { LiveEntities = entities, LiveRelationships = [relA, relB] }
        );
    }

    /// <summary>
    /// 外部キーを支える一意制約を外すとき、外部キーが子列を先頭に持つ索引を要する方言（MySQL）では、その外部キーを
    /// 外して付け直すことを検証する（素の <c>DROP INDEX</c> はエラー 1553）。索引を要しない方言では外し直さない（対照）。
    /// </summary>
    [Fact(
        DisplayName = "索引の喪失: 外部キーを支える一意制約の削除で、その外部キーを外して付け直す（MySQL 相当）"
    )]
    public void DropUniqueSupportingForeignKey_MySqlLike_RebuildsForeignKey()
    {
        var (_, orders, _, context) = LiveFkScenario();
        WithUnique(orders, "UQ_orders_customer", "customer_id");

        SchemaDiffItem[] Items() =>
            [
                UniqueItem(
                    SchemaDiffKind.DropUniqueConstraint,
                    "orders",
                    "UQ_orders_customer",
                    ["customer_id"]
                ),
            ];

        var mySql = new SyncPlanner().BuildPlan(Items(), MySqlLikeCaps, context);
        var other = new SyncPlanner().BuildPlan(Items(), FkRebuildCaps, context);

        mySql
            .Sections.Select(s => s.Kind)
            .Should()
            .Equal(
                SchemaDiffKind.DropForeignKey,
                SchemaDiffKind.DropUniqueConstraint,
                SchemaDiffKind.AddForeignKey
            );
        other.Sections.Select(s => s.Kind).Should().Equal(SchemaDiffKind.DropUniqueConstraint);
    }

    /// <summary>
    /// 同じ列に外部キーが 2 本あり、片方だけが同期後に型が食い違うとき、食い違わないほうも外さないことを検証する
    /// （列の変更は食い違うほうに阻まれて必ず失敗するので、外すと DDL が暗黙コミットされる方言で消えたまま残る）。
    /// </summary>
    [Fact(
        DisplayName = "型の食い違い: 同じ列の別の外部キーが食い違うなら、食い違わない外部キーも外さない"
    )]
    public void AlterColumn_SameColumnWithMismatchedSibling_DoesNotDropEither()
    {
        var other = new Entity { TableName = "other", Columns = { PkId() } };
        var (_, _, _, _, context) = TwoForeignKeysOnSameColumnScenario(other);

        var plan = new SyncPlanner().BuildPlan(
            [
                new SchemaDiffItem
                {
                    Kind = SchemaDiffKind.AlterColumn,
                    TableName = "orders",
                    ColumnName = "customer_id",
                    Column = Col("customer_id", "BIGINT"),
                    IsSelected = true,
                },
                new SchemaDiffItem
                {
                    Kind = SchemaDiffKind.AlterColumn,
                    TableName = "customer",
                    ColumnName = "id",
                    Column = Col("id", "BIGINT"),
                    IsSelected = true,
                },
            ],
            MySqlLikeCaps,
            context
        );

        plan.Sections.Select(s => s.Kind).Should().Equal(SchemaDiffKind.AlterColumn);
        plan.Warnings.Should()
            .ContainSingle(w => w.Kind == SyncPlanWarningKind.ForeignKeyColumnTypeMismatch)
            .Which.Detail.Should()
            .Be("FK_b");
    }

    /// <summary>
    /// 型が食い違う外部キーが 1 本でもあれば、別の表の外部キーも外し直さないことを検証する。残した外部キー（FK_a）が
    /// 参照する親の列 customer.id の変更も失敗するので、同じ親の列を参照する items の外部キー（FK_c）を外すと、
    /// 付け直しまで届かず消えたまま残っていた。
    /// </summary>
    [Fact(DisplayName = "型の食い違い: 食い違う外部キーがあれば、別の表の外部キーも外し直さない")]
    public void AlterColumn_MismatchedForeignKey_DoesNotRebuildAnyForeignKey()
    {
        var other = new Entity { TableName = "other", Columns = { PkId() } };
        var (customer, _, relA, relB, scenario) = TwoForeignKeysOnSameColumnScenario(other);
        var items = new Entity
        {
            TableName = "items",
            Columns = { PkId(), Col("customer_ref", "INT") },
        };
        var relC = new Relationship
        {
            SourceEntityId = customer.Id,
            TargetEntityId = items.Id,
            ColumnPairs = [new(customer.Columns[0].Id, items.Columns[1].Id)],
            ConstraintName = "FK_c",
        };
        var context = new SyncPlanContext
        {
            LiveEntities = [.. scenario.LiveEntities, items],
            LiveRelationships = [relA, relB, relC],
        };

        SchemaDiffItem Alter(string table, string column) =>
            new()
            {
                Kind = SchemaDiffKind.AlterColumn,
                TableName = table,
                ColumnName = column,
                Column = Col(column, "BIGINT"),
                IsSelected = true,
            };

        var plan = new SyncPlanner().BuildPlan(
            [
                Alter("orders", "customer_id"),
                Alter("customer", "id"),
                Alter("items", "customer_ref"),
            ],
            MySqlLikeCaps,
            context
        );

        plan.Sections.Select(s => s.Kind).Should().Equal(SchemaDiffKind.AlterColumn);
        plan.Warnings.Should()
            .ContainSingle(w => w.Kind == SyncPlanWarningKind.ForeignKeyColumnTypeMismatch)
            .Which.Detail.Should()
            .Be("FK_b");
    }

    /// <summary>customer(id, code) ← orders(id, customer_id, customer_code) へ外部キーを追加する差分項目を作る</summary>
    private static (SchemaDiffItem Add, SyncPlanContext Context) AddForeignKeyScenario(
        params ForeignKeyColumnNamePair[] pairs
    )
    {
        var (customer, orders, rel, _) = LiveFkScenario();
        customer.Columns.Add(Col("code", "INT"));
        customer.Columns.Add(Col("kind", "INT"));
        orders.Columns.Add(Col("customer_code", "INT"));
        orders.Columns.Add(Col("customer_kind", "INT"));

        var add = new SchemaDiffItem
        {
            Kind = SchemaDiffKind.AddForeignKey,
            TableName = "orders",
            ParentEntity = customer,
            ChildEntity = orders,
            Relationship = new Relationship { ConstraintName = "FK_new" },
            ForeignKeyName = "FK_new",
            ForeignKeyColumnPairs = pairs,
            IsSelected = true,
        };

        return (
            add,
            new SyncPlanContext { LiveEntities = [customer, orders], LiveRelationships = [rel] }
        );
    }

    /// <summary>
    /// 選んだ外部キーの追加の参照先が、同期後の主キーにも一意制約にもちょうど一致しないとき警告し、一致するとき
    /// （主キー・live の一意制約・同じ計画で追加する一意制約）は警告しないことを検証する。
    /// </summary>
    [Fact(
        DisplayName = "候補キー: 選んだ外部キーの追加は、参照先が同期後の主キー・一意制約でなければ警告する"
    )]
    public void AddForeignKey_NotOnCandidateKey_Warns()
    {
        static IEnumerable<SyncPlanWarningKind> Warnings(
            SchemaDiffItem add,
            SyncPlanContext context,
            params SchemaDiffItem[] others
        ) =>
            new SyncPlanner()
                .BuildPlan([add, .. others], FkRebuildCaps, context)
                .Warnings.Select(w => w.Kind);

        var (toCode, context) = AddForeignKeyScenario(
            new ForeignKeyColumnNamePair("code", "customer_code")
        );
        Warnings(toCode, context).Should().Equal(SyncPlanWarningKind.ForeignKeyNotOnCandidateKey);
        Warnings(
                toCode,
                context,
                UniqueItem(SchemaDiffKind.AddUniqueConstraint, "customer", null, ["code"])
            )
            .Should()
            .BeEmpty();

        var (toKey, keyContext) = AddForeignKeyScenario(
            new ForeignKeyColumnNamePair("id", "customer_code")
        );
        Warnings(toKey, keyContext).Should().BeEmpty();

        var (toUnique, uniqueContext) = AddForeignKeyScenario(
            new ForeignKeyColumnNamePair("code", "customer_code")
        );
        WithUnique(uniqueContext.LiveEntities[0], "UQ_customer_code", "code");
        Warnings(toUnique, uniqueContext).Should().BeEmpty();

        // 同じ計画で追加する表の一意制約も候補キーに数える（表の作成と一緒に作られる）
        var (toNewTable, newTableContext) = AddForeignKeyScenario(
            new ForeignKeyColumnNamePair("code", "customer_code")
        );
        var brand = new Entity { TableName = "brand", Columns = { PkId(), Col("code", "INT") } };
        var addBrand = new SchemaDiffItem
        {
            Kind = SchemaDiffKind.AddTable,
            TableName = "brand",
            Entity = brand,
            IsSelected = true,
        };
        var toBrand = new SchemaDiffItem
        {
            Kind = SchemaDiffKind.AddForeignKey,
            TableName = "orders",
            ParentEntity = brand,
            ChildEntity = toNewTable.ChildEntity,
            Relationship = new Relationship { ConstraintName = "FK_brand" },
            ForeignKeyName = "FK_brand",
            ForeignKeyColumnPairs = [new("code", "customer_code")],
            IsSelected = true,
        };
        Warnings(toBrand, newTableContext, addBrand)
            .Should()
            .Equal(SyncPlanWarningKind.ForeignKeyNotOnCandidateKey);
        WithUnique(brand, "UQ_brand_code", "code");
        Warnings(toBrand, newTableContext, addBrand).Should().BeEmpty();

        // 主キーを外す変更と一緒に選ぶと、主キーを参照する外部キーも候補キーを失う（差分と同じく、外部キーの
        // 参照先は図＝主キーを外した後の表）
        var (toRemovedKey, removedContext) = AddForeignKeyScenario(
            new ForeignKeyColumnNamePair("id", "customer_code")
        );
        var keyless = removedContext.LiveEntities[0].Clone(preserveId: true);
        keyless.Columns[0].IsPrimaryKey = false;
        var toKeyless = new SchemaDiffItem
        {
            Kind = SchemaDiffKind.AddForeignKey,
            TableName = "orders",
            ParentEntity = keyless,
            ChildEntity = toRemovedKey.ChildEntity,
            Relationship = toRemovedKey.Relationship,
            ForeignKeyName = toRemovedKey.ForeignKeyName,
            ForeignKeyColumnPairs = toRemovedKey.ForeignKeyColumnPairs,
            IsSelected = true,
        };
        Warnings(toKeyless, removedContext, AlterPk("customer", keyless))
            .Should()
            .Contain(SyncPlanWarningKind.ForeignKeyNotOnCandidateKey);
    }

    /// <summary>
    /// 参照される複合主キーの並びだけの入れ替え（MySQL では並びの不一致で外す）と、新しい並びの主キーを参照する外部キーの
    /// 追加を両方選ぶと、追加も「選んでいない主キーの変更を前提にする」として外して名指しすることを検証する
    /// （並びの不一致の除外を依存の除外より先に掛ける＝外した主キーの変更は選んでいない扱いになる）。
    /// 主キーの変更を選ばずに追加だけを選んだときも同じく外す。
    /// </summary>
    [Fact(
        DisplayName = "依存: 外した・選んでいない主キーの変更を前提にする外部キーの追加は外して警告する"
    )]
    public void AddForeignKeyOnNewKeyOrder_WithExcludedKeyChange_IsExcluded()
    {
        var (pair, reference, _, context) = PairReferenceScenario();
        var reordered = pair.Clone(preserveId: true);
        reordered.PrimaryKeyColumnIds = [reordered.Columns[1].Id, reordered.Columns[0].Id];
        reference.Columns.Add(Col("xb", "INT"));
        reference.Columns.Add(Col("xa", "INT"));
        var addFk = new SchemaDiffItem
        {
            Kind = SchemaDiffKind.AddForeignKey,
            TableName = "ref",
            ParentEntity = reordered,
            ChildEntity = reference,
            Relationship = new Relationship { ConstraintName = "FK_ref_new" },
            ForeignKeyName = "FK_ref_new",
            ForeignKeyColumnPairs = [new("b", "xb"), new("a", "xa")],
            IsSelected = true,
        };

        var withKeyChange = new SyncPlanner().BuildPlan(
            [AlterPk("pair", reordered), addFk],
            NonTransactionalOrderedCaps,
            context
        );
        withKeyChange.IsEmpty.Should().BeTrue();
        withKeyChange
            .Warnings.Select(w => (w.Kind, w.Detail))
            .Should()
            .BeEquivalentTo([
                (SyncPlanWarningKind.ForeignKeyKeyOrderMismatch, "FK_ref"),
                (
                    SyncPlanWarningKind.SelectedItemRequiresUnselectedItem,
                    "FK_ref_new (PRIMARY KEY)"
                ),
            ]);

        var addOnly = new SyncPlanner().BuildPlan([addFk], MySqlLikeCaps, context);
        addOnly.IsEmpty.Should().BeTrue();
        addOnly
            .Warnings.Should()
            .ContainSingle()
            .Which.Kind.Should()
            .Be(SyncPlanWarningKind.SelectedItemRequiresUnselectedItem);
    }

    /// <summary>
    /// 参照する列の並びを問う方言（SQL Server・MySQL）では、複合キーと違う並びで参照する外部キーの追加を警告し、
    /// 問わない方言では警告しないことを検証する。
    /// </summary>
    [Fact(
        DisplayName = "候補キー: 並びを問う方言では、複合キーと違う並びで参照する外部キーの追加を警告する"
    )]
    public void AddForeignKey_CompositeKeyInOtherOrder_WarnsWhereOrderMatters()
    {
        var (add, context) = AddForeignKeyScenario(
            new("kind", "customer_kind"),
            new("code", "customer_code")
        );
        WithUnique(context.LiveEntities[0], "UQ_customer_code_kind", "code", "kind");

        new SyncPlanner()
            .BuildPlan([add], FkRebuildCaps, context)
            .Warnings.Should()
            .ContainSingle(w => w.Kind == SyncPlanWarningKind.ForeignKeyNotOnCandidateKey)
            .Which.Detail.Should()
            .Be("FK_new");
        new SyncPlanner()
            .BuildPlan(
                [add],
                FkRebuildCaps with
                {
                    ReferencedKeyRequiresSameColumnOrder = false,
                },
                context
            )
            .Warnings.Should()
            .BeEmpty();
    }

    /// <summary>付け直しが巻き戻らず、参照する列の並びも問う方言（MySQL 相当）</summary>
    private static readonly SyncDialectCapabilities NonTransactionalOrderedCaps = MySqlLikeCaps with
    {
        TransactionalDdl = false,
    };

    /// <summary>pair(a, b) の複合主キーを、ref(ra, rb) の外部キー FK_ref が (a, b) の並びで参照する live を作る</summary>
    private static (
        Entity Pair,
        Entity Ref,
        Relationship Fk,
        SyncPlanContext Context
    ) PairReferenceScenario()
    {
        var pair = new Entity
        {
            TableName = "pair",
            Columns =
            {
                new Column
                {
                    Name = "a",
                    DataType = "INT",
                    IsPrimaryKey = true,
                    IsNullable = false,
                },
                new Column
                {
                    Name = "b",
                    DataType = "INT",
                    IsPrimaryKey = true,
                    IsNullable = false,
                },
            },
        };
        var reference = new Entity
        {
            TableName = "ref",
            Columns = { PkId(), Col("ra", "INT"), Col("rb", "INT") },
        };
        var fk = new Relationship
        {
            SourceEntityId = pair.Id,
            TargetEntityId = reference.Id,
            ColumnPairs =
            [
                new(pair.Columns[0].Id, reference.Columns[1].Id),
                new(pair.Columns[1].Id, reference.Columns[2].Id),
            ],
            ConstraintName = "FK_ref",
        };

        return (
            pair,
            reference,
            fk,
            new SyncPlanContext { LiveEntities = [pair, reference], LiveRelationships = [fk] }
        );
    }

    /// <summary>
    /// 付け直しが巻き戻らず参照する列の並びも問う方言（MySQL）では、参照される複合主キーの列の並びだけを入れ替える
    /// 主キーの変更を計画から外して警告し、巻き戻る方言（SQL Server）と並びを問わない方言では従来どおり計画に入れる
    /// ことを検証する。
    /// </summary>
    [Fact(
        DisplayName = "並びの不一致: MySQL では参照される複合主キーの並びだけの入れ替えを計画から外して警告する"
    )]
    public void ReorderReferencedCompositeKey_NonTransactional_IsExcluded()
    {
        var (pair, _, _, context) = PairReferenceScenario();
        var reordered = pair.Clone(preserveId: true);
        reordered.PrimaryKeyColumnIds = [reordered.Columns[1].Id, reordered.Columns[0].Id];

        SyncPlan Plan(SyncDialectCapabilities capabilities) =>
            new SyncPlanner().BuildPlan([AlterPk("pair", reordered)], capabilities, context);

        var mySql = Plan(NonTransactionalOrderedCaps);
        mySql.IsEmpty.Should().BeTrue();
        mySql
            .Warnings.Should()
            .ContainSingle()
            .Which.Should()
            .Be(
                new SyncPlanWarning(SyncPlanWarningKind.ForeignKeyKeyOrderMismatch, "ref", "FK_ref")
            );

        var sqlServer = Plan(MySqlLikeCaps);
        sqlServer.Sections.Should().Contain(s => s.Kind == SchemaDiffKind.AlterPrimaryKey);
        sqlServer
            .Warnings.Should()
            .ContainSingle(w => w.Kind == SyncPlanWarningKind.ForeignKeyRebuildMayLoseCandidateKey);

        var unordered = Plan(
            NonTransactionalOrderedCaps with
            {
                ReferencedKeyRequiresSameColumnOrder = false,
            }
        );
        unordered.Sections.Should().Contain(s => s.Kind == SchemaDiffKind.AlterPrimaryKey);
        unordered.Warnings.Should().BeEmpty();
    }

    /// <summary>
    /// 並び以外が原因の証明できない付け直し（参照する列が新しい主キーと別の列集合になる）は、MySQL でも従来どおり
    /// 警告して計画に入れることを検証する（取り込んでいない索引で通る構成があるため止めない）。
    /// </summary>
    [Fact(DisplayName = "並びの不一致: 並び以外が原因の付け直しは MySQL でも警告して計画に入れる")]
    public void ChangeReferencedKeyColumnSet_NonTransactional_StillRuns()
    {
        var (pair, _, _, context) = PairReferenceScenario();
        var shrunk = pair.Clone(preserveId: true);
        shrunk.Columns[1].IsPrimaryKey = false;

        var plan = new SyncPlanner().BuildPlan(
            [AlterPk("pair", shrunk)],
            NonTransactionalOrderedCaps,
            context
        );

        plan.Sections.Should().Contain(s => s.Kind == SchemaDiffKind.AlterPrimaryKey);
        plan.Warnings.Should()
            .ContainSingle()
            .Which.Kind.Should()
            .Be(SyncPlanWarningKind.ForeignKeyRebuildMayLoseCandidateKey);
    }

    /// <summary>
    /// MySQL では、列の並びだけが参照先のキーと合わなくなる外部キーの作り直しを計画から外して警告することを検証する
    /// （外して付け直すと、付け直しが失敗して外部キーが消えたまま残る）。巻き戻る方言では警告して計画に入れる。
    /// </summary>
    [Fact(
        DisplayName = "並びの不一致: MySQL では並びだけが参照先のキーと合わなくなる外部キーの作り直しを外して警告する"
    )]
    public void AlterForeignKeyIntoOtherOrder_NonTransactional_IsExcluded()
    {
        var (_, _, fk, context) = PairReferenceScenario();
        var targetEntities = context.LiveEntities.Select(e => e.Clone(preserveId: true)).ToList();
        var reversed = new Relationship
        {
            SourceEntityId = fk.SourceEntityId,
            TargetEntityId = fk.TargetEntityId,
            ColumnPairs = [.. fk.ColumnPairs.Select(p => p.Clone()).Reverse()],
            ConstraintName = fk.ConstraintName,
        };

        SyncPlan Plan(SyncDialectCapabilities capabilities)
        {
            var items = new SchemaDiffService()
                .Compute(
                    context.LiveEntities,
                    context.LiveRelationships,
                    targetEntities,
                    [reversed],
                    capabilities
                )
                .Items;
            items.Should().ContainSingle(i => i.Kind == SchemaDiffKind.AlterForeignKey);

            foreach (var item in items)
            {
                item.IsSelected = true;
            }

            return new SyncPlanner().BuildPlan(items, capabilities, context);
        }

        var mySql = Plan(NonTransactionalOrderedCaps);
        mySql.IsEmpty.Should().BeTrue();
        mySql
            .Warnings.Should()
            .ContainSingle()
            .Which.Should()
            .Be(
                new SyncPlanWarning(SyncPlanWarningKind.ForeignKeyKeyOrderMismatch, "ref", "FK_ref")
            );

        var sqlServer = Plan(MySqlLikeCaps);
        sqlServer.Sections.Should().Contain(s => s.Kind == SchemaDiffKind.DropForeignKey);
        sqlServer
            .Warnings.Should()
            .ContainSingle(w => w.Kind == SyncPlanWarningKind.ForeignKeyNotOnCandidateKey);
    }

    /// <summary>
    /// 同じ列ペアに名前の違う外部キーが 2 本あるとき、片方を明示的に外しても、もう片方は列の変更に合わせて
    /// 外して付け直すことを検証する（照合が名前を見ず、残る 1 本まで「外した」扱いになって列の変更が失敗していた）。
    /// </summary>
    [Fact(
        DisplayName = "外部キーの照合: 同じ列ペアの 2 本の片方を外しても、もう 1 本は列の変更に合わせて外し直す"
    )]
    public void DropOneOfSamePairForeignKeys_OtherIsStillRebuilt()
    {
        var (_, _, relA, _, context) = TwoForeignKeysOnSameColumnScenario();
        var targetEntities = context.LiveEntities.Select(e => e.Clone(preserveId: true)).ToList();
        targetEntities.Single(e => e.TableName == "orders").Columns[1].DataType = "BIGINT";
        targetEntities.Single(e => e.TableName == "customer").Columns[0].DataType = "BIGINT";
        var targetA = new Relationship
        {
            SourceEntityId = relA.SourceEntityId,
            TargetEntityId = relA.TargetEntityId,
            ColumnPairs = [.. relA.ColumnPairs.Select(p => p.Clone())],
            ConstraintName = relA.ConstraintName,
        };

        var items = new SchemaDiffService()
            .Compute(
                context.LiveEntities,
                context.LiveRelationships,
                targetEntities,
                [targetA],
                MySqlLikeCaps
            )
            .Items;

        foreach (var item in items)
        {
            item.IsSelected =
                item.Kind is SchemaDiffKind.AlterColumn or SchemaDiffKind.DropForeignKey;
        }

        var plan = new SyncPlanner().BuildPlan(items, MySqlLikeCaps, context);

        plan.Sections.Single(s => s.Kind == SchemaDiffKind.DropForeignKey)
            .Items.Select(i => i.ForeignKeyName)
            .Should()
            .BeEquivalentTo(["FK_a", "FK_b"]);
        plan.Sections.Single(s => s.Kind == SchemaDiffKind.AddForeignKey)
            .Items.Select(i => i.ForeignKeyName ?? i.Relationship?.ConstraintName)
            .Should()
            .Equal("FK_a");
        plan.Warnings.Should().BeEmpty();
    }

    /// <summary>
    /// 再構築の方言で、同じ列ペアの外部キー 2 本のうち 1 本の参照アクションだけを変えると、変えた 1 本が
    /// 新しい定義になり、もう 1 本はそのまま残ることを検証する（合成が構成列だけで照合し 2 本とも外していた）。
    /// </summary>
    [Fact(
        DisplayName = "外部キー変更: 再構築の方言で同じ列ペアの 2 本のうち 1 本を変えても、もう 1 本は残る"
    )]
    public void AlterOneOfDuplicatedForeignKeys_RebuildDialect_KeepsTheOther()
    {
        var (_, _, relA, relB, context) = TwoForeignKeysOnSameColumnScenario();
        relA.ConstraintName = null;
        relB.ConstraintName = null;
        relB.OnDelete = ForeignKeyReferentialAction.Cascade;

        var targetEntities = context.LiveEntities.Select(e => e.Clone(preserveId: true)).ToList();
        Relationship Copy(Relationship r, ForeignKeyReferentialAction onUpdate) =>
            new()
            {
                SourceEntityId = r.SourceEntityId,
                TargetEntityId = r.TargetEntityId,
                ColumnPairs = [.. r.ColumnPairs.Select(p => p.Clone())],
                OnDelete = r.OnDelete,
                OnUpdate = onUpdate,
            };

        var items = new SchemaDiffService()
            .Compute(
                context.LiveEntities,
                context.LiveRelationships,
                targetEntities,
                [
                    Copy(relA, ForeignKeyReferentialAction.Cascade),
                    Copy(relB, ForeignKeyReferentialAction.NoAction),
                ],
                RebuildCaps
            )
            .Items;

        items.Should().ContainSingle(i => i.Kind == SchemaDiffKind.AlterForeignKey);

        foreach (var item in items)
        {
            item.IsSelected = item.Kind == SchemaDiffKind.AlterForeignKey;
        }

        var plan = new SyncPlanner().BuildPlan(items, RebuildCaps, context);

        plan.Rebuilds.Should()
            .ContainSingle()
            .Which.ForeignKeys.Select(fk => (fk.OnDelete, fk.OnUpdate))
            .Should()
            .BeEquivalentTo([
                (ForeignKeyReferentialAction.NoAction, ForeignKeyReferentialAction.Cascade),
                (ForeignKeyReferentialAction.Cascade, ForeignKeyReferentialAction.NoAction),
            ]);
    }

    // ---------------- 大文字小文字だけ違う live テーブルの引き当て ----------------

    /// <summary>live に大文字小文字だけ違うテーブルが共存するときの、再構築の引き当てを検証する</summary>
    /// <remarks>
    /// 引き当ての規則は差分と同じ（序数一致の 1 件を優先し、大文字小文字を無視した一致は 1 件のときだけ）。
    /// 曖昧な名前は並び順で片方を選ばず「live に無いテーブル」と同じ扱いにする＝再構築せず、項目はセクションへ
    /// 残り、レンダラーがスキップを明示する
    /// </remarks>
    [Fact(
        DisplayName = "再構築: 大文字小文字だけ違う live テーブルは序数一致で引き、曖昧なら再構築せずスキップを明示する"
    )]
    public void Rebuild_CaseOnlyDifferentLiveTables_ExactOrSkipped()
    {
        var upper = new Entity { TableName = "Dup", Columns = { PkId(), Col("note", "TEXT") } };
        var lower = new Entity { TableName = "dup", Columns = { PkId(), Col("note", "TEXT") } };
        var context = new SyncPlanContext { LiveEntities = [upper, lower] };

        SchemaDiffItem AlterNote(string table) =>
            new()
            {
                Kind = SchemaDiffKind.AlterColumn,
                TableName = table,
                ColumnName = "note",
                Column = Col("note", "INTEGER"),
                IsSelected = true,
            };

        // 序数一致: 小文字のほうを作り直す
        var exact = new SyncPlanner().BuildPlan([AlterNote("dup")], RebuildCaps, context);
        exact.Rebuilds.Should().ContainSingle().Which.TableName.Should().Be("dup");

        // 曖昧: 再構築せず、項目はセクションへ残ってスキップのコメントになる
        var ambiguousItem = AlterNote("DUP");
        var ambiguous = new SyncPlanner().BuildPlan([ambiguousItem], RebuildCaps, context);
        ambiguous.Rebuilds.Should().BeEmpty();
        ambiguous.Sections.Should().ContainSingle().Which.Items.Should().Equal(ambiguousItem);

        var script = new QuickER.Provider.Sqlite.SqliteSyncScriptBuilder().Build(ambiguous);
        script.Should().Contain("-- Skipped 'AlterColumn' on DUP");
    }

    /// <summary>ネイティブ列順変更でも、曖昧な live テーブルは並べ替え計画に含めないことを検証する</summary>
    [Fact(
        DisplayName = "列順変更: 大文字小文字だけ違う live テーブルが曖昧なら並べ替え計画に含めない"
    )]
    public void NativeReorder_CaseOnlyDifferentLiveTables_AmbiguousIsDropped()
    {
        var context = new SyncPlanContext
        {
            LiveEntities = [Ent("Dup", "a", "b"), Ent("dup", "a", "b")],
        };

        var exact = new SyncPlanner().BuildPlan(
            [Reorder("Dup", Ent("Dup", "b", "a"))],
            NativeCaps,
            context
        );
        exact.Reorders.Should().ContainSingle().Which.TableName.Should().Be("Dup");

        var ambiguous = new SyncPlanner().BuildPlan(
            [Reorder("DUP", Ent("DUP", "b", "a"))],
            NativeCaps,
            context
        );
        ambiguous.Reorders.Should().BeEmpty();
    }

    // ---------------- 28-6: 外し直し・再構築の細部 ----------------

    /// <summary>
    /// 列の変更で外し直しを要しない方言（PostgreSQL 相当）でも、主キーの変更で付け直す外部キーの構成列の型を変えて
    /// 両端の型が食い違うなら、外さずに警告することを検証する（付け直しが必ず失敗する）。
    /// </summary>
    [Fact(
        DisplayName = "型の食い違い: 主キーの変更で付け直す外部キーも、型が食い違うなら外さずに警告する"
    )]
    public void AlterPrimaryKey_WithMismatchedForeignKeyColumn_WarnsWithoutRebuild()
    {
        var (customer, _, _, context) = LiveFkScenario();
        customer.Columns.Add(
            new Column
            {
                Name = "code",
                DataType = "INT",
                IsNullable = false,
            }
        );
        var target = customer.Clone(preserveId: true);
        target.Columns.Single(c => c.Name == "code").IsPrimaryKey = true;

        var plan = new SyncPlanner().BuildPlan(
            [
                AlterPk("customer", target),
                new SchemaDiffItem
                {
                    Kind = SchemaDiffKind.AlterColumn,
                    TableName = "orders",
                    ColumnName = "customer_id",
                    Column = Col("customer_id", "VARCHAR(10)"),
                    IsSelected = true,
                },
            ],
            new SyncDialectCapabilities(),
            context
        );

        plan.Sections.Should().NotContain(s => s.Kind == SchemaDiffKind.DropForeignKey);
        plan.Warnings.Should()
            .ContainSingle(w => w.Kind == SyncPlanWarningKind.ForeignKeyColumnTypeMismatch)
            .Which.Detail.Should()
            .Be("FK_orders_customer");
    }

    /// <summary>
    /// 主キーの変更で外し直すのは、旧主キーに支えられた外部キーだけであることを検証する。一意制約を参照する外部キーは
    /// 主キーの解除に巻き込まれないので外さない。参照先の索引の先頭の列を要する方言（MySQL）では、旧主キーの先頭の列だけを
    /// 参照する外部キー（非標準キー）も旧主キーに支えられうるので外し直す（外さないと主キーの解除がエラー 1553 になる）。
    /// </summary>
    [Fact(
        DisplayName = "主キー変更: 外し直すのは旧主キーに支えられた外部キーだけ（MySQL は先頭の列だけを参照するものも）"
    )]
    public void AlterPrimaryKey_RebuildsOnlyForeignKeysOnOldKey()
    {
        // 一意制約 (code) を参照する外部キーは、主キー (id) → (id, code) の変更に巻き込まれない
        // （code は最初から NOT NULL＝主キーへ加える NOT NULL 化の列の変更が外し直しを呼ばない形にする）
        var (customer, orders, rel, _) = LiveFkScenario();
        customer.Columns.Add(
            new Column
            {
                Name = "code",
                DataType = "INT",
                IsNullable = false,
            }
        );
        WithUnique(customer, "UQ_customer_code", "code");
        orders.Columns.Add(Col("customer_code", "INT"));
        rel.ColumnPairs = [new(customer.Columns[1].Id, orders.Columns[2].Id)];
        var widened = customer.Clone(preserveId: true);
        widened.Columns[1].IsPrimaryKey = true;
        widened.Columns[1].IsNullable = false;
        var context = new SyncPlanContext
        {
            LiveEntities = [customer, orders],
            LiveRelationships = [rel],
        };

        new SyncPlanner()
            .BuildPlan([AlterPk("customer", widened)], FkRebuildCaps, context)
            .Sections.Should()
            .NotContain(s => s.Kind == SchemaDiffKind.DropForeignKey);

        // 旧主キー (id, code) の先頭の列 id だけを参照する外部キー
        var (prefixParent, _, _, prefixContext) = LiveFkScenario();
        prefixParent.Columns.Add(
            new Column
            {
                Name = "code",
                DataType = "INT",
                IsPrimaryKey = true,
                IsNullable = false,
            }
        );
        var codeOnly = prefixParent.Clone(preserveId: true);
        codeOnly.Columns[0].IsPrimaryKey = false;

        SyncPlan Plan(SyncDialectCapabilities capabilities) =>
            new SyncPlanner().BuildPlan(
                [AlterPk("customer", codeOnly)],
                capabilities,
                prefixContext
            );

        Plan(MySqlLikeCaps)
            .Sections.Single(s => s.Kind == SchemaDiffKind.DropForeignKey)
            .Items.Should()
            .ContainSingle()
            .Which.ForeignKeyName.Should()
            .Be("FK_orders_customer");
        Plan(FkRebuildCaps)
            .Sections.Should()
            .NotContain(s => s.Kind == SchemaDiffKind.DropForeignKey);
    }

    /// <summary>
    /// 一意制約の暗黙の付け直しは、構成列が同じ計画で消えるなら外すだけで付け直さないことを検証する
    /// （付け直すと消えた列を参照して失敗する。外部キーの外し直しと対称）。
    /// </summary>
    [Fact(DisplayName = "暗黙の一意制約: 構成列が消える一意制約は外すだけで付け直さない")]
    public void ImplicitUniqueRebuild_WithDroppedColumn_DropsOnly()
    {
        var live = WithUnique(
            new Entity { TableName = "t", Columns = { PkId(), Col("a", "INT"), Col("b", "INT") } },
            "UQ_t_ab",
            "a",
            "b"
        );

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
                    Kind = SchemaDiffKind.DropColumn,
                    TableName = "t",
                    ColumnName = "b",
                    Entity = live,
                    Column = live.Columns[2],
                    IsSelected = true,
                },
            ],
            FkRebuildCaps,
            new SyncPlanContext { LiveEntities = [live] }
        );

        plan.Sections.Should().Contain(s => s.Kind == SchemaDiffKind.DropUniqueConstraint);
        plan.Sections.Should().NotContain(s => s.Kind == SchemaDiffKind.AddUniqueConstraint);
    }

    /// <summary>一意制約の削除の警告は、同期で外す外部キー（子表の削除で消えるもの）には出さないことを検証する</summary>
    [Fact(DisplayName = "一意制約: 削除の警告は、同期で消える外部キーには出さない")]
    public void DropUnique_ForeignKeyThatGoesAway_DoesNotWarn()
    {
        var (customer, _, _, context) = LiveFkScenario();
        WithUnique(customer, "UQ_customer_id", "id");
        var dropUnique = UniqueItem(
            SchemaDiffKind.DropUniqueConstraint,
            "customer",
            "UQ_customer_id",
            ["id"]
        );

        new SyncPlanner()
            .BuildPlan([dropUnique], FkRebuildCaps, context)
            .Warnings.Should()
            .ContainSingle(w =>
                w.Kind == SyncPlanWarningKind.UniqueConstraintDropMayBreakForeignKey
            );
        new SyncPlanner()
            .BuildPlan(
                [
                    dropUnique,
                    new SchemaDiffItem
                    {
                        Kind = SchemaDiffKind.DropTable,
                        TableName = "orders",
                        Entity = context.LiveEntities[1],
                        IsSelected = true,
                    },
                ],
                FkRebuildCaps,
                context
            )
            .Warnings.Should()
            .NotContain(w => w.Kind == SyncPlanWarningKind.UniqueConstraintDropMayBreakForeignKey);
    }

    /// <summary>
    /// 名前の無い同じ組の外部キーが 2 本あるとき、暗黙の付け直しの名前が図全体から求めた名前（DDL と同じ規則＝2 本目は
    /// 一意化した名前）になることを検証する（リレーションごとに既定名を求めると 2 本とも同じ名前になった）。
    /// </summary>
    [Fact(
        DisplayName = "暗黙の付け直し: 名前の無い同じ組の外部キー 2 本は、DDL と同じ別々の名前で付け直す"
    )]
    public void ImplicitRebuild_UnnamedSamePairForeignKeys_UseDdlNames()
    {
        var (_, _, _, _, context) = TwoForeignKeysOnSameColumnScenario();

        foreach (var relationship in context.LiveRelationships)
        {
            relationship.ConstraintName = null;
        }

        var plan = new SyncPlanner().BuildPlan(
            [
                new SchemaDiffItem
                {
                    Kind = SchemaDiffKind.AlterColumn,
                    TableName = "orders",
                    ColumnName = "customer_id",
                    Column = Col("customer_id", "BIGINT"),
                    IsSelected = true,
                },
                new SchemaDiffItem
                {
                    Kind = SchemaDiffKind.AlterColumn,
                    TableName = "customer",
                    ColumnName = "id",
                    Column = Col("id", "BIGINT"),
                    IsSelected = true,
                },
            ],
            FkRebuildCaps,
            context
        );

        var expected = ConstraintNames.ResolveForeignKeyNames(
            context.LiveEntities,
            context.LiveRelationships
        );
        plan.Sections.Single(s => s.Kind == SchemaDiffKind.AddForeignKey)
            .Items.Select(i => i.ForeignKeyName)
            .Should()
            .BeEquivalentTo(expected.Values)
            .And.OnlyHaveUniqueItems();
    }

    /// <summary>
    /// 再構築の合成で解決できない項目（同じ計画で消える列の一意制約）は、処理済みにせずセクションへ残すことを検証する
    /// （レンダラーがスキップのコメントを出す。処理済みにすると、何も変えずに成功扱いになり、次の差分でまた出た）。
    /// </summary>
    [Fact(DisplayName = "再構築: 解決できない項目は処理済みにせずセクションへ残す")]
    public void Rebuild_UnresolvableItem_StaysInSection()
    {
        var live = new Entity
        {
            TableName = "t",
            Columns = { PkId(), Col("x", "INT"), Col("y", "INT") },
        };
        var addUnique = UniqueItem(SchemaDiffKind.AddUniqueConstraint, "t", "UQ_t_x", ["x"]);

        var plan = new SyncPlanner().BuildPlan(
            [
                new SchemaDiffItem
                {
                    Kind = SchemaDiffKind.DropColumn,
                    TableName = "t",
                    ColumnName = "x",
                    Entity = live,
                    Column = live.Columns[1],
                    IsSelected = true,
                },
                addUnique,
            ],
            RebuildCaps,
            new SyncPlanContext { LiveEntities = [live] }
        );

        var rebuild = plan.Rebuilds.Should().ContainSingle().Which;
        rebuild.SourceItems.Should().NotContain(addUnique);
        rebuild.NewDefinition.UniqueConstraints.Should().BeEmpty();
        plan.Sections.SelectMany(s => s.Items).Should().Contain(addUnique);
    }

    /// <summary>
    /// 再構築の方言で、削除を選んだ表は作り直さず（作り直してから消すのは無駄で、意味の無い喪失警告も出た）、
    /// その表への作り直しの項目は表ごと消えるので捨てることを検証する。
    /// </summary>
    [Fact(DisplayName = "再構築: 削除を選んだ表は作り直さない")]
    public void Rebuild_DroppedTable_IsNotRebuilt()
    {
        var live = new Entity { TableName = "t", Columns = { PkId(), Col("x", "INT") } };

        var plan = new SyncPlanner().BuildPlan(
            [
                new SchemaDiffItem
                {
                    Kind = SchemaDiffKind.AlterColumn,
                    TableName = "t",
                    ColumnName = "x",
                    Column = Col("x", "TEXT"),
                    IsSelected = true,
                },
                new SchemaDiffItem
                {
                    Kind = SchemaDiffKind.DropTable,
                    TableName = "t",
                    Entity = live,
                    IsSelected = true,
                },
            ],
            RebuildCaps,
            new SyncPlanContext
            {
                LiveEntities = [live],
                TableCreateSql = new Dictionary<string, string>
                {
                    ["t"] = "CREATE TABLE t (id INT PRIMARY KEY, x INT DEFAULT 0)",
                },
            }
        );

        plan.Rebuilds.Should().BeEmpty();
        plan.Warnings.Should().BeEmpty();
        plan.Sections.Select(s => s.Kind).Should().Equal(SchemaDiffKind.DropTable);
    }
}
