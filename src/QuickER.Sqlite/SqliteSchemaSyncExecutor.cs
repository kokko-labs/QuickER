using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using QuickER.Provider;

namespace QuickER.Sqlite;

/// <summary>生成済みの SQLite 同期スクリプトを単一トランザクションで実行する</summary>
/// <remarks>
/// <para>
/// <see cref="SqliteSyncScriptBuilder"/> の出力との対規約:
/// スクリプトは <c>PRAGMA foreign_keys=OFF;</c> で始まり <c>PRAGMA foreign_key_check;</c> →
/// <c>PRAGMA foreign_keys=ON;</c> で終わる。実行手順は次のとおり。
/// </para>
/// <list type="number">
///   <item>
///     接続を開き <c>PRAGMA foreign_keys=OFF;</c> を<b>トランザクション外</b>で実行する。
///     SQLite は <c>foreign_keys</c> PRAGMA をトランザクション内では no-op として無視するため、
///     再構築（旧テーブル DROP・親を後から作る等）を FK 強制なしで通すには BEGIN より前に切る必要がある。
///   </item>
///   <item>
///     BEGIN → スクリプト本文を実行する（Microsoft.Data.Sqlite は 1 コマンドで複数文を実行できる。
///     本文中に埋まった <c>PRAGMA foreign_keys</c> はトランザクション内では無害な no-op）。
///   </item>
///   <item>
///     Executor 自身が <c>PRAGMA foreign_key_check</c> を <c>ExecuteReader</c> で実行し、違反行があれば
///     ROLLBACK して違反テーブル名を <see cref="SchemaSyncResult.Error"/> に列挙する（<c>Committed=false</c>）。
///   </item>
///   <item>違反が無ければ COMMIT する（<c>Committed=true</c>）。</item>
/// </list>
/// </remarks>
public sealed class SqliteSchemaSyncExecutor : ISchemaSyncExecutor
{
    /// <inheritdoc />
    public async Task<SchemaSyncResult> ExecuteAsync(
        DbConnectionSettings settings,
        string script,
        CancellationToken ct = default
    )
    {
        var result = new SchemaSyncResult();

        // 空スクリプトは no-op として成功扱い（差分ゼロ・全未選択のケース）
        if (string.IsNullOrWhiteSpace(script))
        {
            result.Committed = true;
            return result;
        }

        // 同期は既存 DB への書き込み。取込専用の ReadOnly ファクトリは使わず、存在必須の ReadWrite で開く
        // （ReadWriteCreate にすると誤パスで空 DB を生成する事故を招くため）
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = settings.FilePath,
            Mode = SqliteOpenMode.ReadWrite,
        }.ConnectionString;

        await using var conn = new SqliteConnection(connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);

        // FK 強制はトランザクション外で切る（トランザクション内の同 PRAGMA は no-op のため）
        await using (var pragma = conn.CreateCommand())
        {
            pragma.CommandText = "PRAGMA foreign_keys=OFF;";
            await pragma.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        await using var tran = (SqliteTransaction)
            await conn.BeginTransactionAsync(ct).ConfigureAwait(false);

        try
        {
            // スクリプト本文（複数文）を 1 コマンドで実行する
            await using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tran;
                cmd.CommandText = script;
                await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            }

            // FK 整合性を実検査する（違反があれば安全側にロールバックする）
            var violatingTables = await CollectForeignKeyViolationsAsync(conn, tran, ct)
                .ConfigureAwait(false);

            if (violatingTables.Count > 0)
            {
                await tran.RollbackAsync(ct).ConfigureAwait(false);
                result.Error = string.Format(
                    QuickER.Provider.Resources.Strings.Sync_Error_SqliteForeignKeyViolation,
                    string.Join(", ", violatingTables)
                );
                result.Batches.Add(new SchemaSyncBatchResult(1, script, false, result.Error));
                return result;
            }

            await tran.CommitAsync(ct).ConfigureAwait(false);
            result.Committed = true;
            result.Batches.Add(new SchemaSyncBatchResult(1, script, true, null));
        }
        catch (Exception ex)
        {
            // ロールバック済みである旨は方言差があるため、表示側の見出しではなくエラー本文へ含める
            result.Error = string.Format(
                QuickER.Provider.Resources.Strings.Sync_Error_RolledBack,
                ex.Message
            );

            try
            {
                await tran.RollbackAsync(ct).ConfigureAwait(false);
            }
            catch
            {
                // ロールバック自体の失敗は最善努力で握りつぶす（元の例外情報を優先する）。
                // 明示ロールバックに失敗しても、未コミットのトランザクションは破棄時に取り消される
            }

            result.Batches.Add(new SchemaSyncBatchResult(1, script, false, ex.Message));
        }

        return result;
    }

    /// <summary><c>PRAGMA foreign_key_check</c> で FK 違反のあるテーブル名（重複除去）を収集する</summary>
    private static async Task<List<string>> CollectForeignKeyViolationsAsync(
        SqliteConnection conn,
        SqliteTransaction tran,
        CancellationToken ct
    )
    {
        var tables = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        await using var cmd = conn.CreateCommand();
        cmd.Transaction = tran;
        cmd.CommandText = "PRAGMA foreign_key_check;";
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);

        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            // foreign_key_check の列は table / rowid / parent / fkid。先頭が違反のあった子テーブル名
            if (reader.IsDBNull(0))
            {
                continue;
            }

            var table = reader.GetString(0);

            if (seen.Add(table))
            {
                tables.Add(table);
            }
        }

        return tables;
    }
}
