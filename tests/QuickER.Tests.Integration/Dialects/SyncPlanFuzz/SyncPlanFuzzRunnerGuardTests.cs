using System;
using System.Linq;
using System.Threading.Tasks;
using AwesomeAssertions;
using QuickER.Dialects;
using QuickER.Tests.TestSupport.Engines;

namespace QuickER.Tests.Integration.Dialects.SyncPlanFuzz;

/// <summary>
/// 同期計画の総当たりの 1 種の処理（<see cref="SyncPlanFuzzRunner.RunGuardedAsync"/>）が、予期しない例外をその種の失敗として
/// 記録し、ほかの種の結果を止めないことを固定する（DB に触れない＝Docker なしで流れる）。
/// </summary>
public sealed class SyncPlanFuzzRunnerGuardTests
{
    [Fact(
        DisplayName = "1 種の予期しない例外は、その種の失敗として記録され、新しい失敗として報告に出る"
    )]
    public async Task ThrowingSeed_IsRecordedAndReportedAsUnknownFailure()
    {
        var outcome = await SyncPlanFuzzRunner.RunGuardedAsync(
            DialectNames.Sqlite,
            42,
            () => throw new ArgumentOutOfRangeException("index", "生成器の例外")
        );

        outcome.Seed.Should().Be(42);
        outcome.Dialect.Should().Be(DialectNames.Sqlite);
        outcome
            .Violations.Should()
            .ContainSingle()
            .Which.Should()
            .StartWith(
                $"{SyncPlanFuzzRunner.SeedThrewPrefix} ArgumentOutOfRangeException: 生成器の例外"
            );
        outcome.Error.Should().Contain(nameof(ThrowingSeed_IsRecordedAndReportedAsUnknownFailure));

        // 既知の失敗の一覧（形で名指しする項目）に当たらず、新しい失敗として種つきで報告に出る
        var plan = SeedSource.Resolve(
            SqliteSyncPlanFuzzTests.Seeds,
            name => name == SyncPlanFuzzTestsBase.SeedVariable ? "42" : null
        );
        var report = new EngineReport("SyncPlan.sqlite", 8);
        report.Add(SyncPlanFuzzKnownFailures.ToFailure(outcome), plan.ReproductionFor(42));
        var known = SyncPlanFuzzKnownFailures.Set.Classify(
            report.Failures,
            plan,
            scope => scope == DialectNames.Sqlite
        );

        known.Unknown.Select(f => f.Seed).Should().Equal(42);
        known.Problem("SyncPlan", report).Should().Contain("42").And.Contain("seed threw:");
    }

    [Fact(DisplayName = "例外の後の種も流れ、例外を起こさなかった種の結果はそのまま返る")]
    public async Task SeedsAfterThrowingSeed_StillRun()
    {
        var outcomes = new[] { 1, 2, 3 }
            .Select(seed =>
                SyncPlanFuzzRunner.RunGuardedAsync(
                    DialectNames.Sqlite,
                    seed,
                    () =>
                        seed == 2
                            ? throw new InvalidOperationException("2 だけ失敗")
                            : Task.FromResult(
                                new FuzzSeedOutcome { Seed = seed, Dialect = DialectNames.Sqlite }
                            )
                )
            )
            .ToList();

        var results = await Task.WhenAll(outcomes);

        results.Select(o => o.Seed).Should().Equal(1, 2, 3);
        results.Select(o => o.Violations.Count).Should().Equal(0, 1, 0);
    }

    [Fact(DisplayName = "取り消しは記録せずに止める")]
    public async Task Cancellation_Propagates()
    {
        var act = () =>
            SyncPlanFuzzRunner.RunGuardedAsync(
                DialectNames.Sqlite,
                1,
                () => throw new OperationCanceledException()
            );

        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
