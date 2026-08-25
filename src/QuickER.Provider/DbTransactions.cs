using System.Data.Common;

namespace QuickER.Provider;

/// <summary>
/// 失敗経路のトランザクション後始末を方言横断で 1 箇所に集約する共通ヘルパー。
/// </summary>
/// <remarks>
/// <para>
/// スキーマ同期の実行器は「途中失敗なら ROLLBACK して結果へエラーを載せる」形を 3 方言
/// （SQL Server / PostgreSQL / SQLite。MySQL / Oracle は DDL が暗黙コミットされるため対象外）で共有する。
/// ロールバックの作法を呼び出し側ごとに書く形にすると、キャンセル済みトークンを渡す・完了済みへ
/// Rollback を投げる、といった「後始末が本来の結果を壊す」書き方が静かに紛れ込むため、1 箇所へ集約する。
/// </para>
/// <para>
/// 生成コード側の同じ問題は <c>SqlTransactions.RollbackQuietlyAsync</c>（Templates/CSharpRuntime）が
/// 同じ意味論で解いている。片方だけ意味論を変えないこと。
/// </para>
/// </remarks>
public static class DbTransactions
{
    /// <summary>後始末としてトランザクションを静かにロールバックする（後始末の失敗で本来の結果を壊さない）。</summary>
    /// <remarks>
    /// <para>
    /// 完了済み（コミット済み・ロールバック済み）のトランザクションへの Rollback は
    /// <see cref="InvalidOperationException"/> になり、catch 節から出た例外は伝播中の元の例外を置き換えてしまう。
    /// 本当の失敗原因を残すため、完了済み（<see cref="DbTransaction.Connection"/> が null）なら何もせず、
    /// ロールバック自体が投げた例外は最善努力として握りつぶす。
    /// </para>
    /// <para>
    /// 握りつぶしても取り残しは生じない: 呼び出し側は復帰時に接続を破棄し、未コミットのトランザクションは
    /// それだけで取り消される。またロールバックは常に <see cref="CancellationToken.None"/> で実行する
    /// （キャンセル済みのトークンが後始末そのものを中断してはならないため）。
    /// </para>
    /// </remarks>
    /// <param name="transaction">ロールバックするトランザクション</param>
    public static async Task RollbackQuietlyAsync(DbTransaction transaction)
    {
        // 完了済みのトランザクションは接続を手放している＝取り消すものが残っていないことの方言非依存な判定
        if (transaction.Connection is null)
        {
            return;
        }

        try
        {
            await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch
        {
            // ロールバック自体の失敗は最善努力で握りつぶす（元の例外情報を優先する）。
            // 明示ロールバックに失敗しても、未コミットのトランザクションは破棄時に取り消される
        }
    }
}
