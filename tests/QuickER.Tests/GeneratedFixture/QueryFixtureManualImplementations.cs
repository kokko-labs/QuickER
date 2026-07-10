using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace QuickER.Tests.GeneratedQueryFixture;

// 名前付きクエリの manual／実装が生成されない実装先（EF Core の自由 SQL 分）を、
// 利用者がそうするのと同じ形（partial クラス）で実装する。
// 実装漏れはこのテストプロジェクトのコンパイルエラーとして検出される＝統一規則の実証を兼ねる。

/// <summary>manual クエリ（SpecialLookup）の自作 Repository 側実装</summary>
public sealed partial class OrderRepository
{
    /// <summary>顧客IDに紐づく注文のうち最初の 1 件を返す（manual 実装の見本）</summary>
    public Task<OrderEntity?> SpecialLookupAsync(
        int customerId,
        CancellationToken cancellationToken = default
    ) =>
        Query()
            .Where(e => e.CustomerId == CustomerIdValue.Create(customerId))
            .OrderBy(e => e.OrderId)
            .FirstOrDefaultAsync(cancellationToken);
}

/// <summary>EF Core 実装で生成されないメンバー（自由 SQL 2 件＋manual 1 件）の partial 実装</summary>
public sealed partial class EfCoreOrderRepository
{
    /// <summary>顧客IDに紐づく注文金額の合計（該当なしは null。自作側の SUM と同じ意味論）</summary>
    public async Task<decimal?> SumAmountsAsync(
        int customerId,
        CancellationToken cancellationToken = default
    )
    {
        var items = await Query()
            .Where(e => e.CustomerId == CustomerIdValue.Create(customerId))
            .ToListAsync(cancellationToken);
        return items.Count == 0 ? null : items.Sum(e => e.Amount!.Value);
    }

    /// <summary>注文IDの一覧で注文を取得する（自作側の自由 SQL と同じ意味論）</summary>
    public Task<IReadOnlyList<OrderEntity>> GetByIdsRawAsync(
        IReadOnlyList<int> ids,
        CancellationToken cancellationToken = default
    )
    {
        var idsValues = ids.Select(OrderIdValue.Create).ToList();
        return Query()
            .Where(e => idsValues.Contains(e.OrderId))
            .OrderBy(e => e.OrderId)
            .ToListAsync(cancellationToken);
    }

    /// <summary>顧客IDに紐づく注文のうち最初の 1 件を返す（manual 実装の見本）</summary>
    public Task<OrderEntity?> SpecialLookupAsync(
        int customerId,
        CancellationToken cancellationToken = default
    ) =>
        Query()
            .Where(e => e.CustomerId == CustomerIdValue.Create(customerId))
            .OrderBy(e => e.OrderId)
            .FirstOrDefaultAsync(cancellationToken);
}
