using QuickER.Model;
using QuickER.Provider;
using QuickER.Tests.TestSupport;
using E = QuickER.Tests.Provider.DialectDdlElements;
using F = QuickER.Tests.Provider.DialectDdlFacets;
using P = QuickER.Tests.Provider.DialectDdlPaths;

namespace QuickER.Tests.Provider;

/// <summary>
/// 固定文が英語。ASCII だけの入力から作った出力に CJK が無いことは、
/// 全行にかかる自動の照合（英語）が見る。行は入力だけを組み、例外を投げないことだけを期待に書く
/// </summary>
internal static partial class DialectDdlRows
{
    private static IEnumerable<DialectDdlRow> EnglishRows() =>
        [
            DialectDdlRow
                .Ddl(
                    "ddl-table-fixed-text-english",
                    "5 方言の DDL 出力に日本語（CJK）が含まれない（ASCII のみの図）",
                    E.Table,
                    F.English,
                    _ => EnAsciiDiagram()
                )
                .ExpectAll(e => e.NoException()),
            DialectDdlRow
                .SyncItems(
                    "sync-other-fixed-text-english",
                    "各方言の同期スクリプト出力に日本語（CJK）が含まれない（ASCII のみの差分）",
                    P.SyncOther,
                    E.Table,
                    F.English,
                    _ => new DdlSyncItems(EnAsciiDiffItems(), TestDialectCapabilities.Neutral)
                )
                .Expect([Ss, Pg, My, Or], e => e.NoException()),
            DialectDdlRow
                .SyncItems(
                    "sync-rebuild-fixed-text-english",
                    "SQLite の再構築の同期スクリプト出力に日本語（CJK）が含まれない（ASCII のみの再構築計画）",
                    P.SyncRebuild,
                    E.Table,
                    F.English,
                    _ => EnSqliteRebuild()
                )
                .Expect(Sl, e => e.NoException()),
            DialectDdlRow
                .SyncItems(
                    "sync-other-column-reorder-fixed-text-english",
                    "MySQL の列順変更の同期スクリプト出力に日本語（CJK）が含まれない（ASCII のみ）",
                    P.SyncOther,
                    E.Column,
                    F.English,
                    _ => EnMySqlReorder()
                )
                .Expect(My, e => e.NoException()),
        ];

    /// <summary>ASCII のみの列順変更（MySQL Native）の入力（live: id,a,b,c → target: c,id,a,b）</summary>
    private static DdlSyncItems EnMySqlReorder()
    {
        static Entity Ent(string table, params string[] cols)
        {
            var e = new Entity { TableName = table };

            foreach (var c in cols)
            {
                e.Columns.Add(
                    new Column
                    {
                        Name = c,
                        DataType = "int",
                        IsNullable = true,
                    }
                );
            }

            return e;
        }

        var live = Ent("t", "id", "a", "b", "c");
        var target = Ent("t", "c", "id", "a", "b");

        return new DdlSyncItems(
            [
                new SchemaDiffItem
                {
                    Kind = SchemaDiffKind.ReorderColumns,
                    TableName = "t",
                    Entity = target,
                    IsSelected = true,
                },
            ],
            TestDialectCapabilities.Neutral with
            {
                ColumnReorder = ColumnReorderMode.Native,
            },
            new SyncPlanContext { LiveEntities = [live] }
        );
    }

    /// <summary>ASCII のみの再構築計画（CreateOnly＋FK・既存テーブル再構築＋補助オブジェクト・ADD COLUMN・DROP TABLE）の入力</summary>
    private static DdlSyncItems EnSqliteRebuild()
    {
        var orders = new Entity
        {
            TableName = "orders",
            Columns =
            {
                new Column
                {
                    Name = "id",
                    DataType = "int",
                    IsPrimaryKey = true,
                },
                new Column { Name = "note", DataType = "text" },
                new Column { Name = "old_col", DataType = "int" },
            },
        };
        var customer = new Entity
        {
            TableName = "customer",
            Columns =
            {
                new Column
                {
                    Name = "id",
                    DataType = "int",
                    IsPrimaryKey = true,
                },
            },
        };
        var legacy = new Entity
        {
            TableName = "legacy",
            Columns =
            {
                new Column
                {
                    Name = "id",
                    DataType = "int",
                    IsPrimaryKey = true,
                },
            },
        };

        // 新規テーブル invoice（orders への FK 付き）
        var invoice = new Entity
        {
            TableName = "invoice",
            Columns =
            {
                new Column
                {
                    Name = "id",
                    DataType = "int",
                    IsPrimaryKey = true,
                },
                new Column { Name = "orders_id", DataType = "int" },
            },
        };
        var invoiceRel = new Relationship
        {
            SourceEntityId = orders.Id,
            TargetEntityId = invoice.Id,
            ColumnPairs = [new(orders.Columns[0].Id, invoice.Columns[1].Id)],
        };

        var context = new SyncPlanContext
        {
            LiveEntities = [orders, customer, legacy],
            LiveRelationships = [],
            AuxiliaryObjects =
            [
                new SchemaAuxiliaryObject
                {
                    TableName = "orders",
                    Name = "idx_orders_note",
                    Kind = SchemaAuxiliaryObjectKind.Index,
                    CreateSql = "CREATE INDEX \"idx_orders_note\" ON \"orders\" (\"note\")",
                },
                new SchemaAuxiliaryObject
                {
                    TableName = "orders",
                    Name = "trg_orders",
                    Kind = SchemaAuxiliaryObjectKind.Trigger,
                    CreateSql =
                        "CREATE TRIGGER \"trg_orders\" AFTER INSERT ON \"orders\" BEGIN SELECT 1; END",
                },
            ],
        };

        var items = new List<SchemaDiffItem>
        {
            new()
            {
                Kind = SchemaDiffKind.AddTable,
                TableName = "invoice",
                Entity = invoice,
                IsSelected = true,
            },
            new()
            {
                Kind = SchemaDiffKind.AddForeignKey,
                TableName = "invoice",
                ColumnName = "orders_id",
                ForeignKeyColumnPairs = [new("id", "orders_id")],
                ParentEntity = orders,
                ChildEntity = invoice,
                Relationship = invoiceRel,
                IsSelected = true,
            },
            new()
            {
                Kind = SchemaDiffKind.AlterColumn,
                TableName = "orders",
                ColumnName = "note",
                Column = new Column { Name = "note", DataType = "varchar(100)" },
                IsSelected = true,
            },
            new()
            {
                Kind = SchemaDiffKind.DropColumn,
                TableName = "orders",
                ColumnName = "old_col",
                Column = new Column { Name = "old_col", DataType = "int" },
                IsSelected = true,
            },
            new()
            {
                Kind = SchemaDiffKind.AddColumn,
                TableName = "customer",
                ColumnName = "email",
                Column = new Column { Name = "email", DataType = "text" },
                IsSelected = true,
            },
            new()
            {
                Kind = SchemaDiffKind.DropTable,
                TableName = "legacy",
                Entity = legacy,
                IsSelected = true,
            },
        };

        // 能力は方言の宣言（SQLite の実装の宣言）、文脈は live の表と補助オブジェクト
        return new DdlSyncItems(items, null, context);
    }

    /// <summary>ASCII のみの識別子・空の説明の図（ヘッダ・多対多コメント・Oracle の ON UPDATE 注意コメントを通す）</summary>
    private static ErDiagram EnAsciiDiagram()
    {
        var parent = new Entity
        {
            TableName = "parent",
            Columns =
            {
                new Column
                {
                    Name = "id",
                    DataType = "int",
                    IsPrimaryKey = true,
                },
            },
        };
        var child = new Entity
        {
            TableName = "child",
            Columns =
            {
                new Column { Name = "parent_id", DataType = "int" },
            },
        };
        var other = new Entity
        {
            TableName = "other",
            Columns =
            {
                new Column
                {
                    Name = "id",
                    DataType = "int",
                    IsPrimaryKey = true,
                },
            },
        };

        return new ErDiagram
        {
            Entities = [parent, child, other],
            Relationships =
            [
                // ON UPDATE 指定つきの 1 対多 → Oracle の注意コメントを誘発する
                new Relationship
                {
                    SourceEntityId = parent.Id,
                    TargetEntityId = child.Id,
                    Type = RelationshipType.OneToMany,
                    ColumnPairs = [new(parent.Columns[0].Id, child.Columns[0].Id)],
                    ConstraintName = "FK_child_parent",
                    OnDelete = ForeignKeyReferentialAction.Cascade,
                    OnUpdate = ForeignKeyReferentialAction.SetNull,
                },
                // 多対多 → ジャンクションテーブルの案内コメントを誘発する
                new Relationship
                {
                    SourceEntityId = parent.Id,
                    TargetEntityId = other.Id,
                    Type = RelationshipType.ManyToMany,
                },
            ],
        };
    }

    /// <summary>ASCII のみで構成した同期の差分項目（セクション見出し・FK スキップ・Oracle 注意・MySQL 列スキップ・主キー変更）</summary>
    private static IReadOnlyList<SchemaDiffItem> EnAsciiDiffItems()
    {
        var addedTable = new Entity
        {
            TableName = "t",
            Columns =
            {
                new Column
                {
                    Name = "id",
                    DataType = "int",
                    IsPrimaryKey = true,
                },
            },
        };
        // 主キーを持たない親 → FK 参照列が解決できず「スキップ」コメントを誘発する
        var parentWithoutPk = new Entity
        {
            TableName = "parent",
            Columns =
            {
                new Column { Name = "code", DataType = "int" },
            },
        };
        var childForSkip = new Entity { TableName = "child" };
        // 解決可能な親子＋ON UPDATE 指定 → Oracle の注意コメントを誘発する
        var parentWithPk = new Entity
        {
            TableName = "p2",
            Columns =
            {
                new Column
                {
                    Name = "id",
                    DataType = "int",
                    IsPrimaryKey = true,
                },
            },
        };
        var childForFk = new Entity { TableName = "c2" };

        return
        [
            new SchemaDiffItem
            {
                Kind = SchemaDiffKind.AddTable,
                TableName = "t",
                Entity = addedTable,
                IsSelected = true,
            },
            new SchemaDiffItem
            {
                Kind = SchemaDiffKind.AddForeignKey,
                TableName = "child",
                ColumnName = null,
                ParentEntity = parentWithoutPk,
                ChildEntity = childForSkip,
                IsSelected = true,
            },
            new SchemaDiffItem
            {
                Kind = SchemaDiffKind.AddForeignKey,
                TableName = "c2",
                ColumnName = "p2_id",
                ForeignKeyColumnPairs = [new("id", "p2_id")],
                ParentEntity = parentWithPk,
                ChildEntity = childForFk,
                Relationship = new Relationship
                {
                    OnDelete = ForeignKeyReferentialAction.Cascade,
                    OnUpdate = ForeignKeyReferentialAction.SetNull,
                },
                IsSelected = true,
            },
            new SchemaDiffItem
            {
                // 主キー変更 → 4 方言はカタログ逆引きの実 DDL を誘発する
                Kind = SchemaDiffKind.AlterPrimaryKey,
                TableName = "t",
                Entity = addedTable,
                IsSelected = true,
            },
            new SchemaDiffItem
            {
                // Entity に該当列が無い → MySQL の列スキップコメントを誘発する（NewDescription は空＝ASCII）
                Kind = SchemaDiffKind.SetColumnDescription,
                TableName = "t",
                ColumnName = "missing",
                Entity = addedTable,
                NewDescription = string.Empty,
                IsSelected = true,
            },
        ];
    }
}
