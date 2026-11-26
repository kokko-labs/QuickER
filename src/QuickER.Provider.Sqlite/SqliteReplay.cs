using System.Collections.Generic;
using System.Linq;
using Microsoft.Data.Sqlite;
using QuickER.Provider;

namespace QuickER.Provider.Sqlite;

/// <summary>写しの DB に置く表 1 つ（DB の名前・<c>CREATE</c> 文の原文・原文で置けないときの名前だけの表の列）</summary>
/// <param name="Name">表の DB の名前（綴りのまま）</param>
/// <param name="Sql">表・仮想表の <c>CREATE</c> 文の原文</param>
/// <param name="StandInColumns">
/// 素のメモリの DB に原文で置けない表の列名（<c>PRAGMA table_xinfo</c> の hidden が 1 でない列・cid の順）。原文で置ける表と、
/// 列を読めない表は空
/// </param>
internal sealed record SqliteReplayTable(
    string Name,
    string Sql,
    IReadOnlyList<string> StandInColumns
);

/// <summary>
/// 計画の段の判定（<see cref="SqliteLiveDependentCheck"/>・<see cref="SqliteRebuildBlockers"/>）が、素のメモリの DB へ表を写す部品
/// </summary>
/// <remarks>
/// <para>
/// 写しの DB は QuickER の接続と同じく、アプリが登録した照合順序・関数を持たない。それを使う表（列の COLLATE・CHECK・生成列）は
/// 原文で置けない。置けない表は、取込が読んだ列名だけの表（名前だけの表）で置ける。名前だけの表の上では、索引の文に書いた
/// 照合順序・関数を使う索引は作れず、列だけを読む索引は作れる（作り直しも列の属性を落とすので、作り直しで作れる索引と同じ答え）。
/// </para>
/// <para>
/// 名前だけの表は索引の判定にだけ使う。ビュー・トリガーの判定は名前だけの表を置く前に「同期の前」を測り、写せない表の上の
/// ビュー・トリガーを数えない（名前だけの表の上では照合順序・関数を使う比較まで読めてしまい、実行器の接続で同期の前から読めない
/// ビューを新しく壊れたと数えうる。範囲を広げるかは偽の名指しを測ってから決める）。
/// </para>
/// </remarks>
internal static class SqliteReplay
{
    /// <summary>
    /// 取込の接続で、素のメモリの DB に原文で置けない表の列名を読み、写しの表の並びを作る
    /// </summary>
    /// <param name="conn">取込の接続（利用者の DB。読むだけ）</param>
    /// <param name="tables">表の DB の名前と <c>CREATE</c> 文の原文（取込の表の列挙の順）</param>
    /// <param name="commandTimeoutSeconds">カタログ照会 1 本ごとの実行タイムアウト（秒）</param>
    public static List<SqliteReplayTable> Prepare(
        SqliteConnection conn,
        IReadOnlyList<(string Name, string Sql)> tables,
        int commandTimeoutSeconds
    )
    {
        var result = new List<SqliteReplayTable>();
        using var memory = SqliteFileConnections.Create(SqliteLiveDependentCheck.InMemory);
        memory.Open();

        foreach (var (name, sql) in tables)
        {
            var columns = SqliteLiveDependentCheck.TryRun(memory, sql)
                ? []
                : ReadColumns(conn, name, commandTimeoutSeconds);
            result.Add(new SqliteReplayTable(name, sql, columns));
        }

        return result;
    }

    /// <summary>表を原文で置き、置けなかった表を返す</summary>
    public static List<SqliteReplayTable> CreateOriginals(
        SqliteConnection conn,
        IReadOnlyList<SqliteReplayTable> tables
    ) => [.. tables.Where(table => !SqliteLiveDependentCheck.TryRun(conn, table.Sql))];

    /// <summary>原文で置けなかった表を名前だけの表で置く（列名を読めなかった表は置かない）</summary>
    public static void CreateStandIns(
        SqliteConnection conn,
        IEnumerable<SqliteReplayTable> unplaced
    )
    {
        foreach (var table in unplaced.Where(t => t.StandInColumns.Count > 0))
        {
            SqliteLiveDependentCheck.TryRun(
                conn,
                $"CREATE TABLE {SqliteIdentifier.QuoteSimple(table.Name)} ({string.Join(", ", table.StandInColumns.Select(SqliteIdentifier.QuoteSimple))});"
            );
        }
    }

    /// <summary>表の列名（hidden が 1＝仮想表の隠れた列を除く・cid の順。読めなければ空）</summary>
    private static List<string> ReadColumns(
        SqliteConnection conn,
        string table,
        int commandTimeoutSeconds
    )
    {
        var columns = new List<string>();

        try
        {
            using var cmd = (SqliteCommand)
                DbCommands.Create(
                    conn,
                    "SELECT name FROM pragma_table_xinfo($table) WHERE hidden <> 1 ORDER BY cid;",
                    commandTimeoutSeconds
                );
            cmd.Parameters.AddWithValue("$table", table);
            using var reader = cmd.ExecuteReader();

            while (reader.Read())
            {
                columns.Add(reader.GetString(0));
            }
        }
        catch (SqliteException)
        {
            // 列を読めない表（同梱の SQLite に無い仮想表のモジュール）は名前だけの表でも置かない
            return [];
        }

        return columns;
    }
}
