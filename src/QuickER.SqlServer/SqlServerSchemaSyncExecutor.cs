using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using QuickER.Provider;

namespace QuickER.SqlServer;

/// <summary>生成済みの T-SQL スクリプトを単一トランザクションで SQL Server に対し実行する</summary>
/// <remarks>sqlcmd の慣習に従い、行頭の <c>GO</c> でバッチ分割する</remarks>
public sealed class SqlServerSchemaSyncExecutor : ISchemaSyncExecutor
{
    /// <summary>スクリプトを単一トランザクション内で実行する（途中で例外発生時は ROLLBACK する）</summary>
    /// <remarks>全バッチ成功時のみ COMMIT し、原子性を保証する</remarks>
    public async Task<SchemaSyncResult> ExecuteAsync(
        DbConnectionSettings settings,
        string script,
        CancellationToken ct = default
    )
    {
        var result = new SchemaSyncResult();
        var batches = SplitBatches(script);

        if (batches.Count == 0)
        {
            result.Committed = true;
            return result;
        }

        await using var conn = new SqlConnection(SqlServerConnectionStringFactory.Build(settings));
        await conn.OpenAsync(ct).ConfigureAwait(false);
        await using var tran = (SqlTransaction)
            await conn.BeginTransactionAsync(ct).ConfigureAwait(false);

        try
        {
            for (var i = 0; i < batches.Count; i++)
            {
                var sql = batches[i];
                await using var cmd = DbCommands.Create(
                    conn,
                    sql,
                    settings.CommandTimeoutSeconds,
                    tran
                );
                await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                result.Batches.Add(new SchemaSyncBatchResult(i + 1, sql, true, null));
            }

            await tran.CommitAsync(ct).ConfigureAwait(false);
            result.Committed = true;
        }
        catch (Exception ex)
        {
            // ロールバック済みである旨は方言差があるため、表示側の見出しではなくエラー本文へ含める
            result.Error = string.Format(
                QuickER.Provider.Resources.Strings.Sync_Error_RolledBack,
                ex.Message
            );

            // 後始末の作法（完了済みなら no-op・失敗は握りつぶし・キャンセル不可）は共有ヘルパーに集約
            await DbTransactions.RollbackQuietlyAsync(tran).ConfigureAwait(false);

            result.Batches.Add(
                new SchemaSyncBatchResult(result.Batches.Count + 1, "", false, ex.Message)
            );
        }

        return result;
    }

    /// <summary>スクリプトを行頭の <c>GO</c> で分割する（大文字小文字を無視し、前後空白を許容する）</summary>
    public static List<string> SplitBatches(string script)
    {
        var list = new List<string>();

        if (string.IsNullOrWhiteSpace(script))
        {
            return list;
        }

        var lines = script.Replace("\r\n", "\n").Split('\n');
        var current = new StringBuilder();
        var goPattern = new Regex(@"^\s*GO\s*$", RegexOptions.IgnoreCase);

        foreach (var line in lines)
        {
            if (goPattern.IsMatch(line))
            {
                var batch = current.ToString().Trim();

                if (batch.Length > 0)
                {
                    list.Add(batch);
                }

                current.Clear();
            }
            else
            {
                current.AppendLine(line);
            }
        }

        var last = current.ToString().Trim();

        if (last.Length > 0)
        {
            list.Add(last);
        }

        return list;
    }
}
