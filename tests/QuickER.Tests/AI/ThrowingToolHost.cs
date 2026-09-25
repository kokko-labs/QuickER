using QuickER.AI;

namespace QuickER.Tests.AI;

/// <summary>
/// 実行のたびに常に例外を投げる <see cref="IErDiagramToolHost"/> のフェイクホスト。
/// 3 エンジン（Codex／Copilot／Claude Code）のツール実行例外経路（失敗のツール結果として返送する）を
/// 同じ形で検証するために使う。
/// </summary>
internal sealed class ThrowingToolHost : IErDiagramToolHost
{
    /// <summary>Execute が投げる例外</summary>
    public Exception Exception { get; set; } = new InvalidOperationException("boom");

    public (string Result, bool Success) Execute(string toolName, string argumentsJson) =>
        throw Exception;
}
