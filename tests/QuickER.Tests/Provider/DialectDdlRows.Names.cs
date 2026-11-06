using QuickER.Dialects;
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
            // ---- 名前の入口（改行・制御文字を含む名前を断る・クォートで扱える名前は通す） ----
            .. NameEntranceRows(),
            // ---- 外部キー・主キーの既定名（安全化・一意化・予約） ----
            .. ForeignKeyNameRows(),
            // ---- 方言の上限を超える既定名を縮める ----
            .. LongDefaultNameRows(),
        ];

    /// <summary>主キー列 id と、指定した名前の列を 1 本ずつ持つ最小の表の図（型は 5 方言とも int）</summary>
    private static ErDiagram NnMinimal(string tableName, string columnName) =>
        DdlModel.Diagram(
            DdlModel.Table(tableName, DdlModel.Pk("id", "int"), DdlModel.Col(columnName, "int"))
        );

    /// <summary>名前の入口の行（改行入りの名前は 1 行も出さずに名指しで断る・引用符を含むだけの名前は通す）</summary>
    private static IEnumerable<DialectDdlRow> NameEntranceRows() =>
        [
            DialectDdlRow
                .Ddl(
                    "ddl-table-name-with-newline-is-rejected",
                    "DDL は改行入りのテーブル名を名指しで断る",
                    E.Table,
                    F.Reject,
                    _ => NnMinimal(CcInjected, "memo")
                )
                .ExpectAll(e => e.Throws("InvalidOperationException: *TABLE*DROP TABLE users*")),
            DialectDdlRow
                .Ddl(
                    "ddl-column-name-with-newline-is-rejected",
                    "DDL は改行入りの列名を名指しで断る",
                    E.Column,
                    F.Reject,
                    _ => NnMinimal("orders", CcInjected)
                )
                .ExpectAll(e => e.Throws("InvalidOperationException: *COLUMN*orders*")),
            DialectDdlRow
                .Ddl(
                    "ddl-unique-constraint-name-with-newline-is-rejected",
                    "DDL は改行入りの一意制約名を名指しで断る",
                    E.Unique,
                    F.Reject,
                    _ =>
                    {
                        var diagram = NnMinimal("orders", "memo");
                        var entity = diagram.Entities[0];
                        entity.UniqueConstraints.Add(
                            new UniqueConstraint
                            {
                                Name = CcInjected,
                                ColumnIds = [entity.Columns[1].Id],
                            }
                        );

                        return diagram;
                    }
                )
                .ExpectAll(e => e.Throws("InvalidOperationException: *UNIQUE*")),
            DialectDdlRow
                .Ddl(
                    "ddl-foreign-key-name-with-newline-is-rejected",
                    "DDL は改行入りの外部キー制約名を名指しで断る",
                    E.ForeignKey,
                    F.Reject,
                    _ =>
                    {
                        var (diagram, _, _, relationship) = CcFamily();
                        relationship.ConstraintName = CcInjected;

                        return diagram;
                    }
                )
                .ExpectAll(e => e.Throws("InvalidOperationException: *FOREIGN KEY*")),
            DialectDdlRow
                .Ddl(
                    "ddl-quote-apostrophe-table-and-double-quote-column-accepted",
                    "改行・制御文字を含まず引用符だけを含む名前は断らず、クォートして DDL を作る",
                    E.Table,
                    F.Quote,
                    _ => NnMinimal("o'rders", "we\"ird")
                )
                .ExpectAll(e =>
                    e.NoException().Lines("table o'rders", "column o'rders.we\"ird int NULL*")
                ),
            DialectDdlRow
                .SyncItems(
                    "sync-add-table-table-name-with-newline-is-rejected",
                    "同期スクリプトは表の追加の改行入りのテーブル名を名指しで断る",
                    P.SyncAddTable,
                    E.Table,
                    F.Reject,
                    _ => new DdlSyncItems(
                        [
                            new SchemaDiffItem
                            {
                                Kind = SchemaDiffKind.AddTable,
                                TableName = CcInjected,
                                Entity = NnMinimal(CcInjected, "memo").Entities[0],
                                IsSelected = true,
                            },
                        ],
                        TestDialectCapabilities.Neutral
                    )
                )
                .ExpectAll(e => e.Throws("InvalidOperationException: *TABLE*")),
            DialectDdlRow
                .SyncPlan(
                    "sync-rebuild-table-name-with-newline-is-rejected",
                    "SQLite の同期スクリプトは再構築の計画の改行入りのテーブル名を名指しで断る",
                    E.Table,
                    F.Reject,
                    _ => new SyncPlan
                    {
                        Rebuilds =
                        [
                            new TableRebuildPlan
                            {
                                TableName = CcInjected,
                                NewDefinition = NnMinimal(CcInjected, "memo").Entities[0],
                                CreateOnly = false,
                                CopyColumns = ["id"],
                            },
                        ],
                    }
                )
                .Expect(Sl, e => e.Throws("InvalidOperationException: *TABLE*")),
            DialectDdlRow
                .SyncItems(
                    "sync-other-alter-column-name-with-newline-is-rejected",
                    "同期スクリプトは列の変更の項目 1 つの改行入りの列名を名指しで断る",
                    P.SyncOther,
                    E.Column,
                    F.Reject,
                    _ => new DdlSyncItems(
                        [
                            new SchemaDiffItem
                            {
                                Kind = SchemaDiffKind.AlterColumn,
                                TableName = "orders",
                                ColumnName = CcInjected,
                                Column = new Column
                                {
                                    Name = CcInjected,
                                    DataType = "int",
                                    IsNullable = true,
                                },
                                IsSelected = true,
                            },
                        ],
                        TestDialectCapabilities.Neutral with
                        {
                            SupportsAlterColumn = true,
                        }
                    )
                )
                .Expect(Ss, e => e.Throws("InvalidOperationException: *COLUMN*orders*")),
        ];

    /// <summary>
    /// customers（主キー id）と orders（主キー id・bill_customer_id・ship_customer_id）の図（型は方言の整数）
    /// </summary>
    private static (ErDiagram Diagram, Entity Customers, Entity Orders) CnCustomersOrders(
        string dialect
    )
    {
        var type = DdlModel.IntType[dialect];
        var customers = DdlModel.Table("customers", DdlModel.Pk("id", type));
        var orders = DdlModel.Table(
            "orders",
            DdlModel.Pk("id", type),
            DdlModel.Col("bill_customer_id", type),
            DdlModel.Col("ship_customer_id", type)
        );

        return (DdlModel.Diagram(customers, orders), customers, orders);
    }

    /// <summary>customers.id → orders の指定列の 1 対多（名前は指定したもの）</summary>
    private static Relationship CnLink(
        Entity customers,
        Entity orders,
        string childColumn,
        string? name = null
    ) =>
        new()
        {
            SourceEntityId = customers.Id,
            TargetEntityId = orders.Id,
            ConstraintName = name,
            ColumnPairs =
            [
                new RelationshipColumnPair(
                    customers.Columns[0].Id,
                    orders.Columns.Single(column => column.Name == childColumn).Id
                ),
            ],
        };

    /// <summary>外部キーを作らない名前の無い関係（多対多・列ペア無し・子の列を解決できない）</summary>
    private static Relationship CnNonForeignKey(string kind, Entity customers, Entity orders) =>
        kind switch
        {
            "many-to-many" => new Relationship
            {
                SourceEntityId = customers.Id,
                TargetEntityId = orders.Id,
                Type = RelationshipType.ManyToMany,
            },
            "no-pairs" => new Relationship
            {
                SourceEntityId = customers.Id,
                TargetEntityId = orders.Id,
            },
            "unresolved-column" => new Relationship
            {
                SourceEntityId = customers.Id,
                TargetEntityId = orders.Id,
                ColumnPairs = [new RelationshipColumnPair(customers.Columns[0].Id, Guid.NewGuid())],
            },
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };

    /// <summary>bill_customer_id の外部キーの行（名前は指定したもの）</summary>
    private static string CnBillLine(string name) =>
        $"fk orders {name} (bill_customer_id) -> customers (id)*";

    /// <summary>ship_customer_id の外部キーの行（名前は指定したもの）</summary>
    private static string CnShipLine(string name) =>
        $"fk orders {name} (ship_customer_id) -> customers (id)*";

    /// <summary>外部キー・主キーの既定名の行（DDL の外部キー名の解決＝ConstraintNames.ResolveForeignKeyNames）</summary>
    private static IEnumerable<DialectDdlRow> ForeignKeyNameRows() =>
        [
            DialectDdlRow
                .Ddl(
                    "ddl-constraint-names-schema-qualified-trimmed-table",
                    "前後に空白のあるスキーマ付きのテーブル名で、外部キー名は FK_{子}_{親}・主キー名は PK_{表}（どちらも安全化した名前）",
                    E.ForeignKey,
                    F.ConstraintName,
                    d =>
                    {
                        var type = DdlModel.IntType[d];
                        var parent = DdlModel.Table("dbo.Customers", DdlModel.Pk("id", type));
                        var child = DdlModel.Table(
                            " dbo.Orders ",
                            DdlModel.Pk("id", type),
                            DdlModel.Col("customer_id", type, nullable: false)
                        );
                        var diagram = DdlModel.Diagram(parent, child);
                        diagram.Relationships.Add(
                            new Relationship
                            {
                                SourceEntityId = parent.Id,
                                TargetEntityId = child.Id,
                                ColumnPairs = [new(parent.Columns[0].Id, child.Columns[1].Id)],
                            }
                        );

                        return diagram;
                    }
                )
                .ExpectAll(e =>
                    e.Lines("fk * FK_dbo_Orders_dbo_Customers (customer_id) -> * (id)*")
                )
                .Expect([Ss, Pg, Or, Sl], e => e.Lines("pk * PK_dbo_Orders (id)*")),
            .. new[] { "many-to-many", "no-pairs", "unresolved-column" }.Select(kind =>
                DialectDdlRow
                    .Ddl(
                        "ddl-fk-unnamed-non-fk-relationship-first-" + kind,
                        $"外部キーを作らない名前の無い関係（{kind}）が先に並んでも、同じ組の本物の外部キーは既定名を取り、2 本目は子の列名つきの名前",
                        E.ForeignKey,
                        F.ConstraintName,
                        d =>
                        {
                            var (diagram, customers, orders) = CnCustomersOrders(d);
                            diagram.Relationships.Add(CnNonForeignKey(kind, customers, orders));
                            diagram.Relationships.Add(
                                CnLink(customers, orders, "bill_customer_id")
                            );
                            diagram.Relationships.Add(
                                CnLink(customers, orders, "ship_customer_id")
                            );

                            return diagram;
                        }
                    )
                    .ExpectAll(e =>
                        e.Lines(
                            CnBillLine("FK_orders_customers"),
                            CnShipLine("FK_orders_customers_ship_customer_id")
                        )
                    )
            ),
            DialectDdlRow
                .Ddl(
                    "ddl-fk-named-many-to-many-reserves-name",
                    "名前を持つ多対多は外部キーを作らなくても名前を予約し、名前の無い外部キーは子の列名つきの名前になる",
                    E.ForeignKey,
                    F.ConstraintName,
                    d =>
                    {
                        var (diagram, customers, orders) = CnCustomersOrders(d);
                        diagram.Relationships.Add(
                            new Relationship
                            {
                                SourceEntityId = customers.Id,
                                TargetEntityId = orders.Id,
                                Type = RelationshipType.ManyToMany,
                                ConstraintName = "FK_orders_customers",
                            }
                        );
                        diagram.Relationships.Add(CnLink(customers, orders, "bill_customer_id"));

                        return diagram;
                    }
                )
                .ExpectAll(e => e.Lines(CnBillLine("FK_orders_customers_bill_customer_id"))),
            DialectDdlRow
                .Ddl(
                    "ddl-fk-unnamed-avoids-unique-constraint-name",
                    "一意制約の名前が外部キーの既定名をふさいでいれば、名前の無い外部キーは子の列名つきの名前になる",
                    E.ForeignKey,
                    F.ConstraintName,
                    d =>
                    {
                        var (diagram, customers, orders) = CnCustomersOrders(d);
                        orders.UniqueConstraints.Add(
                            new UniqueConstraint
                            {
                                Name = "FK_orders_customers",
                                ColumnIds = { orders.Columns[1].Id },
                            }
                        );
                        diagram.Relationships.Add(CnLink(customers, orders, "bill_customer_id"));

                        return diagram;
                    }
                )
                .ExpectAll(e => e.Lines(CnBillLine("FK_orders_customers_bill_customer_id"))),
            DialectDdlRow
                .Ddl(
                    "ddl-fk-unnamed-same-pair-twice-with-named-third",
                    "名前の無い同じ組の 2 本目は子の列名つきの名前になり、名前を持つ 3 本目はその名前のまま",
                    E.ForeignKey,
                    F.ConstraintName,
                    d =>
                    {
                        var (diagram, customers, orders) = CnCustomersOrders(d);
                        diagram.Relationships.Add(CnLink(customers, orders, "bill_customer_id"));
                        diagram.Relationships.Add(CnLink(customers, orders, "ship_customer_id"));
                        diagram.Relationships.Add(
                            CnLink(customers, orders, "ship_customer_id", "FK_custom")
                        );

                        return diagram;
                    }
                )
                .ExpectAll(e =>
                    e.Lines(
                        CnBillLine("FK_orders_customers"),
                        CnShipLine("FK_orders_customers_ship_customer_id"),
                        CnShipLine("FK_custom")
                    )
                ),
        ];

    /// <summary>上限のある 4 方言</summary>
    private static string[] LnLimitedDialects => [Ss, Pg, My, Or];

    /// <summary>既定の制約名が方言の上限を超える図（上限の無い方言は 200 文字の表）</summary>
    private static ErDiagram LnDiagram(string dialect) =>
        LongNameDiagrams.Build(IdentifierLengthLimits.ForDbms(dialect), dialect);

    /// <summary>
    /// 既定の制約名が方言の上限を超える図の期待：主キー 2・一意制約 1・外部キー 2 が方言の上限に収めた既定名（ConstraintNames で求める）で
    /// 現れ、上限のある方言では縮める前の名前がどこにも現れない
    /// </summary>
    private static void LnExpect(string dialect, DdlExpect e)
    {
        var limit = IdentifierLengthLimits.ForDbms(dialect);
        var unlimited = IdentifierLengthLimit.Unlimited;
        var diagram = LnDiagram(dialect);
        var parent = diagram.Entities[0].TableName;
        var child = diagram.Entities[1].TableName;
        var details = ConstraintNames.ResolveForeignKeyNameDetails(
            diagram.Entities,
            diagram.Relationships,
            limit
        );
        var bill = details[diagram.Relationships[0]];
        var ship = details[diagram.Relationships[1]];
        var unique = UniqueConstraint.SynthesizeName(child, ["bill_id", "ship_id"], limit);

        e.Lines(
            $"pk {parent} {ConstraintNames.PrimaryKey(parent, limit)} (id)*",
            $"pk {child} {ConstraintNames.PrimaryKey(child, limit)} (id)*",
            $"uq {child} {unique} (bill_id, ship_id)*",
            $"fk {child} {bill.Name} (bill_id) -> {parent} (id)*",
            $"fk {child} {ship.Name} (ship_id) -> {parent} (id)*"
        );

        if (!limit.IsUnlimited)
        {
            e.NoText(
                $"*{ConstraintNames.PrimaryKey(parent, unlimited)}*",
                $"*{ConstraintNames.PrimaryKey(child, unlimited)}*",
                $"*{UniqueConstraint.SynthesizeName(child, ["bill_id", "ship_id"], unlimited)}*",
                $"*{bill.UnshortenedName}*",
                $"*{ship.UnshortenedName}*"
            );
        }
    }

    /// <summary>方言の上限を超える既定名の行（DDL・同期スクリプトが同じ縮めた名前を使う）</summary>
    private static IEnumerable<DialectDdlRow> LongDefaultNameRows() =>
        [
            DialectDdlRow
                .Ddl(
                    "ddl-long-default-names-are-shortened",
                    "既定の制約名が上限を超える図の DDL は、上限に収めた名前で制約を作り縮める前の名前を出さない（上限の無い SQLite は縮めない）",
                    E.PrimaryKey,
                    F.ConstraintName,
                    LnDiagram
                )
                .ExpectWithDialect([.. LnLimitedDialects, Sl], LnExpect),
            DialectDdlRow
                .SyncDiff(
                    "sync-add-table-long-default-names-are-shortened",
                    "既定の制約名が上限を超える図の同期スクリプトは、DDL と同じ縮めた名前で制約を作る",
                    P.SyncAddTable,
                    E.ForeignKey,
                    F.ConstraintName,
                    d =>
                    {
                        var diagram = LnDiagram(d);

                        return new DdlSyncDiff([], [], diagram.Entities, diagram.Relationships);
                    }
                )
                .ExpectWithDialect(LnLimitedDialects, LnExpect),
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
