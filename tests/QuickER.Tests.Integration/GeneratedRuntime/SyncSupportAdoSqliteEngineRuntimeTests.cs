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
    /// 夜間の既定の種の数（今の端末の直結の <see cref="SyncModelRuntimeTests.NightlySeedCount"/> の 3 分の 1）。種を並列に流して
    /// ランナーの CPU を使い切るので、夜間の Integration のジョブ（50 分未満の目安）で同時に流れるほかのテストを遅らせない数にする
    /// （4 コアのランナーで、広い世界の種と合わせて 1,440 種が 14 分）
    /// </summary>
    public const int NightlySeedCount = 400;

    /// <summary>夜間の広い世界の種の既定の数（<see cref="SyncModelRuntimeTests.NightlyWideSeedCount"/> の 3 分の 1）</summary>
    public const int NightlyWideSeedCount = 80;

    /// <summary>
    /// 乱数の種の決め方。今の端末の直結（<see cref="SyncModelRuntimeTests.Seeds"/>）と環境変数・普段の数・開始番号が同じで、
    /// 夜間の数だけ <see cref="NightlySeedCount"/>
    /// </summary>
    public static SeedSettings Seeds { get; } =
        SyncModelRandomSuite.WithNightlyCount(SyncModelRuntimeTests.Seeds, NightlySeedCount);

    /// <summary>
    /// 広い世界の種の決め方。今の端末の直結（<see cref="SyncModelRuntimeTests.WideSeeds"/>）と夜間の数だけが違う
    /// （<see cref="NightlyWideSeedCount"/>）
    /// </summary>
    public static SeedSettings WideSeeds { get; } =
        SyncModelRandomSuite.WithNightlyCount(
            SyncModelRuntimeTests.WideSeeds,
            NightlyWideSeedCount
        );

    /// <summary>
    /// 直結・新しいエンジンの端末（<see cref="SyncModelEngineDevice"/>）で乱数の種を流すときの組。次元表・生成器は今の端末の
    /// 直結の組と同じで、夜間の種の数（<see cref="Seeds"/>・<see cref="WideSeeds"/>）と、既知の失敗の一覧と網羅しない組の
    /// 範囲を別に持つ
    /// </summary>
    internal static SyncModelRandomSuite EngineSuite { get; } =
        new(
            "SyncModelEngine",
            SyncModelEngineDevice.On(SyncModelWorldOptions.Direct),
            Seeds,
            WideSeeds,
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
    /// 夜間の種の数は普段の数と同じにする（夜間に深くしない）。夜間の Integration のジョブは目安の時間を超えていて、この組の
    /// 種は並列に流れてランナーを使い切るので、足すとジョブの上限に届きうる。塊の経路の形は、普段の CI で控えとの照合
    /// （<see cref="SmallChunkScenarios_MatchDirect"/>）が全行を流す。夜間のジョブの実測が目安の内へ戻ったら、夜間の数を
    /// 普段の 4 倍（広い世界は普段と同じ）へ上げる
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
                SyncModelRuntimeTests.Seeds.RegularCount
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
        DisplayName = "[Sync/新しいエンジン] 小さい塊の端末で流した決定的なシナリオは、控えと同じ結果（違う行は理由の宣言どおり）"
    )]
    public async Task SmallChunkScenarios_MatchDirect()
    {
        var results = new ConcurrentDictionary<
            string,
            (string Outcome, string Detail, long Chunked, long Carried)
        >(StringComparer.Ordinal);

        // 直結の Theory が流す行（今のエンジンだけの印の無い行）を、小さい塊の端末で流す
        await Parallel.ForEachAsync(
            SyncModelScenarios.All.Where(scenario =>
                SyncModelScenarioParts.Runs(scenario, SyncModelTransport.Direct)
            ),
            new ParallelOptions
            {
                MaxDegreeOfParallelism = Math.Clamp(Environment.ProcessorCount, 1, 8),
                CancellationToken = Ct,
            },
            async (scenario, ct) =>
            {
                var routes = new SyncModelDownloadRoutes();
                var (outcome, detail) = await SyncModelScenarioLedger.RunAsync(
                    scenario,
                    ct,
                    SyncModelEngineDevice.OnSmallChunks(SyncModelWorldOptions.Direct, routes),
                    SyncModelKnownFailures.EngineSet
                );
                results[scenario.Name] = (outcome, detail, routes.Chunked, routes.Carried);
            }
        );

        // 直結の Theory は、同じ行が 1 回で全部の道の端末で既知の失敗の一覧どおり（一覧は空＝緑）であることを表明する。
        // 小さい塊の端末でそうならない行が、塊の道で結果の違う行
        var differing = results
            .Where(pair =>
                pair.Value.Outcome
                    is not (SyncModelScenarioLedger.Green or SyncModelScenarioLedger.Known)
            )
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .ToList();

        foreach (var (name, (outcome, detail, _, _)) in differing)
        {
            output.WriteLine($"differs from the direct run: {name}: {outcome} {detail}");
        }

        var chunkedRows = results.Count(pair => pair.Value.Chunked > 0);
        output.WriteLine(
            $"rows: {results.Count}, rows with a chunked fetch: {chunkedRows}, rows differing from the direct run: {differing.Count}"
        );
        chunkedRows
            .Should()
            .BePositive("塊の道を通った実行のある行が 1 つも無ければ、照合が空振りしている");

        // 塊をまたぐ形の印のある行は、小さい塊の端末で持ち越し（最後の段で書く行）が起きていなければ形が塊をまたいでいない
        var notCarried = SyncModelScenarios
            .All.Where(scenario => scenario.CarriesAcrossChunks)
            .Where(scenario =>
                !results.TryGetValue(scenario.Name, out var result) || result.Carried == 0
            )
            .Select(scenario => scenario.Name)
            .ToList();
        notCarried
            .Should()
            .BeEmpty("CarriesAcrossChunks の行は、小さい塊の端末で持ち越しを 1 行以上起こす");

        SyncModelSmallChunkScenarioDifferences
            .All.Values.Where(string.IsNullOrWhiteSpace)
            .Should()
            .BeEmpty("違う行の理由の宣言は理由を書く");

        GuardKnownList.AssertMatches(
            differing.ToDictionary(pair => pair.Key, _ => 1, StringComparer.Ordinal),
            SyncModelSmallChunkScenarioDifferences.All.Keys.Select(name => (name, 1)),
            (name, _) => name,
            "直結の実行と結果の違う行には理由の宣言（SyncModelSmallChunkScenarioDifferences）が要る",
            "直結の実行と同じ結果の行の理由の宣言は消す"
        );
    }

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
