namespace QuickER.AI.UI;

/// <summary>
/// アプリ終了などの同期的な終了経路から、チャットエンジンをまとめて破棄する。
/// </summary>
/// <remarks>
/// <para>
/// CLI バックエンドのエンジンは常駐する子プロセス（codex app-server・copilot ランタイム）を抱えており、
/// それを確実に止める経路はエンジンの破棄だけ。破棄せずにウィンドウを閉じると、子プロセスがアプリ終了後も残る。
/// </para>
/// <para>
/// 破棄そのものは必ずスレッドプールで走らせる。終了経路は UI スレッドで、ここで完了を待つため、
/// 破棄の途中に UI スレッドへのマーシャリングがあると待ちがデッドロックする。
/// 待ちに上限を設けるのは、破棄がハングしたときにアプリの終了を長く止めないため。
/// エンジンごとの失敗は握り潰す＝1 つの失敗で他のエンジンの破棄を止めない。
/// </para>
/// </remarks>
public static class EngineShutdown
{
    /// <summary>
    /// 渡したエンジンをすべて破棄し、完了か上限のどちらか早いほうまで待つ。
    /// </summary>
    /// <param name="timeout">破棄の完了を待つ上限。超過しても戻る（停止はベストエフォート）</param>
    /// <param name="engines">破棄するエンジン（<c>null</c> の要素は無視する）</param>
    public static void DisposeAll(TimeSpan timeout, params IAsyncDisposable?[] engines)
    {
        var shutdown = Task.Run(() => Task.WhenAll(engines.Select(DisposeQuietlyAsync)));

        try
        {
            shutdown.Wait(timeout);
        }
        catch (Exception)
        {
            // 破棄の失敗・待ちの打ち切りで終了処理を妨げない
        }
    }

    /// <summary>エンジン 1 つを破棄する（失敗は握り潰す）</summary>
    private static async Task DisposeQuietlyAsync(IAsyncDisposable? engine)
    {
        if (engine is null)
        {
            return;
        }

        try
        {
            await engine.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception)
        {
            // 終了経路のため、個々の破棄の失敗は報告先が無い
        }
    }
}
