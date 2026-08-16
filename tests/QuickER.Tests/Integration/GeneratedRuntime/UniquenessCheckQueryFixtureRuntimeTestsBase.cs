using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AwesomeAssertions;
using QuickER.Tests.GeneratedQueryFixture;
using QuickER.Tests.Integration;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>
/// 重複事前チェックのランタイムスイートを<b>クエリフィクスチャ（SQLite 方言・VO 有効）</b>で流す派生の共通基底。
/// QuickER の <c>SqliteRepository</c> 版と EF Core Sqlite 版がリポジトリ生成だけを差し込む。
/// </summary>
/// <remarks>
/// 実 SQLite（一時ファイル DB）を使うため Docker 不要＝CI 常時実行。フィクスチャの orders には
/// 単一列制約 <c>UQ_orders_memo</c>（NULL 許容列）と複合制約（<c>customer_id</c>＋<c>amount</c>・名前なし＝合成名）がある。
/// </remarks>
public abstract class UniquenessCheckQueryFixtureRuntimeTestsBase
    : UniquenessCheckLocalRuntimeTestsBase<OrderEntity>,
        IDisposable
{
    /// <summary>各テストが読み書きする一時ファイル DB</summary>
    private readonly SqliteTempDatabase _db = SqliteTempDatabase.Create();

    /// <summary>書き込み可能な接続文字列（バックエンドはこの実ファイルへ読み書きする）</summary>
    protected string ConnectionString => _db.ReadWriteCreateConnectionString;

    /// <summary>顧客リポジトリを生成する</summary>
    protected abstract ICustomerRepository CreateCustomerRepository();

    /// <summary>注文リポジトリを生成する</summary>
    protected abstract IOrderRepository CreateOrderRepository();

    protected override async Task ResetAndSeedAsync()
    {
        await _db.ResetSchemaAsync(Ct);
        await _db.ApplyDdlAsync(QueryFixtureDefinition.Build(), Ct);

        var customers = CreateCustomerRepository();
        var orders = CreateOrderRepository();

        await customers.InsertAsync(NewCustomer(1, "Alice"), Ct);
        await customers.InsertAsync(NewCustomer(2, "Bob"), Ct);
        await orders.InsertAsync(NewOrder(10, 1, 100m, "apple pie"), Ct);
        await orders.InsertAsync(NewOrder(11, 1, 50m, null), Ct);
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
        CreateOrderRepository().GetByIdAsync(OrderIdValue.Create(orderId), Ct);

    protected override async Task<IReadOnlyList<UniquenessViolationRow>> CheckUniquenessAsync(
        OrderEntity candidate
    ) =>
        (await CreateOrderRepository().CheckUniquenessAsync(candidate, Ct))
            .Select(v => new UniquenessViolationRow(v.ConstraintName, v.PropertyNames, v.Message))
            .ToList();

    protected override decimal CustomCheckAmount => OrderUniquenessCustomCheck.ReservedAmount;

    protected override string CustomCheckConstraintName =>
        OrderUniquenessCustomCheck.ConstraintName;

    protected override string? CustomCheckMessage => OrderUniquenessCustomCheck.Message;

    protected override async Task<IReadOnlyList<int>> OrderIdsWhereMemoEqualsNullVariableAsync()
    {
        MemoValue? missing = null;

        var rows = await CreateOrderRepository()
            .Query()
            .Where(o => o.Memo == missing)
            .ToListAsync(Ct);
        return rows.Select(o => o.OrderId.Value).ToList();
    }

    protected override async Task<IReadOnlyList<int>> OrderIdsWhereMemoNotEqualsNullVariableAsync()
    {
        MemoValue? missing = null;

        var rows = await CreateOrderRepository()
            .Query()
            .Where(o => o.Memo != missing)
            .ToListAsync(Ct);
        return rows.Select(o => o.OrderId.Value).ToList();
    }

    protected override async Task<IReadOnlyList<int>> OrderIdsWhereNotMemoEqualsAsync(string memo)
    {
        var value = MemoValue.Create(memo);

        var rows = await CreateOrderRepository()
            .Query()
            .Where(o => !(o.Memo == value))
            .ToListAsync(Ct);
        return rows.Select(o => o.OrderId.Value).ToList();
    }

    protected override async Task<IReadOnlyList<int>> OrderIdsWhereMemoNotEqualsAsync(string memo)
    {
        var value = MemoValue.Create(memo);

        var rows = await CreateOrderRepository()
            .Query()
            .Where(o => o.Memo != value)
            .ToListAsync(Ct);
        return rows.Select(o => o.OrderId.Value).ToList();
    }

    /// <summary>一時 DB を破棄する</summary>
    public virtual void Dispose()
    {
        _db.Dispose();
        GC.SuppressFinalize(this);
    }
}
