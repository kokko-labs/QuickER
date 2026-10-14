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

/// <summary>インメモリ Repository で流す派生</summary>
public sealed class StringMatchMethodInMemoryRuntimeTests
    : StringMatchMethodQueryFixtureRuntimeTestsBase
{
    private readonly InMemoryDataStore _store = new();

    protected override Task ResetStorageAsync()
    {
        _store.Clear();

        return Task.CompletedTask;
    }

    protected override ICustomerRepository CreateCustomerRepository() =>
        new InMemoryCustomerRepository(_store);
}
