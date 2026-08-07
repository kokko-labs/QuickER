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
            new SyncPlanContext()
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
            new SyncDialectCapabilities(),
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
        // 主キーを (id) → (id, code) へ拡張する＝被参照列集合 (id, code) はそのまま候補キーであり続ける
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

        var plan = new SyncPlanner().BuildPlan(
            [AlterPk("parent", targetParent)],
            new SyncDialectCapabilities(),
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
    [InlineData("child", "parent_id", "BIGINT")]
    [InlineData("child", "order_no", "BIGINT")]
    [InlineData("parent", "code", "BIGINT")]
    public void AlterColumn_OnCompositeForeignKeyColumn_RebuildsAllColumnPairs(
        string table,
        string column,
        string newType
    )
    {
        var context = CompositeFkScenario();
        var alter = new SchemaDiffItem
        {
            Kind = SchemaDiffKind.AlterColumn,
            TableName = table,
            ColumnName = column,
            Column = Col(column, newType),
            IsSelected = true,
        };

        var plan = new SyncPlanner().BuildPlan([alter], FkRebuildCaps, context);

        plan.Sections.Single(s => s.Kind == SchemaDiffKind.AlterColumn).Items.Should().Equal(alter);

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
        // 依存 FK の自動 DROP → 再 ADD 自体は従来どおり行う
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
}
