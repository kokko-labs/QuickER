using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace QuickER.Tests.GeneratedInMemoryFixture;

/// <summary>
/// Save フックの登録手段 2 つ（DI なしの <see cref="SaveHookRegistry"/> と、DI 向けの
/// <c>IServiceCollection.AddSaveHook</c>）が生成コードに存在し、実際にフックを発火させることを検証する。
/// </summary>
/// <remarks>
/// <para>
/// 生成 Repository は <c>ISaveHookRegistry? saveHooks = null</c> をコンストラクタで直接受ける（手動 new が公式経路）
/// ため、フック 1 個を渡すために DI コンテナを建てる必要がない形（<see cref="SaveHookRegistry"/>）を検証する。
/// </para>
/// <para>
/// <c>AddSaveHook</c> は「フックが実装している全 <c>ISaveHook&lt;T&gt;</c> を実体から導く」ため、登録側に
/// エンティティ型ごとの分岐が要らない（テーブルが増えても登録コードが壊れない）ことを、2 エンティティ型を
/// 同時に対象とする 1 個のフックで固定する。
/// </para>
/// <para>実 DB を使わないインメモリ実装のため Docker 不要＝CI 常時実行。</para>
/// </remarks>
public sealed class SaveHookRegistrationRuntimeTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    /// <summary>発火した Before / After を記録するだけのフック（対象は 1 エンティティ型）</summary>
    private sealed class LoggingHook<TEntity>(string name, List<string> log) : ISaveHook<TEntity>
        where TEntity : EntityBase
    {
        public Task<bool> BeforeSaveAsync(
            TEntity entity,
            SaveOperation operation,
            CancellationToken cancellationToken = default
        )
        {
            log.Add($"{name}:before:{operation}");
            return Task.FromResult(true);
        }

        public Task AfterSaveAsync(
            TEntity entity,
            SaveOperation operation,
            ISaveHookContext context,
            CancellationToken cancellationToken = default
        )
        {
            log.Add($"{name}:after:{operation}");
            return Task.CompletedTask;
        }
    }

    /// <summary>2 つのエンティティ型を同時に対象とするフック（型分岐なし登録の検証用）</summary>
    private sealed class MultiEntityHook(List<string> log)
        : ISaveHook<CustomerEntity>,
            ISaveHook<OrderEntity>
    {
        public Task<bool> BeforeSaveAsync(
            CustomerEntity entity,
            SaveOperation operation,
            CancellationToken cancellationToken = default
        )
        {
            log.Add($"customer:{operation}");
            return Task.FromResult(true);
        }

        public Task<bool> BeforeSaveAsync(
            OrderEntity entity,
            SaveOperation operation,
            CancellationToken cancellationToken = default
        )
        {
            log.Add($"order:{operation}");
            return Task.FromResult(true);
        }
    }

    /// <summary>どの <c>ISaveHook&lt;T&gt;</c> も実装していないただのオブジェクト（AddSaveHook の拒否検証用）</summary>
    private sealed class NotAHook { }

    private static CustomerEntity NewCustomer(int id, string name) =>
        new()
        {
            CustomerId = id,
            Name = name,
            RowState = RowState.Added,
        };

    // ── 1. DI なしの SaveHookRegistry ──

    /// <summary>手動 new した Repository へ <see cref="SaveHookRegistry"/> を直接渡すとフックが発火する</summary>
    [Fact(DisplayName = "[SaveHookRegistry] DI なしで Repository へ直接渡すとフックが発火する")]
    public async Task SaveHookRegistry_FiresHook_WithoutDiContainer()
    {
        var log = new List<string>();
        var store = new InMemoryDataStore();
        var customers = new InMemoryCustomerRepository(
            store,
            new SaveHookRegistry().Add(new LoggingHook<CustomerEntity>("h", log))
        );

        var customer = NewCustomer(1, "Alice");
        (await customers.SaveAsync(customer, cancellationToken: Ct)).Should().Be(1);

        log.Should()
            .Equal(
                ["h:before:Insert", "h:after:Insert"],
                "DI コンテナを建てずにフックが Before / After とも発火する"
            );
        (await new InMemoryCustomerRepository(store).GetByIdAsync(1, Ct))
            .Should()
            .NotBeNull("フック経由でも保存自体は通常どおり行われる");
    }

    /// <summary>同じ型へ複数のフックを追加すると登録順に発火し、Add はチェーンできる</summary>
    [Fact(DisplayName = "[SaveHookRegistry] 複数フックは登録順に発火し Add はチェーンできる")]
    public async Task SaveHookRegistry_InvokesHooksInRegistrationOrder()
    {
        var log = new List<string>();
        var store = new InMemoryDataStore();
        var registry = new SaveHookRegistry()
            .Add(new LoggingHook<CustomerEntity>("first", log))
            .Add(new LoggingHook<CustomerEntity>("second", log));

        var customers = new InMemoryCustomerRepository(store, registry);
        await customers.SaveAsync(NewCustomer(1, "Alice"), cancellationToken: Ct);

        log.Should()
            .Equal(
                [
                    "first:before:Insert",
                    "second:before:Insert",
                    "first:after:Insert",
                    "second:after:Insert",
                ],
                "Before も After も追加順（ServiceProviderSaveHookRegistry と同じ規約）"
            );
    }

    /// <summary>フックを追加していない型は invoker が null＝完全 no-op（登録済みの型だけが発火する）</summary>
    [Fact(DisplayName = "[SaveHookRegistry] 未登録の型は invoker が null で完全 no-op")]
    public async Task SaveHookRegistry_ReturnsNullInvoker_ForUnregisteredType()
    {
        var log = new List<string>();
        var registry = new SaveHookRegistry().Add(new LoggingHook<CustomerEntity>("h", log));

        registry.GetInvoker(typeof(CustomerEntity)).Should().NotBeNull();
        registry.GetInvoker(typeof(OrderEntity)).Should().BeNull("登録のない型は null＝no-op");

        // 実際に注文を保存してもフックは 1 件も記録されない
        var store = new InMemoryDataStore();
        var order = new OrderEntity
        {
            OrderId = 1,
            CustomerId = 1,
            Amount = 10m,
            RowState = RowState.Added,
        };
        await new InMemoryOrderRepository(store, registry).SaveAsync(order, cancellationToken: Ct);

        log.Should().BeEmpty();
    }

    /// <summary>null のフックは追加できない</summary>
    [Fact(DisplayName = "[SaveHookRegistry] null のフックは ArgumentNullException")]
    public void SaveHookRegistry_RejectsNullHook()
    {
        var add = () => new SaveHookRegistry().Add<CustomerEntity>(null!);
        add.Should().Throw<ArgumentNullException>();
    }

    // ── 2. AddSaveHook（非ジェネリックな DI 登録） ──

    /// <summary>1 個のフックが対象とする全エンティティ型へ、型分岐なしで登録される</summary>
    [Fact(DisplayName = "[AddSaveHook] 型分岐なしで全 ISaveHook<T> 面へ登録され双方が発火する")]
    public async Task AddSaveHook_RegistersEveryImplementedHookInterface()
    {
        var log = new List<string>();
        var services = new ServiceCollection();
        services.AddGeneratedInMemoryRepositories(seedSampleData: false);
        services.AddSaveHook(new MultiEntityHook(log));

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        await scope
            .ServiceProvider.GetRequiredService<ICustomerRepository>()
            .SaveAsync(NewCustomer(1, "Alice"), cancellationToken: Ct);

        var order = new OrderEntity
        {
            OrderId = 1,
            CustomerId = 1,
            Amount = 10m,
            RowState = RowState.Added,
        };
        await scope
            .ServiceProvider.GetRequiredService<IOrderRepository>()
            .SaveAsync(order, cancellationToken: Ct);

        log.Should()
            .Equal(
                ["customer:Insert", "order:Insert"],
                "1 回の登録で 2 つのエンティティ型のフック面が有効になる"
            );
    }

    /// <summary>同じインスタンスが両方の面で解決される（登録は面ごとだが実体は 1 つ）</summary>
    [Fact(DisplayName = "[AddSaveHook] 登録した実体が全ての面で同一インスタンスとして解決される")]
    public void AddSaveHook_ResolvesTheSameInstanceForEveryInterface()
    {
        var hook = new MultiEntityHook([]);
        using var provider = new ServiceCollection().AddSaveHook(hook).BuildServiceProvider();

        provider.GetRequiredService<ISaveHook<CustomerEntity>>().Should().BeSameAs(hook);
        provider.GetRequiredService<ISaveHook<OrderEntity>>().Should().BeSameAs(hook);
    }

    /// <summary>ISaveHook を 1 つも実装しないオブジェクトは黙って無視せず ArgumentException で弾く</summary>
    [Fact(DisplayName = "[AddSaveHook] ISaveHook を実装しないオブジェクトは ArgumentException")]
    public void AddSaveHook_RejectsObjectThatImplementsNoHookInterface()
    {
        var register = () => new ServiceCollection().AddSaveHook(new NotAHook());

        register
            .Should()
            .Throw<ArgumentException>()
            .WithParameterName("hook")
            .WithMessage("*NotAHook*");
    }
}
