using System.Threading;
using System.Threading.Tasks;
using Npgsql;
using QuickER.Provider;

namespace QuickER.PostgreSql;

/// <summary>生成済みの DDL スクリプトを単一トランザクションで PostgreSQL に対し実行する</summary>
/// <remarks>
/// Npgsql は複数文を 1 コマンドで実行できるため、スクリプト全体を単一トランザクション内で実行し、
/// 成功時のみ COMMIT する（<see cref="SchemaSyncResult.Committed"/> に反映）。
/// </remarks>
public sealed class PostgreSqlSchemaSyncExecutor : ISchemaSyncExecutor
{
    /// <summary>スクリプトを単一トランザクション内で実行する（途中で例外発生時は ROLLBACK する）</summary>
    /// <remarks>全文成功時のみ COMMIT し、原子性を保証する</remarks>
    public async Task<SchemaSyncResult> ExecuteAsync(
        DbConnectionSettings settings,
        string script,
        CancellationToken ct = default
    )
    {
        var result = new SchemaSyncResult();

        if (string.IsNullOrWhiteSpace(script))
        {
            result.Committed = true;
            return result;
        }

        await using var conn = new NpgsqlConnection(
            PostgreSqlConnectionStringFactory.Build(settings)
        );
        await conn.OpenAsync(ct).ConfigureAwait(false);
        await using var tran = await conn.BeginTransactionAsync(ct).ConfigureAwait(false);

        try
        {
            await using var cmd = DbCommands.Create(
                conn,
                script,
                settings.CommandTimeoutSeconds,
                tran
            );
            await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            result.Batches.Add(new SchemaSyncBatchResult(1, script, true, null));

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
}
