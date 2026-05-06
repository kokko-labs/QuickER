using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;

namespace ERDesigner.Services;

/// <summary>
/// 生成済みの T-SQL スクリプトを SQL Server に対して 1 つのトランザクションで実行します。
/// <c>GO</c> 区切りでバッチ分割します (sqlcmd の慣習)。
/// </summary>
public class SchemaSyncExecutor
{
    /// <summary>1 件のバッチ実行結果。</summary>
    public sealed record BatchResult(int Index, string Sql, bool Success, string? Error);

    /// <summary>実行結果サマリ。</summary>
    public sealed class ExecutionResult
    {
        /// <summary>各バッチの結果。</summary>
        public List<BatchResult> Batches { get; } = new();

        /// <summary>すべて成功して COMMIT したか。</summary>
        public bool Committed { get; set; }

        /// <summary>失敗時のエラーメッセージ。</summary>
        public string? Error { get; set; }
    }

    /// <summary>
    /// スクリプトをトランザクション内で実行します。途中で例外が発生したら ROLLBACK します。
    /// </summary>
    public async Task<ExecutionResult> ExecuteAsync(SqlConnectionSettings settings, string script, CancellationToken ct = default)
    {
        var result = new ExecutionResult();
        var batches = SplitBatches(script);

        if (batches.Count == 0)
        {
            result.Committed = true;
            return result;
        }

        await using var conn = new SqlConnection(settings.Build());
        await conn.OpenAsync(ct).ConfigureAwait(false);
        await using var tran = (SqlTransaction)await conn.BeginTransactionAsync(ct).ConfigureAwait(false);

        try
        {
            for (var i = 0; i < batches.Count; i++)
            {
                var sql = batches[i];
                await using var cmd = new SqlCommand(sql, conn, tran);
                cmd.CommandTimeout = 60;
                await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                result.Batches.Add(new BatchResult(i + 1, sql, true, null));
            }

            await tran.CommitAsync(ct).ConfigureAwait(false);
            result.Committed = true;
        }
        catch (Exception ex)
        {
            result.Error = ex.Message;

            try
            {
                await tran.RollbackAsync(ct).ConfigureAwait(false);
            }
            catch
            { /* best effort */
            }

            result.Batches.Add(new BatchResult(result.Batches.Count + 1, "", false, ex.Message));
        }

        return result;
    }

    /// <summary>スクリプトを行頭の <c>GO</c> で分割します (大文字小文字無視・前後空白許容)。</summary>
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
