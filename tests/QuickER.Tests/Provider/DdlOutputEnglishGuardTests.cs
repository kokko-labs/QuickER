using System.Collections.Generic;
using System.Text.RegularExpressions;
using AwesomeAssertions;
using QuickER.Model;
using QuickER.MySql;
using QuickER.Oracle;
using QuickER.PostgreSql;
using QuickER.Provider;
using QuickER.Sqlite;
using QuickER.SqlServer;
using Xunit;

namespace QuickER.Tests.Provider;

/// <summary>
/// DDL（<c>.sql</c>）およびスキーマ同期スクリプトの固定文（ヘッダ・注意コメント・セクション見出し・スキップコメント）が
/// 英語で統一されていること＝日本語（CJK 文字）が紛れ込んでいないことを守る回帰防止ガード。
/// </summary>
/// <remarks>
/// <para>
/// 生成される SQL テキストに埋め込まれる固定文は、各方言の <c>DdlGenerator</c>（<see cref="DdlGeneratorBase"/> 派生）と
/// 各方言の <c>SyncScriptBuilder</c> に由来する。ここへ日本語が混入してもビルド・型検査は通ってしまい静かに回帰するため、
/// 「日本語を含まない入力（ASCII のみの識別子・説明は空）から生成した SQL に CJK が 1 文字も無い」ことをテストで固定する。
/// </para>
/// <para>
/// <b>ユーザーデータ由来の日本語は正当</b>（テーブル・列・説明が SQL コメントや <c>COMMENT</c> 句へ流れる）。
/// そのため本テストは入力の説明（<see cref="Entity.Description"/> / <see cref="Column.Description"/> /
/// <see cref="SchemaDiffItem.NewDescription"/>）をすべて空にし、識別子も ASCII のみで構成する。
/// 生成 C# コード側の同種ガードは <c>QuickER.Tests.Generator.GeneratedOutputEnglishGuardTests</c>。
/// </para>
/// </remarks>
public sealed class DdlOutputEnglishGuardTests
{
    /// <summary>
    /// CJK 文字の検出パターン（U+3000-U+9FFF＝CJK 記号・ひらがな・カタカナ・CJK 統合漢字、
    /// U+FF00-U+FFEF＝全角英数記号・半角カナ）。
    /// </summary>
    private static readonly Regex CjkPattern = new("[　-鿿＀-￯]", RegexOptions.Compiled);

    /// <summary>
    /// 5 方言の <c>DdlGenerator</c> が ASCII のみの図から出力する DDL に CJK が含まれないことを検証する。
    /// ヘッダ（自動生成コメント・生成日時）・多対多コメント・Oracle の ON UPDATE 注意コメントを網羅する。
    /// </summary>
    [Fact(DisplayName = "5 方言の DDL 出力に日本語（CJK）が含まれない（ASCII のみの図）")]
    public void DdlGenerators_ProduceNoCjk()
    {
        var diagram = BuildAsciiDiagram();

        var outputs = new (string Dialect, string Sql)[]
        {
            ("SqlServer", new SqlServerDdlGenerator().Build(diagram)),
            ("PostgreSql", new PostgreSqlDdlGenerator().Build(diagram)),
            ("MySql", new MySqlDdlGenerator().Build(diagram)),
            ("Oracle", new OracleDdlGenerator().Build(diagram)),
            ("Sqlite", new SqliteDdlGenerator().Build(diagram)),
        };

        AssertNoCjk(outputs);
    }

    /// <summary>
    /// 各方言の <c>SyncScriptBuilder</c> が ASCII のみの差分から出力する同期スクリプトに CJK が含まれないことを検証する。
    /// セクション見出し・FK スキップコメント・Oracle の ON UPDATE 注意コメント・MySQL の列スキップコメントを網羅する。
    /// SQLite はテーブル再構築（PRAGMA ヘッダ／フッタ・RebuildTable 見出し・補助オブジェクト再作成）を別途網羅する。
    /// </summary>
    [Fact(
        DisplayName = "各方言の同期スクリプト出力に日本語（CJK）が含まれない（ASCII のみの差分）"
    )]
    public void SyncScriptBuilders_ProduceNoCjk()
    {
        var items = BuildAsciiDiffItems();
        var plan = new SyncPlanner().BuildPlan(items, new SyncDialectCapabilities());

        var outputs = new (string Dialect, string Sql)[]
        {
            ("SqlServer", new SqlServerSyncScriptBuilder().Build(plan)),
            ("PostgreSql", new PostgreSqlSyncScriptBuilder().Build(plan)),
            ("MySql", new MySqlSyncScriptBuilder().Build(plan)),
            ("Oracle", new OracleSyncScriptBuilder().Build(plan)),
            ("Sqlite", BuildSqliteRebuildScript()),
            ("MySqlReorder", BuildMySqlReorderScript()),
        };

        AssertNoCjk(outputs);
    }

    /// <summary>
    /// ASCII のみの列順変更（MySQL Native）から同期スクリプトを生成する。
    /// ReorderColumns 見出しと <c>MODIFY ... AFTER</c> / <c>FIRST</c> の固定文を出力経路で通す。
    /// </summary>
    private static string BuildMySqlReorderScript()
    {
        Entity Ent(string table, params string[] cols)
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

        // live: id,a,b,c → target: c,id,a,b（c を先頭へ＝FIRST 経路も通す）
        var live = Ent("t", "id", "a", "b", "c");
        var target = Ent("t", "c", "id", "a", "b");
        var item = new SchemaDiffItem
        {
            Kind = SchemaDiffKind.ReorderColumns,
            TableName = "t",
            Entity = target,
            IsSelected = true,
        };
        var plan = new SyncPlanner().BuildPlan(
            [item],
            new SyncDialectCapabilities { ColumnReorder = ColumnReorderMode.Native },
            new SyncPlanContext { LiveEntities = [live] }
        );
        return new MySqlSyncScriptBuilder().Build(plan);
    }

    /// <summary>
    /// ASCII のみの再構築計画（CreateOnly＋FK・既存テーブル再構築＋補助オブジェクト・ADD COLUMN・DROP TABLE）から
    /// SQLite の同期スクリプトを生成する。PRAGMA ヘッダ／フッタ・RebuildTable 見出しの固定文を出力経路で通す。
    /// </summary>
    private static string BuildSqliteRebuildScript()
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
            SourceColumnId = orders.Columns[0].Id,
            TargetColumnId = invoice.Columns[1].Id,
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
                new SchemaAuxiliaryObject
                {
                    TableName = "orders",
                    Name = "sqlite_autoindex_orders_1",
                    Kind = SchemaAuxiliaryObjectKind.UniqueConstraint,
                    Columns = ["note"],
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

        var capabilities = new SqliteProvider().SyncCapabilities;
        var plan = new SyncPlanner().BuildPlan(items, capabilities, context);
        return new SqliteSyncScriptBuilder().Build(plan);
    }

    /// <summary>
    /// ASCII のみの識別子・空の説明で構成した検証用 ER 図を組み立てる。
    /// ヘッダ・多対多コメント・Oracle の ON UPDATE 注意コメントの各固定文を出力経路で通す。
    /// </summary>
    private static ErDiagram BuildAsciiDiagram()
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
                    SourceColumnId = parent.Columns[0].Id,
                    TargetColumnId = child.Columns[0].Id,
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

    /// <summary>
    /// ASCII のみで構成した検証用の差分項目一覧を組み立てる。
    /// セクション見出し・FK スキップ・Oracle 注意コメント・MySQL 列スキップの各固定文を出力経路で通す。
    /// </summary>
    private static IReadOnlyList<SchemaDiffItem> BuildAsciiDiffItems()
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

    /// <summary>
    /// 各方言の出力に CJK 文字が含まれないことを検証する。
    /// 検出時は「どの方言の何行目・該当行の内容」を列挙し、固定文への日本語混入へ誘導するメッセージで失敗させる。
    /// </summary>
    private static void AssertNoCjk(IEnumerable<(string Dialect, string Sql)> outputs)
    {
        var findings = new List<string>();

        foreach (var (dialect, sql) in outputs)
        {
            var lines = sql.Split('\n');

            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i].TrimEnd('\r');

                if (CjkPattern.IsMatch(line))
                {
                    findings.Add($"{dialect}:{i + 1} 「{line.Trim()}」");
                }
            }
        }

        findings
            .Should()
            .BeEmpty(
                "DDL / 同期スクリプトの固定文は英語で統一する必要があります（ユーザーデータ由来の日本語は入力を空にしてあります）。"
                    + "検出＝方言の DdlGenerator または SyncScriptBuilder の固定文字列に日本語が混入しています。"
                    + "上記の 方言:行番号・該当行 を確認してください"
            );
    }
}
