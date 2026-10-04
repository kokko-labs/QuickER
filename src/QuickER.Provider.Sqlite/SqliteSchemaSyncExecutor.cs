using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using QuickER.Provider;

namespace QuickER.Provider.Sqlite;

/// <summary>生成済みの SQLite 同期スクリプトを単一トランザクションで実行する</summary>
/// <remarks>
/// <para>
/// <see cref="SqliteSyncScriptBuilder"/> の出力との対規約:
/// スクリプトは <c>PRAGMA foreign_keys=OFF;</c> で始まり <c>PRAGMA foreign_key_check;</c> →
/// <c>PRAGMA foreign_keys=ON;</c> で終わる。実行手順は次のとおり。
/// </para>
/// <list type="number">
///   <item>
///     接続を開き <c>PRAGMA foreign_keys=OFF;</c> と <c>PRAGMA legacy_alter_table=ON;</c> を<b>トランザクション外</b>で
///     実行する。SQLite は <c>foreign_keys</c> PRAGMA をトランザクション内では no-op として無視するため、
///     再構築（旧テーブル DROP・親を後から作る等）を FK 強制なしで通すには BEGIN より前に切る必要がある。
///     <c>legacy_alter_table</c> は、作り直す表を参照するビューがあっても <c>RENAME</c> が失敗しないようにするため
///     （<see cref="SqliteSyncScriptBuilder"/> の remarks を参照）。接続はプールへ戻り得るので、最後に必ず
///     <c>foreign_keys</c> を同期前の値へ（同梱の e_sqlite3 の既定は ON）、<c>legacy_alter_table</c> を OFF へ戻す。
///   </item>
///   <item>
///     BEGIN → 同期前の外部キー違反（(子表, 親表) ごとの件数）と壊れたビューを控え、スクリプト本文を実行する
///     （Microsoft.Data.Sqlite は 1 コマンドで複数文を実行できる。本文中に埋まった PRAGMA はトランザクション内では無害）。
///   </item>
///   <item>
///     Executor 自身が <c>PRAGMA foreign_key_check</c> を <c>ExecuteReader</c> で実行し、同期前より違反が増えた
///     (子表, 親表) があれば ROLLBACK して子表名を <see cref="SchemaSyncResult.Error"/> に列挙する（<c>Committed=false</c>）。
///     同期前から在る違反（SQLite は既定で外部キーを検査しないので親の無い子行は珍しくない）では失敗にしない。
///   </item>
///   <item>
///     同期前には読めたのに読めなくなったビュー（作り直しで消した列を参照するもの等）があれば、同じく ROLLBACK する。
///   </item>
///   <item>どちらも無ければ COMMIT する（<c>Committed=true</c>）。</item>
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

        // 接続はプールへ戻り得るので、元の外部キー検査の設定を控えて最後に戻す
        // （同梱の e_sqlite3 は既定で ON。OFF のまま返すと、同じ接続文字列で後から開いた接続が検査なしになる）
        long foreignKeysBefore;

        await using (
            var read = DbCommands.Create(
                conn,
                "PRAGMA foreign_keys;",
                settings.CommandTimeoutSeconds
            )
        )
        {
            foreignKeysBefore = Convert.ToInt64(
                await read.ExecuteScalarAsync(ct).ConfigureAwait(false)
            );
        }

        // FK 強制はトランザクション外で切る（トランザクション内の同 PRAGMA は no-op のため）。
        // RENAME がスキーマ全体を検証し直さないよう旧来の動作にもする（作り直す表を参照するビューで失敗しないため）
        await ExecutePragmaAsync(
                conn,
                "PRAGMA foreign_keys=OFF; PRAGMA legacy_alter_table=ON;",
                settings.CommandTimeoutSeconds,
                ct
            )
            .ConfigureAwait(false);

        try
        {
            return await ExecuteInTransactionAsync(conn, settings, script, result, ct)
                .ConfigureAwait(false);
        }
        finally
        {
            // 接続はプールへ戻り得るので、外部キー検査を元の設定へ戻し、旧来の RENAME の動作を残さない
            // （どちらもトランザクションの外でしか効かない。後始末の失敗で結果を壊さない）
            try
            {
                await ExecutePragmaAsync(
                        conn,
                        $"PRAGMA foreign_keys={(foreignKeysBefore != 0 ? "ON" : "OFF")}; "
                            + "PRAGMA legacy_alter_table=OFF;",
                        settings.CommandTimeoutSeconds,
                        CancellationToken.None
                    )
                    .ConfigureAwait(false);
            }
            catch (SqliteException) { }
        }
    }

    /// <summary>トランザクション内でスクリプトを実行し、外部キーとビューを同期の前後で検査してコミットする</summary>
    /// <remarks>
    /// 外部キーの違反とビューの破損は、同期で<b>増えたとき</b>だけ失敗にする。SQLite は既定で外部キーを検査しないため
    /// 親の無い子行を抱えた DB は珍しくなく、DB 全体の検査結果で判定すると、無関係な同期まで失敗する
    /// （壊れたビューを作れるのも同じ）。比べる単位は外部キー違反が (子表, 親表) ごとの件数、ビューが名前。
    /// </remarks>
    private static async Task<SchemaSyncResult> ExecuteInTransactionAsync(
        SqliteConnection conn,
        DbConnectionSettings settings,
        string script,
        SchemaSyncResult result,
        CancellationToken ct
    )
    {
        await using var tran = (SqliteTransaction)
            await conn.BeginTransactionAsync(ct).ConfigureAwait(false);

        try
        {
            // 同期前の状態（既存の外部キー違反と壊れたビュー）を控える
            var violationsBefore = await CountForeignKeyViolationsAsync(
                    conn,
                    tran,
                    settings.CommandTimeoutSeconds,
                    ct
                )
                .ConfigureAwait(false);
            var brokenViewsBefore = await CollectBrokenViewsAsync(
                    conn,
                    tran,
                    settings.CommandTimeoutSeconds,
                    ct
                )
                .ConfigureAwait(false);

            // スクリプト本文（複数文）を 1 コマンドで実行する
            await using (
                var cmd = DbCommands.Create(conn, script, settings.CommandTimeoutSeconds, tran)
            )
            {
                await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            }

            // FK 整合性を実検査する（同期で違反が増えた (子表, 親表) があれば安全側にロールバックする）
            var violationsAfter = await CountForeignKeyViolationsAsync(
                    conn,
                    tran,
                    settings.CommandTimeoutSeconds,
                    ct
                )
                .ConfigureAwait(false);
            var violatingTables = violationsAfter
                .Where(entry => entry.Value > violationsBefore.GetValueOrDefault(entry.Key))
                .Select(entry => entry.Key.Table)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (violatingTables.Count > 0)
            {
                // 結果は「FK 違反」で確定しており、後始末の失敗・キャンセルでこの報告を壊さない
                await DbTransactions.RollbackQuietlyAsync(tran).ConfigureAwait(false);
                result.Error = string.Format(
                    QuickER.Provider.Resources.Strings.Sync_Error_SqliteForeignKeyViolation,
                    string.Join(", ", violatingTables)
                );
                result.Batches.Add(new SchemaSyncBatchResult(1, script, false, result.Error));
                return result;
            }

            // 作り直しで壊れたビュー（消した列を参照するもの等）があればロールバックする
            var newlyBrokenViews = (
                await CollectBrokenViewsAsync(conn, tran, settings.CommandTimeoutSeconds, ct)
                    .ConfigureAwait(false)
            )
                .Where(view => !brokenViewsBefore.Contains(view))
                .ToList();

            if (newlyBrokenViews.Count > 0)
            {
                await DbTransactions.RollbackQuietlyAsync(tran).ConfigureAwait(false);
                result.Error = string.Format(
                    QuickER.Provider.Resources.Strings.Sync_Error_SqliteBrokenView,
                    string.Join(", ", newlyBrokenViews)
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

            // 後始末の作法（完了済みなら no-op・失敗は握りつぶし・キャンセル不可）は共有ヘルパーに集約
            await DbTransactions.RollbackQuietlyAsync(tran).ConfigureAwait(false);

            result.Batches.Add(new SchemaSyncBatchResult(1, script, false, ex.Message));
        }

        return result;
    }

    /// <summary>トランザクションの外で PRAGMA を実行する</summary>
    private static async Task ExecutePragmaAsync(
        SqliteConnection conn,
        string pragma,
        int commandTimeoutSeconds,
        CancellationToken ct
    )
    {
        await using var cmd = DbCommands.Create(conn, pragma, commandTimeoutSeconds);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    /// <summary>読み込めない（壊れた）ビューの名前を集める</summary>
    /// <remarks>
    /// SQLite はビューを作るときにも参照先を検証しないため、壊れたビューは既存の DB にもあり得る。
    /// 各ビューを <c>SELECT * … LIMIT 0</c> で準備させ、失敗したものを壊れたビューとする。
    /// </remarks>
    private static async Task<HashSet<string>> CollectBrokenViewsAsync(
        SqliteConnection conn,
        SqliteTransaction tran,
        int commandTimeoutSeconds,
        CancellationToken ct
    )
    {
        var views = new List<string>();

        await using (
            var list = DbCommands.Create(
                conn,
                "SELECT name FROM sqlite_master WHERE type = 'view';",
                commandTimeoutSeconds,
                tran
            )
        )
        await using (var reader = await list.ExecuteReaderAsync(ct).ConfigureAwait(false))
        {
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                views.Add(reader.GetString(0));
            }
        }

        var broken = new HashSet<string>(StringComparer.Ordinal);

        foreach (var view in views)
        {
            try
            {
                await using var probe = DbCommands.Create(
                    conn,
                    $"SELECT * FROM {SqliteIdentifier.QuoteSimple(view)} LIMIT 0;",
                    commandTimeoutSeconds,
                    tran
                );
                await using var reader = await probe.ExecuteReaderAsync(ct).ConfigureAwait(false);
            }
            catch (SqliteException)
            {
                broken.Add(view);
            }
        }

        return broken;
    }

    /// <summary><c>PRAGMA foreign_key_check</c> の違反を (子表, 親表) ごとに数える</summary>
    private static async Task<
        Dictionary<(string Table, string Parent), int>
    > CountForeignKeyViolationsAsync(
        SqliteConnection conn,
        SqliteTransaction tran,
        int commandTimeoutSeconds,
        CancellationToken ct
    )
    {
        var counts = new Dictionary<(string Table, string Parent), int>(
            ForeignKeyViolationKeyComparer.Instance
        );

        await using var cmd = DbCommands.Create(
            conn,
            "PRAGMA foreign_key_check;",
            commandTimeoutSeconds,
            tran
        );
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);

        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            // foreign_key_check の列は table / rowid / parent / fkid。先頭が違反のあった子テーブル名
            if (reader.IsDBNull(0))
            {
                continue;
            }

            var key = (
                reader.GetString(0),
                reader.IsDBNull(2) ? string.Empty : reader.GetString(2)
            );
            counts[key] = counts.GetValueOrDefault(key) + 1;
        }

        return counts;
    }

    /// <summary>(子表, 親表) の組を大文字小文字を無視して比べる（SQLite の表名の比較に合わせる）</summary>
    private sealed class ForeignKeyViolationKeyComparer
        : IEqualityComparer<(string Table, string Parent)>
    {
        public static readonly ForeignKeyViolationKeyComparer Instance = new();

        public bool Equals((string Table, string Parent) x, (string Table, string Parent) y) =>
            StringComparer.OrdinalIgnoreCase.Equals(x.Table, y.Table)
            && StringComparer.OrdinalIgnoreCase.Equals(x.Parent, y.Parent);

        public int GetHashCode((string Table, string Parent) obj) =>
            HashCode.Combine(
                StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Table),
                StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Parent)
            );
    }
}
