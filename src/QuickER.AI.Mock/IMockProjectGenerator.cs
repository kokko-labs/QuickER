using QuickER.AI;
using QuickER.Model;
using QuickER.Provider;

namespace QuickER.AI.Mock;

/// <summary>WPF モックプロジェクト生成（第2ステップ）の実行結果</summary>
/// <param name="Success">全体として成功したか</param>
/// <param name="Message">結果メッセージ（成功理由・失敗理由）</param>
/// <param name="OutputDirectory">出力フォルダ（成功・失敗を問わず成果物とログが残る）</param>
/// <param name="LogPath">実行ログのパス（生成まで到達しなかった場合は null）</param>
public sealed record MockProjectGenerationResult(
    bool Success,
    string Message,
    string OutputDirectory,
    string? LogPath
);

/// <summary>
/// 「確定 HTML → 決定的スキャフォールド → Claude Code による UI 層生成 → 最終ビルド検証」を
/// 一括で担う第2ステップの抽象。ViewModel はこの 1 つの seam に依存し、単体テストでフェイクへ差し替える。
/// </summary>
public interface IMockProjectGenerator
{
    /// <summary>claude CLI が利用可能か</summary>
    bool IsClaudeAvailable();

    /// <summary>dotnet SDK が利用可能か（<c>dotnet --version</c> の成否で判定）</summary>
    Task<bool> IsDotnetAvailableAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// スキャフォールドを書き出し、Claude Code をヘッドレス実行して WPF モックプロジェクトを生成する。
    /// </summary>
    /// <param name="diagram">生成元の ER 図</param>
    /// <param name="designHtml">デザイン仕様として同梱する確定 HTML</param>
    /// <param name="outputDirectory">出力フォルダ</param>
    /// <param name="projectName">プロジェクト名</param>
    /// <param name="model">Claude Code モデルエイリアス（空なら既定）</param>
    /// <param name="onProgress">進捗テキストの逐次転送先</param>
    /// <param name="cancellationToken">キャンセルトークン</param>
    Task<MockProjectGenerationResult> GenerateAsync(
        ErDiagram diagram,
        string designHtml,
        string outputDirectory,
        string projectName,
        string model,
        Action<string> onProgress,
        CancellationToken cancellationToken = default
    );

    /// <summary>実行中の生成を中断する</summary>
    Task InterruptAsync();
}

/// <summary>
/// <see cref="MockProjectScaffoldService"/>（決定的スキャフォールド）と <see cref="MockProjectAgentRunner"/>
/// （Claude Code オーケストレーション）を束ねる <see cref="IMockProjectGenerator"/> の既定実装。
/// </summary>
public sealed class MockProjectGenerator : IMockProjectGenerator
{
    private readonly MockProjectScaffoldService _scaffold;
    private readonly MockProjectAgentRunner _runner;

    /// <summary>プロバイダレジストリからスキャフォールド・ランナーを構築する（本番構成）</summary>
    /// <param name="providers">型解決に使う DB プロバイダレジストリ</param>
    public MockProjectGenerator(DatabaseProviderRegistry providers)
        : this(
            new MockProjectScaffoldService(providers),
            new MockProjectAgentRunner(new ClaudeCodeProcessClient(), new DotnetBuildRunner())
        ) { }

    /// <summary>スキャフォールド・ランナーを注入して生成する（テスト用）</summary>
    public MockProjectGenerator(MockProjectScaffoldService scaffold, MockProjectAgentRunner runner)
    {
        _scaffold = scaffold;
        _runner = runner;
    }

    /// <inheritdoc />
    public bool IsClaudeAvailable() => _runner.IsClaudeAvailable();

    /// <inheritdoc />
    public Task<bool> IsDotnetAvailableAsync(CancellationToken cancellationToken = default) =>
        _runner.IsDotnetAvailableAsync(cancellationToken);

    /// <inheritdoc />
    public async Task<MockProjectGenerationResult> GenerateAsync(
        ErDiagram diagram,
        string designHtml,
        string outputDirectory,
        string projectName,
        string model,
        Action<string> onProgress,
        CancellationToken cancellationToken = default
    )
    {
        // 1) 決定的スキャフォールド（データ層コード＋csproj＋README＋design/mock.html）を書き出す
        try
        {
            _scaffold.Scaffold(diagram, outputDirectory, projectName, designHtml);
        }
        catch (Exception ex)
        {
            return new MockProjectGenerationResult(
                Success: false,
                Message: $"スキャフォールドの生成に失敗しました: {ex.Message}",
                OutputDirectory: outputDirectory,
                LogPath: null
            );
        }

        // 2) Claude Code をヘッドレス実行して UI 層を生成させ、最終ビルドを検証する
        var result = await _runner
            .RunAsync(outputDirectory, projectName, model, onProgress, cancellationToken)
            .ConfigureAwait(false);

        return new MockProjectGenerationResult(
            Success: result.Success,
            Message: result.Message,
            OutputDirectory: outputDirectory,
            LogPath: result.LogPath
        );
    }

    /// <inheritdoc />
    public Task InterruptAsync() => _runner.InterruptAsync();
}
