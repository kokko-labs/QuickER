using QuickER.AI;
using QuickER.Tests.TestSupport.Engines;

namespace QuickER.Tests.AI.ChatEngines;

/// <summary>エンジンが持つか持たないかを、アダプタが出典つきで宣言する性質</summary>
/// <remarks>
/// 持たない性質を前提にする行は、そのエンジンでは流さない（網羅しない組の一覧の Accepted に、アダプタの出典を理由として載る）。
/// 持たない宣言は製品の事実だけに使う（エンジンを直せば埋まる組を持たない宣言にしない）。
/// </remarks>
internal enum ChatEngineCapability
{
    /// <summary>ターンの完了がバックエンドの別の通知で届く（送信が戻った後に完了する。ターンの外の完了の通知があり得る）</summary>
    AsyncCompletion,

    /// <summary>ターンの実行が送信の中で走る（送信が戻るまでにターンが終わる）</summary>
    TurnRunsInsideSend,

    /// <summary>ツールの結果をバックエンドへ別の送信で返す（送信が失敗し得る）</summary>
    ToolResponseChannel,

    /// <summary>ツールホストを渡さずに作れる</summary>
    OptionalToolHost,

    /// <summary>バックエンドがターンの失敗を理由つきで報告する（例外でない失敗の通知）</summary>
    ReportedFailure,

    /// <summary>バックエンドが受け取るツールの結果が成否を運ぶ</summary>
    ToolReturnCarriesSuccess,

    /// <summary>CLI を見つけて接続する（見つからない・接続の再確認がある）</summary>
    CliLifecycle,

    /// <summary>CLI が見つからない印を公開する</summary>
    ExposesCliMissing,

    /// <summary>常駐プロセスを起動して接続する（起動の失敗・重ねた接続・やり直しがある）</summary>
    LaunchesRuntime,

    /// <summary>ターンの外でバックエンドとの接続が切れ得る（次の送信で会話を開き直す）</summary>
    ConnectionCanDrop,

    /// <summary>破棄するクライアントを持つ</summary>
    OwnsBackend,

    /// <summary>作業フォルダ（<c>%TEMP%\QuickER\{backend}\&lt;guid&gt;</c>）を作る</summary>
    CreatesWorkingDirectory,

    /// <summary>添付を受け付ける種類がある</summary>
    AcceptsAttachments,

    /// <summary>受け付けない添付の種類がある</summary>
    RefusesSomeAttachmentKind,

    /// <summary>受け付けない添付を、例外でなくターンの失敗で知らせる</summary>
    RefusesAttachmentAsTurnFailure,
}

/// <summary>性質の宣言（持つか・出典）</summary>
/// <param name="Has">持つか</param>
/// <param name="Evidence">出典（docs・CLAUDE.md・製品のコードのコメントのどれか。ファイルと箇所）</param>
internal sealed record ChatEngineDeclaration(bool Has, string Evidence);

/// <summary>チャットエンジンの軸の値ごとのアダプタ（実エンジン＋既存のテストダブルの組を作る）</summary>
/// <remarks>
/// エンジンごとの違いは、このクラスの抽象メンバー（性質の宣言・期待の文の組み方・組の作り方）に置く。
/// アダプタの中で軸の値を名指しして分岐しない（値を足したときに黙って既定の枝へ落ちる）。
/// 期待の文は resx から、流す時点の UI のカルチャで組む（組む時点と流す時点のカルチャを揃える）。
/// </remarks>
internal abstract class ChatEngineAdapter : IMatrixAdapter
{
    /// <summary>軸の値（<see cref="ErChatBackendKind"/> の名前）</summary>
    public string AxisValue => Kind.ToString();

    /// <summary>受け持つバックエンドの種類</summary>
    public abstract ErChatBackendKind Kind { get; }

    /// <summary>性質の宣言（<see cref="ChatEngineCapability"/> の全値を持つ）</summary>
    public abstract IReadOnlyDictionary<
        ChatEngineCapability,
        ChatEngineDeclaration
    > Declarations { get; }

    /// <summary>性質を持つか</summary>
    /// <param name="capability">性質</param>
    public bool Has(ChatEngineCapability capability) => Declarations[capability].Has;

    /// <summary>エンジンが公開する添付の範囲の期待値</summary>
    public abstract AttachmentSupport ExpectedAttachmentSupport { get; }

    /// <summary>送信が例外で終わったときの失敗の文の期待値</summary>
    /// <param name="exception">送信が投げた例外</param>
    public abstract string ExpectedFailureFromException(Exception exception);

    /// <summary>バックエンドが理由つきの失敗を報告したときの失敗の文の期待値</summary>
    /// <remarks><see cref="ChatEngineCapability.ReportedFailure"/> を持つアダプタだけが呼ばれる</remarks>
    /// <param name="detail">報告された理由</param>
    public virtual string ExpectedReportedFailure(string? detail) =>
        throw new NotSupportedException("このバックエンドは失敗を理由つきで報告しない");

    /// <summary>ツールの結果の送信に失敗したときの状態の文の期待値</summary>
    /// <remarks><see cref="ChatEngineCapability.ToolResponseChannel"/> を持つアダプタだけが呼ばれる</remarks>
    /// <param name="reason">送信の失敗の理由</param>
    public virtual string ExpectedToolResponseSendFailed(string reason) =>
        throw new NotSupportedException("このバックエンドはツールの結果を別の送信で返さない");

    /// <summary>CLI が見つからないときの状態の概要の文の期待値</summary>
    /// <remarks><see cref="ChatEngineCapability.CliLifecycle"/> を持つアダプタだけが呼ばれる</remarks>
    public virtual string ExpectedCliMissingSummary =>
        throw new NotSupportedException("このエンジンは CLI の接続を持たない");

    /// <summary>CLI が見つからないときの案内の文の期待値</summary>
    /// <remarks><see cref="ChatEngineCapability.CliLifecycle"/> を持つアダプタだけが呼ばれる</remarks>
    public virtual string ExpectedInstallGuidance =>
        throw new NotSupportedException("このエンジンは CLI の接続を持たない");

    /// <summary>組を作る</summary>
    /// <param name="hostKind">ツールホストの種類</param>
    public abstract ChatEngineRig CreateRig(ChatToolHostKind hostKind);

    /// <summary>要約に「詳細: 理由」の行を添えた失敗の文（理由が空なら要約だけ）</summary>
    /// <remarks>製品の <c>AiErrorMessageLocalizer.WithDetail</c> を呼ばずに、元のテストと同じ組み方で独立に組む</remarks>
    /// <param name="summary">要約（resx）</param>
    /// <param name="detail">理由</param>
    protected static string SummaryWithDetail(string summary, string? detail) =>
        string.IsNullOrEmpty(detail)
            ? summary
            : summary + Environment.NewLine + QuickER.AI.Resources.Strings.Ai_DetailLabel + detail;
}

/// <summary>UI スレッドへのマーシャリングを同期実行で代替するディスパッチャ</summary>
internal sealed class ChatSyncDispatcher : IUiDispatcher
{
    public T Invoke<T>(Func<T> func) => func();
}

/// <summary>チャットエンジンの軸（正本＝製品の <see cref="ErChatBackendKind"/> の名前。除外なし）</summary>
internal static class ChatEngineAxis
{
    /// <summary>軸の名前</summary>
    public const string Name = "ChatEngine";

    /// <summary>軸</summary>
    public static MatrixAxis<ChatEngineAdapter> Axis { get; } =
        new(
            Name,
            Enum.GetNames<ErChatBackendKind>(),
            [
                new ApiKeyChatEngineAdapter(),
                new CodexChatEngineAdapter(),
                new ClaudeCodeChatEngineAdapter(),
                new CopilotChatEngineAdapter(),
            ]
        );
}
