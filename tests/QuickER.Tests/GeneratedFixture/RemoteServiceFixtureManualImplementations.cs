using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace QuickER.Tests.GeneratedRemoteServiceFixture;

// 名前付きクエリの manual／実装が生成されない実装先（EF Core の自由 SQL 分）を、
// 利用者がそうするのと同じ形（partial クラス）で実装する（クエリフィクスチャの見本と同一内容）。
// リモートサービス生成でもサーバー側の実装クラスは従来どおりで、HTTP クライアント側は全クエリを
// 転送メソッドとして自動生成するため、manual 実装が必要なのはサーバー側だけになる。

/// <summary>manual クエリ（SpecialLookup）とユーザー定義重複チェックのQuickER 版 Repository 側実装</summary>
public sealed partial class OrderRepository
{
    /// <summary>予約済みとして弾く金額（ユーザー定義の重複チェック。クライアント側にフックは無く、必ずサーバー側で走る）</summary>
    public const decimal ReservedAmount = 999m;

    /// <summary>ユーザー定義チェックが返す制約名</summary>
    public const string CustomConstraintName = "CUSTOM_reserved_amount";

    /// <summary>ユーザー定義の重複チェックを登録する（拡張点 partial の実装見本）</summary>
    partial void CollectCustomUniquenessChecks(ref List<UniquenessCheck<OrderEntity>>? checks) =>
        (checks ??= new List<UniquenessCheck<OrderEntity>>()).Add(
            static (entity, cancellationToken) =>
                Task.FromResult<UniquenessViolation?>(
                    entity.Amount is { Value: ReservedAmount }
                        ? new UniquenessViolation(
                            CustomConstraintName,
                            new[] { nameof(OrderEntity.Amount) }
                        )
                        : null
                )
        );

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

/// <summary>EF Core 実装で生成されないメンバー（自由 SQL 5 件＋manual 1 件）の partial 実装</summary>
public sealed partial class EfCoreOrderRepository
{
    /// <summary>最新（注文IDが最大）の注文を 1 件取得する（QuickER 側の自由 SQL と同じ意味論）</summary>
    public Task<OrderEntity?> FindTopRawAsync(CancellationToken cancellationToken = default) =>
        Query().OrderByDescending(e => e.OrderId).FirstOrDefaultAsync(cancellationToken);

    /// <summary>顧客IDに紐づく注文件数を取得する（QuickER 側の自由 SQL と同じ意味論）</summary>
    public Task<int> CountByCustomerRawAsync(
        int customerId,
        CancellationToken cancellationToken = default
    ) =>
        Query()
            .Where(e => e.CustomerId == CustomerIdValue.Create(customerId))
            .CountAsync(cancellationToken);

    /// <summary>顧客IDに紐づく注文のメモ一覧を取得する（QuickER 側の自由 SQL と同じ意味論）</summary>
    public async Task<IReadOnlyList<OrderMemoRow>> GetMemoRowsRawAsync(
        int customerId,
        CancellationToken cancellationToken = default
    )
    {
        var items = await Query()
            .Where(e => e.CustomerId == CustomerIdValue.Create(customerId))
            .OrderBy(e => e.OrderId)
            .ToListAsync(cancellationToken);
        return items
            .Select(e => new OrderMemoRow { OrderId = e.OrderId.Value, Memo = e.Memo?.Value })
            .ToList();
    }

    /// <summary>顧客IDに紐づく注文金額の合計（該当なしは null。QuickER 側の SUM と同じ意味論）</summary>
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

    /// <summary>注文IDの一覧で注文を取得する（QuickER 側の自由 SQL と同じ意味論）</summary>
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
