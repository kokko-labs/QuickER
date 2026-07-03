using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QuickER.MySql;
using QuickER.Tests.GeneratedPortableFixture;

namespace QuickER.Tests.Integration;

/// <summary>
/// 方言可搬な生成物（EF 版）を実 MySQL（Testcontainers）で流す方言ランタイムテスト。
/// スキーマは <see cref="MySqlDdlGenerator"/> の DDL、接続は <c>UseMySQL</c> で構成する。
/// </summary>
[Trait("Category", "Integration")]
[Collection(MySqlContainerCollection.Name)]
public sealed class GeneratedEfCoreMySqlRuntimeTests(MySqlContainerFixture fixture)
    : GeneratedEfCoreDialectRuntimeTestsBase,
        IDisposable
{
    private ServiceProvider? _provider;

    /// <summary>AddGeneratedEfCoreRepositories → UseMySQL の DI 経路でリポジトリ群を解決する</summary>
    private ServiceProvider Provider() =>
        _provider ??= new ServiceCollection()
            .AddGeneratedEfCoreRepositories(options => options.UseMySQL(fixture.ConnectionString))
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
        var ddl = new MySqlDdlGenerator().Build(
            PortableFixtureDefinition.Build(PortableDialect.MySql)
        );
        await fixture.ExecuteAsync(ddl, Ct);
    }

    /// <summary>MySQL はバッククォートで識別子を引用する</summary>
    protected override string Quote(string identifier) => $"`{identifier}`";

    /// <summary>MySQL（MySqlConnector）は @ プレフィックスのプレースホルダを用いる</summary>
    protected override string Param(string name) => $"@{name}";

    public void Dispose() => _provider?.Dispose();
}
