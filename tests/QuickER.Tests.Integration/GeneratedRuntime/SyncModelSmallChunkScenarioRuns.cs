using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using QuickER.Tests.TestSupport;
using QuickER.Tests.TestSupport.Engines;
using Xunit;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>
/// 決定的なシナリオを小さい塊の端末（<see cref="SyncModelEngineDevice.OnSmallChunks"/>）で流し、1 回で全部の道の端末と同じ結果に
/// なることを転送ごとに確かめる（違う行は理由の宣言 <see cref="SyncModelSmallChunkScenarioDifferences"/> どおり）
/// </summary>
/// <remarks>
/// <para>
/// 塊の経路は取得のページを流しながら塊を確定するので、取得の上限を実行の始めに固定するか
/// （<see cref="ISyncModelServerRole.FixesFetchCeilingAtRunStart"/>）・HTTP のページ分けと再開点の往復に依る。直結・HTTP・
/// 実 SQL Server のそれぞれで、その転送の Theory が流す行（<see cref="SyncModelScenario.RunsOn"/>）を流す。
/// </para>
/// <para>
/// 比べる相手は、その転送の 1 回で全部の道の結果。転送の Theory が「直結と同じ違反」を表明し、直結の Theory が「既知の失敗の
/// 一覧〔空〕どおり」を表明するので、小さい塊の端末の結果は緑か一覧どおりになるはずで、そうならない行が塊の道で結果の違う行。
/// </para>
/// </remarks>
internal static class SyncModelSmallChunkScenarioRuns
{
    /// <summary>
    /// <paramref name="transport"/> の Theory が流す行を、<paramref name="options"/> の世界の小さい塊の端末で流して照合する
    /// </summary>
    /// <param name="transport">転送（流す行と、違う行の理由の宣言を選ぶ）</param>
    /// <param name="options">世界の組み方（転送とサーバー役。端末は小さい塊の端末に差し替える）</param>
    /// <param name="parallelism">並列に流す世界の数</param>
    /// <param name="output">出力</param>
    /// <param name="ct">取り消し</param>
    public static async Task AssertMatchesDirectAsync(
        SyncModelTransport transport,
        SyncModelWorldOptions options,
        int parallelism,
        ITestOutputHelper output,
        CancellationToken ct
    )
    {
        // 一覧は直結の一覧（空）。SQL Server だけの既知の失敗（SyncModelEngineTransports.SqlServerOnly・空）に項目を足したら、
        // 小さい塊の実 SQL Server ではその行が違う行として名指しされるので、違う行の理由の宣言に同じ理由で足す
        var known = SyncModelKnownFailures.EngineSet;
        var scenarios = SyncModelScenarios
            .All.Where(scenario => scenario.RunsOn(transport))
            .ToList();
        var results = new ConcurrentDictionary<
            string,
            (string Outcome, string Detail, long Chunked, long Carried)
        >(StringComparer.Ordinal);

        await Parallel.ForEachAsync(
            scenarios,
            new ParallelOptions { MaxDegreeOfParallelism = parallelism, CancellationToken = ct },
            async (scenario, token) =>
            {
                var routes = new SyncModelDownloadRoutes();
                var (outcome, detail) = await SyncModelScenarioLedger.RunAsync(
                    scenario,
                    token,
                    SyncModelEngineDevice.OnSmallChunks(options, routes),
                    known
                );
                results[scenario.Name] = (outcome, detail, routes.Chunked, routes.Carried);
            }
        );

        var differing = results
            .Where(pair =>
                pair.Value.Outcome
                    is not (SyncModelScenarioLedger.Green or SyncModelScenarioLedger.Known)
            )
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .ToList();

        foreach (var (name, (outcome, detail, _, _)) in differing)
        {
            output.WriteLine(
                $"differs from the full run ({transport}): {name}: {outcome} {detail}"
            );
        }

        var chunkedRows = results.Count(pair => pair.Value.Chunked > 0);
        output.WriteLine(
            $"transport: {transport}, rows: {results.Count}, rows with a chunked fetch: {chunkedRows}, rows differing from the full run: {differing.Count}"
        );
        chunkedRows
            .Should()
            .BePositive("塊の道を通った実行のある行が 1 つも無ければ、照合が空振りしている");

        // 塊をまたぐ形の印のある行は、小さい塊の端末で持ち越し（最後の段で書く行）が起きていなければ形が塊をまたいでいない
        var notCarried = scenarios
            .Where(scenario => scenario.CarriesAcrossChunks)
            .Where(scenario =>
                !results.TryGetValue(scenario.Name, out var result) || result.Carried == 0
            )
            .Select(scenario => scenario.Name)
            .ToList();
        notCarried
            .Should()
            .BeEmpty("CarriesAcrossChunks の行は、小さい塊の端末で持ち越しを 1 行以上起こす");

        var declared = SyncModelSmallChunkScenarioDifferences.For(transport);
        declared
            .Values.Where(string.IsNullOrWhiteSpace)
            .Should()
            .BeEmpty("違う行の理由の宣言は理由を書く");

        GuardKnownList.AssertMatches(
            differing.ToDictionary(pair => pair.Key, _ => 1, StringComparer.Ordinal),
            declared.Keys.Select(name => (name, 1)),
            (name, _) => name,
            $"1 回で全部の道の実行と結果の違う行には理由の宣言（SyncModelSmallChunkScenarioDifferences・{transport}）が要る",
            $"1 回で全部の道の実行と同じ結果の行の理由の宣言は消す（{transport}）"
        );
    }
}
