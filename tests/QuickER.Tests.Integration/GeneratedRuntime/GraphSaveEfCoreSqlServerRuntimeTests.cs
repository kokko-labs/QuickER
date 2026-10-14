using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QuickER.Tests.GeneratedQueryFixture;
using QuickER.Tests.Integration;
using QuickER.Tests.TestSupport;
using Xunit;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>
/// グラフ保存のランタイムスイートを<b>EF Core 版 Repository を実 SQL Server</b>（Testcontainers・Docker 依存）で流す派生。
/// </summary>
/// <remarks>
/// EF Core 版の DbContext は方言に依らないため、クエリフィクスチャ（SQLite 方言で生成）の <c>QuickErDbContext</c> を
/// <c>UseSqlServer</c> でそのまま使い、スキーマは同じ図から作った SQL Server の DDL で用意する。SQLite と違い
/// 外部キーを文ごとに検査し、EF Core の更新バッチも別の形になるため、閉包の外を追跡しないことを実 DB で確かめる。
/// Docker 不在時は <see cref="SqlServerContainerFixture"/> の検出でスキップされる。
/// </remarks>
[Collection(SqlServerContainerCollection.Name)]
[Trait("RequiresDocker", "true")]
public sealed class GraphSaveEfCoreSqlServerRuntimeTests(SqlServerContainerFixture fixture)
    : GraphSaveRuntimeTestsBase,
        IAsyncLifetime
{
    /// <summary>共有する SQL Server コンテナ</summary>
    private readonly SqlServerContainerFixture _fixture = fixture;

    /// <summary>EF Core 版リポジトリ群を登録した DI コンテナ（UseSqlServer）</summary>
    private ServiceProvider _provider = null!;

    /// <summary>Docker の有無を判定し、リポジトリ DI を構築する</summary>
    public ValueTask InitializeAsync()
    {
        Assert.SkipUnless(_fixture.IsAvailable, _fixture.UnavailableReason);

        _provider = new ServiceCollection()
            .AddGeneratedEfCoreRepositories(options =>
                options.UseSqlServer(_fixture.ConnectionString).IgnoreServiceProviderCacheLimit()
            )
            .BuildServiceProvider();

        return ValueTask.CompletedTask;
    }

    /// <summary>DI コンテナを破棄する</summary>
    public ValueTask DisposeAsync()
    {
        _provider?.Dispose();

        return ValueTask.CompletedTask;
    }

    protected override async Task ResetStorageAsync()
    {
        await _fixture.ResetSchemaAsync(Ct);
        await _fixture.ApplyDdlAsync(QueryFixtureDefinition.Build(), Ct);
    }

    protected override ICustomerRepository CreateCustomerRepository() =>
        _provider.GetRequiredService<ICustomerRepository>();

    protected override IOrderRepository CreateOrderRepository() =>
        _provider.GetRequiredService<IOrderRepository>();

    protected override IOrderLineRepository CreateOrderLineRepository() =>
        _provider.GetRequiredService<IOrderLineRepository>();
}
