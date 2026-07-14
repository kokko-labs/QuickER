using Microsoft.Extensions.DependencyInjection;
using QuickER.Tests.GeneratedQueryFixture;

namespace QuickER.Tests.Integration;

/// <summary>
/// 名前付きクエリのランタイムスイートを<b>QuickER の <c>SqliteRepository</c> 版</b>で実行する派生。
/// リポジトリは実運用と同じ DI 経路（<c>AddGeneratedRepositories(connectionString)</c>）で解決する。
/// </summary>
public sealed class NamedQueryAdoRuntimeTests : NamedQueryRuntimeTestsBase
{
    /// <summary>QuickER の SQLite リポジトリ群を登録した DI コンテナ（接続文字列は基底の一時 DB）</summary>
    private ServiceProvider? _provider;

    /// <summary>AddGeneratedRepositories → QuickER の SqliteRepository の DI 経路でリポジトリ群を解決する</summary>
    private ServiceProvider Provider() =>
        _provider ??= new ServiceCollection()
            .AddGeneratedRepositories(ConnectionString)
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
