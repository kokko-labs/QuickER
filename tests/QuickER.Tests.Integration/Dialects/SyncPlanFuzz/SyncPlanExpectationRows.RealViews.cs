using System;
using System.Collections.Generic;
using System.Linq;
using QuickER.Dialects;
using QuickER.Tests.TestSupport.Engines;

namespace QuickER.Tests.Integration.Dialects.SyncPlanFuzz;

internal static partial class SyncPlanExpectationRows
{
    /// <summary>
    /// SQLite 以外の方言のビューの実 DB の段の行のうち、その方言で流す行の数の下限（行を足したら・消したら合わせる。方言の行のファイルの数に足す）
    /// </summary>
    private static int RealViewRowCount(string dialect) =>
        dialect switch
        {
            DialectNames.SqlServer => 8,
            DialectNames.PostgreSql => 6,
            DialectNames.MySql => 6,
            DialectNames.Oracle => 6,
            _ => 0,
        };

    /// <summary>ビューの行を流す方言（SQLite は計画の段で名指しする別の行＝<c>realdb-view-*</c>）</summary>
    private static string[] ViewRowDialects =>
        [DialectNames.SqlServer, DialectNames.PostgreSql, DialectNames.MySql, DialectNames.Oracle];

    /// <summary>
    /// 実 DB の段：SQLite 以外の 4 方言で、DB のビュー（図に無い）が読む表・列を同期が変えたときに、DB が同期とビューをどう扱うか
    /// </summary>
    /// <remarks>
    /// 4 方言の同期はビューを確かめず、計画も名指ししない（docs/database.md「Running the script」のビューの項目）。行は形 × 方言で
    /// 1 行ずつ置き、同期のスクリプトが通るか・断られたら DB がどの状態で残るか・同期の後にビューを読めるか（面 <c>probe:{ビュー}</c>）を
    /// 実測の値で固定する。Oracle は同期の後のビューの状態（<c>USER_OBJECTS.STATUS</c>）を、読む前（面 <c>probe:status:{ビュー}</c>）と
    /// 読んだ後（<c>probe:status-after-read:{ビュー}</c>）で読む。ビューは取込の前に作る（取込は <c>ViewExcluded</c> の取込警告で名指しする）。
    /// SQL Server・MySQL・Oracle のリセットはビューを消さないので、行ごとの後片付けで消す。
    /// </remarks>
    private static IEnumerable<ScenarioRow<SyncPlanExpectationInput>> RealViewRows() =>
        [
            // ビューが読む列の削除：PostgreSQL は断って巻き戻す（前に並ぶ独立した列の追加も残らない）。ほかの 3 方言は削除をコミットし、
            // ビューは読めなくなる（Oracle は INVALID のまま、読んでも再コンパイルに失敗する）
            .. ViewRowDialects.Select(d =>
                ViewRow(
                    d,
                    "reading-dropped-column",
                    "ビューが読む列の削除（と、前に並ぶ独立した列の追加）",
                    [NoteView(d)],
                    s =>
                    {
                        s["vo_item"].Columns.RemoveAll(c => c.Name == "note");
                        s["vo_other"]
                            .Columns.Add(ExpectationSchema.Nullable("memo", ViewVarChar(d, 20)));
                    },
                    PostgreSqlRefusal(d, "ビューが読む列の削除を断る（2BP01）"),
                    e =>
                    {
                        if (d == DialectNames.PostgreSql)
                        {
                            ExpectRefusedAndRolledBack(
                                e,
                                "failed: 2BP01: cannot drop column note of table vo_item because other objects depend on it",
                                "vo_v_note",
                                "1 note=a"
                            );
                            return;
                        }

                        e.Outcome.Exact("committed");
                        e.After.Absent("col vo_item.note *");
                        e.After.Single("col vo_other.memo *");
                        e.Probe("vo_v_note")
                            .Exact(BrokenViewRead(d, "vo_v_note", "Invalid column name 'note'."));
                        ExpectOracleStatus(e, d, "vo_v_note", "INVALID", "INVALID");
                    }
                )
            ),
            // ビューが読む表の削除：PostgreSQL は断る。ほかの 3 方言は削除をコミットし、ビューは読めなくなる
            .. ViewRowDialects.Select(d =>
                ViewRow(
                    d,
                    "reading-dropped-table",
                    "ビューが読む表の削除",
                    [("vo_v_count", ViewSelect(d, "vo_v_count", "COUNT(*) AS \"n\"", "vo_item"))],
                    s => s.Entities.RemoveAll(t => t.TableName == "vo_item"),
                    PostgreSqlRefusal(d, "ビューが読む表の削除を断る（2BP01）"),
                    e =>
                    {
                        if (d == DialectNames.PostgreSql)
                        {
                            ExpectRefusedAndRolledBack(
                                e,
                                "failed: 2BP01: cannot drop table vo_item because other objects depend on it",
                                "vo_v_count",
                                "1"
                            );
                            return;
                        }

                        e.Outcome.Exact("committed");
                        e.After.Absent("table vo_item");
                        e.Probe("vo_v_count")
                            .Exact(
                                BrokenViewRead(d, "vo_v_count", "Invalid object name 'vo_item'.")
                            );
                        ExpectOracleStatus(e, d, "vo_v_count", "INVALID", "INVALID");
                    }
                )
            ),
            // ビューが読む列の型の変更（長さを広げる）：PostgreSQL は断る。ほかの 3 方言はコミットし、ビューは読める（Oracle は
            // INVALID になり、次の読み取りで再コンパイルされて VALID に戻る）
            .. ViewRowDialects.Select(d =>
                ViewRow(
                    d,
                    "reading-column-type-change",
                    "ビューが読む列の型の変更（長さを広げる）",
                    [NoteView(d)],
                    s => s.Col("vo_item", "note").DataType = ViewVarChar(d, 40),
                    PostgreSqlRefusal(d, "ビューが読む列の型の変更を断る（0A000）"),
                    e =>
                    {
                        if (d == DialectNames.PostgreSql)
                        {
                            ExpectRefusedAndRolledBack(
                                e,
                                "failed: 0A000: cannot alter type of a column used by a view or rule",
                                "vo_v_note",
                                "1 note=a"
                            );
                            return;
                        }

                        e.Outcome.Exact("committed");
                        e.After.Single($"col vo_item.note {ViewVarChar(d, 40)} null");
                        e.Probe("vo_v_note").Exact("1 note=a");
                        ExpectOracleStatus(e, d, "vo_v_note", "INVALID", "VALID");
                    }
                )
            ),
            // SELECT * のビューが読む表への列の追加：どの方言もコミットし、ビューは作ったときの列のまま読める（足した列は出ない）
            .. ViewRowDialects.Select(d =>
                ViewRow(
                    d,
                    "select-star-add-column",
                    "SELECT * のビューが読む表への列の追加",
                    [StarView(d)],
                    s =>
                        s["vo_item"]
                            .Columns.Add(ExpectationSchema.Nullable("memo", ViewVarChar(d, 20))),
                    unwarnedFailure: null,
                    e =>
                    {
                        e.Outcome.Exact("committed");
                        e.After.Single("col vo_item.memo *");
                        e.Probe("vo_v_all").Exact("1 note=a extra=7");
                        ExpectOracleStatus(e, d, "vo_v_all", "VALID", "VALID");
                    }
                )
            ),
            // SELECT * のビューが読む表の列の削除：PostgreSQL は断る。ほかの 3 方言はコミットし、ビューは読めなくなる（SQL Server は
            // ビューの列の情報が作ったときのまま残り、列の数が合わない）
            .. ViewRowDialects.Select(d =>
                ViewRow(
                    d,
                    "select-star-drop-column",
                    "SELECT * のビューが読む表の列の削除",
                    [StarView(d)],
                    s => s["vo_item"].Columns.RemoveAll(c => c.Name == "note"),
                    PostgreSqlRefusal(d, "SELECT * のビューが読む列の削除を断る（2BP01）"),
                    e =>
                    {
                        if (d == DialectNames.PostgreSql)
                        {
                            ExpectRefusedAndRolledBack(
                                e,
                                "failed: 2BP01: cannot drop column note of table vo_item because other objects depend on it",
                                "vo_v_all",
                                "1 note=a extra=7"
                            );
                            return;
                        }

                        e.Outcome.Exact("committed");
                        e.After.Absent("col vo_item.note *");
                        e.Probe("vo_v_all")
                            .Exact(
                                BrokenViewRead(
                                    d,
                                    "vo_v_all",
                                    "View or function 'vo_v_all' has more column names specified than columns defined."
                                )
                            );
                        ExpectOracleStatus(e, d, "vo_v_all", "INVALID", "INVALID");
                    }
                )
            ),
            // ビューが読まない列の削除（壊れない対照）：どの方言もコミットし、ビューは読める
            .. ViewRowDialects.Select(d =>
                ViewRow(
                    d,
                    "unread-column-drop",
                    "ビューが読まない列の削除（壊れない対照）",
                    [NoteView(d)],
                    s => s["vo_item"].Columns.RemoveAll(c => c.Name == "extra"),
                    unwarnedFailure: null,
                    e =>
                    {
                        e.Outcome.Exact("committed");
                        e.After.Absent("col vo_item.extra *");
                        e.Probe("vo_v_note").Exact("1 note=a");
                        ExpectOracleStatus(e, d, "vo_v_note", "VALID", "VALID");
                    }
                )
            ),
            // SQL Server の WITH SCHEMABINDING のビューは、読む列の削除も型の変更も断る（同期は巻き戻る）
            ViewRow(
                DialectNames.SqlServer,
                "schemabinding-reading-dropped-column",
                "WITH SCHEMABINDING のビューが読む列の削除",
                [("vo_v_bound", SchemaBoundView)],
                s => s["vo_item"].Columns.RemoveAll(c => c.Name == "note"),
                "モデルの外の物：SQL Server は、WITH SCHEMABINDING のビュー（図に無い）が読む列の削除を断る（計画はビューを見ない）",
                e =>
                {
                    ExpectRefusedAndRolledBack(
                        e,
                        "failed: The object 'vo_v_bound' is dependent on column 'note'.",
                        "vo_v_bound",
                        "1 note=a"
                    );
                    e.Error.Contains(
                        "ALTER TABLE DROP COLUMN note failed because one or more objects access this column.*"
                    );
                }
            ),
            ViewRow(
                DialectNames.SqlServer,
                "schemabinding-reading-column-type-change",
                "WITH SCHEMABINDING のビューが読む列の型の変更（長さを広げる）",
                [("vo_v_bound", SchemaBoundView)],
                s => s.Col("vo_item", "note").DataType = ViewVarChar(DialectNames.SqlServer, 40),
                "モデルの外の物：SQL Server は、WITH SCHEMABINDING のビュー（図に無い）が読む列の型の変更を断る（計画はビューを見ない）",
                e =>
                {
                    ExpectRefusedAndRolledBack(
                        e,
                        "failed: The object 'vo_v_bound' is dependent on column 'note'.",
                        "vo_v_bound",
                        "1 note=a"
                    );
                    e.Error.Contains(
                        "ALTER TABLE ALTER COLUMN note failed because one or more objects access this column.*"
                    );
                }
            ),
        ];

    /// <summary>列 id・note を読むビュー vo_v_note</summary>
    private static (string Name, string Create) NoteView(string dialect) =>
        ("vo_v_note", ViewSelect(dialect, "vo_v_note", "\"id\", \"note\"", "vo_item"));

    /// <summary>SELECT * のビュー vo_v_all</summary>
    private static (string Name, string Create) StarView(string dialect) =>
        ("vo_v_all", ViewSelect(dialect, "vo_v_all", "*", "vo_item"));

    /// <summary>PostgreSQL の行だけの、計画が警告しない失敗の理由（ほかの方言は null）</summary>
    private static string? PostgreSqlRefusal(string dialect, string what) =>
        dialect == DialectNames.PostgreSql
            ? $"モデルの外の物：PostgreSQL は、ビュー（図に無い）が{what}（計画はビューを見ない）"
            : null;

    /// <summary>断られて巻き戻った行の期待（DB は同期の前のまま・ビューは前のまま読める）</summary>
    private static void ExpectRefusedAndRolledBack(
        ExpectationBuilder e,
        string outcome,
        string view,
        string read
    )
    {
        e.Outcome.Exact(outcome);
        e.Unchanged();
        e.Probe(view).Exact(read);
    }

    /// <summary>
    /// 読めなくなったビューを読んだときの行（エラー番号と固定の文言だけ。DB 名・スキーマ名・各国語の文言は版や環境で変わるので含めない）
    /// </summary>
    private static string BrokenViewRead(string dialect, string view, string sqlServerMessage) =>
        dialect switch
        {
            DialectNames.SqlServer => "error SqlException: " + sqlServerMessage,
            DialectNames.MySql =>
                $"error MySqlException: View '*{view}' references invalid table(s) or column(s)*",
            DialectNames.Oracle => "error OracleException: ORA-04063:*",
            _ => throw new ArgumentOutOfRangeException(nameof(dialect), dialect, null),
        };

    /// <summary>Oracle の行だけ、同期の後のビューの状態（読む前と、読んだ後）</summary>
    private static void ExpectOracleStatus(
        ExpectationBuilder e,
        string dialect,
        string view,
        string beforeRead,
        string afterRead
    )
    {
        if (dialect != DialectNames.Oracle)
        {
            return;
        }

        e.Probe("status:" + view).Exact(beforeRead);
        e.Probe("status-after-read:" + view).Exact(afterRead);
    }

    /// <summary>SQL Server の WITH SCHEMABINDING のビュー（2 部の名前で表を読む）</summary>
    private const string SchemaBoundView =
        "CREATE VIEW [vo_v_bound] WITH SCHEMABINDING AS SELECT [id], [note] FROM [dbo].[vo_item];";

    /// <summary>
    /// ビューの行 1 つ（DB は表 <c>vo_item(id PK, note, extra)</c> に 1 行、表 <c>vo_other(id PK)</c>、と <paramref name="views"/> のビュー）
    /// </summary>
    /// <param name="dialect">行を流す方言</param>
    /// <param name="shape">行 Id の形の部分</param>
    /// <param name="description">形の説明</param>
    /// <param name="views">ビューの名前と CREATE VIEW の文</param>
    /// <param name="target">目標の図の変更（取り込んだ図の複製へ当てる）</param>
    /// <param name="unwarnedFailure">計画が警告せずに失敗するときの理由（「モデルの外の物：…」）。失敗しないなら null</param>
    /// <param name="expect">実 DB の段の期待</param>
    private static ScenarioRow<SyncPlanExpectationInput> ViewRow(
        string dialect,
        string shape,
        string description,
        (string Name, string Create)[] views,
        Action<ExpectationSchema> target,
        string? unwarnedFailure,
        Action<ExpectationBuilder> expect
    ) =>
        Row(
                $"realdb-view-{ViewDialectTag(dialect)}-{shape}",
                $"[Integration] {dialect} 同期: {description}（同期はビューを確かめず名指ししない）",
                dialect
            )
            .TargetFromLive(target)
            .Select(RealScenarios.SelectAll)
            .OnRealDatabase(r =>
            {
                r.LiveSql(
                    string.Join(
                        dialect == DialectNames.SqlServer ? "\nGO\n" : "\n",
                        [ViewTablesSql(dialect), .. views.Select(v => v.Create)]
                    ),
                    dialect
                );
                r.Teardown(
                    string.Join(
                        "\n",
                        views.Select(v =>
                            $"DROP VIEW IF EXISTS {RealCrossDialectScenarios.Quote(dialect, v.Name)};"
                        )
                    ),
                    dialect
                );

                if (dialect == DialectNames.Oracle)
                {
                    foreach (var (name, _) in views)
                    {
                        r.Query("status:" + name, OracleViewStatusSql(name), dialect);
                    }
                }

                foreach (var (name, _) in views)
                {
                    r.Query(
                        name,
                        $"SELECT * FROM {RealCrossDialectScenarios.Quote(dialect, name)}",
                        dialect
                    );
                }

                if (dialect == DialectNames.Oracle)
                {
                    foreach (var (name, _) in views)
                    {
                        r.Query("status-after-read:" + name, OracleViewStatusSql(name), dialect);
                    }
                }

                if (unwarnedFailure is not null)
                {
                    r.ExpectsUnwarnedFailure(unwarnedFailure);
                }

                // 4 方言の同期はビューを確かめない：計画は警告も除外もしない（docs の「Running the script」のビューの項目）
                r.Expect(e =>
                {
                    e.Warnings.Empty();
                    e.Excluded.Empty();
                });
                r.Expect(expect);
            });

    private static string OracleViewStatusSql(string view) =>
        $"SELECT \"STATUS\" FROM \"USER_OBJECTS\" WHERE \"OBJECT_NAME\" = '{view}' AND \"OBJECT_TYPE\" = 'VIEW'";

    private static string ViewDialectTag(string dialect) =>
        dialect switch
        {
            DialectNames.SqlServer => "sqlserver",
            DialectNames.PostgreSql => "pg",
            DialectNames.MySql => "mysql",
            DialectNames.Oracle => "oracle",
            _ => throw new ArgumentOutOfRangeException(nameof(dialect), dialect, null),
        };

    /// <summary>方言の可変長の文字列の型（取り込み直した綴り）</summary>
    private static string ViewVarChar(string dialect, int length) =>
        dialect == DialectNames.Oracle ? $"VARCHAR2({length})" : $"varchar({length})";

    /// <summary>ビューの読む表 vo_item（1 行）と、ほかの表 vo_other</summary>
    private static string ViewTablesSql(string dialect)
    {
        string Q(string identifier) => RealCrossDialectScenarios.Quote(dialect, identifier);
        var intType = RealCrossDialectScenarios.IntType(dialect);
        var text = ViewVarChar(dialect, 20);

        return string.Join(
            "\n",
            $"CREATE TABLE {Q("vo_item")} ({Q("id")} {intType} NOT NULL, {Q("note")} {text} NULL, {Q("extra")} {text} NULL, CONSTRAINT {Q("PK_vo_item")} PRIMARY KEY ({Q("id")}));",
            $"CREATE TABLE {Q("vo_other")} ({Q("id")} {intType} NOT NULL, CONSTRAINT {Q("PK_vo_other")} PRIMARY KEY ({Q("id")}));",
            RealCrossDialectScenarios.Insert(
                dialect,
                "vo_item",
                ["id", "note", "extra"],
                "1, 'a', '7'"
            )
        );
    }

    /// <summary>
    /// 表 1 つを読む CREATE VIEW（<paramref name="select"/> の引用符 <c>"</c> は方言の引用へ直す）
    /// </summary>
    private static string ViewSelect(string dialect, string view, string select, string table)
    {
        string Q(string identifier) => RealCrossDialectScenarios.Quote(dialect, identifier);
        var columns =
            select == "*"
                ? "*"
                : string.Join(
                    ", ",
                    select
                        .Split(',', StringSplitOptions.TrimEntries)
                        .Select(part => QuoteSelectPart(dialect, part))
                );

        return $"CREATE VIEW {Q(view)} AS SELECT {columns} FROM {Q(table)};";
    }

    // "id" → 方言の引用、COUNT(*) AS "n" → COUNT(*) AS 方言の引用
    private static string QuoteSelectPart(string dialect, string part)
    {
        var start = part.IndexOf('"', StringComparison.Ordinal);

        if (start < 0)
        {
            return part;
        }

        var end = part.IndexOf('"', start + 1);
        return part[..start]
            + RealCrossDialectScenarios.Quote(dialect, part[(start + 1)..end])
            + part[(end + 1)..];
    }
}
