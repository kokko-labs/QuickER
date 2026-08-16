using Microsoft.Extensions.DependencyInjection;
using QuickER.Tests.GeneratedQueryFixture;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>
/// 名前付きクエリのランタイムスイートを<b>QuickER の <c>SqliteRepository</c> 版</b>で実行する派生。
/// リポジトリは実運用と同じ DI 経路（<c>AddGeneratedSqliteRepositories(connectionString)</c>）で解決する。
/// </summary>
public sealed class NamedQueryAdoRuntimeTests : NamedQueryRawSqlRuntimeTestsBase
{
    /// <summary>QuickER の SQLite リポジトリ群を登録した DI コンテナ（接続文字列は基底の一時 DB）</summary>
    private ServiceProvider? _provider;

    /// <summary>AddGeneratedSqliteRepositories → QuickER の SqliteRepository の DI 経路でリポジトリ群を解決する</summary>
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
