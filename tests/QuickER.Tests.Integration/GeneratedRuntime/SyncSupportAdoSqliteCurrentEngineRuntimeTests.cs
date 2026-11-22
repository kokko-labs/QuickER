using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using QuickER.Tests.TestSupport;
using QuickER.Tests.TestSupport.Engines;
using Xunit;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>
/// 同期のモデルの決定的なシナリオ（<see cref="SyncModelScenarios.All"/>）を、今のエンジンの端末（記録のデコレータと
/// <c>SyncEngine</c>・直結・SQLite のサーバー役）で全部流し、行ごとの結果をチェックインした控え
/// （<c>sync-current-engine-scenario-ledger.md</c>）と照合する。
/// </summary>
/// <remarks>
/// <para>
/// 結果は <see cref="SyncModelScenarioLedger"/> の 4 つ。<c>known</c> は、既知の失敗の一覧
/// （<see cref="SyncModelKnownFailures.Set"/>）のその行の署名の組どおりに失敗した行。4 列目は、今のエンジンだけの印
/// （<see cref="SyncModelScenario.CurrentEngineOnly"/>）のある行に、その行を新しいエンジンの端末で流した結果を書く
/// （転送の Theory は印のある行を流さない。印のある行が新しいエンジンの端末で緑なら赤にする）。
/// </para>
/// <para>
/// 控えの照合の後に、<c>differs</c> の行の集合を理由の宣言（<see cref="SyncModelCurrentEngineScenarioDifferences"/>）と 2 方向に
/// 照合する。今のエンジンの端末はどの手も流せるので、<c>unsupported</c> の行は無い。控えの書き直しは環境変数
/// <c>QUICKER_REGEN_FIXTURES</c>（<see cref="RegenerateSteps"/>）。
/// </para>
/// </remarks>
[Trait("Category", "Integration")]
[RuntimeCell(RuntimeFeatures.SyncSupport, RuntimeTarget.AdoSqlite)]
public sealed class SyncSupportAdoSqliteCurrentEngineRuntimeTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>控えのリポジトリの相対パス</summary>
    public const string LedgerPath =
        "tests/QuickER.Tests.Integration/GeneratedRuntime/sync-current-engine-scenario-ledger.md";

    /// <summary>控えの書き直しのコマンド</summary>
    public static string RegenerateSteps { get; } =
        RegenerationRequest.Command(
            (
                "tests/QuickER.Tests.Integration/QuickER.Tests.Integration.csproj",
                "ScenarioLedgerDrift"
            )
        );

    /// <summary>結果の名前（控えの書式の並び）</summary>
    private static readonly string[] Outcomes =
    [
        SyncModelScenarioLedger.Green,
        SyncModelScenarioLedger.Known,
        SyncModelScenarioLedger.Unsupported,
        SyncModelScenarioLedger.Differs,
    ];

    [Fact(
        DisplayName = "[Sync/今のエンジン] 決定的なシナリオの控えは、今のエンジンの端末で流した結果と同じ"
    )]
    public async Task ScenarioLedgerDrift_MatchesRecord()
    {
        var results = await SyncModelScenarioLedger.RunAllAsync(
            SyncModelWorldOptions.Direct,
            Ct,
            SyncModelKnownFailures.Set
        );

        // 今のエンジンだけの印の行を、新しいエンジンの端末でも流す（転送の Theory はこの行を流さない）
        var marked = new ConcurrentDictionary<string, (string Outcome, string Detail)>(
            StringComparer.Ordinal
        );
        await Parallel.ForEachAsync(
            SyncModelScenarios.All.Where(scenario => scenario.CurrentEngineOnly is not null),
            new ParallelOptions
            {
                MaxDegreeOfParallelism = Math.Clamp(Environment.ProcessorCount, 1, 8),
                CancellationToken = Ct,
            },
            async (scenario, ct) =>
                marked[scenario.Name] = await SyncModelScenarioLedger.RunAsync(
                    scenario,
                    ct,
                    SyncModelEngineDevice.On(SyncModelWorldOptions.Direct),
                    SyncModelKnownFailures.EngineSet
                )
        );

        GeneratedTextFile.Verify(
            Path.Combine(EngineRepository.Root(), LedgerPath),
            SyncModelScenarioLedger.Render(
                "SyncSupportAdoSqliteCurrentEngineRuntimeTests.ScenarioLedgerDrift_MatchesRecord が、今のエンジンの端末で決定的なシナリオを流した結果から作る",
                RegenerateSteps,
                "今のエンジンの端末の決定的なシナリオの控え",
                Outcomes,
                results,
                "今のエンジンだけ（新しいエンジンの端末での結果）",
                name => marked.TryGetValue(name, out var result) ? result.Outcome : ""
            ),
            "今のエンジンの端末の決定的なシナリオの控え",
            RegenerateSteps
        );

        // 今のエンジンだけの印の行は、新しいエンジンの端末で緑にならない行だけ（緑になる行に印を付けると、その行が黙って
        // 新しいエンジンの守りから外れる）
        marked
            .Where(pair =>
                pair.Value.Outcome is SyncModelScenarioLedger.Green or SyncModelScenarioLedger.Known
            )
            .Select(pair => pair.Key)
            .Order(StringComparer.Ordinal)
            .Should()
            .BeEmpty(
                "今のエンジンだけの印（SyncModelCurrentEngineOnlyScenarios）の行は、新しいエンジンの端末で緑にならない行だけ"
            );

        results
            .Where(pair => pair.Value.Outcome == SyncModelScenarioLedger.Unsupported)
            .Select(pair => $"{pair.Key}: {pair.Value.Detail}")
            .Order(StringComparer.Ordinal)
            .Should()
            .BeEmpty("今のエンジンの端末は、決定的なシナリオのどの手も流せる");

        // 今のエンジンだけの印は、理由（新しいエンジンに同じ仕組みが無い根拠）を具体的に書く
        SyncModelScenarios
            .All.Where(scenario =>
                scenario.CurrentEngineOnly is { } reason
                && FactorInventoryGuard.IsVagueReason(reason)
            )
            .Select(scenario => scenario.Name)
            .Should()
            .BeEmpty(
                "今のエンジンだけの印（SyncModelScenario.CurrentEngineOnly）は理由を具体的に書く"
            );

        SyncModelCurrentEngineScenarioDifferences
            .All.Values.Where(FactorInventoryGuard.IsVagueReason)
            .Should()
            .BeEmpty("differs の行の理由の宣言は理由を具体的に書く");

        GuardKnownList.AssertMatches(
            results
                .Where(pair => pair.Value.Outcome == SyncModelScenarioLedger.Differs)
                .ToDictionary(pair => pair.Key, _ => 1, StringComparer.Ordinal),
            SyncModelCurrentEngineScenarioDifferences.All.Keys.Select(name => (name, 1)),
            (name, _) => name,
            "控えの differs の行には理由の宣言（SyncModelCurrentEngineScenarioDifferences）が要る",
            "differs でない行の理由の宣言は消す"
        );
    }
}
