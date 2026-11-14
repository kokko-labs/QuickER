using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Data.Sqlite;
using QuickER.Provider;

namespace QuickER.Provider.Sqlite;

/// <summary>
/// 取込で読んだ表・索引・ビュー・トリガーの原文から、ビューが同期後の表の形でも読めるか・トリガーが発火できるかを SQLite 自身に試させる
/// </summary>
/// <remarks>
/// <para>
/// 試すたびにメモリの DB へスキーマだけ（表・仮想表・索引・ビューの原文。行は写さない）を写し、作り直しと同じ形（新しい表 → DROP →
/// RENAME・<c>legacy_alter_table</c> は ON＝同期の実行器と同じ）で、変わる表を同期後の列名の表に置き換える。前後で読めるかを実行器と同じ文
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
/// 写せない表（DB の外で登録した照合順序の列・同梱の SQLite に無い仮想表のモジュール）は飛ばすので、その上のビュー・トリガーは写しの中で
/// 同期の前から読めず（発火できず）、数えない。アプリの関数を使うビュー・トリガーも同じ。どれも、実行器の同期の前後の比較が最後に拾う。
/// </para>
/// </remarks>
internal sealed class SqliteLiveDependentCheck : ILiveDependentCheck
{
    /// <summary>置き換えの一時表の名前の接尾辞（作り直しの一時表と同じ流儀）</summary>
    private const string ProbeSuffix = "_quicker_view_check";

    /// <summary>メモリの DB の接続文字列</summary>
    private const string InMemory = "Data Source=:memory:";

    private readonly IReadOnlyList<string> _tableSql;
    private readonly IReadOnlyList<SqliteIndexSql> _indexSql;
    private readonly IReadOnlyList<string> _viewSql;
    private readonly IReadOnlyList<SqliteTrigger> _triggers;

    /// <param name="tableSql">表・仮想表の <c>CREATE</c> 文の原文（仮想表の付属表は含めない＝仮想表の作成で自動でできる）</param>
    /// <param name="indexSql">索引の <c>CREATE INDEX</c> 文の原文と表（自動の索引は含めない）</param>
    /// <param name="viewSql">ビューの <c>CREATE VIEW</c> 文の原文</param>
    /// <param name="triggers">トリガー（ビューの INSTEAD OF のもの・取り込まない表のものも含む）</param>
    public SqliteLiveDependentCheck(
        IReadOnlyList<string> tableSql,
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
        IReadOnlyDictionary<string, IReadOnlyList<string>?> tablesAfterSync
    )
    {
        ArgumentNullException.ThrowIfNull(tablesAfterSync);

        if (tablesAfterSync.Count == 0 || (_viewSql.Count == 0 && _triggers.Count == 0))
        {
            return new LiveDependentBreaks([], []);
        }

        using var conn = SqliteFileConnections.Create(InMemory);
        conn.Open();

        foreach (var sql in _tableSql.Concat(_indexSql.Select(i => i.Sql)).Concat(_viewSql))
        {
            TryRun(conn, sql);
        }

        Run(conn, "PRAGMA legacy_alter_table=ON;");
        var viewsBefore = UnreadableViews(conn);
        var triggersBefore = BrokenTriggers(conn, _triggers);

        foreach (var (table, columns) in tablesAfterSync)
        {
            TryRun(conn, ReplaceSql(conn, table, columns));

            foreach (var index in _indexSql.Where(i => SqliteIdentifier.NameEquals(i.Table, table)))
            {
                TryRun(conn, index.Sql);
            }
        }

        // 消した表のトリガーは表と一緒に消える（壊れたのではない）
        var remaining = _triggers
            .Where(t => !(tablesAfterSync.TryGetValue(t.Table, out var columns) && columns is null))
            .ToList();

        var views = UnreadableViews(conn)
            .Where(view => !viewsBefore.Contains(view))
            .Order(StringComparer.Ordinal)
            .ToList();
        var triggers = BrokenTriggers(conn, remaining)
            .Where(trigger => !triggersBefore.Contains(trigger))
            .Order(StringComparer.Ordinal)
            .ToList();

        return new LiveDependentBreaks(views, triggers);
    }

    /// <summary>表を同期後の形に置き換える文（列名と、列がすべて残る主キー・一意制約・<c>null</c> は表の削除）</summary>
    private static string ReplaceSql(
        SqliteConnection conn,
        string table,
        IReadOnlyList<string>? columns
    )
    {
        var quoted = SqliteIdentifier.QuoteSimple(table);

        if (columns is null)
        {
            return $"DROP TABLE {quoted};";
        }

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

    /// <summary>文を流す（断られたら飛ばす＝写せない表・置き換えられない表は試さない）</summary>
    private static void TryRun(SqliteConnection conn, string sql)
    {
        try
        {
            Run(conn, sql);
        }
        catch (SqliteException)
        {
            // 写せない物は数えない（クラスの remarks）
        }
    }

    private static void Run(SqliteConnection conn, string sql)
    {
        using var cmd = DbCommands.Create(conn, sql, DbCommands.DefaultTimeoutSeconds);
        SqliteScripts.RunToEnd(cmd);
    }
}

/// <summary>DB の索引 1 つ（表と <c>CREATE INDEX</c> の原文）</summary>
internal sealed record SqliteIndexSql(string Table, string Sql);
