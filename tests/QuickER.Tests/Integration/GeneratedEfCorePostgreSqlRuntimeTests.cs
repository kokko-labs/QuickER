using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QuickER.PostgreSql;
using QuickER.Tests.GeneratedPortableFixture;

namespace QuickER.Tests.Integration;

/// <summary>
/// 方言可搬な生成物（EF Core 版）を実 PostgreSQL（Testcontainers）で流す方言ランタイムテスト。
/// スキーマは <see cref="PostgreSqlDdlGenerator"/> の DDL、接続は <c>UseNpgsql</c> で構成する。
/// </summary>
[Trait("Category", "Integration")]
[Collection(PostgreSqlContainerCollection.Name)]
public sealed class GeneratedEfCorePostgreSqlRuntimeTests(PostgreSqlContainerFixture fixture)
    : GeneratedEfCoreDialectRuntimeTestsBase,
        IDisposable
{
    private ServiceProvider? _provider;

    /// <summary>AddGeneratedEfCoreRepositories → UseNpgsql の DI 経路でリポジトリ群を解決する</summary>
    private ServiceProvider Provider() =>
        _provider ??= new ServiceCollection()
            .AddGeneratedEfCoreRepositories(options => options.UseNpgsql(fixture.ConnectionString))
            .BuildServiceProvider();

    protected override ICustomerRepository CreateCustomerRepository() =>
        Provider().GetRequiredService<ICustomerRepository>();

    protected override IOrderRepository CreateOrderRepository() =>
        Provider().GetRequiredService<IOrderRepository>();

    protected override ISqlExecutor CreateSqlExecutor() =>
        Provider().GetRequiredService<ISqlExecutor>();

    protected override async Task ResetAndCreateSchemaAsync()
    {
        Assert.SkipUnless(fixture.IsAvailable, fixture.UnavailableReason);
        await fixture.ResetSchemaAsync(Ct);
        var ddl = new PostgreSqlDdlGenerator().Build(
            PortableFixtureDefinition.Build(PortableDialect.PostgreSql)
        );
        await fixture.ExecuteAsync(ddl, Ct);
    }

    /// <summary>PostgreSQL は二重引用符で識別子を引用する</summary>
    protected override string Quote(string identifier) => $"\"{identifier}\"";

    /// <summary>PostgreSQL（Npgsql）は @ プレフィックスのプレースホルダを用いる</summary>
    protected override string Param(string name) => $"@{name}";

    public void Dispose() => _provider?.Dispose();
}
