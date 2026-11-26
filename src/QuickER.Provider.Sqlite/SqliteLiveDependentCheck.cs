using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Data.Sqlite;
using QuickER.Model;
using QuickER.Provider;

namespace QuickER.Provider.Sqlite;

/// <summary>
/// 取込で読んだ表・索引・ビュー・トリガーの原文から、ビューが同期後の表の形でも読めるか・トリガーが発火できるかを SQLite 自身に試させる
/// </summary>
/// <remarks>
/// <para>
/// 試すたびにメモリの DB へスキーマだけ（表・仮想表・索引・ビューの原文。行は写さない）を写し、変わる表を同期スクリプトと同じ形にする：
/// 作り直す表（製品の再構築の合成と同じ判定＝呼び出し側が渡す）は作り直しと同じ形（新しい表 → DROP → RENAME・<c>legacy_alter_table</c> は
/// ON・<c>foreign_keys</c> は OFF＝同期の実行器と同じ）で同期後の列名の表に置き換え、作り直さない表は <c>ALTER TABLE … ADD COLUMN</c>、
/// 消す表は <c>DROP TABLE</c>。
/// 計画が渡す列名（図の名前＝前後の空白を除いた名前・取込が捨てた大文字小文字だけ違う双子の列が無い）は、写しの DB の列へ
/// <see cref="SchemaNames"/> の規則で照らし、DB の綴りで残す（双子の列も残る）。前後で読めるかを実行器と同じ文
/// （<see cref="SqliteViews"/>）で、発火できるかを実行器と同じ部品（<see cref="SqliteTriggers"/>）で比べ、新しく読めなくなったビュー・
/// 発火できなくなったトリガーを返す。SQL を解析しない（<c>SELECT *</c>・列の並びを書いたビュー・入れ子のビュー・大文字小文字の違い・
/// トリガーの <c>NEW</c>／<c>OLD</c>・本文の問い合わせを SQLite がそのまま判定する）。利用者の DB には触れない。
/// </para>
/// <para>
/// 置き換えの表は、同期後の列名に加えて、今の表の主キー・一意制約のうち列がすべて残るものを写し（SQL を解析せず PRAGMA から読む）、
/// その表の索引を原文で作り直す。トリガーの <c>ON CONFLICT</c> は的のキーが要るので、名前だけの表にすると列を足すだけで偽の警告になる。
/// 同じ同期が主キー・一意制約を変える形は写さない（実行器の同期の前後の比較が巻き戻して名指しする）。
/// </para>
/// <para>
/// 原文で写せない表（DB の外で登録した照合順序・関数を使う列の COLLATE・CHECK・生成列）は、名前だけの表で置いて索引だけを判定する
/// （<see cref="SqliteReplay"/>）。その上のビュー・トリガーは、名前だけの表を置く前の写しで同期の前から読めず（発火できず）、数えない。
/// 列を読めない表（同梱の SQLite に無い仮想表のモジュール）とアプリの関数を使うビュー・トリガーも同じ。どれも実行器の同期の前後の比較が
/// 最後に拾う。例外：DB の外の関数の生成列を持つ表へ書くトリガーは、実行器の接続でも同期の前から書き込みの準備が断られて「前から壊れて
/// いる」と数えるので、前後の比較も拾わない（src/QuickER.Provider.Sqlite/CLAUDE.md「計画の段のビュー・トリガー・索引の判定」）。
/// </para>
/// </remarks>
internal sealed class SqliteLiveDependentCheck : ILiveDependentCheck
{
    /// <summary>置き換えの一時表の名前の接尾辞（作り直しの一時表と同じ流儀）</summary>
    private const string ProbeSuffix = "_quicker_view_check";

    /// <summary>メモリの DB の接続文字列</summary>
    internal const string InMemory = "Data Source=:memory:";

    private readonly IReadOnlyList<SqliteReplayTable> _tableSql;
    private readonly IReadOnlyList<SqliteIndexSql> _indexSql;
    private readonly IReadOnlyList<string> _viewSql;
    private readonly IReadOnlyList<SqliteTrigger> _triggers;

    /// <param name="tableSql">
    /// 写しの DB に置く表（表・仮想表の <c>CREATE</c> 文の原文と、原文で置けないときの名前だけの表の列。仮想表の付属表は含めない＝
    /// 仮想表の作成で自動でできる）
    /// </param>
    /// <param name="indexSql">索引の <c>CREATE INDEX</c> 文の原文と表（自動の索引は含めない）</param>
    /// <param name="viewSql">ビューの <c>CREATE VIEW</c> 文の原文</param>
    /// <param name="triggers">トリガー（ビューの INSTEAD OF のもの・取り込まない表のものも含む）</param>
    public SqliteLiveDependentCheck(
        IReadOnlyList<SqliteReplayTable> tableSql,
        IReadOnlyList<SqliteIndexSql> indexSql,
        IReadOnlyList<string> viewSql,
        IReadOnlyList<SqliteTrigger> triggers
    )
    {
        _tableSql = tableSql;
        _indexSql = indexSql;
        _viewSql = viewSql;
        _triggers = triggers;
    }

    /// <inheritdoc />
    public LiveDependentBreaks DependentsBrokenBy(
        IReadOnlyDictionary<string, IReadOnlyList<string>?> tablesAfterSync,
        IReadOnlySet<string> rebuiltTables
    )
    {
        ArgumentNullException.ThrowIfNull(tablesAfterSync);
        ArgumentNullException.ThrowIfNull(rebuiltTables);

        if (
            tablesAfterSync.Count == 0
            || (_viewSql.Count == 0 && _triggers.Count == 0 && _indexSql.Count == 0)
        )
        {
            return new LiveDependentBreaks([], [], []);
        }

        using var conn = SqliteFileConnections.Create(InMemory);
        conn.Open();
        var unplaced = SqliteReplay.CreateOriginals(conn, _tableSql);

        foreach (var sql in _viewSql)
        {
            TryRun(conn, sql);
        }

        // 実行器と同じく外部キーの検査を切る（写しの子の表は、作り直しが暗黙に外す外部キーを持ったまま残るので、検査すると消した親を
        // 参照する子へ書くトリガーを壊れたと数える）
        Run(conn, "PRAGMA foreign_keys=OFF; PRAGMA legacy_alter_table=ON;");

        // 同期の前のビュー・トリガーは、名前だけの表を置く前に測る（原文で置けない表の上のビュー・トリガーは同期の前から読めず
        // 発火できないので数えない＝クラスの remarks の割り切り）
        var viewsBefore = UnreadableViews(conn);
        var triggersBefore = BrokenTriggers(conn, _triggers);

        // 原文で置けない表は名前だけの表で置き、その表の索引も判定する（作り直しも列の属性を落とすので同じ答え）
        SqliteReplay.CreateStandIns(conn, unplaced);

        // 同期の前に写しで作れた索引（DB の外の照合順序・アプリの関数を使う索引は作れず、後で作れなくても数えない）
        var indexesBefore = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var index in _indexSql)
        {
            if (TryRun(conn, index.Sql))
            {
                indexesBefore.Add(index.Name);
            }
        }

        // 置き換えた表の索引を、作り直しと同じく原文で作り直す（消した表の索引は表と一緒に消えるので作り直さない）。同期の前に作れたのに
        // 作れない索引が、作り直しで断られる索引
        var indexes = new List<string>();
        var droppedTables = new List<string>();

        // 表名は計画の名前（図の名前）のまま引く：同期スクリプトも図の名前で表を引くので、前後に空白のある表名の表はそもそも同期に
        // 届かない（それ以外の表は DB の名前と同じ）
        foreach (var (table, columns) in tablesAfterSync)
        {
            if (columns is null)
            {
                TryRun(conn, $"DROP TABLE {SqliteIdentifier.QuoteSimple(table)};");
                droppedTables.Add(table);
                continue;
            }

            // DB の列のうち同期後の名前のどれかと同じ列（綴りは DB のまま。前後に空白のある列・取込が捨てた大文字小文字だけ違う双子の列も
            // 残る）と、どの DB の列とも同じでない同期後の名前（足す列）
            var existing = TableColumns(conn, table);
            var kept = existing.Where(c => columns.Any(n => SchemaNames.AreSame(n, c))).ToList();
            var added = columns.Where(n => !existing.Any(c => SchemaNames.AreSame(n, c))).ToList();

            // 作り直さない表（製品の再構築の合成と同じ判定。列の削除はいつも作り直す）は、同期スクリプトと同じ列の追加の文で試す
            if (!rebuiltTables.Contains(table))
            {
                // 列の属性・キー・索引はそのまま残る
                foreach (var column in added)
                {
                    TryRun(
                        conn,
                        $"ALTER TABLE {SqliteIdentifier.QuoteSimple(table)} ADD COLUMN {SqliteIdentifier.QuoteSimple(column)};"
                    );
                }

                continue;
            }

            TryRun(conn, ReplaceSql(conn, table, [.. kept, .. added]));

            foreach (var index in _indexSql.Where(i => SqliteIdentifier.NameEquals(i.Table, table)))
            {
                if (!TryRun(conn, index.Sql) && indexesBefore.Contains(index.Name))
                {
                    indexes.Add(index.Name);
                }
            }
        }

        // 消した表のトリガーは表と一緒に消える（壊れたのではない）。表は SQLite の規則（ASCII だけ大文字小文字を畳む）で照らす
        var remaining = _triggers
            .Where(t => !droppedTables.Any(d => SqliteIdentifier.NameEquals(t.Table, d)))
            .ToList();

        var views = UnreadableViews(conn)
            .Where(view => !viewsBefore.Contains(view))
            .Order(StringComparer.Ordinal)
            .ToList();
        var triggers = BrokenTriggers(conn, remaining)
            .Where(trigger => !triggersBefore.Contains(trigger))
            .Order(StringComparer.Ordinal)
            .ToList();

        return new LiveDependentBreaks(
            views,
            triggers,
            [.. indexes.Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.Ordinal)]
        );
    }

    /// <summary>写しの DB の表の列名（仮想表の隠れた列を除く・cid の順）</summary>
    private static List<string> TableColumns(SqliteConnection conn, string table)
    {
        var columns = new List<string>();
        using var cmd = (SqliteCommand)
            DbCommands.Create(
                conn,
                "SELECT name FROM pragma_table_xinfo($table) WHERE hidden <> 1 ORDER BY cid;",
                DbCommands.DefaultTimeoutSeconds
            );
        cmd.Parameters.AddWithValue("$table", table);
        using var reader = cmd.ExecuteReader();

        while (reader.Read())
        {
            columns.Add(reader.GetString(0));
        }

        return columns;
    }

    /// <summary>表を作り直しの形に置き換える文（列名と、列がすべて残る主キー・一意制約。図に無い列の属性は落とす）</summary>
    private static string ReplaceSql(
        SqliteConnection conn,
        string table,
        IReadOnlyList<string> columns
    )
    {
        var quoted = SqliteIdentifier.QuoteSimple(table);
        var probe = SqliteIdentifier.QuoteSimple(table + ProbeSuffix);
        var definition = string.Join(
            ", ",
            columns.Select(SqliteIdentifier.QuoteSimple).Concat(KeyClauses(conn, table, columns))
        );

        return $"CREATE TABLE {probe} ({definition}); DROP TABLE {quoted}; ALTER TABLE {probe} RENAME TO {quoted};";
    }

    /// <summary>今の表の主キー・一意制約のうち列がすべて残るものを、置き換えの表の句にする（PRAGMA から読む）</summary>
    private static IEnumerable<string> KeyClauses(
        SqliteConnection conn,
        string table,
        IReadOnlyList<string> columns
    )
    {
        var clauses = new List<string>();
        var primaryKey = new List<(int Order, string Column)>();

        try
        {
            using (
                var cmd = (SqliteCommand)
                    DbCommands.Create(
                        conn,
                        "SELECT name, pk FROM pragma_table_info($table) WHERE pk > 0;",
                        DbCommands.DefaultTimeoutSeconds
                    )
            )
            {
                cmd.Parameters.AddWithValue("$table", table);
                using var reader = cmd.ExecuteReader();

                while (reader.Read())
                {
                    primaryKey.Add((reader.GetInt32(1), reader.GetString(0)));
                }
            }

            if (primaryKey.Count > 0 && primaryKey.All(p => Remains(p.Column)))
            {
                clauses.Add(
                    "PRIMARY KEY ("
                        + string.Join(
                            ", ",
                            primaryKey
                                .OrderBy(p => p.Order)
                                .Select(p => SqliteIdentifier.QuoteSimple(p.Column))
                        )
                        + ")"
                );
            }

            foreach (var index in UniqueConstraintIndexes(conn, table))
            {
                var indexColumns = IndexColumns(conn, index);

                if (indexColumns.Count > 0 && indexColumns.All(c => c is not null && Remains(c)))
                {
                    clauses.Add(
                        "UNIQUE ("
                            + string.Join(
                                ", ",
                                indexColumns.Select(c => SqliteIdentifier.QuoteSimple(c!))
                            )
                            + ")"
                    );
                }
            }
        }
        catch (SqliteException)
        {
            // 写せなかった表（キーを読めない）は名前だけで置き換える
        }

        return clauses;

        bool Remains(string column) => columns.Any(c => SqliteIdentifier.NameEquals(c, column));
    }

    /// <summary>表の制約の句が作った一意な索引（<c>UNIQUE</c> の制約。CREATE INDEX の索引は原文で作り直す）の名前</summary>
    private static List<string> UniqueConstraintIndexes(SqliteConnection conn, string table)
    {
        var names = new List<string>();
        using var cmd = (SqliteCommand)
            DbCommands.Create(
                conn,
                "SELECT name FROM pragma_index_list($table) WHERE \"unique\" = 1 AND origin = 'u';",
                DbCommands.DefaultTimeoutSeconds
            );
        cmd.Parameters.AddWithValue("$table", table);
        using var reader = cmd.ExecuteReader();

        while (reader.Read())
        {
            names.Add(reader.GetString(0));
        }

        return names;
    }

    /// <summary>索引の列（式の列は <c>null</c>）</summary>
    private static List<string?> IndexColumns(SqliteConnection conn, string index)
    {
        var columns = new List<string?>();
        using var cmd = (SqliteCommand)
            DbCommands.Create(
                conn,
                "SELECT name FROM pragma_index_info($index) ORDER BY seqno;",
                DbCommands.DefaultTimeoutSeconds
            );
        cmd.Parameters.AddWithValue("$index", index);
        using var reader = cmd.ExecuteReader();

        while (reader.Read())
        {
            columns.Add(reader.IsDBNull(0) ? null : reader.GetString(0));
        }

        return columns;
    }

    /// <summary>発火できないトリガーの名前（写しにはトリガーを置かず、1 つずつ作って試す）</summary>
    private static HashSet<string> BrokenTriggers(
        SqliteConnection conn,
        IReadOnlyList<SqliteTrigger> triggers
    ) =>
        triggers.Count == 0
            ? []
            : SqliteTriggers.BrokenAmong(conn, null, DbCommands.DefaultTimeoutSeconds, triggers);

    /// <summary>読めないビューの名前を集める</summary>
    private static HashSet<string> UnreadableViews(SqliteConnection conn)
    {
        var views = new List<string>();

        using (
            var list = DbCommands.Create(
                conn,
                SqliteViews.ListSql,
                DbCommands.DefaultTimeoutSeconds
            )
        )
        using (var reader = list.ExecuteReader())
        {
            while (reader.Read())
            {
                views.Add(reader.GetString(0));
            }
        }

        var unreadable = new HashSet<string>(StringComparer.Ordinal);

        foreach (var view in views)
        {
            try
            {
                using var probe = DbCommands.Create(
                    conn,
                    SqliteViews.ReadProbeSql(view),
                    DbCommands.DefaultTimeoutSeconds
                );
                using var reader = probe.ExecuteReader();
            }
            catch (SqliteException)
            {
                unreadable.Add(view);
            }
        }

        return unreadable;
    }

    /// <summary>文を流す（断られたら飛ばして false＝写せない表・置き換えられない表は試さない・作れない索引を数える）</summary>
    internal static bool TryRun(SqliteConnection conn, string sql)
    {
        try
        {
            Run(conn, sql);
            return true;
        }
        catch (SqliteException)
        {
            // 写せない物は数えない（クラスの remarks）
            return false;
        }
    }

    private static void Run(SqliteConnection conn, string sql)
    {
        using var cmd = DbCommands.Create(conn, sql, DbCommands.DefaultTimeoutSeconds);
        SqliteScripts.RunToEnd(cmd);
    }
}

/// <summary>DB の索引 1 つ（名前・表・<c>CREATE INDEX</c> の原文）</summary>
internal sealed record SqliteIndexSql(string Name, string Table, string Sql);
