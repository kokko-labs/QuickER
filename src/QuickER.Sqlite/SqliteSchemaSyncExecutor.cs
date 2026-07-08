using System.Threading;
using System.Threading.Tasks;
using QuickER.Provider;

namespace QuickER.Sqlite;

/// <summary>SQLite の同期スクリプト実行の明示スタブ（初回スコープ外）</summary>
/// <remarks>
/// SQLite の同期は未対応のため実行経路も提供しない。理由と将来方針は
/// <see cref="SqliteSyncScriptBuilder"/> を参照（テーブル再構築方式で対応予定）。
/// </remarks>
public sealed class SqliteSchemaSyncExecutor : ISchemaSyncExecutor
{
    /// <summary>常に <see cref="NotSupportedException"/> を投げる（SQLite の同期は未対応）</summary>
    public Task<SchemaSyncResult> ExecuteAsync(
        DbConnectionSettings settings,
        string script,
        CancellationToken ct = default
    ) =>
        throw new NotSupportedException(
            QuickER.Provider.Resources.Strings.Sync_Sqlite_NotSupported
        );
}
