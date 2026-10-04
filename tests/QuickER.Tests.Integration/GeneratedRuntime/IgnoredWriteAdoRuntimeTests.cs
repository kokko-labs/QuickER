using Microsoft.Extensions.DependencyInjection;
using QuickER.Tests.GeneratedQueryFixture;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>当たらない更新の扱いを<b>QuickER 版 Repository（SQLite 方言）</b>で確かめる派生（EF Core 版の基準）。</summary>
public sealed class IgnoredWriteAdoRuntimeTests : IgnoredWriteRuntimeTestsBase
{
    /// <summary>QuickER の SQLite リポジトリ群を登録した DI コンテナ</summary>
    private ServiceProvider? _provider;

    private ServiceProvider Provider() =>
        _provider ??= new ServiceCollection()
            .AddGeneratedSqliteRepositories(ConnectionString)
            .BuildServiceProvider();

    protected override ICustomerRepository CreateCustomerRepository() =>
        Provider().GetRequiredService<ICustomerRepository>();

    protected override IOrderRepository CreateOrderRepository() =>
        Provider().GetRequiredService<IOrderRepository>();

    public override void Dispose()
    {
        _provider?.Dispose();
        base.Dispose();
    }
}
