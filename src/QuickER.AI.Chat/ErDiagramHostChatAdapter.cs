using QuickER.AI;
using QuickER.Extensibility;

namespace QuickER.AI.Chat;

/// <summary>
/// 契約 <see cref="IErDiagramHost"/> を、AI チャット ViewModel が要求する <see cref="IErDiagramChatHost"/> へ適合させるアダプタ。
/// </summary>
/// <remarks>
/// フィーチャーモジュール（QuickER.AI.Chat）側に置く「契約 → チャット固有インターフェース」の橋渡し。
/// <see cref="IsEmpty"/> / <see cref="AutoArrangeNewDiagram"/> はホストへ委譲し、
/// AI ツール実行は内包する <see cref="IErDiagramToolHost"/> 実装（<see cref="ToolHostAdapter"/>）が
/// <see cref="IErDiagramHost.ExecuteTool"/> へ単純委譲する。
/// </remarks>
public sealed class ErDiagramHostChatAdapter : IErDiagramChatHost
{
    private readonly IErDiagramHost _host;
    private readonly ToolHostAdapter _toolHost;

    /// <summary>橋渡し対象の <see cref="IErDiagramHost"/> を指定して生成する</summary>
    public ErDiagramHostChatAdapter(IErDiagramHost host)
    {
        _host = host;
        _toolHost = new ToolHostAdapter(host);
    }

    /// <inheritdoc />
    public IErDiagramToolHost ToolHost => _toolHost;

    /// <inheritdoc />
    public bool IsEmpty => _host.IsEmpty;

    /// <inheritdoc />
    public void AutoArrangeNewDiagram() => _host.AutoArrangeNewDiagram();

    /// <summary>
    /// AI エンジン（QuickER.AI）の <see cref="IErDiagramToolHost"/> を、契約 <see cref="IErDiagramHost.ExecuteTool"/> へ橋渡しする実装。
    /// </summary>
    private sealed class ToolHostAdapter : IErDiagramToolHost
    {
        private readonly IErDiagramHost _host;

        public ToolHostAdapter(IErDiagramHost host)
        {
            _host = host;
        }

        /// <inheritdoc />
        public (string Result, bool Success) Execute(string toolName, string argumentsJson) =>
            _host.ExecuteTool(toolName, argumentsJson);
    }
}
