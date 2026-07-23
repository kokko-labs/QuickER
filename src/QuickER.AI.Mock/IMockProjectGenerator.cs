using QuickER.AI;
using QuickER.AI.Mock.Resources;
using QuickER.Model;
using QuickER.Provider;

namespace QuickER.AI.Mock;

/// <summary>WPF モックプロジェクト生成（第2ステップ）の実行結果</summary>
/// <param name="Success">全体として成功したか</param>
/// <param name="Message">結果メッセージ（成功理由・失敗理由）</param>
/// <param name="OutputDirectory">出力フォルダ（成功・失敗を問わず成果物とログが残る）</param>
/// <param name="LogPath">実行ログのパス（生成まで到達しなかった場合は null）</param>
/// <param name="Interrupted">利用者自身の中断で終了したか（true のとき VM は完了ダイアログを出さない）</param>
public sealed record MockProjectGenerationResult(
    bool Success,
    string Message,
    string OutputDirectory,
    string? LogPath,
    bool Interrupted = false
);

/// <summary>
/// 第2ステップ（モックプロジェクト生成）の生成ターゲット（プラットフォーム）。現状は WPF の 1 種のみ。
/// </summary>
/// <param name="Id">内部識別子（安定した機械可読キー）</param>
/// <param name="DisplayName">UI に出す表示名</param>
/// <remarks>
/// <para>
/// 将来 Blazor 等のターゲットを追加する場合は、この型のインスタンスを増やし、UI でメニュー化して選ばせる
/// （現状は 1 種のみのため UI にターゲット選択は出さない）。ターゲット追加はスキャフォールドの csproj／
/// README とランナーのプロンプトを分岐させる形で実現し、<see cref="IMockProjectGenerator"/> の口は変えない。
/// </para>
/// <para>
/// バックエンド拡張（Codex / API キー等）は、共有オーケストレーター <see cref="MockProjectAgentRunner"/> が
/// 受け取る <see cref="IMockProjectAgent"/> seam を別のエージェント実装へ差し替える形で実現する
/// （生成の骨格＝タイムアウト・成果物検証・独立ビルド・ログ保全は共有する）。
/// </para>
/// </remarks>
public sealed record MockProjectTarget(string Id, string DisplayName)
{
    /// <summary>WPF (.NET) ターゲット（現状の唯一のターゲット）</summary>
    public static readonly MockProjectTarget Wpf = new("wpf", "WPF (.NET)");
}

/// <summary>
/// 「確定 HTML → 決定的スキャフォールド → 選択バックエンド（Claude Code / Codex）による UI 層生成 → 最終ビルド検証」を
/// 一括で担う第2ステップの抽象。ViewModel はこの 1 つの seam に依存し、単体テストでフェイクへ差し替える。
/// </summary>
public interface IMockProjectGenerator
{
    /// <summary>この生成器の対象ターゲット（現状は常に <see cref="MockProjectTarget.Wpf"/>）</summary>
    MockProjectTarget Target { get; }

    /// <summary>指定バックエンド（Claude Code / Codex）の実行器（CLI）が利用可能か</summary>
    bool IsAgentAvailable(ErChatBackendKind backend);

    /// <summary>dotnet SDK が利用可能か（<c>dotnet --version</c> の成否で判定）</summary>
    Task<bool> IsDotnetAvailableAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// スキャフォールドを書き出し、選択バックエンド（Claude Code / Codex）をヘッドレス実行して
    /// WPF モックプロジェクトを生成する。
    /// </summary>
    /// <param name="diagram">生成元の ER 図</param>
    /// <param name="mockFolder">デザイン仕様として同梱するモックフォルダのパス（mock.json＋画面 HTML＋共有 style.css）</param>
    /// <param name="additionalInstructions">実装に対する追加指示（空／null なら付与しない）</param>
    /// <param name="outputDirectory">出力フォルダ</param>
    /// <param name="projectName">プロジェクト名</param>
    /// <param name="backend">実行バックエンド（Claude Code / Codex）</param>
    /// <param name="model">モデルエイリアス（空なら既定）</param>
    /// <param name="modelProvider">モデルプロバイダー（Codex 用。空なら既定。Claude Code は無視する）</param>
    /// <param name="onProgress">進捗テキストの逐次転送先</param>
    /// <param name="cancellationToken">キャンセルトークン</param>
    Task<MockProjectGenerationResult> GenerateAsync(
        ErDiagram diagram,
        string mockFolder,
        string? additionalInstructions,
        string outputDirectory,
        string projectName,
        ErChatBackendKind backend,
        string model,
        string modelProvider,
        Action<string> onProgress,
        CancellationToken cancellationToken = default
    );

    /// <summary>実行中の生成を中断する</summary>
    Task InterruptAsync();
}

/// <summary>
/// <see cref="MockProjectScaffoldService"/>（決定的スキャフォールド）と、バックエンド別に構築する
/// <see cref="MockProjectAgentRunner"/>（Claude Code / Codex のオーケストレーション）を束ねる
/// <see cref="IMockProjectGenerator"/> の既定実装。
/// </summary>
public sealed class MockProjectGenerator : IMockProjectGenerator
{
    private readonly MockProjectScaffoldService _scaffold;
    private readonly IBuildRunner _buildRunner;
    private readonly Func<ErChatBackendKind, IMockProjectAgent> _agentFactory;
    private readonly TimeSpan? _timeout;

    /// <summary>
    /// API キーエンジンのファクトリ（プロファイル・ツールホスト受け取り）。API キー実行器の構築に必要で、
    /// モデル・キー・エンドポイントは呼び出し側（VM）の閉包に閉じ込める（生成時点の Connection 状態で解決）。
    /// </summary>
    private readonly Func<ErChatProfile, IErDiagramToolHost, IErChatEngine>? _apiKeyEngineFactory;

    /// <summary>実行中のオーケストレーター（生成ごとにバックエンド別で構築する。中断で参照する）</summary>
    private MockProjectAgentRunner? _activeRunner;

    /// <summary>プロバイダレジストリからスキャフォールド・実行器ファクトリ・ビルド検証器を構築する（本番構成）</summary>
    /// <param name="providers">型解決に使う DB プロバイダレジストリ</param>
    /// <param name="apiKeyEngineFactory">
    /// API キー実行器が使うエンジンファクトリ（VM の apiKeyEngineFactory と同型）。省略時は API キーバックエンドが選べない
    /// （Claude Code / Codex のみ）。VM は生成時点の Connection 状態を閉じ込めた閉包を渡す。
    /// </param>
    public MockProjectGenerator(
        DatabaseProviderRegistry providers,
        Func<ErChatProfile, IErDiagramToolHost, IErChatEngine>? apiKeyEngineFactory = null
    )
    {
        _scaffold = new MockProjectScaffoldService(providers);
        _buildRunner = new DotnetBuildRunner();
        _apiKeyEngineFactory = apiKeyEngineFactory;
        // 本番はバックエンド別に実行器を構築する（API キーは _apiKeyEngineFactory と _buildRunner を要するため
        // インスタンスメソッドで解決する）。
        _agentFactory = CreateDefaultAgent;
        _timeout = null;
    }

    /// <summary>スキャフォールド・ビルド検証器・実行器ファクトリを注入して生成する（テスト用）</summary>
    /// <param name="scaffold">決定的スキャフォールド</param>
    /// <param name="buildRunner">最終ビルド検証器</param>
    /// <param name="agentFactory">バックエンド別の実行器ファクトリ</param>
    /// <param name="timeout">全体タイムアウト（省略時はランナー既定）</param>
    public MockProjectGenerator(
        MockProjectScaffoldService scaffold,
        IBuildRunner buildRunner,
        Func<ErChatBackendKind, IMockProjectAgent> agentFactory,
        TimeSpan? timeout = null
    )
    {
        _scaffold = scaffold;
        _buildRunner = buildRunner;
        _agentFactory = agentFactory;
        _apiKeyEngineFactory = null;
        _timeout = timeout;
    }

    /// <summary>本番の実行器をバックエンド別に構築する（Claude Code / Codex / API キー。既定は Claude Code）</summary>
    private IMockProjectAgent CreateDefaultAgent(ErChatBackendKind backend) =>
        backend switch
        {
            ErChatBackendKind.Codex => new CodexMockProjectAgent(new CodexAppServerClient()),
            ErChatBackendKind.ApiKey => new ApiKeyMockProjectAgent(
                _apiKeyEngineFactory
                    ?? throw new InvalidOperationException(
                        "API キーバックエンドの実行にはエンジンファクトリが必要です。"
                    ),
                _buildRunner
            ),
            _ => new ClaudeCodeMockProjectAgent(new ClaudeCodeProcessClient()),
        };

    /// <inheritdoc />
    public MockProjectTarget Target => MockProjectTarget.Wpf;

    /// <inheritdoc />
    public bool IsAgentAvailable(ErChatBackendKind backend) => _agentFactory(backend).IsAvailable();

    /// <inheritdoc />
    public Task<bool> IsDotnetAvailableAsync(CancellationToken cancellationToken = default) =>
        _buildRunner.IsDotnetAvailableAsync(cancellationToken);

    /// <inheritdoc />
    public async Task<MockProjectGenerationResult> GenerateAsync(
        ErDiagram diagram,
        string mockFolder,
        string? additionalInstructions,
        string outputDirectory,
        string projectName,
        ErChatBackendKind backend,
        string model,
        string modelProvider,
        Action<string> onProgress,
        CancellationToken cancellationToken = default
    )
    {
        // 1) 決定的スキャフォールド（データ層コード＋csproj＋README＋design/mock/ 同梱）を書き出す
        try
        {
            _scaffold.Scaffold(diagram, outputDirectory, projectName, mockFolder);
        }
        catch (Exception ex)
        {
            return new MockProjectGenerationResult(
                Success: false,
                Message: string.Format(Strings.Mock_ScaffoldFailedFormat, ex.Message),
                OutputDirectory: outputDirectory,
                LogPath: null
            );
        }

        // 2) 選択バックエンドの実行器で 1 回分のオーケストレーションを行い、最終ビルドを検証する
        var runner = new MockProjectAgentRunner(_agentFactory(backend), _buildRunner, _timeout);
        _activeRunner = runner;

        try
        {
            var result = await runner
                .RunAsync(
                    outputDirectory,
                    projectName,
                    additionalInstructions,
                    model,
                    modelProvider,
                    onProgress,
                    cancellationToken
                )
                .ConfigureAwait(false);

            return new MockProjectGenerationResult(
                Success: result.Success,
                Message: result.Message,
                OutputDirectory: outputDirectory,
                LogPath: result.LogPath,
                // ユーザー自身の中断（タイムアウトは含めない）を VM へ伝える
                Interrupted: result.Canceled
            );
        }
        finally
        {
            _activeRunner = null;
        }
    }

    /// <inheritdoc />
    public Task InterruptAsync() => _activeRunner?.InterruptAsync() ?? Task.CompletedTask;
}
