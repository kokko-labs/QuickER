using QuickER.Extensibility;
using QuickER.Model;
using QuickER.Provider;

namespace QuickER.AI.Mock;

/// <summary>
/// 契約 <see cref="IErDiagramHost"/> を、モック生成 ViewModel が要求する <see cref="IMockDiagramSource"/> へ適合させるアダプタ。
/// </summary>
/// <remarks>
/// フィーチャーモジュール（QuickER.AI.Mock）側に置く「契約 → モック固有インターフェース」の橋渡し。
/// <see cref="IsEmpty"/> / <see cref="GetDiagram"/> / <see cref="Providers"/> をホストへ委譲する。
/// </remarks>
public sealed class ErDiagramHostMockDiagramSource : IMockDiagramSource
{
    private readonly IErDiagramHost _host;

    /// <summary>橋渡し対象の <see cref="IErDiagramHost"/> を指定して生成する</summary>
    public ErDiagramHostMockDiagramSource(IErDiagramHost host)
    {
        _host = host;
    }

    /// <inheritdoc />
    public bool IsEmpty => _host.IsEmpty;

    /// <inheritdoc />
    public ErDiagram GetDiagram() => _host.GetDiagram();

    /// <inheritdoc />
    public DatabaseProviderRegistry Providers => _host.Providers;
}
