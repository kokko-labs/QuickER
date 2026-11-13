using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using QuickER.Provider;
using QuickER.Provider.PostgreSql;
using QuickER.Tests.TestSupport;
using Xunit;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>EF Core 版 Repository（PostgreSQL）で実コンテナへ流す派生（Testcontainers・Docker 依存）</summary>
[Collection(PostgreSqlContainerCollection.Name)]
[Trait("RequiresDocker", "true")]
[RuntimeCell(RuntimeFeatures.ValueObjectStringMatch, RuntimeTarget.EfCorePostgreSql)]
public sealed class ValueObjectStringMatchEfCorePostgreSqlRuntimeTests(
    PostgreSqlContainerFixture fixture
) : StringMatchMethodEfCoreDialectRuntimeTestsBase
{
    /// <inheritdoc />
    protected override IDatabaseProvider Provider { get; } = new PostgreSqlProvider();

    /// <inheritdoc />
    protected override void SkipUnlessAvailable() =>
        Assert.SkipUnless(fixture.IsAvailable, fixture.UnavailableReason);

    /// <inheritdoc />
    protected override async Task ResetAndApplyDdlAsync(string ddl)
    {
        await fixture.ResetSchemaAsync(Ct);
        await fixture.ExecuteAsync(ddl, Ct);
    }

    /// <inheritdoc />
    protected override void Configure(DbContextOptionsBuilder options) =>
        options.UseNpgsql(fixture.ConnectionString).IgnoreServiceProviderCacheLimit();
}
