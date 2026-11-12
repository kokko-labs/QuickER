using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using QuickER.Tests.GeneratedSyncModelFixture;
using QuickER.Tests.TestSupport;
using QuickER.Tests.TestSupport.Engines;
using Xunit;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>
/// 同期エンジンのモデルベーステスト（端末 2 台か 3 台＋サーバー・直結・サーバー役は SQLite）。乱数の操作の列と、指摘ごとの
/// 決定的なシナリオを流し、止まらない・収束する・消えないを表明する（表明の中身は <see cref="SyncModelRun"/>）。
/// </summary>
/// <remarks>
/// <para>
/// 既知の失敗は <see cref="SyncModelKnownFailures"/> の一覧で一時的に外す。一覧は厳格で、載っているシナリオが
/// その署名の組で失敗しなければ赤、載っていない失敗も赤。乱数の種の失敗は、一覧のどれかの署名に当たれば
/// 既知として数えるだけにする（件数は出力へ書く）。
/// </para>
/// <para>
/// 種の数は既定 60（環境変数 <c>QUICKER_SYNC_MODEL_SEEDS</c> で変えられる）。その後に広い世界の種（端末 3 台・端末ごとの
/// 最初の形＝<see cref="SyncModelWorldShape"/>・<see cref="WideSeeds"/>）を流す。種は 1 つのテストの中で並列に流す
/// （xUnit は同じクラスの Theory の行を並列に流さない）。世界は種ごとに作り、プロセス全体で共有するものは無い（ローカルの
/// 書き込みロックはローカルの生 SQL 実行器ごと、抑制の印は非同期フローごと）。
/// </para>
/// </remarks>
[Trait("Category", "Integration")]
[MatrixAxisValue(SyncModelTransportAxis.Name, nameof(SyncModelTransport.Direct))]
[RuntimeCell(RuntimeFeatures.SyncSupport, RuntimeTarget.AdoSqlite)]
public sealed class SyncModelRuntimeTests(ITestOutputHelper output) : ISyncModelTransportTests
{
    /// <summary>1 つの種の操作の数</summary>
    public const int StepsPerSeed = 40;

    /// <summary>世界の組み方（既定＝直結・SQLite のサーバー役。種もシナリオも組み方を渡さずに流す）</summary>
    SyncModelWorldOptions ISyncModelTransportTests.WorldOptions => SyncModelWorldOptions.Direct;

    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    /// <summary>夜間の既定の種の数</summary>
    public const int NightlySeedCount = 1200;

    /// <summary>乱数の種（環境変数 QUICKER_SYNC_MODEL_SEEDS・既定 60・夜間は <see cref="NightlySeedCount"/>・0 から）</summary>
    public static SeedSettings Seeds { get; } =
        new("QUICKER_SYNC_MODEL_SEEDS", 60, NightlySeedCount, firstSeed: 0);

    /// <summary>夜間の広い世界の種の既定の数</summary>
    public const int NightlyWideSeedCount = 240;

    /// <summary>
    /// 広い世界の種（端末 3 台・<see cref="SyncModelWorldShape"/>。端末ごとの最初の形を種の番号から回す。環境変数 QUICKER_SYNC_MODEL_WIDE_SEEDS・既定 48・夜間は
    /// <see cref="NightlyWideSeedCount"/>・<see cref="SyncModelWorldShape.WideSeedBase"/> から）。既存の範囲の種と同じテストの中で流す
    /// </summary>
    public static SeedSettings WideSeeds { get; } =
        new(
            "QUICKER_SYNC_MODEL_WIDE_SEEDS",
            48,
            NightlyWideSeedCount,
            firstSeed: SyncModelWorldShape.WideSeedBase
        );

    /// <summary>根拠を確かめる競合・決着の種類（<see cref="SyncModelRun.BasisChecks"/> の鍵）</summary>
    private static readonly string[] RefusalBasisKinds =
    [
        nameof(SyncConflictReason.DeleteRefusedOnServer),
        nameof(SyncConflictReason.UniqueValueTakenOnServer),
        nameof(SyncConflictReason.ParentMissingOnServer),
        SyncModelRun.SettledUpsertKind,
    ];

    /// <summary>
    /// 直結で流す決定的なシナリオの名前のうち、このクラスが受け持つ分（残りは <see cref="SyncModelScenarioParts"/> の
    /// 分けたクラスが受け持つ）
    /// </summary>
    public static TheoryData<string> ScenarioNames() =>
        SyncModelScenarioParts.Names(SyncModelTransport.Direct, 0);

    /// <summary>種 1 つを流す（操作の列と違反を返す）</summary>
    /// <param name="seed">種</param>
    /// <param name="ct">取り消し</param>
    /// <param name="options">世界の作り方（転送経路・サーバー役）</param>
    /// <param name="coverage">
    /// 操作を次元表のケースとして数える先（null なら数えない）。生成器が返した操作を実行の前に読むだけで、乱数を引かない
    /// </param>
    /// <param name="outcomes">同期が返した結果（競合・自分から変えた行の理由）を数える先（null なら数えない）</param>
    /// <param name="basisTally">
    /// 理由ごとの、根拠を確かめた件数と根拠のあった件数を足し込む先（null なら数えない。空振りしていないかを出力で確かめる）
    /// </param>
    /// <param name="spellingTally">
    /// 一意な値の形ごとの、サーバーが同じ値の持ち主を根拠に断った件数を足し込む先（<see cref="SyncModelRun.SpellingRefusals"/>。
    /// null なら数えない）
    /// </param>
    /// <param name="preloadTally">
    /// 端末の最初の形が実際に効いた件数を足し込む先（<see cref="SyncModelRun.PreloadEffects"/>。null なら数えない）
    /// </param>
    internal static async Task<(
        IReadOnlyList<ModelFailure> Failures,
        IReadOnlyList<string> Log
    )> RunSeedAsync(
        int seed,
        CancellationToken ct,
        SyncModelWorldOptions? options = null,
        CoverageCounter? coverage = null,
        ConcurrentDictionary<string, (int Checked, int WithBasis)>? basisTally = null,
        CoverageCounter? outcomes = null,
        ConcurrentDictionary<UniqueSpelling, int>? spellingTally = null,
        ConcurrentDictionary<string, int>? preloadTally = null
    )
    {
        var generator = new SyncModelGenerator(seed);
        var setup = generator.Setup();
        await using var run = await SyncModelRun.StartAsync(
            setup,
            ct,
            (options ?? SyncModelWorldOptions.Direct) with
            {
                Devices = generator.Shape.Devices,
            },
            generator.Preloads(setup)
        );
        run.Log.Count.Should().BeGreaterThan(0);

        // 生成器の誤り（書き手の DB で成功しない手）は、書き込みの失敗と分けて名指しし、その種の残りの手を作らない
        ModelFailure? generatorFailure = null;

        for (var step = 0; step < StepsPerSeed; step++)
        {
            var states = new Dictionary<ModelActor, ModelDbState>();

            foreach (var actor in run.World.Actors)
            {
                states[actor] = await run.World.ReadStateAsync(actor, ct);
            }

            IReadOnlyList<ModelOp> ops;

            try
            {
                ops = generator.Next(states);
            }
            catch (SyncModelGeneratorContractException e)
            {
                generatorFailure = new(e.Signature, e.Message);
                break;
            }

            if (coverage is not null)
            {
                SyncModelDimensions.Observe(coverage, generator, ops);
            }

            foreach (var op in ops)
            {
                if (!await run.ExecuteAsync(op))
                {
                    break;
                }
            }
        }

        await run.FinishAsync();

        if (outcomes is not null)
        {
            SyncModelOutcomeDimensions.Observe(outcomes, run.Outcomes);
        }

        if (spellingTally is not null)
        {
            foreach (var (spelling, count) in run.SpellingRefusals)
            {
                spellingTally.AddOrUpdate(spelling, count, (_, sum) => sum + count);
            }
        }

        if (preloadTally is not null)
        {
            foreach (var (kind, count) in run.PreloadEffects)
            {
                preloadTally.AddOrUpdate(kind, count, (_, sum) => sum + count);
            }
        }

        if (basisTally is not null)
        {
            foreach (var (reason, (count, withBasis)) in run.BasisChecks)
            {
                basisTally.AddOrUpdate(
                    reason,
                    (count, withBasis),
                    (_, sum) => (sum.Checked + count, sum.WithBasis + withBasis)
                );
            }
        }

        return generatorFailure is null
            ? (run.Failures, run.Log)
            : ([.. run.Failures, generatorFailure], run.Log);
    }

    [Fact(
        DisplayName = "[Sync/モデル] 乱数の操作の列で、止まらず・収束し・消えない（既知の失敗の署名は除く）"
    )]
    [Trait(NightlyEngineTrait.Name, NightlyEngineTrait.Selected)]
    public async Task RandomSeeds_ConvergeWithoutLoss()
    {
        // 乱数の種では、途中の同期で起きたか静止化で起きたか、どの表で起きたかを区別しない（Family を参照）
        var known = SyncModelKnownFailures.Set;
        var results =
            new ConcurrentDictionary<
                int,
                (IReadOnlyList<ModelFailure> Failures, IReadOnlyList<string> Log)
            >();
        var elapsed = new ConcurrentDictionary<int, TimeSpan>();
        var plan = SeedSource.Resolve(Seeds);
        plan.WriteWarnings(output);
        var widePlan = SeedSource.Resolve(WideSeeds);
        widePlan.WriteWarnings(output);
        var coverage = new CoverageCounter(SyncModelDimensions.Table);
        var outcomes = new CoverageCounter(SyncModelOutcomeDimensions.Table);
        var basisTally = new ConcurrentDictionary<string, (int Checked, int WithBasis)>(
            StringComparer.Ordinal
        );
        var preloadTally = new ConcurrentDictionary<string, int>(StringComparer.Ordinal);

        var rowResults = new ConcurrentDictionary<
            string,
            (IReadOnlyList<ModelFailure> Failures, IReadOnlyList<string> Log)
        >(StringComparer.Ordinal);

        // 種ごとに世界（SQLite の DB 3 つ）を作るので、種どうしは何も共有しない。並列度は CPU 数で抑える
        await RunObservedAsync(
            [coverage, outcomes],
            async () =>
            {
                // 網羅の行（SyncModelCoverageRows）を種の前に流して数える（種を 1 つだけ流すときは流さない）。行も種と同じく
                // 世界を行ごとに作るので並列に流す。決定的なシナリオは別の Theory で流れ、網羅の計数に入らない
                if (!plan.IsSingle)
                {
                    await Parallel.ForEachAsync(
                        SyncModelCoverageRows.Table,
                        new ParallelOptions
                        {
                            MaxDegreeOfParallelism = Math.Clamp(Environment.ProcessorCount, 1, 8),
                            CancellationToken = Ct,
                        },
                        async (row, ct) =>
                            rowResults[row.Id] = await SyncModelCoverageRows.RunAsync(
                                row,
                                coverage,
                                outcomes,
                                ct
                            )
                    );
                }

                // 既存の範囲の種の後に、広い世界の種（端末 3 台）を同じ並列の列で流す
                await Parallel.ForEachAsync(
                    plan.Seeds.Concat(widePlan.Seeds),
                    new ParallelOptions
                    {
                        MaxDegreeOfParallelism = Math.Clamp(Environment.ProcessorCount, 1, 8),
                        CancellationToken = Ct,
                    },
                    async (seed, ct) =>
                    {
                        var watch = Stopwatch.StartNew();
                        results[seed] = await RunSeedAsync(
                            seed,
                            ct,
                            coverage: coverage,
                            basisTally: basisTally,
                            outcomes: outcomes,
                            preloadTally: preloadTally
                        );
                        elapsed[seed] = watch.Elapsed;
                    }
                );
            }
        );

        // 宣言の誤りは種の失敗より先に赤にする（Report が Observe の誤りを投げ直す）
        var coverageReport = coverage.Report();
        var outcomeReport = outcomes.Report();
        coverageReport.WriteSummary(output);
        coverageReport.WriteGapUsage(
            SyncModelDimensions.Gaps,
            SyncModelDimensions.DirectScope,
            output
        );
        outcomeReport.WriteSummary(output);
        outcomeReport.WriteGapUsage(
            SyncModelOutcomeDimensions.Gaps,
            SyncModelDimensions.DirectScope,
            output
        );

        var report = new EngineReport("SyncModel");
        var knownCounts = new SortedDictionary<string, int>(StringComparer.Ordinal);

        foreach (var (seed, (failures, log)) in results.OrderBy(p => p.Key))
        {
            // 広い世界の種は広い世界の種の決め方で照合し、再現の手順もそちらの変数で書く
            var seedPlan = SyncModelWorldShape.IsWide(seed) ? widePlan : plan;
            var classified = known.Classify(
                failures.Select(f => new EngineFailure(
                    SyncModelKnownFailures.RandomScope,
                    f.Signature,
                    f.Detail,
                    seed,
                    f
                )),
                seedPlan,
                scope => scope == SyncModelKnownFailures.RandomScope
            );

            foreach (
                var signature in classified
                    .Matched.Select(m => Family(m.Failure.Signature))
                    .Distinct()
            )
            {
                knownCounts[signature] = knownCounts.GetValueOrDefault(signature) + 1;
            }

            if (classified.Unknown.Count == 0)
            {
                continue;
            }

            report.Add(
                SeedFailure(seed, failures, classified, log, normalize: null),
                seedPlan.ReproductionFor(seed)
            );
        }

        // 網羅の行の違反は既知の失敗の一覧で外さない（どれも新しい失敗）
        foreach (
            var (id, (rowFailures, rowLog)) in rowResults
                .Where(r => r.Value.Failures.Count > 0)
                .OrderBy(r => r.Key, StringComparer.Ordinal)
        )
        {
            report.Add(
                new EngineFailure(
                    $"coverage/{id}",
                    rowFailures[0].Signature,
                    string.Join(
                        Environment.NewLine,
                        rowFailures.Select(f => $"{f.Signature}: {f.Detail}")
                    )
                        + Environment.NewLine
                        + string.Join(Environment.NewLine, rowLog)
                ),
                $"直結の乱数のテストを流す（網羅の行 `{id}` は種の前に必ず流れる）"
            );
        }

        output.WriteLine(
            $"seeds: {plan.Seeds.Count}, wide seeds: {widePlan.Seeds.Count}, coverage rows: {rowResults.Count}"
        );

        // 理由ごとの根拠の確認の件数（0 件の理由は確かめが一度も走っていない）
        foreach (
            var (reason, (count, withBasis)) in basisTally.OrderBy(
                p => p.Key,
                StringComparer.Ordinal
            )
        )
        {
            output.WriteLine($"refusal basis {reason}: checked {count}, with basis {withBasis}");
        }

        // 端末の最初の形が実際に効いた件数（0 件の形は空振り）
        foreach (var kind in SyncModelRun.PreloadEffectKinds)
        {
            output.WriteLine($"preload effect {kind}: {preloadTally.GetValueOrDefault(kind)}");
        }

        foreach (var (seed, time) in elapsed.OrderByDescending(p => p.Value).Take(5))
        {
            output.WriteLine($"slowest seed {seed}: {time.TotalSeconds:F1} s");
        }

        foreach (var (signature, count) in knownCounts)
        {
            output.WriteLine($"known {signature}: {count} seed(s)");
        }

        report
            .Failures.Should()
            .BeEmpty(
                "every failure of a random seed must be on the known list:\n" + report.Render()
            );

        // 既定の種の数で必ず起きる乱数の項目（Reproduction＝RegularSeeds）が、どの種でも起きなかったら赤（種ごとの照合は
        // 載っていない失敗だけを見るので、起きないことは全部の種をまとめて照合する）。照合するのは既存の範囲の種だけ（項目の
        // 再現の扱いは既存の範囲の既定の種の数で決めてある。広い世界の種は載っていない失敗だけを見る）
        known
            .Classify(
                results
                    .Where(p => !SyncModelWorldShape.IsWide(p.Key))
                    .SelectMany(p =>
                        p.Value.Failures.Select(f => new EngineFailure(
                            SyncModelKnownFailures.RandomScope,
                            f.Signature,
                            f.Detail,
                            p.Key,
                            f
                        ))
                    ),
                plan,
                scope => scope == SyncModelKnownFailures.RandomScope
            )
            .NotObserved.Select(entry => entry.Describe())
            .Should()
            .BeEmpty("a random-seed entry reproduced at the default seed count must still occur");

        // エンジンの結果を表明してから網羅を照合する（網羅の赤がエンジンの失敗を隠さない）
        // 網羅しない組の照合は、2 つの範囲がどちらも既定の種の数のときだけ（広い世界の種の数を変えた実行は照合しない）
        var gapPlan = widePlan.IsDefaultCount ? plan : widePlan;
        coverageReport.AssertGaps(
            SyncModelDimensions.Gaps,
            SyncModelDimensions.DirectScope,
            gapPlan
        );
        outcomeReport.AssertGaps(
            SyncModelOutcomeDimensions.Gaps,
            SyncModelDimensions.DirectScope,
            gapPlan
        );

        // 理由の根拠の確認が空振りしていない（既定の種の数で、どの理由も 1 回以上確かめ、根拠のある形を 1 回以上見る）
        if (plan.IsDefaultCount)
        {
            foreach (var reason in RefusalBasisKinds)
            {
                var (count, withBasis) = basisTally.GetValueOrDefault(reason);
                CoverageGuard.AtLeast($"refusal basis checks of {reason}", count, 1);
                CoverageGuard.AtLeast($"refusal basis found for {reason}", withBasis, 1);
            }
        }

        // 端末の最初の形が空振りしていない（広い世界の種が既定の種の数のとき、どの形も 1 回以上効く）
        if (widePlan.IsDefaultCount)
        {
            foreach (var kind in SyncModelRun.PreloadEffectKinds)
            {
                CoverageGuard.AtLeast(
                    $"preload effect {kind}",
                    preloadTally.GetValueOrDefault(kind),
                    1
                );
            }
        }
    }

    /// <summary>
    /// 種を流す本体を実行し、本体が例外で終わったときは先に網羅の宣言の誤りを確かめる（並列の種のどれかの
    /// <see cref="CoverageDeclarationException"/> が別の種の例外に隠れても、宣言の誤りとして赤にする）
    /// </summary>
    /// <param name="coverage">数える先</param>
    /// <param name="body">種を流す本体</param>
    internal static Task RunObservedAsync(CoverageCounter coverage, Func<Task> body) =>
        RunObservedAsync([coverage], body);

    /// <summary>数える先が複数のとき（どれかの宣言の誤りを先に赤にする）</summary>
    /// <param name="counters">数える先</param>
    /// <param name="body">種を流す本体</param>
    internal static async Task RunObservedAsync(
        IReadOnlyList<CoverageCounter> counters,
        Func<Task> body
    )
    {
        try
        {
            await body();
        }
        catch
        {
            // 宣言の誤りがあれば Report がそれを投げる（無ければ元の例外を投げ直す）
            foreach (var counter in counters)
            {
                _ = counter.Report();
            }

            throw;
        }
    }

    /// <summary>
    /// 既知の失敗に当たらない失敗のあった種を、報告の 1 件にする（その種の全失敗の [known|NEW] と操作の列）
    /// </summary>
    /// <param name="seed">種</param>
    /// <param name="failures">その種の失敗</param>
    /// <param name="classified">その種の失敗の照合の結果</param>
    /// <param name="log">その種の操作の列</param>
    /// <param name="normalize">照合に使った署名の正規化（書き添えるとき。直結は null）</param>
    internal static EngineFailure SeedFailure(
        int seed,
        IReadOnlyList<ModelFailure> failures,
        KnownReport classified,
        IReadOnlyList<string> log,
        Func<string, string>? normalize
    )
    {
        var unknown = classified
            .Unknown.Select(f => f.Source)
            .ToHashSet(ReferenceEqualityComparer.Instance);
        var detail = new StringBuilder();

        foreach (var failure in failures)
        {
            var mark = unknown.Contains(failure) ? "NEW" : "known";
            var normalized = normalize is null ? "" : $" -> {normalize(failure.Signature)}";
            detail.AppendLine(
                CultureInfo.InvariantCulture,
                $"[{mark}] {failure.Signature}{normalized}"
            );
            detail.AppendLine(CultureInfo.InvariantCulture, $"       {FirstLine(failure.Detail)}");
        }

        detail.AppendLine("ops:");

        foreach (var line in log)
        {
            detail.AppendLine(CultureInfo.InvariantCulture, $"  {line}");
        }

        return new EngineFailure(
            SyncModelKnownFailures.RandomScope,
            string.Join(", ", classified.Unknown.Select(f => f.Signature).Distinct()),
            detail.ToString().TrimEnd(),
            seed
        );
    }

    [Fact(
        DisplayName = "[Sync/モデル] 決定的なシナリオを分けたクラスの行は、転送ごとに流すシナリオをちょうど 1 回ずつ受け持つ"
    )]
    public void ScenarioParts_CoverEveryScenarioOncePerTransport()
    {
        // この名前空間の Theory の MemberData のうち、SyncModelScenarioParts.Names を呼ぶものを集める（分けたクラスと
        // 転送の本体のクラス）。分ける数を変えてクラスを足し忘れる・クラスを消す・同じ番号を 2 つ書く、のどれでも赤になる
        var parts = typeof(SyncModelRuntimeTests)
            .Assembly.GetTypes()
            .Where(type =>
                type.Namespace == typeof(SyncModelRuntimeTests).Namespace
                && type is { IsClass: true, IsAbstract: false, IsGenericTypeDefinition: false }
            )
            .SelectMany(type =>
                type.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                    .Where(method => method.GetCustomAttribute<TheoryAttribute>() is not null)
                    .SelectMany(method => method.GetCustomAttributes<MemberDataAttribute>())
                    .Select(data =>
                        (data.MemberType ?? type).GetMethod(
                            data.MemberName,
                            BindingFlags.Public | BindingFlags.Static,
                            Type.EmptyTypes
                        )
                    )
                    .Where(member => member?.ReturnType == typeof(TheoryData<string>))
                    .Select(member =>
                    {
                        TheoryData<string>? rows = null;
                        var calls = SyncModelScenarioParts.Record(() =>
                            rows = (TheoryData<string>)member!.Invoke(null, null)!
                        );
                        return (Class: type.Name, Calls: calls, Rows: rows!);
                    })
            )
            .Where(part => part.Calls.Count > 0)
            .ToList();

        var problems = new List<string>();
        problems.AddRange(
            parts
                .Where(part => part.Calls.Count != 1)
                .Select(part => $"{part.Class}: calls Names {part.Calls.Count} times")
        );

        foreach (var transport in new[] { SyncModelTransport.Direct, SyncModelTransport.Http })
        {
            var mine = parts
                .Where(part => part.Calls.Count == 1 && part.Calls[0].Transport == transport)
                .ToList();
            var expected = SyncModelScenarios
                .All.Where(s => s.RunsOn(transport))
                .Select(s => s.Name)
                .ToList();
            var rows = mine.SelectMany(part => part.Rows.Select(row => (string)row.Data)).ToList();

            if (mine.Count < 2)
            {
                problems.Add($"{transport}: only {mine.Count} class(es) take scenario rows");
            }

            problems.AddRange(
                mine.GroupBy(part => part.Calls[0].Part)
                    .Where(group => group.Count() > 1)
                    .Select(group =>
                        $"{transport}: part {group.Key} is taken by "
                        + string.Join(", ", group.Select(part => part.Class))
                    )
            );
            problems.AddRange(
                rows.GroupBy(row => row, StringComparer.Ordinal)
                    .Where(group => group.Count() > 1)
                    .Select(group => $"{transport}: '{group.Key}' runs {group.Count()} times")
            );
            problems.AddRange(
                expected
                    .Except(rows, StringComparer.Ordinal)
                    .Select(name => $"{transport}: '{name}' runs in no class")
            );
            problems.AddRange(
                rows.Except(expected, StringComparer.Ordinal)
                    .Select(name => $"{transport}: '{name}' does not run on this transport")
            );
        }

        problems.Should().BeEmpty();
    }

    [Fact(
        DisplayName = "[Sync/モデル] 割り込みの行は、書き手ごとに点 × ポリシー × 前段 × 割り込みの書き込み（サーバーの書き手は × 実行の種類 × 後段 × その後の失敗）の起こり得る組をそろえ、どの行も割り込みを 1 つだけ仕掛けてそのポリシーで同期する"
    )]
    public void InterruptScenarios_CoverEveryPointAndShape()
    {
        // 宣言の網（行の点と書き手）。点が実際に当たったかは、各行の実行が not-fired の違反で見る
        var declared = SyncModelScenarios
            .All.SelectMany(s =>
                s.Ops.OfType<ArmInterruptOp>()
                    .Select(arm =>
                        (
                            s.Name,
                            arm.Point,
                            Writer: arm.Writer == ModelActor.Server
                                ? SyncModelScenarios.InterruptWriter.Server
                                : SyncModelScenarios.InterruptWriter.Device
                        )
                    )
            )
            .ToList();
        // 期待する組は書き手ごとの軸の全積から、起こり得ない組だけを外したもの。外す規則をここで書き下ろし、宣言の規則が
        // 広く外しすぎたら赤にする。
        // 端末の書き手：行が無いのに更新・削除／行があるのに作り直し／ServerWins はサーバーへ書かない
        var expectedDevice = Enum.GetValues<ModelInterruptPoint>()
            .SelectMany(point =>
                new[] { SyncConflictPolicy.Collect, SyncConflictPolicy.ServerWins }.SelectMany(
                    policy =>
                        Enum.GetValues<SyncModelScenarios.InterruptPrelude>()
                            .SelectMany(prelude =>
                                Enum.GetValues<SyncModelScenarios.InterruptWrite>()
                                    .Select(write => (point, policy, prelude, write))
                            )
                )
            )
            .Where(cell =>
                !(
                    cell.policy == SyncConflictPolicy.ServerWins
                    && cell.point == ModelInterruptPoint.BeforeServerWrite
                )
                && (
                    cell.prelude == SyncModelScenarios.InterruptPrelude.Delete
                        ? cell.write == SyncModelScenarios.InterruptWrite.Recreate
                        : cell.write != SyncModelScenarios.InterruptWrite.Recreate
                )
            )
            .Select(cell => $"Device/{cell.point}/{cell.policy}/{cell.prelude}/{cell.write}")
            .ToList();
        expectedDevice.Should().HaveCount(35);
        // サーバーの書き手：サーバーへの書き込みの直前の点は来ない（端末が送るものの無い形）／消した行は端末で編集できない
        var expectedServer = Enum.GetValues<ModelInterruptPoint>()
            .SelectMany(point =>
                new[] { SyncConflictPolicy.Collect, SyncConflictPolicy.ServerWins }.SelectMany(
                    policy =>
                        Enum.GetValues<SyncModelScenarios.ServerInterruptPrelude>()
                            .SelectMany(prelude =>
                                Enum.GetValues<SyncModelScenarios.ServerInterruptWrite>()
                                    .SelectMany(write =>
                                        Enum.GetValues<SyncModelScenarios.InterruptRun>()
                                            .SelectMany(run =>
                                                Enum.GetValues<SyncModelScenarios.InterruptFollowUp>()
                                                    .SelectMany(followUp =>
                                                        Enum.GetValues<SyncModelScenarios.ServerInterruptFault>()
                                                            .Select(fault =>
                                                                (
                                                                    point,
                                                                    policy,
                                                                    prelude,
                                                                    write,
                                                                    run,
                                                                    followUp,
                                                                    fault
                                                                )
                                                            )
                                                    )
                                            )
                                    )
                            )
                )
            )
            // 次の取得の失敗は、割り込まれた同期が取得の後の点のバッチ（古い写し）を書いてから同じ表のダウンロードを例外で
            // 終える窓の水準なので、取得の後の点・更新・同期の組にだけある
            .Where(cell =>
                cell.point != ModelInterruptPoint.BeforeServerWrite
                && !(
                    cell.write == SyncModelScenarios.ServerInterruptWrite.Delete
                    && cell.followUp == SyncModelScenarios.InterruptFollowUp.EditThenSync
                )
                && (
                    cell.fault == SyncModelScenarios.ServerInterruptFault.None
                    || cell.point == ModelInterruptPoint.AfterServerFetchBeforeApply
                        && cell.write == SyncModelScenarios.ServerInterruptWrite.Update
                        && cell.run == SyncModelScenarios.InterruptRun.Sync
                )
            )
            .Select(cell =>
                $"Server/{cell.point}/{cell.policy}/{cell.prelude}/{cell.write}/{cell.run}/{cell.followUp}/{cell.fault}"
            )
            .ToList();
        expectedServer.Should().HaveCount(84);
        var expected = Enum.GetValues<SyncModelScenarios.InterruptWriter>()
            .SelectMany(writer =>
                writer == SyncModelScenarios.InterruptWriter.Device
                    ? expectedDevice
                    : expectedServer
            )
            .ToList();
        CoverageGuard.ContainsAll(
            "interrupt rows (writer x point x policy x prelude x write [x run x follow-up x fault])",
            SyncModelScenarios
                .InterruptCells.Where(cell =>
                    declared.Contains(
                        (cell.Name, cell.Point, SyncModelScenarios.InterruptWriter.Device)
                    )
                )
                .Select(cell => $"Device/{cell.Point}/{cell.Policy}/{cell.Prelude}/{cell.Write}")
                .Concat(
                    SyncModelScenarios
                        .ServerInterruptCells.Where(cell =>
                            declared.Contains(
                                (cell.Name, cell.Point, SyncModelScenarios.InterruptWriter.Server)
                            )
                        )
                        .Select(cell =>
                            $"Server/{cell.Point}/{cell.Policy}/{cell.Prelude}/{cell.Write}/{cell.Run}/{cell.FollowUp}/{cell.Fault}"
                        )
                ),
            expected
        );

        // 既知の失敗の一覧が名指しする 4m-D1 の行は、点 × 形の行の 1 つ
        ModelSyncScenarioNames
            .D1Items.Should()
            .Be(
                SyncModelScenarios.InterruptName(
                    ModelInterruptPoint.BeforeServerWrite,
                    SyncModelScenarios.DeleteRecreatedShape
                )
            );

        foreach (
            var scenario in SyncModelScenarios.All.Where(s => s.Ops.OfType<ArmInterruptOp>().Any())
        )
        {
            scenario.Ops.OfType<ArmInterruptOp>().Should().ContainSingle(scenario.Name);
            scenario.Ops.OfType<ModelSyncOp>().Should().NotBeEmpty(scenario.Name);
        }

        // ポリシーの軸は、行の同期が実際にそのポリシーで流れることまで見る（名前だけ ServerWins の行を作らない）
        foreach (var cell in SyncModelScenarios.InterruptCells)
        {
            SyncModelScenarios
                .Get(cell.Name)
                .Ops.OfType<ModelSyncOp>()
                .Should()
                .OnlyContain(sync => sync.Policy == cell.Policy, cell.Name);
        }

        // サーバーの書き手の行は、書き手・ポリシー・実行の種類が宣言どおりに流れることまで見る（割り込まれる実行は割り込みの
        // 後の最初の同期か洗い替え。洗い替えの行は後段の同期だけがポリシーを持つ）
        foreach (var cell in SyncModelScenarios.ServerInterruptCells)
        {
            var ops = SyncModelScenarios.Get(cell.Name).Ops;
            var arm = ops.OfType<ArmInterruptOp>().Single();
            arm.Writes.Should().OnlyContain(write => write.Actor == ModelActor.Server, cell.Name);
            arm.ThenArm.Should()
                .Be(
                    cell.Fault == SyncModelScenarios.ServerInterruptFault.NextFetchFails
                        ? ModelFaultKind.ServerFetch
                        : null,
                    cell.Name
                );
            ops.OfType<ModelSyncOp>()
                .Should()
                .NotBeEmpty(cell.Name)
                .And.OnlyContain(sync => sync.Policy == cell.Policy, cell.Name);
            var interrupted = ops.Skip(ops.ToList().IndexOf(arm) + 1)
                .First(op => op is ModelSyncOp or ModelRefreshOp);
            (interrupted is ModelRefreshOp)
                .Should()
                .Be(cell.Run == SyncModelScenarios.InterruptRun.Refresh, cell.Name);
            (interrupted is ModelSyncOp)
                .Should()
                .Be(cell.Run == SyncModelScenarios.InterruptRun.Sync, cell.Name);
        }
    }

    [Theory(
        DisplayName = "[Sync/モデル] 決定的なシナリオは、既知の失敗の一覧に載った署名の組どおりに失敗する（載っていなければ失敗しない）"
    )]
    [MemberData(nameof(ScenarioNames))]
    public Task Scenario_FailsExactlyAsListed(string name) => AssertFailsExactlyAsListed(name);

    /// <summary>決定的なシナリオを直結で流し、既知の失敗の一覧に載った署名の組どおりに失敗することを表明する</summary>
    internal static async Task AssertFailsExactlyAsListed(string name)
    {
        var (failures, log) = await SyncModelScenarios.Get(name).RunAsync(Ct);

        // 署名の組が一覧のそのシナリオの組と等しいこと（載っていない署名も、載っているのに出ない署名も赤）
        var problem = SyncModelKnownFailures
            .Set.Classify(
                failures
                    .GroupBy(f => f.Signature, StringComparer.Ordinal)
                    .Select(g => new EngineFailure(name, g.Key, FirstLine(g.First().Detail))),
                scope => scope == name
            )
            .Problem($"[Sync/モデル] シナリオ {name}");

        if (problem is not null)
        {
            throw new Xunit.Sdk.XunitException(
                problem + "\nops:\n" + string.Join("\n", log.Select(l => "  " + l))
            );
        }
    }

    /// <summary>
    /// N12 の対照：競合も ServerWins の破棄も削除も無いのに、blob が空でそろう形は、緩めた後も違反として数える。
    /// </summary>
    /// <remarks>
    /// blob を運ばない実行で新しい行を上げると、サーバーの行は blob が空のまま作られ（文書の割り切り＝運ぶかどうかは
    /// 端末ごとに一貫させる）、書いた端末の記録は blob なしで決着する。モデルの見逃しの条件はこれを根拠にしないので、
    /// 3/blob-empty が残る。一覧（All）には入れない＝わざと失敗させる形で、ここでだけ流す。
    /// </remarks>
    [Fact(
        DisplayName = "[Sync/モデル] 根拠の無い空の blob は、緩めた後も違反として数える（N12 の対照）"
    )]
    public async Task EmptyBlobWithoutConflict_IsStillAViolation()
    {
        var scenario = new SyncModelScenario(
            "n12-control-empty-blob-without-cause",
            [new AddFolderOp(ModelActor.Server, ModelWriteVia.Direct, 530, null, "f530")],
            [
                new AddItemOp(
                    ModelActor.Device1,
                    ModelWriteVia.Direct,
                    531,
                    530,
                    null,
                    "c-531",
                    "t531",
                    "0D0D0D0D"
                ),
                new ModelSyncOp(
                    ModelActor.Device1,
                    ModelSyncKind.Sync,
                    SyncMode.Versioned,
                    SyncConflictPolicy.Collect,
                    [],
                    false,
                    500
                ),
            ]
        );

        var (failures, _) = await scenario.RunAsync(Ct);

        failures.Select(f => f.Signature).Should().Contain("3/blob-empty:model_items");
    }

    /// <summary>
    /// N12 の対照（作り直し）：消された品目を上げ直した端末の記録が、競合にも ServerWins の破棄にもならないのに、blob が空で
    /// そろう形は、違反として数える。
    /// </summary>
    /// <remarks>
    /// blob を運ばない後勝ちの実行で、別の端末が消した品目を編集から作り直すと、サーバーの行は blob が空のまま作られ、記録は
    /// blob なしで決着する。上げ直した端末は書き手に数えるが、根拠（競合・破棄）が無いので見逃さない。
    /// </remarks>
    [Fact(
        DisplayName = "[Sync/モデル] 作り直した端末に根拠が無い空の blob は、違反として数える（N12 の対照）"
    )]
    public async Task EmptyBlobOfRecreatedRowWithoutConflict_IsStillAViolation()
    {
        var scenario = new SyncModelScenario(
            "n12-control-recreated-empty-blob-without-cause",
            [
                new AddFolderOp(ModelActor.Server, ModelWriteVia.Direct, 532, null, "f532"),
                new AddItemOp(
                    ModelActor.Server,
                    ModelWriteVia.Direct,
                    533,
                    532,
                    null,
                    "c-533",
                    "t533",
                    "0E0E0E0E"
                ),
            ],
            [
                new DeleteItemOp(ModelActor.Device2, ModelWriteVia.Direct, 533),
                new UpdateItemOp(
                    ModelActor.Device1,
                    ModelWriteVia.Direct,
                    533,
                    532,
                    null,
                    "c-533",
                    "d1"
                ),
                new ModelSyncOp(
                    ModelActor.Device2,
                    ModelSyncKind.Sync,
                    SyncMode.LastWriteWins,
                    SyncConflictPolicy.Collect,
                    [],
                    true,
                    500
                ),
                new ModelSyncOp(
                    ModelActor.Device1,
                    ModelSyncKind.Sync,
                    SyncMode.LastWriteWins,
                    SyncConflictPolicy.Collect,
                    [],
                    false,
                    500
                ),
            ]
        );

        var (failures, _) = await scenario.RunAsync(Ct);

        failures.Select(f => f.Signature).Should().Contain("3/blob-empty:model_items");
    }

    [Fact(
        DisplayName = "[Sync/モデル] 静止化の分類 holder-referenced-by-other-table は、名前の持ち主がサーバーに無く、別の表の参照する行のサーバーの行が値を取る行の下にある形だけを数える（対照）"
    )]
    public void OtherTableHolderShape_CountsOnlyTheNarrowShape()
    {
        // サーバー：フォルダー 3781（名前 h）とその子 3782、品目 1782 は 3782 の下。端末：フォルダー 1781（名前 h）の下に品目 1782
        static (ModelDbState Server, ModelDbState Local) Shape()
        {
            var server = new ModelDbState();
            server.Folders[3781] = (null, "h", null);
            server.Folders[3782] = (3781, "f3782", null);
            server.Folders[3790] = (null, "f3790", null);
            server.Items[1782] = (3782, null, "c-sv", "t", "00");
            var local = new ModelDbState();
            local.Folders[1781] = (null, "h", null);
            local.Folders[3790] = (null, "f3790", null);
            local.Items[1782] = (1781, null, "c-local", "t", "00");

            return (server, local);
        }

        string? Holder(
            (ModelDbState Server, ModelDbState Local) states,
            IEqualityComparer<string>? uniqueValues = null
        ) =>
            SyncModelRun
                .OtherTableHolder(
                    states.Server,
                    states.Local,
                    ModelTables.Folders,
                    "3781",
                    uniqueValues ?? StringComparer.Ordinal
                )
                ?.Holder;

        Holder(Shape()).Should().Be("1781", "the shape itself");

        var throughLabel = Shape();
        throughLabel.Local.Labels["lab"] = (1781, "caption");
        throughLabel.Local.Items[1782] = (3790, "lab", "c-local", "t", "00");
        Holder(throughLabel)
            .Should()
            .Be("1781", "the referring row reaches the holder through a row of a third table");

        var holderOnServer = Shape();
        holderOnServer.Server.Folders[1781] = (null, "other", null);
        Holder(holderOnServer).Should().BeNull("the holder is on the server");

        var notUnderTaker = Shape();
        notUnderTaker.Server.Items[1782] = (3790, null, "c-sv", "t", "00");
        Holder(notUnderTaker)
            .Should()
            .BeNull("the referring row's server copy is not under the taker");

        var referrerNotOnServer = Shape();
        referrerNotOnServer.Server.Items.Remove(1782);
        Holder(referrerNotOnServer).Should().BeNull("the referring row is not on the server");

        var sameTableReferrer = Shape();
        sameTableReferrer.Local.Items.Remove(1782);
        sameTableReferrer.Local.Folders[3782] = (1781, "f3782", null);
        Holder(sameTableReferrer)
            .Should()
            .BeNull(
                "a referrer in the same table is holder-referenced-by-taker's shape, not this one"
            );

        // 一意な値の同じさはサーバー役の比べ方で見る：持ち主の名前が大文字と末尾の空白だけ違う形は、実 SQL Server の比べ方では
        // 当たり、序数（SQLite のサーバー役）では当たらない
        var otherSpelling = Shape();
        otherSpelling.Local.Folders[1781] = (null, "H ", null);
        Holder(otherSpelling, SyncModelSqlServerUniqueValues.Instance)
            .Should()
            .Be("1781", "SQL Server takes the spellings for the same value");
        Holder(otherSpelling).Should().BeNull("the ordinal comparison takes them for two values");

        // 引き留める行の中に最新の写し（ローカルの行がサーバーの行と同じ）があれば、エンジンが消さない残りの形として分ける
        var currentChild = Shape();
        var eventAt = new DateTime(2026, 6, 1, 9, 0, 0, DateTimeKind.Unspecified);
        currentChild.Server.Events["e"] = (eventAt, 1782, "server-child");
        currentChild.Local.Events["e"] = (eventAt, 1782, "server-child");
        bool Current((ModelDbState Server, ModelDbState Local) states) =>
            SyncModelRun.HasCurrentReferrer(
                states.Server,
                states.Local,
                SyncModelRun
                    .OtherTableHolder(
                        states.Server,
                        states.Local,
                        ModelTables.Folders,
                        "3781",
                        StringComparer.Ordinal
                    )!
                    .Value.Referrers
            );
        Current(currentChild).Should().BeTrue("the event is the server's copy, up to date here");
        Current(Shape()).Should().BeFalse("the only referring row differs from its server copy");

        // 同じ比べ方を、同じ表の形（holder-referenced-by-taker）の分類も使う
        var takerShape = Shape();
        takerShape.Local.Items.Remove(1782);
        takerShape.Local.Folders[1781] = (null, "H ", null);
        takerShape.Local.Folders[3781] = (1781, "f3781", null);
        SyncModelRun
            .StuckShape(
                takerShape.Server,
                takerShape.Local,
                ModelTables.Folders,
                "3781",
                SyncModelSqlServerUniqueValues.Instance
            )
            .Should()
            .Be(("holder-referenced-by-taker", "1781"));
        SyncModelRun
            .StuckShape(
                takerShape.Server,
                takerShape.Local,
                ModelTables.Folders,
                "3781",
                StringComparer.Ordinal
            )
            .Should()
            .BeNull();
    }

    [Fact(
        DisplayName = "[Sync/モデル] 既知の失敗の一覧は、存在するシナリオを名指しし、指摘の番号と直す区切りを持つ"
    )]
    public void KnownFailures_AreWellFormed()
    {
        // 指摘の番号・署名の有無・重複は、一覧（KnownFailureSet）が構築時に検査する
        SyncModelKnownFailures.Set.Entries.Should().NotBeNull();
        var names = SyncModelScenarios.All.Select(s => s.Name).ToHashSet(StringComparer.Ordinal);

        foreach (var known in SyncModelKnownFailures.Scenarios)
        {
            names.Should().Contain(known.Scope);
            known.Match.Should().Be(KnownMatch.ExactSignature);

            if (known.Kind == KnownKind.Pending)
            {
                // 直す区切りの名前か、きっかけを待つ台帳の項目（「きっかけ待ち（台帳 {項目}）」）
                known
                    .FixedIn.Should()
                    .Match(fixedIn =>
                        fixedIn == "区切り 30"
                        || fixedIn == "区切り 31"
                        || fixedIn == "修正 C"
                        || fixedIn == SyncModelKnownFailures.StuckSyncRemainderFixedIn
                        || fixedIn.StartsWith("きっかけ待ち（台帳 ", StringComparison.Ordinal)
                            && fixedIn.EndsWith('）')
                    );
            }
        }
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
