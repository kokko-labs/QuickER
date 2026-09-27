namespace QuickER.Gui.Abstractions;

/// <summary>アプリ終了の連鎖を、1 つの失敗が他を止めない形で実行するための器</summary>
/// <remarks>
/// 終了の連鎖は「中断 → 設定保存 → エンジンの破棄 → クローズ」のように、後段ほど
/// 取り返しがつかない後始末（常駐する子プロセスの停止）が並ぶ。前段の失敗で後段へ
/// 届かなくなると子プロセスが孤児として残るため、各段をこの器に通して独立させる。
/// 受け止めの形を 1 箇所へ集約するのは、段を足したときに受け止めだけ書き忘れる乖離を防ぐため。
/// </remarks>
public static class ShutdownSteps
{
    /// <summary>終了の連鎖の 1 段を実行し、失敗したら記録だけして次の段へ進ませる</summary>
    /// <param name="reporter">失敗の記録先（必須。未登録で黙って捨てる経路は作らない）</param>
    /// <param name="step">
    /// この段を表す短い識別子（英語・例 <c>AiChat.SaveSettings</c>）。記録の本文へ載る
    /// </param>
    /// <param name="action">実行する後始末</param>
    /// <remarks>
    /// 記録そのものが投げた場合も握り潰す。<see cref="IShutdownFailureReporter"/> の契約上
    /// 起きてはならないが、ここで漏らすと器の目的（連鎖を止めない）がそのまま破れるため。
    /// </remarks>
    public static void Run(IShutdownFailureReporter reporter, string step, Action action)
    {
        ArgumentNullException.ThrowIfNull(reporter);
        ArgumentNullException.ThrowIfNull(action);

        try
        {
            action();
        }
        catch (Exception ex)
        {
            try
            {
                reporter.Report(step, ex);
            }
            catch
            {
                // 記録できなくても終了の連鎖は続ける（記録は証跡を残すための付随処理）
            }
        }
    }
}
