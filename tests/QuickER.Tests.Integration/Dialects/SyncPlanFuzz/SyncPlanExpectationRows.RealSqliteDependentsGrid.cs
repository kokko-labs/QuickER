using System.Collections.Generic;
using System.Linq;
using QuickER.Dialects;
using QuickER.Model;
using QuickER.Provider;
using QuickER.Tests.TestSupport.Engines;
using S = QuickER.Tests.Integration.Dialects.SyncPlanFuzz.ExpectationSchema;

namespace QuickER.Tests.Integration.Dialects.SyncPlanFuzz;

/// <summary>
/// 実 DB の段：SQLite の計画の段の判定（写しの DB で試すビュー・トリガー・索引の判定と、DB の外の照合順序・関数で作り直せない表）の
/// 入力の空間を、軸の積の表で交差させる行（3 回目の効果測定の XS1〜XS5。台帳の対象外の領域なので、軸の組は表の因子で交差させる）
/// </summary>
/// <remarks>
/// 軸は 5 つの表に分ける。
/// <list type="bullet">
/// <item>表 A（XS1）：写しの DB に置けない表にする原因 × 同じ表の索引 → その表の作り直し</item>
/// <item>表 B（XS3）：同じ原因 × 依存の物（索引 3 種・ビュー・トリガー）× 変更（読む列の削除・ほかの列の削除・列の追加）</item>
/// <item>表 C（XS2）：名前の形（前後の空白・大文字小文字だけ違う双子の列）× 依存の物 → 列の追加と作り直しの外れ方</item>
/// <item>表 D（XS4）：キーの照合順序 × キーの種類 × UPSERT の的に COLLATE を書くか × 変更（列の追加・作り直し）</item>
/// <item>表 E（XS5）：大文字小文字だけ違う表名のトリガー × 表の削除</item>
/// </list>
/// 照合順序 <see cref="SqliteSyncPlanFuzzDialect.TestCollation"/> と関数 <see cref="SqliteSyncPlanFuzzDialect.TestFunction"/> を使う物は、
/// テストの側の接続だけが登録するので、生の準備（RawSetup）で作る（製品の取込・実行器・写しの DB は持たない）。
/// </remarks>
internal static partial class SyncPlanExpectationRows
{
    /// <summary>DB の外の物の表が使う照合順序</summary>
    private static string AppCollation => SqliteSyncPlanFuzzDialect.TestCollation;

    /// <summary>DB の外の物の表が使う関数</summary>
    private static string AppFunction => SqliteSyncPlanFuzzDialect.TestFunction;

    /// <summary>生の準備のほかに置く、LiveSql の決まりを満たす表（行に関わらない）</summary>
    private const string GridAnchorSql =
        "CREATE TABLE \"xs_anchor\" (\"id\" INTEGER NOT NULL, CONSTRAINT \"PK_xs_anchor\" PRIMARY KEY (\"id\"));";

    private static IEnumerable<
        ScenarioRow<SyncPlanExpectationInput>
    > RealSqliteDependentsGridRows() =>
        [
            .. UncopyableTableIndexRows(),
            .. UncopyableTableDependentRows(),
            .. NameFormDependentRows(),
            .. UpsertKeyCollationRows(),
            .. CaseTwinTableTriggerRows(),
            .. VanishingForeignKeyRebuildRows(),
            VanishingForeignKeyParentDropOnlyRow(),
        ];

    /// <summary>写しの DB に置けない表にする原因（Id の部分・列の定義・表に足す句）</summary>
    /// <remarks>
    /// 列の COLLATE が DB の外の照合順序・CHECK が DB の外の関数・STORED の生成列が DB の外の関数。どれも作り直しは落として進む
    /// （列の属性・CHECK・生成列を落とす既存の警告）。表の列は id・a（原因の列）・note・extra・c・d。
    /// </remarks>
    private static IEnumerable<(string Id, string Definition)> UncopyableCauses() =>
        [
            ("column-collation", $"\"a\" TEXT COLLATE {AppCollation} NULL"),
            (
                "check-function",
                $"\"a\" TEXT NULL CHECK ({AppFunction}(\"a\") IS NOT NULL OR \"a\" IS NULL)"
            ),
            (
                "stored-generated-function",
                $"\"a\" TEXT NULL, \"g\" TEXT GENERATED ALWAYS AS ({AppFunction}(\"a\")) STORED"
            ),
        ];

    /// <summary>原因 <paramref name="cause"/> の表 <paramref name="table"/>（id・a・note・extra・c・d と 1 行）</summary>
    private static string UncopyableTableSql(string table, string cause) =>
        $"CREATE TABLE \"{table}\" (\"id\" INTEGER NOT NULL PRIMARY KEY, {cause}, \"note\" TEXT NULL, \"extra\" TEXT NULL, \"c\" INTEGER NULL, \"d\" INTEGER NULL);"
        + $"INSERT INTO \"{table}\" (\"id\", \"a\", \"note\", \"extra\", \"c\", \"d\") VALUES (1, 'a', 'n', '7', 2, 3);";

    /// <summary>
    /// 表 A（XS1）：写しの DB に置けない表の、DB の外の照合順序・関数を使う索引は、その表の作り直しを止める（局所）。普通の索引は止めない
    /// </summary>
    private static IEnumerable<ScenarioRow<SyncPlanExpectationInput>> UncopyableTableIndexRows()
    {
        (string Id, string Sql, bool Blocks)[] indexes =
        [
            (
                "collation-index",
                $"CREATE INDEX \"xa_i\" ON \"xa\" (\"note\" COLLATE {AppCollation});",
                true
            ),
            ("function-index", $"CREATE INDEX \"xa_i\" ON \"xa\" ({AppFunction}(\"note\"));", true),
            (
                "function-partial-index",
                $"CREATE INDEX \"xa_i\" ON \"xa\" (\"note\") WHERE {AppFunction}(\"note\") IS NOT NULL;",
                true
            ),
            ("plain-index", "CREATE INDEX \"xa_i\" ON \"xa\" (\"note\");", false),
        ];

        foreach (var (causeId, cause) in UncopyableCauses())
        {
            foreach (var (indexId, indexSql, blocks) in indexes)
            {
                yield return Row(
                        $"realdb-grid-uncopyable-{causeId}-{indexId}-own-table-rebuild",
                        $"[Integration] SQLite 同期: 写しの DB に置けない表（{causeId}）の {indexId} は、その表の作り直しを"
                            + (blocks ? "外して名指しする" : "止めずにコミットする"),
                        DialectNames.Sqlite
                    )
                    .TargetFromLive(s => s.Col("xa", "extra").DataType = "VARCHAR(50)")
                    .Select(RealScenarios.SelectAll)
                    .OnRealDatabase(r =>
                        r.LiveSql(GridAnchorSql)
                            .RawSetup(UncopyableTableSql("xa", cause) + indexSql)
                            .Expect(e =>
                            {
                                e.Outcome.Exact("committed");

                                if (blocks)
                                {
                                    e.Warnings.Contains(
                                        "TableRebuildBlockedByExternalDefinition(xa): *xa_i*"
                                    );
                                    e.Excluded.Exact("AlterColumn xa.extra");
                                    e.Unchanged();
                                }
                                else
                                {
                                    e.Warnings.Absent("TableRebuildBlockedByExternalDefinition*");
                                    e.Excluded.Empty();
                                    e.After.Contains("col xa.extra VARCHAR(50) *");
                                }
                            })
                    )
                    .Tagged(
                        SqliteJudgementFinding(
                            "XS1",
                            "中・台帳の対象外の領域（SQLite の計画の段の判定）・軸の組（置けない原因 × 同じ表の索引）は表の因子で交差"
                        )
                    );
            }
        }
    }

    /// <summary>
    /// 表 B（XS3）：写しの DB に置けない表の依存の物。索引は名前だけの表の上で判定し、読む列の削除を外して名指しする。ビュー・トリガーは
    /// 写せない表の上では判定せず（割り切り）、実行器の前後の比較が巻き戻して名指しする
    /// </summary>
    private static IEnumerable<ScenarioRow<SyncPlanExpectationInput>> UncopyableTableDependentRows()
    {
        (string Id, string Sql, bool IsIndex)[] dependents =
        [
            ("plain-index", "CREATE INDEX \"xb_i\" ON \"xb\" (\"c\");", true),
            ("expression-index", "CREATE INDEX \"xb_i\" ON \"xb\" (abs(\"c\"));", true),
            ("partial-index", "CREATE INDEX \"xb_i\" ON \"xb\" (\"id\") WHERE \"c\" > 0;", true),
            ("view", "CREATE VIEW \"xb_v\" AS SELECT \"id\", \"c\" FROM \"xb\";", false),
            (
                "trigger",
                "CREATE TRIGGER \"xb_t\" AFTER INSERT ON \"xb_src\" BEGIN UPDATE \"xb\" SET \"c\" = NEW.\"id\"; END;",
                false
            ),
        ];

        foreach (var (causeId, cause) in UncopyableCauses())
        {
            foreach (var (dependentId, dependentSql, isIndex) in dependents)
            {
                // 依存の物が読む列 c を消す
                yield return UncopyableDependentRow(
                        causeId,
                        cause,
                        dependentId,
                        dependentSql,
                        "drop-read-column"
                    )
                    .TargetFromLive(s => s["xb"].Columns.RemoveAll(c => c.Name == "c"))
                    .Select(RealScenarios.SelectAll)
                    .OnRealDatabase(r =>
                    {
                        var stage = r.LiveSql(UncopyableSourceSql)
                            .RawSetup(UncopyableTableSql("xb", cause) + dependentSql);

                        if (isIndex)
                        {
                            stage.Expect(e =>
                            {
                                e.Outcome.Exact("committed");
                                e.Warnings.Contains("TableChangeBreaksDependentIndex(xb): xb_i");
                                e.Excluded.Exact("DropColumn xb.c");
                                e.Unchanged();
                            });
                        }
                        else if (IsUncheckedTriggerOnGeneratedFunctionTable(causeId, dependentId))
                        {
                            // 今の振る舞いの固定（直す区切りは未起動）：QuickER の接続では DB の外の関数の生成列を持つ表への書き込みの準備が
                            // 同期の前から断られ、実行器もトリガーを「前から壊れている」と数えるので、c を参照するトリガーを残してコミットする。
                            // 直すとこの行が赤になる（作らない形の宣言の Pending `sqlite-trigger-writing-generated-function-table`）
                            stage
                                .Query(
                                    "trigger",
                                    "SELECT sql FROM sqlite_master WHERE name = 'xb_t';"
                                )
                                .Expect(e =>
                                {
                                    e.Outcome.Exact("committed");
                                    e.Warnings.Absent("TableChangeBreaksDependentTrigger*");
                                    e.Excluded.Empty();
                                    e.After.Absent("col xb.c *");
                                    e.Probe("trigger").Exact("*SET \"c\" = NEW.\"id\"*");
                                });
                        }
                        else
                        {
                            stage
                                .ExpectsUnwarnedFailure(
                                    "モデルの外の物：写しの DB に置けない表の上のビュー・トリガーは計画の段で判定しない（割り切り）。実行器の前後の比較が巻き戻して名指しする"
                                )
                                .Expect(e =>
                                {
                                    e.Outcome.Exact("failed: *xb_*");
                                    e.Unchanged();
                                });
                        }
                    })
                    .Tagged(
                        IsUncheckedTriggerOnGeneratedFunctionTable(causeId, dependentId)
                            ? UncheckedTriggerOnGeneratedFunctionTableTag()
                            : UncopyableDependentTag()
                    );

                // 依存の物が読まない列 d を消す
                yield return UncopyableDependentRow(
                        causeId,
                        cause,
                        dependentId,
                        dependentSql,
                        "drop-other-column"
                    )
                    .TargetFromLive(s => s["xb"].Columns.RemoveAll(c => c.Name == "d"))
                    .Select(RealScenarios.SelectAll)
                    .OnRealDatabase(r =>
                        r.LiveSql(UncopyableSourceSql)
                            .RawSetup(UncopyableTableSql("xb", cause) + dependentSql)
                            .Expect(e =>
                            {
                                e.Outcome.Exact("committed");
                                e.Warnings.Absent("TableChangeBreaksDependent*");
                                e.Excluded.Empty();
                                e.After.Absent("col xb.d *");
                            })
                    )
                    .Tagged(UncopyableDependentTag());

                // 列を足す
                yield return UncopyableDependentRow(
                        causeId,
                        cause,
                        dependentId,
                        dependentSql,
                        "add-column"
                    )
                    .TargetFromLive(s => s["xb"].Columns.Add(S.Nullable("x", "TEXT")))
                    .Select(RealScenarios.SelectAll)
                    .OnRealDatabase(r =>
                        r.LiveSql(UncopyableSourceSql)
                            .RawSetup(UncopyableTableSql("xb", cause) + dependentSql)
                            .Expect(e =>
                            {
                                e.Outcome.Exact("committed");
                                e.Warnings.Absent("TableChangeBreaksDependent*");
                                e.Excluded.Empty();
                                e.After.Contains("col xb.x *");
                            })
                    )
                    .Tagged(UncopyableDependentTag());
            }
        }
    }

    /// <summary>表 B のトリガーが発火する表（LiveSql で作る）</summary>
    private const string UncopyableSourceSql =
        "CREATE TABLE \"xb_src\" (\"id\" INTEGER NOT NULL, CONSTRAINT \"PK_xb_src\" PRIMARY KEY (\"id\"));";

    private static ExpectationRow UncopyableDependentRow(
        string causeId,
        string cause,
        string dependentId,
        string dependentSql,
        string changeId
    ) =>
        Row(
            $"realdb-grid-uncopyable-{causeId}-{dependentId}-{changeId}",
            $"[Integration] SQLite 同期: 写しの DB に置けない表（{causeId}）の {dependentId} と変更 {changeId}",
            DialectNames.Sqlite
        );

    /// <summary>
    /// DB の外の関数の STORED の生成列を持つ表へ書くトリガー × 読む列の削除（計画の段も実行器の前後の比較も拾わない形。今の振る舞いを固定する）
    /// </summary>
    private static bool IsUncheckedTriggerOnGeneratedFunctionTable(
        string causeId,
        string dependentId
    ) => causeId == "stored-generated-function" && dependentId == "trigger";

    private static RowTag UncheckedTriggerOnGeneratedFunctionTableTag() =>
        RowTag
            .Reason(
                RowTagKind.DocumentedCharacteristic,
                $"今の振る舞いの固定・直す区切りは未起動（{Measure3Record} v6 の区切り d で見つけた形）：DB の外の関数の生成列を持つ表へ書くトリガーは、"
                    + "実行器の前後の比較も「前から壊れている」と数えて拾わない（作らない形の宣言の Pending sqlite-trigger-writing-generated-function-table）"
            )
            .Except(DialectNames.SqlServer, SqliteJudgementOnly)
            .Except(DialectNames.PostgreSql, SqliteJudgementOnly)
            .Except(DialectNames.MySql, SqliteJudgementOnly)
            .Except(DialectNames.Oracle, SqliteJudgementOnly);

    private static RowTag UncopyableDependentTag() =>
        SqliteJudgementFinding(
            "XS3",
            "写せない表の索引の判定（XS1 と同じ直し）・軸の組（置けない原因 × 依存の物 × 変更）は表の因子で交差"
        );

    /// <summary>
    /// 表 C（XS2）：取込が名前を変えて持ち帰る列（前後に空白のある列・大文字小文字だけ違う双子の列）を DB の綴りで読むビュー・トリガー・索引は、
    /// 列の追加で壊れない。作り直しは取込が捨てた物のある表として外れ、ビュー・トリガーの規則は名指ししない
    /// </summary>
    private static IEnumerable<ScenarioRow<SyncPlanExpectationInput>> NameFormDependentRows()
    {
        (string Id, string Columns, string Read)[] forms =
        [
            ("padded-column", "\" c \" TEXT NULL", "\" c \""),
            ("case-twin-discarded", "\"Ä\" TEXT NULL, \"ä\" TEXT NULL", "\"ä\""),
            ("case-twin-kept", "\"Ä\" TEXT NULL, \"ä\" TEXT NULL", "\"Ä\""),
            ("non-ascii-column", "\"café\" TEXT NULL", "\"café\""),
        ];

        foreach (var (formId, columns, read) in forms)
        {
            (string Id, string Sql)[] dependents =
            [
                ("view", $"CREATE VIEW \"nv_v\" AS SELECT \"nv\".{read} FROM \"nv\";"),
                (
                    "trigger",
                    $"CREATE TRIGGER \"nv_t\" AFTER INSERT ON \"nv_src\" BEGIN INSERT INTO \"nv\" (\"id\", {read}) VALUES (NEW.\"id\", 'x'); END;"
                ),
                ("index", $"CREATE INDEX \"nv_i\" ON \"nv\" ({read});"),
            ];

            foreach (var (dependentId, dependentSql) in dependents)
            {
                yield return Row(
                        $"realdb-grid-name-form-{formId}-{dependentId}-add-column-is-not-named",
                        $"[Integration] SQLite 同期: {formId} を DB の綴りで読む {dependentId} は、列の追加で名指しせずにコミットする",
                        DialectNames.Sqlite
                    )
                    .TargetFromLive(s => s["nv"].Columns.Add(S.Nullable("x", "TEXT")))
                    .Select(RealScenarios.SelectAll)
                    .OnRealDatabase(r =>
                        r.LiveSql(NameFormTableSql(columns) + dependentSql)
                            .Expect(e =>
                            {
                                e.Outcome.Exact("committed");
                                e.Warnings.Absent("TableChangeBreaksDependent*");
                                e.Excluded.Empty();
                                e.After.Contains("col nv.x *");
                            })
                    )
                    .Tagged(NameFormTag());
            }
        }

        // 作り直しは取込が捨てた物のある表として外れる（ビューは名前を変えた列を読み続けるので、ビューの規則は名指ししない）
        foreach (
            var (formId, columns, read) in forms.Where(f =>
                f.Id is "padded-column" or "case-twin-discarded"
            )
        )
        {
            yield return Row(
                    $"realdb-grid-name-form-{formId}-view-drop-other-column-is-named-as-import-discarded",
                    $"[Integration] SQLite 同期: {formId} を読むビューの表の別の列の削除は、取込が捨てた物のある表として外して名指しし、ビューは名指ししない",
                    DialectNames.Sqlite
                )
                .TargetFromLive(s => s["nv"].Columns.RemoveAll(c => c.Name == "o"))
                .Select(RealScenarios.SelectAll)
                .OnRealDatabase(r =>
                    r.LiveSql(
                            NameFormTableSql(columns)
                                + $"CREATE VIEW \"nv_v\" AS SELECT \"nv\".{read} FROM \"nv\";"
                        )
                        .Expect(e =>
                        {
                            e.Outcome.Exact("committed");
                            e.Warnings.Contains("TableRebuildLosesImportDiscardedContent(nv): *");
                            e.Warnings.Absent("TableChangeBreaksDependent*");
                            e.Excluded.Exact("DropColumn nv.o");
                            e.Unchanged();
                        })
                )
                .Tagged(NameFormTag());
        }
    }

    /// <summary>表 C の表 nv（id・名前の形の列・o と 1 行）と、トリガーが発火する表 nv_src</summary>
    private static string NameFormTableSql(string columns) =>
        $"CREATE TABLE \"nv\" (\"id\" INTEGER NOT NULL PRIMARY KEY, {columns}, \"o\" TEXT NULL);"
        + "INSERT INTO \"nv\" (\"id\", \"o\") VALUES (1, 'o');"
        + "CREATE TABLE \"nv_src\" (\"id\" INTEGER NOT NULL PRIMARY KEY);";

    private static RowTag NameFormTag() =>
        SqliteJudgementFinding(
            "XS2",
            "中・台帳の対象外の領域（SQLite の計画の段の判定）・軸の組（名前の形 × 依存の物 × 変更）は表の因子で交差"
        );

    /// <summary>
    /// 表 D（XS4）：照合順序つきのキーを的にする UPSERT のトリガーは、列の追加（作り直さない）では壊れない。作り直し（ほかの列の削除）は
    /// 列の COLLATE を落とすので、的に COLLATE を書いたトリガーだけが壊れ、計画がその削除を外して名指しする
    /// </summary>
    private static IEnumerable<ScenarioRow<SyncPlanExpectationInput>> UpsertKeyCollationRows()
    {
        string[] collations = ["NOCASE", "RTRIM"];
        (string Id, string Table)[] keys =
        [
            (
                "unique",
                "CREATE TABLE \"kc\" (\"id\" INTEGER NOT NULL PRIMARY KEY, \"code\" TEXT COLLATE {0} NULL UNIQUE, \"memo\" TEXT NULL, \"extra\" TEXT NULL);"
            ),
            (
                "primary-key",
                "CREATE TABLE \"kc\" (\"code\" TEXT COLLATE {0} NOT NULL PRIMARY KEY, \"memo\" TEXT NULL, \"extra\" TEXT NULL);"
            ),
        ];

        foreach (var collation in collations)
        {
            foreach (var (keyId, table) in keys)
            {
                foreach (var targetCollates in new[] { true, false })
                {
                    var target = targetCollates ? $"\"code\" COLLATE {collation}" : "\"code\"";
                    var setup =
                        string.Format(
                            System.Globalization.CultureInfo.InvariantCulture,
                            table,
                            collation
                        )
                        + "CREATE TABLE \"kc_src\" (\"id\" INTEGER NOT NULL PRIMARY KEY, \"k\" TEXT NULL);"
                        + "CREATE TRIGGER \"kc_t\" AFTER INSERT ON \"kc_src\" BEGIN INSERT INTO \"kc\" (\"code\", \"memo\") VALUES (NEW.\"k\", 'm') "
                        + $"ON CONFLICT ({target}) DO NOTHING; END;";
                    var shape =
                        $"{collation.ToLowerInvariant()}-{keyId}-target-"
                        + (targetCollates ? "with-collate" : "without-collate");

                    yield return Row(
                            $"realdb-grid-upsert-key-{shape}-add-column-is-not-named",
                            $"[Integration] SQLite 同期: {shape} の UPSERT のトリガーは、列の追加（作り直さない）で名指しせずにコミットする",
                            DialectNames.Sqlite
                        )
                        .TargetFromLive(s => s["kc"].Columns.Add(S.Nullable("x", "TEXT")))
                        .Select(RealScenarios.SelectAll)
                        .OnRealDatabase(r =>
                            r.LiveSql(setup)
                                .Query(
                                    "fire",
                                    "INSERT INTO \"kc_src\" (\"id\", \"k\") VALUES (1, 'a');"
                                )
                                .Expect(e =>
                                {
                                    e.Outcome.Exact("committed");
                                    e.Warnings.Absent("TableChangeBreaksDependentTrigger*");
                                    e.Excluded.Empty();
                                    e.After.Contains("col kc.x *");
                                    e.Probe("fire").Empty();
                                })
                        )
                        .Tagged(UpsertKeyTag());

                    yield return Row(
                            $"realdb-grid-upsert-key-{shape}-drop-other-column-"
                                + (targetCollates ? "is-named" : "is-not-named"),
                            $"[Integration] SQLite 同期: {shape} の UPSERT のトリガーの表の作り直し（ほかの列の削除）は、"
                                + (
                                    targetCollates
                                        ? "COLLATE を落とすとトリガーが壊れるので外して名指しする"
                                        : "トリガーを壊さないのでコミットする"
                                ),
                            DialectNames.Sqlite
                        )
                        .TargetFromLive(s => s["kc"].Columns.RemoveAll(c => c.Name == "extra"))
                        .Select(RealScenarios.SelectAll)
                        .OnRealDatabase(r =>
                            r.LiveSql(setup)
                                .Expect(e =>
                                {
                                    e.Outcome.Exact("committed");

                                    if (targetCollates)
                                    {
                                        e.Warnings.Contains(
                                            "TableChangeBreaksDependentTrigger(kc): kc_t"
                                        );
                                        e.Excluded.Exact("DropColumn kc.extra");
                                        e.Unchanged();
                                    }
                                    else
                                    {
                                        e.Warnings.Absent("TableChangeBreaksDependentTrigger*");
                                        e.Excluded.Empty();
                                        e.After.Absent("col kc.extra *");
                                    }
                                })
                        )
                        .Tagged(UpsertKeyTag());

                    // 列の名前の集合を変えない作り直し（ほかの列の型の変更）は判定器を通らない。的に COLLATE を書いたトリガーは作り直しで
                    // 壊れ、実行器の前後の比較が巻き戻して名指しする（作らない形の宣言の Pending
                    // `sqlite-rebuild-dropping-collation-relied-on-by-trigger`）
                    var typeChangeRow = Row(
                            $"realdb-grid-upsert-key-{shape}-type-change-other-column-"
                                + (targetCollates ? "rolls-back" : "commits"),
                            $"[Integration] SQLite 同期: {shape} の UPSERT のトリガーの表の作り直し（ほかの列の型の変更）は、"
                                + (
                                    targetCollates
                                        ? "COLLATE を落としてトリガーを壊すので実行器が巻き戻して名指しする"
                                        : "トリガーを壊さないのでコミットする"
                                ),
                            DialectNames.Sqlite
                        )
                        .TargetFromLive(s => s.Col("kc", "extra").DataType = "VARCHAR(50)")
                        .Select(RealScenarios.SelectAll);

                    yield return (
                        targetCollates
                            ? typeChangeRow.OnRealDatabase(r =>
                                r.LiveSql(setup)
                                    .ExpectsUnwarnedFailure(
                                        "モデルの外の物：作り直しが落とす照合順序に頼る UPSERT のトリガー（計画の段は列の名前の集合を変えない作り直しを判定しない）。実行器の前後の比較が巻き戻して名指しする"
                                    )
                                    .Expect(e =>
                                    {
                                        e.Outcome.Exact("failed: *kc_t*");
                                        e.Unchanged();
                                    })
                            )
                            : typeChangeRow.OnRealDatabase(r =>
                                r.LiveSql(setup)
                                    .Expect(e =>
                                    {
                                        e.Outcome.Exact("committed");
                                        e.Warnings.Absent("TableChangeBreaksDependentTrigger*");
                                        e.After.Contains("col kc.extra VARCHAR(50) *");
                                    })
                            )
                    ).Tagged(UpsertKeyTag());

                    // 作り直す理由になる項目が解決できない表は作り直さない（製品の再構築の合成の判定）。列 x の追加と、選ばなかった新しい列 y
                    // への一意制約の追加を同じ表で選ぶと、一意制約の追加は合成で解決できず（依存の除外が後で外す）、表は作り直さないので、
                    // 判定器は列の追加の文で試して名指ししない
                    if (targetCollates)
                    {
                        yield return Row(
                                $"realdb-grid-upsert-key-{shape}-add-column-with-unresolvable-unique-add-is-not-named",
                                $"[Integration] SQLite 同期: {shape} の UPSERT のトリガーの表へ、解決できない一意制約の追加と一緒に列を足す同期（作り直さない）は、名指しせずに列を足す",
                                DialectNames.Sqlite
                            )
                            .TargetFromLive(s =>
                            {
                                s["kc"].Columns.Add(S.Nullable("x", "TEXT"));
                                s["kc"].Columns.Add(S.Nullable("y", "TEXT"));
                                s.Unique("kc", "uq_kc_y", "y");
                            })
                            .Select(items =>
                            {
                                foreach (var item in items)
                                {
                                    item.IsSelected = !(
                                        item.Kind == SchemaDiffKind.AddColumn
                                        && item.ColumnName == "y"
                                    );
                                }
                            })
                            .OnRealDatabase(r =>
                                r.LiveSql(setup)
                                    .Expect(e =>
                                    {
                                        e.Outcome.Exact("committed");
                                        e.Warnings.Contains(
                                            "SelectedItemRequiresUnselectedItem(kc): *"
                                        );
                                        e.Warnings.Absent("TableChangeBreaksDependentTrigger*");
                                        e.Excluded.Exact("AddUniqueConstraint kc uq=uq_kc_y");
                                        e.After.Contains("col kc.x *");
                                    })
                            )
                            .Tagged(UpsertKeyTag());
                    }

                    // 列の追加と型の変更を同じ表で選ぶと作り直す（製品の再構築の合成の判定）。判定器は作り直しの形で試し、的に COLLATE を
                    // 書いたトリガーを壊すので列の追加を外して名指しする。型の変更は流れて作り直しがトリガーを壊し、実行器が巻き戻す
                    // （列の名前の集合を変えない作り直しの形＝作らない形の宣言の Pending と同じ）
                    var addAndTypeChangeRow = Row(
                            $"realdb-grid-upsert-key-{shape}-add-column-with-type-change-"
                                + (targetCollates ? "is-named-and-rolls-back" : "commits"),
                            $"[Integration] SQLite 同期: {shape} の UPSERT のトリガーの表へ、型の変更と一緒に列を足す同期（作り直す）は、"
                                + (
                                    targetCollates
                                        ? "作り直しの形で試して列の追加を名指しして外し、型の変更の作り直しを実行器が巻き戻す"
                                        : "トリガーを壊さないのでコミットする"
                                ),
                            DialectNames.Sqlite
                        )
                        .TargetFromLive(s =>
                        {
                            s["kc"].Columns.Add(S.Nullable("x", "TEXT"));
                            s.Col("kc", "extra").DataType = "VARCHAR(50)";
                        })
                        .Select(RealScenarios.SelectAll);

                    yield return (
                        targetCollates
                            ? addAndTypeChangeRow.OnRealDatabase(r =>
                                r.LiveSql(setup)
                                    .ExpectsUnwarnedFailure(
                                        "モデルの外の物：作り直しが落とす照合順序に頼る UPSERT のトリガー（計画は列の追加を外すが、型の変更の作り直しは流れる）。実行器の前後の比較が巻き戻して名指しする"
                                    )
                                    .Expect(e =>
                                    {
                                        e.Warnings.Contains(
                                            "TableChangeBreaksDependentTrigger(kc): kc_t"
                                        );
                                        e.Excluded.Exact("AddColumn kc.x");
                                        e.Outcome.Exact("failed: *kc_t*");
                                        e.Unchanged();
                                    })
                            )
                            : addAndTypeChangeRow.OnRealDatabase(r =>
                                r.LiveSql(setup)
                                    .Expect(e =>
                                    {
                                        e.Outcome.Exact("committed");
                                        e.Warnings.Absent("TableChangeBreaksDependentTrigger*");
                                        e.Excluded.Empty();
                                        e.After.Contains(
                                            "col kc.x *",
                                            "col kc.extra VARCHAR(50) *"
                                        );
                                    })
                            )
                    ).Tagged(UpsertKeyTag());
                }
            }
        }
    }

    private static RowTag UpsertKeyTag() =>
        SqliteJudgementFinding(
            "XS4",
            "中・台帳の対象外の領域（SQLite の計画の段の判定）・軸の組（キーの照合順序 × キーの種類 × 的の COLLATE × 変更）は表の因子で交差"
        );

    /// <summary>
    /// 表 E（XS5）：大文字小文字だけ違う表名（SQLite は ASCII の外を畳まないので別の表）。図が採った表を消すと、捨てた表のトリガーが
    /// 採った表へ書くなら壊れるので名指しして外す
    /// </summary>
    /// <remarks>
    /// 名指ししない側（捨てた表のトリガーが採った表へ書かない）は行にしない：採った表の削除がコミットすると、取り込み直しで捨てた表が
    /// 図に現れ、汎用の表明 2（選んだ項目が消える）がいつも落ちる（土台の性質）。消した表自身のトリガーを数えない側は、既存の行
    /// <c>realdb-trigger-same-table-drop-table-is-not-named</c> が受け持つ。
    /// </remarks>
    /// <summary>
    /// 周の後に足す暗黙の項目（端が消える外部キーの削除）だけで作り直す表への列の追加。親の表の削除を選び、外部キーの削除を選ばないと、
    /// 子の表は暗黙の外部キーの削除で作り直す。判定器へ渡す作り直す表は除外の周の並び（作り直しの展開を含む）から求めるので、この子の表は
    /// 作り直すと数えず、列の追加の文で試す
    /// </summary>
    /// <remarks>
    /// 的に COLLATE を書いた UPSERT のトリガーは作り直しで壊れ、計画の段は名指しせず、実行器の前後の比較が巻き戻して名指しする（今の振る舞いの
    /// 固定。作り直す表に数えても、列の追加を外すだけで作り直しは流れるので巻き戻りは変わらない。作らない形の宣言の Pending
    /// <c>sqlite-rebuild-dropping-collation-relied-on-by-trigger</c>）。COLLATE を書かない側はコミットする。
    /// </remarks>
    private static IEnumerable<
        ScenarioRow<SyncPlanExpectationInput>
    > VanishingForeignKeyRebuildRows()
    {
        foreach (var targetCollates in new[] { true, false })
        {
            var target = targetCollates ? "\"code\" COLLATE NOCASE" : "\"code\"";
            var setup =
                "CREATE TABLE \"vk_p\" (\"id\" INTEGER NOT NULL PRIMARY KEY);"
                + "CREATE TABLE \"vk_c\" (\"id\" INTEGER NOT NULL PRIMARY KEY, \"code\" TEXT COLLATE NOCASE NULL UNIQUE, \"pid\" INTEGER NULL, "
                + "CONSTRAINT \"fk_vk_c_p\" FOREIGN KEY (\"pid\") REFERENCES \"vk_p\" (\"id\"));"
                + "CREATE TABLE \"vk_src\" (\"id\" INTEGER NOT NULL PRIMARY KEY, \"k\" TEXT NULL);"
                + "CREATE TRIGGER \"vk_t\" AFTER INSERT ON \"vk_src\" BEGIN INSERT INTO \"vk_c\" (\"code\") VALUES (NEW.\"k\") "
                + $"ON CONFLICT ({target}) DO NOTHING; END;";
            var row = Row(
                    "realdb-grid-vanishing-fk-rebuild-add-column-target-"
                        + (targetCollates ? "with-collate-rolls-back" : "without-collate-commits"),
                    "[Integration] SQLite 同期: 親の表の削除で暗黙に外部キーを外して作り直す子の表への列の追加は、作り直すと数えずに列の追加の文で試し、"
                        + (
                            targetCollates
                                ? "COLLATE を落とすとトリガーが壊れるので実行器が巻き戻して名指しする"
                                : "トリガーを壊さないのでコミットする"
                        ),
                    DialectNames.Sqlite
                )
                .TargetFromLive(s =>
                {
                    s.Relationships.RemoveAll(rel => rel.ConstraintName == "fk_vk_c_p");
                    s.Entities.Remove(s["vk_p"]);
                    s["vk_c"].Columns.Add(S.Nullable("x", "TEXT"));
                })
                .Select(items =>
                {
                    foreach (var item in items)
                    {
                        item.IsSelected = item.Kind != SchemaDiffKind.DropForeignKey;
                    }
                });

            yield return targetCollates
                ? row.OnRealDatabase(r =>
                        r.LiveSql(setup)
                            .ExpectsUnwarnedFailure(
                                "モデルの外の物：作り直しが落とす照合順序に頼る UPSERT のトリガー（暗黙の外部キーの削除で作り直す表は、計画の段が作り直すと数えない）。実行器の前後の比較が巻き戻して名指しする"
                            )
                            .Expect(e =>
                            {
                                e.Warnings.Absent("TableChangeBreaksDependentTrigger*");
                                e.Outcome.Exact("failed: *vk_t*");
                                e.Unchanged();
                            })
                    )
                    .Tagged(UncountedRebuildTag())
                : row.OnRealDatabase(r =>
                        r.LiveSql(setup)
                            .Expect(e =>
                            {
                                e.Outcome.Exact("committed");
                                e.Warnings.Absent("TableChangeBreaksDependentTrigger*");
                                e.Excluded.Empty();
                                e.After.Absent("table vk_p");
                                e.After.Contains("col vk_c.x *");
                            })
                    )
                    .Tagged(UpsertKeyTag());
        }
    }

    /// <summary>
    /// 親の表の削除だけ（子の表は列を変えず、暗黙の外部キーの削除で作り直す）。写しの子の表は外部キーを持ったまま残るので、写しの接続が外部キーを
    /// 検査すると、子へ書くトリガーを「消えた親を参照する」と数えて親の表の削除を外す。実行器と同じく外部キーの検査を切って試す
    /// </summary>
    private static ScenarioRow<SyncPlanExpectationInput> VanishingForeignKeyParentDropOnlyRow() =>
        Row(
                "realdb-grid-vanishing-fk-parent-drop-only-trigger-writing-child-is-not-named",
                "[Integration] SQLite 同期: 子の表へ書くトリガーがあっても、親の表の削除（子の表は暗黙に外部キーを外して作り直す）は名指しせずにコミットする",
                DialectNames.Sqlite
            )
            .TargetFromLive(s =>
            {
                s.Relationships.RemoveAll(rel => rel.ConstraintName == "fk_vk_c_p");
                s.Entities.Remove(s["vk_p"]);
            })
            .Select(items =>
            {
                foreach (var item in items)
                {
                    item.IsSelected = item.Kind != SchemaDiffKind.DropForeignKey;
                }
            })
            .OnRealDatabase(r =>
                r.LiveSql(
                        "CREATE TABLE \"vk_p\" (\"id\" INTEGER NOT NULL PRIMARY KEY);"
                            + "CREATE TABLE \"vk_c\" (\"id\" INTEGER NOT NULL PRIMARY KEY, \"code\" TEXT NULL, \"pid\" INTEGER NULL, "
                            + "CONSTRAINT \"fk_vk_c_p\" FOREIGN KEY (\"pid\") REFERENCES \"vk_p\" (\"id\"));"
                            + "CREATE TABLE \"vk_src\" (\"id\" INTEGER NOT NULL PRIMARY KEY, \"k\" TEXT NULL);"
                            + "CREATE TRIGGER \"vk_t\" AFTER INSERT ON \"vk_src\" BEGIN INSERT INTO \"vk_c\" (\"code\") VALUES (NEW.\"k\"); END;"
                    )
                    .Query("fire", "INSERT INTO \"vk_src\" (\"id\", \"k\") VALUES (1, 'a');")
                    .Expect(e =>
                    {
                        e.Outcome.Exact("committed");
                        e.Warnings.Absent("TableChangeBreaksDependentTrigger*");
                        e.Excluded.Empty();
                        e.After.Absent("table vk_p");
                        e.Probe("fire").Empty();
                    })
            )
            .Tagged(
                SqliteJudgementFinding(
                    "XS10",
                    "写しの接続が外部キーを検査し、実行器（foreign_keys=OFF）と食い違う（区切り d で見つけた形）。軸（暗黙の外部キーの削除で作り直す子 × 列の追加の有無 × 的の COLLATE）は表の因子で交差"
                )
            );

    private static RowTag UncountedRebuildTag() =>
        RowTag
            .Reason(
                RowTagKind.DocumentedCharacteristic,
                $"今の振る舞いの固定・直す区切りは未起動（{Measure3Record} v6 の区切り d の隣の枝）：周の後に足す暗黙の外部キーの削除だけで作り直す表は、"
                    + "判定器へ渡す作り直す表に数えない（作らない形の宣言の Pending sqlite-rebuild-dropping-collation-relied-on-by-trigger）"
            )
            .Except(DialectNames.SqlServer, SqliteJudgementOnly)
            .Except(DialectNames.PostgreSql, SqliteJudgementOnly)
            .Except(DialectNames.MySql, SqliteJudgementOnly)
            .Except(DialectNames.Oracle, SqliteJudgementOnly);

    private static IEnumerable<ScenarioRow<SyncPlanExpectationInput>> CaseTwinTableTriggerRows()
    {
        yield return Row(
                "realdb-grid-case-twin-table-trigger-writes-dropped-table-drop-table-is-named",
                "[Integration] SQLite 同期: 大文字小文字だけ違う表名の捨てた表のトリガーが採った表へ書くなら、採った表の削除は壊すので名指しして外す",
                DialectNames.Sqlite
            )
            .TargetFromLive(s => s.Entities.Remove(s["Ä"]))
            .Select(RealScenarios.SelectAll)
            .OnRealDatabase(r =>
                r.LiveSql(
                        "CREATE TABLE \"Ä\" (\"id\" INTEGER NOT NULL PRIMARY KEY, \"v\" TEXT NULL);"
                            + "CREATE TABLE \"ä\" (\"id\" INTEGER NOT NULL PRIMARY KEY, \"v\" TEXT NULL);"
                            + "CREATE TRIGGER \"tw_t\" AFTER INSERT ON \"ä\" BEGIN INSERT INTO \"Ä\" (\"id\", \"v\") VALUES (NEW.\"id\", 'x'); END;"
                    )
                    .Expect(e =>
                    {
                        e.Outcome.Exact("committed");
                        e.Warnings.Contains("TableChangeBreaksDependentTrigger(Ä): tw_t");
                        e.Excluded.Exact("DropTable Ä");
                        e.Unchanged();
                    })
            )
            .Tagged(
                SqliteJudgementFinding(
                    "XS5",
                    "消した表のトリガーを外す絞り込みの名前の比べ方。軸（表名の形）は SQLite だけが持つ判定で、水準の表は名前の形を持たない"
                )
            );
    }

    /// <summary>SQLite の計画の段の判定の指摘の札（ほかの 4 方言は判定を持たないので外す）</summary>
    private static RowTag SqliteJudgementFinding(string finding, string what) =>
        RowTag
            .Reason(RowTagKind.ReviewFinding, $"{finding}（{Measure3Record} v6）：{what}")
            .Except(DialectNames.SqlServer, SqliteJudgementOnly)
            .Except(DialectNames.PostgreSql, SqliteJudgementOnly)
            .Except(DialectNames.MySql, SqliteJudgementOnly)
            .Except(DialectNames.Oracle, SqliteJudgementOnly);

    private const string SqliteJudgementOnly =
        "SQLite の計画の段の判定（写しの DB のビュー・トリガー・索引の判定器と、作り直しを止める物）だけ。ほかの方言は判定を持たない";
}
