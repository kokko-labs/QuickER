using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace QuickER.Tests.GeneratedQueryFixture;

// 名前付きクエリの manual／実装が生成されない実装先（EF Core の自由 SQL 分）を、
// 利用者がそうするのと同じ形（partial クラス）で実装する。
// 実装漏れはこのテストプロジェクトのコンパイルエラーとして検出される＝統一規則の実証を兼ねる。

/// <summary>
/// 重複事前チェックのユーザー定義フック（<c>CollectCustomUniquenessChecks</c>）の共有実装。
/// </summary>
/// <remarks>
/// 図の UNIQUE 制約では表せない業務ルールを足す拡張点の見本で、金額 999 を「予約済み」として弾く。
/// QuickER 版・EF Core 版・リモートのサーバー側実装が同じデリゲートを共有する（実装先ごとの写経を避ける）。
/// </remarks>
public static class OrderUniquenessCustomCheck
{
    /// <summary>予約済みとして弾く金額</summary>
    public const decimal ReservedAmount = 999m;

    /// <summary>ユーザー定義チェックが返す制約名</summary>
    public const string ConstraintName = "CUSTOM_reserved_amount";

    /// <summary>ユーザー定義チェックが返す固定メッセージ（<c>UniquenessViolation.Message</c> の優先を検証する）</summary>
    public const string Message = "The amount 999 is reserved.";

    /// <summary>金額が予約済みなら違反を返すチェック（該当しなければ null）</summary>
    public static Task<UniquenessViolation?> CheckAsync(
        OrderEntity entity,
        CancellationToken cancellationToken
    ) =>
        Task.FromResult<UniquenessViolation?>(
            entity.Amount is { Value: ReservedAmount }
                ? new UniquenessViolation(
                    ConstraintName,
                    new[] { nameof(OrderEntity.Amount) },
                    Message
                )
                : null
        );
}

/// <summary>manual クエリ（SpecialLookup）とユーザー定義重複チェックのQuickER 版 Repository 側実装</summary>
public sealed partial class OrderRepository
{
    /// <summary>ユーザー定義の重複チェックを登録する（拡張点 partial の実装見本）</summary>
    partial void CollectCustomUniquenessChecks(ref List<UniquenessCheck<OrderEntity>>? checks) =>
        (checks ??= new List<UniquenessCheck<OrderEntity>>()).Add(
            OrderUniquenessCustomCheck.CheckAsync
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
    /// <summary>ユーザー定義の重複チェックを登録する（QuickER 版と同一のデリゲートを共有する）</summary>
    partial void CollectCustomUniquenessChecks(ref List<UniquenessCheck<OrderEntity>>? checks) =>
        (checks ??= new List<UniquenessCheck<OrderEntity>>()).Add(
            OrderUniquenessCustomCheck.CheckAsync
        );

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
