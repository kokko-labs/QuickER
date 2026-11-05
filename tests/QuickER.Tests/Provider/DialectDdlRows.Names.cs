using QuickER.Model;
using QuickER.Provider;
using QuickER.Tests.TestSupport;
using E = QuickER.Tests.Provider.DialectDdlElements;
using F = QuickER.Tests.Provider.DialectDdlFacets;
using P = QuickER.Tests.Provider.DialectDdlPaths;

namespace QuickER.Tests.Provider;

/// <summary>
/// 制約名の一致（DDL と同期）・合成した制約名のエスケープ・コメントの畳み込み・外部キーを作る関係の判定
/// </summary>
internal static partial class DialectDdlRows
{
    /// <summary>前後に空白のある子テーブルの名前（<see cref="Entity.TableName"/> は代入時に空白を除く）</summary>
    private const string CcChildTable = " Orders";

    /// <summary>親テーブルの名前</summary>
    private const string CcParentTable = "Customers";

    /// <summary>コメント行を突き破ろうとする説明</summary>
    private const string CcInjected = "a\nDROP TABLE users; --";

    /// <summary>名前を持たない外部キー 1 本でつながる親子の図</summary>
    private static (
        ErDiagram Diagram,
        Entity Parent,
        Entity Child,
        Relationship Relationship
    ) CcFamily()
    {
        var parent = DdlModel.Table(CcParentTable, DdlModel.Pk("id", "int"));
        var child = DdlModel.Table(
            CcChildTable,
            DdlModel.Pk("id", "int"),
            DdlModel.Col("customer_id", "int", nullable: false)
        );
        var relationship = new Relationship
        {
            SourceEntityId = parent.Id,
            TargetEntityId = child.Id,
            Type = RelationshipType.OneToMany,
            ColumnPairs = [new(parent.Columns[0].Id, child.Columns[1].Id)],
        };

        return (
            new ErDiagram { Entities = { parent, child }, Relationships = { relationship } },
            parent,
            child,
            relationship
        );
    }

    /// <summary>単一主キーを持つ最小のテーブル</summary>
    private static Entity CcPkTable(string tableName) =>
        DdlModel.Table(tableName, DdlModel.Pk("id", "integer"));

    /// <summary>エスケープの行の表名（<c>t</c> + 方言の終端クォート文字）</summary>
    private static IReadOnlyDictionary<string, string> CcEscapedTableName =>
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [Ss] = "t]",
            [Pg] = "t\"",
            [My] = "t`",
            [Or] = "t\"",
            [Sl] = "t\"",
        };

    /// <summary>外部キーを作る関係かの判定の形</summary>
    private static ErDiagram CcShape(string shape)
    {
        var parent = new Entity
        {
            TableName = "customers",
            Columns =
            {
                new Column { Name = "tenant_id", IsPrimaryKey = true },
                new Column { Name = "id", IsPrimaryKey = true },
            },
        };
        var child = new Entity
        {
            TableName = "orders",
            Columns =
            {
                new Column { Name = "id", IsPrimaryKey = true },
                new Column { Name = "tenant_id" },
                new Column { Name = "customer_id" },
            },
        };
        var single = new RelationshipColumnPair(parent.Columns[1].Id, child.Columns[2].Id);
        var tenant = new RelationshipColumnPair(parent.Columns[0].Id, child.Columns[1].Id);
        var relationship = new Relationship
        {
            SourceEntityId = parent.Id,
            TargetEntityId = child.Id,
        };

        switch (shape)
        {
            case "OneToMany":
                relationship.ColumnPairs = [single];
                break;
            case "OneToOne":
                relationship.Type = RelationshipType.OneToOne;
                relationship.ColumnPairs = [single];
                break;
            case "Composite":
                relationship.ColumnPairs = [tenant, single];
                break;
            case "ManyToMany":
                relationship.Type = RelationshipType.ManyToMany;
                break;
            case "ManyToManyWithPairs":
                relationship.Type = RelationshipType.ManyToMany;
                relationship.ColumnPairs = [single];
                break;
            case "NoPairs":
                break;
            case "UnresolvedParentColumn":
                relationship.ColumnPairs = [new(Guid.NewGuid(), child.Columns[2].Id)];
                break;
            case "UnresolvedChildColumn":
                relationship.ColumnPairs = [new(parent.Columns[1].Id, Guid.NewGuid())];
                break;
            case "PartiallyUnresolved":
                relationship.ColumnPairs = [tenant, new(parent.Columns[1].Id, Guid.NewGuid())];
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(shape));
        }

        relationship.ConstraintName = "FK_probe";

        return new ErDiagram { Entities = { parent, child }, Relationships = { relationship } };
    }

    private static IEnumerable<DialectDdlRow> ConstraintNameRows() =>
        [
            // ---- 制約名の一致（DDL 側。同期側の行と同じ入力の図で、同じ名前が出る） ----
            DialectDdlRow
                .Ddl(
                    "ddl-foreign-key-name-trimmed-table",
                    "前後に空白のあるテーブル名で、DDL の外部キー名は空白を除いた名前から作る",
                    E.ForeignKey,
                    F.ConstraintName,
                    _ => CcFamily().Diagram
                )
                .ExpectAll(e =>
                    e.Lines("fk Orders FK_Orders_Customers (customer_id) -> Customers (id)*")
                ),
            DialectDdlRow
                .Ddl(
                    "ddl-primary-key-name-trimmed-table",
                    "前後に空白のあるテーブル名で、DDL の主キー名は空白を除いた名前から作る",
                    E.PrimaryKey,
                    F.ConstraintName,
                    _ => CcFamily().Diagram
                )
                .Expect([Ss, Pg, Or, Sl], e => e.Lines("pk Orders PK_Orders (id)*")),
            DialectDdlRow
                .Ddl(
                    "ddl-table-name-trimmed-table",
                    "前後に空白のあるテーブル名で、DDL が作るテーブルの名前は空白を除いた名前",
                    E.Table,
                    F.Quote,
                    _ => CcFamily().Diagram
                )
                .Expect(Ss, e => e.Text("CREATE TABLE [Orders] ("))
                .Expect(Pg, e => e.Text("CREATE TABLE \"Orders\" ("))
                .Expect(My, e => e.Text("CREATE TABLE `Orders` ("))
                .Expect(Or, e => e.Text("CREATE TABLE \"Orders\" ("))
                .Expect(Sl, e => e.Text("CREATE TABLE \"Orders\" (")),
            // ---- 制約名の一致（同期側） ----
            DialectDdlRow
                .SyncItems(
                    "sync-other-foreign-key-name-trimmed-table",
                    "前後に空白のあるテーブル名で、同期が付ける外部キー名は DDL と同じ名前",
                    P.SyncOther,
                    E.ForeignKey,
                    F.ConstraintName,
                    _ =>
                    {
                        var (_, parent, child, relationship) = CcFamily();

                        return new DdlSyncItems(
                            [
                                new SchemaDiffItem
                                {
                                    Kind = SchemaDiffKind.AddForeignKey,
                                    TableName = CcChildTable,
                                    ParentEntity = parent,
                                    ChildEntity = child,
                                    Relationship = relationship,
                                    ForeignKeyColumnPairs = [new("id", "customer_id")],
                                    IsSelected = true,
                                },
                            ],
                            TestDialectCapabilities.Neutral
                        );
                    }
                )
                .Expect([Ss, Pg, My, Or], e => e.Text("*FK_Orders_Customers*")),
            DialectDdlRow
                .SyncItems(
                    "sync-other-primary-key-name-trimmed-table",
                    "前後に空白のあるテーブル名で、同期が付ける主キー名は DDL と同じ名前",
                    P.SyncOther,
                    E.PrimaryKey,
                    F.ConstraintName,
                    _ =>
                    {
                        var (_, _, child, _) = CcFamily();

                        return new DdlSyncItems(
                            [
                                new SchemaDiffItem
                                {
                                    Kind = SchemaDiffKind.AlterPrimaryKey,
                                    TableName = CcChildTable,
                                    Entity = child,
                                    IsSelected = true,
                                },
                            ],
                            TestDialectCapabilities.Neutral
                        );
                    }
                )
                .Expect([Ss, Pg, Or], e => e.Text("*PK_Orders*")),
            DialectDdlRow
                .SyncItems(
                    "sync-rebuild-constraint-names-trimmed-table",
                    "前後に空白のあるテーブル名で、SQLite の再構築は DDL と同じ制約名を使う",
                    P.SyncRebuild,
                    E.ForeignKey,
                    F.ConstraintName,
                    _ =>
                    {
                        var (_, parent, child, relationship) = CcFamily();

                        // 子テーブルの列定義の変更で再構築を起こし、live の外部キー（名前なし）を再構築の CREATE TABLE へ載せる
                        return new DdlSyncItems(
                            [
                                new SchemaDiffItem
                                {
                                    Kind = SchemaDiffKind.AlterColumn,
                                    TableName = CcChildTable,
                                    ColumnName = "customer_id",
                                    Column = new Column
                                    {
                                        Name = "customer_id",
                                        DataType = "int",
                                        IsNullable = true,
                                    },
                                    IsSelected = true,
                                },
                            ],
                            null,
                            new SyncPlanContext
                            {
                                LiveEntities = [parent, child],
                                LiveRelationships = [relationship],
                            }
                        );
                    }
                )
                .Expect(
                    Sl,
                    e =>
                        e.Lines(
                            "fk Orders_quicker_rebuild FK_Orders_Customers (customer_id) -> Customers (id)*",
                            "pk Orders_quicker_rebuild PK_Orders (id)*"
                        )
                ),
            DialectDdlRow
                .SyncItems(
                    "sync-add-column-table-name-trimmed-table",
                    "前後に空白のあるテーブル名で、同期が指すテーブルは DDL が作るテーブルと同じ名前",
                    P.SyncAddColumn,
                    E.Table,
                    F.Quote,
                    _ =>
                    {
                        var (_, _, child, _) = CcFamily();

                        return new DdlSyncItems(
                            [
                                new SchemaDiffItem
                                {
                                    Kind = SchemaDiffKind.AddColumn,
                                    TableName = SchemaDiffService.NormalizeTable(child),
                                    ColumnName = "note",
                                    Entity = child,
                                    Column = new Column
                                    {
                                        Name = "note",
                                        DataType = "int",
                                        IsNullable = true,
                                    },
                                    IsSelected = true,
                                },
                            ],
                            TestDialectCapabilities.Neutral
                        );
                    }
                )
                .Expect(Ss, e => e.Text("*ALTER TABLE [Orders] *"))
                .Expect(Pg, e => e.Text("*ALTER TABLE \"Orders\" *"))
                .Expect(My, e => e.Text("*ALTER TABLE `Orders` *"))
                .Expect(Or, e => e.Text("*ALTER TABLE \"Orders\" *"))
                .Expect(Sl, e => e.Text("*ALTER TABLE \"Orders\" *")),
            // ---- 合成した主キー制約名のエスケープ ----
            DialectDdlRow
                .SyncItems(
                    "sync-add-table-primary-key-name-escaped",
                    "AddTable の主キー制約名は終端クォート文字を二重化する",
                    P.SyncAddTable,
                    E.PrimaryKey,
                    F.ConstraintName,
                    d => new DdlSyncItems(
                        [
                            new SchemaDiffItem
                            {
                                Kind = SchemaDiffKind.AddTable,
                                TableName = CcEscapedTableName[d],
                                Entity = CcPkTable(CcEscapedTableName[d]),
                                IsSelected = true,
                            },
                        ],
                        TestDialectCapabilities.Neutral
                    )
                )
                .Expect(Ss, e => e.Text("*CONSTRAINT [PK_t]]]*"))
                .Expect(Pg, e => e.Text("*CONSTRAINT \"PK_t\"\"\"*"))
                .Expect(My, e => e.Text("*CONSTRAINT `PK_t```*"))
                .Expect(Or, e => e.Text("*CONSTRAINT \"PK_t\"\"\"*")),
            DialectDdlRow
                .SyncItems(
                    "sync-other-primary-key-name-escaped",
                    "AlterPrimaryKey の付与文は主キー制約名の終端クォート文字を二重化する",
                    P.SyncOther,
                    E.PrimaryKey,
                    F.ConstraintName,
                    d => new DdlSyncItems(
                        [
                            new SchemaDiffItem
                            {
                                Kind = SchemaDiffKind.AlterPrimaryKey,
                                TableName = CcEscapedTableName[d],
                                Entity = CcPkTable(CcEscapedTableName[d]),
                                IsSelected = true,
                            },
                        ],
                        TestDialectCapabilities.Neutral
                    )
                )
                .Expect(Ss, e => e.Text("*CONSTRAINT [PK_t]]]*"))
                .Expect(Pg, e => e.Text("*CONSTRAINT \"PK_t\"\"\"*"))
                .Expect(Or, e => e.Text("*CONSTRAINT \"PK_t\"\"\"*")),
            DialectDdlRow
                .Ddl(
                    "ddl-primary-key-name-escaped",
                    "SQLite の DDL は主キー制約名の二重引用符を二重化する",
                    E.PrimaryKey,
                    F.ConstraintName,
                    d => DdlModel.Diagram(CcPkTable(CcEscapedTableName[d]))
                )
                .Expect(Sl, e => e.Text("*CONSTRAINT \"PK_t\"\"\"*")),
            // ---- コメントへ埋める説明の改行を畳む ----
            DialectDdlRow
                .Ddl(
                    "ddl-description-comment-folded-one-line",
                    "SQLite DDL の説明コメントは改行入りの説明を 1 行へ畳む",
                    E.Description,
                    F.CommentSanitize,
                    _ =>
                    {
                        var diagram = DdlModel.Diagram(
                            DdlModel.Table(
                                "orders",
                                new Column
                                {
                                    Name = "id",
                                    DataType = "int",
                                    IsPrimaryKey = true,
                                },
                                new Column
                                {
                                    Name = "memo",
                                    DataType = "text",
                                    Description = CcInjected,
                                    IsNullable = true,
                                }
                            )
                        );
                        diagram.Entities[0].Description = CcInjected;

                        return diagram;
                    }
                )
                .Expect(
                    Sl,
                    e =>
                        e.Text(
                                "-- orders: a DROP TABLE users; --",
                                "    -- memo: a DROP TABLE users; --"
                            )
                            // 畳んだ文字列が現れる行は上の 2 行だけ（どちらも行頭が --）
                            .Count(DialectDdlOutputs.Text, 2, "*a DROP TABLE users; --*")
                ),
            // ---- 外部キーを作る関係かの判定（DDL に外部キーとして現れるか） ----
            .. CcShapeRows(),
        ];

    /// <summary>形ごとの行（9 形・SQL Server だけ）</summary>
    private static IEnumerable<DialectDdlRow> CcShapeRows()
    {
        // 形の名前 → 外部キーが DDL に出るか（今の結果の絶対値）
        (string Shape, bool Emitted)[] shapes =
        [
            ("OneToMany", true),
            ("OneToOne", true),
            ("Composite", true),
            ("ManyToMany", false),
            ("ManyToManyWithPairs", false),
            ("NoPairs", false),
            ("UnresolvedParentColumn", false),
            ("UnresolvedChildColumn", false),
            ("PartiallyUnresolved", false),
        ];

        foreach (var (shape, emitted) in shapes)
        {
            var row = DialectDdlRow.Ddl(
                "ddl-foreign-key-defined-" + shape.ToLowerInvariant(),
                $"外部キーを作らない関係は DDL に外部キーとして現れない（{shape}）",
                E.ForeignKey,
                F.Omit,
                _ => CcShape(shape)
            );

            yield return emitted
                ? row.Expect(Ss, e => e.Text("*FK_probe*"))
                : row.Expect(Ss, e => e.NoText("*FK_probe*"));
        }
    }
}
