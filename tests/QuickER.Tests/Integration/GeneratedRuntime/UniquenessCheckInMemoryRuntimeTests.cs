using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AwesomeAssertions;
using QuickER.Tests.GeneratedInMemoryFixture;
using Xunit;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>
/// 重複事前チェックのランタイムスイートを<b>インメモリ Repository</b>（<see cref="InMemoryDataStore"/> 共有）で
/// 流す派生。実 DB を使わないため Docker 不要＝CI 常時実行。
/// </summary>
/// <remarks>
/// <para>
/// 判定の共有本体（式木クエリ 1 本）はQuickER 版・EF Core 版と同一テキストで出力されるため、ここでは
/// インメモリ実行器がその式木を同じ意味論で評価できること（<b>値オブジェクト無効</b>の図でも同様であること）を確かめる。
/// フィクスチャの orders には単一列制約 <c>UQ_orders_memo</c>（NULL 許容列）と複合制約
/// （<c>customer_id</c>＋<c>amount</c>・名前なし＝合成名）がある。
/// </para>
/// <para>
/// 本フィクスチャの主キーは非 NULL の <c>int</c>（＝未設定は既定値 0）なので、自分自身の除外は
/// 無条件に連なる。VO／string 主キーで null になる経路と<b>同じ観測結果</b>になることを固定する。
/// </para>
/// </remarks>
public sealed class UniquenessCheckInMemoryRuntimeTests
    : UniquenessCheckLocalRuntimeTestsBase<OrderEntity>
{
    /// <summary>全リポジトリで共有するインメモリストア</summary>
    private readonly InMemoryDataStore _store = new();

    /// <summary>注文リポジトリ（シード済みストアを共有する）</summary>
    private InMemoryOrderRepository Orders => new(_store);

    protected override async Task ResetAndSeedAsync()
    {
        _store.Clear();

        await Orders.InsertAsync(NewOrder(10, 1, 100m, "apple pie"), Ct);
        await Orders.InsertAsync(NewOrder(11, 1, 50m, null), Ct);
    }

    /// <summary>注文エンティティを組み立てる（インメモリフィクスチャは値オブジェクト無効）</summary>
    protected override OrderEntity NewOrder(
        int orderId,
        int customerId,
        decimal amount,
        string? memo
    ) =>
        new()
        {
            OrderId = orderId,
            CustomerId = customerId,
            Amount = amount,
            Memo = memo,
        };

    protected override OrderEntity NewOrderWithoutKey(
        int customerId,
        decimal amount,
        string? memo
    ) =>
        new()
        {
            CustomerId = customerId,
            Amount = amount,
            Memo = memo,
        };

    protected override void AssertKeyIsUnset(OrderEntity candidate) =>
        candidate
            .OrderId.Should()
            .Be(0, "挿入前のエンティティは主キーを持たない（非 NULL の int は既定値）");

    protected override Task<OrderEntity?> GetOrderAsync(int orderId) =>
        Orders.GetByIdAsync(orderId, Ct);

    protected override async Task<IReadOnlyList<UniquenessViolationRow>> CheckUniquenessAsync(
        OrderEntity candidate
    ) =>
        (await Orders.CheckUniquenessAsync(candidate, Ct))
            .Select(v => new UniquenessViolationRow(v.ConstraintName, v.PropertyNames, v.Message))
            .ToList();

    protected override decimal CustomCheckAmount => InMemoryOrderRepository.ReservedAmount;

    protected override string CustomCheckConstraintName =>
        InMemoryOrderRepository.CustomConstraintName;

    /// <summary>本フィクスチャのフックはメッセージを指定しない（制約名だけを返す枝の担い手）</summary>
    protected override string? CustomCheckMessage => null;

    protected override async Task<IReadOnlyList<int>> OrderIdsWhereMemoEqualsNullVariableAsync()
    {
        string? missing = null;

        var rows = await Orders.Query().Where(o => o.Memo == missing).ToListAsync(Ct);
        return rows.Select(o => o.OrderId).ToList();
    }

    protected override async Task<IReadOnlyList<int>> OrderIdsWhereMemoNotEqualsNullVariableAsync()
    {
        string? missing = null;

        var rows = await Orders.Query().Where(o => o.Memo != missing).ToListAsync(Ct);
        return rows.Select(o => o.OrderId).ToList();
    }

    protected override async Task<IReadOnlyList<int>> OrderIdsWhereNotMemoEqualsAsync(string memo)
    {
        var rows = await Orders.Query().Where(o => !(o.Memo == memo)).ToListAsync(Ct);
        return rows.Select(o => o.OrderId).ToList();
    }

    protected override async Task<IReadOnlyList<int>> OrderIdsWhereMemoNotEqualsAsync(string memo)
    {
        var rows = await Orders.Query().Where(o => o.Memo != memo).ToListAsync(Ct);
        return rows.Select(o => o.OrderId).ToList();
    }
}
