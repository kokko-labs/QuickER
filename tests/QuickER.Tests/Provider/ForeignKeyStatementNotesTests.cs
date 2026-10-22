using System.Linq;
using AwesomeAssertions;
using QuickER.Dialects;
using QuickER.Model;
using QuickER.Provider;
using QuickER.Provider.MySql;
using QuickER.Provider.Oracle;
using QuickER.Provider.PostgreSql;
using QuickER.Provider.Sqlite;
using QuickER.Provider.SqlServer;
using Xunit;

namespace QuickER.Tests.Provider;

/// <summary>
/// 適用に失敗し得る外部キーの名指し（DDL の注記と同期の計画の警告）を、5 方言の実際の宣言で検証する。
/// </summary>
/// <remarks>
/// 対象は SQL Server の 2 つ＝NOT NULL の子の列への <c>SET DEFAULT</c>（エラー 1762・DDL の注記と計画の警告）と、
/// 両端の文字列型の長さの違い（エラー 1753・DDL の注記）。どちらも扱いの表のマスで決まり、他の方言では何も出さない。
/// </remarks>
public sealed class ForeignKeyStatementNotesTests
{
    private const string SetDefaultNote = "SET DEFAULT needs a DEFAULT constraint";
    private const string LengthNote = "differ in length";
    private const string SetNullNote = "-- Note: SET NULL on the NOT NULL referencing column(s)";

    private static readonly IDatabaseProvider[] Providers =
    [
        new SqlServerProvider(),
        new PostgreSqlProvider(),
        new MySqlProvider(),
        new OracleProvider(),
        new SqliteProvider(),
    ];

    /// <summary>NOT NULL の子の列への SET DEFAULT は、SQL Server の DDL だけが子の列を名指しして注記する</summary>
    [Theory(DisplayName = "DDL: NOT NULL の子の列への SET DEFAULT は SQL Server だけが注記する")]
    [InlineData(DialectNames.SqlServer, true)]
    [InlineData(DialectNames.PostgreSql, false)]
    [InlineData(DialectNames.MySql, false)]
    [InlineData(DialectNames.Oracle, false)]
    [InlineData(DialectNames.Sqlite, false)]
    public void Ddl_SetDefaultOnNotNullColumn_IsAnnotatedOnlyWhereDeclared(
        string dialect,
        bool annotated
    )
    {
        var (diagram, _) = Diagram("int", "int", childNullable: false, onUpdateSetDefault: true);

        var sql = Provider(dialect).DdlGenerator.Build(diagram);

        if (annotated)
        {
            sql.Should()
                .Contain($"{SetDefaultNote} on the NOT NULL referencing column(s) parent_id;");
        }
        else
        {
            sql.Should().NotContain(SetDefaultNote);
        }
    }

    /// <summary>NULL を許す子の列への SET DEFAULT は SQL Server でも作れる（動きは SET NULL と同じ）ので注記しない</summary>
    [Fact(DisplayName = "DDL: NULL を許す子の列への SET DEFAULT は SQL Server でも注記しない")]
    public void Ddl_SetDefaultOnNullableColumn_IsNotAnnotated()
    {
        var (diagram, _) = Diagram("int", "int", childNullable: true, onUpdateSetDefault: true);

        Provider(DialectNames.SqlServer)
            .DdlGenerator.Build(diagram)
            .Should()
            .NotContain(SetDefaultNote);
    }

    /// <summary>両端の文字列型の長さだけが違う外部キーは、SQL Server の DDL だけが列の組を名指しして注記する</summary>
    [Theory(DisplayName = "DDL: 両端の長さだけが違う外部キーは SQL Server だけが注記する")]
    [InlineData(DialectNames.SqlServer, true)]
    [InlineData(DialectNames.PostgreSql, false)]
    [InlineData(DialectNames.MySql, false)]
    [InlineData(DialectNames.Oracle, false)]
    [InlineData(DialectNames.Sqlite, false)]
    public void Ddl_StringLengthMismatch_IsAnnotatedOnlyWhereDeclared(
        string dialect,
        bool annotated
    )
    {
        var (diagram, _) = Diagram(
            "varchar(20)",
            "varchar(10)",
            childNullable: false,
            onUpdateSetDefault: false
        );

        var sql = Provider(dialect).DdlGenerator.Build(diagram);

        if (annotated)
        {
            sql.Should()
                .Contain(
                    "-- Note: child.parent_id (varchar(10)) and parent.id (varchar(20)) differ in length;"
                );
        }
        else
        {
            sql.Should().NotContain(LengthNote);
        }
    }

    /// <summary>長さが同じ・長さ以外が違う型の外部キーには、長さの注記を付けない</summary>
    [Theory(DisplayName = "DDL: 長さが同じか、長さ以外が違う型の外部キーには長さの注記を付けない")]
    [InlineData("varchar(10)", "VARCHAR(10)")]
    [InlineData("varchar(10)", "nvarchar(20)")]
    [InlineData("int", "bigint")]
    public void Ddl_NotLengthOnlyDifference_IsNotAnnotated(string parentType, string childType)
    {
        var (diagram, _) = Diagram(
            parentType,
            childType,
            childNullable: false,
            onUpdateSetDefault: false
        );

        Provider(DialectNames.SqlServer)
            .DdlGenerator.Build(diagram)
            .Should()
            .NotContain(LengthNote);
    }

    /// <summary>
    /// 選んだ外部キーの追加が NOT NULL の子の列に SET DEFAULT を使うとき、SQL Server の計画だけが制約名と列を名指しして警告する
    /// </summary>
    [Theory(
        DisplayName = "同期: NOT NULL の子の列への SET DEFAULT の追加は SQL Server の計画だけが警告する"
    )]
    [InlineData(DialectNames.SqlServer, true)]
    [InlineData(DialectNames.PostgreSql, false)]
    [InlineData(DialectNames.MySql, false)]
    [InlineData(DialectNames.Oracle, false)]
    [InlineData(DialectNames.Sqlite, false)]
    public void Plan_SetDefaultOnNotNullColumn_WarnsOnlyWhereDeclared(string dialect, bool warns)
    {
        var plan = PlanAddForeignKey(dialect, childNullable: false);

        if (warns)
        {
            plan.Warnings.Should()
                .ContainSingle(w => w.Kind == SyncPlanWarningKind.SetDefaultOnNotNullColumn)
                .Which.Should()
                .Be(
                    new SyncPlanWarning(
                        SyncPlanWarningKind.SetDefaultOnNotNullColumn,
                        "child",
                        "FK_child_parent (parent_id)"
                    )
                );
        }
        else
        {
            plan.Warnings.Should()
                .NotContain(w => w.Kind == SyncPlanWarningKind.SetDefaultOnNotNullColumn);
        }
    }

    /// <summary>NULL を許す子の列への SET DEFAULT の追加は、SQL Server でも警告しない</summary>
    [Fact(
        DisplayName = "同期: NULL を許す子の列への SET DEFAULT の追加は SQL Server でも警告しない"
    )]
    public void Plan_SetDefaultOnNullableColumn_DoesNotWarn()
    {
        PlanAddForeignKey(DialectNames.SqlServer, childNullable: true)
            .Warnings.Should()
            .NotContain(w => w.Kind == SyncPlanWarningKind.SetDefaultOnNotNullColumn);
    }

    /// <summary>
    /// 既存の外部キーの参照アクションを NO ACTION から SET DEFAULT へ変える操作（差分では外部キーの作り直し）でも、
    /// NOT NULL の子の列なら SQL Server の計画だけが警告することを検証する（警告は作り直しを展開した後の追加に掛かる）。
    /// MySQL・Oracle は SET DEFAULT を NO ACTION へ畳んで比べるので、この変更は差分にならない（対象外）
    /// </summary>
    [Theory(
        DisplayName = "同期: 外部キーを NOT NULL の子の列への SET DEFAULT へ作り直す変更も SQL Server の計画だけが警告する"
    )]
    [InlineData(DialectNames.SqlServer, true)]
    [InlineData(DialectNames.PostgreSql, false)]
    [InlineData(DialectNames.Sqlite, false)]
    public void Plan_AlterForeignKeyToSetDefault_OnNotNullColumn_WarnsOnlyWhereDeclared(
        string dialect,
        bool warns
    )
    {
        var (diagram, live) = Diagram(
            "int",
            "int",
            childNullable: false,
            onUpdateSetDefault: false
        );
        var target = new Relationship
        {
            SourceEntityId = live.SourceEntityId,
            TargetEntityId = live.TargetEntityId,
            Type = live.Type,
            ColumnPairs = [.. live.ColumnPairs.Select(p => p.Clone())],
            ConstraintName = live.ConstraintName,
            OnDelete = ForeignKeyReferentialAction.SetDefault,
        };
        var capabilities = Provider(dialect).SyncCapabilities;
        var items = new SchemaDiffService()
            .Compute(diagram.Entities, [live], diagram.Entities, [target], capabilities)
            .Items;

        items.Should().ContainSingle().Which.Kind.Should().Be(SchemaDiffKind.AlterForeignKey);
        items[0].IsSelected = true;

        var plan = new SyncPlanner().BuildPlan(
            items,
            capabilities,
            new SyncPlanContext { LiveEntities = diagram.Entities, LiveRelationships = [live] }
        );
        var warnings = plan
            .Warnings.Where(w => w.Kind == SyncPlanWarningKind.SetDefaultOnNotNullColumn)
            .ToList();

        if (warns)
        {
            warnings
                .Should()
                .Equal(
                    new SyncPlanWarning(
                        SyncPlanWarningKind.SetDefaultOnNotNullColumn,
                        "child",
                        "FK_child_parent (parent_id)"
                    )
                );
        }
        else
        {
            warnings.Should().BeEmpty();
        }
    }

    /// <summary>
    /// NOT NULL の子の列への SET NULL は、5 方言とも DDL が子の列を名指しして注記する。作れない方言（宣言が「表せない」）は
    /// 作れないことを、作れる方言は参照される親の行の削除・更新が失敗することを書く
    /// </summary>
    [Theory(
        DisplayName = "DDL: NOT NULL の子の列への SET NULL は 5 方言とも注記し、作れるかで文を変える"
    )]
    [InlineData(DialectNames.SqlServer, true, false, false)]
    [InlineData(DialectNames.SqlServer, false, true, false)]
    [InlineData(DialectNames.PostgreSql, true, false, true)]
    [InlineData(DialectNames.PostgreSql, false, true, true)]
    [InlineData(DialectNames.MySql, true, false, false)]
    [InlineData(DialectNames.MySql, false, true, false)]
    [InlineData(DialectNames.Oracle, true, false, true)]
    [InlineData(DialectNames.Sqlite, true, false, true)]
    [InlineData(DialectNames.Sqlite, false, true, true)]
    public void Ddl_SetNullOnNotNullColumn_IsAnnotated(
        string dialect,
        bool onDelete,
        bool onUpdate,
        bool creatable
    )
    {
        var diagram = SetNullDiagram(childNullable: false, onDelete, onUpdate);

        var sql = Provider(dialect).DdlGenerator.Build(diagram);

        sql.Should()
            .Contain(
                creatable
                    ? $"{SetNullNote} parent_id; deleting or updating a referenced parent row fails because the column cannot be set to NULL"
                    : $"{SetNullNote} parent_id; this database cannot create this foreign key"
            );
        sql.Split(SetNullNote).Should().HaveCount(2, "the note is written once");
    }

    /// <summary>NULL を許す子の列への SET NULL と、Oracle が持たない ON UPDATE の SET NULL（句を省く）には注記しない</summary>
    [Theory(
        DisplayName = "DDL: NULL を許す子の列への SET NULL と、Oracle の ON UPDATE の SET NULL には注記しない"
    )]
    [InlineData(DialectNames.SqlServer, true, true, false)]
    [InlineData(DialectNames.PostgreSql, true, true, false)]
    [InlineData(DialectNames.MySql, true, true, false)]
    [InlineData(DialectNames.Oracle, true, true, false)]
    [InlineData(DialectNames.Sqlite, true, true, false)]
    [InlineData(DialectNames.Oracle, false, false, true)]
    public void Ddl_SetNullNotApplicable_IsNotAnnotated(
        string dialect,
        bool childNullable,
        bool onDelete,
        bool onUpdate
    )
    {
        var diagram = SetNullDiagram(childNullable, onDelete, onUpdate);

        Provider(dialect).DdlGenerator.Build(diagram).Should().NotContain(SetNullNote);
    }

    /// <summary>親 parent(id) ← 子 child(parent_id) の外部キーを、SET NULL の向きと子の列の NULL 許容を指定して作る</summary>
    internal static ErDiagram SetNullDiagram(bool childNullable, bool onDelete, bool onUpdate)
    {
        var (diagram, relationship) = Diagram(
            "int",
            "int",
            childNullable,
            onUpdateSetDefault: false
        );
        relationship.OnDelete = onDelete
            ? ForeignKeyReferentialAction.SetNull
            : ForeignKeyReferentialAction.NoAction;
        relationship.OnUpdate = onUpdate
            ? ForeignKeyReferentialAction.SetNull
            : ForeignKeyReferentialAction.NoAction;

        return diagram;
    }

    private static IDatabaseProvider Provider(string dialect) =>
        Providers.Single(p => p.Name == dialect);

    /// <summary>親 parent(id) ← 子 child(parent_id) の外部キーを、SET DEFAULT の有無と型を指定して作る</summary>
    private static (ErDiagram Diagram, Relationship Relationship) Diagram(
        string parentType,
        string childType,
        bool childNullable,
        bool onUpdateSetDefault
    )
    {
        var parent = new Entity { TableName = "parent" };
        parent.Columns.Add(
            new Column
            {
                Name = "id",
                DataType = parentType,
                IsPrimaryKey = true,
                IsNullable = false,
            }
        );
        var child = new Entity { TableName = "child" };
        child.Columns.Add(
            new Column
            {
                Name = "id",
                DataType = "int",
                IsPrimaryKey = true,
                IsNullable = false,
            }
        );
        child.Columns.Add(
            new Column
            {
                Name = "parent_id",
                DataType = childType,
                IsNullable = childNullable,
            }
        );
        var relationship = new Relationship
        {
            SourceEntityId = parent.Id,
            TargetEntityId = child.Id,
            Type = RelationshipType.OneToMany,
            ColumnPairs = [new(parent.Columns[0].Id, child.Columns[1].Id)],
            ConstraintName = "FK_child_parent",
            OnUpdate = onUpdateSetDefault
                ? ForeignKeyReferentialAction.SetDefault
                : ForeignKeyReferentialAction.NoAction,
        };

        return (
            new ErDiagram { Entities = [parent, child], Relationships = [relationship] },
            relationship
        );
    }

    private static SyncPlan PlanAddForeignKey(string dialect, bool childNullable)
    {
        var (diagram, relationship) = Diagram(
            "int",
            "int",
            childNullable,
            onUpdateSetDefault: true
        );
        var parent = diagram.Entities[0];
        var child = diagram.Entities[1];

        return new SyncPlanner().BuildPlan(
            [
                new SchemaDiffItem
                {
                    Kind = SchemaDiffKind.AddForeignKey,
                    TableName = "child",
                    ColumnName = "parent_id",
                    ForeignKeyColumnPairs = [new("id", "parent_id")],
                    ForeignKeyName = "FK_child_parent",
                    ParentEntity = parent,
                    ChildEntity = child,
                    Relationship = relationship,
                    IsSelected = true,
                },
            ],
            Provider(dialect).SyncCapabilities,
            new SyncPlanContext
            {
                LiveEntities = [parent.Clone(preserveId: true), child.Clone(preserveId: true)],
            }
        );
    }
}
