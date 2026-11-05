using QuickER.Dialects;
using QuickER.Model;
using QuickER.Provider;
using static QuickER.Tests.Provider.DdlModel;
using E = QuickER.Tests.Provider.DialectDdlElements;
using F = QuickER.Tests.Provider.DialectDdlFacets;
using Ft = QuickER.Dialects.UnsupportedFeature;
using K = QuickER.Dialects.UnsupportedDefinitionKind;
using O = QuickER.Tests.Provider.DialectDdlOutputs;
using P = QuickER.Tests.Provider.DialectDdlPaths;
using Tm = QuickER.Dialects.UnsupportedTreatment;

namespace QuickER.Tests.Provider;

/// <summary>
/// 外部キーの注記（SET DEFAULT・SET NULL・長さの違い）と複合外部キーの並べ替え
/// </summary>
internal static partial class DialectDdlRows
{
    /// <summary>SET NULL の注記の先頭（製品の文面を英語で書き下ろす）</summary>
    private const string TrSetNullNote =
        "-- Note: SET NULL on the NOT NULL referencing column(s) parent_id;";

    /// <summary>作れる方言の SET NULL の注記の続き（参照される親の行の削除・更新が失敗する）</summary>
    private const string TrSetNullCreatable =
        " deleting or updating a referenced parent row fails because the column cannot be set to NULL*";

    /// <summary>作れない方言の SET NULL の注記の続き</summary>
    private const string TrSetNullUncreatable = " this database cannot create this foreign key*";

    /// <summary>
    /// 親 parent(id) ← 子 child(id, parent_id) の外部キー FK_child_parent（子の主キーの型は常に int）
    /// </summary>
    private static (ErDiagram Diagram, Relationship Relationship) TrFkDiagram(
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

    /// <summary>SET NULL の向きと子の列の NULL 許容を指定した図</summary>
    private static ErDiagram TrSetNullDiagram(bool childNullable, bool onDelete, bool onUpdate)
    {
        var (diagram, relationship) = TrFkDiagram("int", "int", childNullable, false);
        relationship.OnDelete = onDelete
            ? ForeignKeyReferentialAction.SetNull
            : ForeignKeyReferentialAction.NoAction;
        relationship.OnUpdate = onUpdate
            ? ForeignKeyReferentialAction.SetNull
            : ForeignKeyReferentialAction.NoAction;

        return diagram;
    }

    /// <summary>主キー (a, b) の親と、宣言を (b → y, a → x) にした複合外部キーの子</summary>
    private static (Entity Parent, Entity Child, Relationship Relationship) TrCompositeScenario()
    {
        var parent = new Entity { TableName = "parent" };
        parent.Columns.Add(
            new Column
            {
                Name = "a",
                DataType = "int",
                IsPrimaryKey = true,
                IsNullable = false,
            }
        );
        parent.Columns.Add(
            new Column
            {
                Name = "b",
                DataType = "int",
                IsPrimaryKey = true,
                IsNullable = false,
            }
        );
        var child = new Entity { TableName = "child" };
        child.Columns.Add(
            new Column
            {
                Name = "x",
                DataType = "int",
                IsNullable = false,
            }
        );
        child.Columns.Add(
            new Column
            {
                Name = "y",
                DataType = "int",
                IsNullable = false,
            }
        );
        var relationship = new Relationship
        {
            SourceEntityId = parent.Id,
            TargetEntityId = child.Id,
            ColumnPairs =
            [
                new(parent.Columns[1].Id, child.Columns[1].Id),
                new(parent.Columns[0].Id, child.Columns[0].Id),
            ],
            ConstraintName = "FK_child_parent",
        };

        return (parent, child, relationship);
    }

    /// <summary>SET NULL の注記の期待（作れる方言と作れない方言で文を変え、注記は 1 回だけ）</summary>
    private static DialectDdlRow TrSetNullExpect(
        this DialectDdlRow row,
        string[] creatable,
        string[] uncreatable
    )
    {
        const string AnyNote = "*-- Note: SET NULL on the NOT NULL referencing column(s)*";

        return row.Expect(
                creatable,
                e =>
                    e.Lines("note set-null-not-null | " + TrSetNullNote + TrSetNullCreatable)
                        .Count(O.Text, 1, AnyNote)
            )
            .Expect(
                uncreatable,
                e =>
                    e.Lines("note set-null-not-null | " + TrSetNullNote + TrSetNullUncreatable)
                        .Count(O.Text, 1, AnyNote)
            );
    }

    private static IEnumerable<DialectDdlRow> ForeignKeyNoteRows() =>
        [
            // ---- SET DEFAULT（SQL Server の NOT NULL の子の列）----
            DialectDdlRow
                .Ddl(
                    "ddl-fk-set-default-on-not-null-child-note",
                    "DDL: NOT NULL の子の列への SET DEFAULT は SQL Server だけが注記する",
                    E.ForeignKey,
                    F.Note,
                    _ =>
                        TrFkDiagram(
                            "int",
                            "int",
                            childNullable: false,
                            onUpdateSetDefault: true
                        ).Diagram
                )
                .Expect(
                    Ss,
                    e =>
                        e.Lines(
                            "note set-default-not-null | -- Note: SET DEFAULT needs a DEFAULT constraint on the NOT NULL referencing column(s) parent_id;*"
                        )
                )
                .Expect([Pg, My, Or, Sl], e => e.NoText("*SET DEFAULT needs a DEFAULT constraint*"))
                .TreatsGroups(K.SetDefaultReferentialAction, Ft.Ddl, (Tm.Annotation, [Ss])),
            DialectDdlRow
                .Ddl(
                    "ddl-fk-set-default-on-nullable-child-no-note",
                    "DDL: NULL を許す子の列への SET DEFAULT は SQL Server でも注記しない",
                    E.ForeignKey,
                    F.Note,
                    _ =>
                        TrFkDiagram(
                            "int",
                            "int",
                            childNullable: true,
                            onUpdateSetDefault: true
                        ).Diagram
                )
                .Expect(Ss, e => e.NoText("*SET DEFAULT needs a DEFAULT constraint*"))
                .TreatsGroups(K.SetDefaultReferentialAction, Ft.Ddl, (Tm.Annotation, [Ss])),
            // ---- 長さだけが違う型の外部キー（SQL Server）----
            DialectDdlRow
                .Ddl(
                    "ddl-fk-string-length-mismatch-note",
                    "DDL: 両端の長さだけが違う外部キーは SQL Server だけが注記する",
                    E.ForeignKey,
                    F.Note,
                    _ =>
                        TrFkDiagram(
                            "varchar(20)",
                            "varchar(10)",
                            childNullable: false,
                            onUpdateSetDefault: false
                        ).Diagram
                )
                .Expect(
                    Ss,
                    e =>
                        e.Lines(
                            "note string-length-mismatch | -- Note: child.parent_id (varchar(10)) and parent.id (varchar(20)) differ in length;*"
                        )
                )
                .Expect([Pg, My, Or, Sl], e => e.NoText("*differ in length*"))
                .TreatsGroups(K.ForeignKeyTypeMismatch, Ft.Ddl, (Tm.Annotation, [Ss])),
            DialectDdlRow
                .Ddl(
                    "ddl-fk-type-mismatch-case-only-no-note",
                    "DDL: 大文字小文字だけが違う型の外部キーには長さの注記を付けない",
                    E.ForeignKey,
                    F.Note,
                    _ =>
                        TrFkDiagram(
                            "varchar(10)",
                            "VARCHAR(10)",
                            childNullable: false,
                            onUpdateSetDefault: false
                        ).Diagram
                )
                .Expect(Ss, e => e.NoText("*differ in length*"))
                .TreatsGroups(K.ForeignKeyTypeMismatch, Ft.Ddl, (Tm.Annotation, [Ss])),
            DialectDdlRow
                .Ddl(
                    "ddl-fk-type-mismatch-varchar-nvarchar-no-note",
                    "DDL: 長さ以外（文字列の種類）が違う型の外部キーには長さの注記を付けない",
                    E.ForeignKey,
                    F.Note,
                    _ =>
                        TrFkDiagram(
                            "varchar(10)",
                            "nvarchar(20)",
                            childNullable: false,
                            onUpdateSetDefault: false
                        ).Diagram
                )
                .Expect(Ss, e => e.NoText("*differ in length*"))
                .TreatsGroups(K.ForeignKeyTypeMismatch, Ft.Ddl, (Tm.Annotation, [Ss])),
            DialectDdlRow
                .Ddl(
                    "ddl-fk-type-mismatch-int-bigint-no-note",
                    "DDL: 長さの無い型の違い（int と bigint）の外部キーには長さの注記を付けない",
                    E.ForeignKey,
                    F.Note,
                    _ =>
                        TrFkDiagram(
                            "int",
                            "bigint",
                            childNullable: false,
                            onUpdateSetDefault: false
                        ).Diagram
                )
                .Expect(Ss, e => e.NoText("*differ in length*"))
                .TreatsGroups(K.ForeignKeyTypeMismatch, Ft.Ddl, (Tm.Annotation, [Ss])),
            // ---- SET NULL × NOT NULL の子の列（5 方言とも注記し、作れるかで文を変える）----
            DialectDdlRow
                .Ddl(
                    "ddl-fk-set-null-on-delete-not-null-child-note",
                    "DDL: NOT NULL の子の列への ON DELETE SET NULL は 5 方言とも注記し、作れるかで文を変える",
                    E.ForeignKey,
                    F.Note,
                    _ => TrSetNullDiagram(childNullable: false, onDelete: true, onUpdate: false)
                )
                .TrSetNullExpect(creatable: [Pg, Or, Sl], uncreatable: [Ss, My])
                .TreatsAll(K.SetNullOnNotNullColumn, Ft.Ddl, Tm.Annotation),
            DialectDdlRow
                .Ddl(
                    "ddl-fk-set-null-on-update-not-null-child-note",
                    "DDL: NOT NULL の子の列への ON UPDATE SET NULL は注記し、作れるかで文を変える（Oracle は ON UPDATE を持たない）",
                    E.ForeignKey,
                    F.Note,
                    _ => TrSetNullDiagram(childNullable: false, onDelete: false, onUpdate: true)
                )
                .TrSetNullExpect(creatable: [Pg, Sl], uncreatable: [Ss, My])
                // Oracle が持たない ON UPDATE の SET NULL（句を省く）には注記しない
                .Expect(
                    Or,
                    e => e.NoText("*-- Note: SET NULL on the NOT NULL referencing column(s)*")
                ),
            DialectDdlRow
                .Ddl(
                    "ddl-fk-set-null-on-nullable-child-no-note",
                    "DDL: NULL を許す子の列への SET NULL には注記しない",
                    E.ForeignKey,
                    F.Note,
                    _ => TrSetNullDiagram(childNullable: true, onDelete: true, onUpdate: false)
                )
                .ExpectAll(e =>
                    e.NoText("*-- Note: SET NULL on the NOT NULL referencing column(s)*")
                ),
            // ---- 複合外部キーの並べ替え ----
            DialectDdlRow
                .Ddl(
                    "ddl-fk-composite-reordered-to-referenced-key",
                    "DDL: 複合外部キーは SQL Server・MySQL だけが参照先のキーの並びへ並べ替える",
                    E.ForeignKey,
                    F.Order,
                    _ =>
                    {
                        var (parent, child, relationship) = TrCompositeScenario();
                        return new ErDiagram
                        {
                            Entities = [parent, child],
                            Relationships = [relationship],
                        };
                    }
                )
                .Expect([Ss, My], e => e.Lines("fk child * (x, y) -> parent (a, b)*"))
                .Expect([Pg, Or, Sl], e => e.Lines("fk child * (y, x) -> parent (b, a)*"))
                .TreatsGroups(
                    K.CompositeForeignKeyColumnOrder,
                    Ft.Ddl,
                    (Tm.Reorder, [Ss, My]),
                    (Tm.None, [Pg, Or, Sl])
                ),
            DialectDdlRow
                .SyncItems(
                    "sync-other-add-composite-fk-reordered-to-referenced-key",
                    "同期: 複合外部キーの追加は SQL Server・MySQL だけが参照先のキーの並びへ並べ替える（計画の警告なし）",
                    P.SyncOther,
                    E.ForeignKey,
                    F.Order,
                    _ =>
                    {
                        var (parent, child, _) = TrCompositeScenario();

                        return new DdlSyncItems(
                            [
                                new SchemaDiffItem
                                {
                                    Kind = SchemaDiffKind.AddForeignKey,
                                    TableName = "child",
                                    ParentEntity = parent,
                                    ChildEntity = child,
                                    Relationship = new Relationship
                                    {
                                        ConstraintName = "FK_child_parent",
                                    },
                                    ForeignKeyName = "FK_child_parent",
                                    ForeignKeyColumnPairs = [new("b", "y"), new("a", "x")],
                                    IsSelected = true,
                                },
                            ],
                            null,
                            new SyncPlanContext
                            {
                                LiveEntities = [parent, child],
                                LiveRelationships = [],
                            }
                        );
                    }
                )
                .Expect(
                    [Ss, My],
                    e => e.Exact(O.Warnings).Lines("fk child * (x, y) -> parent (a, b)*")
                )
                .Expect(
                    [Pg, Or],
                    e => e.Exact(O.Warnings).Lines("fk child * (y, x) -> parent (b, a)*")
                )
                .TreatsGroups(
                    K.CompositeForeignKeyColumnOrder,
                    Ft.Sync,
                    (Tm.Reorder, [Ss, My]),
                    (Tm.None, [Pg, Or])
                ),
            // ---- Integration（DB を使わない DDL の文字列）----
            DialectDdlRow
                .Ddl(
                    "ddl-fk-set-null-on-not-null-child-dialect-key-types",
                    "DDL: NOT NULL の子の列への SET NULL を作れる方言は、方言ごとの主キーの型の表でも外部キーの前に注記する",
                    E.ForeignKey,
                    F.Note,
                    d =>
                    {
                        var keyType = IntType[d];
                        var parent = new Entity { TableName = "sn_parent" };
                        parent.Columns.Add(Pk("id", keyType));
                        var child = new Entity { TableName = "sn_child" };
                        child.Columns.Add(Pk("id", keyType));
                        child.Columns.Add(Col("parent_id", keyType, nullable: false));

                        return new ErDiagram
                        {
                            Entities = [parent, child],
                            Relationships =
                            [
                                new Relationship
                                {
                                    SourceEntityId = parent.Id,
                                    TargetEntityId = child.Id,
                                    Type = RelationshipType.OneToMany,
                                    ColumnPairs = [new(parent.Columns[0].Id, child.Columns[1].Id)],
                                    ConstraintName = "sn_fk",
                                    OnDelete = ForeignKeyReferentialAction.SetNull,
                                },
                            ],
                        };
                    }
                )
                .Expect(
                    [Pg, Or, Sl],
                    e => e.Lines("note set-null-not-null | " + TrSetNullNote + "*")
                )
                .TreatsGroups(K.SetNullOnNotNullColumn, Ft.Ddl, (Tm.Annotation, [Pg, Or, Sl])),
            DialectDdlRow
                .Ddl(
                    "ddl-fk-set-default-omitted-mysql-ra-tables",
                    "DDL: MySQL は SET DEFAULT の句を出さず、注意コメントを付ける",
                    E.ForeignKey,
                    F.Omit,
                    _ =>
                    {
                        var parent = new Entity { TableName = "ra_parent" };
                        parent.Columns.Add(
                            new Column
                            {
                                Name = "id",
                                DataType = "int",
                                IsPrimaryKey = true,
                            }
                        );
                        var child = new Entity { TableName = "ra_child" };
                        child.Columns.Add(
                            new Column
                            {
                                Name = "id",
                                DataType = "int",
                                IsPrimaryKey = true,
                            }
                        );
                        child.Columns.Add(
                            new Column
                            {
                                Name = "parent_id",
                                DataType = "int",
                                IsNullable = true,
                            }
                        );

                        return new ErDiagram
                        {
                            Entities = [parent, child],
                            Relationships =
                            [
                                new Relationship
                                {
                                    SourceEntityId = parent.Id,
                                    TargetEntityId = child.Id,
                                    Type = RelationshipType.OneToMany,
                                    ColumnPairs = [new(parent.Columns[0].Id, child.Columns[1].Id)],
                                    ConstraintName = "fk_ra",
                                    OnDelete = ForeignKeyReferentialAction.SetDefault,
                                    OnUpdate = ForeignKeyReferentialAction.SetDefault,
                                },
                            ],
                        };
                    }
                )
                .Expect(My, e => e.Text("*" + MySqlSetDefaultNote + "*").NoText("*SET DEFAULT *"))
                .TreatsGroups(K.SetDefaultReferentialAction, Ft.Ddl, (Tm.OmitWithAnnotation, [My])),
        ];
}
