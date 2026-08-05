using System.Linq;
using AwesomeAssertions;
using QuickER.Model;
using QuickER.Provider;
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
                Item(SchemaDiffKind.AlterPrimaryKey),
                Item(SchemaDiffKind.DropForeignKey),
                Item(SchemaDiffKind.AlterColumn),
                Item(SchemaDiffKind.AddColumn),
                Item(SchemaDiffKind.AddTable),
            ],
            new SyncDialectCapabilities()
        );

        // DropForeignKey が AlterColumn より先なのは意図的:
        // FK 依存列の型変更を通すため、先に FK を外しておく必要がある（SQL Server は Msg 5074 で失敗する）
        // AlterPrimaryKey は AlterColumn の後・DropColumn の前:
        // 新 PK 列の NOT NULL 化を先に済ませ、旧 PK 列の削除は PK を外した後に行う必要がある
        plan.Sections.Select(s => s.Kind)
            .Should()
            .Equal(
                SchemaDiffKind.AddTable,
                SchemaDiffKind.AddColumn,
                SchemaDiffKind.DropForeignKey,
                SchemaDiffKind.AlterColumn,
                SchemaDiffKind.AlterPrimaryKey,
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

    /// <summary>FK 参加列の型変更に FK の外し直しが必要な方言（SQL Server 相当）のケーパビリティ</summary>
    private static readonly SyncDialectCapabilities FkRebuildCaps = new()
    {
        AlterColumnRequiresForeignKeyRebuild = true,
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
            SourceColumnId = customer.Columns[0].Id,
            TargetColumnId = orders.Columns[1].Id,
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

        plan.Sections.Select(s => s.Kind)
            .Should()
            .Equal(
                SchemaDiffKind.DropForeignKey,
                SchemaDiffKind.AlterPrimaryKey,
                SchemaDiffKind.AddForeignKey
            );

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
        var alter = new SchemaDiffItem
        {
            Kind = SchemaDiffKind.AlterColumn,
            TableName = "orders",
            ColumnName = "customer_id",
            Column = Col("customer_id", "BIGINT"),
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
        var alter = new SchemaDiffItem
        {
            Kind = SchemaDiffKind.AlterColumn,
            TableName = "customer",
            ColumnName = "id",
            Column = Col("id", "BIGINT"),
            IsSelected = true,
        };

        var plan = new SyncPlanner().BuildPlan([alter], FkRebuildCaps, context);

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
}
