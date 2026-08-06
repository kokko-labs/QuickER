using System.Linq;
using AwesomeAssertions;
using QuickER.Model;
using QuickER.Provider;
using QuickER.SqlServer;
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

    // ---------------- 計画時の警告（SyncPlan.Warnings） ----------------

    /// <summary>
    /// 主キー変更で被参照列が新しい主キーから外れる場合、自動再作成する FK について
    /// 「候補キーでなくなる恐れがある」警告が積まれることを検証する（実行はブロックしない）。
    /// </summary>
    [Fact(DisplayName = "主キー変更: 被参照列が新主キーから外れると候補キー喪失の警告が積まれる")]
    public void AlterPrimaryKey_ReferencedColumnLeavesPrimaryKey_AddsWarning()
    {
        var (_, _, _, context) = LiveFkScenario();
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

        // 一意制約は取り込んでいないため断定できず、警告に留める（FK の自動 DROP → 再 ADD は従来どおり出る）
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
        var (_, _, _, context) = LiveFkScenario();
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
            SourceColumnId = customer.Columns[0].Id,
            TargetColumnId = orders.Columns[1].Id,
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
            new SyncDialectCapabilities(),
            context
        );

        plan.Warnings.Should().BeEmpty();
        plan.Sections.Should().Contain(s => s.Kind == SchemaDiffKind.AddForeignKey);
    }

    // ---------------- 複合外部キーの作り直しを招く変更の除外（逐次 DDL 方言） ----------------

    /// <summary>
    /// 複合外部キー（取込で列対応が失われた FK）と、同じ子テーブルが持つ無関係な単列 FK を含む live を組み立てる。
    /// </summary>
    /// <remarks>
    /// 複合外部キーの子側は列対応を失うため <c>TargetColumnId</c> が null になり、列の解決は命名規約の
    /// フォールバックへ落ちる（＝live のリレーションからは構成列を復元できない）。そのため照合範囲は
    /// 取込警告（<see cref="CompositeForeignKeyImportWarning"/>）の全構成列から組み立てる。
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
            SourceColumnId = parent.Columns[0].Id,
            // 複合外部キーは意味モデルが列対応を表現できず、子列の指定を失う
            TargetColumnId = null,
            ConstraintName = "FK_child_parent",
        };
        var simple = new Relationship
        {
            SourceEntityId = vendor.Id,
            TargetEntityId = child.Id,
            SourceColumnId = vendor.Columns[0].Id,
            TargetColumnId = child.Columns[3].Id,
            ConstraintName = "FK_child_vendor",
        };

        return new SyncPlanContext
        {
            LiveEntities = [parent, vendor, child],
            LiveRelationships = [composite, simple],
            CompositeForeignKeyWarnings =
            [
                new CompositeForeignKeyImportWarning(
                    "FK_child_parent",
                    "child",
                    ["parent_id", "order_no"],
                    "parent",
                    ["id", "code"]
                ),
            ],
        };
    }

    /// <summary>
    /// 複合外部キーが参照している親テーブルの主キー変更は、計画から除外されて警告が積まれることを検証する。
    /// </summary>
    /// <remarks>
    /// 実行すると複合外部キーが単列 FK として作り直される（MySQL は成功して静かに壊れ、Oracle は
    /// 部分適用で FK が消える）ため、暗黙の DROP → 再 ADD ごと計画へ入れない。
    /// </remarks>
    [Fact(DisplayName = "複合外部キー: 参照先テーブルの主キー変更は計画から除外され警告が積まれる")]
    public void AlterPrimaryKey_OnCompositeForeignKeyParent_IsBlocked()
    {
        var context = CompositeFkScenario();
        var targetParent = new Entity
        {
            TableName = "parent",
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
            [AlterPk("parent", targetParent)],
            new SyncDialectCapabilities(),
            context
        );

        // 主キー変更も、それに伴う FK の自動 DROP → 再 ADD も計画に現れない
        plan.Sections.Should().BeEmpty();

        var warning = plan.Warnings.Should().ContainSingle().Which;
        warning.Kind.Should().Be(SyncPlanWarningKind.CompositeForeignKeyBlocksChange);
        warning.TableName.Should().Be("parent");
        warning.Detail.Should().BeEmpty();
    }

    /// <summary>
    /// 複合外部キーと無関係な親テーブル（同じ子テーブルが持つ別 FK の参照先）の主キー変更は、
    /// 従来どおり計画へ入ることを検証する（照合が子テーブル名だけに落ちていないことの担保）。
    /// </summary>
    [Fact(
        DisplayName = "複合外部キー: 無関係な FK の参照先テーブルの主キー変更は従来どおり計画へ入る"
    )]
    public void AlterPrimaryKey_OnUnrelatedParent_IsNotBlocked()
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
    /// FK 参加列の型変更に FK の外し直しが必要な方言では、複合外部キーが関与する列の定義変更が
    /// 計画から除外されることを検証する（同じテーブルの無関係な列は従来どおり）。
    /// </summary>
    /// <remarks>
    /// 「無関係な列」には複合外部キーの構成列でない <c>memo</c> を使う。以前はここで第 2 構成列の
    /// <c>order_no</c> を無関係な列として扱っていたが、それは誤り——構成列はどれを変えても外部キー全体が
    /// 作り直される（<see cref="AlterColumn_OnCompositeForeignKeySecondaryColumns_IsBlocked"/> で固定）。
    /// </remarks>
    [Fact(
        DisplayName = "複合外部キー: capability が真の方言では関与列の定義変更が計画から除外される"
    )]
    public void AlterColumn_OnCompositeForeignKeyColumn_IsBlockedWhenCapabilityIsTrue()
    {
        var context = CompositeFkScenario();
        var alterFkColumn = new SchemaDiffItem
        {
            Kind = SchemaDiffKind.AlterColumn,
            TableName = "child",
            ColumnName = "parent_id",
            Column = Col("parent_id", "BIGINT"),
            IsSelected = true,
        };
        var alterOtherColumn = new SchemaDiffItem
        {
            Kind = SchemaDiffKind.AlterColumn,
            TableName = "child",
            ColumnName = "memo",
            Column = Col("memo", "NVARCHAR(50)"),
            IsSelected = true,
        };

        var plan = new SyncPlanner().BuildPlan(
            [alterFkColumn, alterOtherColumn],
            FkRebuildCaps,
            context
        );

        // 複合外部キーが関与する列だけが落ち、無関係な列の変更は残る
        plan.Sections.Should().ContainSingle().Which.Items.Should().Equal(alterOtherColumn);

        var warning = plan.Warnings.Should().ContainSingle().Which;
        warning.Kind.Should().Be(SyncPlanWarningKind.CompositeForeignKeyBlocksChange);
        warning.TableName.Should().Be("child");
        warning.Detail.Should().Be("parent_id");
    }

    /// <summary>
    /// 複合外部キーの<b>副構成列</b>（子側の 2 列目・親側の 2 列目）の定義変更も計画から除外されることを検証する。
    /// </summary>
    /// <remarks>
    /// 副構成列は意味モデルへ劣化した live リレーションからは復元できない（子側は <c>TargetColumnId</c> が
    /// 無く命名規約フォールバックで 1 列だけが選ばれる）ため、live 列挙を照合の土台にすると素通りしていた。
    /// 素通りすると SQL Server では暗黙の FK 再構築にも載らず、実行のたびに Msg 5074 で失敗し続ける
    /// （ロールバックされるので壊れはしないが、説明のない恒久的な失敗になる）。照合範囲を取込警告の
    /// 全構成列から組み立てることで、実行前に「複合外部キーのため同期できない」と伝える。
    /// </remarks>
    [Fact(DisplayName = "複合外部キー: 副構成列（子・親の 2 列目）の定義変更も計画から除外される")]
    public void AlterColumn_OnCompositeForeignKeySecondaryColumns_IsBlocked()
    {
        var context = CompositeFkScenario();
        // 子側の第 2 構成列（FK 列名の命名規約に合わないため live 列挙では解決されない）
        var alterChildSecondary = new SchemaDiffItem
        {
            Kind = SchemaDiffKind.AlterColumn,
            TableName = "child",
            ColumnName = "order_no",
            Column = Col("order_no", "BIGINT"),
            IsSelected = true,
        };
        // 親側の第 2 構成列（被参照列としては主キーの 1 列目しか復元されない）
        var alterParentSecondary = new SchemaDiffItem
        {
            Kind = SchemaDiffKind.AlterColumn,
            TableName = "parent",
            ColumnName = "code",
            Column = Col("code", "BIGINT"),
            IsSelected = true,
        };

        var plan = new SyncPlanner().BuildPlan(
            [alterChildSecondary, alterParentSecondary],
            FkRebuildCaps,
            context
        );

        // 両方が落ちる＝暗黙の FK 再構築も注入されない
        plan.Sections.Should().BeEmpty();

        plan.Warnings.Should()
            .OnlyContain(w => w.Kind == SyncPlanWarningKind.CompositeForeignKeyBlocksChange);
        plan.Warnings.Select(w => (w.TableName, w.Detail))
            .Should()
            .Equal(("child", "order_no"), ("parent", "code"));
    }

    /// <summary>
    /// FK の外し直しが不要な方言では、複合外部キーが関与する列の定義変更も従来どおり計画へ入ることを検証する
    /// （そもそも FK を作り直さないため、静かに壊れる経路が無い）。
    /// </summary>
    [Fact(DisplayName = "複合外部キー: capability が偽の方言では関与列の定義変更を止めない")]
    public void AlterColumn_OnCompositeForeignKeyColumn_IsNotBlockedWhenCapabilityIsFalse()
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
    /// rebuild 方言（SQLite）で、複合外部キーの子テーブルは再構築対象から外れて警告が積まれ、
    /// 他テーブルの再構築は続行することを検証する。
    /// </summary>
    /// <remarks>
    /// 再構築すると列対応を失った複合外部キーが単列外部キーとして作り直される（成功して静かに壊れる）ため、
    /// 該当テーブルだけを止める。畳み込まれなかった項目はセクションへ残り、レンダラーがスキップを明示する。
    /// </remarks>
    [Fact(
        DisplayName = "SQLite: 複合外部キーの子テーブルは再構築せず警告を積む（他テーブルは続行）"
    )]
    public void Rebuild_CompositeForeignKeyChildTable_IsBlocked()
    {
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
        var memo = new Entity { TableName = "memo", Columns = { PkId(), Col("note", "TEXT") } };
        var compositeWarning = new CompositeForeignKeyImportWarning(
            "FK_order_line_orders",
            "order_line",
            ["order_id", "line_no"],
            "orders",
            ["id", "line_no"]
        );

        var blockedAlter = new SchemaDiffItem
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
            [blockedAlter, otherAlter],
            RebuildCaps,
            new SyncPlanContext
            {
                LiveEntities = [orderLine, memo],
                CompositeForeignKeyWarnings = [compositeWarning],
            }
        );

        // 複合外部キーを持たない memo だけが再構築される
        plan.Rebuilds.Should().ContainSingle().Which.TableName.Should().Be("memo");

        var warning = plan.Warnings.Should().ContainSingle().Which;
        warning.Kind.Should().Be(SyncPlanWarningKind.RebuildBlockedByCompositeForeignKey);
        warning.TableName.Should().Be("order_line");

        // 畳み込まれなかった項目はセクションへ残る（SQLite レンダラーがスキップコメントを出す）
        plan.Sections.Should().ContainSingle().Which.Items.Should().Equal(blockedAlter);
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

        var dropPrimaryKey = sql.IndexOf("DROP CONSTRAINT [' + @pk + ']", StringComparison.Ordinal);
        var alterColumnAt = sql.IndexOf("ALTER COLUMN [old_id]", StringComparison.Ordinal);
        var addPrimaryKey = sql.IndexOf("ADD CONSTRAINT [PK_T]", StringComparison.Ordinal);

        dropPrimaryKey.Should().BeGreaterThan(-1);
        alterColumnAt.Should().BeGreaterThan(dropPrimaryKey);
        addPrimaryKey.Should().BeGreaterThan(alterColumnAt);
    }

    /// <summary>
    /// 主キー変更を含まない計画の出力が 3 フェーズ化の前後でバイト不変であることを固定する
    /// （フェーズを持たないセクションの見出しは従来どおり種別名のみ）。
    /// </summary>
    [Fact(DisplayName = "主キー変更を含まない計画の出力はバイト不変")]
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
}
