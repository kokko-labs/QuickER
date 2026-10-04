using Microsoft.Extensions.DependencyInjection;
using QuickER.Tests.GeneratedQueryFixture;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>グラフ保存のランタイムスイートを<b>QuickER 版 Repository（SQLite 方言）</b>で実行する派生（他の実装先の基準）。</summary>
public sealed class GraphSaveAdoRuntimeTests : GraphSaveSqliteFileRuntimeTestsBase
{
    /// <summary>QuickER の SQLite リポジトリ群を登録した DI コンテナ（接続文字列は基底の一時 DB）</summary>
    private ServiceProvider? _provider;

    /// <summary>AddGeneratedSqliteRepositories の DI 経路でリポジトリ群を解決する</summary>
    private ServiceProvider Provider() =>
        _provider ??= new ServiceCollection()
            .AddGeneratedSqliteRepositories(ConnectionString)
            .BuildServiceProvider();

    protected override ICustomerRepository CreateCustomerRepository() =>
        Provider().GetRequiredService<ICustomerRepository>();

    protected override IOrderRepository CreateOrderRepository() =>
        Provider().GetRequiredService<IOrderRepository>();

    protected override IOrderLineRepository CreateOrderLineRepository() =>
        Provider().GetRequiredService<IOrderLineRepository>();

    public override void Dispose()
    {
        _provider?.Dispose();
        base.Dispose();
    }
}
