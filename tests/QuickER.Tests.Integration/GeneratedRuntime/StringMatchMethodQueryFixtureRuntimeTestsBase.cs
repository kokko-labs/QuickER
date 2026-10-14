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

/// <summary>
/// 値オブジェクトの文字列一致のシナリオを、クエリフィクスチャ（VO 有効・SQLite 方言＋EF Core＋インメモリ）で流す派生の共通基底。
/// </summary>
public abstract class StringMatchMethodQueryFixtureRuntimeTestsBase
    : StringMatchMethodRuntimeTestsBase<CustomerEntity>
{
    /// <summary>保存先を空にする</summary>
    protected abstract Task ResetStorageAsync();

    /// <summary>顧客リポジトリを生成する</summary>
    protected abstract ICustomerRepository CreateCustomerRepository();

    protected override async Task ResetAndSeedAsync()
    {
        await ResetStorageAsync();

        var customers = CreateCustomerRepository();

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
        (await CreateCustomerRepository().Query().Where(predicate).ToListAsync(Ct))
            .Select(c => c.CustomerId.Value)
            .ToList();
}
