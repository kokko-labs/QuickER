namespace QuickER.Tests.AI.ChatEngines;

/// <summary>ターンの途中でバックエンドが流す 1 手（エンジンに依らない中立の台本）</summary>
/// <remarks>アダプタがそのバックエンドの入口（イベントの発火・ドライバの応答・クライアントの戻り値）へ訳す</remarks>
internal abstract record ChatStep
{
    /// <summary>アシスタントの応答の差分</summary>
    /// <param name="Text">差分のテキスト</param>
    public sealed record Delta(string Text) : ChatStep;

    /// <summary>ツールの呼び出しの要求</summary>
    /// <param name="Id">呼び出しの Id（バックエンドが Id を運ぶときに使う）</param>
    /// <param name="Name">ツール名</param>
    /// <param name="ArgumentsJson">引数の JSON</param>
    public sealed record ToolCall(string Id, string Name, string ArgumentsJson) : ChatStep;
}

/// <summary>ターンの終わり方（エンジンに依らない中立の台本）</summary>
internal abstract record ChatTurnEnding
{
    /// <summary>送信（ターンを始める呼び出し）そのものが例外で終わる終わり方か</summary>
    public virtual bool FailsSend => false;

    /// <summary>バックエンドが成功で終える</summary>
    public sealed record Success : ChatTurnEnding;

    /// <summary>バックエンドが理由つきの失敗を報告する（例外ではなく、プロトコルの失敗の通知）</summary>
    /// <param name="Detail">報告された理由（理由の無い報告は null）</param>
    public sealed record ReportedFailure(string? Detail) : ChatTurnEnding;

    /// <summary>送信（ターンの実行）が例外で終わる</summary>
    /// <param name="Exception">投げる例外</param>
    public sealed record Throws(Exception Exception) : ChatTurnEnding
    {
        /// <inheritdoc/>
        public override bool FailsSend => true;
    }

    /// <summary>送信の途中で呼び出し元のトークンが取り消され、取り消しの例外で終わる</summary>
    public sealed record CallerCancels : ChatTurnEnding
    {
        /// <inheritdoc/>
        public override bool FailsSend => true;
    }

    /// <summary>呼び出し元のトークンは立っていないのに、取り消しの例外で終わる（タイムアウトなど）</summary>
    /// <param name="Exception">投げる取り消しの例外</param>
    public sealed record ForeignCancellation(OperationCanceledException Exception) : ChatTurnEnding
    {
        /// <inheritdoc/>
        public override bool FailsSend => true;
    }

    /// <summary>利用者がターンの途中で中断し、バックエンドが中断として終える</summary>
    /// <param name="BackendThrows">
    /// 中断を受けたバックエンドが取り消しの例外で終えるか（偽なら理由の無い失敗を返す）。送信の中でターンを走らせる
    /// バックエンドだけが 2 つの終え方を持つ
    /// </param>
    public sealed record UserInterrupts(bool BackendThrows = false) : ChatTurnEnding;

    /// <summary>終わらない（ターンを実行中のまま残す。中断の行で使う）</summary>
    public sealed record Open : ChatTurnEnding;
}

/// <summary>1 ターンの中立の台本（途中の手の列と終わり方）</summary>
/// <param name="Steps">途中の手</param>
/// <param name="Ending">終わり方</param>
internal sealed record ChatTurnScript(IReadOnlyList<ChatStep> Steps, ChatTurnEnding Ending)
{
    /// <summary>送信に同梱する添付（既定は無し）</summary>
    public IReadOnlyList<QuickER.AI.ChatAttachment> Attachments { get; init; } = [];

    /// <summary>台本を作る</summary>
    /// <param name="ending">終わり方</param>
    /// <param name="steps">途中の手</param>
    public static ChatTurnScript Of(ChatTurnEnding ending, params ChatStep[] steps) =>
        new(steps, ending);
}
