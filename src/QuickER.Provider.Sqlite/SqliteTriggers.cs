using System.Collections.Generic;
using System.Linq;
using Microsoft.Data.Sqlite;
using QuickER.Provider;

namespace QuickER.Provider.Sqlite;

/// <summary>トリガーが発火できるかの判定（同期の実行器・計画の段の判定 <see cref="SqliteLiveDependentCheck"/>・同期のテストが共有する）</summary>
/// <remarks>
/// <para>
/// SQLite はトリガーの本文を、作るときにも DB を開き直すときにも検証せず、トリガーを発火させる文を準備するときに初めて解決する。
/// 作り直しで消した列・表をトリガーが参照していても、作り直しはそのまま通ってコミットし、後で表へ書いたときに初めて断られる
/// （同じ表のトリガーは作り直しが原文で作り直し、そのときも断られない。<c>PRAGMA integrity_check</c> も検出しない）。
/// </para>
/// <para>
/// 判定は SQL を解析せず、SQLite 自身に準備させる。トリガーを全部外し、1 つずつ作って、その表（INSTEAD OF のトリガーはビュー）への
/// <c>INSERT</c>・<c>UPDATE</c>（書ける全列）・<c>DELETE</c> を準備だけする（実行しない・行に依らない）。トリガーを置かないときには
/// 出なかった誤り（文の種類と誤りの文）が出るか、トリガーを作れなければ、そのトリガーは発火できない。1 つずつ置くのは、トリガーの本文が
/// 別の表へ書くと、その表のトリガーも準備に巻き込まれ、壊れたトリガーの巻き添えを名指ししないため。
/// </para>
/// <para>
/// <c>UPDATE OF 列</c> のトリガーは、その列が無くなると二度と発火しないので、壊れたとはみなさない（SQLite もそう扱う）。
/// 読めないビューの INSTEAD OF のトリガーは数えない（ビューの判定が名指しする）。
/// </para>
/// </remarks>
public static class SqliteTriggers
{
    /// <summary>トリガーを列挙する文（名前・表・原文）</summary>
    private const string ListSql =
        "SELECT name, tbl_name, sql FROM sqlite_master WHERE type = 'trigger' AND sql IS NOT NULL ORDER BY rowid;";

    /// <summary>判定の間だけトリガーを外すセーブポイントの名前</summary>
    private const string Savepoint = "quicker_trigger_check";

    /// <summary>接続の DB にあるトリガーのうち、今は発火できないものの名前を集める（DB は変えない）</summary>
    /// <param name="connection">書き込める接続（判定の間だけセーブポイントの中でトリガーを外し、戻す）</param>
    /// <param name="transaction">接続のトランザクション（無ければ <c>null</c>）</param>
    /// <param name="commandTimeoutSeconds">文の実行タイムアウト（秒）</param>
    /// <returns>発火できないトリガーの名前（同期の前後で比べ、新しく増えたものだけを壊れたとみなす）</returns>
    public static IReadOnlySet<string> CollectBroken(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        int commandTimeoutSeconds
    )
    {
        var triggers = List(connection, transaction, commandTimeoutSeconds);

        if (triggers.Count == 0)
        {
            return new HashSet<string>(System.StringComparer.Ordinal);
        }

        Run(connection, transaction, commandTimeoutSeconds, $"SAVEPOINT {Savepoint};");

        try
        {
            foreach (var trigger in triggers)
            {
                Run(
                    connection,
                    transaction,
                    commandTimeoutSeconds,
                    $"DROP TRIGGER {SqliteIdentifier.QuoteSimple(trigger.Name)};"
                );
            }

            return BrokenAmong(connection, transaction, commandTimeoutSeconds, triggers);
        }
        finally
        {
            Run(
                connection,
                transaction,
                commandTimeoutSeconds,
                $"ROLLBACK TO {Savepoint}; RELEASE {Savepoint};"
            );
        }
    }

    /// <summary>接続の DB のトリガーを列挙する</summary>
    internal static List<SqliteTrigger> List(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        int commandTimeoutSeconds
    )
    {
        var triggers = new List<SqliteTrigger>();
        using var cmd = DbCommands.Create(connection, ListSql, commandTimeoutSeconds, transaction);
        using var reader = cmd.ExecuteReader();

        while (reader.Read())
        {
            triggers.Add(
                new SqliteTrigger(reader.GetString(0), reader.GetString(1), reader.GetString(2))
            );
        }

        return triggers;
    }

    /// <summary>
    /// 渡したトリガーを 1 つずつ作って試し、発火できないものの名前を返す（呼び出す時点で DB にトリガーが無いこと。試した後は残さない）
    /// </summary>
    internal static HashSet<string> BrokenAmong(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        int commandTimeoutSeconds,
        IReadOnlyList<SqliteTrigger> triggers
    )
    {
        var broken = new HashSet<string>(System.StringComparer.Ordinal);
        var baseline = new Dictionary<string, HashSet<string>>(
            System.StringComparer.OrdinalIgnoreCase
        );

        foreach (var trigger in triggers)
        {
            if (!baseline.TryGetValue(trigger.Table, out var withoutTrigger))
            {
                withoutTrigger = UnpreparableWrites(
                    connection,
                    transaction,
                    commandTimeoutSeconds,
                    trigger.Table
                );
                baseline[trigger.Table] = withoutTrigger;
            }

            try
            {
                Run(connection, transaction, commandTimeoutSeconds, trigger.Sql);
            }
            catch (SqliteException)
            {
                broken.Add(trigger.Name);
                continue;
            }

            if (
                UnpreparableWrites(connection, transaction, commandTimeoutSeconds, trigger.Table)
                    .Except(withoutTrigger)
                    .Any()
            )
            {
                broken.Add(trigger.Name);
            }

            Run(
                connection,
                transaction,
                commandTimeoutSeconds,
                $"DROP TRIGGER {SqliteIdentifier.QuoteSimple(trigger.Name)};"
            );
        }

        return broken;
    }

    /// <summary>表（ビュー）への INSERT・UPDATE・DELETE のうち準備できないものを「種類: 誤りの文」で返す（実行しない）</summary>
    private static HashSet<string> UnpreparableWrites(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        int commandTimeoutSeconds,
        string table
    )
    {
        var failed = new HashSet<string>(System.StringComparer.Ordinal);

        foreach (var (kind, sql) in Writes(connection, transaction, commandTimeoutSeconds, table))
        {
            try
            {
                using var cmd = (SqliteCommand)
                    DbCommands.Create(connection, sql, commandTimeoutSeconds, transaction);
                cmd.Prepare();
            }
            catch (SqliteException ex)
            {
                failed.Add($"{kind}: {ex.Message}");
            }
        }

        return failed;
    }

    /// <summary>表（ビュー）への書き込みの文（行に依らない <c>WHERE 0</c>・書ける列は隠れていない列＝生成列・仮想表の隠れた列を除く）</summary>
    private static List<(string Kind, string Sql)> Writes(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        int commandTimeoutSeconds,
        string table
    )
    {
        var columns = new List<string>();

        try
        {
            using var cmd = (SqliteCommand)
                DbCommands.Create(
                    connection,
                    "SELECT name FROM pragma_table_xinfo($table) WHERE hidden = 0;",
                    commandTimeoutSeconds,
                    transaction
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
            // 読めないビュー（ビューの判定が名指しする）
            return [];
        }

        if (columns.Count == 0)
        {
            return [];
        }

        var quoted = SqliteIdentifier.QuoteSimple(table);
        var list = string.Join(", ", columns.Select(SqliteIdentifier.QuoteSimple));
        var assignments = string.Join(
            ", ",
            columns.Select(c =>
                $"{SqliteIdentifier.QuoteSimple(c)} = {SqliteIdentifier.QuoteSimple(c)}"
            )
        );

        return
        [
            ("INSERT", $"INSERT INTO {quoted} ({list}) SELECT {list} FROM {quoted} WHERE 0;"),
            ("UPDATE", $"UPDATE {quoted} SET {assignments} WHERE 0;"),
            ("DELETE", $"DELETE FROM {quoted} WHERE 0;"),
        ];
    }

    private static void Run(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        int commandTimeoutSeconds,
        string sql
    )
    {
        using var cmd = DbCommands.Create(connection, sql, commandTimeoutSeconds, transaction);
        SqliteScripts.RunToEnd(cmd);
    }
}

/// <summary>DB のトリガー 1 つ（名前・表〔INSTEAD OF はビュー〕・<c>CREATE TRIGGER</c> の原文）</summary>
internal sealed record SqliteTrigger(string Name, string Table, string Sql);
