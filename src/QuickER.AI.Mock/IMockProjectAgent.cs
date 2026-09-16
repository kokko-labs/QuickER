namespace QuickER.AI.Mock;

/// <summary>モックプロジェクトの UI 層生成をエージェント（バックエンド）へ依頼する要求</summary>
/// <param name="WorkingDirectory">スキャフォールド済みの出力フォルダ（cwd になる）</param>
/// <param name="ProjectName">プロジェクト名（プロンプトの案内に使う）</param>
/// <param name="AdditionalInstructions">実装に対する追加指示（空／null なら付与しない）</param>
/// <param name="Model">モデルエイリアス（空なら既定）</param>
/// <param name="Profile">生成ターゲットのプロファイル（プロンプト文面・UI 成果物の検索パターンの正本）</param>
/// <param name="ModelProvider">モデルプロバイダー（Codex 用。空なら既定。Claude Code バックエンドは無視する）</param>
public sealed record MockProjectAgentRequest(
    string WorkingDirectory,
    string ProjectName,
    string? AdditionalInstructions,
    string Model,
    MockProjectTargetProfile Profile,
    string ModelProvider = ""
);

/// <summary>エージェント（バックエンド）の 1 実行結果（クライアントの自己申告）</summary>
/// <param name="Success">エージェントが成功で完了したと自己申告したか</param>
/// <param name="Error">失敗時のメッセージ（成功時は null）</param>
/// <param name="NotLoggedIn">未ログインが原因の失敗か（バックエンドが区別できる場合）</param>
/// <remarks>
/// これはあくまでエージェントの自己申告であり、最終判定は共有オーケストレーター側の
/// 成果物検証・独立ビルド（<see cref="IBuildRunner"/>）が行う。
/// </remarks>
public sealed record MockProjectAgentOutcome(bool Success, string? Error, bool NotLoggedIn);

/// <summary>
/// スキャフォールド済みフォルダに対して「WPF の UI 層を書く」部分だけを担うエージェント（バックエンド）の抽象。
/// </summary>
/// <remarks>
/// <para>
/// バックエンドごとの差異（Claude Code CLI / Codex / API キー等）はこの抽象の実装で吸収する。全体タイムアウト・
/// 成果物検証・独立ビルド・ログ保全・結果メッセージ生成は共有オーケストレーター（<see cref="MockProjectAgentRunner"/>）
/// がバックエンド非依存に担うため、ここでは扱わない。
/// </para>
/// <para>
/// 進捗テキストは <c>onProgress</c> で逐次転送する。キャンセル（全体タイムアウト・明示的な中断）は
/// <c>cancellationToken</c> で伝わり、キャンセル時は <see cref="OperationCanceledException"/> の伝播を許す
/// （タイムアウトと中断の区別はオーケストレーター側が行う）。
/// </para>
/// </remarks>
public interface IMockProjectAgent
{
    /// <summary>このエージェント（バックエンド）が利用可能か</summary>
    bool IsAvailable();

    /// <summary>
    /// 最終ビルドをサンドボックスの外で実行することが、この実行器にとって新しい境界越えになるか。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 最終ビルドは QuickER がユーザー権限で（実行器のサンドボックスの外で）実行する。実行器が自分の
    /// サンドボックス内で csproj などのビルド設定を書ける場合、そのビルドは実行器に与えられていない
    /// 権限でその内容を実行することになる＝ここが唯一の境界越えの地点なので、true を宣言した実行器の
    /// ランでは <see cref="MockProjectAgentRunner"/> がビルド直前に変更を検知して利用者へ確認する。
    /// </para>
    /// <para>
    /// false になるのは次の 2 通り。(a) 実行器自身が任意のコマンドを実行できる（Claude Code の
    /// 無制限 <c>Bash</c>）＝最終ビルドで新しく越える境界が無い。(b) 実行器がそもそもビルド設定を
    /// 書けない（API キー方式の拡張子ホワイトリスト）。
    /// </para>
    /// <para>
    /// バックエンド名で分岐させないための宣言であり、ランナーはこの値だけを見る。
    /// </para>
    /// </remarks>
    bool FinalBuildEscapesSandbox { get; }

    /// <summary>スキャフォールド済みフォルダに対して UI 層を生成させる</summary>
    /// <param name="request">生成要求（出力フォルダ・プロジェクト名・追加指示・モデル）</param>
    /// <param name="onProgress">進捗テキストの逐次転送先</param>
    /// <param name="cancellationToken">キャンセルトークン（全体タイムアウト・中断で発火）</param>
    Task<MockProjectAgentOutcome> RunAsync(
        MockProjectAgentRequest request,
        Action<string> onProgress,
        CancellationToken cancellationToken
    );

    /// <summary>実行中のターンを中断する</summary>
    Task InterruptAsync();
}
