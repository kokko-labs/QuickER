using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Data.Sqlite;
using QuickER.Provider;

namespace QuickER.Provider.Sqlite;

/// <summary>
/// 作り直し（テーブル再構築）を QuickER の接続では必ず止める DB の物（DB の外で登録した照合順序・関数を使う索引・仮想の生成列）を集める
/// </summary>
/// <remarks>
/// <para>
/// QuickER の接続（同期の実行器・取込）は、アプリが登録した照合順序・関数を持たない。作り直しは索引を原文で作り直し、写すときに列を読み、
/// RENAME でスキーマ全体を読み直すので、次の 3 つで同期全体が巻き戻る（2026-11 の実測）。
/// </para>
/// <list type="bullet">
/// <item>
/// 照合順序の無い索引（索引の文に書いたものも、列の COLLATE を継いだものも）は、ほかの表の作り直しの RENAME で断られる（大域）。
/// <c>PRAGMA index_xinfo</c> が返す索引の列の照合順序を、実行器と同じ素の接続の <c>PRAGMA collation_list</c> と比べる。自動の索引
/// （sql の無い索引）は RENAME で読み直されないので見ない。
/// </item>
/// <item>
/// 作り直しで作れない索引（照合順序・関数）は、その表の作り直しで断られる（局所）。表と索引の原文を素のメモリの DB へ写し、表を置けたのに
/// 索引を作れなければ数える（索引は自分の表しか読まないので、原因は DB の外の照合順序か関数に限られる。照合順序が使えるなら関数）。
/// 原文で置けない表（列の COLLATE・CHECK・生成列が DB の外の照合順序・関数を使う）は、名前だけの表で置く（<see cref="SqliteReplay"/>）。
/// 作り直しもそれらの属性を落とすので、名前だけの表で作れない索引が作り直しで作れない索引になる。
/// </item>
/// <item>
/// 読めない仮想の生成列（<c>PRAGMA table_xinfo</c> の hidden 2）は、作り直しの写しで断られる（局所）。その列を読む文の準備で確かめる
/// （行に依らず断られる）。STORED の生成列は保存した値を読むので数えない。
/// </item>
/// </list>
/// <para>
/// SQL もエラーの文も解析しない。利用者の DB には書かない（読むだけ）。
/// </para>
/// </remarks>
internal static class SqliteRebuildBlockers
{
    /// <summary>作り直しを止める物を集める</summary>
    /// <param name="conn">取込の接続（利用者の DB）</param>
    /// <param name="tableSql">写しの DB に置く表（判定の写しと同じ）</param>
    /// <param name="indexes">索引（sql のあるもの）</param>
    /// <param name="tables">取り込んだ表の DB の名前（綴りのまま）</param>
    /// <param name="commandTimeoutSeconds">カタログ照会 1 本ごとの実行タイムアウト（秒）</param>
    public static IReadOnlyList<TableRebuildBlocker> Collect(
        SqliteConnection conn,
        IReadOnlyList<SqliteReplayTable> tableSql,
        IReadOnlyList<SqliteIndexSql> indexes,
        IReadOnlyList<string> tables,
        int commandTimeoutSeconds
    )
    {
        var blockers = new List<TableRebuildBlocker>();
        var unavailable = new Dictionary<string, string?>(StringComparer.Ordinal);

        if (indexes.Count > 0)
        {
            var available = AvailableCollations();

            foreach (var index in indexes)
            {
                var collation = IndexCollations(conn, index.Name, commandTimeoutSeconds)
                    .FirstOrDefault(c => !available.Contains(c));
                unavailable[index.Name] = collation;

                if (collation is not null)
                {
                    blockers.Add(
                        new TableRebuildBlocker(
                            index.Table,
                            $"INDEX {index.Name} ON {index.Table} COLLATE {collation}",
                            BlocksOtherTables: true
                        )
                    );
                }
            }

            blockers.AddRange(
                UnreplayableIndexes(tableSql, indexes)
                    .Select(index => new TableRebuildBlocker(
                        index.Table,
                        unavailable[index.Name] is { } collation
                            ? $"INDEX {index.Name} COLLATE {collation}"
                            : $"INDEX {index.Name} FUNCTION",
                        BlocksOtherTables: false
                    ))
            );
        }

        // 表名は DB の綴り（TableRebuildBlocker の契約）。計画は SchemaNames の規則で表と照らすので、図の名前（前後の空白を除いた名前）と
        // 答えは変わらない（前後に空白のある表名は、同期スクリプトが図の名前で表を引くのでそもそも同期に届かない）
        foreach (var key in tables)
        {
            foreach (var column in VirtualGeneratedColumns(conn, key, commandTimeoutSeconds))
            {
                if (!CanRead(conn, key, column, commandTimeoutSeconds))
                {
                    blockers.Add(
                        new TableRebuildBlocker(
                            key,
                            $"GENERATED {column} FUNCTION",
                            BlocksOtherTables: false
                        )
                    );
                }
            }
        }

        return blockers;
    }

    /// <summary>実行器と同じ素の接続で使える照合順序（組み込み＋Microsoft.Data.Sqlite が登録するもの）</summary>
    private static HashSet<string> AvailableCollations()
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using var conn = SqliteFileConnections.Create(SqliteLiveDependentCheck.InMemory);
        conn.Open();
        using var cmd = DbCommands.Create(
            conn,
            "SELECT name FROM pragma_collation_list;",
            DbCommands.DefaultTimeoutSeconds
        );
        using var reader = cmd.ExecuteReader();

        while (reader.Read())
        {
            names.Add(reader.GetString(0));
        }

        return names;
    }

    /// <summary>索引のキーの列の照合順序（列の COLLATE を継いだものも含む）</summary>
    private static List<string> IndexCollations(
        SqliteConnection conn,
        string index,
        int commandTimeoutSeconds
    )
    {
        var collations = new List<string>();
        using var cmd = (SqliteCommand)
            DbCommands.Create(
                conn,
                "SELECT coll FROM pragma_index_xinfo($index) WHERE key = 1 AND coll IS NOT NULL ORDER BY seqno;",
                commandTimeoutSeconds
            );
        cmd.Parameters.AddWithValue("$index", index);
        using var reader = cmd.ExecuteReader();

        while (reader.Read())
        {
            collations.Add(reader.GetString(0));
        }

        return collations;
    }

    /// <summary>素のメモリの DB に表（原文で置けなければ名前だけの表）を置けたのに作れない索引</summary>
    private static List<SqliteIndexSql> UnreplayableIndexes(
        IReadOnlyList<SqliteReplayTable> tableSql,
        IReadOnlyList<SqliteIndexSql> indexes
    )
    {
        var result = new List<SqliteIndexSql>();
        using var conn = SqliteFileConnections.Create(SqliteLiveDependentCheck.InMemory);
        conn.Open();
        SqliteReplay.CreateStandIns(conn, SqliteReplay.CreateOriginals(conn, tableSql));

        foreach (var index in indexes)
        {
            if (TableExists(conn, index.Table) && !SqliteLiveDependentCheck.TryRun(conn, index.Sql))
            {
                result.Add(index);
            }
        }

        return result;
    }

    /// <summary>写しの DB に表があるか</summary>
    private static bool TableExists(SqliteConnection conn, string table)
    {
        using var cmd = (SqliteCommand)
            DbCommands.Create(
                conn,
                "SELECT count(*) FROM sqlite_master WHERE type = 'table' AND name = $table;",
                DbCommands.DefaultTimeoutSeconds
            );
        cmd.Parameters.AddWithValue("$table", table);

        return Convert.ToInt64(
                cmd.ExecuteScalar(),
                System.Globalization.CultureInfo.InvariantCulture
            ) > 0;
    }

    /// <summary>表の仮想の生成列（<c>PRAGMA table_xinfo</c> の hidden 2）</summary>
    private static List<string> VirtualGeneratedColumns(
        SqliteConnection conn,
        string table,
        int commandTimeoutSeconds
    )
    {
        var columns = new List<string>();
        using var cmd = (SqliteCommand)
            DbCommands.Create(
                conn,
                "SELECT name FROM pragma_table_xinfo($table) WHERE hidden = 2 ORDER BY cid;",
                commandTimeoutSeconds
            );
        cmd.Parameters.AddWithValue("$table", table);
        using var reader = cmd.ExecuteReader();

        while (reader.Read())
        {
            columns.Add(reader.GetString(0));
        }

        return columns;
    }

    /// <summary>列を読む文を準備できるか（行を読まない）</summary>
    private static bool CanRead(
        SqliteConnection conn,
        string table,
        string column,
        int commandTimeoutSeconds
    )
    {
        try
        {
            using var cmd = DbCommands.Create(
                conn,
                $"SELECT {SqliteIdentifier.QuoteSimple(column)} FROM {SqliteIdentifier.QuoteSimple(table)} LIMIT 0;",
                commandTimeoutSeconds
            );
            using var reader = cmd.ExecuteReader();
            return true;
        }
        catch (SqliteException)
        {
            // 読めない＝作り直しの写しで断られる（クラスの remarks）
            return false;
        }
    }
}
