using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using QuickER.Dialects;
using QuickER.Model;
using QuickER.Provider;
using QuickER.Tests.TestSupport.Engines;
using ProviderStrings = QuickER.Provider.Resources.Strings;

namespace QuickER.Tests.Integration.Dialects.SyncPlanFuzz;

internal static partial class SyncPlanExpectationRows
{
    /// <summary>SQLite の実 DB の段の行の数の下限（行を足したら・消したら合わせる）</summary>
    private const int RealSqliteRowCount = 48;

    /// <summary>実 DB の段：SQLite の同期（テーブル再構築・外部キー・主キー・一意制約・ビュー）</summary>
    private static IEnumerable<ScenarioRow<SyncPlanExpectationInput>> RealSqliteRows() =>
        [
            .. RealSqliteRebuildRows(),
            .. RealSqliteKeyRows(),
            .. RealSqliteViewRows(),
            .. RealSqliteScriptRows(),
            .. RealSqliteImportDiscardedRows(),
        ];

    /// <summary>
    /// 取込が捨てる表の中の物（前後の空白だけ違う列名の後着の列・ドット付きの表への外部キー・参照先の無い外部キー）× 表を作り直す項目
    /// （無関係な列の型の変更・外部キーの追加・列の削除）
    /// </summary>
    /// <remarks>
    /// 再構築は live の図から表を作り直すので、捨てた物は新しい表に無く、データごと黙って消える。計画はその表を作り直す項目を外して
    /// 名指しし、DB は変わらない（捨てた物も残る）。
    /// </remarks>
    private static IEnumerable<
        ScenarioRow<SyncPlanExpectationInput>
    > RealSqliteImportDiscardedRows()
    {
        // 捨てる物：表 t の追加の列の定義（行の値つき）・探針・探針の期待
        (string Id, string Columns, string Values, string Probe, string[] Expected)[] discards =
        [
            (
                "collided-column",
                "\"name\" TEXT NULL, \"name \" TEXT NULL",
                "'kept', 'precious'",
                "SELECT '[' || \"name\" || ']' AS c FROM pragma_table_info('t') ORDER BY cid;",
                ["[id]", "[note]", "[legacy]", "[pid]", "[name]", "[name ]"]
            ),
            (
                "fk-to-dotted-table",
                "\"bid\" INTEGER NULL, CONSTRAINT \"FK_t_ab\" FOREIGN KEY (\"bid\") REFERENCES \"a.b\" (\"id\")",
                "1",
                "SELECT \"table\" AS c FROM pragma_foreign_key_list('t');",
                ["a.b"]
            ),
            (
                "fk-to-missing-table",
                "\"zid\" INTEGER NULL, CONSTRAINT \"FK_t_zz\" FOREIGN KEY (\"zid\") REFERENCES \"zz\" (\"id\")",
                "NULL",
                "SELECT \"table\" AS c FROM pragma_foreign_key_list('t');",
                ["zz"]
            ),
        ];

        // 表 t を作り直す項目
        (string Id, System.Action<ExpectationSchema> Target)[] triggers =
        [
            ("type-change", s => s.Col("t", "note").DataType = "VARCHAR(200)"),
            ("add-fk", s => s.Fk("p", "t", null, ("id", "pid"))),
            ("drop-column", s => s["t"].Columns.RemoveAll(c => c.Name == "legacy")),
        ];

        foreach (var discard in discards)
        {
            foreach (var trigger in triggers)
            {
                yield return Row(
                        $"realdb-rebuild-skipped-when-import-discarded-{discard.Id}-{trigger.Id}",
                        $"[Integration] SQLite 同期: 取込が捨てた物（{discard.Id}）のある表は、作り直す項目（{trigger.Id}）を外して名指しし、捨てた物をデータごと消さない",
                        DialectNames.Sqlite
                    )
                    .TargetFromLive(trigger.Target)
                    .Select(RealScenarios.SelectAll)
                    .OnRealDatabase(r =>
                        r.LiveSql(
                                "CREATE TABLE \"p\" (\"id\" INTEGER NOT NULL, CONSTRAINT \"PK_p\" PRIMARY KEY (\"id\"));"
                                    + "CREATE TABLE \"a.b\" (\"id\" INTEGER NOT NULL PRIMARY KEY);"
                                    + "INSERT INTO \"p\" (\"id\") VALUES (1);"
                                    + "INSERT INTO \"a.b\" (\"id\") VALUES (1);"
                                    + "CREATE TABLE \"t\" (\"id\" INTEGER NOT NULL, \"note\" TEXT NULL, \"legacy\" INTEGER NULL, \"pid\" INTEGER NULL, "
                                    + discard.Columns
                                    + ", CONSTRAINT \"PK_t\" PRIMARY KEY (\"id\"));"
                                    + "INSERT INTO \"t\" VALUES (1, 'x', 9, 1, "
                                    + discard.Values
                                    + ");"
                            )
                            .Query("kept", discard.Probe)
                            .Expect(e =>
                            {
                                e.Outcome.Exact("committed");
                                e.Warnings.Single("TableRebuildLosesImportDiscardedContent(t): *");
                                e.Unchanged();
                                e.Probe("kept").Unordered(discard.Expected);
                            })
                    );
            }
        }
    }

    /// <summary>再構築で温存するもの・失うもの（行・補助オブジェクト・外部キー・列順・属性）</summary>
    private static IEnumerable<ScenarioRow<SyncPlanExpectationInput>> RealSqliteRebuildRows() =>
        [
            Row(
                    "realdb-rebuild-keeps-data-and-auxiliary-objects",
                    "[Integration] SQLite 同期: 型変更＋列削除の再構築で行データ・FK・索引・トリガー・UNIQUE を温存",
                    DialectNames.Sqlite
                )
                .TargetFromLive(s =>
                {
                    s.Col("product", "note").DataType = "VARCHAR(200)";
                    s["product"].Columns.RemoveAll(c => c.Name == "legacy_col");
                })
                .Select(RealScenarios.SelectAll)
                .OnRealDatabase(r =>
                    r.LiveSql(
                            "CREATE TABLE \"category\" (\"id\" INTEGER NOT NULL, \"name\" TEXT NULL, CONSTRAINT \"PK_category\" PRIMARY KEY (\"id\"));"
                                + "CREATE TABLE \"product\" ("
                                + "\"id\" INTEGER NOT NULL, "
                                + "\"category_id\" INTEGER NOT NULL, "
                                + "\"sku\" TEXT NOT NULL, "
                                + "\"note\" TEXT NULL, "
                                + "\"legacy_col\" INTEGER NULL, "
                                + "CONSTRAINT \"PK_product\" PRIMARY KEY (\"id\"), "
                                + "UNIQUE (\"sku\"), "
                                + "CONSTRAINT \"FK_product_category\" FOREIGN KEY (\"category_id\") REFERENCES \"category\" (\"id\") ON DELETE CASCADE"
                                + ");"
                                + "CREATE INDEX \"idx_product_note\" ON \"product\" (\"note\");"
                                + "CREATE TRIGGER \"trg_product_ai\" AFTER INSERT ON \"product\" BEGIN UPDATE \"category\" SET \"name\" = \"name\"; END;"
                                + "INSERT INTO \"category\" (\"id\", \"name\") VALUES (1, 'cat1');"
                                + "INSERT INTO \"product\" (\"id\", \"category_id\", \"sku\", \"note\", \"legacy_col\") VALUES (10, 1, 'SKU-10', 'hello', 99);"
                                + "INSERT INTO \"product\" (\"id\", \"category_id\", \"sku\", \"note\", \"legacy_col\") VALUES (11, 1, 'SKU-11', 'world', 100);"
                        )
                        .Query(
                            "rows",
                            "SELECT \"id\", \"category_id\", \"sku\", \"note\" FROM \"product\";"
                        )
                        .Expect(e =>
                        {
                            e.Script.Contains(
                                "-- ===== RebuildTable: product =====",
                                "CREATE TABLE \"product_quicker_rebuild\" (",
                                "*FOREIGN KEY (\"category_id\")*",
                                "*REFERENCES \"category\" (\"id\") ON DELETE CASCADE*",
                                "*CONSTRAINT \"UQ_product_sku\" UNIQUE (\"sku\")*",
                                "INSERT INTO \"product_quicker_rebuild\" (\"id\", \"category_id\", \"sku\", \"note\") "
                                    + "SELECT \"id\", \"category_id\", \"sku\", \"note\" FROM \"product\";",
                                "DROP TABLE \"product\";",
                                "ALTER TABLE \"product_quicker_rebuild\" RENAME TO \"product\";",
                                "CREATE INDEX \"idx_product_note\" ON \"product\" (\"note\");"
                            );
                            e.Outcome.Exact("committed");
                            e.After.Absent("col product.legacy_col *");
                            e.After.Single("col product.note VARCHAR(200) *");
                            e.After.Count("fk *", 1);
                            e.After.Single("fk * del=Cascade *");
                            e.After.Contains("aux * Index idx_product_note");
                            e.After.Contains("aux * Trigger trg_product_ai");
                            e.After.Count("uq product *", 1);
                            e.After.Single("uq product * (sku)");
                            e.Probe("rows")
                                .Unordered(
                                    "10 category_id=1 sku=SKU-10 note=hello",
                                    "11 category_id=1 sku=SKU-11 note=world"
                                );
                        })
                ),
            Row(
                    "realdb-rebuild-keeps-non-constraint-unique-indexes",
                    "[Integration] SQLite 同期: 再構築で制約でない一意索引（式・条件付き）を温存",
                    DialectNames.Sqlite
                )
                .TargetFromLive(s => s.Col("users", "note").DataType = "VARCHAR(200)")
                .Select(RealScenarios.SelectAll)
                .OnRealDatabase(r =>
                    r.LiveSql(
                            "CREATE TABLE users (id INTEGER PRIMARY KEY, email TEXT NOT NULL, deleted INTEGER NOT NULL, note TEXT NULL);"
                                + "CREATE UNIQUE INDEX ux_users_email ON users (email);"
                                + "CREATE UNIQUE INDEX ux_users_email_lower ON users (lower(email));"
                                + "CREATE UNIQUE INDEX ux_users_live_email ON users (email) WHERE deleted = 0;"
                                + "INSERT INTO users (id, email, deleted, note) VALUES (1, 'a@example.com', 0, 'x');"
                        )
                        .Probe("schema")
                        .Expect(e =>
                        {
                            e.Outcome.Exact("committed");
                            e.After.Single("col users.note VARCHAR(200) *");
                            e.After.Count("aux * Index *", 3);
                            e.After.Single(
                                "aux * Index ux_users_email",
                                "aux * Index ux_users_email_lower",
                                "aux * Index ux_users_live_email"
                            );
                            e.Probe("schema")
                                .Single(
                                    "schema index ux_users_live_email * sql=*WHERE deleted = 0*"
                                );
                            e.After.Absent("uq *");
                        })
                ),
            Row(
                    "realdb-rebuild-add-fk-succeeds",
                    "[Integration] SQLite 同期: FK 追加の再構築が成功する（整合データ）",
                    DialectNames.Sqlite
                )
                .TargetFromLive(s => s.Fk("category", "product", null, ("id", "category_id")))
                .Select(RealScenarios.SelectAll)
                .OnRealDatabase(r =>
                    r.LiveSql(RealSqliteScenarios.CategoryProductSql(withForeignKey: false))
                        .LiveSql(
                            "INSERT INTO \"category\" (\"id\") VALUES (1);"
                                + "INSERT INTO \"product\" (\"id\", \"category_id\") VALUES (10, 1);"
                        )
                        .Expect(e =>
                        {
                            e.Outcome.Exact("committed");
                            e.After.Count("fk product *", 1);
                        })
                ),
            Row(
                    "realdb-rebuild-drop-fk-removes-it",
                    "[Integration] SQLite 同期: FK 削除の再構築で FK が外れる",
                    DialectNames.Sqlite
                )
                .TargetFromLive(s => s.Relationships.Clear())
                .Select(RealScenarios.SelectAll)
                .OnRealDatabase(r =>
                    r.LiveSql(RealSqliteScenarios.CategoryProductSql(withForeignKey: true))
                        .LiveSql(
                            "INSERT INTO \"category\" (\"id\") VALUES (1);"
                                + "INSERT INTO \"product\" (\"id\", \"category_id\") VALUES (10, 1);"
                        )
                        .Expect(e =>
                        {
                            e.Outcome.Exact("committed");
                            e.After.Absent("fk product *");
                        })
                ),
            Row(
                    "realdb-rebuild-not-null-with-existing-null-rolls-back",
                    "[Integration] SQLite 同期: 既存 NULL がある列の NOT NULL 化はロールバックされ DB は無変更",
                    DialectNames.Sqlite
                )
                .TargetFromLive(s => s.Col("product", "note").IsNullable = false)
                .Select(RealScenarios.SelectAll)
                .OnRealDatabase(r =>
                    r.LiveSql(
                            "CREATE TABLE \"product\" (\"id\" INTEGER NOT NULL, \"note\" TEXT NULL, CONSTRAINT \"PK_product\" PRIMARY KEY (\"id\"));"
                                + "INSERT INTO \"product\" (\"id\", \"note\") VALUES (10, NULL);"
                        )
                        .ExpectsUnwarnedFailure(
                            "データの都合：既存の NULL 行がある列の NOT NULL 化は、計画が予言できないデータの都合で再構築の移送が失敗する"
                        )
                        .Query("rows", "SELECT \"id\", \"note\" FROM \"product\";")
                        .Expect(e =>
                        {
                            e.Outcome.Exact("failed: *NOT NULL*");
                            e.Unchanged();
                            e.After.Single("col product.note TEXT null");
                            e.Probe("rows").Single("10 note=-");
                        })
                ),
            Row(
                    "realdb-rebuild-fk-violation-rolls-back",
                    "[Integration] SQLite 同期: 孤立データへの FK 追加は foreign_key_check で失敗しロールバック",
                    DialectNames.Sqlite
                )
                .TargetFromLive(s => s.Fk("category", "product", null, ("id", "category_id")))
                .Select(RealScenarios.SelectAll)
                .OnRealDatabase(r =>
                    r.LiveSql(RealSqliteScenarios.CategoryProductSql(withForeignKey: false))
                        .LiveSql(
                            "INSERT INTO \"category\" (\"id\") VALUES (1);"
                                + "INSERT INTO \"product\" (\"id\", \"category_id\") VALUES (10, 999);"
                        )
                        .ExpectsUnwarnedFailure(
                            "データの都合：親の無い行（category_id=999）への外部キーの追加は、計画が予言できないデータの都合で foreign_key_check が断る"
                        )
                        .Expect(e =>
                        {
                            e.Outcome.Exact("failed: *product*");
                            e.Unchanged();
                            e.After.Absent("fk product *");
                        })
                ),
            Row(
                    "realdb-rebuild-reorder-with-insertion-in-one-sync",
                    "[Integration] SQLite 同期: 並び替え＋真ん中への列追加を 1 回で反映し列順・データを保つ",
                    DialectNames.Sqlite
                )
                .TargetFromLive(s =>
                {
                    var item = s["item"];
                    var id = s.Col("item", "id");
                    var a = s.Col("item", "a");
                    var b = s.Col("item", "b");
                    item.Columns.Clear();
                    item.Columns.Add(id);
                    item.Columns.Add(ExpectationSchema.Nullable("new_col", "TEXT"));
                    item.Columns.Add(b);
                    item.Columns.Add(a);
                })
                .Select(RealScenarios.SelectAll)
                .OnRealDatabase(r =>
                    r.LiveSql(
                            "CREATE TABLE \"item\" ("
                                + "\"id\" INTEGER NOT NULL, \"a\" INTEGER NULL, \"b\" INTEGER NULL, "
                                + "CONSTRAINT \"PK_item\" PRIMARY KEY (\"id\"));"
                                + "INSERT INTO \"item\" (\"id\", \"a\", \"b\") VALUES (1, 10, 20);"
                                + "INSERT INTO \"item\" (\"id\", \"a\", \"b\") VALUES (2, 30, 40);"
                        )
                        .Query("rows", "SELECT \"id\", \"a\", \"b\", \"new_col\" FROM \"item\";")
                        .Expect(e =>
                        {
                            e.Script.Contains(
                                "-- ===== RebuildTable: item =====",
                                "CREATE TABLE \"item_quicker_rebuild\" ("
                            );
                            e.Outcome.Exact("committed");
                            e.After.Exact(
                                "table item",
                                "col item.id *",
                                "col item.new_col *",
                                "col item.b *",
                                "col item.a *",
                                "pk item (id)"
                            );
                            e.Probe("rows")
                                .Unordered("1 a=10 b=20 new_col=-", "2 a=30 b=40 new_col=-");
                        })
                ),
            Row(
                    "realdb-rebuild-alter-pk-moves-key-and-keeps-rows",
                    "[Integration] SQLite 同期: 主キーを id から code へ付け替える再構築でデータを温存",
                    DialectNames.Sqlite
                )
                .TargetFromLive(s =>
                {
                    s.Col("pk_item", "id").IsPrimaryKey = false;
                    s.Col("pk_item", "code").IsPrimaryKey = true;
                })
                .Select(RealScenarios.SelectAll)
                .OnRealDatabase(r =>
                    r.LiveSql(
                            "CREATE TABLE \"pk_item\" ("
                                + "\"code\" TEXT NOT NULL, \"id\" INTEGER NOT NULL, "
                                + "CONSTRAINT \"PK_pk_item\" PRIMARY KEY (\"id\"));"
                                + "INSERT INTO \"pk_item\" (\"code\", \"id\") VALUES ('A-001', 1);"
                                + "INSERT INTO \"pk_item\" (\"code\", \"id\") VALUES ('A-002', 2);"
                        )
                        .Query("rows", "SELECT \"code\", \"id\" FROM \"pk_item\";")
                        .Expect(e =>
                        {
                            e.Outcome.Exact("committed");
                            e.Script.Contains("-- ===== RebuildTable: pk_item =====");
                            e.After.Single("col pk_item.code * pk*");
                            e.After.Absent("col pk_item.id * pk*");
                            e.Probe("rows").Exact("A-001 id=1", "A-002 id=2");
                        })
                ),
            Row(
                    "realdb-rebuild-unique-add-and-drop",
                    "[Integration] SQLite 同期: 一意制約の追加・削除がテーブル再構築で反映される",
                    DialectNames.Sqlite
                )
                .TargetFromLive(s =>
                {
                    s["uq_item"].UniqueConstraints.Clear();
                    s.Unique("uq_item", null, "code");
                })
                .Select(RealScenarios.SelectAll)
                .OnRealDatabase(r =>
                    r.LiveSql(
                            "CREATE TABLE \"uq_item\" ("
                                + "\"id\" INTEGER NOT NULL, "
                                + "\"code\" TEXT NOT NULL, "
                                + "\"legacy\" TEXT NULL, "
                                + "CONSTRAINT \"PK_uq_item\" PRIMARY KEY (\"id\"), "
                                + "UNIQUE (\"legacy\")"
                                + ");"
                                + "INSERT INTO \"uq_item\" (\"id\", \"code\", \"legacy\") VALUES (1, 'C-1', 'L-1');"
                        )
                        .Expect(e =>
                        {
                            e.Outcome.Exact("committed");
                            e.Script.Contains(
                                "-- ===== RebuildTable: uq_item =====",
                                "*CONSTRAINT \"UQ_uq_item_code\" UNIQUE (\"code\")*"
                            );
                            e.After.Count("uq uq_item *", 1);
                            e.After.Single("uq uq_item * (code)");
                        })
                ),
            Row(
                    "realdb-rebuild-composite-fk-child-keeps-pairs",
                    "[Integration] SQLite 同期: 複合外部キーの子テーブルも再構築でき構成列が失われない",
                    DialectNames.Sqlite
                )
                .TargetFromLive(s => s.Col("cfk_child", "note").DataType = "VARCHAR(200)")
                .Select(RealScenarios.SelectAll)
                .OnRealDatabase(r =>
                    r.LiveSql(
                            "CREATE TABLE \"cfk_parent\" ("
                                + "\"a\" INTEGER NOT NULL, \"b\" INTEGER NOT NULL, "
                                + "CONSTRAINT \"PK_cfk_parent\" PRIMARY KEY (\"a\", \"b\"));"
                                + "CREATE TABLE \"cfk_child\" ("
                                + "\"id\" INTEGER NOT NULL, "
                                + "\"a_ref\" INTEGER NOT NULL, "
                                + "\"b_ref\" INTEGER NOT NULL, "
                                + "\"note\" TEXT NULL, "
                                + "CONSTRAINT \"PK_cfk_child\" PRIMARY KEY (\"id\"), "
                                + "CONSTRAINT \"FK_cfk_child_cfk_parent\" FOREIGN KEY (\"a_ref\", \"b_ref\") "
                                + "REFERENCES \"cfk_parent\" (\"a\", \"b\"));"
                                + "INSERT INTO \"cfk_parent\" (\"a\", \"b\") VALUES (1, 2);"
                                + "INSERT INTO \"cfk_child\" (\"id\", \"a_ref\", \"b_ref\", \"note\") "
                                + "VALUES (10, 1, 2, 'hello');"
                        )
                        .Expect(e =>
                        {
                            e.Outcome.Exact("committed");
                            e.Script.Contains(
                                "-- ===== RebuildTable: cfk_child =====",
                                RealSqliteScenarios.CompositeForeignKeyClause
                            );
                            e.After.Single("col cfk_child.note VARCHAR(200) *");
                            e.After.Count("fk *", 1);
                            e.After.Single("fk cfk_child * (a_ref,b_ref) -> cfk_parent(a,b) *");
                        })
                ),
            Row(
                    "realdb-rebuild-composite-fk-add",
                    "[Integration] SQLite 同期: 複合外部キーの追加・削除がテーブル再構築で反映される",
                    DialectNames.Sqlite
                )
                .TargetFromLive(s =>
                    s.Fk(
                        "cfk_parent",
                        "cfk_child",
                        "FK_cfk_child_cfk_parent",
                        ("a", "a_ref"),
                        ("b", "b_ref")
                    )
                )
                .Select(RealScenarios.SelectAll)
                .OnRealDatabase(r =>
                    r.LiveSql(RealSqliteScenarios.CompositeParentChildSql(withForeignKey: false))
                        .Expect(e =>
                        {
                            e.Outcome.Exact("committed");
                            e.Script.Contains(RealSqliteScenarios.CompositeForeignKeyClause);
                            e.After.Count("fk *", 1);
                            e.After.Single("fk cfk_child * (a_ref,b_ref) -> cfk_parent(a,b) *");
                        })
                ),
            Row(
                    "realdb-rebuild-composite-fk-drop",
                    "[Integration] SQLite 同期: 複合外部キーの追加・削除がテーブル再構築で反映される",
                    DialectNames.Sqlite
                )
                .TargetFromLive(s => s.Relationships.Clear())
                .Select(RealScenarios.SelectAll)
                .OnRealDatabase(r =>
                    r.LiveSql(RealSqliteScenarios.CompositeParentChildSql(withForeignKey: true))
                        .Expect(e =>
                        {
                            e.Before.Count("fk *", 1);
                            e.Outcome.Exact("committed");
                            e.After.Absent("fk *");
                        })
                ),
            Row(
                    "realdb-rebuild-warns-and-drops-column-attributes",
                    "[Integration] SQLite 同期: 再構築で失われる列属性を警告し、実際に失われる",
                    DialectNames.Sqlite
                )
                .TargetFromLive(s => s.Col("item", "note").DataType = "VARCHAR(50)")
                .Select(RealScenarios.SelectAll)
                .OnRealDatabase(r =>
                    r.LiveSql(
                            "CREATE TABLE \"item\" ("
                                + "\"id\" INTEGER PRIMARY KEY AUTOINCREMENT, "
                                + "\"qty\" INTEGER NOT NULL DEFAULT 7, "
                                + "\"note\" TEXT NULL"
                                + ");"
                                + "INSERT INTO \"item\" (\"qty\", \"note\") VALUES (1, 'a');"
                        )
                        .Probe("schema")
                        .Expect(e =>
                        {
                            e.ProbeBefore("schema").Single("schema table item *");
                            e.Warnings.Count("TableRebuildDropsColumnAttribute(*", 1);
                            e.Warnings.Single(
                                "TableRebuildDropsColumnAttribute(item): AUTOINCREMENT, DEFAULT"
                            );
                            e.Outcome.Exact("committed");
                            e.Probe("schema").Single("schema table item * sql=*VARCHAR(50)*");
                            e.Probe("schema")
                                .Absent([
                                    .. RealSqliteScenarios.RebuildAttributeWords.Select(w =>
                                        $"schema table item *{w}*"
                                    ),
                                ]);
                        })
                ),
            Row(
                    "realdb-rebuild-warns-table-options-and-constraint-clauses",
                    "[Integration] SQLite 同期: WITHOUT ROWID・STRICT・DEFERRABLE・ON CONFLICT も再構築で失われることを警告する",
                    DialectNames.Sqlite
                )
                .TargetFromLive(s => s.Col("w", "note").DataType = "INT")
                .Select(RealScenarios.SelectAll)
                .OnRealDatabase(r =>
                    r.LiveSql(
                            "CREATE TABLE \"p\" (\"id\" INTEGER NOT NULL, CONSTRAINT \"PK_p\" PRIMARY KEY (\"id\"));"
                                + "CREATE TABLE \"w\" ("
                                + "\"id\" INTEGER NOT NULL, "
                                + "\"p_id\" INTEGER NULL, "
                                + "\"note\" TEXT NOT NULL ON CONFLICT ABORT, "
                                + "PRIMARY KEY (\"id\"), "
                                + "FOREIGN KEY (\"p_id\") REFERENCES \"p\" (\"id\") DEFERRABLE INITIALLY DEFERRED"
                                + ") WITHOUT ROWID, STRICT;"
                                + "INSERT INTO \"p\" (\"id\") VALUES (1);"
                                + "INSERT INTO \"w\" (\"id\", \"p_id\", \"note\") VALUES (1, 1, 'a');"
                        )
                        .Probe("schema")
                        .Expect(e =>
                        {
                            e.Warnings.Count("TableRebuildDropsColumnAttribute(*", 1);
                            e.Warnings.Single(
                                "TableRebuildDropsColumnAttribute(w): ON CONFLICT, DEFERRABLE, WITHOUT ROWID, STRICT"
                            );
                            e.Outcome.Exact("committed");
                            e.Probe("schema").Single("schema table w *");
                            e.Probe("schema")
                                .Absent(
                                    "schema table w *WITHOUT ROWID*",
                                    "schema table w *STRICT*",
                                    "schema table w *DEFERRABLE*",
                                    "schema table w *ON CONFLICT*"
                                );
                        })
                ),
        ];

    /// <summary>別の Guid の図での再構築の合成（一意制約・主キー・外部キー）と、選ばなかった変更・依存の欠け</summary>
    private static IEnumerable<ScenarioRow<SyncPlanExpectationInput>> RealSqliteKeyRows() =>
        [
            Row(
                    "realdb-rebuild-alter-column-keeps-unique",
                    "[Integration] SQLite 同期: 別 Guid の図で列の型を変えても一意制約は再構築後に残る",
                    DialectNames.Sqlite
                )
                .TargetFromLive(s => s.Col("uq_keep", "sku").DataType = "VARCHAR(50)")
                .Select(items => RealScenarios.SelectKinds(items, SchemaDiffKind.AlterColumn))
                .OnRealDatabase(r =>
                    r.LiveSql(
                            "CREATE TABLE \"uq_keep\" ("
                                + "\"id\" INTEGER NOT NULL, \"sku\" TEXT NOT NULL, "
                                + "CONSTRAINT \"PK_uq_keep\" PRIMARY KEY (\"id\"), UNIQUE (\"sku\"));"
                                + "INSERT INTO \"uq_keep\" (\"id\", \"sku\") VALUES (1, 'A');"
                        )
                        .Expect(e =>
                        {
                            e.Outcome.Exact("committed");
                            e.After.Single("col uq_keep.sku VARCHAR(50) *");
                            e.After.Count("uq uq_keep *", 1);
                            e.After.Single("uq uq_keep * (sku)");
                        })
                ),
            Row(
                    "realdb-rebuild-alter-column-keeps-composite-pk-order",
                    "[Integration] SQLite 同期: 別 Guid の図で複合主キーの列の型を変えても主キーの順序は変わらない",
                    DialectNames.Sqlite
                )
                .TargetFromLive(s => s.Col("pk_order", "a").DataType = "VARCHAR(10)")
                .Select(items => RealScenarios.SelectKinds(items, SchemaDiffKind.AlterColumn))
                .OnRealDatabase(r =>
                    r.LiveSql(
                            "CREATE TABLE \"pk_order\" ("
                                + "\"a\" TEXT NOT NULL, \"b\" TEXT NOT NULL, \"v\" TEXT NULL, "
                                + "CONSTRAINT \"PK_pk_order\" PRIMARY KEY (\"a\", \"b\"));"
                                + "INSERT INTO \"pk_order\" (\"a\", \"b\", \"v\") VALUES ('x', 'y', 'z');"
                        )
                        .Expect(e =>
                        {
                            e.Outcome.Exact("committed");
                            e.After.Single("pk pk_order (a,b)");
                        })
                ),
            Row(
                    "realdb-rebuild-alter-column-without-pk-selection-keeps-pk",
                    "[Integration] SQLite 同期: 主キー変更を選ばずに列の型だけを変えても主キーは変わらない",
                    DialectNames.Sqlite
                )
                .TargetFromLive(s =>
                {
                    var code = s.Col("pk_keep", "code");
                    code.DataType = "VARCHAR(20)";
                    code.IsPrimaryKey = true;
                })
                .Select(items => RealScenarios.SelectKinds(items, SchemaDiffKind.AlterColumn))
                .OnRealDatabase(r =>
                    r.LiveSql(
                            "CREATE TABLE \"pk_keep\" ("
                                + "\"id\" INTEGER NOT NULL, \"code\" TEXT NOT NULL, "
                                + "CONSTRAINT \"PK_pk_keep\" PRIMARY KEY (\"id\"));"
                                + "INSERT INTO \"pk_keep\" (\"id\", \"code\") VALUES (1, 'A');"
                        )
                        .Expect(e =>
                        {
                            e.Outcome.Exact("committed");
                            e.After.Single("col pk_keep.code VARCHAR(20) *");
                            e.After.Single("pk pk_keep (id)");
                        })
                ),
            Row(
                    "realdb-rebuild-add-pk-column-without-pk-selection-keeps-pk",
                    "[Integration] SQLite 同期: 主キー変更を選ばずに主キー指定の列を追加しても主キーは変わらない",
                    DialectNames.Sqlite
                )
                .TargetFromLive(s =>
                {
                    s.Col("pk_add", "note").DataType = "VARCHAR(10)";
                    s["pk_add"].Columns.Add(ExpectationSchema.Pk("k2", "TEXT"));
                })
                .Select(items =>
                    RealScenarios.SelectKinds(
                        items,
                        SchemaDiffKind.AlterColumn,
                        SchemaDiffKind.AddColumn
                    )
                )
                .OnRealDatabase(r =>
                    r.LiveSql(
                            "CREATE TABLE \"pk_add\" ("
                                + "\"id\" INTEGER NOT NULL, \"note\" TEXT NULL, "
                                + "CONSTRAINT \"PK_pk_add\" PRIMARY KEY (\"id\"));"
                        )
                        .Expect(e =>
                        {
                            e.Outcome.Exact("committed");
                            e.After.Contains("col pk_add.k2 *");
                            e.After.Single("pk pk_add (id)");
                        })
                ),
            Row(
                    "realdb-rebuild-alter-referenced-pk-warns-candidate-key-loss",
                    "[Integration] SQLite 同期: 参照される主キーの付け替えで候補キーを失う外部キーを警告する",
                    DialectNames.Sqlite
                )
                .TargetFromLive(s =>
                {
                    s.Col("ck_parent", "id").IsPrimaryKey = false;
                    s.Col("ck_parent", "code").IsPrimaryKey = true;
                    s["ck_parent"].PrimaryKeyColumnIds = [];
                })
                .Select(items => RealScenarios.SelectKinds(items, SchemaDiffKind.AlterPrimaryKey))
                .OnRealDatabase(r =>
                    r.LiveSql(
                            "CREATE TABLE \"ck_parent\" ("
                                + "\"id\" INTEGER NOT NULL, \"code\" TEXT NOT NULL, "
                                + "CONSTRAINT \"PK_ck_parent\" PRIMARY KEY (\"id\"));"
                                + "CREATE TABLE \"ck_child\" ("
                                + "\"id\" INTEGER NOT NULL, \"parent_id\" INTEGER NOT NULL, "
                                + "CONSTRAINT \"PK_ck_child\" PRIMARY KEY (\"id\"), "
                                + "CONSTRAINT \"FK_ck_child_ck_parent\" FOREIGN KEY (\"parent_id\") "
                                + "REFERENCES \"ck_parent\" (\"id\"));"
                                + "INSERT INTO \"ck_parent\" (\"id\", \"code\") VALUES (1, 'A');"
                                + "INSERT INTO \"ck_child\" (\"id\", \"parent_id\") VALUES (1, 1);"
                        )
                        .Expect(e =>
                        {
                            e.Warnings.Count("ForeignKeyRebuildMayLoseCandidateKey(*", 1);
                            e.Warnings.Single(
                                "ForeignKeyRebuildMayLoseCandidateKey(ck_child): FK_ck_child_ck_parent*"
                            );
                            e.Outcome.Exact("failed: *");
                            e.After.Single("pk ck_parent (id)");
                        })
                ),
            Row(
                    "realdb-rebuild-alter-fk-replaces-fk",
                    "[Integration] SQLite 同期: 参照アクションの変更は外部キー変更として 1 本に作り直される",
                    DialectNames.Sqlite
                )
                .TargetFromLive(s =>
                    s.Relationships[0].OnDelete = ForeignKeyReferentialAction.Cascade
                )
                .Select(items => RealScenarios.SelectKinds(items, SchemaDiffKind.AlterForeignKey))
                .OnRealDatabase(r =>
                    r.LiveSql(
                            "CREATE TABLE \"fa_parent\" (\"id\" INTEGER NOT NULL, CONSTRAINT \"PK_fa_parent\" PRIMARY KEY (\"id\"));"
                                + "CREATE TABLE \"fa_child\" ("
                                + "\"id\" INTEGER NOT NULL, \"parent_id\" INTEGER NOT NULL, "
                                + "CONSTRAINT \"PK_fa_child\" PRIMARY KEY (\"id\"), "
                                + "FOREIGN KEY (\"parent_id\") REFERENCES \"fa_parent\" (\"id\"));"
                                + "INSERT INTO \"fa_parent\" (\"id\") VALUES (1);"
                                + "INSERT INTO \"fa_child\" (\"id\", \"parent_id\") VALUES (1, 1);"
                        )
                        .Expect(e =>
                        {
                            e.Outcome.Exact("committed");
                            RealSqliteScenarios.SingleRebuild(e);
                            e.Rebuilds.Count("* fk *", 1);
                            e.After.Count("fk *", 1);
                            e.After.Single("fk fa_child * del=Cascade *");
                        })
                ),
            DropParentTableOnlyRow(childHasRows: true),
            DropParentTableOnlyRow(childHasRows: false),
            PrimaryKeyOnNewColumnRow(selectAddColumn: false),
            PrimaryKeyOnNewColumnRow(selectAddColumn: true),
            Row(
                    "realdb-fk-to-unselected-new-table-skipped-with-warning",
                    "[Integration] SQLite 同期: 選んでいない新しい表を参照する外部キーは外して警告する",
                    DialectNames.Sqlite
                )
                .TargetFromLive(s =>
                {
                    s.Table("np_parent", ExpectationSchema.Pk("id", "INTEGER"));
                    s["np_child"].Columns.Add(ExpectationSchema.Nullable("parent_id", "INTEGER"));
                    s.Fk("np_parent", "np_child", null, ("id", "parent_id"));
                })
                .Select(items =>
                    RealScenarios.SelectKinds(
                        items,
                        SchemaDiffKind.AddColumn,
                        SchemaDiffKind.AddForeignKey
                    )
                )
                .OnRealDatabase(r =>
                    r.LiveSql(
                            "CREATE TABLE \"np_child\" (\"id\" INTEGER NOT NULL, CONSTRAINT \"PK_np_child\" PRIMARY KEY (\"id\"));"
                                + "INSERT INTO \"np_child\" (\"id\") VALUES (1);"
                        )
                        .Expect(e =>
                        {
                            e.Outcome.Exact("committed");
                            e.Warnings.Exact("*: FK_np_child_np_parent (np_parent)");
                            e.After.Absent("import-warning *", "fk *");
                            e.After.Count("table *", 1);
                            e.After.Single("table np_child");
                        })
                ),
            Row(
                    "realdb-rebuild-alter-one-of-duplicated-fks-keeps-other",
                    "[Integration] SQLite 同期: 同じ列ペアの外部キー 2 本のうち 1 本だけ変えても、もう 1 本は残る",
                    DialectNames.Sqlite
                )
                .TargetFromLive(s =>
                    s
                        .Relationships.Single(r =>
                            r.OnDelete == ForeignKeyReferentialAction.NoAction
                        )
                        .OnUpdate = ForeignKeyReferentialAction.Cascade
                )
                .Select(items => RealScenarios.SelectKinds(items, SchemaDiffKind.AlterForeignKey))
                .OnRealDatabase(r =>
                    r.LiveSql(
                            "CREATE TABLE \"dp_parent\" (\"id\" INTEGER NOT NULL, CONSTRAINT \"PK_dp_parent\" PRIMARY KEY (\"id\"));"
                                + "CREATE TABLE \"dp_child\" ("
                                + "\"id\" INTEGER NOT NULL, \"parent_id\" INTEGER NOT NULL, "
                                + "CONSTRAINT \"PK_dp_child\" PRIMARY KEY (\"id\"), "
                                + "FOREIGN KEY (\"parent_id\") REFERENCES \"dp_parent\" (\"id\"), "
                                + "FOREIGN KEY (\"parent_id\") REFERENCES \"dp_parent\" (\"id\") ON DELETE CASCADE);"
                                + "INSERT INTO \"dp_parent\" (\"id\") VALUES (1);"
                                + "INSERT INTO \"dp_child\" (\"id\", \"parent_id\") VALUES (1, 1);"
                        )
                        .Expect(e =>
                        {
                            e.Outcome.Exact("committed");
                            e.After.Count("fk *", 2);
                            e.After.Single(
                                "fk * del=NoAction upd=Cascade",
                                "fk * del=Cascade upd=NoAction"
                            );
                        })
                ),
            Row(
                    "realdb-unique-on-pk-folded-no-repeat",
                    "[Integration] SQLite 同期: 主キーと同じ列集合の一意制約は差分にならず、同期を繰り返しても差分が出ない",
                    DialectNames.Sqlite
                )
                .TargetFromLive(s => s.Unique("uk_item", "UQ_uk_item_id", "id"))
                .Select(RealSqliteScenarios.SelectSelectable)
                .OnRealDatabase(r =>
                    r.LiveSql(
                            "CREATE TABLE \"uk_item\" (\"id\" INTEGER NOT NULL, \"name\" TEXT, CONSTRAINT \"PK_uk_item\" PRIMARY KEY (\"id\"));"
                        )
                        .Expect(e =>
                        {
                            e.Diff.Empty();
                            e.Rebuilds.Empty();
                            e.Outcome.Exact("committed");
                            e.AfterDiff.Empty();
                            e.After.Absent("uq *");
                        })
                ),
            Row(
                    "realdb-dotted-table-name-warns-and-fails",
                    "[Integration] SQLite 同期: ドットを含む表名は警告され、添付していないデータベースの表として失敗する",
                    DialectNames.Sqlite
                )
                .TargetFromLive(s => s.Table("sales.items", ExpectationSchema.Pk("id", "INTEGER")))
                .Select(items => RealScenarios.SelectKinds(items, SchemaDiffKind.AddTable))
                .OnRealDatabase(r =>
                    r.LiveSql(
                            "CREATE TABLE \"plain\" (\"id\" INTEGER NOT NULL, CONSTRAINT \"PK_plain\" PRIMARY KEY (\"id\"));"
                        )
                        .Expect(e =>
                        {
                            e.Warnings.Count("SchemaQualifiedTableName(*", 1);
                            e.Warnings.Single("SchemaQualifiedTableName(sales.items)*");
                            e.Outcome.Exact("failed: *unknown database*");
                        })
                ),
        ];

    /// <summary>親の表だけを消す同期（子に行があるか）</summary>
    private static ScenarioRow<SyncPlanExpectationInput> DropParentTableOnlyRow(
        bool childHasRows
    ) =>
        Row(
                "realdb-drop-parent-only-rebuilds-child-without-fk-"
                    + (childHasRows ? "with-rows" : "without-rows"),
                "[Integration] SQLite 同期: 親の表だけを消しても、子の表を作り直して外部キーを外す",
                DialectNames.Sqlite
            )
            .TargetFromLive(s =>
            {
                s.Entities.RemoveAll(e => e.TableName == "vp_parent");
                s.Relationships.Clear();
            })
            .Select(items => RealScenarios.SelectKinds(items, SchemaDiffKind.DropTable))
            .OnRealDatabase(r =>
                r.LiveSql(
                        "CREATE TABLE \"vp_parent\" (\"id\" INTEGER NOT NULL, CONSTRAINT \"PK_vp_parent\" PRIMARY KEY (\"id\"));"
                            + "CREATE TABLE \"vp_child\" ("
                            + "\"id\" INTEGER NOT NULL, \"parent_id\" INTEGER NOT NULL, "
                            + "CONSTRAINT \"PK_vp_child\" PRIMARY KEY (\"id\"), "
                            + "FOREIGN KEY (\"parent_id\") REFERENCES \"vp_parent\" (\"id\"));"
                            + "INSERT INTO \"vp_parent\" (\"id\") VALUES (1);"
                            + (
                                childHasRows
                                    ? "INSERT INTO \"vp_child\" (\"id\", \"parent_id\") VALUES (1, 1);"
                                    : string.Empty
                            )
                    )
                    .Expect(e =>
                    {
                        e.Outcome.Exact("committed");
                        RealSqliteScenarios.SingleRebuild(e);
                        e.Rebuilds.Single("vp_child rebuild");
                        e.After.Absent("import-warning *", "fk *");
                        e.After.Count("table *", 1);
                        e.After.Single("table vp_child");
                    })
            );

    /// <summary>新しい列を主キーにする変更（列の追加も選ぶか）</summary>
    private static ScenarioRow<SyncPlanExpectationInput> PrimaryKeyOnNewColumnRow(
        bool selectAddColumn
    ) =>
        Row(
                "realdb-pk-on-new-column-"
                    + (
                        selectAddColumn
                            ? "with-add-column-applies"
                            : "without-add-column-is-excluded"
                    ),
                "[Integration] SQLite 同期: 新しい列を主キーにする変更は、列の追加を選ばなければ外して警告する",
                DialectNames.Sqlite
            )
            .TargetFromLive(s =>
            {
                var c = ExpectationSchema.NotNull("c", "INTEGER");
                s["pk_part"].Columns.Add(c);
                s.Col("pk_part", "a").IsPrimaryKey = false;
                s.Col("pk_part", "b").IsPrimaryKey = true;
                c.IsPrimaryKey = true;
                s.PrimaryKeyOrder("pk_part", "b", "c");
            })
            .Select(items =>
                RealScenarios.SelectKinds(
                    items,
                    selectAddColumn
                        ? [SchemaDiffKind.AlterPrimaryKey, SchemaDiffKind.AddColumn]
                        : [SchemaDiffKind.AlterPrimaryKey]
                )
            )
            .OnRealDatabase(r =>
                r.LiveSql(
                        "CREATE TABLE \"pk_part\" (\"a\" INTEGER NOT NULL, \"b\" INTEGER NOT NULL, "
                            + "CONSTRAINT \"PK_pk_part\" PRIMARY KEY (\"a\"));"
                    )
                    .Expect(e =>
                    {
                        e.Outcome.Exact("committed");

                        if (selectAddColumn)
                        {
                            e.Warnings.Empty();
                            e.After.Single("pk pk_part (b,c)");
                        }
                        else
                        {
                            e.Warnings.Exact("*: PRIMARY KEY (pk_part.c)");
                            e.After.Single("pk pk_part (a)");
                        }
                    })
            );

    /// <summary>作り直す表を参照するビュー・同期の前から在る外部キー違反と再構築</summary>
    private static IEnumerable<ScenarioRow<SyncPlanExpectationInput>> RealSqliteViewRows() =>
        [
            Row(
                    "realdb-rebuild-with-dependent-view-succeeds",
                    "[Integration] SQLite 同期: 作り直す表を参照するビューがあっても再構築が成功しビューは新しい表を指す",
                    DialectNames.Sqlite
                )
                .TargetFromLive(s => s.Col("vw_item", "note").DataType = "VARCHAR(50)")
                .Select(items => RealScenarios.SelectKinds(items, SchemaDiffKind.AlterColumn))
                .OnRealDatabase(r =>
                    r.LiveSql(
                            "CREATE TABLE \"vw_item\" (\"id\" INTEGER NOT NULL, \"note\" TEXT NULL, CONSTRAINT \"PK_vw_item\" PRIMARY KEY (\"id\"));"
                                + "INSERT INTO \"vw_item\" (\"id\", \"note\") VALUES (1, 'a');"
                                + "CREATE VIEW \"vw_notes\" AS SELECT \"id\", \"note\" FROM \"vw_item\";"
                        )
                        // もともと壊れているビュー（実行器は壊れたビューの増加を断るので、検査を通さずに作る）と、
                        // 作り直す表へ書き込む別の表のトリガー
                        .RawSetup(
                            "CREATE VIEW \"vw_pre_broken\" AS SELECT \"x\" FROM \"no_such_table\";"
                                + "CREATE TABLE \"vw_log\" (\"id\" INTEGER NOT NULL, CONSTRAINT \"PK_vw_log\" PRIMARY KEY (\"id\"));"
                                + "CREATE TRIGGER \"trg_vw_log\" AFTER INSERT ON \"vw_log\" BEGIN "
                                + "INSERT INTO \"vw_item\" (\"id\", \"note\") VALUES (NEW.\"id\" + 100, 'from trigger'); END;"
                        )
                        .Query("view", "SELECT COUNT(*) FROM \"vw_notes\" WHERE \"note\" = 'a';")
                        .Query("trigger-insert", "INSERT INTO \"vw_log\" (\"id\") VALUES (1);")
                        .Query(
                            "view-after-trigger",
                            "SELECT COUNT(*) FROM \"vw_notes\" WHERE \"note\" = 'from trigger';"
                        )
                        .Expect(e =>
                        {
                            e.Outcome.Exact("committed");
                            e.Probe("view").Exact("1");
                            e.Probe("trigger-insert").Empty();
                            e.Probe("view-after-trigger").Exact("1");
                            e.After.Single("col vw_item.note VARCHAR(50) *");
                        })
                ),
            Row(
                    "realdb-existing-fk-violations-unrelated-add-column-succeeds",
                    "[Integration] SQLite 同期: 既存の外部キー違反があっても無関係な同期は成功し、違反が増えるときだけ失敗する",
                    DialectNames.Sqlite
                )
                .TargetFromLive(s =>
                    s["ov_parent"].Columns.Add(ExpectationSchema.Nullable("memo", "TEXT"))
                )
                .Select(items => RealScenarios.SelectKinds(items, SchemaDiffKind.AddColumn))
                .OnRealDatabase(r =>
                    r.LiveSql(RealSqliteScenarios.ExistingViolationTablesSql(afterEarlierSyncs: 0))
                        .RawSetup(RealSqliteScenarios.ExistingViolationRowsSql)
                        .Expect(e => e.Outcome.Exact("committed"))
                ),
            Row(
                    "realdb-existing-fk-violations-child-rebuild-succeeds",
                    "[Integration] SQLite 同期: 既存の外部キー違反があっても無関係な同期は成功し、違反が増えるときだけ失敗する",
                    DialectNames.Sqlite
                )
                .TargetFromLive(s => s.Col("ov_child", "note").DataType = "VARCHAR(20)")
                .Select(items => RealScenarios.SelectKinds(items, SchemaDiffKind.AlterColumn))
                .OnRealDatabase(r =>
                    r.LiveSql(RealSqliteScenarios.ExistingViolationTablesSql(afterEarlierSyncs: 1))
                        .RawSetup(RealSqliteScenarios.ExistingViolationRowsSql)
                        .Expect(e => e.Outcome.Exact("committed"))
                ),
            Row(
                    "realdb-existing-fk-violations-new-violation-fails",
                    "[Integration] SQLite 同期: 既存の外部キー違反があっても無関係な同期は成功し、違反が増えるときだけ失敗する",
                    DialectNames.Sqlite
                )
                .TargetFromLive(s => s.Fk("ov_parent", "ov_other", null, ("id", "ref")))
                .Select(items => RealScenarios.SelectKinds(items, SchemaDiffKind.AddForeignKey))
                .OnRealDatabase(r =>
                    r.LiveSql(RealSqliteScenarios.ExistingViolationTablesSql(afterEarlierSyncs: 2))
                        .RawSetup(RealSqliteScenarios.ExistingViolationRowsSql)
                        .ExpectsUnwarnedFailure(
                            "データの都合：親の無い行（ov_other.ref=999）への外部キーの追加は、計画が予言できないデータの都合で foreign_key_check が断る"
                        )
                        .Expect(e =>
                        {
                            e.Outcome.Exact("failed: *ov_other*");
                            e.Unchanged();
                        })
                ),
            Row(
                    "realdb-rebuild-breaking-view-rolls-back",
                    "[Integration] SQLite 同期: 再構築でビューが壊れるならビューを名指ししてロールバックする",
                    DialectNames.Sqlite
                )
                .TargetFromLive(s => s["vw_item"].Columns.RemoveAll(c => c.Name == "note"))
                .Select(items => RealScenarios.SelectKinds(items, SchemaDiffKind.DropColumn))
                .OnRealDatabase(r =>
                    r.LiveSql(
                            "CREATE TABLE \"vw_item\" (\"id\" INTEGER NOT NULL, \"note\" TEXT NULL, CONSTRAINT \"PK_vw_item\" PRIMARY KEY (\"id\"));"
                                + "CREATE VIEW \"vw_notes\" AS SELECT \"id\", \"note\" FROM \"vw_item\";"
                        )
                        .ExpectsUnwarnedFailure(
                            "モデルの外の物：計画が見ないビュー vw_notes が壊れ、実行器が後から検出して巻き戻す（docs の SQLite の再構築の節の仕様）"
                        )
                        .Expect(e =>
                        {
                            e.Outcome.Exact(
                                "failed: "
                                    + string.Format(
                                        CultureInfo.InvariantCulture,
                                        ProviderStrings.Sync_Error_SqliteBrokenView,
                                        "vw_notes"
                                    )
                            );
                            e.Unchanged();
                            e.After.Contains("col vw_item.note *");
                        })
                ),
        ];

    /// <summary>同期スクリプトの文の形（新しい表・写す列の無い再構築・逐次の文・型の引用・主キーの順序）</summary>
    private static IEnumerable<ScenarioRow<SyncPlanExpectationInput>> RealSqliteScriptRows() =>
        [
            Row(
                    "realdb-sqlite-script-create-only-inline-fk",
                    "CreateOnly は FK 句インラインの CREATE のみ",
                    DialectNames.Sqlite
                )
                .Live(s => s.Table("orders", ExpectationSchema.Pk("id", "int")))
                .TargetFromLive(s =>
                {
                    s.Table(
                        "invoice",
                        ExpectationSchema.Pk("id", "int"),
                        ExpectationSchema.Nullable("orders_id", "int")
                    );
                    s.Fk("orders", "invoice", "FK_invoice_orders", ("id", "orders_id"));
                })
                .Select(RealScenarios.SelectAll)
                .OnRealDatabase(r =>
                    r.Expect(e =>
                    {
                        e.Rebuilds.Single("invoice create-only");
                        e.Rebuilds.Absent("* rebuild");
                        e.Script.Contains(
                            "CREATE TABLE \"invoice\" (",
                            "*FOREIGN KEY (\"orders_id\")*",
                            "*REFERENCES \"orders\" (\"id\")*"
                        );
                        e.Script.Absent("*INSERT INTO*", "*_quicker_rebuild*");
                        e.Outcome.Exact("committed");
                        e.After.Count("table *", 2);
                        e.After.Single(
                            "fk invoice * (orders_id) -> orders(id) del=NoAction upd=NoAction"
                        );
                    })
                ),
            RebuildWithoutCopyColumnsRow(nullable: true),
            RebuildWithoutCopyColumnsRow(nullable: false),
            Row(
                    "realdb-sqlite-script-add-column-and-drop-table-sections",
                    "AddColumn / DropTable は逐次セクションになる",
                    DialectNames.Sqlite
                )
                .Live(s =>
                {
                    s.Table("customer", ExpectationSchema.Pk("id", "int"));
                    s.Table("legacy", ExpectationSchema.Pk("id", "int"));
                })
                .TargetFromLive(s =>
                {
                    s["customer"].Columns.Add(ExpectationSchema.Nullable("email", "text"));
                    s.Entities.RemoveAll(e => e.TableName == "legacy");
                })
                .Select(RealScenarios.SelectAll)
                .OnRealDatabase(r =>
                    r.Expect(e =>
                    {
                        e.Rebuilds.Empty();
                        e.Script.Contains(
                            "ALTER TABLE \"customer\" ADD COLUMN \"email\" text NULL;",
                            "DROP TABLE \"legacy\";"
                        );
                        e.Outcome.Exact("committed");
                        e.After.Single("col customer.email TEXT null");
                        e.After.Absent("table legacy");
                    })
                ),
            Row(
                    "realdb-sqlite-script-rebuild-quotes-non-numeric-type-argument",
                    "NVARCHAR(MAX) 等の宣言型はクォートされる",
                    DialectNames.Sqlite
                )
                .Live(s =>
                    s.Table(
                        "orders",
                        ExpectationSchema.Pk("id", "int"),
                        ExpectationSchema.Nullable("payload", "TEXT")
                    )
                )
                .TargetFromLive(s => s.Col("orders", "payload").DataType = "NVARCHAR(MAX)")
                .Select(RealScenarios.SelectAll)
                .OnRealDatabase(r =>
                    r.Setup("INSERT INTO \"orders\" (\"id\", \"payload\") VALUES (1, 'p');")
                        .Query("rows", "SELECT \"id\", \"payload\" FROM \"orders\";")
                        .Expect(e =>
                        {
                            e.Script.Contains(
                                "-- ===== RebuildTable: orders =====",
                                "*\"payload\" \"NVARCHAR(MAX)\"*"
                            );
                            e.Outcome.Exact("committed");
                            e.After.Single("col orders.payload NVARCHAR(MAX) null");
                            e.Probe("rows").Exact("1 payload=p");
                        })
                ),
            Row(
                    "realdb-sqlite-script-create-only-pk-follows-primary-key-order",
                    "再構築の PRIMARY KEY 句は PrimaryKeyColumnIds の順で出力される",
                    DialectNames.Sqlite
                )
                .Live(_ => { })
                .TargetFromLive(s =>
                {
                    s.Table(
                        "pair",
                        ExpectationSchema.Pk("a", "int"),
                        ExpectationSchema.Pk("b", "int")
                    );
                    s.PrimaryKeyOrder("pair", "b", "a");
                })
                .Select(items => RealScenarios.SelectKinds(items, SchemaDiffKind.AddTable))
                .OnRealDatabase(r =>
                    r.Expect(e =>
                    {
                        e.Rebuilds.Single("pair create-only");
                        e.Script.Contains("*PRIMARY KEY (\"b\", \"a\")*");
                        e.Outcome.Exact("committed");
                        e.After.Single("pk pair (b,a)");
                    })
                ),
        ];

    /// <summary>元の列をすべて消して新しい列を足す再構築（新しい最初の列が NULL 許容か）</summary>
    private static ScenarioRow<SyncPlanExpectationInput> RebuildWithoutCopyColumnsRow(
        bool nullable
    ) =>
        Row(
                "realdb-sqlite-script-rebuild-without-copy-columns-"
                    + (nullable ? "keeps-rows-with-nulls" : "not-null-first-column-fails"),
                "写す列が無い再構築も、最初の列へ NULL を入れて行を写す",
                DialectNames.Sqlite
            )
            .Live(s =>
                s.Table(
                    "t",
                    ExpectationSchema.Nullable("old_a", "TEXT"),
                    ExpectationSchema.Nullable("old_b", "INTEGER")
                )
            )
            .TargetFromLive(s =>
            {
                var t = s["t"];
                t.Columns.Clear();
                t.Columns.Add(
                    nullable
                        ? ExpectationSchema.Nullable("added_a", "varchar(30)")
                        : ExpectationSchema.NotNull("added_a", "varchar(30)")
                );
                t.Columns.Add(ExpectationSchema.Nullable("added_b", "int"));
            })
            .Select(RealScenarios.SelectAll)
            .OnRealDatabase(r =>
            {
                r.Setup(
                    "INSERT INTO \"t\" (\"old_a\", \"old_b\") VALUES ('x', 1);"
                        + "INSERT INTO \"t\" (\"old_a\", \"old_b\") VALUES ('y', 2);"
                );

                if (nullable)
                {
                    r.Query("rows", "SELECT \"added_a\", \"added_b\" FROM \"t\";");
                }
                else
                {
                    r.ExpectsUnwarnedFailure(
                        "データの都合：行のある表で、写す列の無い再構築が NOT NULL の新しい最初の列へ NULL を入れると SQLite が断る"
                    );
                }

                r.Expect(e =>
                {
                    e.Script.Contains(
                        "INSERT INTO \"t_quicker_rebuild\" (\"added_a\") SELECT NULL FROM \"t\";"
                    );

                    if (nullable)
                    {
                        e.Outcome.Exact("committed");
                        e.After.Exact(
                            "table t",
                            "col t.added_a varchar(30) null",
                            "col t.added_b INT null"
                        );
                        e.Probe("rows").Exact("- added_b=-", "- added_b=-");
                    }
                    else
                    {
                        e.Outcome.Exact("failed: *NOT NULL*");
                        e.Unchanged();
                    }
                });
            });
}

/// <summary>SQLite の実 DB の段の図・SQL・選び方の部品</summary>
internal static class RealSqliteScenarios
{
    /// <summary>
    /// ov_parent(id)・ov_child(id, parent_id → ov_parent.id, note)・ov_other(id, ref)。
    /// <paramref name="afterEarlierSyncs"/> は、同じ DB に前の同期を何回当てた後の形か（1 回目＝ov_parent に memo TEXT を足す・
    /// 2 回目＝ov_child.note を VARCHAR(20) にする）
    /// </summary>
    public static string ExistingViolationTablesSql(int afterEarlierSyncs) =>
        "CREATE TABLE \"ov_parent\" (\"id\" INTEGER NOT NULL, "
        + (afterEarlierSyncs >= 1 ? "\"memo\" TEXT NULL, " : string.Empty)
        + "CONSTRAINT \"PK_ov_parent\" PRIMARY KEY (\"id\"));"
        + "CREATE TABLE \"ov_child\" (\"id\" INTEGER NOT NULL, \"parent_id\" INTEGER NULL, "
        + (afterEarlierSyncs >= 2 ? "\"note\" VARCHAR(20) NULL, " : "\"note\" TEXT NULL, ")
        + "CONSTRAINT \"PK_ov_child\" PRIMARY KEY (\"id\"), "
        + "FOREIGN KEY (\"parent_id\") REFERENCES \"ov_parent\" (\"id\"));"
        + "CREATE TABLE \"ov_other\" (\"id\" INTEGER NOT NULL, \"ref\" INTEGER NULL, CONSTRAINT \"PK_ov_other\" PRIMARY KEY (\"id\"));";

    /// <summary>
    /// 外部キーを検査しない状態で入れる親の無い行（ov_child.parent_id=999＝同期の前から在る外部キー違反・ov_other.ref=999）。
    /// 実行器は外部キー違反の増加を断るので検査を通さずに流し、プールへ戻る接続の外部キー検査は元へ戻す
    /// </summary>
    public const string ExistingViolationRowsSql =
        "PRAGMA foreign_keys = OFF;"
        + "INSERT INTO \"ov_child\" (\"id\", \"parent_id\") VALUES (1, 999);"
        + "INSERT INTO \"ov_other\" (\"id\", \"ref\") VALUES (1, 999);"
        + "PRAGMA foreign_keys = ON;";

    /// <summary>
    /// category(id) と product(id, category_id)（<paramref name="withForeignKey"/> が真なら
    /// FK_product_category (category_id) → category(id) も）
    /// </summary>
    public static string CategoryProductSql(bool withForeignKey) =>
        "CREATE TABLE \"category\" (\"id\" INTEGER NOT NULL, CONSTRAINT \"PK_category\" PRIMARY KEY (\"id\"));"
        + (
            withForeignKey
                ? "CREATE TABLE \"product\" ("
                    + "\"id\" INTEGER NOT NULL, \"category_id\" INTEGER NOT NULL, "
                    + "CONSTRAINT \"PK_product\" PRIMARY KEY (\"id\"), "
                    + "CONSTRAINT \"FK_product_category\" FOREIGN KEY (\"category_id\") REFERENCES \"category\" (\"id\")"
                    + ");"
                : "CREATE TABLE \"product\" (\"id\" INTEGER NOT NULL, \"category_id\" INTEGER NOT NULL, CONSTRAINT \"PK_product\" PRIMARY KEY (\"id\"));"
        );

    /// <summary>
    /// 複合主キー cfk_parent(a, b) と cfk_child(id, a_ref, b_ref)（<paramref name="withForeignKey"/> が真なら
    /// FK_cfk_child_cfk_parent (a_ref, b_ref) → (a, b) も）と、親子 1 行ずつ
    /// </summary>
    public static string CompositeParentChildSql(bool withForeignKey) =>
        "CREATE TABLE \"cfk_parent\" ("
        + "\"a\" INTEGER NOT NULL, \"b\" INTEGER NOT NULL, "
        + "CONSTRAINT \"PK_cfk_parent\" PRIMARY KEY (\"a\", \"b\"));"
        + "CREATE TABLE \"cfk_child\" ("
        + "\"id\" INTEGER NOT NULL, "
        + "\"a_ref\" INTEGER NOT NULL, "
        + "\"b_ref\" INTEGER NOT NULL, "
        + "CONSTRAINT \"PK_cfk_child\" PRIMARY KEY (\"id\")"
        + (
            withForeignKey
                ? ", CONSTRAINT \"FK_cfk_child_cfk_parent\" FOREIGN KEY (\"a_ref\", \"b_ref\") "
                    + "REFERENCES \"cfk_parent\" (\"a\", \"b\")"
                : string.Empty
        )
        + ");"
        + "INSERT INTO \"cfk_parent\" (\"a\", \"b\") VALUES (1, 2);"
        + "INSERT INTO \"cfk_child\" (\"id\", \"a_ref\", \"b_ref\") VALUES (10, 1, 2);";

    /// <summary>再構築の CREATE TABLE が出す複合外部キーの句（同期スクリプトの行のパターン）</summary>
    public const string CompositeForeignKeyClause =
        "*FOREIGN KEY (\"a_ref\", \"b_ref\") REFERENCES \"cfk_parent\" (\"a\", \"b\")*";

    /// <summary>
    /// 再構築で失われる属性を見つける語（TableRebuildAttributeDetector が探す属性の語。作り直した CREATE 文に残っていないこと）
    /// </summary>
    public static IReadOnlyList<string> RebuildAttributeWords =>
        [
            "AUTOINCREMENT",
            "DEFAULT",
            "CHECK",
            "COLLATE",
            "GENERATED",
            " AS (",
            "ON CONFLICT",
            "DEFERRABLE",
            "WITHOUT ROWID",
            "STRICT",
        ];

    /// <summary>再構築がちょうど 1 つ（作るだけの表を含めて）</summary>
    public static void SingleRebuild(ExpectationBuilder e)
    {
        e.Rebuilds.Count("* rebuild", 1);
        e.Rebuilds.Absent("* create-only");
    }

    /// <summary>選べる項目をすべて選ぶ（選べない案内項目は選ばない）</summary>
    public static void SelectSelectable(IReadOnlyList<SchemaDiffItem> items)
    {
        foreach (var item in items)
        {
            item.IsSelected = item.IsSelectable;
        }
    }
}
