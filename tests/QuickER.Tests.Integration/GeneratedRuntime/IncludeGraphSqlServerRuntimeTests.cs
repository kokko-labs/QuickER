using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using QuickER.Tests.GeneratedUniquenessSqlServerFixture;
using QuickER.Tests.Integration;
using Xunit;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>
/// グラフ取得糖衣のランタイムスイートを<b>QuickER 版 Repository（SQL Server 方言）</b>で実 SQL Server
/// （Testcontainers・Docker 依存）に流す派生。
/// </summary>
/// <remarks>
/// <para>
/// 入力は <see cref="UniquenessSqlServerFixtureDefinition"/>（クエリフィクスチャと同一の図を SQL Server 方言で
/// 生成したもの）。SQL Server の Include は<b>単一クエリ＋FOR JSON</b>で、SQLite のマルチクエリとも
/// EF Core の変換とも別実装である。とくに入れ子 JSON は「同名のプロパティが後勝ちで潰れる」形の壊れ方をするため、
/// 3 階層目まで組み立てられることは実 SQL Server でしか確かめられない。
/// </para>
/// <para>
/// 型がフィクスチャごとに別 namespace になるため、アダプタはクエリフィクスチャ側と同じ内容を SQL Server 版の型で
/// 書き下す（<see cref="UniquenessCheckSqlServerRuntimeTests"/> と同じ流儀）。
/// </para>
/// <para>Docker 不在時は <see cref="SqlServerContainerFixture"/> の検出でスキップされる。</para>
/// </remarks>
[Collection(SqlServerContainerCollection.Name)]
[Trait("RequiresDocker", "true")]
public sealed class IncludeGraphSqlServerRuntimeTests(SqlServerContainerFixture fixture)
    : IncludeGraphRuntimeTestsBase<CustomerEntity, OrderEntity, OrderLineEntity>,
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

    /// <summary>顧客リポジトリを解決する</summary>
    private ICustomerRepository Customers() => _provider.GetRequiredService<ICustomerRepository>();

    /// <summary>注文リポジトリを解決する</summary>
    private IOrderRepository Orders() => _provider.GetRequiredService<IOrderRepository>();

    /// <summary>注文明細リポジトリを解決する</summary>
    private IOrderLineRepository OrderLines() =>
        _provider.GetRequiredService<IOrderLineRepository>();

    protected override async Task ResetAndSeedAsync()
    {
        await _fixture.ResetSchemaAsync(Ct);
        await _fixture.ApplyDdlAsync(UniquenessSqlServerFixtureDefinition.Build(), Ct);

        await Customers().InsertAsync(NewCustomer(1, "Alice"), Ct);
        await Customers().InsertAsync(NewCustomer(2, "Bob"), Ct);
        await Customers().InsertAsync(NewCustomer(3, "Carol"), Ct);

        await Orders().InsertAsync(NewOrder(10, 1, 100m, "apple pie"), Ct);
        await Orders().InsertAsync(NewOrder(11, 1, 50m, null), Ct);
        await Orders().InsertAsync(NewOrder(12, 2, 30m, "banana"), Ct);

        await OrderLines().InsertAsync(NewLine(100, 10, "pen", 2), Ct);
        await OrderLines().InsertAsync(NewLine(101, 10, "ink", 5), Ct);
        await OrderLines().InsertAsync(NewLine(102, 12, "mug", 1), Ct);
    }

    /// <summary>顧客エンティティを組み立てる</summary>
    private static CustomerEntity NewCustomer(int id, string name) =>
        new() { CustomerId = CustomerIdValue.Create(id), Name = NameValue.Create(name) };

    /// <summary>注文エンティティを組み立てる</summary>
    private static OrderEntity NewOrder(
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

    /// <summary>注文明細エンティティを組み立てる</summary>
    private static OrderLineEntity NewLine(
        int lineId,
        int orderId,
        string itemName,
        int quantity
    ) =>
        new()
        {
            LineId = LineIdValue.Create(lineId),
            OrderId = OrderIdValue.Create(orderId),
            ItemName = ItemNameValue.Create(itemName),
            Quantity = QuantityValue.Create(quantity),
        };

    protected override Task<CustomerEntity?> FetchCustomerWithGraphAsync(int customerId)
    {
        var key = CustomerIdValue.Create(customerId);

        return Customers()
            .Query()
            .IncludeGraph()
            .Where(customer => customer.CustomerId == key)
            .FirstOrDefaultAsync(Ct);
    }

    protected override Task<CustomerEntity?> FetchCustomerWithManualIncludeAsync(int customerId)
    {
        var key = CustomerIdValue.Create(customerId);

        return Customers()
            .Query()
            .Include(customer => customer.Orders)
                .ThenInclude(order => order.OrderLines)
            .Where(customer => customer.CustomerId == key)
            .FirstOrDefaultAsync(Ct);
    }

    protected override async Task<
        IReadOnlyList<CustomerEntity>
    > FetchAllCustomersWithGraphAsync() => await Customers().Query().IncludeGraph().ToListAsync(Ct);

    protected override async Task<
        IReadOnlyList<OrderLineEntity>
    > FetchAllOrderLinesWithGraphAsync() =>
        await OrderLines().Query().IncludeGraph().ToListAsync(Ct);

    protected override GraphCustomerRow Project(CustomerEntity customer) =>
        new(
            customer.CustomerId.Value,
            customer.Name.Value,
            customer.RowState.ToString(),
            OrdersOf(customer)
                .Select(order => new GraphOrderRow(
                    order.OrderId.Value,
                    order.Amount.Value,
                    order.Memo?.Value,
                    order.RowState.ToString(),
                    LinesOf(order)
                        .Select(line => new GraphLineRow(
                            line.LineId.Value,
                            line.ItemName.Value,
                            line.Quantity.Value,
                            line.RowState.ToString()
                        ))
                        .ToList()
                ))
                .ToList()
        );

    protected override int LineIdOf(OrderLineEntity line) => line.LineId.Value;

    protected override IReadOnlyList<OrderEntity> OrdersOf(CustomerEntity customer) =>
        customer.Orders.OrderBy(order => order.OrderId.Value).ToList();

    protected override IReadOnlyList<OrderLineEntity> LinesOf(OrderEntity order) =>
        order.OrderLines.OrderBy(line => line.LineId.Value).ToList();

    protected override void AddLine(OrderEntity order, int lineId, string itemName, int quantity)
    {
        var line = NewLine(lineId, order.OrderId.Value, itemName, quantity);
        line.MarkAdded();
        order.OrderLines.Add(line);
    }

    protected override void ChangeLineQuantity(OrderLineEntity line, int quantity)
    {
        line.Quantity = QuantityValue.Create(quantity);
        line.MarkUpdated();
    }

    protected override void RemoveLine(OrderLineEntity line) => line.MarkRemoved();

    protected override Task<int> SaveCustomerAsync(CustomerEntity customer) =>
        Customers().SaveAsync(customer, cancellationToken: Ct);
}
