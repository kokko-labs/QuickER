using QuickER.AI;

namespace QuickER.Tests.AI;

/// <summary>
/// 破棄の回数だけを記録する最小の <see cref="IClaudeCodeClient"/> フェイク。
/// アプリ終了時にエンジンの破棄が届くこと（＝常駐プロセスの停止経路）の検証に使う。
/// </summary>
/// <remarks>
/// ターンの流れを検証するフェイク（<c>ClaudeCodeChatEngineTests</c> のもの）とは目的が異なるため分けている。
/// </remarks>
internal sealed class DisposeTrackingClaudeCodeClient : IClaudeCodeClient
{
    /// <summary>claude CLI を検出できたことにするか</summary>
    public bool Available { get; set; } = true;

    /// <summary>DisposeAsync が呼ばれた回数</summary>
    public int DisposeCount { get; private set; }

    public bool IsAvailable() => Available;

    public Task<ClaudeCodeTurnOutcome> RunTurnAsync(
        string prompt,
        string? resumeSessionId,
        ClaudeCodeLaunchOptions options,
        Action<string> onAssistantText,
        CancellationToken cancellationToken
    ) => Task.FromResult(new ClaudeCodeTurnOutcome(true, null, null, false));

    public Task<ClaudeLoginProbeResult> ProbeLoginAsync(CancellationToken cancellationToken) =>
        Task.FromResult(ClaudeLoginProbeResult.LoggedIn);

    public void Interrupt() { }

    public ValueTask DisposeAsync()
    {
        DisposeCount++;
        return ValueTask.CompletedTask;
    }
}
