using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Xunit;

namespace QuickER.Tests.GeneratedInMemoryFixture;

/// <summary>
/// <c>SqlQuery.BuildPlan</c> が「捕捉した式・Include・ページングのスナップショット」を渡す、という契約を固定する。
/// </summary>
/// <remarks>
/// 生のリストをそのまま渡していた頃は、終端メソッドの後もチェーンを組み立て続けると、実行中（あるいは実行済み）の
/// プランの条件が後から増えた。式木そのものは複製しない（不変であり、作り直すと EF Core のクエリプランキャッシュを
/// 壊す）ため、コピーするのはリストだけでよい。
/// </remarks>
public sealed class SqlQueryPlanSnapshotTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    /// <summary>終端メソッドへ渡したプランは、その後の Where / OrderBy / Skip / Take の追加に影響されない。</summary>
    [Fact(DisplayName = "[QueryPlan] BuildPlan 後にチェーンを足しても既取得プランは変わらない")]
    public async Task BuildPlan_IsSnapshot_LaterChainingDoesNotLeakIn()
    {
        var executor = new CapturingExecutor();
        var query = new SqlQuery<CustomerEntity>(executor);
        query.Where(customer => customer.CustomerId == 1).OrderBy(customer => customer.Name);

        await query.CountAsync(Ct);

        var captured = executor.LastPlan!;
        captured.Predicates.Should().ContainSingle();
        captured.Orderings.Should().ContainSingle();

        // 終端の後もチェーンは組み立てられる（同じ SqlQuery を使い回す利用形）
        query.Where(customer => customer.Name == "Alice").OrderByDescending(c => c.CustomerId);

        captured.Predicates.Should().ContainSingle("既に渡したプランへ後から条件は増えない");
        captured.Orderings.Should().ContainSingle("並び順も同じくスナップショット");

        // 次の終端は当然、増えた分を含む新しいプランを受け取る
        await query.CountAsync(Ct);
        executor.LastPlan!.Predicates.Should().HaveCount(2);
        executor.LastPlan!.Orderings.Should().HaveCount(2);
    }

    /// <summary>終端へ渡されたプランを記録するだけの実行器（バックエンドは呼ばない）。</summary>
    private sealed class CapturingExecutor : ISqlQueryExecutor<CustomerEntity>
    {
        /// <summary>直近の終端メソッドが受け取ったプラン</summary>
        public SqlQueryPlan<CustomerEntity>? LastPlan { get; private set; }

        public Task<IReadOnlyList<CustomerEntity>> ToListAsync(
            SqlQueryPlan<CustomerEntity> plan,
            CancellationToken cancellationToken
        )
        {
            LastPlan = plan;
            return Task.FromResult<IReadOnlyList<CustomerEntity>>([]);
        }

        public Task<IReadOnlyList<TResult>> ToProjectionListAsync<TResult>(
            SqlQueryPlan<CustomerEntity> plan,
            Expression<Func<CustomerEntity, TResult>> selector,
            CancellationToken cancellationToken
        )
        {
            LastPlan = plan;
            return Task.FromResult<IReadOnlyList<TResult>>([]);
        }

        public Task<CustomerEntity?> FirstOrDefaultAsync(
            SqlQueryPlan<CustomerEntity> plan,
            CancellationToken cancellationToken
        )
        {
            LastPlan = plan;
            return Task.FromResult<CustomerEntity?>(null);
        }

        public Task<int> CountAsync(
            SqlQueryPlan<CustomerEntity> plan,
            CancellationToken cancellationToken
        )
        {
            LastPlan = plan;
            return Task.FromResult(0);
        }

        public Task<bool> AnyAsync(
            SqlQueryPlan<CustomerEntity> plan,
            CancellationToken cancellationToken
        )
        {
            LastPlan = plan;
            return Task.FromResult(false);
        }

        public Task<int> ExecuteDeleteAsync(
            SqlQueryPlan<CustomerEntity> plan,
            bool cascadeDelete,
            CancellationToken cancellationToken
        )
        {
            LastPlan = plan;
            return Task.FromResult(0);
        }
    }
}
