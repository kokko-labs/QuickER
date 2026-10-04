using System.Threading;
using System.Threading.Tasks;

namespace QuickER.Provider;

/// <summary>同期スクリプトの実行（DB 方言ごとに実装）</summary>
public interface ISchemaSyncExecutor
{
    /// <summary>同期スクリプトを実行する</summary>
    /// <remarks>
    /// SQL Server・PostgreSQL・SQLite は単一トランザクションで実行し、途中で失敗すればロールバックする。
    /// MySQL と Oracle は DDL が暗黙にコミットされるため、途中で失敗するとそこまでの文が適用されたまま残る
    /// （部分適用。各実装の XmlDoc と <see cref="SchemaSyncResult"/> の報告を参照）。
    /// </remarks>
    /// <param name="settings">接続設定</param>
    /// <param name="script">実行する同期スクリプト</param>
    /// <param name="ct">キャンセルトークン</param>
    Task<SchemaSyncResult> ExecuteAsync(
        DbConnectionSettings settings,
        string script,
        CancellationToken ct = default
    );
}
