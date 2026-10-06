using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QuickER.Tests.GeneratedQueryFixture;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>当たらない更新の扱いを<b>EF Core Sqlite 版</b>で確かめる派生。</summary>
public sealed class IgnoredWriteEfCoreRuntimeTests : IgnoredWriteRuntimeTestsBase
{
    /// <summary>EF Core 版リポジトリ群を登録した DI コンテナ</summary>
    private ServiceProvider? _provider;

    private ServiceProvider Provider() =>
        _provider ??= new ServiceCollection()
            .AddGeneratedEfCoreRepositories(options => options.UseSqlite(ConnectionString))
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
