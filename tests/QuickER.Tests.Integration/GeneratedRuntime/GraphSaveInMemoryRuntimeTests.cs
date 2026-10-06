using System.Threading.Tasks;
using QuickER.Tests.GeneratedQueryFixture;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>グラフ保存のランタイムスイートを<b>インメモリ Repository</b>で実行する派生。</summary>
public sealed class GraphSaveInMemoryRuntimeTests : GraphSaveRuntimeTestsBase
{
    /// <summary>全リポジトリで共有するインメモリストア（実 DB のファイルに相当する永続点）</summary>
    private readonly InMemoryDataStore _store = new();

    protected override Task ResetStorageAsync()
    {
        _store.Clear();

        return Task.CompletedTask;
    }

    protected override ICustomerRepository CreateCustomerRepository() =>
        new InMemoryCustomerRepository(_store);

    protected override IOrderRepository CreateOrderRepository() =>
        new InMemoryOrderRepository(_store);

    protected override IOrderLineRepository CreateOrderLineRepository() =>
        new InMemoryOrderLineRepository(_store);
}
