using System.Collections.Generic;

namespace QuickER.Tests.GeneratedUniquenessSqlServerFixture;

/// <summary>
/// SQL Server 方言リポジトリ側の重複事前チェック拡張点（<c>CollectCustomUniquenessChecks</c>）の実装。
/// </summary>
/// <remarks>
/// 図の UNIQUE 制約では表せない業務ルールを足す拡張点の見本で、金額が予約値なら違反を返す。
/// 判定デリゲートはクエリフィクスチャ側と同一のもの（<c>OrderUniquenessCustomCheck</c>）ではなく、
/// フィクスチャごとに型が別（生成物は namespace ごとに独立）なため、同じ規則をここで宣言する。
/// </remarks>
public sealed partial class OrderRepository
{
    /// <summary>予約済みとして弾く金額</summary>
    public const decimal ReservedAmount = 999m;

    /// <summary>ユーザー定義チェックが返す制約名</summary>
    public const string CustomConstraintName = "CUSTOM_reserved_amount";

    /// <summary>ユーザー定義チェックが返す固定メッセージ（<c>UniquenessViolation.Message</c> の優先を検証する）</summary>
    public const string CustomMessage = "The amount 999 is reserved.";

    /// <summary>ユーザー定義の重複チェックを登録する</summary>
    partial void CollectCustomUniquenessChecks(ref List<UniquenessCheck<OrderEntity>>? checks) =>
        (checks ??= new List<UniquenessCheck<OrderEntity>>()).Add(
            static (entity, cancellationToken) =>
                Task.FromResult<UniquenessViolation?>(
                    entity.Amount is { Value: ReservedAmount }
                        ? new UniquenessViolation(
                            CustomConstraintName,
                            new[] { nameof(OrderEntity.Amount) },
                            CustomMessage
                        )
                        : null
                )
        );
}
