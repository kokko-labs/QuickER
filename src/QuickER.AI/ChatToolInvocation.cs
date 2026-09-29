namespace QuickER.AI;

/// <summary>
/// チャットエンジン 4 種が共有する、ER 図ツールの実行と活動通知の作法。
/// </summary>
/// <remarks>
/// <para>
/// 「ディスパッチャ経由で実行し、例外は失敗のツール結果へ畳む」「活動通知は購読側の例外から守る」の
/// 2 つは、API キー接続・Claude Code・Codex・Copilot の 4 エンジンで同じでなければならない。
/// 4 箇所に写していた間に実際にずれた（通知を保護していないエンジン・通知の失敗を「応答送信の失敗」と
/// 表示するエンジン）ので、ここへ集約する。
/// </para>
/// <para>
/// <b>持つのはこの 2 つだけ</b>。応答の送り方・中断の通し方・ツールホスト未設定の扱いはエンジンごとに
/// 違うので、呼び出し側に残す（間接層を増やすだけの抽象化にしない）。
/// </para>
/// </remarks>
internal static class ChatToolInvocation
{
    /// <summary>ツールを 1 件実行し、例外は「失敗のツール結果」へ畳む</summary>
    /// <param name="toolHost">ER 図ツールの実行先</param>
    /// <param name="dispatcher">UI スレッドへのマーシャリング（ER 図操作は UI スレッドで行う）</param>
    /// <param name="toolName">ツール名</param>
    /// <param name="argumentsJson">
    /// ツール引数の JSON を取り出す関数（<b>取り出し自体もこの try の内側で行う</b>）
    /// </param>
    /// <param name="rethrowCancellation">
    /// <see cref="OperationCanceledException"/> を畳まずに通すか
    /// </param>
    /// <remarks>
    /// <para>
    /// 例外をそのまま伝播させると、1 件の失敗でターン全体が失敗し、後続のツール呼び出しが実行されない。
    /// ツール結果は AI へ返る機械向けの文言なので英語で固定する。
    /// </para>
    /// <para>
    /// <b>引数を関数で受けるのは、取り出しを try の内側へ入れるため。</b>値で受けると呼び出し側の
    /// 引数評価が try の外で走る＝取り出しが投げるエンジン（Codex は <c>arguments</c> を持たない要求で
    /// <c>JsonElement.GetRawText()</c> が <see cref="InvalidOperationException"/> を投げる）で、
    /// 失敗結果を返せずに例外が抜け、応答を送れないまま AI がツール結果を待ち続ける。
    /// </para>
    /// <para>
    /// <paramref name="rethrowCancellation"/> が 4 エンジンの<b>唯一の差</b>。API キー接続だけは中断を
    /// 通す必要がある（中断はターンの中止であってツールの失敗ではなく、未実行の呼び出しへ合成結果を補って
    /// 履歴の tool_use↔tool 対応を保つ経路へ渡すため）。残る 3 つは中断もツールの失敗として畳む
    /// 従来の挙動で、揃えると挙動が変わるため引数にしてある。
    /// </para>
    /// </remarks>
    internal static (string Result, bool Success) Execute(
        IErDiagramToolHost toolHost,
        IUiDispatcher dispatcher,
        string toolName,
        Func<string> argumentsJson,
        bool rethrowCancellation
    )
    {
        try
        {
            var arguments = argumentsJson();

            return dispatcher.Invoke(() => toolHost.Execute(toolName, arguments));
        }
        catch (OperationCanceledException) when (rethrowCancellation)
        {
            throw;
        }
        catch (Exception ex)
        {
            return ($"The tool '{toolName}' threw an exception: {ex.Message}", false);
        }
    }

    /// <summary>活動通知を、購読側の例外から呼び出し元を守って発火する</summary>
    /// <param name="handler">通知の購読者（<c>null</c> なら何もしない）</param>
    /// <param name="sender">イベントの送信元</param>
    /// <param name="activity">通知する活動</param>
    /// <param name="reportStatus">通知に失敗したことの報告先（<c>null</c> なら報告しない）</param>
    /// <remarks>
    /// 購読側（UI）の例外を素通しすると、エンジンごとに違う形で壊れる＝応答送信まで巻き込まれて
    /// AI がツール結果を待ち続けたり（CLI 3 種）、実行済みの呼び出しへ「実行されなかった」合成結果が
    /// 積まれて AI が同じ操作をやり直したり（API キー接続）する。通知は付随処理なので、
    /// 失敗しても呼び出し元の処理は必ず続ける。
    /// </remarks>
    internal static void Notify(
        EventHandler<ErChatToolActivity>? handler,
        object sender,
        ErChatToolActivity activity,
        Action<string>? reportStatus
    )
    {
        try
        {
            handler?.Invoke(sender, activity);
        }
        catch (Exception ex)
        {
            reportStatus?.Invoke(
                string.Format(Resources.Strings.Chat_ToolActivityNotifyFailed, ex.Message)
            );
        }
    }
}
