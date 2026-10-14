using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using QuickER.Tests.GeneratedUniquenessSqlServerFixture;
using QuickER.Tests.Integration;
using Xunit;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>
/// 値オブジェクトの文字列一致のシナリオを、QuickER 版 Repository（SQL Server 方言）で実 SQL Server へ流す派生
/// （Testcontainers・Docker 依存）。図はクエリフィクスチャと同じ。
/// </summary>
[Collection(SqlServerContainerCollection.Name)]
[Trait("RequiresDocker", "true")]
public sealed class StringMatchMethodSqlServerRuntimeTests(SqlServerContainerFixture fixture)
    : StringMatchMethodRuntimeTestsBase<CustomerEntity>,
        IAsyncLifetime
{
    private ServiceProvider _provider = null!;

    /// <summary>Docker の有無を判定し、リポジトリ DI を構築する</summary>
    public ValueTask InitializeAsync()
    {
        Assert.SkipUnless(fixture.IsAvailable, fixture.UnavailableReason);

        _provider = new ServiceCollection()
            .AddGeneratedSqlServerRepositories(fixture.ConnectionString)
            .BuildServiceProvider();

        return ValueTask.CompletedTask;
    }

    /// <summary>DI コンテナを破棄する</summary>
    public ValueTask DisposeAsync()
    {
        _provider?.Dispose();

        return ValueTask.CompletedTask;
    }

    protected override async Task ResetAndSeedAsync()
    {
        await fixture.ResetSchemaAsync(Ct);
        await fixture.ApplyDdlAsync(UniquenessSqlServerFixtureDefinition.Build(), Ct);

        var customers = _provider.GetRequiredService<ICustomerRepository>();

        foreach (var (id, name) in new[] { (1, "Alice"), (2, "Bob"), (3, "Carol") })
        {
            await customers.InsertAsync(
                new CustomerEntity
                {
                    CustomerId = CustomerIdValue.Create(id),
                    Name = NameValue.Create(name),
                },
                Ct
            );
        }
    }

    protected override async Task<IReadOnlyList<int>> QueryIdsAsync(
        Expression<Func<CustomerEntity, bool>> predicate
    ) =>
        (
            await _provider
                .GetRequiredService<ICustomerRepository>()
                .Query()
                .Where(predicate)
                .ToListAsync(Ct)
        )
            .Select(c => c.CustomerId.Value)
            .ToList();
}
