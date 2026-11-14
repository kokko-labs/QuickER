using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Data.Sqlite;
using QuickER.Provider;

namespace QuickER.Provider.Sqlite;

/// <summary>取込で読んだ表とビューの原文から、ビューが同期後の表の形でも読めるかを SQLite 自身に試させる</summary>
/// <remarks>
/// <para>
/// 試すたびにメモリの DB へスキーマだけ（表・仮想表・ビューの原文。行は写さない）を写し、作り直しと同じ形（新しい表 → DROP → RENAME・
/// <c>legacy_alter_table</c> は ON＝同期の実行器と同じ）で、変わる表を同期後の列名だけの表に置き換える。前後で読めるかを実行器と同じ文
/// （<see cref="SqliteViews"/>）で比べ、新しく読めなくなったビューを返す。ビューの SQL を解析しない（<c>SELECT *</c>・列の並びを書いた
/// ビュー・入れ子のビュー・大文字小文字の違いを SQLite がそのまま判定する）。利用者の DB には触れない。
/// </para>
/// <para>
/// 写せない表（DB の外で登録した照合順序の列・同梱の SQLite に無い仮想表のモジュール）は飛ばすので、その上のビューは写しの中で
/// 同期の前から読めず、数えない。アプリの関数を使うビューも同じ。どちらも、実行器の同期の前後の比較が最後に拾う。
/// </para>
/// </remarks>
internal sealed class SqliteLiveViewCheck : ILiveViewCheck
{
    /// <summary>置き換えの一時表の名前の接尾辞（作り直しの一時表と同じ流儀）</summary>
    private const string ProbeSuffix = "_quicker_view_check";

    /// <summary>メモリの DB の接続文字列</summary>
    private const string InMemory = "Data Source=:memory:";

    private readonly IReadOnlyList<string> _tableSql;
    private readonly IReadOnlyList<string> _viewSql;

    /// <param name="tableSql">表・仮想表の <c>CREATE</c> 文の原文（仮想表の付属表は含めない＝仮想表の作成で自動でできる）</param>
    /// <param name="viewSql">ビューの <c>CREATE VIEW</c> 文の原文</param>
    public SqliteLiveViewCheck(IReadOnlyList<string> tableSql, IReadOnlyList<string> viewSql)
    {
        _tableSql = tableSql;
        _viewSql = viewSql;
    }

    /// <inheritdoc />
    public IReadOnlyList<string> ViewsBrokenBy(
        IReadOnlyDictionary<string, IReadOnlyList<string>?> tablesAfterSync
    )
    {
        ArgumentNullException.ThrowIfNull(tablesAfterSync);

        if (tablesAfterSync.Count == 0 || _viewSql.Count == 0)
        {
            return [];
        }

        using var conn = SqliteFileConnections.Create(InMemory);
        conn.Open();

        foreach (var sql in _tableSql.Concat(_viewSql))
        {
            TryRun(conn, sql);
        }

        Run(conn, "PRAGMA legacy_alter_table=ON;");
        var before = UnreadableViews(conn);

        foreach (var (table, columns) in tablesAfterSync)
        {
            TryRun(conn, ReplaceSql(table, columns));
        }

        return UnreadableViews(conn)
            .Where(view => !before.Contains(view))
            .Order(StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>表を同期後の形に置き換える文（列名は名前だけ・<c>null</c> は表の削除）</summary>
    private static string ReplaceSql(string table, IReadOnlyList<string>? columns)
    {
        var quoted = SqliteIdentifier.QuoteSimple(table);

        if (columns is null)
        {
            return $"DROP TABLE {quoted};";
        }

        var probe = SqliteIdentifier.QuoteSimple(table + ProbeSuffix);
        var columnList = string.Join(", ", columns.Select(SqliteIdentifier.QuoteSimple));

        return $"CREATE TABLE {probe} ({columnList}); DROP TABLE {quoted}; ALTER TABLE {probe} RENAME TO {quoted};";
    }

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
