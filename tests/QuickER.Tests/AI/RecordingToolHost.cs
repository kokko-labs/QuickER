using QuickER.AI;

namespace QuickER.Tests.AI;

/// <summary>ツール呼び出しを記録し、定型結果を返す <see cref="IErDiagramToolHost"/> のフェイクホスト</summary>
internal sealed class RecordingToolHost : IErDiagramToolHost
{
    /// <summary>実行された (ツール名, 引数 JSON) の記録</summary>
    public List<(string Tool, string Args)> Calls { get; } = new();

    public (string Result, bool Success) Execute(string toolName, string argumentsJson)
    {
        Calls.Add((toolName, argumentsJson));
        return ($"{toolName} 実行済み", true);
    }
}
