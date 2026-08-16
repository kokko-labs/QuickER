using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QuickER.Tests.GeneratedQueryFixture;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>
/// 重複事前チェックのランタイムスイートを<b>EF Core Sqlite 版</b>で実行する派生。
/// QuickER 版（<see cref="UniquenessCheckAdoRuntimeTests"/>）と同一のアサーション集合を緑にすることで、
/// 共有本体（式木クエリ 1 本）が両バックエンドで同じ意味論に翻訳されることを証明する。
/// </summary>
public sealed class UniquenessCheckEfCoreRuntimeTests : UniquenessCheckQueryFixtureRuntimeTestsBase
{
    /// <summary>EF Core 版リポジトリ群を登録した DI コンテナ（UseSqlite・接続文字列は基底の一時 DB）</summary>
    private ServiceProvider? _provider;

    /// <summary>AddGeneratedEfCoreRepositories → UseSqlite の DI 経路でリポジトリ群を解決する</summary>
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
