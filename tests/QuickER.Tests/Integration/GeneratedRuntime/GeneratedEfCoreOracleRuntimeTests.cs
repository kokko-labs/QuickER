using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QuickER.Oracle;
using QuickER.Tests.GeneratedPortableFixture;
using QuickER.Tests.Integration;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>
/// 方言可搬な生成物（EF Core 版）を実 Oracle（Testcontainers）で流す方言ランタイムテスト。
/// スキーマは <see cref="OracleDdlGenerator"/> の DDL、接続は <c>UseOracle</c> で構成する。
/// </summary>
/// <remarks>Oracle コンテナは起動が重い（数分）。既存 Oracle 統合テストのフィクスチャ・タイムアウトに従う。</remarks>
[Trait("Category", "Integration")]
[Collection(OracleContainerCollection.Name)]
[Trait("RequiresDocker", "true")]
public sealed class GeneratedEfCoreOracleRuntimeTests(OracleContainerFixture fixture)
    : GeneratedEfCoreDialectRuntimeTestsBase,
        IDisposable
{
    private ServiceProvider? _provider;

    /// <summary>AddGeneratedEfCoreRepositories → UseOracle の DI 経路でリポジトリ群を解決する</summary>
    private ServiceProvider Provider() =>
        _provider ??= new ServiceCollection()
            .AddGeneratedEfCoreRepositories(options => options.UseOracle(fixture.ConnectionString))
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
        var ddl = new OracleDdlGenerator().Build(
            PortableFixtureDefinition.Build(PortableDialect.Oracle)
        );
        await fixture.ExecuteAsync(ddl, Ct);
    }

    /// <summary>Oracle は二重引用符で識別子を引用する</summary>
    protected override string Quote(string identifier) => $"\"{identifier}\"";

    /// <summary>Oracle（ODP.NET）は : プレフィックスのプレースホルダを用いる</summary>
    protected override string Param(string name) => $":{name}";

    public void Dispose() => _provider?.Dispose();
}
