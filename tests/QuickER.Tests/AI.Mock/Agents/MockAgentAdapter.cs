using QuickER.AI.Mock;
using QuickER.Tests.TestSupport.Engines;

namespace QuickER.Tests.AI.Mock.Agents;

/// <summary>実行器が持つか持たないかを、アダプタが出典つきで宣言する性質</summary>
/// <remarks>
/// 持たない性質を前提にする行は、その実行器では流さない（網羅しない組の一覧の Accepted に、アダプタの出典を理由として載る）。
/// 持たない宣言は製品の事実だけに使う（実行器を直せば埋まる組を持たない宣言にしない。そういう組は Pending で一覧へ書く）。
/// </remarks>
internal enum MockAgentCapability
{
    /// <summary>未ログインを見分けて <c>NotLoggedIn</c> の失敗で返す</summary>
    DetectsNotLoggedIn,

    /// <summary>セッション（スレッド）を張る前に認証を確かめ、未ログインならセッションを張らない</summary>
    ProbesLoginBeforeSession,

    /// <summary>バックエンドのツールの活動（コマンドの実行・ファイルの変更）を進捗へ流す</summary>
    ForwardsToolActivityToProgress,

    /// <summary>許可の範囲の外の要求を断ったことを進捗へ流す</summary>
    ReportsDeclinedPermission,

    /// <summary>実行中のトークンの取り消しで、バックエンドへ中断を送る</summary>
    TokenCancelCallsBackendInterrupt,

    /// <summary><c>InterruptAsync</c> の後、バックエンドの中断の完了の通知で取り消しとして終わる</summary>
    InterruptCompletesViaEvent,

    /// <summary>実行の前の <c>InterruptAsync</c> もバックエンドへ届く</summary>
    InterruptReachesBackendWithoutRun,

    /// <summary>成功で終わったのに UI のファイルが無ければ、同じセッションへ続行を 1 回だけ促す</summary>
    SendsContinuationNudge,

    /// <summary>ターンの完了を待つ間の接続断を、失敗としてすぐに知らせる</summary>
    ReportsDisconnect,

    /// <summary>1 回の実行の終わりにバックエンド（クライアント・エンジン）を破棄する</summary>
    DisposesBackendPerRun,

    /// <summary>システムプロンプトと初回のプロンプトを、エージェント型の共有の組み立て（<c>MockProjectPromptBuilder.BuildPrompt</c>）で作る</summary>
    SharesAgentPromptBuilder,

    /// <summary>実装のファイルを提出のツール（<c>emit_file</c>）で受け取る</summary>
    SubmitsFilesThroughTool,
}

/// <summary>性質の宣言（持つか・出典）</summary>
/// <param name="Has">持つか</param>
/// <param name="Evidence">出典（製品のコードの <c>src/…:行</c> と説明）</param>
internal sealed record MockAgentDeclaration(bool Has, string Evidence);

/// <summary>値の宣言（値・出典）</summary>
/// <typeparam name="T">値の型</typeparam>
/// <param name="Value">値</param>
/// <param name="Evidence">出典（製品のコードの <c>src/…:行</c> と説明）</param>
internal sealed record MockAgentValue<T>(T Value, string Evidence);

/// <summary>実行器が使う起動の設定（実行器に依らない形）</summary>
/// <param name="WorkingDirectory">バックエンドへ渡した作業フォルダ（渡さなければ null）</param>
/// <param name="Model">バックエンドへ渡したモデル（渡さなければ null）</param>
/// <param name="SystemPrompt">システムプロンプト（相当のもの）</param>
/// <param name="Settings">実行器に固有の設定を <c>名前=値</c> で <c>;</c> 区切りに並べたもの</param>
internal sealed record MockAgentLaunch(
    string? WorkingDirectory,
    string? Model,
    string? SystemPrompt,
    string Settings
);

/// <summary>生成のターゲット（行の第 2 の次元。元のテストがターゲットごとに見ているものだけが使う）</summary>
/// <param name="Name">名前（行の Id の末尾）</param>
/// <param name="Profile">ターゲットのプロファイル</param>
/// <param name="UiFile">プロジェクトフォルダに置く UI のファイルの相対パス</param>
/// <param name="UiContent">UI のファイルの内容</param>
internal sealed record MockAgentTarget(
    string Name,
    MockProjectTargetProfile Profile,
    string UiFile,
    string UiContent
)
{
    /// <summary>WPF</summary>
    public static MockAgentTarget Wpf { get; } =
        new("wpf", MockProjectTargetProfile.Wpf, "App.xaml", "<Application/>");

    /// <summary>Blazor</summary>
    public static MockAgentTarget Blazor { get; } =
        new("blazor", MockProjectTargetProfile.Blazor, "Home.razor", "@page \"/\"");

    /// <summary>両方</summary>
    public static IReadOnlyList<MockAgentTarget> All { get; } = [Wpf, Blazor];
}

/// <summary>実行器の軸の値ごとのアダプタ（実の実行器＋既存のテストダブルの組を作る）</summary>
/// <remarks>
/// 実行器ごとの違いは、このクラスの抽象メンバー（性質と値の宣言・期待の文と設定の組み方・組の作り方）に置く。
/// アダプタの中で軸の値を名指しして分岐しない（値を足したときに黙って既定の枝へ落ちる）。
/// 期待の文は resx から、流す時点の UI のカルチャで組む。
/// </remarks>
internal abstract class MockAgentAdapter : IMatrixAdapter
{
    /// <summary>軸の値（実行器の型の名前）</summary>
    public string AxisValue => AgentType.Name;

    /// <summary>受け持つ実行器の型</summary>
    public abstract Type AgentType { get; }

    /// <summary>性質の宣言（<see cref="MockAgentCapability"/> の全値を持つ）</summary>
    public abstract IReadOnlyDictionary<
        MockAgentCapability,
        MockAgentDeclaration
    > Declarations { get; }

    /// <summary>実行ログと生成画面のタブに出す名前の宣言</summary>
    public abstract MockAgentValue<string> BackendName { get; }

    /// <summary>最終ビルドがこの実行器にとって境界越えになるかの宣言</summary>
    public abstract MockAgentValue<bool> FinalBuildEscapesSandbox { get; }

    /// <summary>性質を持つか</summary>
    /// <param name="capability">性質</param>
    public bool Has(MockAgentCapability capability) => Declarations[capability].Has;

    /// <summary>起動の設定の期待値</summary>
    /// <param name="request">流した要求</param>
    public abstract MockAgentLaunch ExpectedLaunch(MockProjectAgentRequest request);

    /// <summary>バックエンドが理由つきの失敗を報告したときの <c>Error</c> の期待値</summary>
    /// <param name="detail">報告された理由</param>
    public abstract string ExpectedReportedFailure(string detail);

    /// <summary>バックエンドの可用性を <paramref name="backendAvailable"/> にしたときの <c>IsAvailable</c> の期待値（決められなければ null）</summary>
    /// <param name="backendAvailable">組のバックエンドの可用性</param>
    public abstract bool? ExpectedAvailability(bool backendAvailable);

    /// <summary>未ログインの <c>Error</c> の期待値</summary>
    /// <remarks><see cref="MockAgentCapability.DetectsNotLoggedIn"/> を持つアダプタだけが呼ばれる</remarks>
    public virtual string ExpectedNotLoggedInError =>
        throw new NotSupportedException("この実行器は未ログインを見分けない");

    /// <summary>接続断の <c>Error</c> の期待値</summary>
    /// <remarks><see cref="MockAgentCapability.ReportsDisconnect"/> を持つアダプタだけが呼ばれる</remarks>
    public virtual string ExpectedDisconnectError =>
        throw new NotSupportedException("この実行器は接続断を知らせない");

    /// <summary>ツールの活動の 1 手が進捗に出す名前の期待値</summary>
    /// <remarks><see cref="MockAgentCapability.ForwardsToolActivityToProgress"/> を持つアダプタだけが呼ばれる</remarks>
    public virtual string ExpectedToolActivityText =>
        throw new NotSupportedException("この実行器はツールの活動を進捗へ流さない");

    /// <summary>実行中の中断がバックエンドへ渡す宛先の期待値（宛先を運ばなければ null）</summary>
    /// <remarks><see cref="MockAgentCapability.InterruptCompletesViaEvent"/> を持つアダプタだけが呼ばれる</remarks>
    public virtual string? ExpectedInterruptTarget => null;

    /// <summary>組を作る</summary>
    /// <param name="options">組の前提（ターゲット・UI のファイルの有無・追加の指示）</param>
    public abstract MockAgentRig CreateRig(MockAgentRigOptions options);
}

/// <summary>実行器の軸（正本＝QuickER.AI.Mock の <see cref="IMockProjectAgent"/> の具象の型の名前。除外なし）</summary>
internal static class MockAgentAxis
{
    /// <summary>軸の名前</summary>
    public const string Name = "MockProjectAgent";

    /// <summary>正本の値（実装の型の名前・序数の昇順）</summary>
    public static IReadOnlyList<string> Authoritative { get; } =
        typeof(IMockProjectAgent)
            .Assembly.GetTypes()
            .Where(type =>
                type.IsClass && !type.IsAbstract && typeof(IMockProjectAgent).IsAssignableFrom(type)
            )
            .Select(type => type.Name)
            .Order(StringComparer.Ordinal)
            .ToList();

    /// <summary>軸</summary>
    public static MatrixAxis<MockAgentAdapter> Axis { get; } =
        new(
            Name,
            Authoritative,
            [
                new ApiKeyMockAgentAdapter(),
                new ClaudeCodeMockAgentAdapter(),
                new CodexMockAgentAdapter(),
                new CopilotMockAgentAdapter(),
            ]
        );
}
