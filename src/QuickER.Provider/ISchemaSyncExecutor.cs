using System.Threading;
using System.Threading.Tasks;

namespace QuickER.Provider;

/// <summary>同期スクリプトの実行（DB 方言ごとに実装）</summary>
public interface ISchemaSyncExecutor
{
    /// <summary>スクリプトを単一トランザクション内で実行する（途中で例外発生時は ROLLBACK する）</summary>
    /// <param name="settings">接続設定</param>
    /// <param name="script">実行する同期スクリプト</param>
    /// <param name="ct">キャンセルトークン</param>
    Task<SchemaSyncResult> ExecuteAsync(
        DbConnectionSettings settings,
        string script,
        CancellationToken ct = default
    );
}
