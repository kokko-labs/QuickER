using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QuickER.Tests.GeneratedQueryFixture;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>
/// グラフ取得糖衣のランタイムスイートを<b>EF Core Sqlite 版</b>で実行する派生。
/// </summary>
/// <remarks>
/// EF Core 実装は同じ <c>IncludeNode</c> ツリーを EF Core の <c>Include</c>/<c>ThenInclude</c> 呼び出しへ
/// 組み替えて実行する（QuickER 版の <see cref="IncludeGraphAdoRuntimeTests"/> とは別実装）。
/// 同一のアサーション集合を緑にすることで、同じツリーが両実行器で同じ結果グラフを返すことを固定する。
/// </remarks>
public sealed class IncludeGraphEfCoreRuntimeTests : IncludeGraphSqliteFileRuntimeTestsBase
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

    protected override IOrderLineRepository CreateOrderLineRepository() =>
        Provider().GetRequiredService<IOrderLineRepository>();

    public override void Dispose()
    {
        _provider?.Dispose();
        base.Dispose();
    }
}
