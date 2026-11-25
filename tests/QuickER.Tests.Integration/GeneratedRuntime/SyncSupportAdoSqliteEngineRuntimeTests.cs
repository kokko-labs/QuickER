using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using QuickER.Tests.TestSupport;
using QuickER.Tests.TestSupport.Engines;
using Xunit;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>
/// 新しいエンジンの端末（<see cref="SyncModelEngineDevice"/>・直結・SQLite のサーバー役）だけで確かめるもの：乱数の種
/// （1 回で全部の道の端末と小さい塊の端末）・小さい塊の端末で流した決定的なシナリオ・決まった形の行。
/// </summary>
/// <remarks>
/// 決定的なシナリオを 1 回で全部の道の端末で流すのは、直結の Theory
/// （<see cref="SyncModelRuntimeTests.Scenario_FailsExactlyAsListed"/>）。
/// </remarks>
[Trait("Category", "Integration")]
[RuntimeCell(RuntimeFeatures.SyncSupport, RuntimeTarget.AdoSqlite)]
[RuntimeCell(RuntimeFeatures.SyncRefresh, RuntimeTarget.AdoSqlite)]
public sealed class SyncSupportAdoSqliteEngineRuntimeTests(ITestOutputHelper output)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>
    /// 直結・新しいエンジンの端末（<see cref="SyncModelEngineDevice"/>）で乱数の種を流すときの組。種の決め方は
    /// <see cref="SyncModelRuntimeTests.Seeds"/>・<see cref="SyncModelRuntimeTests.WideSeeds"/> そのもの（夜間は 1200＋広い世界 240）
    /// </summary>
    /// <remarks>
    /// 種を全部のコアで並列に流すので、流れている間は同じジョブのほかのテストも遅れる。夜間の 3 夜の実測（2026-11-24・25）では
    /// 400＋広い世界 80 の種で 1.1〜1.7 分で、今の数では約 3 倍（遅いランナーで約 5 分）に伸びる。伸びる分と、その間にほかのテストが
    /// 遅れる分を足しても、integration-nightly-rest の段は目安の 50 分に十分な余裕を残す
    /// （<c>tests/QuickER.Tests/TestSupport/CLAUDE.md</c>「夜間の深い実行」）。
    /// </remarks>
    internal static SyncModelRandomSuite EngineSuite { get; } =
        new(
            "SyncModelEngine",
            SyncModelWorldOptions.Direct,
            SyncModelRuntimeTests.Seeds,
            SyncModelRuntimeTests.WideSeeds,
            SyncModelKnownFailures.EngineSet,
            SyncModelDimensions.EngineGaps,
            SyncModelOutcomeDimensions.EngineGaps,
            SyncModelDimensions.EngineScope,
            SyncModelRuntimeTests.RefusalBasisKinds,
            SyncModelRuntimeTests.GuardedRunChecks,
            [SyncModelRun.CarryEditLostCheck]
        );

    [Fact(
        DisplayName = "[Sync/新しいエンジン] 乱数の操作の列で、止まらず・収束し・消えない（新しいエンジンの端末・既知の失敗の署名は除く）"
    )]
    [Trait(NightlyEngineTrait.Name, NightlyEngineTrait.Selected)]
    public Task RandomSeeds_ConvergeWithoutLoss() =>
        SyncModelRuntimeTests.RunRandomSeedsAsync(EngineSuite, output);

    /// <summary>小さい塊の組の端末のエンジンの取得ごとの道を数える入れ物（組と一緒にプロセスで 1 つ）</summary>
    private static readonly SyncModelDownloadRoutes SmallChunkRoutes = new();

    /// <summary>
    /// 直結・小さい塊の端末（<see cref="SyncModelEngineDevice.OnSmallChunks"/>）で乱数の種を流すときの組。端末の作り方・夜間の数・
    /// 夜間だけの空振りの守りのほかは <see cref="EngineSuite"/> と同じ（既知の失敗の一覧・網羅しない組・空振りの守りの並び）
    /// </summary>
    /// <remarks>
    /// 夜間だけの空振りの守り（戻せなかった行の決定の後の編集の照合）は持たない。その照合が起きるのは未送信の行がある実行だけで、
    /// 未送信のある実行は小さい塊の端末でも 1 回で全部の道を通る（塊の経路に入るのは、入口で未送信の無い実行だけ）。照合が見る
    /// 経路は <see cref="EngineSuite"/> と同じなので、守りはそちらが持つ。この組の空振りの守りは、道の守り（1 回で全部と塊が
    /// どちらも 1 回以上）で受ける。
    /// <para>
    /// 夜間の種の数は普段の 4 倍（240）、広い世界の種は普段と同じ（48）。この組の種も並列に流れてランナーを使い切るが、夜間の
    /// 3 夜の実測（2026-11-24・25）で普段と同じ数の種が 0.5 分なので、この数でも約 1.3 分の見込みで、integration-nightly-rest の段
    /// （同じ 3 夜で 13.1〜21.1 分）は目安の 50 分の内に収まる。塊の経路の形は、普段の CI で直結の Theory との照合
    /// （<see cref="SmallChunkScenarios_MatchDirect"/>）が全行を流す
    /// </para>
    /// </remarks>
    internal static SyncModelRandomSuite SmallChunkSuite { get; } =
        EngineSuite with
        {
            Name = "SyncModelEngineSmallChunks",
            Options = SyncModelEngineDevice.OnSmallChunks(
                SyncModelWorldOptions.Direct,
                SmallChunkRoutes
            ),
            Seeds = SyncModelRandomSuite.WithNightlyCount(
                SyncModelRuntimeTests.Seeds,
                SyncModelRuntimeTests.Seeds.RegularCount * 4
            ),
            WideSeeds = SyncModelRandomSuite.WithNightlyCount(
                SyncModelRuntimeTests.WideSeeds,
                SyncModelRuntimeTests.WideSeeds.RegularCount
            ),
            NightlyGuardedRunChecks = [],
        };

    [Fact(
        DisplayName = "[Sync/新しいエンジン] 小さい塊の端末でも、乱数の操作の列で止まらず・収束し・消えない（既知の失敗の署名は除く）"
    )]
    [Trait(NightlyEngineTrait.Name, NightlyEngineTrait.Selected)]
    public async Task SmallChunkRandomSeeds_ConvergeWithoutLoss()
    {
        var before = (SmallChunkRoutes.Single, SmallChunkRoutes.Chunked, SmallChunkRoutes.Refused);
        await SyncModelRuntimeTests.RunRandomSeedsAsync(SmallChunkSuite, output);

        var single = SmallChunkRoutes.Single - before.Single;
        var chunked = SmallChunkRoutes.Chunked - before.Chunked;
        output.WriteLine(
            $"download routes: single {single}, chunked {chunked}, refused {SmallChunkRoutes.Refused - before.Refused}"
        );

        // 空振りの守り：既定の種の数なら、1 回で全部の道と塊の道がどちらも起きている（断る道は乱数の種では起きない＝決まった形の行が持つ）
        var (plan, widePlan) = SyncModelSignatures.ResolvePlans(
            SmallChunkSuite.Seeds,
            SmallChunkSuite.WideSeeds
        );

        if (plan.IsDefaultCount && widePlan.IsDefaultCount)
        {
            CoverageGuard.AtLeast("small-chunk download route Single", (int)single, 1);
            CoverageGuard.AtLeast("small-chunk download route Chunked", (int)chunked, 1);
        }
    }

    [Fact(
        DisplayName = "[Sync/新しいエンジン] 小さい塊の端末で流した決定的なシナリオは、直結の Theory と同じ結果（違う行は理由の宣言どおり）"
    )]
    public Task SmallChunkScenarios_MatchDirect() =>
        SyncModelSmallChunkScenarioRuns.AssertMatchesDirectAsync(
            SyncModelTransport.Direct,
            SyncModelWorldOptions.Direct,
            Math.Clamp(Environment.ProcessorCount, 1, 8),
            output,
            Ct
        );

    [Theory(
        DisplayName = "[Sync/新しいエンジン] 決まった形の行（実行の入口・blob の運び方）が期待どおりに振る舞う"
    )]
    [MemberData(nameof(SyncEngineFixedRows.Ids), MemberType = typeof(SyncEngineFixedRows))]
    public async Task FixedRow_BehavesAsExpected(string id)
    {
        await using var harness = await SyncEngineFixedHarness.CreateAsync(Ct);
        await SyncEngineFixedRows.Table[id].Input.Run(harness, Ct);
    }
}
