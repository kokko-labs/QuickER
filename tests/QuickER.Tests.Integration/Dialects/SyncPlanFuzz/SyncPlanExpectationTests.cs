using System.Linq;
using QuickER.Tests.TestSupport.Engines;
using Xunit;
using Xunit.Sdk;

namespace QuickER.Tests.Integration.Dialects.SyncPlanFuzz;

/// <summary>
/// 同期計画の期待の行のうち、合成の能力（実方言でない宣言）の行と、複数の能力の計画を比べる行を流す（DB を使わない）
/// </summary>
/// <remarks>実方言の行は能力の組の網の派生（<see cref="SyncPlanCapabilityNetTestsBase.ExpectationRows_Match"/>）が流す</remarks>
[Trait("Category", "Integration")]
public sealed class SyncPlanExpectationTests
{
    [Fact(
        DisplayName = "[Integration] 同期計画の期待の行: 合成の能力と能力を比べる行の差分と計画が期待どおり"
    )]
    public void SyntheticAndComparisonRows_Match()
    {
        var rows = SyncPlanExpectationRows.ForSyntheticOrComparison().ToList();
        var problems = rows.SelectMany(row => SyncPlanExpectationRunner.Run(row)).ToList();
        TestContext.Current.TestOutputHelper?.WriteLine(
            $"synthetic or comparison rows {rows.Count}, problems {problems.Count}"
        );

        if (problems.Count > 0)
        {
            throw new XunitException(string.Join("\n\n", problems));
        }

        CoverageGuard.AtLeast(
            "合成の能力と能力を比べる期待の行",
            rows.Count,
            SyncPlanExpectationRows.MinimumRowsFor(SyncPlanExpectationRows.SyntheticOrComparisonKey)
        );
    }
}
