using AwesomeAssertions;
using QuickER.Dialects;
using QuickER.Model;
using QuickER.Provider;
using QuickER.Provider.MySql;
using QuickER.Provider.Oracle;
using QuickER.Provider.PostgreSql;
using QuickER.Provider.Sqlite;
using QuickER.Provider.SqlServer;

namespace QuickER.Tests.Provider;

/// <summary>
/// 参照アクション SET NULL の外部キーの子の列が同期後に NOT NULL になるときの計画を、5 方言の実際の宣言で検証する。
/// </summary>
/// <remarks>
/// <para>
/// 作れない方言（<see cref="SyncDialectCapabilities.SetNullRequiresNullableChildColumn"/> が <c>true</c>＝SQL Server・MySQL）は、
/// 選んだ追加・作り直しを計画から外し（作り直しは削除ごと）、DB にある外部キーを外し直さない。DDL が暗黙コミットされる方言
/// （MySQL）では、ほかの外部キーも外し直さない。作れる方言（PostgreSQL・Oracle・SQLite）は計画をそのままにして警告だけを出す。
/// </para>
/// <para>
/// 図は親 parent(id)・other(id) ← 子 child(id, parent_id, other_id)。外部キーは FK_child_parent（子の列 parent_id）と
/// FK_child_other（子の列 other_id・NO ACTION）。live と図をそれぞれ組み、差分を計算して選んだ項目から計画を作る。
/// </para>
/// </remarks>
public sealed class SetNullOnNotNullColumnPlanTests
{
    private static readonly IDatabaseProvider[] Providers =
    [
        new SqlServerProvider(),
        new PostgreSqlProvider(),
        new MySqlProvider(),
        new OracleProvider(),
        new SqliteProvider(),
    ];

    private static readonly SyncPlanWarning ParentWarning = new(
        SyncPlanWarningKind.SetNullOnNotNullColumn,
        "child",
        "FK_child_parent (parent_id)"
    );

    /// <summary>作れない方言（SQL Server・MySQL）か</summary>
    private static bool CannotCreate(string dialect) =>
        dialect is DialectNames.SqlServer or DialectNames.MySql;

    /// <summary>
    /// 選んだ外部キーの追加が NOT NULL の子の列に SET NULL を使うと、作れない方言は計画から外し、作れる方言は残して、
    /// どちらも制約名と列で名指しする（ON DELETE・ON UPDATE のどちらでも）
    /// </summary>
    [Theory(
        DisplayName = "追加: NOT NULL の子の列への SET NULL は、作れない方言で計画から外し、5 方言とも名指しする"
    )]
    [InlineData(DialectNames.SqlServer, true)]
    [InlineData(DialectNames.SqlServer, false)]
    [InlineData(DialectNames.PostgreSql, true)]
    [InlineData(DialectNames.PostgreSql, false)]
    [InlineData(DialectNames.MySql, true)]
    [InlineData(DialectNames.MySql, false)]
    [InlineData(DialectNames.Oracle, true)]
    [InlineData(DialectNames.Sqlite, true)]
    [InlineData(DialectNames.Sqlite, false)]
    public void SelectedAdd_OnNotNullChild_IsExcludedWhereNotCreatable(
        string dialect,
        bool onDelete
    )
    {
        var live = Build(new Shape(ChildNullable: false, HasParentForeignKey: false));
        var target = Build(
            new Shape(
                ChildNullable: false,
                OnDelete: SetNullIf(onDelete),
                OnUpdate: SetNullIf(!onDelete)
            )
        );

        var plan = Plan(dialect, live, target, _ => true);

        plan.Warnings.Should().Equal(ParentWarning);
        HasParentForeignKey(plan).Should().Be(!CannotCreate(dialect));
    }

    /// <summary>
    /// Oracle は ON UPDATE を持たない（句を省く）ので、ON UPDATE だけの SET NULL は NOT NULL の子の列でも名指しせず、
    /// 外部キーをそのまま足す
    /// </summary>
    [Fact(DisplayName = "追加: Oracle の ON UPDATE だけの SET NULL は名指しせずに足す")]
    public void SelectedAdd_OracleOnUpdateOnly_IsNotNamed()
    {
        var live = Build(new Shape(ChildNullable: false, HasParentForeignKey: false));
        var target = Build(
            new Shape(
                ChildNullable: false,
                OnDelete: ForeignKeyReferentialAction.NoAction,
                OnUpdate: ForeignKeyReferentialAction.SetNull
            )
        );

        var plan = Plan(DialectNames.Oracle, live, target, _ => true);

        plan.Warnings.Should().BeEmpty();
        HasParentForeignKey(plan).Should().BeTrue();
    }

    /// <summary>
    /// 既存の外部キーを NOT NULL の子の列への SET NULL へ作り直す変更は、作れない方言で削除ごと外す（削除だけが走って
    /// 外部キーが消えたまま残らない）。作れる方言は作り直したうえで名指しする
    /// </summary>
    [Theory(
        DisplayName = "作り直し: NOT NULL の子の列への SET NULL への作り直しは、作れない方言で削除ごと外す"
    )]
    [InlineData(DialectNames.SqlServer)]
    [InlineData(DialectNames.PostgreSql)]
    [InlineData(DialectNames.MySql)]
    [InlineData(DialectNames.Oracle)]
    [InlineData(DialectNames.Sqlite)]
    public void SelectedRecreate_ToSetNullOnNotNullChild_IsExcludedWithItsDrop(string dialect)
    {
        var live = Build(
            new Shape(ChildNullable: false, OnDelete: ForeignKeyReferentialAction.NoAction)
        );
        var target = Build(new Shape(ChildNullable: false));

        var plan = Plan(dialect, live, target, _ => true);

        plan.Warnings.Should().Equal(ParentWarning);

        if (CannotCreate(dialect))
        {
            plan.Sections.Should().BeEmpty("the recreate is left out with its drop");
        }
        else if (dialect == DialectNames.Sqlite)
        {
            ParentRebuildForeignKey(plan)!
                .OnDelete.Should()
                .Be(ForeignKeyReferentialAction.SetNull);
        }
        else
        {
            plan.Sections.Select(s => s.Kind)
                .Should()
                .Equal(SchemaDiffKind.DropForeignKey, SchemaDiffKind.AddForeignKey);
        }
    }

    /// <summary>
    /// 子の列の同期後の NULL 許容は、選んだ列の変更の値、無ければ live の値、live に無い列は図の値で見る。主キーの変更で
    /// 暗黙に NOT NULL になる列も NOT NULL とみなす（作れない方言で確かめる）
    /// </summary>
    [Theory(DisplayName = "同期後の NULL 許容: 列の変更・live・図・主キーの変更の順で決める")]
    [InlineData(DialectNames.SqlServer, "AlterToNotNull", true)]
    [InlineData(DialectNames.MySql, "AlterToNotNull", true)]
    [InlineData(DialectNames.SqlServer, "AlterToNullable", false)]
    [InlineData(DialectNames.MySql, "AlterToNullable", false)]
    [InlineData(DialectNames.SqlServer, "UnselectedAlterToNotNull", false)]
    [InlineData(DialectNames.MySql, "UnselectedAlterToNotNull", false)]
    [InlineData(DialectNames.SqlServer, "NewNotNullColumn", true)]
    [InlineData(DialectNames.MySql, "NewNotNullColumn", true)]
    [InlineData(DialectNames.SqlServer, "PrimaryKeyChange", true)]
    [InlineData(DialectNames.MySql, "PrimaryKeyChange", true)]
    public void SelectedAdd_PostSyncNullability_DecidesExclusion(
        string dialect,
        string form,
        bool excluded
    )
    {
        var (liveShape, targetShape, select) = form switch
        {
            "AlterToNotNull" => (
                new Shape(ChildNullable: true, HasParentForeignKey: false),
                new Shape(ChildNullable: false),
                (Func<SchemaDiffItem, bool>)(_ => true)
            ),
            "AlterToNullable" => (
                new Shape(ChildNullable: false, HasParentForeignKey: false),
                new Shape(ChildNullable: true),
                _ => true
            ),
            "UnselectedAlterToNotNull" => (
                new Shape(ChildNullable: true, HasParentForeignKey: false),
                new Shape(ChildNullable: false),
                i => i.Kind != SchemaDiffKind.AlterColumn
            ),
            "NewNotNullColumn" => (
                new Shape(HasParentColumn: false, HasParentForeignKey: false),
                new Shape(ChildNullable: false),
                _ => true
            ),
            "PrimaryKeyChange" => (
                new Shape(ChildNullable: true, HasParentForeignKey: false),
                new Shape(ParentIdInChildKey: true),
                i => i.Kind != SchemaDiffKind.AlterColumn
            ),
            _ => throw new ArgumentOutOfRangeException(nameof(form)),
        };

        var plan = Plan(dialect, Build(liveShape), Build(targetShape), select);

        HasParentForeignKey(plan).Should().Be(!excluded);

        if (excluded)
        {
            plan.Warnings.Should().Contain(ParentWarning);
        }
        else
        {
            plan.Warnings.Should()
                .NotContain(w => w.Kind == SyncPlanWarningKind.SetNullOnNotNullColumn);
        }
    }

    /// <summary>
    /// DB にある SET NULL の外部キーの子の列を、選んだ列の変更が NOT NULL にするときは 5 方言とも名指しし、作れない方言では
    /// その外部キーを外して付け直さない（付け直しが必ず失敗する）
    /// </summary>
    [Theory(
        DisplayName = "暗黙の付け直し: SET NULL の子の列を NOT NULL にする列の変更は名指しし、その外部キーを外し直さない"
    )]
    [InlineData(DialectNames.SqlServer)]
    [InlineData(DialectNames.PostgreSql)]
    [InlineData(DialectNames.MySql)]
    [InlineData(DialectNames.Oracle)]
    [InlineData(DialectNames.Sqlite)]
    public void AlterChildToNotNull_LiveSetNullForeignKey_IsNamedAndNotRebuilt(string dialect)
    {
        var live = Build(new Shape(ChildNullable: true));
        var target = Build(new Shape(ChildNullable: false));

        var plan = Plan(dialect, live, target, _ => true);

        plan.Warnings.Should().Equal(ParentWarning);

        if (dialect == DialectNames.Sqlite)
        {
            plan.Rebuilds.Should().ContainSingle();
            ParentRebuildForeignKey(plan)!
                .OnDelete.Should()
                .Be(ForeignKeyReferentialAction.SetNull);
        }
        else
        {
            plan.Sections.Select(s => s.Kind).Should().Equal(SchemaDiffKind.AlterColumn);
        }
    }

    /// <summary>
    /// 作れない方言のうち、DDL がトランザクションで巻き戻る方言（SQL Server）はその外部キーだけを外し直さず、ほかの外部キーは
    /// 列の変更に合わせて外し直す。DDL が暗黙コミットされる方言（MySQL）は、列の変更が断られたときに外した外部キーが消えたまま
    /// 残らないよう、どの外部キーも外し直さない
    /// </summary>
    [Theory(
        DisplayName = "暗黙の付け直し: SQL Server はその外部キーだけ、MySQL はどの外部キーも外し直さない"
    )]
    [InlineData(DialectNames.SqlServer, true)]
    [InlineData(DialectNames.MySql, false)]
    public void AlterChildToNotNull_WithOtherRebuild_SkipsOnlyThatForeignKeyWhereTransactional(
        string dialect,
        bool otherRebuilt
    )
    {
        var live = Build(new Shape(ChildNullable: true));
        var target = Build(new Shape(ChildNullable: false, OtherKeyType: "bigint"));

        var plan = Plan(dialect, live, target, _ => true);

        plan.Warnings.Where(w => w.Kind == SyncPlanWarningKind.SetNullOnNotNullColumn)
            .Should()
            .Equal(ParentWarning);
        string[] expected = otherRebuilt ? ["FK_child_other"] : [];
        ForeignKeyNames(plan, SchemaDiffKind.DropForeignKey).Should().Equal(expected);
        ForeignKeyNames(plan, SchemaDiffKind.AddForeignKey).Should().Equal(expected);
    }

    /// <summary>
    /// 主キーの変更で子の列が暗黙に NOT NULL になる形（主キーの変更だけを選ぶ）でも、DB にある SET NULL の外部キーを名指しし、
    /// 作れない方言では外し直さない
    /// </summary>
    [Theory(
        DisplayName = "暗黙の付け直し: 主キーの変更で子の列が NOT NULL になる形も名指しし、外し直さない"
    )]
    [InlineData(DialectNames.SqlServer)]
    [InlineData(DialectNames.PostgreSql)]
    [InlineData(DialectNames.MySql)]
    [InlineData(DialectNames.Oracle)]
    [InlineData(DialectNames.Sqlite)]
    public void PrimaryKeyChange_MakingSetNullChildNotNull_IsNamedAndNotRebuilt(string dialect)
    {
        var live = Build(new Shape(ChildNullable: true));
        var target = Build(new Shape(ParentIdInChildKey: true));

        var plan = Plan(dialect, live, target, i => i.Kind == SchemaDiffKind.AlterPrimaryKey);

        plan.Warnings.Where(w => w.Kind == SyncPlanWarningKind.SetNullOnNotNullColumn)
            .Should()
            .Equal(ParentWarning);

        if (CannotCreate(dialect))
        {
            ForeignKeyNames(plan, SchemaDiffKind.AddForeignKey).Should().BeEmpty();
            ForeignKeyNames(plan, SchemaDiffKind.DropForeignKey).Should().BeEmpty();
        }
    }

    /// <summary>SET NULL でない外部キー・NULL を許したままの子の列は名指ししない</summary>
    [Theory(
        DisplayName = "対象外: SET NULL でない外部キーと、NULL を許したままの子の列は名指ししない"
    )]
    [InlineData(DialectNames.SqlServer)]
    [InlineData(DialectNames.MySql)]
    [InlineData(DialectNames.PostgreSql)]
    public void NotApplicable_IsNotNamed(string dialect)
    {
        var cascade = Plan(
            dialect,
            Build(new Shape(ChildNullable: true, OnDelete: ForeignKeyReferentialAction.Cascade)),
            Build(new Shape(ChildNullable: false, OnDelete: ForeignKeyReferentialAction.Cascade)),
            _ => true
        );
        var nullable = Plan(
            dialect,
            Build(new Shape(ChildNullable: true, HasParentForeignKey: false)),
            Build(new Shape(ChildNullable: true)),
            _ => true
        );

        cascade
            .Warnings.Should()
            .NotContain(w => w.Kind == SyncPlanWarningKind.SetNullOnNotNullColumn);
        nullable
            .Warnings.Should()
            .NotContain(w => w.Kind == SyncPlanWarningKind.SetNullOnNotNullColumn);
        HasParentForeignKey(nullable).Should().BeTrue();
    }

    /// <summary>図の形</summary>
    /// <param name="ChildNullable">子の列 parent_id が NULL を許すか</param>
    /// <param name="OnDelete">FK_child_parent の ON DELETE</param>
    /// <param name="OnUpdate">FK_child_parent の ON UPDATE</param>
    /// <param name="HasParentForeignKey">FK_child_parent を持つか</param>
    /// <param name="HasParentColumn">子の列 parent_id を持つか</param>
    /// <param name="ParentIdInChildKey">parent_id を子の主キーへ加えるか（主キーの列は NOT NULL）</param>
    /// <param name="OtherKeyType">other.id と child.other_id の型</param>
    private sealed record Shape(
        bool ChildNullable = true,
        ForeignKeyReferentialAction OnDelete = ForeignKeyReferentialAction.SetNull,
        ForeignKeyReferentialAction OnUpdate = ForeignKeyReferentialAction.NoAction,
        bool HasParentForeignKey = true,
        bool HasParentColumn = true,
        bool ParentIdInChildKey = false,
        string OtherKeyType = "int"
    );

    private static ForeignKeyReferentialAction SetNullIf(bool condition) =>
        condition ? ForeignKeyReferentialAction.SetNull : ForeignKeyReferentialAction.NoAction;

    private static (List<Entity> Entities, List<Relationship> Relationships) Build(Shape shape)
    {
        var parent = new Entity { TableName = "parent" };
        parent.Columns.Add(Col("id", "int", isPrimaryKey: true, isNullable: false));
        var other = new Entity { TableName = "other" };
        other.Columns.Add(Col("id", shape.OtherKeyType, isPrimaryKey: true, isNullable: false));
        var child = new Entity { TableName = "child" };
        child.Columns.Add(Col("id", "int", isPrimaryKey: true, isNullable: false));

        if (shape.HasParentColumn)
        {
            child.Columns.Add(
                Col(
                    "parent_id",
                    "int",
                    isPrimaryKey: shape.ParentIdInChildKey,
                    isNullable: shape.ChildNullable && !shape.ParentIdInChildKey
                )
            );
        }

        var otherId = Col("other_id", shape.OtherKeyType, isPrimaryKey: false, isNullable: true);
        child.Columns.Add(otherId);

        var relationships = new List<Relationship>
        {
            new()
            {
                SourceEntityId = other.Id,
                TargetEntityId = child.Id,
                Type = RelationshipType.OneToMany,
                ColumnPairs = [new(other.Columns[0].Id, otherId.Id)],
                ConstraintName = "FK_child_other",
            },
        };

        if (shape.HasParentForeignKey && shape.HasParentColumn)
        {
            relationships.Add(
                new Relationship
                {
                    SourceEntityId = parent.Id,
                    TargetEntityId = child.Id,
                    Type = RelationshipType.OneToMany,
                    ColumnPairs =
                    [
                        new(
                            parent.Columns[0].Id,
                            child.Columns.Single(c => c.Name == "parent_id").Id
                        ),
                    ],
                    ConstraintName = "FK_child_parent",
                    OnDelete = shape.OnDelete,
                    OnUpdate = shape.OnUpdate,
                }
            );
        }

        return ([parent, other, child], relationships);
    }

    private static Column Col(string name, string type, bool isPrimaryKey, bool isNullable) =>
        new()
        {
            Name = name,
            DataType = type,
            IsPrimaryKey = isPrimaryKey,
            IsNullable = isNullable,
        };

    /// <summary>live から図への差分を計算し、選んだ項目から計画を作る</summary>
    private static SyncPlan Plan(
        string dialect,
        (List<Entity> Entities, List<Relationship> Relationships) live,
        (List<Entity> Entities, List<Relationship> Relationships) target,
        Func<SchemaDiffItem, bool> select
    )
    {
        var capabilities = Providers.Single(p => p.Name == dialect).SyncCapabilities;
        var diff = new SchemaDiffService().Compute(
            live.Entities,
            live.Relationships,
            target.Entities,
            target.Relationships,
            capabilities
        );

        foreach (var item in diff.Items)
        {
            item.IsSelected = item.IsSelectable && select(item);
        }

        return new SyncPlanner().BuildPlan(
            diff.Items,
            capabilities,
            new SyncPlanContext
            {
                LiveEntities = live.Entities,
                LiveRelationships = live.Relationships,
            }
        );
    }

    /// <summary>計画の外部キーのセクションに並ぶ制約名</summary>
    private static List<string> ForeignKeyNames(SyncPlan plan, SchemaDiffKind kind) =>
        [
            .. plan
                .Sections.Where(s => s.Kind == kind)
                .SelectMany(s => s.Items)
                .Select(i => i.ForeignKeyName ?? i.Relationship?.ConstraintName ?? string.Empty),
        ];

    /// <summary>SQLite の再構築で子の表に張る FK_child_parent（無ければ null）</summary>
    private static TableRebuildForeignKey? ParentRebuildForeignKey(SyncPlan plan) =>
        plan
            .Rebuilds.Where(r => r.TableName == "child")
            .SelectMany(r => r.ForeignKeys)
            .SingleOrDefault(f => f.ParentTable == "parent");

    /// <summary>FK_child_parent を追加する（逐次 DDL のセクションか、SQLite の再構築の外部キー）か</summary>
    private static bool HasParentForeignKey(SyncPlan plan) =>
        ForeignKeyNames(plan, SchemaDiffKind.AddForeignKey).Contains("FK_child_parent")
        || ParentRebuildForeignKey(plan) is not null;
}
