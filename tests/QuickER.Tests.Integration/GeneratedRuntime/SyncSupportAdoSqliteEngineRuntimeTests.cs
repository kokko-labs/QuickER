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
    /// （<see cref="SmallChunkScenarios_MatchLedger"/>）が全行を流す。夜間のジョブの実測が目安の内へ戻ったら、夜間の数を
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
            Seeds = WithNightlyCount(
                SyncModelRuntimeTests.Seeds,
                SyncModelRuntimeTests.Seeds.RegularCount
            ),
            WideSeeds = WithNightlyCount(
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
    public async Task SmallChunkScenarios_MatchLedger()
    {
        var ledger = ReadLedger();
        var results = new ConcurrentDictionary<
            string,
            (string Outcome, string Detail, long Chunked, long Carried)
        >(StringComparer.Ordinal);

        await Parallel.ForEachAsync(
            SyncModelScenarios.All,
            new ParallelOptions
            {
                MaxDegreeOfParallelism = Math.Clamp(Environment.ProcessorCount, 1, 8),
                CancellationToken = Ct,
            },
            async (scenario, ct) =>
            {
                var routes = new SyncModelDownloadRoutes();
                var (outcome, detail) = await RunAsync(
                    scenario,
                    ct,
                    SyncModelEngineDevice.OnSmallChunks(SyncModelWorldOptions.Direct, routes)
                );
                results[scenario.Name] = (outcome, detail, routes.Chunked, routes.Carried);
            }
        );

        ledger
            .Keys.Should()
            .BeEquivalentTo(results.Keys, "控えの行は決定的なシナリオと同じ（先に控えを書き直す）");

        var differing = results
            .Where(pair => (pair.Value.Outcome, pair.Value.Detail) != ledger[pair.Key])
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .ToList();

        foreach (var (name, (outcome, detail, _, _)) in differing)
        {
            output.WriteLine(
                $"differs from ledger: {name}: {outcome} {detail} (ledger: {ledger[name].Outcome} {ledger[name].Detail})"
            );
        }

        var chunkedRows = results.Count(pair => pair.Value.Chunked > 0);
        output.WriteLine(
            $"rows: {results.Count}, rows with a chunked fetch: {chunkedRows}, rows differing from the ledger: {differing.Count}"
        );
        chunkedRows
            .Should()
            .BePositive("塊の道を通った実行のある行が 1 つも無ければ、照合が空振りしている");

        // 塊をまたぐ形の印のある行は、小さい塊の端末で持ち越し（最後の段で書く行）が起きていなければ形が塊をまたいでいない
        var notCarried = SyncModelScenarios
            .All.Where(scenario => scenario.CarriesAcrossChunks)
            .Where(scenario => results[scenario.Name].Carried == 0)
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
            "控えと違う行には理由の宣言（SyncModelSmallChunkScenarioDifferences）が要る",
            "控えと一致している行の理由の宣言は消す"
        );
    }

    /// <summary>控えを読み、シナリオの名前 → 結果と詳細を返す（表のセルの逃がした縦棒を戻す）</summary>
    private static Dictionary<string, (string Outcome, string Detail)> ReadLedger()
    {
        var ledger = new Dictionary<string, (string Outcome, string Detail)>(
            StringComparer.Ordinal
        );

        foreach (var line in File.ReadAllLines(Path.Combine(EngineRepository.Root(), LedgerPath)))
        {
            if (
                !line.StartsWith("| ", StringComparison.Ordinal)
                || line.StartsWith("| シナリオ |", StringComparison.Ordinal)
            )
            {
                continue;
            }

            var cells = LedgerCellSeparator
                .Split(line.Trim().Trim('|'))
                .Select(cell => cell.Trim().Replace("\\|", "|", StringComparison.Ordinal))
                .ToArray();
            ledger[cells[0]] = (cells[1], cells[2]);
        }

        return ledger;
    }

    /// <summary>控えの表のセルの区切り（逃がした縦棒は区切りにしない）</summary>
    private static readonly Regex LedgerCellSeparator = new(
        @"(?<!\\)\|",
        RegexOptions.CultureInvariant
    );

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
            async (scenario, ct) =>
                results[scenario.Name] = await RunAsync(
                    scenario,
                    ct,
                    SyncModelEngineDevice.On(SyncModelWorldOptions.Direct)
                )
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

    /// <summary>シナリオ 1 本を新しいエンジンの端末の世界で流し、結果と詳細を返す</summary>
    /// <param name="scenario">シナリオ</param>
    /// <param name="ct">キャンセルトークン</param>
    /// <param name="options">新しいエンジンの端末の世界の組み方</param>
    private static async Task<(string Outcome, string Detail)> RunAsync(
        SyncModelScenario scenario,
        CancellationToken ct,
        SyncModelWorldOptions options
    )
    {
        try
        {
            // 控えを作る・控えと照らす Fact だけが素通しの印を立てる（「流せない」を違反にせず、行の結果として数える）
            var (failures, _) = await scenario.RunAsync(
                ct,
                options with
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
