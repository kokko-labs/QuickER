using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using QuickER.Tests.GeneratedQueryFixture;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>
/// グラフ取得糖衣のランタイムスイートを<b>クエリフィクスチャ（VO 有効）</b>の型で流す派生の共通基底。
/// </summary>
/// <remarks>
/// このフィクスチャは QuickER 版 Repository（SQLite 方言）・EF Core・インメモリの 3 実装を 1 つの namespace へ
/// 同居させているため、シード・値の写し取り・編集といったアダプタは 3 実装先で完全に共有できる。
/// 派生が差し込むのは「保存先のリセット方法」と「リポジトリの解決方法」だけ。
/// </remarks>
public abstract class IncludeGraphQueryFixtureRuntimeTestsBase
    : IncludeGraphRuntimeTestsBase<CustomerEntity, OrderEntity, OrderLineEntity>
{
    /// <summary>保存先（スキーマまたはストア）を空の状態へ戻す</summary>
    protected abstract Task ResetStorageAsync();

    /// <summary>顧客リポジトリを生成する</summary>
    protected abstract ICustomerRepository CreateCustomerRepository();

    /// <summary>注文リポジトリを生成する</summary>
    protected abstract IOrderRepository CreateOrderRepository();

    /// <summary>注文明細リポジトリを生成する</summary>
    protected abstract IOrderLineRepository CreateOrderLineRepository();

    protected override async Task ResetAndSeedAsync()
    {
        await ResetStorageAsync();

        var customers = CreateCustomerRepository();
        var orders = CreateOrderRepository();
        var lines = CreateOrderLineRepository();

        await customers.InsertAsync(NewCustomer(1, "Alice"), Ct);
        await customers.InsertAsync(NewCustomer(2, "Bob"), Ct);
        await customers.InsertAsync(NewCustomer(3, "Carol"), Ct);

        await orders.InsertAsync(NewOrder(10, 1, 100m, "apple pie"), Ct);
        await orders.InsertAsync(NewOrder(11, 1, 50m, null), Ct);
        await orders.InsertAsync(NewOrder(12, 2, 30m, "banana"), Ct);

        await lines.InsertAsync(NewLine(100, 10, "pen", 2), Ct);
        await lines.InsertAsync(NewLine(101, 10, "ink", 5), Ct);
        await lines.InsertAsync(NewLine(102, 12, "mug", 1), Ct);
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

    protected override Task<CustomerEntity?> FetchCustomerWithGraphAsync(int customerId) =>
        CreateCustomerRepository()
            .Query()
            .IncludeGraph()
            .GetByIdAsync(CustomerIdValue.Create(customerId), Ct);

    protected override Task<CustomerEntity?> FetchCustomerWithManualIncludeAsync(int customerId)
    {
        var key = CustomerIdValue.Create(customerId);

        return CreateCustomerRepository()
            .Query()
            .Include(customer => customer.Orders)
                .ThenInclude(order => order.OrderLines)
            .Where(customer => customer.CustomerId == key)
            .FirstOrDefaultAsync(Ct);
    }

    protected override Task<CustomerEntity?> FetchCustomerByIdThroughIncludeChainAsync(
        int customerId
    ) =>
        CreateCustomerRepository()
            .Query()
            .Include(customer => customer.Orders)
            .GetByIdAsync(CustomerIdValue.Create(customerId), Ct);

    protected override async Task<
        IReadOnlyList<CustomerEntity>
    > FetchAllCustomersWithGraphAsync() =>
        await CreateCustomerRepository().Query().IncludeGraph().ToListAsync(Ct);

    protected override async Task<
        IReadOnlyList<OrderLineEntity>
    > FetchAllOrderLinesWithGraphAsync() =>
        await CreateOrderLineRepository().Query().IncludeGraph().ToListAsync(Ct);

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
        CreateCustomerRepository().SaveAsync(customer, cancellationToken: Ct);

    protected override Task<OrderEntity?> FetchOrderWithGraphAndParentAsync(int orderId) =>
        CreateOrderRepository()
            .Query()
            .IncludeGraph()
            .Include(order => order.Customer)
            .GetByIdAsync(OrderIdValue.Create(orderId), Ct);

    protected override string? CustomerNameOf(OrderEntity order) => order.Customer?.Name.Value;
}
