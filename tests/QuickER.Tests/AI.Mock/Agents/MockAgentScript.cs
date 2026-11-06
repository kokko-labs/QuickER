namespace QuickER.Tests.AI.Mock.Agents;

/// <summary>ターンの途中でバックエンドが流す 1 手（実行器に依らない中立の台本）</summary>
/// <remarks>アダプタがそのバックエンドの入口（イベントの発火・クライアントの戻り値・エンジンの通知）へ訳す</remarks>
internal abstract record MockAgentStep
{
    /// <summary>アシスタントの文章の差分</summary>
    /// <param name="Text">差分のテキスト</param>
    public sealed record Delta(string Text) : MockAgentStep;

    /// <summary>バックエンドがツール（コマンドの実行）を始めた</summary>
    public sealed record ToolStarted : MockAgentStep;

    /// <summary>バックエンドが許可の範囲の外の要求を断った</summary>
    /// <param name="Description">断った要求の説明</param>
    public sealed record PermissionDeclined(string Description) : MockAgentStep;
}

/// <summary>ターンの終わり方（実行器に依らない中立の台本）</summary>
internal abstract record MockAgentTurnEnding
{
    /// <summary>成功で終わる（API キーの実行器ではパイプラインの全部のターンが提出して成功する）</summary>
    public sealed record Success : MockAgentTurnEnding;

    /// <summary>バックエンドが理由つきの失敗を報告する</summary>
    /// <param name="Detail">報告された理由</param>
    public sealed record ReportedFailure(string Detail) : MockAgentTurnEnding;

    /// <summary>バックエンドが中断として終える（取り消しの例外ではなく、中断の完了の通知・理由の無い失敗）</summary>
    public sealed record Interrupted : MockAgentTurnEnding;

    /// <summary>完了を待つ間にバックエンドとの接続が切れる</summary>
    public sealed record Disconnected : MockAgentTurnEnding;

    /// <summary>終わらない（ターンを実行中のまま残す。取り消し・中断の行で使う）</summary>
    public sealed record Open : MockAgentTurnEnding;
}

/// <summary>1 ターンの中立の台本（途中の手の列と終わり方）</summary>
/// <param name="Steps">途中の手</param>
/// <param name="Ending">終わり方</param>
internal sealed record MockAgentTurn(IReadOnlyList<MockAgentStep> Steps, MockAgentTurnEnding Ending)
{
    /// <summary>ターンを作る</summary>
    /// <param name="ending">終わり方</param>
    /// <param name="steps">途中の手</param>
    public static MockAgentTurn Of(MockAgentTurnEnding ending, params MockAgentStep[] steps) =>
        new(steps, ending);
}

/// <summary>1 回の実行の中立の台本（バックエンドのターンの列と、ログインの状態）</summary>
/// <param name="Turns">ターン（初回・続行の促しの順）</param>
/// <param name="LoggedIn">バックエンドにログインしているか</param>
internal sealed record MockAgentScript(IReadOnlyList<MockAgentTurn> Turns, bool LoggedIn = true)
{
    /// <summary>台本を作る</summary>
    /// <param name="turns">ターン</param>
    public static MockAgentScript Of(params MockAgentTurn[] turns) => new(turns);

    /// <summary>成功で終わる 1 ターンの台本</summary>
    public static MockAgentScript SingleSuccess { get; } =
        Of(MockAgentTurn.Of(new MockAgentTurnEnding.Success()));
}
