using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using QuickER.Tests.GeneratedQueryFixture;
using QuickER.Tests.Integration;
using QuickER.Tests.TestSupport;
using Xunit;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>
/// EF Core 版リポジトリが、再試行する実行戦略（SQL Server の <c>EnableRetryOnFailure</c> など）の構成で
/// 自前のトランザクションを開く経路（Save フックありの保存・カスケードの一括削除）を実行できることを検証する。
/// </summary>
/// <remarks>
/// <para>
/// 再試行する実行戦略は、戦略の外で開かれたトランザクションを拒否する。生成コードはその経路を戦略へ渡し、
/// 試行ごとに変更追跡を空にして登録し直す。ここでは再試行を宣言する自作の戦略を SQLite（実ファイル）へ設定し、
/// コミットで 1 回だけ失敗させて、2 回目の試行で保存が成立すること・フックが試行の回数だけ走ることを確かめる。
/// </para>
/// <para>Docker 不要（CI 常時実行）。</para>
/// </remarks>
public sealed class EfCoreExecutionStrategyRuntimeTests : IDisposable
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    /// <summary>各テストが読み書きする一時ファイル DB</summary>
    private readonly SqliteTempDatabase _db = SqliteTempDatabase.Create();

    /// <summary>テストごとに作った DI コンテナ（最後にまとめて破棄する）</summary>
    private readonly List<ServiceProvider> _providers = [];

    /// <summary>一時 DB を破棄する</summary>
    public void Dispose()
    {
        foreach (var provider in _providers)
        {
            provider.Dispose();
        }

        _db.Dispose();
    }

    /// <summary>テスト用の一時的な失敗（再試行の対象として戦略に認識させる）</summary>
    private sealed class TransientTestException()
        : Exception("transient failure injected by the test");

    /// <summary>再試行を宣言し、<see cref="TransientTestException"/> だけを再試行する実行戦略</summary>
    private sealed class RetryingStrategy(ExecutionStrategyDependencies dependencies)
        : ExecutionStrategy(
            dependencies,
            maxRetryCount: 3,
            maxRetryDelay: TimeSpan.FromMilliseconds(1)
        )
    {
        protected override bool ShouldRetryOn(Exception exception) =>
            exception is TransientTestException;
    }

    /// <summary>最初のコミットだけを一時的な失敗にするトランザクションのインターセプター</summary>
    private sealed class FailFirstCommit : DbTransactionInterceptor
    {
        /// <summary>まだ失敗させていないか</summary>
        private int _pending = 1;

        /// <summary>コミットを試みた回数</summary>
        public int Commits;

        public override ValueTask<InterceptionResult> TransactionCommittingAsync(
            DbTransaction transaction,
            TransactionEventData eventData,
            InterceptionResult result,
            CancellationToken cancellationToken = default
        )
        {
            Interlocked.Increment(ref Commits);

            if (Interlocked.Exchange(ref _pending, 0) == 1)
            {
                throw new TransientTestException();
            }

            return ValueTask.FromResult(result);
        }
    }

    /// <summary>呼び出しを記録する注文の Save フック</summary>
    private sealed class OrderHook(List<string> log) : ISaveHook<OrderEntity>
    {
        public Task<bool> BeforeSaveAsync(
            OrderEntity entity,
            SaveOperation operation,
            CancellationToken cancellationToken = default
        )
        {
            log.Add($"before:{operation}:{entity.OrderId.Value}");
            return Task.FromResult(true);
        }

        public Task AfterSaveAsync(
            OrderEntity entity,
            SaveOperation operation,
            ISaveHookContext context,
            CancellationToken cancellationToken = default
        )
        {
            log.Add($"after:{operation}:{entity.OrderId.Value}");
            return Task.CompletedTask;
        }
    }

    /// <summary>再試行する戦略（と任意のインターセプター・フック）を設定した EF Core 版リポジトリの DI を組む</summary>
    private ServiceProvider BuildProvider(IInterceptor? interceptor = null, OrderHook? hook = null)
    {
        var services = new ServiceCollection().AddGeneratedEfCoreRepositories(options =>
        {
            options
                .UseSqlite(
                    _db.ReadWriteCreateConnectionString,
                    sqlite =>
                        sqlite.ExecutionStrategy(dependencies => new RetryingStrategy(dependencies))
                )
                .IgnoreServiceProviderCacheLimit();

            if (interceptor is not null)
            {
                options.AddInterceptors(interceptor);
            }
        });

        if (hook is not null)
        {
            services.AddSaveHook(hook);
        }

        var provider = services.BuildServiceProvider();
        _providers.Add(provider);
        return provider;
    }

    /// <summary>空のスキーマへ顧客 1・注文 2・明細 2 を投入する</summary>
    private async Task ResetAndSeedAsync()
    {
        await _db.ResetSchemaAsync(Ct);
        await _db.ApplyDdlAsync(QueryFixtureDefinition.Build(), Ct);

        var provider = BuildProvider();
        await provider
            .GetRequiredService<ICustomerRepository>()
            .InsertAsync(
                new CustomerEntity
                {
                    CustomerId = CustomerIdValue.Create(1),
                    Name = NameValue.Create("Alice"),
                },
                Ct
            );

        var orders = provider.GetRequiredService<IOrderRepository>();
        await orders.InsertAsync(NewOrder(10), Ct);
        await orders.InsertAsync(NewOrder(11), Ct);

        var lines = provider.GetRequiredService<IOrderLineRepository>();
        await lines.InsertAsync(NewLine(100, 10), Ct);
        await lines.InsertAsync(NewLine(101, 11), Ct);
    }

    /// <summary>注文エンティティを組み立てる</summary>
    private static OrderEntity NewOrder(int orderId) =>
        new()
        {
            OrderId = OrderIdValue.Create(orderId),
            CustomerId = CustomerIdValue.Create(1),
            Amount = AmountValue.Create(orderId),
        };

    /// <summary>注文明細エンティティを組み立てる</summary>
    private static OrderLineEntity NewLine(int lineId, int orderId) =>
        new()
        {
            LineId = LineIdValue.Create(lineId),
            OrderId = OrderIdValue.Create(orderId),
            ItemName = ItemNameValue.Create("pen"),
            Quantity = QuantityValue.Create(1),
        };

    [Fact(DisplayName = "EF Core: 再試行する実行戦略でも Save フックありの保存が成立する")]
    public async Task SaveWithHooks_UnderRetryingStrategy_Succeeds()
    {
        await ResetAndSeedAsync();
        var log = new List<string>();
        var orders = BuildProvider(hook: new OrderHook(log)).GetRequiredService<IOrderRepository>();

        var order = NewOrder(12);
        order.MarkAdded();

        (await orders.SaveAsync(order, cancellationToken: Ct)).Should().Be(1);

        log.Should().Equal("before:Insert:12", "after:Insert:12");
        order.RowState.Should().Be(RowState.Unchanged);
        (await orders.GetByIdAsync(OrderIdValue.Create(12), Ct)).Should().NotBeNull();
    }

    [Fact(
        DisplayName = "EF Core: コミットが一時的に失敗すると保存は試行し直され、フックは試行の回数だけ走る"
    )]
    public async Task SaveWithHooks_RetriedAfterTransientCommitFailure_RunsHooksPerAttempt()
    {
        await ResetAndSeedAsync();
        var log = new List<string>();
        var interceptor = new FailFirstCommit();
        var provider = BuildProvider(interceptor, new OrderHook(log));
        var orders = provider.GetRequiredService<IOrderRepository>();

        var order = NewOrder(12);
        order.MarkAdded();

        (await orders.SaveAsync(order, cancellationToken: Ct)).Should().Be(1);

        interceptor.Commits.Should().Be(2);
        log.Should()
            .Equal("before:Insert:12", "after:Insert:12", "before:Insert:12", "after:Insert:12");
        order.RowState.Should().Be(RowState.Unchanged);

        // 1 回目の試行はロールバックされ、行は 1 つだけ残る
        var all = await BuildProvider()
            .GetRequiredService<IOrderRepository>()
            .Query()
            .ToListAsync(Ct);
        all.Select(o => o.OrderId.Value).Order().Should().Equal(10, 11, 12);
    }

    [Fact(DisplayName = "EF Core: 再試行する実行戦略でもカスケードの一括削除が成立する")]
    public async Task CascadeExecuteDelete_UnderRetryingStrategy_Succeeds()
    {
        await ResetAndSeedAsync();
        var provider = BuildProvider();
        var orders = provider.GetRequiredService<IOrderRepository>();

        var key = OrderIdValue.Create(10);
        var deleted = await orders
            .Query()
            .Where(o => o.OrderId == key)
            .ExecuteDeleteAsync(cascadeDelete: true, cancellationToken: Ct);

        // 注文 1 行＋明細 1 行
        deleted.Should().Be(2);
        var lines = await provider
            .GetRequiredService<IOrderLineRepository>()
            .Query()
            .ToListAsync(Ct);
        lines.Select(l => l.LineId.Value).Should().Equal(101);
    }
}
