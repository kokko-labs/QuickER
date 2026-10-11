using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Xunit;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>
/// 同期エンジンのモデルベーステスト（端末 2 台＋サーバー・直結・サーバー役は SQLite）。乱数の操作の列と、指摘ごとの
/// 決定的なシナリオを流し、止まらない・収束する・消えないを表明する（表明の中身は <see cref="SyncModelRun"/>）。
/// </summary>
/// <remarks>
/// <para>
/// 既知の失敗は <see cref="SyncModelKnownFailures"/> の一覧で一時的に外す。一覧は厳格で、載っているシナリオが
/// その署名の組で失敗しなければ赤、載っていない失敗も赤。乱数の種の失敗は、一覧のどれかの署名に当たれば
/// 既知として数えるだけにする（件数は出力へ書く）。
/// </para>
/// <para>
/// 種の数は既定 60（環境変数 <c>QUICKER_SYNC_MODEL_SEEDS</c> で変えられる）。種は 1 つのテストの中で並列に流す
/// （xUnit は同じクラスの Theory の行を並列に流さない）。世界は種ごとに作り、プロセス全体で共有するものは無い（ローカルの
/// 書き込みロックはローカルの生 SQL 実行器ごと、抑制の印は非同期フローごと）。
/// </para>
/// </remarks>
[Trait("Category", "Integration")]
public sealed class SyncModelRuntimeTests(ITestOutputHelper output)
{
    /// <summary>1 つの種の操作の数</summary>
    public const int StepsPerSeed = 40;

    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    /// <summary>種の数（環境変数 QUICKER_SYNC_MODEL_SEEDS・既定 60）</summary>
    private static int SeedCount =>
        int.TryParse(
            Environment.GetEnvironmentVariable("QUICKER_SYNC_MODEL_SEEDS"),
            NumberStyles.None,
            CultureInfo.InvariantCulture,
            out var count
        )
        && count > 0
            ? count
            : 60;

    /// <summary>決定的なシナリオの名前（HTTP の派生も使う）</summary>
    public static TheoryData<string> ScenarioNames()
    {
        var data = new TheoryData<string>();

        foreach (var scenario in SyncModelScenarios.All)
        {
            data.Add(scenario.Name);
        }

        return data;
    }

    /// <summary>種 1 つを流す（操作の列と違反を返す）</summary>
    internal static async Task<(
        IReadOnlyList<ModelFailure> Failures,
        IReadOnlyList<string> Log
    )> RunSeedAsync(int seed, CancellationToken ct, SyncModelWorldOptions? options = null)
    {
        var generator = new SyncModelGenerator(seed);
        await using var run = await SyncModelRun.StartAsync(generator.Setup(), ct, options);
        run.Log.Count.Should().BeGreaterThan(0);

        for (var step = 0; step < StepsPerSeed; step++)
        {
            var states = new Dictionary<ModelActor, ModelDbState>();

            foreach (var actor in Enum.GetValues<ModelActor>())
            {
                states[actor] = await run.World.ReadStateAsync(actor, ct);
            }

            foreach (var op in generator.Next(states))
            {
                if (!await run.ExecuteAsync(op))
                {
                    break;
                }
            }
        }

        await run.FinishAsync();

        return (run.Failures, run.Log);
    }

    [Fact(
        DisplayName = "[Sync/モデル] 乱数の操作の列で、止まらず・収束し・消えない（既知の失敗の署名は除く）"
    )]
    public async Task RandomSeeds_ConvergeWithoutLoss()
    {
        // 乱数の種では、途中の同期で起きたか静止化で起きたか、どの表で起きたかを区別しない（Family を参照）
        var known = SyncModelKnownFailures
            .All.Select(k => Family(k.Signature))
            .ToHashSet(StringComparer.Ordinal);
        var results =
            new ConcurrentDictionary<
                int,
                (IReadOnlyList<ModelFailure> Failures, IReadOnlyList<string> Log)
            >();
        var elapsed = new ConcurrentDictionary<int, TimeSpan>();

        // 種ごとに世界（SQLite の DB 3 つ）を作るので、種どうしは何も共有しない。並列度は CPU 数で抑える
        await Parallel.ForEachAsync(
            Enumerable.Range(0, SeedCount),
            new ParallelOptions
            {
                MaxDegreeOfParallelism = Math.Clamp(Environment.ProcessorCount, 1, 8),
                CancellationToken = Ct,
            },
            async (seed, ct) =>
            {
                var watch = Stopwatch.StartNew();
                results[seed] = await RunSeedAsync(seed, ct);
                elapsed[seed] = watch.Elapsed;
            }
        );

        var report = new StringBuilder();
        var knownCounts = new SortedDictionary<string, int>(StringComparer.Ordinal);
        var failed = 0;

        foreach (var (seed, (failures, log)) in results.OrderBy(p => p.Key))
        {
            foreach (
                var signature in failures
                    .Select(f => Family(f.Signature))
                    .Distinct()
                    .Where(known.Contains)
            )
            {
                knownCounts[signature] = knownCounts.GetValueOrDefault(signature) + 1;
            }

            if (failures.All(f => known.Contains(Family(f.Signature))))
            {
                continue;
            }

            failed++;
            report.AppendLine(CultureInfo.InvariantCulture, $"seed {seed}:");

            foreach (var failure in failures)
            {
                var mark = known.Contains(Family(failure.Signature)) ? "known" : "NEW";
                report.AppendLine(CultureInfo.InvariantCulture, $"  [{mark}] {failure.Signature}");
                report.AppendLine(
                    CultureInfo.InvariantCulture,
                    $"         {FirstLine(failure.Detail)}"
                );
            }

            report.AppendLine("  ops:");

            foreach (var line in log)
            {
                report.AppendLine(CultureInfo.InvariantCulture, $"    {line}");
            }
        }

        output.WriteLine($"seeds: {SeedCount}");

        foreach (var (seed, time) in elapsed.OrderByDescending(p => p.Value).Take(5))
        {
            output.WriteLine($"slowest seed {seed}: {time.TotalSeconds:F1} s");
        }

        foreach (var (signature, count) in knownCounts)
        {
            output.WriteLine($"known {signature}: {count} seed(s)");
        }

        failed
            .Should()
            .Be(0, "every failure of a random seed must be on the known list:\n" + report);
    }

    [Theory(
        DisplayName = "[Sync/モデル] 決定的なシナリオは、既知の失敗の一覧に載った署名の組どおりに失敗する（載っていなければ失敗しない）"
    )]
    [MemberData(nameof(ScenarioNames))]
    public async Task Scenario_FailsExactlyAsListed(string name)
    {
        var (failures, log) = await SyncModelScenarios.Get(name).RunAsync(Ct);
        var expected = SyncModelKnownFailures
            .All.Where(k => k.Scenario == name)
            .Select(k => k.Signature)
            .Order(StringComparer.Ordinal)
            .ToList();
        var actual = failures
            .Select(f => f.Signature)
            .Distinct()
            .Order(StringComparer.Ordinal)
            .ToList();

        actual
            .Should()
            .Equal(
                expected,
                "the scenario must fail with exactly the listed signatures.\nfailures:\n"
                    + string.Join(
                        "\n",
                        failures.Select(f => $"  {f.Signature}\n    {FirstLine(f.Detail)}")
                    )
                    + "\nops:\n"
                    + string.Join("\n", log.Select(l => "  " + l))
            );
    }

    [Fact(
        DisplayName = "[Sync/モデル] 既知の失敗の一覧は、存在するシナリオを名指しし、指摘の番号と直す区切りを持つ"
    )]
    public void KnownFailures_AreWellFormed()
    {
        var names = SyncModelScenarios.All.Select(s => s.Name).ToHashSet(StringComparer.Ordinal);

        foreach (var known in SyncModelKnownFailures.All)
        {
            names.Should().Contain(known.Scenario);
            known.Finding.Should().NotBeNullOrWhiteSpace();
            known.Section.Should().BeOneOf(30, 31);
            known.Signature.Should().NotBeNullOrWhiteSpace();
        }

        SyncModelKnownFailures
            .All.Select(k => (k.Scenario, k.Signature))
            .Should()
            .OnlyHaveUniqueItems();
    }

    [Fact(
        DisplayName = "[Sync/モデル] 同じ種は、並列に流しても同じ操作の列と同じ違反になる（出どころは種だけ）"
    )]
    public async Task SameSeed_IsDeterministic()
    {
        const int seed = 7;
        var runs = await Task.WhenAll(
            Enumerable.Range(0, 3).Select(_ => Task.Run(() => RunSeedAsync(seed, Ct), Ct))
        );

        foreach (var (failures, log) in runs.Skip(1))
        {
            log.Should().Equal(runs[0].Log);
            failures
                .Select(f => f.Signature)
                .Should()
                .Equal(runs[0].Failures.Select(f => f.Signature));
        }
    }

    /// <summary>乱数の種の照合に使う署名（静止化で起きた例外を途中の同期の例外と同じに扱う）</summary>
    /// <remarks>
    /// 表明 2・3 の署名からは表の名前も外す（同じ不具合が、種によって別の表で起きる＝決定的なシナリオは 1 つの表で再現
    /// するので、表まで一致させると表の数だけシナリオが要る）。決定的なシナリオの照合は表まで含めて厳格に行う。
    /// </remarks>
    internal static string Family(string signature)
    {
        if (signature.StartsWith("1/quiesce-throw:", StringComparison.Ordinal))
        {
            return "1/throw:" + signature["1/quiesce-throw:".Length..];
        }

        // 例: 3/lost:model_items:present-but-absent → 3/lost:*:present-but-absent
        return System.Text.RegularExpressions.Regex.Replace(
            signature,
            "^([235]/[a-z-]+):model_[a-z]+",
            "$1:*"
        );
    }

    private static string FirstLine(string text)
    {
        var index = text.IndexOfAny(['\r', '\n']);

        return index < 0 ? text : text[..index];
    }
}
