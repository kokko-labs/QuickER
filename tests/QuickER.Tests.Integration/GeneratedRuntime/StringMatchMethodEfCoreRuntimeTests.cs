using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QuickER.Tests.GeneratedQueryFixture;
using QuickER.Tests.Integration;
using QuickER.Tests.TestSupport;
using Xunit;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>EF Core 版 Repository（SQLite）で流す派生</summary>
public sealed class StringMatchMethodEfCoreRuntimeTests
    : StringMatchMethodSqliteFileRuntimeTestsBase
{
    private ServiceProvider? _provider;

    private ServiceProvider Provider() =>
        _provider ??= new ServiceCollection()
            .AddGeneratedEfCoreRepositories(options =>
                options.UseSqlite(ConnectionString).IgnoreServiceProviderCacheLimit()
            )
            .BuildServiceProvider();

    protected override ICustomerRepository CreateCustomerRepository() =>
        Provider().GetRequiredService<ICustomerRepository>();

    /// <summary>
    /// 受け手が値オブジェクトのパラメータで、引数が列の文字列一致（<c>term.Contains(c.Name)</c>）。
    /// </summary>
    /// <remarks>
    /// 受け手のパラメータは型の対応付けを持たないので、列の対応付け（値オブジェクト → 素の値の変換つき）を当てる。
    /// 素の string の対応付けを当てると、値オブジェクトを送れずに失敗する。
    /// </remarks>
    [Fact(
        DisplayName = "[StringMatch] EF Core: 受け手が値オブジェクトのパラメータの文字列一致も実行できる"
    )]
    public async Task ParameterReceiver_UsesTheColumnMapping()
    {
        await ResetAndSeedAsync();

        var term = NameValue.Create("Alice Smith");
        (await QueryIdsAsync(c => term.Contains(c.Name))).Should().BeEquivalentTo([1]);

        var prefix = NameValue.Create("Bobby");
        (await QueryIdsAsync(c => prefix.StartsWith(c.Name))).Should().BeEquivalentTo([2]);
    }

    public override void Dispose()
    {
        _provider?.Dispose();
        base.Dispose();
    }
}
