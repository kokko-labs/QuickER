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
    /// <summary>Oracle の実 DB の段の行の数の下限（行を足したら・消したら合わせる）</summary>
    private const int RealOracleRowCount = 24;

    /// <summary>実 DB の段：Oracle の同期</summary>
    private static IEnumerable<ScenarioRow<SyncPlanExpectationInput>> RealOracleRows() =>
        [
            .. RealOracleSchemaSyncRows(),
            .. RealOracleKeyRows(),
            .. RealOracleColumnRows(),
            .. RealOracleDescriptionRows(),
        ];

    /// <summary>スキーマ同期の往復（表・列・外部キーの追加と削除・一意制約・複合外部キー）</summary>
    private static IEnumerable<ScenarioRow<SyncPlanExpectationInput>> RealOracleSchemaSyncRows() =>
        [
            Row(
                    "realdb-oracle-schema-sync-add-tables-fk-and-descriptions-from-empty",
                    "[Integration] C: スキーマ同期が Add/Alter/Drop/FK まで実 DB に適用される",
                    DialectNames.Oracle
                )
                .Live(_ => { })
                .Target(s => RealOracleScenarios.SchemaSyncParentChild(s, stage: 0))
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
                    "realdb-oracle-schema-sync-add-alter-drop-columns",
                    "[Integration] C: スキーマ同期が Add/Alter/Drop/FK まで実 DB に適用される",
                    DialectNames.Oracle
                )
                .Live(s => RealOracleScenarios.SchemaSyncParentChild(s, stage: 0))
                .TargetFromLive(s =>
                {
                    // 子は図を組み直す（型変更＋NULL→NOT NULL・列の追加・列の削除）。外部キーは維持
                    s.Relationships.Clear();
                    s.Entities.RemoveAll(e => e.TableName == "child");
                    s.Table(
                        "child",
                        ExpectationSchema.Pk("id", "NUMBER(10)"),
                        ExpectationSchema.Nullable("parent_id", "NUMBER(10)"),
                        ExpectationSchema.NotNull("to_alter", "VARCHAR2(100)"),
                        ExpectationSchema.Nullable("added", "NUMBER(10)")
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
                        e.After.Single("col child.to_alter VARCHAR2(100) notnull*");
                    })
                ),
            Row(
                    "realdb-oracle-schema-sync-drop-named-fk",
                    "[Integration] C: スキーマ同期が Add/Alter/Drop/FK まで実 DB に適用される",
                    DialectNames.Oracle
                )
                .Live(s => RealOracleScenarios.SchemaSyncParentChild(s, stage: 1))
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
                    "realdb-oracle-schema-sync-drop-table",
                    "[Integration] C: スキーマ同期が Add/Alter/Drop/FK まで実 DB に適用される",
                    DialectNames.Oracle
                )
                .Live(s => RealOracleScenarios.SchemaSyncParentChild(s, stage: 2))
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
                    "realdb-oracle-uq-add",
                    "[Integration] C: 一意制約の追加・削除が実 DB へ反映される",
                    DialectNames.Oracle
                )
                .TargetFromLive(s => s.Unique("uq_item", null, "code"))
                .OnRealDatabase(r =>
                    r.LiveSql(RealOracleScenarios.UniqueTargetTableSql(withUnique: false))
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
                    "realdb-oracle-uq-drop",
                    "[Integration] C: 一意制約の追加・削除が実 DB へ反映される",
                    DialectNames.Oracle
                )
                .TargetFromLive(s => s["uq_item"].UniqueConstraints.Clear())
                .Select(items =>
                    RealScenarios.AlsoSelect(items, SchemaDiffKind.DropUniqueConstraint)
                )
                .OnRealDatabase(r =>
                    r.LiveSql(RealOracleScenarios.UniqueTargetTableSql(withUnique: true))
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
                    "realdb-oracle-uq-columns-change-default-selection-changes-nothing",
                    "[Integration] C: 名前付き一意制約の列の変更は作り直し項目になり、選べば同じ名前で作り直す",
                    DialectNames.Oracle
                )
                .TargetFromLive(RealOracleScenarios.WidenUniqueToCodeAndKind)
                .OnRealDatabase(r =>
                    r.LiveSql(RealOracleScenarios.UniqueKeyItemSql)
                        .Expect(e =>
                        {
                            // 差分は作り直しの 1 項目だけで、既定では選ばれない＝計画は空
                            e.Diff.Exact("AlterUniqueConstraint uq_item uq=UQ_uq_item_key sel=0 *");
                            e.Sections.Empty();
                            e.Rebuilds.Empty();
                            e.Reorders.Empty();
                            e.Outcome.Exact("committed");
                        })
                ),
            Row(
                    "realdb-oracle-uq-columns-change-selected-rebuilds-same-name",
                    "[Integration] C: 名前付き一意制約の列の変更は作り直し項目になり、選べば同じ名前で作り直す",
                    DialectNames.Oracle
                )
                .TargetFromLive(RealOracleScenarios.WidenUniqueToCodeAndKind)
                .Select(items =>
                    RealScenarios.SelectKinds(items, SchemaDiffKind.AlterUniqueConstraint)
                )
                .OnRealDatabase(r =>
                    r.LiveSql(RealOracleScenarios.UniqueKeyItemSql)
                        .Expect(e =>
                        {
                            e.Outcome.Exact("committed");
                            e.After.Count("uq uq_item *", 1);
                            e.After.Single("uq uq_item UQ_uq_item_key (code,kind)");
                        })
                ),
            Row(
                    "realdb-oracle-fk-composite-declared-other-order-no-diff",
                    "[Integration] C: 主キーと違う並びで宣言した複合外部キーは、宣言どおりの図と差分を出さない",
                    DialectNames.Oracle
                )
                .TargetFromLive(s =>
                {
                    // 外部キーを宣言どおりの並び（x→a, y→b）で組み直す
                    s.Relationships.Clear();
                    s.Fk("po_parent", "po_child", "FK_po_child", ("a", "x"), ("b", "y"));
                })
                .Select(items => RealScenarios.SelectKinds(items))
                .OnRealDatabase(r =>
                    r.LiveSql(
                            "CREATE TABLE \"po_parent\" (\"a\" NUMBER(10) NOT NULL, \"b\" NUMBER(10) NOT NULL, "
                                + "CONSTRAINT \"PK_po_parent\" PRIMARY KEY (\"b\", \"a\"));\n"
                                + "CREATE TABLE \"po_child\" (\"id\" NUMBER(10) NOT NULL, \"x\" NUMBER(10), \"y\" NUMBER(10), "
                                + "CONSTRAINT \"PK_po_child\" PRIMARY KEY (\"id\"), "
                                + "CONSTRAINT \"FK_po_child\" FOREIGN KEY (\"x\", \"y\") REFERENCES \"po_parent\" (\"a\", \"b\"));"
                        )
                        .Expect(e =>
                        {
                            // カタログは参照先のキー (b, a) の並びで返す＝宣言 (x→a, y→b) と逆の並び
                            e.Before.Single("fk po_child FK_po_child (y,x) -> po_parent(b,a) *");
                            e.Diff.Empty();
                            e.Outcome.Exact("committed");
                        })
                ),
            Row(
                    "realdb-oracle-fk-composite-add-keeps-pairs",
                    "[Integration] C: 複合外部キーの追加・削除が実 DB へ反映される",
                    DialectNames.Oracle
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
                    r.LiveSql(RealOracleScenarios.CompositeParentChildSql(withForeignKey: false))
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
                    "realdb-oracle-fk-composite-drop",
                    "[Integration] C: 複合外部キーの追加・削除が実 DB へ反映される",
                    DialectNames.Oracle
                )
                .TargetFromLive(s => s.Relationships.Clear())
                .Select(items => RealScenarios.AlsoSelect(items, SchemaDiffKind.DropForeignKey))
                .OnRealDatabase(r =>
                    r.LiveSql(RealOracleScenarios.CompositeParentChildSql(withForeignKey: true))
                        .Expect(e =>
                        {
                            e.Diff.Single("DropForeignKey *");
                            e.Outcome.Exact("committed");
                            e.After.Absent("fk *");
                        })
                ),
            Row(
                    "realdb-oracle-fk-composite-survives-pk-change",
                    "[Integration] C: 複合主キー変更に伴う複合外部キーの張り直しで構成列が失われない",
                    DialectNames.Oracle
                )
                .TargetFromLive(s => s.Col("cfk_parent", "code").IsPrimaryKey = false)
                .Select(items => RealScenarios.AlsoSelect(items, SchemaDiffKind.AlterPrimaryKey))
                .OnRealDatabase(r =>
                    r.LiveSql(
                            "CREATE TABLE \"cfk_parent\" (\"id\" NUMBER(10) NOT NULL, \"code\" NUMBER(10) NOT NULL, "
                                + "\"a\" NUMBER(10) NOT NULL, \"b\" NUMBER(10) NOT NULL, "
                                + "CONSTRAINT \"PK_cfk_parent\" PRIMARY KEY (\"id\", \"code\"), "
                                + "CONSTRAINT \"UQ_cfk_parent_a_b\" UNIQUE (\"a\", \"b\"));\n"
                                + "CREATE TABLE \"cfk_child\" (\"id\" NUMBER(10) NOT NULL, \"a_ref\" NUMBER(10) NOT NULL, "
                                + "\"b_ref\" NUMBER(10) NOT NULL, CONSTRAINT \"PK_cfk_child\" PRIMARY KEY (\"id\"), "
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

    /// <summary>主キーの付け替え（行の温存・部分適用・外部キーの片側の長さだけの変更・別のスキーマの同じ名前の表）</summary>
    private static IEnumerable<ScenarioRow<SyncPlanExpectationInput>> RealOracleKeyRows() =>
        [
            Row(
                    "realdb-oracle-pk-move-to-code-keeps-rows",
                    "[Integration] C: 主キーを id から code へ付け替えてもデータが温存される",
                    DialectNames.Oracle
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
                            "CREATE TABLE \"pk_item\" (\"code\" VARCHAR2(20) NOT NULL, \"id\" NUMBER(10) NOT NULL, "
                                + "CONSTRAINT \"PK_pk_item\" PRIMARY KEY (\"id\"));"
                        )
                        .Setup(
                            "INSERT INTO \"pk_item\" (\"code\", \"id\") VALUES ('A-001', 1);\n"
                                + "INSERT INTO \"pk_item\" (\"code\", \"id\") VALUES ('A-002', 2);"
                        )
                        .Query("rows", "SELECT \"code\", \"id\" FROM \"pk_item\" ORDER BY \"id\"")
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
                    "realdb-oracle-pk-change-breaking-referenced-fk-partially-applies",
                    "[Integration] C: AlterPrimaryKey: 被参照列が候補キーでなくなる変更は失敗し部分適用で残る",
                    DialectNames.Oracle
                )
                .TargetFromLive(s =>
                {
                    // 親の主キーを id から code へ移す（子の外部キーは id を参照したまま維持）
                    s.Col("pk_parent", "id").IsPrimaryKey = false;
                    s.Col("pk_parent", "code").IsPrimaryKey = true;
                })
                .Select(items => RealScenarios.AlsoSelect(items, SchemaDiffKind.AlterPrimaryKey))
                .OnRealDatabase(r =>
                    r.LiveSql(
                            "CREATE TABLE \"pk_parent\" (\"id\" NUMBER(10) NOT NULL, \"code\" VARCHAR2(20) NOT NULL, "
                                + "CONSTRAINT \"PK_pk_parent\" PRIMARY KEY (\"id\"));\n"
                                + "CREATE TABLE \"pk_child\" (\"id\" NUMBER(10) NOT NULL, \"parent_id\" NUMBER(10) NULL, "
                                + "CONSTRAINT \"PK_pk_child\" PRIMARY KEY (\"id\"), "
                                + "CONSTRAINT \"FK_pk_child_pk_parent\" FOREIGN KEY (\"parent_id\") "
                                + "REFERENCES \"pk_parent\" (\"id\"));"
                        )
                        .Setup(
                            "INSERT INTO \"pk_parent\" (\"id\", \"code\") VALUES (1, 'P-1');\n"
                                + "INSERT INTO \"pk_child\" (\"id\", \"parent_id\") VALUES (1, 1);"
                        )
                        .Expect(e =>
                        {
                            e.Diff.Single("AlterPrimaryKey pk_parent *");
                            // 計画側は候補キー喪失の恐れを警告として積む（一意制約は取り込んでいないため実行はブロックしない）
                            e.Warnings.Contains("ForeignKeyRebuildMayLoseCandidateKey(*)*");
                            // 再 ADD できない外部キーがあるため失敗する（ORA-02270: 一致する一意キー・主キーが無い）
                            e.Outcome.Exact("failed: ORA-02270:*");
                            // 部分適用があり得る旨の説明が付く（実行器の文言の後半＝ODP.NET の例外文の後ろの括弧の部分）
                            e.Error.Contains(RealOracleScenarios.PartialApplyNotePattern);
                            // Oracle の DDL は暗黙コミット＝主キーの付け替えは適用済みのまま残り、外した外部キーは戻らない
                            e.After.Single("pk pk_parent (code)");
                            e.After.Absent("fk *");
                        })
                ),
            Row(
                    "realdb-oracle-pk-change-with-string-length-only-fk-keeps-fk",
                    "[Integration] C: 主キーの付け替えと外部キーの片側の長さだけの変更は、外部キーを保って成功する",
                    DialectNames.Oracle
                )
                .TargetFromLive(s =>
                {
                    // 親の主キーを code から id へ移し、code には一意制約を置く（外部キーの参照先は候補キーのまま）。
                    // 子の列は長さだけを変える
                    s.Col("lp_parent", "code").IsPrimaryKey = false;
                    s.Col("lp_parent", "id").IsPrimaryKey = true;
                    s.Unique("lp_parent", "UQ_lp_parent_code", "code");
                    s.Col("lp_child", "parent_code").DataType = "VARCHAR2(20)";
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
                            "CREATE TABLE \"lp_parent\" (\"id\" NUMBER(10) NOT NULL, \"code\" VARCHAR2(10) NOT NULL, "
                                + "CONSTRAINT \"PK_lp_parent\" PRIMARY KEY (\"code\"));\n"
                                + "CREATE TABLE \"lp_child\" (\"id\" NUMBER(10) NOT NULL, \"parent_code\" VARCHAR2(10) NOT NULL, "
                                + "CONSTRAINT \"PK_lp_child\" PRIMARY KEY (\"id\"), "
                                + "CONSTRAINT \"FK_lp_child\" FOREIGN KEY (\"parent_code\") REFERENCES \"lp_parent\" (\"code\"));"
                        )
                        .Setup(
                            "INSERT INTO \"lp_parent\" (\"id\", \"code\") VALUES (1, 'a');\n"
                                + "INSERT INTO \"lp_child\" (\"id\", \"parent_code\") VALUES (1, 'a');"
                        )
                        .Expect(e =>
                        {
                            e.Warnings.Empty();
                            e.Outcome.Exact("committed");
                            e.After.Single(
                                "col lp_child.parent_code VARCHAR2(20) *",
                                "col lp_parent.id * pk*"
                            );
                            e.After.Count("fk *", 1);
                            e.After.Single("fk lp_child FK_lp_child *");
                        })
                ),
            Row(
                    "realdb-oracle-pk-alter-unqualified-table-keeps-same-named-table-in-other-schema",
                    "[Integration] 修飾しない表の主キーの付け替えと外部キーの削除は、自スキーマの表だけに効く",
                    DialectNames.Oracle
                )
                .TargetFromLive(s => RealScenarios.MovePrimaryKeyToCode(s, "T"))
                .Select(items => RealScenarios.AlsoSelect(items, SchemaDiffKind.AlterPrimaryKey))
                .OnRealDatabase(r =>
                    // 別のスキーマ OTHER に同じ名前の T・P・C を置く（取込の範囲は自スキーマ）
                    r.AdminSetup(RealOracleScenarios.DropOtherSchemaSql)
                        .AdminSetup(RealOracleScenarios.CreateOtherSchemaSql)
                        .AdminSetup(
                            "CREATE TABLE OTHER.\"T\" (\"ID\" NUMBER(10) NOT NULL, \"CODE\" NUMBER(10) NOT NULL, CONSTRAINT \"PK_OTHER_T\" PRIMARY KEY (\"ID\"));\n"
                                + "CREATE TABLE OTHER.\"P\" (\"ID\" NUMBER(10) NOT NULL, CONSTRAINT \"PK_OTHER_P\" PRIMARY KEY (\"ID\"));\n"
                                + "CREATE TABLE OTHER.\"C\" (\"ID\" NUMBER(10) NOT NULL, \"PID\" NUMBER(10), CONSTRAINT \"PK_OTHER_C\" PRIMARY KEY (\"ID\"), CONSTRAINT \"FK_OTHER_C_P\" FOREIGN KEY (\"PID\") REFERENCES OTHER.\"P\" (\"ID\"));\n"
                                + "GRANT ALTER, SELECT ON OTHER.\"T\" TO ORACLE;\n"
                                + "GRANT ALTER, SELECT ON OTHER.\"C\" TO ORACLE;\n"
                                + "GRANT SELECT ON OTHER.\"P\" TO ORACLE;"
                        )
                        .LiveSql(
                            "CREATE TABLE \"T\" (\"ID\" NUMBER(10) NOT NULL, \"CODE\" NUMBER(10) NOT NULL, CONSTRAINT \"PK_T\" PRIMARY KEY (\"ID\"));\n"
                                + "CREATE TABLE \"P\" (\"ID\" NUMBER(10) NOT NULL, CONSTRAINT \"PK_P\" PRIMARY KEY (\"ID\"));\n"
                                + "CREATE TABLE \"C\" (\"ID\" NUMBER(10) NOT NULL, \"PID\" NUMBER(10), CONSTRAINT \"PK_C\" PRIMARY KEY (\"ID\"), CONSTRAINT \"FK_C_P\" FOREIGN KEY (\"PID\") REFERENCES \"P\" (\"ID\"));"
                        )
                        .AdminQuery("primary-keys", RealOracleScenarios.PrimaryKeysSql)
                        .AdminQuery("foreign-keys", RealOracleScenarios.ForeignKeysSql)
                        .AdminTeardown(RealOracleScenarios.DropOtherSchemaSql)
                        .Expect(e =>
                        {
                            e.Diff.Single("AlterPrimaryKey T *");
                            e.Outcome.Exact("committed");
                            // 自スキーマの T だけが付け替わる
                            e.Probe("primary-keys")
                                .Exact(
                                    "ORACLE.C -> PK_C (ID)",
                                    "ORACLE.P -> PK_P (ID)",
                                    "ORACLE.T -> PK_T (CODE)",
                                    "OTHER.C -> PK_OTHER_C (ID)",
                                    "OTHER.P -> PK_OTHER_P (ID)",
                                    "OTHER.T -> PK_OTHER_T (ID)"
                                );
                            e.Probe("foreign-keys")
                                .Exact("ORACLE.C -> FK_C_P", "OTHER.C -> FK_OTHER_C_P");
                        })
                ),
        ];

    /// <summary>列の変更（データの都合の失敗・型だけの変更・既定値・計算列・大文字小文字だけ違う名前）</summary>
    private static IEnumerable<ScenarioRow<SyncPlanExpectationInput>> RealOracleColumnRows() =>
        [
            Row(
                    "realdb-oracle-column-alter-non-empty-to-number-fails",
                    "[Integration] C: 不正な同期は Committed=false・エラーが報告される",
                    DialectNames.Oracle
                )
                .TargetFromLive(s => s.Col("t", "note").DataType = "NUMBER(10)")
                .Select(items => RealScenarios.SelectKinds(items, SchemaDiffKind.AlterColumn))
                .OnRealDatabase(r =>
                    // 文字列列 note に非数値データを入れておくと NUMBER への変換が失敗する
                    r.LiveSql(
                            "CREATE TABLE \"t\" (\"id\" NUMBER(10) NOT NULL, \"note\" VARCHAR2(50) NULL, "
                                + "CONSTRAINT \"PK_t\" PRIMARY KEY (\"id\"));"
                        )
                        .Setup("INSERT INTO \"t\" (\"id\", \"note\") VALUES (1, 'abc');")
                        .ExpectsUnwarnedFailure(
                            "データの都合：note は値 'abc' を持つので型を変えられない（Oracle は空でない列の型の変更を断る。計画は列の値を見ない）"
                        )
                        .Expect(e =>
                        {
                            e.Outcome.Exact("failed: ORA-01439:*");
                            // 失敗した文の前に適用された文は無い＝スキーマ不変（note は VARCHAR2(50) のまま）
                            e.Unchanged();
                            e.After.Single("col t.note VARCHAR2(50) *");
                        })
                ),
            Row(
                    "realdb-oracle-column-alter-type-only-keeps-nullability",
                    "[Integration] C: NULL 許容を変えずに型だけを変える同期が NOT NULL・NULL 許容の列とも成功する",
                    DialectNames.Oracle
                )
                .TargetFromLive(s =>
                {
                    s.Col("alt_item", "name").DataType = "VARCHAR2(100)";
                    s.Col("alt_item", "note").DataType = "VARCHAR2(100)";
                })
                .Select(items => RealScenarios.SelectKinds(items, SchemaDiffKind.AlterColumn))
                .OnRealDatabase(r =>
                    r.LiveSql(
                            "CREATE TABLE \"alt_item\" (\"id\" NUMBER(10) NOT NULL, "
                                + "\"name\" VARCHAR2(50) NOT NULL, \"note\" VARCHAR2(50) NULL, "
                                + "CONSTRAINT \"PK_alt_item\" PRIMARY KEY (\"id\"));"
                        )
                        .Expect(e =>
                        {
                            e.Diff.Count("AlterColumn *", 2);
                            e.Outcome.Exact("committed");
                            e.After.Single(
                                "col alt_item.name VARCHAR2(100) notnull",
                                "col alt_item.note VARCHAR2(100) null"
                            );
                        })
                ),
            Row(
                    "realdb-oracle-column-type-kind-change-with-default-warns",
                    "[Integration] Oracle 同期: 既定値のある列の型の種類の変更は事前に警告される",
                    DialectNames.Oracle
                )
                .TargetFromLive(s => s.Col("jd", "v").DataType = "NUMBER(10)")
                .Select(items => RealScenarios.SelectKinds(items, SchemaDiffKind.AlterColumn))
                .OnRealDatabase(r =>
                    r.LiveSql(
                            "CREATE TABLE \"jd\" (\"id\" NUMBER(10) NOT NULL PRIMARY KEY, \"v\" VARCHAR2(20) DEFAULT 'abc' NOT NULL);"
                        )
                        .Expect(e =>
                        {
                            e.Warnings.Single(
                                "DefaultOnColumnTypeKindChange(*)*",
                                "DefaultOnColumnTypeKindChange(*): v (*"
                            );
                            // Oracle も型の変更で既定値を新しい型で確かめるので、合わない既定値では同期が失敗する
                            e.Outcome.Exact("failed: *");
                            e.Unchanged();
                        })
                ),
            Row(
                    "realdb-oracle-computed-column-add-applies-with-leading-comment",
                    "[Integration] C: 計算列の AddColumn はコメント前置のまま実 DB へ適用される",
                    DialectNames.Oracle
                )
                .TargetFromLive(s =>
                {
                    // 計算列の印つきの列を足す（式は図に載らない＝足されるのは普通の列）
                    var total = ExpectationSchema.Nullable("total", "NUMBER(21,2)");
                    total.IsComputed = true;
                    s["calc_item"].Columns.Add(total);
                })
                .Select(items => RealScenarios.SelectKinds(items, SchemaDiffKind.AddColumn))
                .OnRealDatabase(r =>
                    r.LiveSql(
                            "CREATE TABLE \"calc_item\" (\"id\" NUMBER(10) NOT NULL, "
                                + "CONSTRAINT \"PK_calc_item\" PRIMARY KEY (\"id\"));"
                        )
                        .Expect(e =>
                        {
                            e.Diff.Single("AddColumn calc_item.total * computed*");
                            // 注意コメントが ALTER の直前へ前置され、実行器がコメントつきの 1 文として流す
                            e.Script.Contains("-- Computed column 'total'*");
                            e.Outcome.Exact("committed");
                            // 再取込: 列は普通の列として追加されている（実 DB に式は無い＝計算列の印は立たない）
                            e.After.Single("col calc_item.total NUMBER(21,2) null");
                        })
                ),
            Row(
                    "realdb-oracle-case-only-names-sync-to-uppercase-table",
                    "[Integration] Oracle: 大文字で格納された表へ大文字小文字だけ違う図を同期できる",
                    DialectNames.Oracle
                )
                .Target(RealOracleScenarios.MixedCaseOrders)
                .Select(RealOracleScenarios.SelectSelectable)
                .OnRealDatabase(r =>
                    r.LiveSql(
                            """
                            CREATE TABLE ORDERS (
                                ID NUMBER(10) NOT NULL,
                                NAME VARCHAR2(50) NULL,
                                CODE VARCHAR2(10) NOT NULL,
                                CONSTRAINT PK_ORDERS PRIMARY KEY (ID)
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
                                "table ORDERS",
                                "desc ORDERS \"orders\"",
                                "col ORDERS.NAME VARCHAR2(100) *",
                                "desc ORDERS.NAME \"order name\""
                            );
                            e.After.Single("pk ORDERS *", "pk ORDERS (CODE)");
                        })
                ),
        ];

    /// <summary>説明（表と列の COMMENT ON）の設定・更新・削除</summary>
    private static IEnumerable<ScenarioRow<SyncPlanExpectationInput>> RealOracleDescriptionRows() =>
        [
            OracleDescriptionRoundTripRow(stage: 0),
            OracleDescriptionRoundTripRow(stage: 1),
            OracleDescriptionRoundTripRow(stage: 2),
        ];

    /// <summary>
    /// 表と列の説明の設定（<paramref name="stage"/> 0）・更新（1）・削除（2）。DB の前提は前の段の同期の後と同じ説明を持つ表
    /// </summary>
    private static ExpectationRow OracleDescriptionRoundTripRow(int stage) =>
        Row(
                stage switch
                {
                    0 => "realdb-oracle-description-set",
                    1 => "realdb-oracle-description-update",
                    _ => "realdb-oracle-description-delete",
                },
                "[Integration] B: COMMENT 同期で説明が設定→更新→削除まで往復する",
                DialectNames.Oracle
            )
            .TargetFromLive(s =>
            {
                var (table, column) = RealOracleScenarios.ItemsDescriptions(stage + 1);
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
                r.LiveSql(RealOracleScenarios.ItemsTableSql(stage))
                    .Expect(e =>
                    {
                        var (table, column) = RealOracleScenarios.ItemsDescriptions(stage + 1);
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
                            // Oracle は COMMENT ... IS ''（空文字）が削除に相当する
                            e.After.Absent("desc *");
                        }
                    })
            );
}

/// <summary>Oracle の実 DB の段の図・SQL・選び方の部品</summary>
/// <remarks>
/// Oracle の実行器はスクリプトを SQL*Plus と同じ規則で文に分ける（通常の文は行末の <c>;</c>・PL/SQL ブロックは <c>/</c> だけの行）。
/// そのため 1 つの SQL に複数の文を書くときは、文ごとに改行で区切る。
/// </remarks>
internal static class RealOracleScenarios
{
    /// <summary>
    /// 別のスキーマ OTHER が在れば中のオブジェクトごと撤去し、与えた索引のシステム権限を取り消す（管理者の口で流す）
    /// </summary>
    public const string DropOtherSchemaSql = """
        BEGIN
            EXECUTE IMMEDIATE 'DROP USER OTHER CASCADE';
        EXCEPTION
            WHEN OTHERS THEN
                IF SQLCODE != -1918 THEN
                    RAISE;
                END IF;
        END;
        /
        BEGIN
            EXECUTE IMMEDIATE 'REVOKE DROP ANY INDEX, CREATE ANY INDEX FROM ORACLE';
        EXCEPTION
            WHEN OTHERS THEN
                IF SQLCODE != -1952 THEN
                    RAISE;
                END IF;
        END;
        /
        """;

    /// <summary>
    /// 別のスキーマ OTHER を作り、接続ユーザーへ別のスキーマの主キーを外して付け直すのに要る索引のシステム権限を与える
    /// （管理者の口で流す）
    /// </summary>
    public const string CreateOtherSchemaSql =
        "CREATE USER OTHER IDENTIFIED BY \"other_pw\" QUOTA UNLIMITED ON USERS;\n"
        + "GRANT DROP ANY INDEX, CREATE ANY INDEX TO ORACLE;";

    /// <summary>ORACLE と OTHER の全主キーを「所有者.表 -&gt; 制約名 (構成列)」の形で（管理者の口で読む）</summary>
    public const string PrimaryKeysSql = """
        SELECT c.owner || '.' || c.table_name || ' -> ' || c.constraint_name || ' ('
            || LISTAGG(cc.column_name, ',') WITHIN GROUP (ORDER BY cc.position) || ')'
        FROM all_constraints c
        JOIN all_cons_columns cc ON cc.owner = c.owner AND cc.constraint_name = c.constraint_name
        WHERE c.constraint_type = 'P' AND c.owner IN ('ORACLE', 'OTHER')
        GROUP BY c.owner, c.table_name, c.constraint_name
        """;

    /// <summary>ORACLE と OTHER の全外部キーを「所有者.子の表 -&gt; 制約名」の形で（管理者の口で読む）</summary>
    public const string ForeignKeysSql = """
        SELECT owner || '.' || table_name || ' -> ' || constraint_name
        FROM all_constraints
        WHERE constraint_type = 'R' AND owner IN ('ORACLE', 'OTHER')
        """;

    /// <summary>uq_item(id, code, kind) と、code だけの名前付き一意制約 UQ_uq_item_key</summary>
    public const string UniqueKeyItemSql =
        "CREATE TABLE \"uq_item\" (\"id\" NUMBER(10) NOT NULL, \"code\" VARCHAR2(20) NOT NULL, "
        + "\"kind\" VARCHAR2(20) NOT NULL, "
        + "CONSTRAINT \"PK_uq_item\" PRIMARY KEY (\"id\"), "
        + "CONSTRAINT \"UQ_uq_item_key\" UNIQUE (\"code\"));";

    /// <summary>
    /// 実行器の失敗の文（<c>Sync_Error_OracleStatementFailed</c>）のうち、ODP.NET の例外文の後ろに付く部分適用の説明
    /// （成功した文の数は <c>*</c>）を含む行のパターン
    /// </summary>
    public static string PartialApplyNotePattern =>
        "*"
        + string.Format(
                CultureInfo.InvariantCulture,
                ProviderStrings.Sync_Error_OracleStatementFailed,
                string.Empty,
                "*"
            )
            .Trim()
        + "*";

    /// <summary>選べる項目をすべて選ぶ（選べない案内の項目は選ばない）</summary>
    public static void SelectSelectable(IReadOnlyList<SchemaDiffItem> items)
    {
        foreach (var item in items.Where(i => i.IsSelectable))
        {
            item.IsSelected = true;
        }
    }

    /// <summary>uq_item の一意制約 UQ_uq_item_key の列を (code, kind) へ広げる</summary>
    public static void WidenUniqueToCodeAndKind(ExpectationSchema s) =>
        s["uq_item"].UniqueConstraints.Single().ColumnIds = [
            s.Col("uq_item", "code").Id,
            s.Col("uq_item", "kind").Id,
        ];

    /// <summary>
    /// スキーマ同期の往復の DB の図（<paramref name="stage"/>：0＝説明つきの parent と child(id, parent_id, to_alter, to_drop) と
    /// FK_child_parent（CASCADE）・1＝child を (id, parent_id, to_alter VARCHAR2(100) NOT NULL, added) へ変えた後・
    /// 2＝さらに外部キーを外した後）
    /// </summary>
    public static void SchemaSyncParentChild(ExpectationSchema s, int stage)
    {
        var name = ExpectationSchema.Nullable("name", "VARCHAR2(50)");
        name.Description = "名称";
        s.Table("parent", ExpectationSchema.Pk("id", "NUMBER(10)"), name).Description =
            "親テーブルの説明";

        if (stage == 0)
        {
            s.Table(
                "child",
                ExpectationSchema.Pk("id", "NUMBER(10)"),
                ExpectationSchema.Nullable("parent_id", "NUMBER(10)"),
                ExpectationSchema.Nullable("to_alter", "VARCHAR2(50)"),
                ExpectationSchema.Nullable("to_drop", "NUMBER(10)")
            );
        }
        else
        {
            s.Table(
                "child",
                ExpectationSchema.Pk("id", "NUMBER(10)"),
                ExpectationSchema.Nullable("parent_id", "NUMBER(10)"),
                ExpectationSchema.NotNull("to_alter", "VARCHAR2(100)"),
                ExpectationSchema.Nullable("added", "NUMBER(10)")
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
        "CREATE TABLE \"uq_item\" (\"id\" NUMBER(10) NOT NULL, \"code\" VARCHAR2(20) NOT NULL, "
        + "CONSTRAINT \"PK_uq_item\" PRIMARY KEY (\"id\")"
        + (withUnique ? ", CONSTRAINT \"UQ_uq_item_code\" UNIQUE (\"code\")" : string.Empty)
        + ");";

    /// <summary>
    /// 複合主キー cfk_parent(a, b) と cfk_child(id, a_ref, b_ref)（<paramref name="withForeignKey"/> が真なら
    /// FK_cfk_child_cfk_parent (a_ref, b_ref) → (a, b) も）
    /// </summary>
    public static string CompositeParentChildSql(bool withForeignKey) =>
        "CREATE TABLE \"cfk_parent\" (\"a\" NUMBER(10) NOT NULL, \"b\" NUMBER(10) NOT NULL, "
        + "CONSTRAINT \"PK_cfk_parent\" PRIMARY KEY (\"a\", \"b\"));\n"
        + "CREATE TABLE \"cfk_child\" (\"id\" NUMBER(10) NOT NULL, \"a_ref\" NUMBER(10) NOT NULL, "
        + "\"b_ref\" NUMBER(10) NOT NULL, CONSTRAINT \"PK_cfk_child\" PRIMARY KEY (\"id\")"
        + (
            withForeignKey
                ? ", CONSTRAINT \"FK_cfk_child_cfk_parent\" FOREIGN KEY (\"a_ref\", \"b_ref\") "
                    + "REFERENCES \"cfk_parent\" (\"a\", \"b\")"
                : string.Empty
        )
        + ");";

    /// <summary>
    /// 大文字小文字だけ違う綴りの図：Orders（説明 orders）・Id NUMBER(10) NOT NULL・Name VARCHAR2(100)（説明つき）・
    /// Code VARCHAR2(10) の主キー
    /// </summary>
    public static void MixedCaseOrders(ExpectationSchema s)
    {
        var name = ExpectationSchema.Nullable("Name", "VARCHAR2(100)");
        name.Description = "order name";
        s.Table(
            "Orders",
            ExpectationSchema.NotNull("Id", "NUMBER(10)"),
            name,
            ExpectationSchema.Pk("Code", "VARCHAR2(10)")
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

    /// <summary>items(id, name VARCHAR2(50) NULL) を、段 <paramref name="stage"/> の説明つきで作る</summary>
    public static string ItemsTableSql(int stage)
    {
        var (table, column) = ItemsDescriptions(stage);
        return "CREATE TABLE \"items\" (\"id\" NUMBER(10) NOT NULL, \"name\" VARCHAR2(50) NULL, "
            + "CONSTRAINT \"PK_items\" PRIMARY KEY (\"id\"));"
            + (
                table.Length > 0
                    ? $"\nCOMMENT ON TABLE \"items\" IS '{table.Replace("'", "''")}';"
                    : string.Empty
            )
            + (
                column.Length > 0
                    ? $"\nCOMMENT ON COLUMN \"items\".\"name\" IS '{column.Replace("'", "''")}';"
                    : string.Empty
            );
    }
}
