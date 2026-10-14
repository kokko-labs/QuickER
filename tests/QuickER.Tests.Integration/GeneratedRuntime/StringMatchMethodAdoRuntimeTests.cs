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
using Xunit;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>QuickER 版 Repository（SQLite 方言）で流す派生</summary>
public sealed class StringMatchMethodAdoRuntimeTests : StringMatchMethodSqliteFileRuntimeTestsBase
{
    private ServiceProvider? _provider;

    private ServiceProvider Provider() =>
        _provider ??= new ServiceCollection()
            .AddGeneratedSqliteRepositories(ConnectionString)
            .BuildServiceProvider();

    protected override ICustomerRepository CreateCustomerRepository() =>
        Provider().GetRequiredService<ICustomerRepository>();

    public override void Dispose()
    {
        _provider?.Dispose();
        base.Dispose();
    }
}
