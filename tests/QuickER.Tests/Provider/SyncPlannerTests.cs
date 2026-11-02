using System;
using System.Collections.Generic;
using System.Linq;
using AwesomeAssertions;
using QuickER.Model;
using QuickER.Provider;
using QuickER.Provider.MySql;
using QuickER.Provider.Oracle;
using QuickER.Provider.PostgreSql;
using QuickER.Provider.Sqlite;
using QuickER.Provider.SqlServer;
using QuickER.Tests.TestSupport;
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

    // ---------------- テーブル再構築（rebuild）方言の集約 ----------------

    /// <summary>ALTER COLUMN も FK の後付けもできない再構築方言のケーパビリティ</summary>
    private static readonly SyncDialectCapabilities RebuildCaps =
        TestDialectCapabilities.Neutral with
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

    // ---------------- 列順変更（ReorderColumns） ----------------

    /// <summary>ネイティブ列順変更方言（MySQL 相当）のケーパビリティ</summary>
    private static readonly SyncDialectCapabilities NativeCaps =
        TestDialectCapabilities.Neutral with
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

    // ---------------- 主キー変更（AlterPrimaryKey）と依存 FK の自動 DROP → 再 ADD ----------------

    /// <summary>
    /// FK 参加列・主キー／一意制約の構成列の型変更に、制約の外し直しが必要な方言（SQL Server 相当）のケーパビリティ
    /// </summary>
    private static readonly SyncDialectCapabilities FkRebuildCaps =
        TestDialectCapabilities.Neutral with
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
    /// 計画が足した項目が、DB 側の図のオブジェクト（表・リレーション）をそのまま運ぶことを検証する
    /// （書式の行では同一性を書けないので残す。項目の並び・選択・説明は同期計画の期待の行
    /// <c>plan-alter-pk-injects-implicit-fk-drop-and-readd</c> が見る）。
    /// </summary>
    /// <param name="shape">形（implicit-fk-rebuild＝主キーの変更で参照している外部キーを自動で外して付け直す）</param>
    [Theory(DisplayName = "計画が足した項目は DB 側の図のオブジェクトを運ぶ")]
    [InlineData("implicit-fk-rebuild")]
    public void PlanItems_CarryLiveObjects(string shape)
    {
        switch (shape)
        {
            case "implicit-fk-rebuild":
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
                    TestDialectCapabilities.Neutral,
                    context
                );

                var drop = plan
                    .Sections.Single(s => s.Kind == SchemaDiffKind.DropForeignKey)
                    .Items.Should()
                    .ContainSingle()
                    .Which;
                drop.ChildEntity.Should().BeSameAs(orders);
                drop.ParentEntity.Should().BeSameAs(customer);
                drop.Relationship.Should().BeSameAs(rel);

                var add = plan
                    .Sections.Single(s => s.Kind == SchemaDiffKind.AddForeignKey)
                    .Items.Should()
                    .ContainSingle()
                    .Which;
                add.ChildEntity.Should().BeSameAs(orders);
                add.ParentEntity.Should().BeSameAs(customer);
                add.Relationship.Should().BeSameAs(rel);
                break;
            }

            default:
                throw new ArgumentOutOfRangeException(nameof(shape), shape, null);
        }
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
            TestDialectCapabilities.Neutral
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
    /// （フェーズを持たないセクションの見出しは種別名のみ。計画のセクションにフェーズが付かないことは同期計画の期待の行
    /// <c>plan-without-pk-change-has-no-phase</c> が見る）。
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
            // 文字列型でない列の NULL 許容だけの変更＝照合順序も DEFAULT 制約も読み直さない素の ALTER COLUMN になる
            Column = Col("Memo", "int"),
            OldColumn = new Column
            {
                Name = "Memo",
                DataType = "int",
                IsNullable = false,
            },
            IsSelected = true,
        };

        var plan = new SyncPlanner().BuildPlan(
            [alterColumn, addColumn],
            TestDialectCapabilities.Neutral
        );
        var sql = new SqlServerSyncScriptBuilder().Build(plan);

        var expected = string.Join(
            Environment.NewLine,
            "-- ===== AddColumn (1 items) =====",
            "ALTER TABLE [T] ADD [Email] nvarchar(200) NOT NULL;",
            "GO",
            "",
            "-- ===== AlterColumn (1 items) =====",
            "ALTER TABLE [T] ALTER COLUMN [Memo] int NULL;",
            "GO",
            "",
            ""
        );

        sql.Should().Be(expected);
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
                capabilities ?? TestDialectCapabilities.Neutral
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
            TestDialectCapabilities.Neutral,
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
            .BuildPlan([alter], TestDialectCapabilities.Neutral, context)
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
            TestDialectCapabilities.Neutral,
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
    private static readonly SyncDialectCapabilities MySqlLikeCaps =
        TestDialectCapabilities.Neutral with
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
    [InlineData("nchar(2)", "NCHAR(4)", true)]
    [InlineData("nvarchar(10)", "nvarchar(20)", true)]
    [InlineData("VARCHAR2(50 CHAR)", "varchar2(100 char)", true)]
    [InlineData("NVARCHAR2(10)", "NVARCHAR2(20)", true)]
    [InlineData("RAW(16)", "RAW(32)", true)]
    [InlineData("VARCHAR2(50 CHAR)", "VARCHAR2(100 BYTE)", false)]
    [InlineData("VARCHAR2(50)", "VARCHAR2(100 BYTE)", false)]
    [InlineData("VARCHAR2(10)", "NVARCHAR2(10)", false)]
    public void AreForeignKeyCompatibleTypes_AllowsStringLengthOnly(
        string left,
        string right,
        bool expected
    )
    {
        var lenient = TestDialectCapabilities.Neutral with
        {
            ForeignKeyAllowsStringLengthDifference = true,
        };

        lenient.AreForeignKeyCompatibleTypes(left, right).Should().Be(expected);
        TestDialectCapabilities
            .Neutral.AreForeignKeyCompatibleTypes(left, right)
            .Should()
            .Be(left.Equals(right, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// 主キーの変更で付け直す外部キーの片側の長さだけを変えるとき、長さの違いを許す方言（SQL Server 以外の 4 方言）では
    /// 外部キーを外して付け直し、SQL Server では両端の型の食い違いとして警告することを、5 方言の実際の能力の宣言で検証する。
    /// Oracle の長さの単位語が両端で違うときは長さだけの違いではないので、従来の食い違いの扱いへ落ちる。
    /// </summary>
    [Theory(
        DisplayName = "型の食い違い: 長さだけ違う外部キーの付け直しは SQL Server 以外の方言で許す（単位語の違いは食い違い）"
    )]
    [InlineData(SqlServerProvider.ProviderName, "varchar(10)", "varchar(20)", false)]
    [InlineData(PostgreSqlProvider.ProviderName, "varchar(10)", "varchar(20)", true)]
    [InlineData(MySqlProvider.ProviderName, "varchar(10)", "varchar(20)", true)]
    [InlineData(OracleProvider.ProviderName, "VARCHAR2(10 CHAR)", "VARCHAR2(20 CHAR)", true)]
    [InlineData(OracleProvider.ProviderName, "VARCHAR2(10 CHAR)", "VARCHAR2(20 BYTE)", false)]
    [InlineData(SqliteProvider.ProviderName, "VARCHAR(10)", "VARCHAR(20)", true)]
    public void AlterPrimaryKey_StringLengthOnlyForeignKey_FollowsDialectDeclaration(
        string dialect,
        string keyType,
        string newChildType,
        bool rebuilds
    )
    {
        IDatabaseProvider[] providers =
        [
            new SqlServerProvider(),
            new PostgreSqlProvider(),
            new MySqlProvider(),
            new OracleProvider(),
            new SqliteProvider(),
        ];
        var capabilities = providers.Single(p => p.Name == dialect).SyncCapabilities;
        var (customer, orders, _, context) = LiveFkScenario();
        customer.Columns[0].DataType = keyType;
        orders.Columns[1].DataType = keyType;
        customer.Columns.Add(
            new Column
            {
                Name = "code",
                DataType = "INT",
                IsNullable = false,
            }
        );
        // 主キーを外しても外部キーの参照先が候補キーのまま残るよう、id に一意制約を置く
        WithUnique(customer, "UQ_customer_id", "id");
        var target = customer.Clone(preserveId: true);
        target.Columns[0].IsPrimaryKey = false;
        target.Columns.Single(c => c.Name == "code").IsPrimaryKey = true;

        var plan = new SyncPlanner().BuildPlan(
            [
                AlterPk("customer", target),
                new SchemaDiffItem
                {
                    Kind = SchemaDiffKind.AlterColumn,
                    TableName = "orders",
                    ColumnName = "customer_id",
                    Column = Col("customer_id", newChildType),
                    IsSelected = true,
                },
            ],
            capabilities,
            context
        );

        if (rebuilds && capabilities.ColumnReorder == ColumnReorderMode.Rebuild)
        {
            // テーブル再構築の方言（SQLite）は、外部キーを子表の作り直しで外して付け直す
            plan.Warnings.Should().BeEmpty();
            plan.Rebuilds.Should().NotBeEmpty();
        }
        else if (rebuilds)
        {
            plan.Warnings.Should().BeEmpty();
            plan.Sections.Select(s => s.Kind)
                .Should()
                .Contain(SchemaDiffKind.DropForeignKey)
                .And.Contain(SchemaDiffKind.AddForeignKey);
        }
        else
        {
            plan.Sections.Should().NotContain(s => s.Kind == SchemaDiffKind.DropForeignKey);
            plan.Warnings.Should()
                .ContainSingle(w => w.Kind == SyncPlanWarningKind.ForeignKeyColumnTypeMismatch);
        }
    }

    /// <summary>
    /// 選んだ外部キーの作り直しが、同じ計画の列の変更で同期後に両端の型が食い違うなら、逐次 DDL の 4 方言では削除と追加を
    /// まとめて計画から外して名指しし、ほかの外部キーも外し直さないことを、5 方言の実際の能力の宣言で検証する。
    /// 付け直しは必ず失敗し、DDL が暗黙コミットされる方言では外部キーが消えたまま残るため、外さずに残す。
    /// テーブル再構築の方言（SQLite）は外部キーの型を確かめないので、作り直しをそのまま再構築へ畳む。
    /// 子の列も同じ型へ変えれば食い違わないので、作り直しを残して警告しない。
    /// </summary>
    [Theory(
        DisplayName = "型の食い違い: 選んだ外部キーの作り直しも、同期後に両端の型が食い違うなら削除ごと外して警告する"
    )]
    [InlineData(SqlServerProvider.ProviderName, false)]
    [InlineData(PostgreSqlProvider.ProviderName, false)]
    [InlineData(MySqlProvider.ProviderName, false)]
    [InlineData(OracleProvider.ProviderName, false)]
    [InlineData(SqliteProvider.ProviderName, false)]
    [InlineData(SqlServerProvider.ProviderName, true)]
    [InlineData(PostgreSqlProvider.ProviderName, true)]
    [InlineData(MySqlProvider.ProviderName, true)]
    [InlineData(OracleProvider.ProviderName, true)]
    [InlineData(SqliteProvider.ProviderName, true)]
    public void SelectedForeignKeyAlter_PostSyncTypeMismatch_IsExcludedAsAWhole(
        string dialect,
        bool childChangedToo
    )
    {
        var (customer, orders, rel, context) = LiveFkScenario();
        // 別の表の外部キー（子の列も BIGINT へ変えるので同期後に型が揃う）。列の変更で外部キーを外し直す方言
        // （SQL Server・MySQL）ではふだん暗黙に外して付け直すが、食い違う外部キーがあれば列の変更が必ず失敗するので外さない
        var invoice = new Entity
        {
            TableName = "invoice",
            Columns = { PkId(), Col("customer_id", "INT") },
        };
        var invoiceRel = new Relationship
        {
            SourceEntityId = customer.Id,
            TargetEntityId = invoice.Id,
            ColumnPairs = [new(customer.Columns[0].Id, invoice.Columns[1].Id)],
            ConstraintName = "FK_invoice_customer",
        };
        context = new SyncPlanContext
        {
            LiveEntities = [customer, orders, invoice],
            LiveRelationships = [rel, invoiceRel],
        };

        var target = context.LiveEntities.Select(e => e.Clone(preserveId: true)).ToList();
        target.Single(e => e.TableName == "customer").Columns[0].DataType = "BIGINT";
        target.Single(e => e.TableName == "invoice").Columns[1].DataType = "BIGINT";

        if (childChangedToo)
        {
            target
                .Single(e => e.TableName == "orders")
                .Columns.Single(c => c.Name == "customer_id")
                .DataType = "BIGINT";
        }

        var plan = PlanAllDiffs(
            dialect,
            context,
            target,
            [
                new Relationship
                {
                    SourceEntityId = rel.SourceEntityId,
                    TargetEntityId = rel.TargetEntityId,
                    ColumnPairs = [.. rel.ColumnPairs.Select(p => p.Clone())],
                    ConstraintName = rel.ConstraintName,
                    OnDelete = ForeignKeyReferentialAction.Cascade,
                },
                invoiceRel,
            ],
            out var capabilities
        );

        var mismatchWarnings = plan.Warnings.Where(w =>
            w.Kind == SyncPlanWarningKind.ForeignKeyColumnTypeMismatch
        );

        if (capabilities.ColumnReorder == ColumnReorderMode.Rebuild)
        {
            mismatchWarnings.Should().BeEmpty();
            plan.Rebuilds.Should().NotBeEmpty();
            return;
        }

        if (childChangedToo)
        {
            mismatchWarnings.Should().BeEmpty();
            plan.Sections.Single(s => s.Kind == SchemaDiffKind.AddForeignKey)
                .Items.Should()
                .Contain(i =>
                    i.ForeignKeyName == "FK_orders_customer"
                    && i.Relationship!.OnDelete == ForeignKeyReferentialAction.Cascade
                );
            return;
        }

        mismatchWarnings
            .Should()
            .ContainSingle()
            .Which.Should()
            .Be(
                new SyncPlanWarning(
                    SyncPlanWarningKind.ForeignKeyColumnTypeMismatch,
                    "orders",
                    "FK_orders_customer"
                )
            );
        plan.Sections.Should()
            .NotContain(s =>
                s.Kind == SchemaDiffKind.DropForeignKey || s.Kind == SchemaDiffKind.AddForeignKey
            );
        plan.Sections.Where(s => s.Kind == SchemaDiffKind.AlterColumn)
            .SelectMany(s => s.Items)
            .Should()
            .Contain(i => i.TableName == "customer" && i.ColumnName == "id");

        // DDL が暗黙コミットされ、外部キーを張ったまま両端の型を変えられない方言（MySQL）では、食い違う外部キーに阻まれて
        // 必ず断られる列の変更を計画の先頭へ移し、何も変えずに失敗させる。ほかの方言は並びを変えない
        var first = plan.Sections[0];

        if (!capabilities.TransactionalDdl && capabilities.AlterColumnRequiresForeignKeyRebuild)
        {
            first.Kind.Should().Be(SchemaDiffKind.AlterColumn);
            first
                .Items.Should()
                .ContainSingle()
                .Which.Should()
                .Match<SchemaDiffItem>(i => i.TableName == "customer" && i.ColumnName == "id");
        }
        else
        {
            plan.Sections.Count(s => s.Kind == SchemaDiffKind.AlterColumn).Should().Be(1);
        }
    }

    /// <summary>
    /// 選んだ外部キーの追加が、同じ計画の列の変更で同期後に両端の型が食い違うなら、逐次 DDL の 4 方言では追加を外して
    /// 名指しすることを検証する（追加は必ず失敗する）。テーブル再構築の方言（SQLite）は追加をそのまま再構築へ畳む。
    /// </summary>
    [Theory(
        DisplayName = "型の食い違い: 選んだ外部キーの追加も、同期後に両端の型が食い違うなら外して警告する"
    )]
    [InlineData(SqlServerProvider.ProviderName)]
    [InlineData(PostgreSqlProvider.ProviderName)]
    [InlineData(MySqlProvider.ProviderName)]
    [InlineData(OracleProvider.ProviderName)]
    [InlineData(SqliteProvider.ProviderName)]
    public void SelectedForeignKeyAdd_PostSyncTypeMismatch_IsExcluded(string dialect)
    {
        var (customer, orders, rel, _) = LiveFkScenario();
        var context = new SyncPlanContext
        {
            LiveEntities = [customer, orders],
            LiveRelationships = [],
        };
        var target = context.LiveEntities.Select(e => e.Clone(preserveId: true)).ToList();
        target.Single(e => e.TableName == "customer").Columns[0].DataType = "BIGINT";

        var plan = PlanAllDiffs(dialect, context, target, [rel], out var capabilities);

        var mismatchWarnings = plan.Warnings.Where(w =>
            w.Kind == SyncPlanWarningKind.ForeignKeyColumnTypeMismatch
        );

        if (capabilities.ColumnReorder == ColumnReorderMode.Rebuild)
        {
            mismatchWarnings.Should().BeEmpty();
            plan.Rebuilds.Should().NotBeEmpty();
            return;
        }

        mismatchWarnings
            .Should()
            .ContainSingle()
            .Which.Should()
            .Be(
                new SyncPlanWarning(
                    SyncPlanWarningKind.ForeignKeyColumnTypeMismatch,
                    "orders",
                    "FK_orders_customer"
                )
            );
        plan.Sections.Should().NotContain(s => s.Kind == SchemaDiffKind.AddForeignKey);
        plan.Sections.Should().Contain(s => s.Kind == SchemaDiffKind.AlterColumn);
    }

    /// <summary>
    /// 構成列の型をこの同期で変えない外部キーの作り直しは、live の両端の型が食い違って見えても外さないことを検証する
    /// （PostgreSQL は int の列から bigint の列への外部キーを作れる。判定を live の型にまで掛けると、参照アクションだけを
    /// 変える正当な作り直しを止める）。
    /// </summary>
    [Fact(
        DisplayName = "型の食い違い: 構成列の型を変えない外部キーの作り直しは、live の型が食い違って見えても外さない"
    )]
    public void SelectedForeignKeyAlter_LiveTypesOnly_IsKept()
    {
        var (customer, _, rel, context) = LiveFkScenario();
        customer.Columns[0].DataType = "BIGINT";
        var target = context.LiveEntities.Select(e => e.Clone(preserveId: true)).ToList();

        var plan = PlanAllDiffs(
            PostgreSqlProvider.ProviderName,
            context,
            target,
            [
                new Relationship
                {
                    SourceEntityId = rel.SourceEntityId,
                    TargetEntityId = rel.TargetEntityId,
                    ColumnPairs = [.. rel.ColumnPairs.Select(p => p.Clone())],
                    ConstraintName = rel.ConstraintName,
                    OnDelete = ForeignKeyReferentialAction.Cascade,
                },
            ],
            out _
        );

        plan.Warnings.Should().BeEmpty();
        plan.Sections.Select(s => s.Kind)
            .Should()
            .Equal(SchemaDiffKind.DropForeignKey, SchemaDiffKind.AddForeignKey);
    }

    /// <summary>
    /// 方言のプロバイダの能力の宣言で、live から図への差分をすべて選んで計画を組み立てる
    /// </summary>
    private static SyncPlan PlanAllDiffs(
        string dialect,
        SyncPlanContext context,
        List<Entity> targetEntities,
        List<Relationship> targetRelationships,
        out SyncDialectCapabilities capabilities
    )
    {
        IDatabaseProvider[] providers =
        [
            new SqlServerProvider(),
            new PostgreSqlProvider(),
            new MySqlProvider(),
            new OracleProvider(),
            new SqliteProvider(),
        ];
        capabilities = providers.Single(p => p.Name == dialect).SyncCapabilities;
        var diff = new SchemaDiffService().Compute(
            context.LiveEntities,
            context.LiveRelationships,
            targetEntities,
            targetRelationships,
            capabilities
        );

        foreach (var item in diff.Items)
        {
            item.IsSelected = true;
        }

        return new SyncPlanner().BuildPlan(diff.Items, capabilities, context);
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
    /// 親の主キーの列の削除は、その列を外す主キーの変更も選ぶ（選ばなければ列の削除そのものを外す）。
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

        List<SchemaDiffItem> items = [drop];
        List<SchemaDiffKind> expected = [SchemaDiffKind.DropForeignKey, drop.Kind];

        // 親の主キーの列の削除は、その列を外す主キーの変更（主キーを外すだけ）と一緒に選ぶ。親の表にはほかの列を
        // 持たせる（id だけの表から id を消すと、表に列が残らないので列の削除そのものを外す＝TableLeftWithoutColumns）
        if (table == "customer" && column == "id")
        {
            customer.Columns.Add(
                new Column
                {
                    Name = "name",
                    DataType = "nvarchar(50)",
                    IsNullable = true,
                }
            );
            var keyless = customer.Clone(preserveId: true);
            keyless.Columns.Single(c => c.Name == "id").IsPrimaryKey = false;
            items.Add(
                new SchemaDiffItem
                {
                    Kind = SchemaDiffKind.AlterPrimaryKey,
                    TableName = "customer",
                    Entity = keyless,
                    IsSelected = true,
                }
            );
            expected = [SchemaDiffKind.DropForeignKey, SchemaDiffKind.AlterPrimaryKey, drop.Kind];
        }

        var plan = new SyncPlanner().BuildPlan(items, FkRebuildCaps, context);

        plan.Sections.Select(s => s.Kind).Should().Equal(expected);
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

        var plan = new SyncPlanner().BuildPlan([item], TestDialectCapabilities.Neutral, context);

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

        // DDL が暗黙コミットされる方言では、証明できない追加を外部キーの追加の最後へ回す（断られても、ほかの付け直しは済んでいる）。
        // 巻き戻る方言は選んだ順のまま
        var (unproven, orderContext) = AddForeignKeyScenario(
            new ForeignKeyColumnNamePair("code", "customer_code")
        );
        var proven = new SchemaDiffItem
        {
            Kind = SchemaDiffKind.AddForeignKey,
            TableName = "orders",
            ParentEntity = unproven.ParentEntity,
            ChildEntity = unproven.ChildEntity,
            Relationship = new Relationship { ConstraintName = "FK_proven" },
            ForeignKeyName = "FK_proven",
            ForeignKeyColumnPairs = [new("id", "customer_kind")],
            IsSelected = true,
        };

        foreach (
            var (caps, expected) in new[]
            {
                (NonTransactionalOrderedCaps, new[] { "FK_proven", "FK_new" }),
                (FkRebuildCaps, new[] { "FK_new", "FK_proven" }),
            }
        )
        {
            new SyncPlanner()
                .BuildPlan([unproven, proven], caps, orderContext)
                .Sections.Single(s => s.Kind == SchemaDiffKind.AddForeignKey)
                .Items.Select(i => i.ForeignKeyName)
                .Should()
                .Equal(expected);
        }

        // 同じ列集合の一意制約が 2 つあれば、1 つを外してももう 1 つが同期後の候補キーとして残る（照合は名前と列集合）
        var (toTwin, twinContext) = AddForeignKeyScenario(
            new ForeignKeyColumnNamePair("code", "customer_code")
        );
        WithUnique(twinContext.LiveEntities[0], "UQ1", "code");
        WithUnique(twinContext.LiveEntities[0], "UQ2", "code");
        Warnings(
                toTwin,
                twinContext,
                UniqueItem(SchemaDiffKind.DropUniqueConstraint, "customer", "UQ1", ["code"])
            )
            .Should()
            .BeEmpty();
        Warnings(
                toTwin,
                twinContext,
                UniqueItem(SchemaDiffKind.DropUniqueConstraint, "customer", "UQ1", ["code"]),
                UniqueItem(SchemaDiffKind.DropUniqueConstraint, "customer", "UQ2", ["code"])
            )
            .Should()
            .Contain(SyncPlanWarningKind.ForeignKeyNotOnCandidateKey);
    }

    /// <summary>
    /// 参照する列の並びを問う方言（SQL Server・MySQL）では、外部キーの追加を同期後の主キーの並びで出すことを検証する。
    /// 参照される複合主キーの並びを入れ替える変更と一緒に選べば新しい並び（旧主キーを参照する外部キーの付け直しも
    /// 新しい並び）、主キーの変更を選ばなければ live の並び。問わない方言では図の並びのまま。どれも警告しない。
    /// </summary>
    [Fact(
        DisplayName = "並べ替え: 外部キーの追加は、同期後の主キーの並びで出す（主キーの並びを変えれば新しい並び）"
    )]
    public void AddForeignKeyOnNewKeyOrder_FollowsPostSyncPrimaryKey()
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

        foreach (var caps in new[] { NonTransactionalOrderedCaps, MySqlLikeCaps })
        {
            var withKeyChange = new SyncPlanner().BuildPlan(
                [AlterPk("pair", reordered), addFk],
                caps,
                context
            );
            withKeyChange.Warnings.Should().BeEmpty();
            AddedForeignKeyPairs(withKeyChange, "FK_ref").Should().Equal(("b", "rb"), ("a", "ra"));
            AddedForeignKeyPairs(withKeyChange, "FK_ref_new")
                .Should()
                .Equal(("b", "xb"), ("a", "xa"));

            var addOnly = new SyncPlanner().BuildPlan([addFk], caps, context);
            addOnly.Warnings.Should().BeEmpty();
            AddedForeignKeyPairs(addOnly, "FK_ref_new").Should().Equal(("a", "xa"), ("b", "xb"));
        }

        var unordered = new SyncPlanner().BuildPlan(
            [AlterPk("pair", reordered), addFk],
            NonTransactionalOrderedCaps with
            {
                ReferencedKeyRequiresSameColumnOrder = false,
            },
            context
        );
        unordered.Warnings.Should().BeEmpty();
        AddedForeignKeyPairs(unordered, "FK_ref").Should().Equal(("a", "ra"), ("b", "rb"));
        AddedForeignKeyPairs(unordered, "FK_ref_new").Should().Equal(("b", "xb"), ("a", "xa"));
    }

    /// <summary>
    /// 参照する列の並びを問う方言では、一意制約と違う並びで参照する外部キーの追加を一意制約の並びで出し、警告しないことを
    /// 検証する（live の一意制約と、同じ計画で追加する一意制約）。問わない方言では図の並びのまま。
    /// </summary>
    [Fact(
        DisplayName = "並べ替え: 並びを問う方言では、一意制約と違う並びで参照する外部キーの追加を一意制約の並びで出す"
    )]
    public void AddForeignKey_CompositeKeyInOtherOrder_IsArrangedWhereOrderMatters()
    {
        var (add, context) = AddForeignKeyScenario(
            new("kind", "customer_kind"),
            new("code", "customer_code")
        );
        WithUnique(context.LiveEntities[0], "UQ_customer_code_kind", "code", "kind");

        var ordered = new SyncPlanner().BuildPlan([add], FkRebuildCaps, context);
        ordered.Warnings.Should().BeEmpty();
        AddedForeignKeyPairs(ordered, "FK_new")
            .Should()
            .Equal(("code", "customer_code"), ("kind", "customer_kind"));

        var unordered = new SyncPlanner().BuildPlan(
            [add],
            FkRebuildCaps with
            {
                ReferencedKeyRequiresSameColumnOrder = false,
            },
            context
        );
        unordered.Warnings.Should().BeEmpty();
        AddedForeignKeyPairs(unordered, "FK_new")
            .Should()
            .Equal(("kind", "customer_kind"), ("code", "customer_code"));

        // 同じ計画で追加する一意制約の並びにもそろえる
        var (addToNewUnique, newUniqueContext) = AddForeignKeyScenario(
            new("kind", "customer_kind"),
            new("code", "customer_code")
        );
        var withNewUnique = new SyncPlanner().BuildPlan(
            [
                addToNewUnique,
                UniqueItem(SchemaDiffKind.AddUniqueConstraint, "customer", null, ["code", "kind"]),
            ],
            FkRebuildCaps,
            newUniqueContext
        );
        withNewUnique.Warnings.Should().BeEmpty();
        AddedForeignKeyPairs(withNewUnique, "FK_new")
            .Should()
            .Equal(("code", "customer_code"), ("kind", "customer_kind"));
    }

    /// <summary>
    /// 参照先に主キーと同じ列の集合で並びの違う一意制約があるとき、並びを問う方言では外部キーの追加を主キーの並びで出すことを
    /// 検証する（DDL の <see cref="ForeignKeyColumnOrder.ArrangeToReferencedKey"/> と同じく主キーを先に試す。一意制約を先に
    /// 試すと、同期と DDL で同じ外部キーの並びが食い違う）。
    /// </summary>
    [Fact(
        DisplayName = "並べ替え: 主キーと同じ列の集合の一意制約があっても、外部キーの追加は主キーの並びで出す"
    )]
    public void AddForeignKey_PrimaryKeyAndUniqueOnSameColumns_FollowsPrimaryKey()
    {
        var (pair, reference, _, context) = PairReferenceScenario();
        WithUnique(pair, "UQ_pair_b_a", "b", "a");
        reference.Columns.Add(Col("xb", "INT"));
        reference.Columns.Add(Col("xa", "INT"));
        var addFk = new SchemaDiffItem
        {
            Kind = SchemaDiffKind.AddForeignKey,
            TableName = "ref",
            ParentEntity = pair,
            ChildEntity = reference,
            Relationship = new Relationship { ConstraintName = "FK_ref_new" },
            ForeignKeyName = "FK_ref_new",
            ForeignKeyColumnPairs = [new("b", "xb"), new("a", "xa")],
            IsSelected = true,
        };

        var plan = new SyncPlanner().BuildPlan([addFk], MySqlLikeCaps, context);

        plan.Warnings.Should().BeEmpty();
        AddedForeignKeyPairs(plan, "FK_ref_new").Should().Equal(("a", "xa"), ("b", "xb"));
        ForeignKeyColumnOrder
            .ArrangeToReferencedKey(addFk.ForeignKeyColumnPairs, pair)
            .Select(p => (p.ParentColumn, p.ChildColumn))
            .Should()
            .Equal(("a", "xa"), ("b", "xb"));
    }

    /// <summary>
    /// 参照先の列がどのキー（同期後の主キー・一意制約）ともちょうど一致しない外部キーの追加は、並びを問う方言でも並べ替えず、
    /// 従来どおり <see cref="SyncPlanWarningKind.ForeignKeyNotOnCandidateKey"/> で名指しすることを検証する
    /// （MySQL で一意でない索引の列を参照する形。QuickER は一意でない索引を取り込まないので、並べ替える先のキーが無い）。
    /// </summary>
    [Fact(
        DisplayName = "並べ替え: 参照先がどのキーとも一致しない外部キーの追加は並べ替えず、従来どおり警告する"
    )]
    public void AddForeignKey_NotOnAnyKey_IsNotArrangedAndWarns()
    {
        var (add, context) = AddForeignKeyScenario(
            new("kind", "customer_kind"),
            new("code", "customer_code")
        );

        var plan = new SyncPlanner().BuildPlan([add], NonTransactionalOrderedCaps, context);

        plan.Warnings.Should()
            .ContainSingle()
            .Which.Should()
            .Be(
                new SyncPlanWarning(
                    SyncPlanWarningKind.ForeignKeyNotOnCandidateKey,
                    "orders",
                    "FK_new"
                )
            );
        AddedForeignKeyPairs(plan, "FK_new")
            .Should()
            .Equal(("kind", "customer_kind"), ("code", "customer_code"));
    }

    /// <summary>計画の AddForeignKey セクションの、指定した名前の外部キーの列ペア（親の列, 子の列）</summary>
    private static IEnumerable<(string Parent, string Child)> AddedForeignKeyPairs(
        SyncPlan plan,
        string name
    ) =>
        plan
            .Sections.Single(s => s.Kind == SchemaDiffKind.AddForeignKey)
            .Items.Single(i => i.ForeignKeyName == name)
            .ForeignKeyColumnPairs.Select(p => (p.ParentColumn, p.ChildColumn));

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
    /// 参照される複合主キーの列の並びだけを入れ替える主キーの変更は、旧主キーに支えられた外部キーを外して付け直す
    /// 項目を引き続き注入し（<c>DependsOnOldPrimaryKey</c>）、並びを問う方言（SQL Server・MySQL）では付け直しを
    /// 新しい主キーの並びで出すことを検証する（どの方言も警告しない）。並びを問わない方言は live の並びのまま付け直す。
    /// </summary>
    [Fact(
        DisplayName = "並べ替え: 参照される複合主キーの並びだけを変えると、外部キーを外して新しい主キーの並びで付け直す"
    )]
    public void ReorderReferencedCompositeKey_RebuildsForeignKeyInNewOrder()
    {
        var (pair, _, _, context) = PairReferenceScenario();
        var reordered = pair.Clone(preserveId: true);
        reordered.PrimaryKeyColumnIds = [reordered.Columns[1].Id, reordered.Columns[0].Id];

        SyncPlan Plan(SyncDialectCapabilities capabilities) =>
            new SyncPlanner().BuildPlan([AlterPk("pair", reordered)], capabilities, context);

        foreach (
            var (caps, expected) in new[]
            {
                (NonTransactionalOrderedCaps, new[] { ("b", "rb"), ("a", "ra") }),
                (MySqlLikeCaps, new[] { ("b", "rb"), ("a", "ra") }),
                (
                    NonTransactionalOrderedCaps with
                    {
                        ReferencedKeyRequiresSameColumnOrder = false,
                    },
                    new[] { ("a", "ra"), ("b", "rb") }
                ),
            }
        )
        {
            var plan = Plan(caps);

            plan.Warnings.Should().BeEmpty();
            plan.Sections.Select(s => s.Kind)
                .Should()
                .Equal(
                    SchemaDiffKind.DropForeignKey,
                    SchemaDiffKind.AlterPrimaryKey,
                    SchemaDiffKind.AlterPrimaryKey,
                    SchemaDiffKind.AddForeignKey
                );
            plan.Sections.Single(s => s.Kind == SchemaDiffKind.DropForeignKey)
                .Items.Should()
                .ContainSingle()
                .Which.ForeignKeyName.Should()
                .Be("FK_ref");
            AddedForeignKeyPairs(plan, "FK_ref").Should().Equal(expected);
        }
    }

    /// <summary>
    /// 参照する列の集合が新しい主キーと違う付け直しは、証明できないので MySQL でも従来どおり警告して計画に入れ、
    /// 付け直しは live の並びのまま出すことを検証する（取り込んでいない索引で通る構成があるため止めない）。
    /// </summary>
    [Fact(
        DisplayName = "候補キー: 参照する列の集合が新しい主キーと違う付け直しは、MySQL でも警告して計画に入れる"
    )]
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
        AddedForeignKeyPairs(plan, "FK_ref").Should().Equal(("a", "ra"), ("b", "rb"));
    }

    /// <summary>
    /// 図の列ペアの並びが参照先のキーと違う外部キーの作り直しを、並びを問う方言（SQL Server・MySQL）ではキーの並びで
    /// 付け直し、警告しないことを検証する。問わない方言は図の並びのまま。差分は列ペアを並びを見ずに比べるので、作り直しは
    /// 並び以外の変更（ここでは参照アクション）で起きる。
    /// </summary>
    [Fact(
        DisplayName = "並べ替え: 図の並びが参照先のキーと違う外部キーの作り直しは、キーの並びで付け直す"
    )]
    public void AlterForeignKeyInOtherOrder_IsArrangedToKey()
    {
        var (_, _, fk, context) = PairReferenceScenario();
        var targetEntities = context.LiveEntities.Select(e => e.Clone(preserveId: true)).ToList();
        var reversed = new Relationship
        {
            SourceEntityId = fk.SourceEntityId,
            TargetEntityId = fk.TargetEntityId,
            ColumnPairs = [.. fk.ColumnPairs.Select(p => p.Clone()).Reverse()],
            ConstraintName = fk.ConstraintName,
            OnDelete = ForeignKeyReferentialAction.Cascade,
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

        foreach (var caps in new[] { NonTransactionalOrderedCaps, MySqlLikeCaps })
        {
            var plan = Plan(caps);

            plan.Warnings.Should().BeEmpty();
            plan.Sections.Select(s => s.Kind)
                .Should()
                .Equal(SchemaDiffKind.DropForeignKey, SchemaDiffKind.AddForeignKey);
            AddedForeignKeyPairs(plan, "FK_ref").Should().Equal(("a", "ra"), ("b", "rb"));
        }

        var unordered = Plan(
            NonTransactionalOrderedCaps with
            {
                ReferencedKeyRequiresSameColumnOrder = false,
            }
        );
        unordered.Warnings.Should().BeEmpty();
        AddedForeignKeyPairs(unordered, "FK_ref").Should().Equal(("b", "rb"), ("a", "ra"));
    }

    /// <summary>
    /// 一意制約の列の並びだけが違う図は作り直しにならず（差分は一意制約を列集合で比べる）、MySQL でもその一意制約に
    /// 支えられた外部キーを外し直さないことを検証する（索引の喪失による外し直し＝<c>CollectLostIndexes</c> は、
    /// 一意制約の削除を選んだときだけ働く）。
    /// </summary>
    [Fact(
        DisplayName = "並べ替え: 一意制約の列の並びだけの違いは作り直しにならず、MySQL でも外部キーを外し直さない"
    )]
    public void UniqueConstraintInOtherOrder_IsNoDiffAndKeepsForeignKey()
    {
        var (_, orders, rel, context) = LiveFkScenario();
        orders.Columns.Add(Col("note", "INT"));
        WithUnique(orders, "UQ_orders_customer_note", "customer_id", "note");
        var targetEntities = context.LiveEntities.Select(e => e.Clone(preserveId: true)).ToList();
        var targetOrders = targetEntities.Single(e => e.TableName == "orders");
        targetOrders.UniqueConstraints[0].ColumnIds =
        [
            .. targetOrders.UniqueConstraints[0].ColumnIds.AsEnumerable().Reverse(),
        ];

        var items = new SchemaDiffService()
            .Compute(
                context.LiveEntities,
                context.LiveRelationships,
                targetEntities,
                [rel],
                NonTransactionalOrderedCaps
            )
            .Items;
        items.Should().BeEmpty();

        new SyncPlanner()
            .BuildPlan(items, NonTransactionalOrderedCaps, context)
            .IsEmpty.Should()
            .BeTrue();
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
    /// 主キーの変更で付け直す外部キーの構成列の型を変えて両端の型が食い違うなら、外さずに警告すること（付け直しが必ず失敗する）と、
    /// 主キーの解除がその外部キーに阻まれて必ず断られるので、DDL が暗黙コミットされる方言（MySQL・Oracle）では主キーの解除を
    /// 計画の先頭へ移すことを、5 方言の実際の能力の宣言で検証する（4m-P1：兄弟の外部キーを先に外して付け直す計画が、主キーの
    /// 解除の失敗で兄弟を消したまま残していた）。トランザクションの方言は並びを変えない。
    /// </summary>
    /// <param name="dialect">方言名</param>
    /// <param name="sibling">同じ主キーを参照する兄弟の外部キー（型を変えない）を持つか</param>
    [Theory(
        DisplayName = "型の食い違い: 主キーの変更で付け直す外部キーも、型が食い違うなら外さずに警告する（暗黙コミットの方言は主キーの解除を先頭へ）"
    )]
    [InlineData(SqlServerProvider.ProviderName, false)]
    [InlineData(PostgreSqlProvider.ProviderName, false)]
    [InlineData(MySqlProvider.ProviderName, false)]
    [InlineData(OracleProvider.ProviderName, false)]
    [InlineData(SqlServerProvider.ProviderName, true)]
    [InlineData(PostgreSqlProvider.ProviderName, true)]
    [InlineData(MySqlProvider.ProviderName, true)]
    [InlineData(OracleProvider.ProviderName, true)]
    public void AlterPrimaryKey_WithMismatchedForeignKeyColumn_WarnsWithoutRebuild(
        string dialect,
        bool sibling
    )
    {
        var (customer, orders, rel, _) = LiveFkScenario();
        customer.Columns.Add(
            new Column
            {
                Name = "code",
                DataType = "INT",
                IsNullable = false,
            }
        );
        var invoice = new Entity
        {
            TableName = "invoice",
            Columns = { PkId(), Col("customer_id", "INT") },
        };
        var invoiceRel = new Relationship
        {
            SourceEntityId = customer.Id,
            TargetEntityId = invoice.Id,
            ColumnPairs = [new(customer.Columns[0].Id, invoice.Columns[1].Id)],
            ConstraintName = "FK_invoice_customer",
        };
        var context = new SyncPlanContext
        {
            LiveEntities = sibling ? [customer, orders, invoice] : [customer, orders],
            LiveRelationships = sibling ? [rel, invoiceRel] : [rel],
        };

        // 主キーを code へ付け替え、id に一意制約を足して候補キーを保つ（兄弟の外部キーは付け直せる）。orders の子の列だけ型を変える
        var target = context.LiveEntities.Select(e => e.Clone(preserveId: true)).ToList();
        var targetCustomer = target.Single(e => e.TableName == "customer");
        targetCustomer.Columns.Single(c => c.Name == "id").IsPrimaryKey = false;
        targetCustomer.Columns.Single(c => c.Name == "code").IsPrimaryKey = true;
        WithUnique(targetCustomer, "UQ_customer_id", "id");
        target
            .Single(e => e.TableName == "orders")
            .Columns.Single(c => c.Name == "customer_id")
            .DataType = "VARCHAR(10)";

        var plan = PlanAllDiffs(
            dialect,
            context,
            target,
            [.. context.LiveRelationships],
            out var capabilities
        );

        plan.Warnings.Should()
            .ContainSingle(w => w.Kind == SyncPlanWarningKind.ForeignKeyColumnTypeMismatch)
            .Which.Detail.Should()
            .Be("FK_orders_customer");
        plan.Sections.SelectMany(s => s.Items)
            .Should()
            .NotContain(i => i.Kind == SchemaDiffKind.DropForeignKey && i.TableName == "orders");

        var pkDrop = plan
            .Sections.ToList()
            .FindIndex(s =>
                s.Kind == SchemaDiffKind.AlterPrimaryKey
                && s.PrimaryKeyPhase == PrimaryKeyPhase.Drop
            );
        var fkDrop = plan.Sections.ToList().FindIndex(s => s.Kind == SchemaDiffKind.DropForeignKey);

        if (capabilities.TransactionalDdl)
        {
            // 巻き戻る方言は固定順のまま（兄弟の外部キーの解除が主キーの解除より前）
            if (fkDrop >= 0)
            {
                fkDrop.Should().BeLessThan(pkDrop);
            }

            return;
        }

        // 暗黙コミットの方言：食い違う外部キーに阻まれる主キーの解除を、兄弟の外部キーを外すより前（計画の先頭）へ移す
        var first = plan.Sections[0];
        first.Kind.Should().Be(SchemaDiffKind.AlterPrimaryKey);
        first.PrimaryKeyPhase.Should().Be(PrimaryKeyPhase.Drop);
        first.Items.Should().ContainSingle().Which.TableName.Should().Be("customer");
        plan.Sections.Should()
            .Contain(s =>
                s.Kind == SchemaDiffKind.AlterPrimaryKey && s.PrimaryKeyPhase == PrimaryKeyPhase.Add
            );
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
            context.LiveRelationships,
            FkRebuildCaps.IdentifierLengthLimit
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
