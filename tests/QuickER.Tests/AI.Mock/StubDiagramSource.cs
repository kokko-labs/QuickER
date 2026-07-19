using QuickER.AI.Mock;
using QuickER.Model;
using QuickER.Provider;
using QuickER.SqlServer;

namespace QuickER.Tests.AI.Mock;

/// <summary>
/// 現在の ER 図を供給する <see cref="IMockDiagramSource"/> のスタブ。
/// 与えた図をそのまま返し、空判定はエンティティ数で切り替わる。
/// </summary>
internal sealed class StubDiagramSource : IMockDiagramSource
{
    private readonly ErDiagram _diagram;

    public StubDiagramSource(ErDiagram diagram) => _diagram = diagram;

    public bool IsEmpty => _diagram.Entities.Count == 0;

    public ErDiagram GetDiagram() => _diagram;

    public DatabaseProviderRegistry Providers { get; } = new([new SqlServerProvider()]);
}
