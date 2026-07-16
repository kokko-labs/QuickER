using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QuickER.Tests.GeneratedFixture;

namespace QuickER.Tests.Integration;

/// <summary>
/// パリティスイートを<b>EF Core 版</b>ランタイム（<see cref="EfCoreRepository{TEntity, TKey}"/> /
/// <see cref="EfCoreSqlExecutor"/>）で実行する派生。リポジトリ・エグゼキュータは実運用と同じ経路
/// （<see cref="GeneratedEfCoreRepositoryServiceCollectionExtensions.AddGeneratedEfCoreRepositories"/> →
/// <see cref="ServiceProvider"/> から解決）で取得する。接続は同一コンテナの接続文字列で
/// <c>options.UseSqlServer(connectionString)</c> により構成する。
/// </summary>
/// <remarks>
/// これにより「AddGeneratedSqlServerRepositories と AddGeneratedEfCoreRepositories を差し替えるだけで交換可能」
/// という DI 契約そのものを、全共通シナリオを DI 解決経路で流して証明する。
/// </remarks>
public sealed class GeneratedRuntimeEfCoreParityTests(SqlServerContainerFixture fixture)
    : GeneratedRuntimeParityTestsBase(fixture),
        IDisposable
{
    /// <summary>EF Core 版リポジトリ群を登録した DI コンテナ（接続文字列が有効なときのみ構築）</summary>
    private ServiceProvider? _provider;

    /// <summary>DI コンテナを遅延構築する（フィクスチャ利用可能時のみ。接続文字列は Initialize 後に確定するため）</summary>
    private ServiceProvider Provider()
    {
        // AddGeneratedEfCoreRepositories を実運用と同じ形で呼び、UseSqlServer で SQL Server 方言を選択する
        return _provider ??= new ServiceCollection()
            .AddGeneratedEfCoreRepositories(options =>
                options.UseSqlServer(Fixture.ConnectionString)
            )
            .BuildServiceProvider();
    }

    protected override ICustomerRepository CreateCustomerRepository() =>
        Provider().GetRequiredService<ICustomerRepository>();

    protected override IOrderRepository CreateOrderRepository() =>
        Provider().GetRequiredService<IOrderRepository>();

    protected override ISqlExecutor CreateSqlExecutor() =>
        Provider().GetRequiredService<ISqlExecutor>();

    /// <summary>DI コンテナを破棄する</summary>
    public void Dispose() => _provider?.Dispose();
}
