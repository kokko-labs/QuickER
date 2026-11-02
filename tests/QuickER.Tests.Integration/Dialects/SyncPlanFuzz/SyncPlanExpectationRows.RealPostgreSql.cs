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
    private const int RealPostgreSqlRowCount = 24;

    /// <summary>実 DB の段：PostgreSQL の同期</summary>
    private static IEnumerable<ScenarioRow<SyncPlanExpectationInput>> RealPostgreSqlRows() =>
        [
            .. RealPostgreSqlSchemaSyncRows(),
            .. RealPostgreSqlKeyRows(),
            .. RealPostgreSqlColumnRows(),
            .. RealPostgreSqlDescriptionRows(),
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
                            e.After.Single(
                                $"desc items \"{table}\"",
                                $"desc items.name \"{column}\""
                            );
                        }
                        else
                        {
                            e.After.Absent("desc *");
                        }
                    })
            );
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
}
