using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using QuickER.Tests.GeneratedBackendParityFixture;
using Xunit;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>
/// EF Core 版 Repository のカスケードの一括削除（<c>Query().ExecuteDeleteAsync(cascadeDelete: true)</c>）が、実行した文と
/// 消した行の両面で決めたとおりに動くことを、SQLite の実ファイルで確かめる（Docker 不要）。
/// </summary>
/// <remarks>
/// <para>
/// 実装先どうしの一致テスト（<see cref="BackendParityRuntimeTests"/>）は保存先の結果しか比べないので、ここは結果に現れない
/// 面を受け持つ：循環するカスケードを断る前に DELETE を流していないか（ロールバックされても仕事は起きている）と、
/// 条件に合う根のキーを読んでから消すときに、根がかたまりの大きさを超えても全部消えるか。
/// </para>
/// </remarks>
[Trait("Category", "Integration")]
public sealed class EfCoreExecuteDeleteRuntimeTests : IDisposable
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    private readonly SqliteTempDatabase _db = SqliteTempDatabase.Create();
    private readonly CountDeletes _deletes = CountDeletes.Instance;
    private ServiceProvider? _provider;

    public void Dispose()
    {
        _provider?.Dispose();
        _db.Dispose();
    }

    /// <summary>空のスキーマを作り、DELETE を数える EF Core 版リポジトリを組み立てる</summary>
    private async Task<ServiceProvider> SetUpAsync()
    {
        await _db.ResetSchemaAsync(Ct);
        await _db.ApplyDdlAsync(BackendParityFixtureDefinition.Build(), Ct);
        _deletes.Count = 0;

        return _provider = new ServiceCollection()
            .AddGeneratedEfCoreRepositories(options =>
                options.UseSqliteWith(_db.ReadWriteCreateConnectionString, _deletes)
            )
            .BuildServiceProvider();
    }

    [Fact(DisplayName = "[EfCore] 循環するカスケードの一括削除は、DELETE を 1 本も流さずに断る")]
    public async Task CyclicCascade_IsRefusedBeforeAnyDelete()
    {
        var provider = await SetUpAsync();
        await provider
            .GetRequiredService<ICustomerRepository>()
            .InsertAsync((CustomerEntity)new CustomerRow(1, "Alice").Build(), Ct);
        await provider
            .GetRequiredService<IOrderRepository>()
            .InsertAsync((OrderEntity)new OrderRow(10, 1, 1m).Build(), Ct);
        await provider
            .GetRequiredService<IOrderLineRepository>()
            .InsertAsync((OrderLineEntity)new LineRow(100, 10, "pen", 1).Build(), Ct);
        _deletes.Count = 0;

        var act = () =>
            provider
                .GetRequiredService<ICustomerRepository>()
                .Query()
                .Where(c => c.CustomerId.Value >= 1)
                .ExecuteDeleteAsync(cascadeDelete: true, Ct);

        await act.Should().ThrowAsync<NotSupportedException>();
        _deletes.Count.Should().Be(0, "循環はメタデータだけで、トランザクションを開く前に見つける");
        (await provider.GetRequiredService<IOrderLineRepository>().GetAllAsync(Ct))
            .Should()
            .ContainSingle();
    }

    [Fact(
        DisplayName = "[EfCore] ナビゲーションの条件のカスケード一括削除は、根がかたまりより多くても根と子孫をすべて消す"
    )]
    public async Task NavigationPredicate_DeletesEveryMatchingRootAndDescendant()
    {
        var provider = await SetUpAsync();
        await provider
            .GetRequiredService<ICustomerRepository>()
            .InsertAsync((CustomerEntity)new CustomerRow(1, "Alice").Build(), Ct);
        var orderIds = Enumerable.Range(1, 1200).ToList();
        await provider
            .GetRequiredService<IOrderRepository>()
            .BulkInsertAsync(
                orderIds.Select(id => (OrderEntity)new OrderRow(id, 1, id).Build()).ToList(),
                Ct
            );
        await provider
            .GetRequiredService<IOrderLineRepository>()
            .BulkInsertAsync(
                orderIds
                    .Where(id => id % 2 == 0)
                    .Select(id => (OrderLineEntity)new LineRow(id, id, "x", 1).Build())
                    .ToList(),
                Ct
            );

        var rows = await provider
            .GetRequiredService<IOrderRepository>()
            .Query()
            .Where(o => o.OrderLines.Any())
            .ExecuteDeleteAsync(cascadeDelete: true, Ct);

        rows.Should().Be(1200, "根 600 件と明細 600 件");
        var orders = await provider.GetRequiredService<IOrderRepository>().GetAllAsync(Ct);
        orders
            .Select(o => o.OrderId.Value)
            .Should()
            .OnlyContain(id => id % 2 == 1)
            .And.HaveCount(600);
        (await provider.GetRequiredService<IOrderLineRepository>().GetAllAsync(Ct))
            .Should()
            .BeEmpty();
    }

    /// <summary>流れた DELETE の文を数える</summary>
    /// <remarks>
    /// 1 インスタンスを使い回し、テストの初めに数え直す（インスタンスが違うと EF Core は内部のサービスプロバイダを別に作り、
    /// プロセス全体で 20 を超えると ManyServiceProvidersCreatedWarning でほかのテストを巻き込む）。このクラスのテストは 1 つずつ
    /// 流れ、ほかのクラスはこのインターセプターを使わない。
    /// </remarks>
    private sealed class CountDeletes : DbCommandInterceptor
    {
        public static readonly CountDeletes Instance = new();

        public int Count;

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default
        )
        {
            if (
                command
                    .CommandText.TrimStart()
                    .StartsWith("DELETE", StringComparison.OrdinalIgnoreCase)
            )
            {
                Interlocked.Increment(ref Count);
            }

            return ValueTask.FromResult(result);
        }
    }
}
