using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QuickER.Tests.GeneratedQueryFixture;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>
/// 名前付きクエリのランタイムスイートを<b>EF Core Sqlite 版</b>で実行する派生。
/// QuickER 版（<see cref="NamedQueryAdoRuntimeTests"/>）と同一のアサーション集合を緑にすることで、
/// ミニ DSL 由来の共有本体（単一ラムダ→両バックエンド翻訳）のパリティを証明する。
/// 自由 SQL・manual 分は partial 実装（QueryFixtureManualImplementations）が担う。
/// </summary>
public sealed class NamedQueryEfCoreRuntimeTests : NamedQueryRawSqlRuntimeTestsBase
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
