using System.Linq;
using System.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QuickER.Provider.Oracle;
using QuickER.Tests.GeneratedPortableFixture;
using QuickER.Tests.Integration;
using QuickER.Tests.TestSupport;

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
            .AddGeneratedEfCoreRepositories(options =>
                options.UseOracle(fixture.ConnectionString).IgnoreServiceProviderCacheLimit()
            )
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

    /// <summary>
    /// null のパターンの文字列一致は、ほかの方言と同じく一致しない。
    /// </summary>
    /// <remarks>
    /// Oracle は NULL を連結すると残りの文字列になるので、何も補わないとパターンは <c>'%' || NULL || '%'</c>＝<c>'%%'</c>
    /// になり、全行に一致する。翻訳器はパラメータのパターンに <c>IS NOT NULL</c> を添える。null かどうかは EF Core が
    /// C# の値で判断するので、空文字列（Oracle では NULL として届く）の一致は変わらない（基底の 2c が固定する）。
    /// <c>SqlQuery.Where</c> は null を先に断るので、この経路に届くのは DbContext を直接使ったときだけ。
    /// </remarks>
    [Fact(DisplayName = "[Dialect] Oracle: null のパターンの Contains は全行に一致しない")]
    public async Task Where_ContainsNullPattern_MatchesNoRow()
    {
        await ResetAndCreateSchemaAsync();

        var repo = CreateCustomerRepository();
        await repo.InsertAsync(NewCustomer(1, "Alice"), Ct);
        await repo.InsertAsync(NewCustomer(2, "Bob"), Ct);

        using var scope = Provider().CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<QuickErDbContext>();
        string? pattern = null;

        var matched = await context
            .Set<CustomerEntity>()
            .Where(c => c.Name.Contains(pattern!))
            .ToListAsync(Ct);

        matched.Should().BeEmpty();
    }

    public void Dispose() => _provider?.Dispose();
}
