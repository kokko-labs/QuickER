using System;
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
    /// <summary>PostgreSQL の実 DB の段の行の数の下限（行を足したら・消したら合わせる）</summary>
    private const int RealPostgreSqlRowCount = 46;

    /// <summary>実 DB の段：PostgreSQL の同期</summary>
    private static IEnumerable<ScenarioRow<SyncPlanExpectationInput>> RealPostgreSqlRows() =>
        [
            .. RealPostgreSqlSchemaSyncRows(),
            .. RealPostgreSqlKeyRows(),
            .. RealPostgreSqlColumnRows(),
            .. RealPostgreSqlDescriptionRows(),
            .. RealPostgreSqlScriptRows(),
        ];

    /// <summary>スキーマ同期の往復（表・列・外部キーの追加と削除・一意制約・複合外部キー）</summary>
    private static IEnumerable<
        ScenarioRow<SyncPlanExpectationInput>
    > RealPostgreSqlSchemaSyncRows() =>
        [
            Row(
                    "realdb-pg-schema-sync-add-tables-fk-and-descriptions-from-empty",
                    "[Integration] C: スキーマ同期が Add/Alter/Drop/FK まで実 DB に適用される",
                    DialectNames.PostgreSql
                )
                .Live(_ => { })
                .Target(s => RealPostgreSqlScenarios.SchemaSyncParentChild(s, stage: 0))
                .OnRealDatabase(r =>
                    r.Expect(e =>
                    {
                        e.Diff.Contains(
                            "AddTable parent *",
                            "AddTable child *",
                            "AddForeignKey *",
                            "SetTableDescription parent *newdesc=\"親テーブルの説明\"*"
                        );
                        e.Outcome.Exact("committed");
                        e.After.Count("table *", 2);
                        e.After.Single(
                            "table parent",
                            "table child",
                            "desc parent \"親テーブルの説明\"",
                            "desc parent.name \"名称\""
                        );
                        e.After.Count("fk *", 1);
                        e.After.Single("fk child FK_child_parent * del=Cascade *");
                    })
                ),
            Row(
                    "realdb-pg-schema-sync-add-alter-drop-columns",
                    "[Integration] C: スキーマ同期が Add/Alter/Drop/FK まで実 DB に適用される",
                    DialectNames.PostgreSql
                )
                .Live(s => RealPostgreSqlScenarios.SchemaSyncParentChild(s, stage: 0))
                .TargetFromLive(s =>
                {
                    // 子は図を組み直す（型変更＋NULL→NOT NULL・列の追加・列の削除）。外部キーは維持
                    s.Relationships.Clear();
                    s.Entities.RemoveAll(e => e.TableName == "child");
                    s.Table(
                        "child",
                        ExpectationSchema.Pk("id", "integer"),
                        ExpectationSchema.Nullable("parent_id", "integer"),
                        ExpectationSchema.NotNull("to_alter", "varchar(100)"),
                        ExpectationSchema.Nullable("added", "integer")
                    );
                    s.Fk(
                        "parent",
                        "child",
                        "FK_child_parent",
                        ForeignKeyReferentialAction.Cascade,
                        ("id", "parent_id")
                    );
                })
                .Select(RealScenarios.SelectAll)
                .OnRealDatabase(r =>
                    r.Expect(e =>
                    {
                        e.Diff.Contains(
                            "AddColumn child.added *",
                            "AlterColumn child.to_alter *",
                            "DropColumn child.to_drop *"
                        );
                        // 型の変更（照合順序を読み直す DO ブロックの中・同じ系統なので USING なし）と NOT NULL は別の文
                        e.Script.Contains(
                            "*EXECUTE 'ALTER TABLE \"child\" ALTER COLUMN \"to_alter\" TYPE varchar(100)' || COALESCE(' COLLATE ' || coll, '') || '';",
                            "ALTER TABLE \"child\" ALTER COLUMN \"to_alter\" SET NOT NULL;",
                            "ALTER TABLE \"child\" DROP COLUMN \"to_drop\";"
                        );
                        e.Outcome.Exact("committed");
                        e.After.Contains("col child.added *");
                        e.After.Absent("col child.to_drop *");
                        e.After.Single("col child.to_alter varchar(100) notnull*");
                    })
                ),
            Row(
                    "realdb-pg-schema-sync-drop-named-fk",
                    "[Integration] C: スキーマ同期が Add/Alter/Drop/FK まで実 DB に適用される",
                    DialectNames.PostgreSql
                )
                .Live(s => RealPostgreSqlScenarios.SchemaSyncParentChild(s, stage: 1))
                .TargetFromLive(s => s.Relationships.Clear())
                .Select(items => RealScenarios.SelectKinds(items, SchemaDiffKind.DropForeignKey))
                .OnRealDatabase(r =>
                    r.Expect(e =>
                    {
                        e.Diff.Single(
                            "DropForeignKey *",
                            "DropForeignKey child fk=FK_child_parent *"
                        );
                        e.Script.Contains(
                            "ALTER TABLE \"child\" DROP CONSTRAINT IF EXISTS \"FK_child_parent\";"
                        );
                        e.Outcome.Exact("committed");
                        e.After.Absent("fk *");
                    })
                ),
            Row(
                    "realdb-pg-schema-sync-drop-table",
                    "[Integration] C: スキーマ同期が Add/Alter/Drop/FK まで実 DB に適用される",
                    DialectNames.PostgreSql
                )
                .Live(s => RealPostgreSqlScenarios.SchemaSyncParentChild(s, stage: 2))
                .TargetFromLive(s => s.Entities.RemoveAll(e => e.TableName == "parent"))
                .Select(items => RealScenarios.SelectKinds(items, SchemaDiffKind.DropTable))
                .OnRealDatabase(r =>
                    r.Expect(e =>
                    {
                        e.Diff.Single("DropTable parent *");
                        e.Script.Contains("DROP TABLE \"parent\";");
                        e.Outcome.Exact("committed");
                        e.After.Count("table *", 1);
                        e.After.Single("table child");
                    })
                ),
            Row(
                    "realdb-pg-uq-add",
                    "[Integration] C: 一意制約の追加・削除が実 DB へ反映される",
                    DialectNames.PostgreSql
                )
                .TargetFromLive(s => s.Unique("uq_item", null, "code"))
                .OnRealDatabase(r =>
                    r.LiveSql(RealPostgreSqlScenarios.UniqueTargetTableSql(withUnique: false))
                        .Expect(e =>
                        {
                            // 一意制約の追加は既定で選択される（制約を増やすだけで既存定義を壊さないため）
                            e.Diff.Single(
                                "AddUniqueConstraint uq_item *",
                                "AddUniqueConstraint uq_item * sel=1 *"
                            );
                            e.Outcome.Exact("committed");
                            e.After.Count("uq uq_item *", 1);
                            e.After.Single("uq uq_item * (code)");
                        })
                ),
            Row(
                    "realdb-pg-uq-drop",
                    "[Integration] C: 一意制約の追加・削除が実 DB へ反映される",
                    DialectNames.PostgreSql
                )
                .TargetFromLive(s => s["uq_item"].UniqueConstraints.Clear())
                .Select(items =>
                    RealScenarios.AlsoSelect(items, SchemaDiffKind.DropUniqueConstraint)
                )
                .OnRealDatabase(r =>
                    r.LiveSql(RealPostgreSqlScenarios.UniqueTargetTableSql(withUnique: true))
                        .Expect(e =>
                        {
                            e.Before.Single("uq uq_item UQ_uq_item_code (code)");
                            // 削除は破壊的のため既定では未選択
                            e.Diff.Single(
                                "DropUniqueConstraint uq_item *",
                                "DropUniqueConstraint uq_item * sel=0 *"
                            );
                            e.Outcome.Exact("committed");
                            e.After.Absent("uq *");
                        })
                ),
            Row(
                    "realdb-pg-fk-composite-add-keeps-pairs",
                    "[Integration] C: 複合外部キーの追加・削除が実 DB へ反映される",
                    DialectNames.PostgreSql
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
                .OnRealDatabase(r =>
                    r.LiveSql(
                            RealPostgreSqlScenarios.CompositeParentChildSql(withForeignKey: false)
                        )
                        .Expect(e =>
                        {
                            e.Diff.Single(
                                "AddForeignKey *",
                                "AddForeignKey * pairs=(a>a_ref,b>b_ref) *"
                            );
                            e.Outcome.Exact("committed");
                            // 再取込: 構成列を 2 組とも保った 1 本の外部キーとして戻る
                            e.After.Count("fk *", 1);
                            e.After.Single(
                                "fk cfk_child FK_cfk_child_cfk_parent (a_ref,b_ref) -> cfk_parent(a,b) *"
                            );
                        })
                ),
            Row(
                    "realdb-pg-fk-composite-drop",
                    "[Integration] C: 複合外部キーの追加・削除が実 DB へ反映される",
                    DialectNames.PostgreSql
                )
                .TargetFromLive(s => s.Relationships.Clear())
                .Select(items => RealScenarios.AlsoSelect(items, SchemaDiffKind.DropForeignKey))
                .OnRealDatabase(r =>
                    r.LiveSql(RealPostgreSqlScenarios.CompositeParentChildSql(withForeignKey: true))
                        .Expect(e =>
                        {
                            e.Diff.Single("DropForeignKey *");
                            e.Outcome.Exact("committed");
                            e.After.Absent("fk *");
                        })
                ),
            Row(
                    "realdb-pg-fk-composite-survives-pk-change",
                    "[Integration] C: 複合主キー変更に伴う複合外部キーの張り直しで構成列が失われない",
                    DialectNames.PostgreSql
                )
                .TargetFromLive(s => s.Col("cfk_parent", "code").IsPrimaryKey = false)
                .Select(items => RealScenarios.AlsoSelect(items, SchemaDiffKind.AlterPrimaryKey))
                .OnRealDatabase(r =>
                    r.LiveSql(
                            "CREATE TABLE \"cfk_parent\" (\"id\" integer NOT NULL, \"code\" integer NOT NULL, "
                                + "\"a\" integer NOT NULL, \"b\" integer NOT NULL, "
                                + "CONSTRAINT \"PK_cfk_parent\" PRIMARY KEY (\"id\", \"code\"), "
                                + "CONSTRAINT \"UQ_cfk_parent_a_b\" UNIQUE (\"a\", \"b\"));"
                                + "CREATE TABLE \"cfk_child\" (\"id\" integer NOT NULL, \"a_ref\" integer NOT NULL, "
                                + "\"b_ref\" integer NOT NULL, CONSTRAINT \"PK_cfk_child\" PRIMARY KEY (\"id\"), "
                                + "CONSTRAINT \"FK_cfk_child_cfk_parent\" FOREIGN KEY (\"a_ref\", \"b_ref\") "
                                + "REFERENCES \"cfk_parent\" (\"a\", \"b\"));"
                        )
                        .Expect(e =>
                        {
                            // 被参照列 (a, b) は同期後も一意制約として残るため、候補キー喪失の警告は出ない
                            e.Warnings.Empty();
                            e.Outcome.Exact("committed");
                            // 主キーは縮み、複合外部キーは構成列を保ったまま残る
                            e.After.Single("pk cfk_parent (id)");
                            e.After.Count("fk *", 1);
                            e.After.Single(
                                "fk cfk_child FK_cfk_child_cfk_parent (a_ref,b_ref) -> cfk_parent(a,b) *"
                            );
                        })
                ),
        ];

    /// <summary>主キーの付け替え（行の温存・外部キーの片側の長さだけの変更・実行時に逆引きする制約名）</summary>
    private static IEnumerable<ScenarioRow<SyncPlanExpectationInput>> RealPostgreSqlKeyRows() =>
        [
            Row(
                    "realdb-pg-pk-move-to-code-keeps-rows",
                    "[Integration] C: 主キーを id から code へ付け替えてもデータが温存される",
                    DialectNames.PostgreSql
                )
                .TargetFromLive(s =>
                {
                    // 列構成・型は変えず、主キーだけ id から code へ移す
                    s.Col("pk_item", "id").IsPrimaryKey = false;
                    s.Col("pk_item", "code").IsPrimaryKey = true;
                })
                .Select(items => RealScenarios.AlsoSelect(items, SchemaDiffKind.AlterPrimaryKey))
                .OnRealDatabase(r =>
                    r.LiveSql(
                            "CREATE TABLE \"pk_item\" (\"code\" varchar(20) NOT NULL, \"id\" integer NOT NULL, "
                                + "CONSTRAINT \"PK_pk_item\" PRIMARY KEY (\"id\"));"
                        )
                        .Setup(
                            "INSERT INTO \"pk_item\" (\"code\", \"id\") VALUES ('A-001', 1), ('A-002', 2);"
                        )
                        .Query("rows", "SELECT \"code\", \"id\" FROM \"pk_item\" ORDER BY \"id\";")
                        .Expect(e =>
                        {
                            e.Diff.Single("AlterPrimaryKey pk_item *");
                            e.Outcome.Exact("committed");
                            e.After.Single("pk pk_item (code)", "col pk_item.code * pk*");
                            e.After.Absent("col pk_item.id * pk*");
                            // 行データは失われない
                            e.Probe("rows").Exact("A-001 id=1", "A-002 id=2");
                        })
                ),
            Row(
                    "realdb-pg-pk-change-with-string-length-only-fk-keeps-fk",
                    "[Integration] C: 主キーの付け替えと外部キーの片側の長さだけの変更は、外部キーを保って成功する",
                    DialectNames.PostgreSql
                )
                .TargetFromLive(s =>
                {
                    // 親の主キーを code から id へ移し、code には一意制約を置く（外部キーの参照先は候補キーのまま）。
                    // 子の列は長さだけを変える
                    s.Col("lp_parent", "code").IsPrimaryKey = false;
                    s.Col("lp_parent", "id").IsPrimaryKey = true;
                    s.Unique("lp_parent", "UQ_lp_parent_code", "code");
                    s.Col("lp_child", "parent_code").DataType = "varchar(20)";
                })
                .Select(items =>
                    RealScenarios.SelectKinds(
                        items,
                        SchemaDiffKind.AlterPrimaryKey,
                        SchemaDiffKind.AlterColumn,
                        SchemaDiffKind.AddUniqueConstraint
                    )
                )
                .OnRealDatabase(r =>
                    r.LiveSql(
                            "CREATE TABLE \"lp_parent\" (\"id\" integer NOT NULL, \"code\" varchar(10) NOT NULL, "
                                + "CONSTRAINT \"PK_lp_parent\" PRIMARY KEY (\"code\"));"
                                + "CREATE TABLE \"lp_child\" (\"id\" integer NOT NULL, \"parent_code\" varchar(10) NOT NULL, "
                                + "CONSTRAINT \"PK_lp_child\" PRIMARY KEY (\"id\"), "
                                + "CONSTRAINT \"FK_lp_child\" FOREIGN KEY (\"parent_code\") REFERENCES \"lp_parent\" (\"code\"));"
                        )
                        .Setup(
                            "INSERT INTO \"lp_parent\" (\"id\", \"code\") VALUES (1, 'a');"
                                + "INSERT INTO \"lp_child\" (\"id\", \"parent_code\") VALUES (1, 'a');"
                        )
                        .Expect(e =>
                        {
                            e.Warnings.Empty();
                            e.Outcome.Exact("committed");
                            e.After.Single(
                                "col lp_child.parent_code varchar(20) *",
                                "col lp_parent.id * pk*"
                            );
                            e.After.Count("fk *", 1);
                            e.After.Single("fk lp_child FK_lp_child *");
                        })
                ),
            Row(
                    "realdb-pg-pk-alter-name-with-double-quote",
                    "[Integration] 表名と主キー名に \" を含む表でも主キーの付け替えが成功する",
                    DialectNames.PostgreSql
                )
                .TargetFromLive(s => RealScenarios.MovePrimaryKeyToCode(s, "a\"b"))
                .Select(items => RealScenarios.AlsoSelect(items, SchemaDiffKind.AlterPrimaryKey))
                .OnRealDatabase(r =>
                    // QuickER 自身の DDL がこの表に付ける主キー名と同じ形（PK_a"b）
                    r.LiveSql(
                            "CREATE TABLE \"a\"\"b\" (\"id\" integer NOT NULL, \"code\" integer NOT NULL, "
                                + "CONSTRAINT \"PK_a\"\"b\" PRIMARY KEY (\"id\"));"
                        )
                        .Probe("keys")
                        .Expect(e =>
                        {
                            e.Diff.Single("AlterPrimaryKey *");
                            // 実行時に読んだ主キー名（PK_a"b）は quote_ident を通して動的 SQL へ埋める（生の連結は無い）
                            e.Script.Contains("*quote_ident(pk_name)*");
                            e.Script.Absent("*|| pk_name*");
                            e.Outcome.Exact("committed");
                            // 主キーが id から code へ付け替わる
                            e.Probe("keys")
                                .Exact("keys public.a\"b kind=PK name=PK_a\"b columns=code");
                        })
                ),
            Row(
                    "realdb-pg-pk-alter-name-with-single-quote",
                    "[Integration] 主キー名に ' を含む表でも主キーの付け替えが成功する",
                    DialectNames.PostgreSql
                )
                .TargetFromLive(s => RealScenarios.MovePrimaryKeyToCode(s, "odd_orders"))
                .Select(items => RealScenarios.AlsoSelect(items, SchemaDiffKind.AlterPrimaryKey))
                .OnRealDatabase(r =>
                    r.LiveSql(
                            "CREATE TABLE \"odd_orders\" (\"id\" integer NOT NULL, \"code\" integer NOT NULL, "
                                + "CONSTRAINT \"pk_odd'name\" PRIMARY KEY (\"id\"));"
                        )
                        .Probe("keys")
                        .Expect(e =>
                        {
                            e.Diff.Single("AlterPrimaryKey *");
                            e.Outcome.Exact("committed");
                            // 旧主キーが外れて新主キーへ置き換わる
                            e.Probe("keys")
                                .Exact(
                                    "keys public.odd_orders kind=PK name=PK_odd_orders columns=code"
                                );
                        })
                ),
            Row(
                    "realdb-pg-pk-alter-public-table-keeps-same-named-table-in-other-schema",
                    "[Integration] ドット付きの表の主キーの付け替えが成功し、別スキーマの同名の表を取り違えない",
                    DialectNames.PostgreSql
                )
                .TargetFromLive(s => RealScenarios.MovePrimaryKeyToCode(s, "orders"))
                .Select(items => RealScenarios.AlsoSelect(items, SchemaDiffKind.AlterPrimaryKey))
                .OnRealDatabase(r =>
                    // sales.orders は取込の範囲（public）の外。主キーは code へ付け替えた後の形（PK_sales_orders）で置く
                    r.Prepare(
                            RealPostgreSqlScenarios.RecreateSalesSchemaSql
                                + "CREATE TABLE \"sales\".\"orders\" (\"id\" integer NOT NULL, \"code\" integer NOT NULL, "
                                + "CONSTRAINT \"PK_sales_orders\" PRIMARY KEY (\"code\"));"
                        )
                        .LiveSql(
                            "CREATE TABLE \"orders\" (\"id\" integer NOT NULL, \"code\" integer NOT NULL, "
                                + "CONSTRAINT \"pk_orders\" PRIMARY KEY (\"id\"));"
                        )
                        .Teardown(RealPostgreSqlScenarios.DropSalesSchemaSql)
                        .Probe("keys")
                        .Expect(e =>
                        {
                            e.Diff.Single("AlterPrimaryKey *");
                            e.Outcome.Exact("committed");
                            // public の orders だけが付け替わる
                            e.Probe("keys")
                                .Exact(
                                    "keys public.orders kind=PK name=PK_orders columns=code",
                                    "keys sales.orders kind=PK name=PK_sales_orders columns=code"
                                );
                        })
                ),
        ];

    /// <summary>列の変更（型の系統・短縮・生成列・照合順序・既定値・大文字小文字だけ違う名前）</summary>
    private static IEnumerable<ScenarioRow<SyncPlanExpectationInput>> RealPostgreSqlColumnRows() =>
        [
            Row(
                    "realdb-pg-column-alter-invalid-cast-rolls-back",
                    "[Integration] C: 不正な同期はロールバックされ Committed=false・スキーマ不変",
                    DialectNames.PostgreSql
                )
                .TargetFromLive(s => s.Col("t", "note").DataType = "integer")
                .Select(items => RealScenarios.SelectKinds(items, SchemaDiffKind.AlterColumn))
                .OnRealDatabase(r =>
                    // 文字列列 note に非数値データを入れておくと integer への変換が失敗する
                    r.LiveSql(
                            "CREATE TABLE \"t\" (\"id\" integer NOT NULL, \"note\" varchar(50) NULL, "
                                + "CONSTRAINT \"PK_t\" PRIMARY KEY (\"id\"));"
                        )
                        .Setup("INSERT INTO \"t\" (\"id\", \"note\") VALUES (1, 'abc');")
                        .ExpectsUnwarnedFailure(
                            "データの都合：note の値 'abc' は integer へ変換できない（計画は列の値を見ない）"
                        )
                        .Expect(e =>
                        {
                            e.Outcome.Exact(
                                RealPostgreSqlScenarios.RolledBack(
                                    "*invalid input syntax for type integer*"
                                )
                            );
                            // スキーマ不変（note は varchar(50) のまま）
                            e.Unchanged();
                            e.After.Single("col t.note varchar(50) *");
                        })
                ),
            Row(
                    "realdb-pg-column-alter-across-type-family-uses-cast",
                    "[Integration] C: 型の系統をまたぐ変更は USING で通り、文字列の短縮は切り詰めずに失敗する",
                    DialectNames.PostgreSql
                )
                .TargetFromLive(s => s.Col("u", "qty").DataType = "integer")
                .Select(items => RealScenarios.SelectKinds(items, SchemaDiffKind.AlterColumn))
                .OnRealDatabase(r =>
                    r.LiveSql(RealPostgreSqlScenarios.TypeFamilyTableSql(qtyType: "varchar(50)"))
                        .Setup(RealPostgreSqlScenarios.TypeFamilyRowSql(qty: "'42'"))
                        .Query("row", RealPostgreSqlScenarios.TypeFamilySelectSql)
                        .Expect(e =>
                        {
                            // USING で '42' を integer へ変換できる
                            e.Script.Contains(
                                "ALTER TABLE \"u\" ALTER COLUMN \"qty\" TYPE integer USING \"qty\"::integer;"
                            );
                            e.Outcome.Exact("committed");
                            e.After.Single("col u.qty integer *");
                            e.Probe("row").Exact("42 name=abcdefghij");
                        })
                ),
            Row(
                    "realdb-pg-column-shorten-never-truncates",
                    "[Integration] C: 型の系統をまたぐ変更は USING で通り、文字列の短縮は切り詰めずに失敗する",
                    DialectNames.PostgreSql
                )
                .TargetFromLive(s => s.Col("u", "name").DataType = "varchar(5)")
                .Select(items => RealScenarios.SelectKinds(items, SchemaDiffKind.AlterColumn))
                .OnRealDatabase(r =>
                    // 前提は qty を integer へ変えた後の DB
                    r.LiveSql(RealPostgreSqlScenarios.TypeFamilyTableSql(qtyType: "integer"))
                        .Setup(RealPostgreSqlScenarios.TypeFamilyRowSql(qty: "42"))
                        .Query("row", RealPostgreSqlScenarios.TypeFamilySelectSql)
                        .ExpectsUnwarnedFailure(
                            "データの都合：name の値 'abcdefghij' は varchar(5) に収まらない（計画は列の値を見ない）"
                        )
                        .Expect(e =>
                        {
                            // 長すぎる値は切り詰めずにエラーにする（USING を付けない）
                            e.Script.Contains(
                                "*EXECUTE 'ALTER TABLE \"u\" ALTER COLUMN \"name\" TYPE varchar(5)' || COALESCE(' COLLATE ' || coll, '') || '';"
                            );
                            e.Script.Absent("*USING*");
                            e.Outcome.Exact(RealPostgreSqlScenarios.RolledBack("*value too long*"));
                            e.Unchanged();
                            e.Probe("row").Exact("42 name=abcdefghij");
                        })
                ),
            Row(
                    "realdb-pg-computed-column-type-change-keeps-expression-and-not-null",
                    "[Integration] C: 生成列の型の変更は式を保って適用され、NOT NULL を外さない",
                    DialectNames.PostgreSql
                )
                .TargetFromLive(s =>
                {
                    var total = s.Col("gen_item", "total");
                    total.DataType = "bigint";
                    total.IsNullable = true;
                })
                .Select(items => RealScenarios.SelectKinds(items, SchemaDiffKind.AlterColumn))
                .OnRealDatabase(r =>
                    // 生成列は QuickER の DDL では作れない
                    r.LiveSql(
                            "CREATE TABLE gen_item (id integer NOT NULL PRIMARY KEY, price integer NOT NULL, "
                                + "total integer GENERATED ALWAYS AS (price + 1) STORED NOT NULL);"
                        )
                        .Setup("INSERT INTO gen_item (id, price) VALUES (1, 10);")
                        .Query("total", "SELECT total FROM gen_item;")
                        .Expect(e =>
                        {
                            e.Diff.Exact("AlterColumn gen_item.total * able=1 *");
                            e.Outcome.Exact("committed");
                            e.After.Single("col gen_item.total bigint notnull computed");
                            e.Probe("total").Exact("11");
                        })
                ),
            CollationRow(),
            Row(
                    "realdb-pg-column-type-kind-change-with-default-warns",
                    "[Integration] PostgreSQL 同期: 既定値のある列の型の種類の変更は事前に警告される",
                    DialectNames.PostgreSql
                )
                .TargetFromLive(s => s.Col("jd", "v").DataType = "integer")
                .Select(items => RealScenarios.SelectKinds(items, SchemaDiffKind.AlterColumn))
                .OnRealDatabase(r =>
                    r.LiveSql(
                            "CREATE TABLE \"jd\" (\"id\" integer NOT NULL PRIMARY KEY, \"v\" varchar(20) NOT NULL DEFAULT 'abc');"
                        )
                        .Expect(e =>
                        {
                            e.Warnings.Single(
                                "DefaultOnColumnTypeKindChange(*)*",
                                "DefaultOnColumnTypeKindChange(*): v (*"
                            );
                            // PostgreSQL は型の変更で既定値を新しい型へ変換するので、合わない既定値では同期が失敗して巻き戻る
                            e.Outcome.Exact("failed: *");
                            e.Unchanged();
                        })
                ),
            Row(
                    "realdb-pg-case-only-names-sync-to-lowercase-table",
                    "[Integration] PG: 小文字で格納された表へ大文字小文字だけ違う図を同期できる",
                    DialectNames.PostgreSql
                )
                .Target(RealPostgreSqlScenarios.MixedCaseOrders)
                .Select(RealPostgreSqlScenarios.SelectSelectable)
                .OnRealDatabase(r =>
                    r.LiveSql(
                            """
                            CREATE TABLE orders (
                                id integer NOT NULL,
                                name varchar(50) NULL,
                                code varchar(10) NOT NULL,
                                CONSTRAINT pk_orders PRIMARY KEY (id)
                            );
                            """
                        )
                        .Expect(e =>
                        {
                            e.Diff.Contains(
                                "AlterColumn *",
                                "AlterPrimaryKey *",
                                "SetTableDescription *",
                                "SetColumnDescription *"
                            );
                            e.Outcome.Exact("committed");
                            e.After.Count("table *", 1);
                            e.After.Single(
                                "table orders",
                                "desc orders \"orders\"",
                                "col orders.name varchar(100) *",
                                "desc orders.name \"order name\""
                            );
                            e.After.Single("pk orders *", "pk orders (code)");
                        })
                ),
        ];

    /// <summary>
    /// 型の既定と違う照合順序の列は型を変えても照合順序を保ち、既定の照合順序の列は既定のまま（取込の範囲の public の表）
    /// </summary>
    private static ExpectationRow CollationRow() =>
        Row(
                "realdb-pg-column-alter-keeps-non-default-collation-public-table",
                "[Integration] PostgreSQL 同期: 列の変更は型の既定と違う照合順序を保つ",
                DialectNames.PostgreSql
            )
            .TargetFromLive(s =>
            {
                s.Col("cl", "c").DataType = "varchar(40)";
                s.Col("cl", "d").DataType = "text";
            })
            .Select(items => RealScenarios.SelectKinds(items, SchemaDiffKind.AlterColumn))
            .OnRealDatabase(r =>
                r.Prepare(RealPostgreSqlScenarios.RecreateSalesSchemaSql)
                    .LiveSql(
                        "CREATE TABLE \"cl\" (\"id\" integer NOT NULL PRIMARY KEY, "
                            + "\"c\" varchar(20) COLLATE \"C\" NOT NULL, \"d\" varchar(20) NOT NULL);"
                    )
                    .Teardown(RealPostgreSqlScenarios.DropSalesSchemaSql)
                    .Probe("columns")
                    .Expect(e =>
                    {
                        e.ProbeBefore("columns").Single("columns public.cl.c collation=C *");
                        // 実行時に列の照合順序を読み、型の既定と違うときだけ COLLATE を書き添える（表は to_regclass で絞る）
                        e.Script.Contains(
                            "*SELECT CASE WHEN a.attcollation = 0 OR a.attcollation = t.typcollation THEN NULL",
                            "*ELSE quote_ident(n.nspname) || '.' || quote_ident(co.collname) END INTO coll",
                            "*WHERE a.attrelid = to_regclass('\"cl\"') AND a.attname = 'c' AND NOT a.attisdropped;"
                        );
                        e.Outcome.Exact("committed");
                        e.Probe("columns")
                            .Single(
                                "columns public.cl.c collation=C type=character varying(40)",
                                "columns public.cl.d collation=default type=text"
                            );
                    })
            );

    /// <summary>説明（表と列の COMMENT ON）の設定・更新・削除</summary>
    private static IEnumerable<
        ScenarioRow<SyncPlanExpectationInput>
    > RealPostgreSqlDescriptionRows() =>
        [
            PostgreSqlDescriptionRoundTripRow(stage: 0),
            PostgreSqlDescriptionRoundTripRow(stage: 1),
            PostgreSqlDescriptionRoundTripRow(stage: 2),
        ];

    /// <summary>
    /// 表と列の説明の設定（<paramref name="stage"/> 0）・更新（1）・削除（2）。DB の前提は前の段の同期の後と同じ説明を持つ表
    /// </summary>
    private static ExpectationRow PostgreSqlDescriptionRoundTripRow(int stage) =>
        Row(
                stage switch
                {
                    0 => "realdb-pg-description-set",
                    1 => "realdb-pg-description-update",
                    _ => "realdb-pg-description-delete",
                },
                "[Integration] B: COMMENT 同期で説明が設定→更新→削除まで往復する",
                DialectNames.PostgreSql
            )
            .TargetFromLive(s =>
            {
                var (table, column) = RealPostgreSqlScenarios.ItemsDescriptions(stage + 1);
                s["items"].Description = table;
                s.Col("items", "name").Description = column;
            })
            .Select(items =>
                RealScenarios.SelectKinds(
                    items,
                    SchemaDiffKind.SetTableDescription,
                    SchemaDiffKind.SetColumnDescription
                )
            )
            .OnRealDatabase(r =>
                r.LiveSql(RealPostgreSqlScenarios.ItemsTableSql(stage))
                    .Expect(e =>
                    {
                        var (table, column) = RealPostgreSqlScenarios.ItemsDescriptions(stage + 1);
                        e.Outcome.Exact("committed");

                        if (stage < 2)
                        {
                            // 説明は COMMENT ON の文字列リテラルで書き、' を二重化する
                            e.Script.Contains(
                                $"COMMENT ON TABLE \"items\" IS '{table.Replace("'", "''", StringComparison.Ordinal)}';",
                                $"COMMENT ON COLUMN \"items\".\"name\" IS '{column.Replace("'", "''", StringComparison.Ordinal)}';"
                            );
                            e.After.Single(
                                $"desc items \"{table}\"",
                                $"desc items.name \"{column}\""
                            );
                        }
                        else
                        {
                            // 空の説明は IS NULL で消す
                            e.Script.Contains(
                                "COMMENT ON TABLE \"items\" IS NULL;",
                                "COMMENT ON COLUMN \"items\".\"name\" IS NULL;"
                            );
                            e.After.Absent("desc *");
                        }
                    })
            );

    /// <summary>
    /// 同期スクリプトの文の形（表・列・外部キー・主キー・一意制約の文）を実 DB で流し、結果と流れた文の形を見る
    /// </summary>
    private static IEnumerable<ScenarioRow<SyncPlanExpectationInput>> RealPostgreSqlScriptRows() =>
        [
            .. RealPostgreSqlScriptTableAndColumnRows(),
            .. RealPostgreSqlScriptForeignKeyRows(),
            .. RealPostgreSqlScriptKeyRows(),
        ];

    /// <summary>表・列の追加と列の変更の文</summary>
    private static IEnumerable<
        ScenarioRow<SyncPlanExpectationInput>
    > RealPostgreSqlScriptTableAndColumnRows() =>
        [
            Row(
                    "realdb-pg-script-add-table-create",
                    "AddTable は CREATE TABLE と PK を含み GO を使わない",
                    DialectNames.PostgreSql
                )
                .Live(_ => { })
                .TargetFromLive(s =>
                    s.Table(
                        "customer",
                        ExpectationSchema.Pk("id", "integer"),
                        ExpectationSchema.Nullable("name", "varchar(50)")
                    )
                )
                .Select(items => RealScenarios.SelectKinds(items, SchemaDiffKind.AddTable))
                .OnRealDatabase(r =>
                    r.Expect(e =>
                    {
                        e.Script.Contains(
                            "CREATE TABLE \"customer\" (",
                            "*\"id\" integer NOT NULL,",
                            "*CONSTRAINT \"PK_customer\" PRIMARY KEY (\"id\")"
                        );
                        // スクリプトは 1 回で流す＝GO の行があれば構文エラーになる
                        e.Script.Absent("*GO*", "*UNIQUE*");
                        e.Outcome.Exact("committed");
                        e.After.Single(
                            "table customer",
                            "col customer.id integer notnull pk",
                            "col customer.name varchar(50) null",
                            "pk customer (id)"
                        );
                        e.After.Absent("uq *");
                    })
                ),
            Row(
                    "realdb-pg-script-add-table-unique-constraints-sync",
                    "AddTable の UNIQUE 句は DDL 生成と同形",
                    DialectNames.PostgreSql
                )
                .Live(_ => { })
                .TargetFromLive(RealPostgreSqlScenarios.Shops)
                .Select(items => RealScenarios.SelectKinds(items, SchemaDiffKind.AddTable))
                .OnRealDatabase(r =>
                    r.Expect(e =>
                    {
                        // 名前つきの単一列は名前のまま・名前なしの複合は合成名で宣言の並び。最後の制約行にカンマは付かない
                        e.Script.Contains(
                            "*CONSTRAINT \"PK_shops\" PRIMARY KEY (\"id\"),",
                            "*CONSTRAINT \"UQ_shops_code\" UNIQUE (\"code\"),",
                            "*CONSTRAINT \"UQ_shops_region_code\" UNIQUE (\"region\", \"code\")"
                        );
                        e.Outcome.Exact("committed");
                        e.After.Count("uq *", 2);
                        e.After.Single(
                            "uq shops UQ_shops_code (code)",
                            "uq shops UQ_shops_region_code (region,code)"
                        );
                    })
                ),
            Row(
                    "realdb-pg-script-add-table-unique-constraints-ddl",
                    "AddTable の UNIQUE 句は DDL 生成と同形",
                    DialectNames.PostgreSql
                )
                .Live(RealPostgreSqlScenarios.Shops)
                .Target(RealPostgreSqlScenarios.Shops)
                .Select(items => RealScenarios.SelectKinds(items))
                .OnRealDatabase(r =>
                    r.Expect(e =>
                    {
                        e.Before.Count("uq *", 2);
                        e.Before.Single(
                            "uq shops UQ_shops_code (code)",
                            "uq shops UQ_shops_region_code (region,code)"
                        );
                        e.Diff.Empty();
                        e.Outcome.Exact("committed");
                    })
                ),
            Row(
                    "realdb-pg-script-add-column-not-null",
                    "AddColumn は ALTER TABLE ADD COLUMN を生成する",
                    DialectNames.PostgreSql
                )
                .Live(s => s.Table("customer", ExpectationSchema.Pk("id", "integer")))
                .TargetFromLive(s =>
                    s["customer"].Columns.Add(ExpectationSchema.NotNull("email", "varchar(200)"))
                )
                .Select(items => RealScenarios.SelectKinds(items, SchemaDiffKind.AddColumn))
                .OnRealDatabase(r =>
                    r.Expect(e =>
                    {
                        e.Script.Contains(
                            "ALTER TABLE \"customer\" ADD COLUMN \"email\" varchar(200) NOT NULL;"
                        );
                        e.Outcome.Exact("committed");
                        e.After.Single("col customer.email varchar(200) notnull");
                    })
                ),
            Row(
                    "realdb-pg-script-unselected-add-column-not-emitted",
                    "選択されていない項目はスクリプトに含まれない",
                    DialectNames.PostgreSql
                )
                .Live(s => s.Table("customer", ExpectationSchema.Pk("id", "integer")))
                .TargetFromLive(s =>
                    s["customer"].Columns.Add(ExpectationSchema.Nullable("email", "varchar(200)"))
                )
                .Select(items => RealScenarios.SelectKinds(items))
                .OnRealDatabase(r =>
                    r.Expect(e =>
                    {
                        e.Diff.Single("AddColumn customer.email *");
                        e.Script.Absent("*email*");
                        e.Outcome.Exact("committed");
                        e.After.Absent("col customer.email *");
                    })
                ),
            Row(
                    "realdb-pg-script-alter-column-drop-not-null",
                    "AlterColumn は NULL 許容化で DROP NOT NULL を生成する",
                    DialectNames.PostgreSql
                )
                .Live(s =>
                    s.Table(
                        "customer",
                        ExpectationSchema.Pk("id", "integer"),
                        ExpectationSchema.NotNull("note", "text")
                    )
                )
                .TargetFromLive(s => s.Col("customer", "note").IsNullable = true)
                .Select(items => RealScenarios.SelectKinds(items, SchemaDiffKind.AlterColumn))
                .OnRealDatabase(r =>
                    r.Expect(e =>
                    {
                        e.Script.Contains(
                            "ALTER TABLE \"customer\" ALTER COLUMN \"note\" DROP NOT NULL;"
                        );
                        e.Outcome.Exact("committed");
                        e.After.Single("col customer.note text null");
                    })
                ),
            UsingCastRow("text-to-date", "text", "date", "'2024-01-02'", "2024-01-02"),
            UsingCastRow("integer-to-boolean", "integer", "boolean", "1", "true"),
            UsingCastRow(
                "varchar-to-uuid",
                "varchar(36)",
                "uuid",
                "'0f8fad5b-d9cb-469f-a165-70867728950e'",
                "0f8fad5b-d9cb-469f-a165-70867728950e"
            ),
            UsingCastRow("text-to-jsonb", "text", "jsonb", "'{\"a\": 1}'", "{\"a\": 1}"),
            Row(
                    "realdb-pg-script-alter-column-integer-to-varchar-fails-without-truncation",
                    "AlterColumn は型の系統をまたぐ変更にだけ USING を付ける",
                    DialectNames.PostgreSql
                )
                .Live(s =>
                    s.Table(
                        "uc",
                        ExpectationSchema.Pk("id", "integer"),
                        ExpectationSchema.Nullable("v", "integer")
                    )
                )
                .TargetFromLive(s => s.Col("uc", "v").DataType = "varchar(10)")
                .Select(items => RealScenarios.SelectKinds(items, SchemaDiffKind.AlterColumn))
                .OnRealDatabase(r =>
                    r.Setup("INSERT INTO \"uc\" (\"id\", \"v\") VALUES (1, -2147483648);")
                        .Query("row", "SELECT \"v\" FROM \"uc\";")
                        .ExpectsUnwarnedFailure(
                            "データの都合：v の値 -2147483648（11 文字）は varchar(10) に収まらない（計画は列の値を見ない）"
                        )
                        .Expect(e =>
                        {
                            // 文字列系への変更には USING を付けない（明示キャストは長すぎる値を黙って切り詰める）
                            e.Script.Contains(
                                "*EXECUTE 'ALTER TABLE \"uc\" ALTER COLUMN \"v\" TYPE varchar(10)' || COALESCE(' COLLATE ' || coll, '') || '';"
                            );
                            e.Script.Absent("*USING*");
                            e.Outcome.Exact(RealPostgreSqlScenarios.RolledBack("*value too long*"));
                            e.Unchanged();
                            e.Probe("row").Exact("-2147483648");
                        })
                ),
        ];

    /// <summary>
    /// 型の系統をまたぐ列の変更（<paramref name="oldType"/> → <paramref name="newType"/>）は USING 列::新型 で通る
    /// （USING が無いと代入キャストが無く断られる）
    /// </summary>
    private static ExpectationRow UsingCastRow(
        string shape,
        string oldType,
        string newType,
        string value,
        string expected
    ) =>
        Row(
                $"realdb-pg-script-alter-column-using-cast-{shape}",
                "AlterColumn は型の系統をまたぐ変更にだけ USING を付ける",
                DialectNames.PostgreSql
            )
            .Live(s =>
                s.Table(
                    "uc",
                    ExpectationSchema.Pk("id", "integer"),
                    ExpectationSchema.Nullable("v", oldType)
                )
            )
            .TargetFromLive(s => s.Col("uc", "v").DataType = newType)
            .Select(items => RealScenarios.SelectKinds(items, SchemaDiffKind.AlterColumn))
            .OnRealDatabase(r =>
                r.Setup($"INSERT INTO \"uc\" (\"id\", \"v\") VALUES (1, {value});")
                    .Query("row", "SELECT \"v\"::text AS \"v\" FROM \"uc\";")
                    .Expect(e =>
                    {
                        e.Script.Contains(
                            $"ALTER TABLE \"uc\" ALTER COLUMN \"v\" TYPE {newType} USING \"v\"::{newType};"
                        );
                        e.Outcome.Exact("committed");
                        e.After.Single($"col uc.v {newType} null");
                        e.Probe("row").Exact(expected);
                    })
            );

    /// <summary>外部キーの追加と、表・列の追加と外部キーの並び</summary>
    private static IEnumerable<
        ScenarioRow<SyncPlanExpectationInput>
    > RealPostgreSqlScriptForeignKeyRows() =>
        [
            Row(
                    "realdb-pg-script-add-fk-named-with-referential-actions",
                    "AddForeignKey は ADD CONSTRAINT FOREIGN KEY と参照アクションを生成する",
                    DialectNames.PostgreSql
                )
                .Live(RealPostgreSqlScenarios.CustomerOrder)
                .TargetFromLive(s =>
                    s.Fk(
                        "customer",
                        "order",
                        "FK_order_customer",
                        ForeignKeyReferentialAction.Cascade,
                        ForeignKeyReferentialAction.SetDefault,
                        ("id", "customer_id")
                    )
                )
                .Select(items => RealScenarios.SelectKinds(items, SchemaDiffKind.AddForeignKey))
                .OnRealDatabase(r =>
                    r.Expect(e =>
                    {
                        e.Script.Contains(
                            "*ALTER TABLE \"order\" ADD CONSTRAINT \"FK_order_customer\" FOREIGN KEY (\"customer_id\") REFERENCES \"customer\" (\"id\")*",
                            "*ON DELETE CASCADE*",
                            "*ON UPDATE SET DEFAULT*"
                        );
                        e.Outcome.Exact("committed");
                        e.After.Count("fk *", 1);
                        e.After.Single(
                            "fk order FK_order_customer (customer_id) -> customer(id) del=Cascade upd=SetDefault"
                        );
                    })
                ),
            Row(
                    "realdb-pg-script-add-table-and-column-before-fk",
                    "実行順序: AddTable → AddColumn → AddForeignKey",
                    DialectNames.PostgreSql
                )
                .Live(s => s.Table("t", ExpectationSchema.Pk("id", "integer")))
                .TargetFromLive(s =>
                {
                    s.Table("customer", ExpectationSchema.Pk("id", "integer"));
                    s["t"].Columns.Add(ExpectationSchema.Nullable("customer_id", "integer"));
                    s.Fk("customer", "t", null, ("id", "customer_id"));
                })
                .Select(items =>
                    RealScenarios.SelectKinds(
                        items,
                        SchemaDiffKind.AddTable,
                        SchemaDiffKind.AddColumn,
                        SchemaDiffKind.AddForeignKey
                    )
                )
                .OnRealDatabase(r =>
                    r.Expect(e =>
                    {
                        // 外部キーが表・列より前に流れると断られる＝コミットが並びを示す
                        e.Script.Contains(
                            "CREATE TABLE \"customer\" (",
                            "ALTER TABLE \"t\" ADD COLUMN \"customer_id\" integer NULL;",
                            "ALTER TABLE \"t\" ADD CONSTRAINT \"FK_t_customer\" FOREIGN KEY (\"customer_id\")*"
                        );
                        e.Outcome.Exact("committed");
                        e.After.Single(
                            "table customer",
                            "col t.customer_id integer null",
                            "fk t FK_t_customer (customer_id) -> customer(id) *"
                        );
                    })
                ),
        ];

    /// <summary>主キーの付け替え・付与・解除と一意制約の追加・削除の文</summary>
    private static IEnumerable<
        ScenarioRow<SyncPlanExpectationInput>
    > RealPostgreSqlScriptKeyRows() =>
        [
            Row(
                    "realdb-pg-script-pk-single-to-composite",
                    "AlterPrimaryKey は DO ブロックの DROP と複合 PK の ADD を生成する",
                    DialectNames.PostgreSql
                )
                .TargetFromLive(s =>
                {
                    s.Col("orders", "line_no").IsPrimaryKey = true;
                    s.PrimaryKeyOrder("orders", "order_id", "line_no");
                })
                .Select(items => RealScenarios.SelectKinds(items, SchemaDiffKind.AlterPrimaryKey))
                .OnRealDatabase(r =>
                    r.LiveSql(
                            "CREATE TABLE \"orders\" (\"order_id\" integer NOT NULL, \"line_no\" integer NOT NULL, "
                                + "\"memo\" text NULL, CONSTRAINT \"pk_orders_old\" PRIMARY KEY (\"order_id\"));"
                        )
                        .Probe("keys")
                        .Expect(e =>
                        {
                            // 旧主キーの名前は差分に無いので pg_constraint から逆引きして DO ブロックで外す
                            e.Script.Contains(
                                "DO $$",
                                "*FROM pg_constraint con",
                                "*WHERE con.contype = 'p' AND con.conrelid = to_regclass('\"orders\"');",
                                "*IF pk_name IS NOT NULL THEN",
                                "*EXECUTE 'ALTER TABLE \"orders\" DROP CONSTRAINT ' || quote_ident(pk_name);",
                                "ALTER TABLE \"orders\" ADD CONSTRAINT \"PK_orders\" PRIMARY KEY (\"order_id\", \"line_no\");"
                            );
                            e.Outcome.Exact("committed");
                            e.After.Single("pk orders (order_id,line_no)");
                            e.Probe("keys")
                                .Exact(
                                    "keys public.orders kind=PK name=PK_orders columns=order_id,line_no"
                                );
                        })
                ),
            Row(
                    "realdb-pg-script-pk-add-only-guarded-drop",
                    "AlterPrimaryKey は PK 付与のみでも DROP を no-op 形で出す",
                    DialectNames.PostgreSql
                )
                .TargetFromLive(s => s.Col("customer", "id").IsPrimaryKey = true)
                .Select(items => RealScenarios.SelectKinds(items, SchemaDiffKind.AlterPrimaryKey))
                .OnRealDatabase(r =>
                    r.LiveSql(
                            "CREATE TABLE \"customer\" (\"id\" integer NOT NULL, \"memo\" text NULL);"
                        )
                        .Probe("keys")
                        .Expect(e =>
                        {
                            e.Before.Absent("pk *");
                            e.Script.Contains(
                                "*IF pk_name IS NOT NULL THEN",
                                "ALTER TABLE \"customer\" ADD CONSTRAINT \"PK_customer\" PRIMARY KEY (\"id\");"
                            );
                            e.Outcome.Exact("committed");
                            e.After.Single("pk customer (id)");
                            e.Probe("keys")
                                .Exact("keys public.customer kind=PK name=PK_customer columns=id");
                        })
                ),
            Row(
                    "realdb-pg-script-pk-drop-only-omits-add",
                    "AlterPrimaryKey は PK 解除のみなら ADD を出さない",
                    DialectNames.PostgreSql
                )
                .TargetFromLive(s =>
                {
                    s.Col("customer", "id").IsPrimaryKey = false;
                    s["customer"].PrimaryKeyColumnIds.Clear();
                })
                .Select(items => RealScenarios.SelectKinds(items, SchemaDiffKind.AlterPrimaryKey))
                .OnRealDatabase(r =>
                    r.LiveSql(
                            "CREATE TABLE \"customer\" (\"id\" integer NOT NULL, \"memo\" text NULL, "
                                + "CONSTRAINT \"PK_customer\" PRIMARY KEY (\"id\"));"
                        )
                        .Probe("keys")
                        .Expect(e =>
                        {
                            e.Script.Contains("*IF pk_name IS NOT NULL THEN");
                            e.Script.Absent("*ADD CONSTRAINT*", "*PRIMARY KEY (*");
                            e.Outcome.Exact("committed");
                            e.After.Absent("pk *");
                            e.Probe("keys").Empty();
                        })
                ),
            Row(
                    "realdb-pg-script-pk-change-splits-around-alter-column",
                    "AlterPrimaryKey は AlterColumn を挟んで DROP → ALTER → ADD の順に出る",
                    DialectNames.PostgreSql
                )
                .TargetFromLive(s =>
                {
                    var oldId = s.Col("orders", "old_id");
                    oldId.IsPrimaryKey = false;
                    oldId.IsNullable = true;
                    s.Col("orders", "order_id").IsPrimaryKey = true;
                    s["orders"].PrimaryKeyColumnIds.Clear();
                })
                .Select(items =>
                    RealScenarios.SelectKinds(
                        items,
                        SchemaDiffKind.AlterPrimaryKey,
                        SchemaDiffKind.AlterColumn
                    )
                )
                .OnRealDatabase(r =>
                    r.LiveSql(
                            "CREATE TABLE \"orders\" (\"old_id\" integer NOT NULL, \"order_id\" integer NOT NULL, "
                                + "\"memo\" text NULL, CONSTRAINT \"pk_orders_old\" PRIMARY KEY (\"old_id\"));"
                        )
                        .Expect(e =>
                        {
                            // 主キーが残ったままの DROP NOT NULL は断られる（主キーの列）＝コミットが並びを示す
                            e.Script.Contains(
                                "DO $$",
                                "ALTER TABLE \"orders\" ALTER COLUMN \"old_id\" TYPE integer;",
                                "ALTER TABLE \"orders\" ALTER COLUMN \"old_id\" DROP NOT NULL;",
                                "ALTER TABLE \"orders\" ADD CONSTRAINT \"PK_orders\" PRIMARY KEY (\"order_id\");"
                            );
                            e.Outcome.Exact("committed");
                            e.After.Single(
                                "pk orders (order_id)",
                                "col orders.old_id integer null"
                            );
                        })
                ),
            Row(
                    "realdb-pg-script-pk-reorder-follows-primary-key-order",
                    "AlterPrimaryKey は PrimaryKeyColumnIds の順で複合 PK を付与する",
                    DialectNames.PostgreSql
                )
                .TargetFromLive(s => s.PrimaryKeyOrder("orders", "line_no", "order_id"))
                .Select(items => RealScenarios.SelectKinds(items, SchemaDiffKind.AlterPrimaryKey))
                .OnRealDatabase(r =>
                    // 列の宣言の並びは (order_id, line_no)。主キーの実効順だけを逆にする
                    r.LiveSql(
                            "CREATE TABLE \"orders\" (\"order_id\" integer NOT NULL, \"line_no\" integer NOT NULL, "
                                + "CONSTRAINT \"PK_orders\" PRIMARY KEY (\"order_id\", \"line_no\"));"
                        )
                        .Expect(e =>
                        {
                            e.Diff.Single("AlterPrimaryKey orders *");
                            e.Script.Contains(
                                "ALTER TABLE \"orders\" ADD CONSTRAINT \"PK_orders\" PRIMARY KEY (\"line_no\", \"order_id\");"
                            );
                            e.Outcome.Exact("committed");
                            e.After.Single("pk orders (line_no,order_id)");
                        })
                ),
            Row(
                    "realdb-pg-script-pk-change-table-name-with-quote",
                    "AlterPrimaryKey の DO ブロックはテーブル名の ' を二重化する",
                    DialectNames.PostgreSql
                )
                .TargetFromLive(s => RealScenarios.MovePrimaryKeyToCode(s, "o'rders"))
                .Select(items => RealScenarios.SelectKinds(items, SchemaDiffKind.AlterPrimaryKey))
                .OnRealDatabase(r =>
                    r.LiveSql(
                            "CREATE TABLE \"o'rders\" (\"id\" integer NOT NULL, \"code\" integer NOT NULL, "
                                + "CONSTRAINT \"pk_o_old\" PRIMARY KEY (\"id\"));"
                        )
                        .Probe("keys")
                        .Expect(e =>
                        {
                            // EXECUTE '…' の中身は文字列リテラル＝クォートした名前の ' も二重化する
                            e.Script.Contains(
                                "*WHERE con.contype = 'p' AND con.conrelid = to_regclass('\"o''rders\"');",
                                "*EXECUTE 'ALTER TABLE \"o''rders\" DROP CONSTRAINT ' || quote_ident(pk_name);"
                            );
                            e.Script.Absent("*EXECUTE 'ALTER TABLE \"o'rders\"*");
                            e.Outcome.Exact("committed");
                            e.After.Single("pk o'rders (code)");
                            e.Probe("keys")
                                .Exact("keys public.o'rders kind=PK name=PK_o'rders columns=code");
                        })
                ),
            Row(
                    "realdb-pg-script-pk-drop-table-name-with-dollar-tag",
                    "AlterPrimaryKey の DO ブロックは $$ 入りテーブル名でも終端されない",
                    DialectNames.PostgreSql
                )
                .TargetFromLive(s =>
                {
                    s.Col("o$$rders", "id").IsPrimaryKey = false;
                    s["o$$rders"].PrimaryKeyColumnIds.Clear();
                })
                .Select(items => RealScenarios.SelectKinds(items, SchemaDiffKind.AlterPrimaryKey))
                .OnRealDatabase(r =>
                    r.LiveSql(
                            "CREATE TABLE \"o$$rders\" (\"id\" integer NOT NULL, \"memo\" text NULL, "
                                + "CONSTRAINT \"pk_o_dollar\" PRIMARY KEY (\"id\"));"
                        )
                        .Probe("keys")
                        .Expect(e =>
                        {
                            // タグが $$ のままなら、本文の名前の $$ でブロックが閉じて構文エラーになる＝コミットが示す
                            e.Script.Contains(
                                "*EXECUTE 'ALTER TABLE \"o$$rders\" DROP CONSTRAINT ' || quote_ident(pk_name);"
                            );
                            e.Script.Absent("DO $$", "END $$;");
                            e.Outcome.Exact("committed");
                            e.After.Absent("pk *");
                            e.Probe("keys").Empty();
                        })
                ),
            Row(
                    "realdb-pg-script-unique-add-synthesized-composite-name",
                    "AddUniqueConstraint は ADD CONSTRAINT UNIQUE を生成する",
                    DialectNames.PostgreSql
                )
                .Live(s =>
                    s.Table(
                        "customer",
                        ExpectationSchema.Pk("id", "integer"),
                        ExpectationSchema.NotNull("code", "varchar(10)"),
                        ExpectationSchema.NotNull("kind", "varchar(10)")
                    )
                )
                .TargetFromLive(s => s.Unique("customer", null, "code", "kind"))
                .Select(items =>
                    RealScenarios.SelectKinds(items, SchemaDiffKind.AddUniqueConstraint)
                )
                .OnRealDatabase(r =>
                    r.Expect(e =>
                    {
                        // 制約名は未設定のため UQ_{テーブル}_{列…} を合成する
                        e.Script.Contains(
                            "ALTER TABLE \"customer\" ADD CONSTRAINT \"UQ_customer_code_kind\" UNIQUE (\"code\", \"kind\");"
                        );
                        e.Outcome.Exact("committed");
                        e.After.Single("uq customer UQ_customer_code_kind (code,kind)");
                    })
                ),
            Row(
                    "realdb-pg-script-unique-drop-by-live-name",
                    "DropUniqueConstraint は実名で DROP CONSTRAINT を生成する",
                    DialectNames.PostgreSql
                )
                .Live(s =>
                {
                    s.Table(
                        "customer",
                        ExpectationSchema.Pk("id", "integer"),
                        ExpectationSchema.NotNull("code", "varchar(10)")
                    );
                    s.Unique("customer", "uq_legacy", "code");
                })
                .TargetFromLive(s => s["customer"].UniqueConstraints.Clear())
                .Select(items =>
                    RealScenarios.SelectKinds(items, SchemaDiffKind.DropUniqueConstraint)
                )
                .OnRealDatabase(r =>
                    r.Expect(e =>
                    {
                        e.Before.Single("uq customer uq_legacy (code)");
                        // 合成名（UQ_customer_code）でなく DB の実名で外す（名前を違えると断られる）
                        e.Script.Contains(
                            "ALTER TABLE \"customer\" DROP CONSTRAINT \"uq_legacy\";"
                        );
                        e.Outcome.Exact("committed");
                        e.After.Absent("uq *");
                    })
                ),
        ];
}

/// <summary>PostgreSQL の実 DB の段の図・SQL・選び方の部品</summary>
internal static class RealPostgreSqlScenarios
{
    /// <summary>スキーマ sales を作り直す（取込の範囲の外の表を置く行が使う。リセットは public しか消さない）</summary>
    public const string RecreateSalesSchemaSql =
        "DROP SCHEMA IF EXISTS \"sales\" CASCADE; CREATE SCHEMA \"sales\";";

    /// <summary>スキーマ sales を消す（後片付け）</summary>
    public const string DropSalesSchemaSql = "DROP SCHEMA IF EXISTS \"sales\" CASCADE;";

    /// <summary>
    /// 実行器が巻き戻した失敗の行（<paramref name="fragment"/> は PostgreSQL のエラーの断片のパターン）
    /// </summary>
    public static string RolledBack(string fragment) =>
        "failed: "
        + string.Format(
            CultureInfo.InvariantCulture,
            ProviderStrings.Sync_Error_RolledBack,
            fragment
        );

    /// <summary>選べる項目をすべて選ぶ（選べない案内の項目は選ばない）</summary>
    public static void SelectSelectable(IReadOnlyList<SchemaDiffItem> items)
    {
        foreach (var item in items.Where(i => i.IsSelectable))
        {
            item.IsSelected = true;
        }
    }

    /// <summary>
    /// スキーマ同期の往復の DB の図（<paramref name="stage"/>：0＝説明つきの parent と child(id, parent_id, to_alter, to_drop) と
    /// FK_child_parent（CASCADE）・1＝child を (id, parent_id, to_alter varchar(100) NOT NULL, added) へ変えた後・
    /// 2＝さらに外部キーを外した後）
    /// </summary>
    public static void SchemaSyncParentChild(ExpectationSchema s, int stage)
    {
        var name = ExpectationSchema.Nullable("name", "varchar(50)");
        name.Description = "名称";
        s.Table("parent", ExpectationSchema.Pk("id", "integer"), name).Description =
            "親テーブルの説明";

        if (stage == 0)
        {
            s.Table(
                "child",
                ExpectationSchema.Pk("id", "integer"),
                ExpectationSchema.Nullable("parent_id", "integer"),
                ExpectationSchema.Nullable("to_alter", "varchar(50)"),
                ExpectationSchema.Nullable("to_drop", "integer")
            );
        }
        else
        {
            s.Table(
                "child",
                ExpectationSchema.Pk("id", "integer"),
                ExpectationSchema.Nullable("parent_id", "integer"),
                ExpectationSchema.NotNull("to_alter", "varchar(100)"),
                ExpectationSchema.Nullable("added", "integer")
            );
        }

        if (stage < 2)
        {
            s.Fk(
                "parent",
                "child",
                "FK_child_parent",
                ForeignKeyReferentialAction.Cascade,
                ("id", "parent_id")
            );
        }
    }

    /// <summary>
    /// uq_item(id, code)（<paramref name="withUnique"/> が真なら、同期が付けた名前 UQ_uq_item_code の code の一意制約も）
    /// </summary>
    public static string UniqueTargetTableSql(bool withUnique) =>
        "CREATE TABLE \"uq_item\" (\"id\" integer NOT NULL, \"code\" varchar(20) NOT NULL, "
        + "CONSTRAINT \"PK_uq_item\" PRIMARY KEY (\"id\")"
        + (withUnique ? ", CONSTRAINT \"UQ_uq_item_code\" UNIQUE (\"code\")" : string.Empty)
        + ");";

    /// <summary>
    /// 複合主キー cfk_parent(a, b) と cfk_child(id, a_ref, b_ref)（<paramref name="withForeignKey"/> が真なら
    /// FK_cfk_child_cfk_parent (a_ref, b_ref) → (a, b) も）
    /// </summary>
    public static string CompositeParentChildSql(bool withForeignKey) =>
        "CREATE TABLE \"cfk_parent\" (\"a\" integer NOT NULL, \"b\" integer NOT NULL, "
        + "CONSTRAINT \"PK_cfk_parent\" PRIMARY KEY (\"a\", \"b\"));"
        + "CREATE TABLE \"cfk_child\" (\"id\" integer NOT NULL, \"a_ref\" integer NOT NULL, "
        + "\"b_ref\" integer NOT NULL, CONSTRAINT \"PK_cfk_child\" PRIMARY KEY (\"id\")"
        + (
            withForeignKey
                ? ", CONSTRAINT \"FK_cfk_child_cfk_parent\" FOREIGN KEY (\"a_ref\", \"b_ref\") "
                    + "REFERENCES \"cfk_parent\" (\"a\", \"b\")"
                : string.Empty
        )
        + ");";

    /// <summary>u(id, qty, name varchar(100))（qty の型は <paramref name="qtyType"/>）</summary>
    public static string TypeFamilyTableSql(string qtyType) =>
        $"CREATE TABLE \"u\" (\"id\" integer NOT NULL, \"qty\" {qtyType} NULL, "
        + "\"name\" varchar(100) NULL, CONSTRAINT \"PK_u\" PRIMARY KEY (\"id\"));";

    /// <summary>u に 1 行（qty は <paramref name="qty"/> のリテラル・name は 'abcdefghij'）</summary>
    public static string TypeFamilyRowSql(string qty) =>
        $"INSERT INTO \"u\" VALUES (1, {qty}, 'abcdefghij');";

    /// <summary>u の行の qty と name</summary>
    public const string TypeFamilySelectSql = "SELECT \"qty\", \"name\" FROM \"u\";";

    /// <summary>
    /// 大文字小文字だけ違う綴りの図：Orders（説明 orders）・Id integer NOT NULL・Name varchar(100)（説明つき）・Code varchar(10) の主キー
    /// </summary>
    public static void MixedCaseOrders(ExpectationSchema s)
    {
        var name = ExpectationSchema.Nullable("Name", "varchar(100)");
        name.Description = "order name";
        s.Table(
            "Orders",
            ExpectationSchema.NotNull("Id", "integer"),
            name,
            ExpectationSchema.Pk("Code", "varchar(10)")
        ).Description = "orders";
    }

    /// <summary>説明の往復の段ごとの（表の説明, 列の説明）（0＝説明なし・1＝設定・2＝更新・3＝削除）</summary>
    public static (string Table, string Column) ItemsDescriptions(int stage) =>
        stage switch
        {
            0 => ("", ""),
            1 => ("商品テーブル 'マスタ'", "商品名 'name' 列"),
            2 => ("商品テーブル（更新後）", "商品名（更新後）"),
            _ => ("", ""),
        };

    /// <summary>items(id, name varchar(50) NULL) を、段 <paramref name="stage"/> の説明つきで作る</summary>
    public static string ItemsTableSql(int stage)
    {
        var (table, column) = ItemsDescriptions(stage);
        return "CREATE TABLE \"items\" (\"id\" integer NOT NULL, \"name\" varchar(50) NULL, "
            + "CONSTRAINT \"PK_items\" PRIMARY KEY (\"id\"));"
            + (
                table.Length > 0
                    ? $"COMMENT ON TABLE \"items\" IS '{table.Replace("'", "''")}';"
                    : string.Empty
            )
            + (
                column.Length > 0
                    ? $"COMMENT ON COLUMN \"items\".\"name\" IS '{column.Replace("'", "''")}';"
                    : string.Empty
            );
    }

    /// <summary>
    /// shops(id 主キー・code・region) と、名前つき単一列の一意制約 UQ_shops_code (code) と、名前なしの複合の一意制約
    /// (region, code)（宣言の並びは列の並びと逆）
    /// </summary>
    public static void Shops(ExpectationSchema s)
    {
        s.Table(
            "shops",
            ExpectationSchema.Pk("id", "integer"),
            ExpectationSchema.NotNull("code", "varchar(20)"),
            ExpectationSchema.NotNull("region", "varchar(10)")
        );
        s.Unique("shops", "UQ_shops_code", "code");
        s.Unique("shops", null, "region", "code");
    }

    /// <summary>親 customer(id) と、外部キーの無い子 order(id, customer_id NULL 許容)</summary>
    public static void CustomerOrder(ExpectationSchema s)
    {
        s.Table("customer", ExpectationSchema.Pk("id", "integer"));
        s.Table(
            "order",
            ExpectationSchema.Pk("id", "integer"),
            ExpectationSchema.Nullable("customer_id", "integer")
        );
    }
}
