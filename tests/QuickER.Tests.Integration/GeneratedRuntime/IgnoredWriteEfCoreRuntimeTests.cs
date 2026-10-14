using System.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QuickER.Tests.GeneratedQueryFixture;
using QuickER.Tests.TestSupport;
using Xunit;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>当たらない更新の扱いを<b>EF Core Sqlite 版</b>で確かめる派生。</summary>
public sealed class IgnoredWriteEfCoreRuntimeTests : IgnoredWriteRuntimeTestsBase
{
    /// <summary>EF Core 版リポジトリ群を登録した DI コンテナ</summary>
    private ServiceProvider? _provider;

    private ServiceProvider Provider() =>
        _provider ??= new ServiceCollection()
            .AddGeneratedEfCoreRepositories(options =>
                options.UseSqlite(ConnectionString).IgnoreServiceProviderCacheLimit()
            )
            .BuildServiceProvider();

    protected override ICustomerRepository CreateCustomerRepository() =>
        Provider().GetRequiredService<ICustomerRepository>();

    protected override IOrderRepository CreateOrderRepository() =>
        Provider().GetRequiredService<IOrderRepository>();

    /// <summary>
    /// グラフ保存で 1 行も当たらなかった文の NotFound は、流した操作の動詞で報告する（削除を「update」と言わない）。
    /// </summary>
    /// <remarks>
    /// 挿入はこの形では確かめられない：EF Core（SQLite）は、トリガーが捨てた INSERT を影響行数で検出せず、例外なく通す。
    /// </remarks>
    [Theory(DisplayName = "[EfCore] 当たらない文の NotFound は、流した操作の動詞で報告する")]
    [InlineData("UPDATE", "update")]
    [InlineData("DELETE", "delete")]
    public async Task NotFound_NamesTheOperation(string statement, string verb)
    {
        var order = await SeedWithIgnoringTriggerAsync(statement);
        OrderEntity target;

        switch (statement)
        {
            case "DELETE":
                order.MarkRemoved();
                target = order;
                break;
            default:
                target = order;
                break;
        }

        var act = () => CreateOrderRepository().SaveAsync(target, cancellationToken: Ct);

        var thrown = await act.Should().ThrowAsync<SaveConflictException>();
        thrown.Which.Reason.Should().Be(SaveConflictReason.NotFound);
        thrown.Which.Message.Should().StartWith($"The record to {verb} was not found");
    }

    public override void Dispose()
    {
        _provider?.Dispose();
        base.Dispose();
    }
}
