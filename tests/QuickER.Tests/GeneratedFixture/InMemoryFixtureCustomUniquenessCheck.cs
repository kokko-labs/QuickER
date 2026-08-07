using System.Collections.Generic;
using System.Threading.Tasks;

namespace QuickER.Tests.GeneratedInMemoryFixture;

/// <summary>
/// インメモリ Repository 側の重複事前チェック拡張点（<c>CollectCustomUniquenessChecks</c>）の実装。
/// </summary>
/// <remarks>
/// 図の UNIQUE 制約では表せない業務ルールを足す拡張点の見本で、金額が予約値なら違反を返す。
/// 利用者がそうするのと同じ形（partial クラス）で実装する。
/// </remarks>
public sealed partial class InMemoryOrderRepository
{
    /// <summary>予約済みとして弾く金額</summary>
    public const decimal ReservedAmount = 999m;

    /// <summary>ユーザー定義チェックが返す制約名</summary>
    public const string CustomConstraintName = "CUSTOM_reserved_amount";

    /// <summary>ユーザー定義の重複チェックを登録する</summary>
    partial void CollectCustomUniquenessChecks(ref List<UniquenessCheck<OrderEntity>>? checks) =>
        (checks ??= new List<UniquenessCheck<OrderEntity>>()).Add(
            static (entity, cancellationToken) =>
                Task.FromResult<UniquenessViolation?>(
                    entity.Amount == ReservedAmount
                        ? new UniquenessViolation(
                            CustomConstraintName,
                            new[] { nameof(OrderEntity.Amount) }
                        )
                        : null
                )
        );
}
