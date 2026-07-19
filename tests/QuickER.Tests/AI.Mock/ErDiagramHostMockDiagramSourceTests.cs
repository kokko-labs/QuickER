using FluentAssertions;
using QuickER.AI.Mock;
using QuickER.Model;
using QuickER.Provider;
using QuickER.Tests.TestDoubles;

namespace QuickER.Tests.AI.Mock;

/// <summary>
/// <see cref="ErDiagramHostMockDiagramSource"/> が契約 <see cref="QuickER.Extensibility.IErDiagramHost"/> を
/// モック固有インターフェースへ正しく委譲することを検証するテストクラス。
/// </summary>
public class ErDiagramHostMockDiagramSourceTests
{
    /// <summary>IsEmpty がホストの値をそのまま返すことを検証する</summary>
    [Fact(DisplayName = "IsEmpty はホストへ委譲する")]
    public void IsEmpty_DelegatesToHost()
    {
        var host = new StubErDiagramHost { IsEmptyToReturn = true };
        var source = new ErDiagramHostMockDiagramSource(host);

        source.IsEmpty.Should().BeTrue();
    }

    /// <summary>GetDiagram がホストの返すダイアグラムをそのまま返すことを検証する</summary>
    [Fact(DisplayName = "GetDiagram はホストへ委譲する")]
    public void GetDiagram_DelegatesToHost()
    {
        var diagram = new ErDiagram();
        var host = new StubErDiagramHost { DiagramToReturn = diagram };
        var source = new ErDiagramHostMockDiagramSource(host);

        source.GetDiagram().Should().BeSameAs(diagram);
    }

    /// <summary>Providers がホストの返すレジストリをそのまま返すことを検証する</summary>
    [Fact(DisplayName = "Providers はホストへ委譲する")]
    public void Providers_DelegatesToHost()
    {
        var registry = new DatabaseProviderRegistry(Array.Empty<IDatabaseProvider>());
        var host = new StubErDiagramHost { ProvidersToReturn = registry };
        var source = new ErDiagramHostMockDiagramSource(host);

        source.Providers.Should().BeSameAs(registry);
    }
}
