using System.Data.Common;

namespace QuickER.Provider;

/// <summary>
/// DB コマンドの生成を方言横断で 1 箇所に集約する共通ヘルパー。
/// </summary>
/// <remarks>
/// <para>
/// スキーマ取込・スキーマ同期のコマンド生成箇所は、各方言のインポータ／同期実行器に 3〜7 箇所ずつ散在する。
/// 生成箇所ごとに <c>CommandTimeout</c> を書くと、クエリを 1 本足したときに設定が静かに漏れる
/// （実際、SQLite の同期実行器だけ 3 箇所とも未設定＝ADO 既定 30 秒のままだった）。そのため
/// 「コマンドを作る」と「タイムアウトを設定する」を分離できない形にして、設定漏れを構造的に防ぐ。
/// </para>
/// <para>
/// 戻り値は <see cref="DbCommand"/>（方言非依存）。取込・同期のクエリはいずれもパラメータを使わず
/// <c>CommandText</c> と <c>ExecuteReaderAsync</c> / <c>ExecuteNonQueryAsync</c> しか要求しないため、
/// 方言固有のコマンド型へキャストする必要はない。
/// </para>
/// </remarks>
public static class DbCommands
{
    /// <summary>コマンド実行タイムアウトの既定値（秒）。</summary>
    /// <remarks>
    /// ADO.NET の既定は 30 秒だが、スキーマ取込・同期は 1 文が長時間かかりうる（大量テーブルのカタログ照会・
    /// テーブル再構築を伴う同期）ため、既定を 60 秒に置く（従来ハードコードされていた値と同じ）。
    /// </remarks>
    public const int DefaultTimeoutSeconds = 60;

    /// <summary>接続からコマンドを生成し、同時に実行タイムアウトを設定する。</summary>
    /// <param name="connection">コマンドを生成する接続</param>
    /// <param name="commandText">実行する SQL</param>
    /// <param name="commandTimeoutSeconds">
    /// 実行タイムアウト（秒）。<c>0</c> は ADO.NET の規約どおり「無制限」を意味する。負値は不正
    /// （<see cref="ArgumentOutOfRangeException"/>）。
    /// </param>
    public static DbCommand Create(
        DbConnection connection,
        string commandText,
        int commandTimeoutSeconds
    ) => Create(connection, commandText, commandTimeoutSeconds, transaction: null);

    /// <summary>トランザクションへ参加させるコマンドを生成し、同時に実行タイムアウトを設定する。</summary>
    /// <param name="connection">コマンドを生成する接続</param>
    /// <param name="commandText">実行する SQL</param>
    /// <param name="commandTimeoutSeconds">
    /// 実行タイムアウト（秒）。<c>0</c> は ADO.NET の規約どおり「無制限」を意味する。負値は不正
    /// （<see cref="ArgumentOutOfRangeException"/>）。
    /// </param>
    /// <param name="transaction">参加させるトランザクション（<c>null</c> なら参加しない）</param>
    public static DbCommand Create(
        DbConnection connection,
        string commandText,
        int commandTimeoutSeconds,
        DbTransaction? transaction
    )
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentOutOfRangeException.ThrowIfNegative(commandTimeoutSeconds);

        var command = connection.CreateCommand();
        command.CommandText = commandText;
        command.CommandTimeout = commandTimeoutSeconds;

        if (transaction is not null)
        {
            command.Transaction = transaction;
        }

        return command;
    }
}
