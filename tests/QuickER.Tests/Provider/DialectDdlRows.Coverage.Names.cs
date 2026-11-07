using QuickER.Dialects;
using QuickER.Model;
using QuickER.Provider;
using QuickER.Tests.TestSupport;
using static QuickER.Tests.Provider.DdlModel;
using E = QuickER.Tests.Provider.DialectDdlElements;
using F = QuickER.Tests.Provider.DialectDdlFacets;
using O = QuickER.Tests.Provider.DialectDdlOutputs;
using P = QuickER.Tests.Provider.DialectDdlPaths;

namespace QuickER.Tests.Provider;

/// <summary>
/// 網羅しない組を埋める行（スキーマ修飾名・多対多・型の表記・再構築の文の形）
/// </summary>
internal static partial class DialectDdlRows
{
    /// <summary>主キーだけの表 1 本の図に、指定した 2 表の多対多（列ペア・名前は任意）を足した図</summary>
    private static ErDiagram CvManyToMany(
        string dialect,
        bool withPairs,
        string? name,
        bool missingEnd = false
    )
    {
        var a = Table("a", Pk("id", IntType[dialect]));
        var b = Table("b", Pk("id", IntType[dialect]));
        var diagram = Diagram(a, b);
        diagram.Relationships.Add(
            new Relationship
            {
                SourceEntityId = a.Id,
                TargetEntityId = missingEnd ? Guid.NewGuid() : b.Id,
                Type = RelationshipType.ManyToMany,
                ColumnPairs = withPairs ? [new(a.Columns[0].Id, b.Columns[0].Id)] : [],
                ConstraintName = name,
            }
        );

        return diagram;
    }

    private static IEnumerable<DialectDdlRow> CoverageNameAndTypeRows() =>
        [
            // 行 36
            DialectDdlRow
                .SyncItems(
                    "sync-add-table-schema-qualified-name-is-split-and-warned-where-unsupported",
                    "スキーマ修飾名の表の追加は、スキーマと表に分けてクォートし、主キー名は PK_dbo_Orders。スキーマ修飾名を持てない SQLite だけ警告する（src/QuickER.Dialects の扱いの表 SchemaQualifiedTableName）",
                    P.SyncAddTable,
                    E.SchemaQualified,
                    F.StatementShape,
                    d => new DdlSyncItems(
                        [CvAddTable(Table("dbo.Orders", Pk("id", IntType[d])))],
                        null,
                        new SyncPlanContext()
                    )
                )
                .ExpectAll(e => e.Lines("table dbo.Orders", "pk dbo.Orders PK_dbo_Orders (id)*"))
                .Expect(Sl, e => e.Contains(O.Warnings, "SchemaQualifiedTableName*"))
                .Expect([Ss, Pg, My, Or], e => e.Absent(O.Warnings, "SchemaQualifiedTableName*")),
            // 行 37
            DialectDdlRow
                .SyncItems(
                    "sync-other-drop-table-schema-and-table-terminators-are-doubled-per-part",
                    "終端クォート文字を含むスキーマ部・表部のテーブルの削除は、部分ごとに二重化してクォートする（src/QuickER.Provider/CLAUDE.md「制約名の正本と識別子の分割クォート」）",
                    P.SyncOther,
                    E.SchemaQualified,
                    F.Quote,
                    d => new DdlSyncItems(
                        [
                            new SchemaDiffItem
                            {
                                Kind = SchemaDiffKind.DropTable,
                                TableName = CvQuoteName(d, "s") + "." + CvQuoteName(d, "t"),
                                IsSelected = true,
                            },
                        ],
                        TestDialectCapabilities.Neutral
                    )
                )
                .ExpectWithDialect(
                    DialectNames.All,
                    (d, e) =>
                        e.Text(
                            $"*DROP TABLE*{CvQuote(d, CvQuoteName(d, "s"))}.{CvQuote(d, CvQuoteName(d, "t"))}*"
                        )
                ),
            // 行 38
            DialectDdlRow
                .Ddl(
                    "ddl-schema-qualified-tables-write-no-schema-creation-statements",
                    "特性の固定（文書に定めが無い今の振る舞い）：別スキーマの表を持つ図でも、DDL は CREATE SCHEMA・USE・search_path を書かない（スキーマの作成は利用者の責任）。根拠の実装は src/QuickER.Provider/DdlGeneratorBase.cs（スキーマの作成文を出す箇所が無い）",
                    E.SchemaQualified,
                    F.Omit,
                    d =>
                        Diagram(
                            Table("a.x", Pk("id", IntType[d])),
                            Table("b.y", Pk("id", IntType[d]))
                        )
                )
                .ExpectAll(e =>
                    e.Lines("table a.x", "table b.y")
                        .NoText("*CREATE SCHEMA*", "*CREATE DATABASE*", "USE *", "*search_path*")
                ),
            // 行 39
            DialectDdlRow
                .Ddl(
                    "ddl-schema-qualified-tables-keep-diagram-order",
                    "特性の固定（文書に定めが無い今の振る舞い）：スキーマをまたぐ表は、図の順のまま CREATE する（スキーマごとの並べ替えをしない）。根拠の実装は src/QuickER.Provider/DdlGeneratorBase.cs の Build が図の表の順に回すこと",
                    E.SchemaQualified,
                    F.Order,
                    d =>
                        Diagram(
                            Table("s1.a", Pk("id", IntType[d])),
                            Table("s2.b", Pk("id", IntType[d])),
                            Table("s1.c", Pk("id", IntType[d]))
                        )
                )
                .ExpectAll(e => e.Order(O.Lines, "table s1.a", "table s2.b", "table s1.c")),
            // 行 40
            DialectDdlRow
                .Ddl(
                    "ddl-schema-qualified-schema-part-over-limit-is-rejected-where-limited",
                    "スキーマ部が 130 字のテーブル名は、識別子の上限のある 4 方言が断り、上限の無い SQLite は断らない（src/QuickER.Provider/CLAUDE.md「利用者が付けた名前の上限」）",
                    E.SchemaQualified,
                    F.Reject,
                    d => Diagram(Table(new string('x', 130) + ".t", Pk("id", IntType[d])))
                )
                .Expect(
                    [Ss, Pg, My, Or],
                    e => e.Throws("InvalidOperationException: *is longer than the limit of*")
                )
                .Expect(Sl, e => e.NoException()),
            // 行 41
            DialectDdlRow
                .SyncItems(
                    "sync-add-column-schema-qualified-table-is-split-and-warned-where-unsupported",
                    "スキーマ修飾名の表への列の追加は、スキーマと表に分けてクォートする。スキーマ修飾名を持てない SQLite だけ警告する（src/QuickER.Dialects の扱いの表 SchemaQualifiedTableName）",
                    P.SyncAddColumn,
                    E.SchemaQualified,
                    F.StatementShape,
                    d => new DdlSyncItems(
                        [CvAddColumn("dbo.Orders", Col("note", IntType[d]))],
                        null,
                        new SyncPlanContext()
                    )
                )
                .ExpectWithDialect(
                    DialectNames.All,
                    (d, e) => e.Text($"ALTER TABLE {CvQuote(d, "dbo")}.{CvQuote(d, "Orders")} ADD*")
                )
                .Expect(Sl, e => e.Contains(O.Warnings, "SchemaQualifiedTableName*"))
                .Expect([Ss, Pg, My, Or], e => e.Absent(O.Warnings, "SchemaQualifiedTableName*")),
            // 行 42
            DialectDdlRow
                .SyncItems(
                    "sync-rebuild-statement-order-create-copy-drop-rename",
                    "SQLite の再構築の文は、一時表の CREATE → INSERT ... SELECT → 元の表の DROP → RENAME の順で並ぶ（src/QuickER.Provider/CLAUDE.md「テーブル再構築の合成」・src/QuickER.Provider.Sqlite/CLAUDE.md「同期」）",
                    P.SyncRebuild,
                    E.Table,
                    F.StatementShape,
                    _ =>
                    {
                        var live = Table(
                            "t",
                            Pk("id", "INTEGER"),
                            Col("a", "INTEGER", nullable: false)
                        );
                        return CvLiveItems(
                            [live],
                            [],
                            CvAlterColumn("t", Col("a", "INTEGER", nullable: true))
                        );
                    }
                )
                .Expect(
                    Sl,
                    e =>
                        e.Order(
                            O.Text,
                            "CREATE TABLE \"t_quicker_rebuild\" (",
                            "INSERT INTO \"t_quicker_rebuild\"*",
                            "DROP TABLE \"t\";",
                            "ALTER TABLE \"t_quicker_rebuild\" RENAME TO \"t\";"
                        )
                ),
            // 行 43
            DialectDdlRow
                .Ddl(
                    "ddl-many-to-many-with-column-pairs-writes-no-foreign-key",
                    "列ペア付きの多対多も外部キーを出さず、中間テーブルを別に定義する旨の注記だけを書く（src/QuickER.Provider/DdlGeneratorBase.cs の多対多の扱い・扱いの表 ManyToManyRelationship）",
                    E.ManyToMany,
                    F.StatementShape,
                    d => CvManyToMany(d, withPairs: true, name: null)
                )
                .ExpectAll(e =>
                    e.Lines("note many-to-many | -- Many-to-many (a <-> b)*")
                        .NoLines("fk * * *")
                        .NoText("*FOREIGN KEY*")
                ),
            // 行 44
            DialectDdlRow
                .Ddl(
                    "ddl-many-to-many-name-is-not-written",
                    "名前付きの多対多は制約を作らないので、その名前を出力に書かない（src/QuickER.Model/ConstraintNames.cs は名前を予約するだけ）",
                    E.ManyToMany,
                    F.ConstraintName,
                    d => CvManyToMany(d, withPairs: true, name: "FK_mm_name")
                )
                .ExpectAll(e =>
                    e.Lines("note many-to-many | -- Many-to-many (a <-> b)*").NoText("*FK_mm_name*")
                ),
            // 行 45
            DialectDdlRow
                .Ddl(
                    "ddl-many-to-many-with-missing-end-writes-nothing",
                    "端が図に無い多対多は何も出さない（注記も外部キーも）（src/QuickER.Provider/DdlGeneratorBase.cs の多対多の端の解決）",
                    E.ManyToMany,
                    F.Omit,
                    d => CvManyToMany(d, withPairs: true, name: null, missingEnd: true)
                )
                .ExpectAll(e =>
                    e.NoException()
                        .Lines("table a", "table b")
                        .NoText("*Many-to-many*", "*FOREIGN KEY*")
                ),
            // 行 46
            DialectDdlRow
                .Ddl(
                    "ddl-many-to-many-notes-follow-relationship-order",
                    "関係の順が 外部キー・多対多・外部キー のとき、逐次 4 方言は関係の順に出し、SQLite は多対多の注記を全表の後へまとめる（src/QuickER.Provider/DdlGeneratorBase.cs・SqliteDdlGenerator.cs）",
                    E.ManyToMany,
                    F.Order,
                    d =>
                    {
                        var a = Table("a", Pk("id", IntType[d]));
                        var b = Table("b", Pk("id", IntType[d]), Col("a_id", IntType[d]));
                        var c = Table("c", Pk("id", IntType[d]), Col("a_id", IntType[d]));
                        var diagram = Diagram(a, b, c);
                        diagram.Relationships.Add(CvLink(a, b, 1, "FK_b_a"));
                        diagram.Relationships.Add(
                            new Relationship
                            {
                                SourceEntityId = a.Id,
                                TargetEntityId = c.Id,
                                Type = RelationshipType.ManyToMany,
                            }
                        );
                        diagram.Relationships.Add(CvLink(a, c, 1, "FK_c_a"));
                        return diagram;
                    }
                )
                .Expect(
                    [Ss, Pg, My, Or],
                    e =>
                        e.Order(
                            O.Lines,
                            "fk b FK_b_a (a_id) -> a (id) alter",
                            "note many-to-many | -- Many-to-many (a <-> c)*",
                            "fk c FK_c_a (a_id) -> a (id) alter"
                        )
                )
                .Expect(
                    Sl,
                    e =>
                        e.Order(
                            O.Lines,
                            "fk b FK_b_a (a_id) -> a (id) inline",
                            "fk c FK_c_a (a_id) -> a (id) inline",
                            "note many-to-many | -- Many-to-many (a <-> c)*"
                        )
                ),
            // 行 47
            DialectDdlRow
                .Ddl(
                    "ddl-many-to-many-constraint-name-with-newline-is-rejected",
                    "外部キーを作らない多対多でも、制約名に改行があれば DDL の入口が断る（src/QuickER.Provider/CLAUDE.md「SQL の信頼境界」の SqlNameText.DiagramNames）",
                    E.ManyToMany,
                    F.Reject,
                    d => CvManyToMany(d, withPairs: true, name: CcInjected)
                )
                .ExpectAll(e => e.Throws("InvalidOperationException: *FOREIGN KEY*")),
            // 行 48
            DialectDdlRow
                .SyncItems(
                    "sync-rebuild-non-numeric-argument-type-is-quoted-as-a-whole",
                    "特性の固定（文書に定めが無い今の振る舞い）：SQLite の再構築の列定義は、数値でない引数を持つ型（nvarchar(max)）の型名全体を二重引用符で囲む。根拠の実装は src/QuickER.Provider.Sqlite/SqliteDdlGenerator.cs の FormatDeclaredType",
                    P.SyncRebuild,
                    E.Type,
                    F.Quote,
                    _ =>
                    {
                        var live = Table("t", Pk("id", "INTEGER"), Col("v", "INTEGER"));
                        return CvLiveItems(
                            [live],
                            [],
                            CvAlterColumn("t", Col("v", "nvarchar(max)", nullable: true))
                        );
                    }
                )
                .Expect(Sl, e => e.Lines("column t_quicker_rebuild.v \"nvarchar(max)\" NULL*")),
            // 行 49
            DialectDdlRow
                .SyncDiff(
                    "sync-other-foreign-key-one-end-type-change-warns-type-mismatch",
                    "外部キーの片側だけの型の変更は、外部キーを外し直さず ForeignKeyColumnTypeMismatch で名指しする（src/QuickER.Provider/CLAUDE.md「外部キーの暗黙の外し直し」。期待の行 plan-type-mismatch-one-side-warns-and-does-not-inject-child と同じ警告）",
                    P.SyncOther,
                    E.Type,
                    F.Note,
                    d =>
                    {
                        (List<Entity> Tables, List<Relationship> Links) Build(string childType)
                        {
                            var customer = Table("customer", Pk("id", IntType[d]));
                            var orders = Table(
                                "orders",
                                Pk("id", IntType[d]),
                                Col("customer_id", childType)
                            );
                            return (
                                [customer, orders],
                                [CvLink(customer, orders, 1, "FK_orders_customer")]
                            );
                        }

                        var (liveTables, liveLinks) = Build(IntType[d]);
                        var (targetTables, targetLinks) = Build(CvWideInt[d]);
                        return CvDiff(
                            liveTables,
                            liveLinks,
                            targetTables,
                            targetLinks,
                            items =>
                            {
                                foreach (var item in items)
                                {
                                    item.IsSelected = item.Kind == SchemaDiffKind.AlterColumn;
                                }
                            }
                        );
                    }
                )
                .Expect(
                    [Ss, My],
                    e =>
                        e.Contains(
                            O.Warnings,
                            "ForeignKeyColumnTypeMismatch orders FK_orders_customer"
                        )
                )
                .Expect([Pg, Or, Sl], e => e.Absent(O.Warnings, "ForeignKeyColumnTypeMismatch*")),
            // 行 50
            DialectDdlRow
                .SyncItems(
                    "sync-add-column-unsafe-data-type-is-rejected",
                    "列の追加の型に列定義の外へ出る文字列があれば、同期スクリプトの入口がテーブル.列つきで断る（src/QuickER.Provider/CLAUDE.md「SqlTypeText の安全パターン」）",
                    P.SyncAddColumn,
                    E.Type,
                    F.Reject,
                    _ => new DdlSyncItems(
                        [CvAddColumn("t", Col("memo", "int); DROP TABLE x;--"))],
                        TestDialectCapabilities.Neutral
                    )
                )
                .ExpectAll(e =>
                    e.Contains(
                        O.Exception,
                        "InvalidOperationException: *",
                        "*t.memo*",
                        "*DROP TABLE x*"
                    )
                ),
        ];
}
