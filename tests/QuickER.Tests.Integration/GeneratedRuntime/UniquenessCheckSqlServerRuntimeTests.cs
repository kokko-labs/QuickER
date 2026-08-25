using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using QuickER.Tests.GeneratedUniquenessSqlServerFixture;
using QuickER.Tests.Integration;
using Xunit;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>
/// 重複事前チェックのランタイムスイートを<b>QuickER 版 Repository（SQL Server 方言）</b>で実 SQL Server
/// （Testcontainers・Docker 依存）に流す派生。
/// </summary>
/// <remarks>
/// <para>
/// 入力は <see cref="UniquenessSqlServerFixtureDefinition"/>（クエリフィクスチャと同一の図・同一の UNIQUE 制約を
/// SQL Server 方言で生成したもの）。判定の共有本体は全実装先で同一テキストだが、SQL への翻訳は方言ごとに違う
/// （SQL Server は FOR JSON 経路・識別子は角括弧）ため、SQLite で緑でも SQL Server で同じ行集合になるとは限らない。
/// バックエンド非依存のシナリオは基底が持ち、本派生は接続だけを差し込む。
/// </para>
/// <para>Docker 不在時は <see cref="SqlServerContainerFixture"/> の検出でスキップされる（CI では常にスキップ）。</para>
/// </remarks>
[Collection(SqlServerContainerCollection.Name)]
[Trait("RequiresDocker", "true")]
public sealed class UniquenessCheckSqlServerRuntimeTests(SqlServerContainerFixture fixture)
    : UniquenessCheckLocalRuntimeTestsBase<OrderEntity>,
        IAsyncLifetime
{
    /// <summary>共有する SQL Server コンテナ</summary>
    private readonly SqlServerContainerFixture _fixture = fixture;

    /// <summary>QuickER の SQL Server リポジトリ群を登録した DI コンテナ</summary>
    private ServiceProvider _provider = null!;

    /// <summary>Docker の有無を判定し、リポジトリ DI を構築する</summary>
    public ValueTask InitializeAsync()
    {
        Assert.SkipUnless(_fixture.IsAvailable, _fixture.UnavailableReason);

        _provider = new ServiceCollection()
            .AddGeneratedSqlServerRepositories(_fixture.ConnectionString)
            .BuildServiceProvider();

        return ValueTask.CompletedTask;
    }

    /// <summary>DI コンテナを破棄する</summary>
    public ValueTask DisposeAsync()
    {
        _provider?.Dispose();

        return ValueTask.CompletedTask;
    }

    /// <summary>注文リポジトリを解決する</summary>
    private IOrderRepository Orders() => _provider.GetRequiredService<IOrderRepository>();

    protected override async Task ResetAndSeedAsync()
    {
        await _fixture.ResetSchemaAsync(Ct);
        await _fixture.ApplyDdlAsync(UniquenessSqlServerFixtureDefinition.Build(), Ct);

        var customers = _provider.GetRequiredService<ICustomerRepository>();
        await customers.InsertAsync(NewCustomer(1, "Alice"), Ct);
        await customers.InsertAsync(NewCustomer(2, "Bob"), Ct);
        await Orders().InsertAsync(NewOrder(10, 1, 100m, "apple pie"), Ct);
        await Orders().InsertAsync(NewOrder(11, 1, 50m, null), Ct);
    }

    /// <summary>顧客エンティティを組み立てる</summary>
    private static CustomerEntity NewCustomer(int id, string name) =>
        new() { CustomerId = CustomerIdValue.Create(id), Name = NameValue.Create(name) };

    protected override OrderEntity NewOrder(
        int orderId,
        int customerId,
        decimal amount,
        string? memo
    ) =>
        new()
        {
            OrderId = OrderIdValue.Create(orderId),
            CustomerId = CustomerIdValue.Create(customerId),
            Amount = AmountValue.Create(amount),
            Memo = memo is null ? null : MemoValue.Create(memo),
        };

    protected override OrderEntity NewOrderWithoutKey(
        int customerId,
        decimal amount,
        string? memo
    ) =>
        new()
        {
            CustomerId = CustomerIdValue.Create(customerId),
            Amount = AmountValue.Create(amount),
            Memo = memo is null ? null : MemoValue.Create(memo),
        };

    protected override void AssertKeyIsUnset(OrderEntity candidate) =>
        candidate.OrderId.Should().BeNull("挿入前のエンティティは主キーを持たない");

    protected override Task<OrderEntity?> GetOrderAsync(int orderId) =>
        Orders().GetByIdAsync(OrderIdValue.Create(orderId), Ct);

    protected override async Task<IReadOnlyList<UniquenessViolationRow>> CheckUniquenessAsync(
        OrderEntity candidate
    ) =>
        (await Orders().CheckUniquenessAsync(candidate, Ct))
            .Select(v => new UniquenessViolationRow(v.ConstraintName, v.PropertyNames, v.Message))
            .ToList();

    protected override decimal CustomCheckAmount => OrderRepository.ReservedAmount;

    protected override string CustomCheckConstraintName => OrderRepository.CustomConstraintName;

    protected override string? CustomCheckMessage => OrderRepository.CustomMessage;

    protected override async Task<IReadOnlyList<int>> OrderIdsWhereMemoEqualsNullVariableAsync()
    {
        MemoValue? missing = null;

        var rows = await Orders().Query().Where(o => o.Memo == missing).ToListAsync(Ct);
        return rows.Select(o => o.OrderId.Value).ToList();
    }

    protected override async Task<IReadOnlyList<int>> OrderIdsWhereMemoNotEqualsNullVariableAsync()
    {
        MemoValue? missing = null;

        var rows = await Orders().Query().Where(o => o.Memo != missing).ToListAsync(Ct);
        return rows.Select(o => o.OrderId.Value).ToList();
    }

    protected override async Task<IReadOnlyList<int>> OrderIdsWhereNotMemoEqualsAsync(string memo)
    {
        var value = MemoValue.Create(memo);

        var rows = await Orders().Query().Where(o => !(o.Memo == value)).ToListAsync(Ct);
        return rows.Select(o => o.OrderId.Value).ToList();
    }

    protected override async Task<IReadOnlyList<int>> OrderIdsWhereMemoNotEqualsAsync(string memo)
    {
        var value = MemoValue.Create(memo);

        var rows = await Orders().Query().Where(o => o.Memo != value).ToListAsync(Ct);
        return rows.Select(o => o.OrderId.Value).ToList();
    }
}
