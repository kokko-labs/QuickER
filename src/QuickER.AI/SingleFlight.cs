namespace QuickER.AI;

/// <summary>
/// 同じ非同期処理（接続・初期化）が重ねて呼ばれたとき、実行中の 1 本を共有する。
/// </summary>
/// <remarks>
/// <para>
/// 接続方式タブの素早い往復や、タブ切替と「再確認」の重なりで、エンジンの接続・初期化が 2 本同時に走ると、
/// 2 本目が 1 本目の途中の状態（プロセスは起動済みでハンドシェイクは未完了など）を見て先へ進んでしまう。
/// 実行中の呼び出しがあればその処理を返し、新しく起動しない。
/// </para>
/// <para>
/// 共有するのは<b>実行中のあいだだけ</b>。完了した処理（成功・失敗・取り消しのいずれでも）は保持せず、
/// 次の呼び出しは処理をやり直す（失敗した接続を以後ずっと返し続けない）。
/// </para>
/// <para>
/// 処理を起動した呼び出し元の取り消しトークンが、そのまま処理へ渡る。相乗りした呼び出し元は、
/// 自分のトークンで「待つのをやめる」ことはできるが、処理そのものは取り消さない。
/// 代償として、起動した呼び出し元が取り消すと、相乗りした呼び出し元にも取り消しが届く
/// （呼び出し元はいずれも既定のトークンか、ターンの中断用のトークンなので実害は無い）。
/// </para>
/// </remarks>
internal sealed class SingleFlight
{
    private readonly object _gate = new();

    /// <summary>実行中（または直近に完了した）処理</summary>
    private Task? _running;

    /// <summary>実行中の処理があればそれを、なければ <paramref name="start"/> を起動した処理を待つ</summary>
    /// <param name="start">処理を起動する（実行中の処理が無いときだけ、このトークンを渡して呼ぶ）</param>
    /// <param name="cancellationToken">
    /// 処理を起動したときは処理へ渡すトークン、相乗りしたときは待つのをやめるためのトークン
    /// </param>
    public Task RunAsync(
        Func<CancellationToken, Task> start,
        CancellationToken cancellationToken = default
    )
    {
        Task<Task>? cold = null;
        Task shared;

        lock (_gate)
        {
            if (_running is null || _running.IsCompleted)
            {
                // ロックの中で起動しない（起動の同期部分が UI スレッドへの同期呼び出しをすると、
                // UI スレッドがこのロックを待っている場合にデッドロックする）。未起動のタスクだけ作って登録する
                cold = new Task<Task>(() => start(cancellationToken));
                _running = cold.Unwrap();
            }

            shared = _running;
        }

        if (cold is not null)
        {
            // 起動は呼び出し元のスレッドで同期的に行う（起動の同期部分は従来どおり呼び出し元で走る）
            cold.RunSynchronously(TaskScheduler.Default);
            return shared;
        }

        return cancellationToken.CanBeCanceled ? shared.WaitAsync(cancellationToken) : shared;
    }
}
