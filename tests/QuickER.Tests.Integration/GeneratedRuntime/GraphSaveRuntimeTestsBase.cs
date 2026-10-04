using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using QuickER.Tests.GeneratedQueryFixture;
using Xunit;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>
/// グラフ保存（<c>SaveAsync</c>）が「カスケードの閉包だけ」を保存することを、実装先をまたいで同じ結果で固定する共通基底。
/// </summary>
/// <remarks>
/// <para>
/// グラフ保存が書くのは、根から <c>EnumerateCascadeChildren</c> で辿れる子孫だけで、親への逆参照
/// （<c>line.Order</c>・<c>order.Customer</c>）は辿らない。逆参照まで辿ると、子を 1 件削除したときに親と兄弟まで削除し、
/// 別インスタンスの同じ親を 2 つ持つグラフで追跡が衝突し、保存を頼んでいない親を書くことになる。
/// ここの各シナリオは QuickER 版（SQLite 実ファイル）を基準に、EF Core（SQLite 実ファイル・SQL Server）と
/// インメモリが同じ結果になることを表明する。
/// </para>
/// <para>
/// 入力はクエリフィクスチャ（customers → orders → order_lines の 3 階層・VO 有効）。派生が差し込むのは
/// 保存先のリセット方法とリポジトリの解決方法だけ。
/// </para>
/// </remarks>
public abstract class GraphSaveRuntimeTestsBase
{
    /// <summary>テストのキャンセルトークン</summary>
    protected static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    /// <summary>保存先（スキーマまたはストア）を空の状態へ戻す</summary>
    protected abstract Task ResetStorageAsync();

    /// <summary>顧客リポジトリを生成する</summary>
    protected abstract ICustomerRepository CreateCustomerRepository();

    /// <summary>注文リポジトリを生成する</summary>
    protected abstract IOrderRepository CreateOrderRepository();

    /// <summary>注文明細リポジトリを生成する</summary>
    protected abstract IOrderLineRepository CreateOrderLineRepository();

    /// <summary>空の保存先へ顧客 3・注文 3・明細 3 を投入する</summary>
    private async Task ResetAndSeedAsync()
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

    [Fact(
        DisplayName = "グラフ保存: 親のコレクション経由で読んだ明細 1 件を明細から保存すると、その 1 行だけが消える"
    )]
    public async Task DeletingChildLoadedThroughParent_DeletesOnlyThatChild()
    {
        await ResetAndSeedAsync();

        var order = await CreateOrderRepository()
            .Query()
            .Include(o => o.OrderLines)
            .GetByIdAsync(OrderIdValue.Create(10), Ct);
        var line = order!.OrderLines.Single(l => l.LineId.Value == 100);
        line.MarkRemoved();

        var rows = await CreateOrderLineRepository().SaveAsync(line, cancellationToken: Ct);

        rows.Should().Be(1);
        (await OrderIdsAsync()).Should().Equal(10, 11, 12);
        (await LineIdsAsync()).Should().Equal(101, 102);
    }

    [Fact(
        DisplayName = "グラフ保存: 親を Include して読んだ明細を削除して保存しても、親は消えない"
    )]
    public async Task DeletingChildWithParentIncluded_KeepsParent()
    {
        await ResetAndSeedAsync();

        var line = await CreateOrderLineRepository()
            .Query()
            .Include(l => l.Order)
            .GetByIdAsync(LineIdValue.Create(100), Ct);
        line!.Order.Should().NotBeNull();
        line.MarkRemoved();

        var rows = await CreateOrderLineRepository().SaveAsync(line, cancellationToken: Ct);

        rows.Should().Be(1);
        (await OrderIdsAsync()).Should().Equal(10, 11, 12);
        (await LineIdsAsync()).Should().Equal(101, 102);
    }

    [Fact(DisplayName = "グラフ保存: 注文を根に明細 1 件を削除して保存すると、兄弟の明細は残る")]
    public async Task DeletingChildThroughRoot_KeepsSiblings()
    {
        await ResetAndSeedAsync();

        var order = await CreateOrderRepository()
            .Query()
            .Include(o => o.OrderLines)
            .GetByIdAsync(OrderIdValue.Create(10), Ct);
        order!.OrderLines.Single(l => l.LineId.Value == 101).MarkRemoved();

        var rows = await CreateOrderRepository().SaveAsync(order, cancellationToken: Ct);

        rows.Should().Be(1);
        (await OrderIdsAsync()).Should().Equal(10, 11, 12);
        (await LineIdsAsync()).Should().Equal(100, 102);
    }

    [Fact(DisplayName = "グラフ保存: 同じ顧客を別インスタンスで持つ注文 2 件をまとめて保存できる")]
    public async Task SavingRootsSharingParentKey_Succeeds()
    {
        await ResetAndSeedAsync();

        var first = await FetchOrderWithCustomerAsync(10);
        var second = await FetchOrderWithCustomerAsync(11);
        first.Customer.Should().NotBeSameAs(second.Customer);

        first.Memo = MemoValue.Create("first");
        first.MarkUpdated();
        second.Memo = MemoValue.Create("second");
        second.MarkUpdated();

        var rows = await CreateOrderRepository().SaveAsync([first, second], cancellationToken: Ct);

        rows.Should().Be(2);
        (await MemoOfAsync(10)).Should().Be("first");
        (await MemoOfAsync(11)).Should().Be("second");
    }

    [Fact(DisplayName = "グラフ保存: Include した親の変更は書かず、親の RowState も触らない")]
    public async Task SavingChild_DoesNotWriteParent()
    {
        await ResetAndSeedAsync();

        var order = await FetchOrderWithCustomerAsync(10);
        order.Customer.Name = NameValue.Create("Changed");
        order.Customer.MarkUpdated();
        order.Memo = MemoValue.Create("edited");
        order.MarkUpdated();

        var rows = await CreateOrderRepository().SaveAsync(order, cancellationToken: Ct);

        rows.Should().Be(1);
        order.RowState.Should().Be(RowState.Unchanged);
        order.Customer.RowState.Should().Be(RowState.Updated);
        (await MemoOfAsync(10)).Should().Be("edited");

        var customer = await CreateCustomerRepository().GetByIdAsync(CustomerIdValue.Create(1), Ct);
        customer!.Name.Value.Should().Be("Alice");
    }

    [Fact(
        DisplayName = "グラフ保存: cascadeSave=false でも削除の根の子孫は cascadeDelete で子から消える"
    )]
    public async Task DeletingRootWithoutCascadeSave_DeletesDescendants()
    {
        await ResetAndSeedAsync();

        var order = await CreateOrderRepository()
            .Query()
            .Include(o => o.OrderLines)
            .GetByIdAsync(OrderIdValue.Create(10), Ct);
        order!.MarkRemoved();

        var rows = await CreateOrderRepository()
            .SaveAsync(order, cascadeSave: false, cascadeDelete: true, cancellationToken: Ct);

        // 注文 1 行＋明細 2 行（QuickER 版は子から順に DELETE し、件数へ数える）
        rows.Should().Be(3);
        (await OrderIdsAsync()).Should().Equal(11, 12);
        (await LineIdsAsync()).Should().Equal(102);
    }

    /// <summary>顧客を Include して注文 1 件を読む（呼び出しごとに別インスタンスの顧客が付く）</summary>
    private async Task<OrderEntity> FetchOrderWithCustomerAsync(int orderId)
    {
        var order = await CreateOrderRepository()
            .Query()
            .Include(o => o.Customer)
            .GetByIdAsync(OrderIdValue.Create(orderId), Ct);
        order!.Customer.Should().NotBeNull();
        return order;
    }

    /// <summary>注文のメモを保存先から読み直す</summary>
    private async Task<string?> MemoOfAsync(int orderId) =>
        (await CreateOrderRepository().GetByIdAsync(OrderIdValue.Create(orderId), Ct))!.Memo?.Value;

    /// <summary>保存先に残っている注文の ID（昇順）</summary>
    private async Task<IReadOnlyList<int>> OrderIdsAsync() =>
        (await CreateOrderRepository().Query().ToListAsync(Ct))
            .Select(o => o.OrderId.Value)
            .Order()
            .ToList();

    /// <summary>保存先に残っている明細の ID（昇順）</summary>
    private async Task<IReadOnlyList<int>> LineIdsAsync() =>
        (await CreateOrderLineRepository().Query().ToListAsync(Ct))
            .Select(l => l.LineId.Value)
            .Order()
            .ToList();

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
}
