using QuickER.AI.Mock;

namespace QuickER.Tests.AI.Mock.Agents;

/// <summary>スクリプト化したビルド結果（成否）をキューで返すフェイクビルド検証器（API キーの実行器の中間ビルド用）</summary>
/// <remarks>実行器の網（API キーのアダプタ）と <c>ApiKeyMockProjectAgentTests</c> が共有する</remarks>
internal sealed class QueueBuildRunner : IBuildRunner
{
    /// <summary>ビルドごとに 1 つ取り出す成否（空なら成功）</summary>
    public Queue<bool> Results { get; } = new();

    /// <summary>ビルドの出力</summary>
    public string Output { get; set; } = "BUILD_OUTPUT_CS1002";

    /// <summary>ビルドが呼ばれた回数</summary>
    public int BuildCallCount { get; private set; }

    public Task<BuildRunResult> BuildAsync(
        string workingDirectory,
        CancellationToken cancellationToken = default
    )
    {
        BuildCallCount++;
        var success = Results.Count > 0 ? Results.Dequeue() : true;
        return Task.FromResult(new BuildRunResult(success, Output));
    }

    public Task<bool> IsDotnetAvailableAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(true);
}
