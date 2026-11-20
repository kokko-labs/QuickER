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
/// 同期のモデルの決定的なシナリオ（<see cref="SyncModelScenarios.All"/>）を、新しいエンジンの端末
/// （<see cref="SyncModelEngineDevice"/>・直結・SQLite のサーバー役）で全部流し、行ごとの結果をチェックインした控え
/// （<c>sync-engine-scenario-ledger.md</c>）と照合する。
/// </summary>
/// <remarks>
/// <para>
/// 結果は 3 つ：<c>green</c>（違反が無い。判定役の対照の行は出た署名の集合が宣言と一致）・<c>unsupported</c>（「流せない」の
/// 例外で終わった。詳細は手の名前）・<c>differs</c>（それ以外。詳細は署名を序数の順に並べたものか、例外の型と短い要約）。「今のエンジン」の列は流さずに宣言から作る
/// （判定役の対照の行と、既知の失敗の一覧にその行の項目の無い行は <c>green</c>、項目のある行は <c>known</c>）。
/// </para>
/// <para>
/// 控えの照合の後に、<c>differs</c> の行の集合を理由の宣言（<see cref="SyncModelEngineScenarioDifferences"/>）と 2 方向に照合する。
/// 控えの書き直しは環境変数 <c>QUICKER_REGEN_FIXTURES</c>（<see cref="RegenerateSteps"/>）。
/// </para>
/// </remarks>
[Trait("Category", "Integration")]
[RuntimeCell(RuntimeFeatures.SyncSupport, RuntimeTarget.AdoSqlite)]
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
        WithNightlyCount(SyncModelRuntimeTests.Seeds, NightlySeedCount);

    /// <summary>
    /// 広い世界の種の決め方。今の端末の直結（<see cref="SyncModelRuntimeTests.WideSeeds"/>）と夜間の数だけが違う
    /// （<see cref="NightlyWideSeedCount"/>）
    /// </summary>
    public static SeedSettings WideSeeds { get; } =
        WithNightlyCount(SyncModelRuntimeTests.WideSeeds, NightlyWideSeedCount);

    /// <summary>夜間の数だけを替えた決め方を作る（普段の種が同じなので、種の指紋の記録は元の決め方と共有できる）</summary>
    private static SeedSettings WithNightlyCount(SeedSettings settings, int nightlyCount) =>
        new(
            settings.CountVariable,
            settings.RegularCount,
            nightlyCount,
            settings.FirstSeed,
            settings.SingleVariable
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

    /// <summary>控えのリポジトリの相対パス</summary>
    public const string LedgerPath =
        "tests/QuickER.Tests.Integration/GeneratedRuntime/sync-engine-scenario-ledger.md";

    /// <summary>控えの書き直しのコマンド</summary>
    public static string RegenerateSteps { get; } =
        RegenerationRequest.Command(
            (
                "tests/QuickER.Tests.Integration/QuickER.Tests.Integration.csproj",
                "ScenarioLedgerDrift"
            )
        );

    /// <summary>結果の名前（控えの書式の並び）</summary>
    private static readonly string[] Outcomes = ["green", "unsupported", "differs"];

    [Fact(
        DisplayName = "[Sync/新しいエンジン] 決定的なシナリオの控えは、新しいエンジンの端末で流した結果と同じ"
    )]
    public async Task ScenarioLedgerDrift_MatchesRecord()
    {
        var results = new ConcurrentDictionary<string, (string Outcome, string Detail)>(
            StringComparer.Ordinal
        );

        await Parallel.ForEachAsync(
            SyncModelScenarios.All,
            new ParallelOptions
            {
                MaxDegreeOfParallelism = Math.Clamp(Environment.ProcessorCount, 1, 8),
                CancellationToken = Ct,
            },
            async (scenario, ct) => results[scenario.Name] = await RunAsync(scenario, ct)
        );

        GeneratedTextFile.Verify(
            Path.Combine(EngineRepository.Root(), LedgerPath),
            Render(results),
            "新しいエンジンの端末の決定的なシナリオの控え",
            RegenerateSteps
        );

        SyncModelEngineScenarioDifferences
            .All.Values.Where(string.IsNullOrWhiteSpace)
            .Should()
            .BeEmpty("differs の行の理由の宣言は理由を書く");

        GuardKnownList.AssertMatches(
            results
                .Where(pair => pair.Value.Outcome == "differs")
                .ToDictionary(pair => pair.Key, _ => 1, StringComparer.Ordinal),
            SyncModelEngineScenarioDifferences.All.Keys.Select(name => (name, 1)),
            (name, _) => name,
            "控えの differs の行には理由の宣言（SyncModelEngineScenarioDifferences）が要る",
            "differs でない行の理由の宣言は消す"
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

    /// <summary>シナリオ 1 本を新しいエンジンの端末で流し、結果と詳細を返す</summary>
    private static async Task<(string Outcome, string Detail)> RunAsync(
        SyncModelScenario scenario,
        CancellationToken ct
    )
    {
        try
        {
            // 控えの Fact だけが素通しの印を立てる（「流せない」を違反にせず、行の結果として数える）
            var (failures, _) = await scenario.RunAsync(
                ct,
                SyncModelEngineDevice.On(SyncModelWorldOptions.Direct) with
                {
                    PassesNotRunnableThrough = true,
                }
            );
            var signatures = failures.Select(f => f.Signature).ToHashSet(StringComparer.Ordinal);
            var green = scenario.ExpectedJudgeSignatures is { } expected
                ? signatures.SetEquals(expected)
                : signatures.Count == 0;

            return green
                ? ("green", "")
                : (
                    "differs",
                    signatures.Count == 0
                        ? "(no violations)"
                        : string.Join(", ", signatures.Order(StringComparer.Ordinal))
                );
        }
        catch (Exception ex) when (SyncModelDevice.UnsupportedHand(ex) is { } hand)
        {
            return ("unsupported", hand);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return ("differs", "throw " + Summary(ex));
        }
    }

    /// <summary>例外の型と、機械やパスに依らない短い要約（数字は # へ畳み、パスに見える部分は伏せる）</summary>
    private static string Summary(Exception ex) =>
        PathLike.Replace(SyncModelRun.ErrorText(ex), "<path>");

    /// <summary>パスに見える部分（ドライブ文字かスラッシュで始まり空白・引用符まで）</summary>
    private static readonly Regex PathLike = new(
        @"([A-Za-z]:[\\/]|[\\/]{1,2}[^\s'""]*[\\/])[^\s'""]*",
        RegexOptions.CultureInvariant
    );

    /// <summary>控えのテキスト（改行は LF）を作る</summary>
    private static string Render(
        IReadOnlyDictionary<string, (string Outcome, string Detail)> results
    )
    {
        var text = new StringBuilder();
        text.Append(
            "<!-- 手で直さない生成物（SyncSupportAdoSqliteEngineRuntimeTests.ScenarioLedgerDrift_MatchesRecord が、新しいエンジンの端末で決定的なシナリオを流した結果から作る）。書き直し："
                + RegenerateSteps
                + " -->\n\n"
        );
        text.Append("# 新しいエンジンの端末の決定的なシナリオの控え\n\n");
        text.Append(
            string.Join(
                " / ",
                Outcomes.Select(outcome =>
                    $"{outcome}: {results.Values.Count(r => r.Outcome == outcome).ToString(System.Globalization.CultureInfo.InvariantCulture)}"
                )
            )
        );
        text.Append("\n\n| シナリオ | 結果 | 詳細 | 今のエンジン |\n|---|---|---|---|\n");

        foreach (var (name, result) in results.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            text.Append(
                $"| {Cell(name)} | {result.Outcome} | {Cell(result.Detail)} | {CurrentEngine(name)} |\n"
            );
        }

        return text.ToString();
    }

    /// <summary>
    /// 今のエンジンでその行が緑か（流さずに宣言から作る。判定役の対照の行は宣言の集合で判定するので green、ほかは既知の失敗の
    /// 一覧にそのシナリオを範囲にした項目があれば known）
    /// </summary>
    private static string CurrentEngine(string name) =>
        SyncModelScenarios.Get(name).ExpectedJudgeSignatures is null
        && SyncModelKnownFailures.Set.Entries.Any(entry =>
            string.Equals(entry.Scope, name, StringComparison.Ordinal)
        )
            ? "known"
            : "green";

    /// <summary>表のセルに置けるようにする（縦棒を逃がす）</summary>
    private static string Cell(string text) => text.Replace("|", "\\|", StringComparison.Ordinal);
}
