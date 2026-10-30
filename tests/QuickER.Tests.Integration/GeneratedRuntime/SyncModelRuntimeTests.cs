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
using QuickER.Tests.GeneratedSyncModelFixture;
using QuickER.Tests.TestSupport.Engines;
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
[MatrixAxisValue(SyncModelTransportAxis.Name, nameof(SyncModelTransport.Direct))]
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

    /// <summary>根拠を確かめる競合・決着の種類（<see cref="SyncModelRun.BasisChecks"/> の鍵）</summary>
    private static readonly string[] RefusalBasisKinds =
    [
        nameof(SyncConflictReason.DeleteRefusedOnServer),
        nameof(SyncConflictReason.UniqueValueTakenOnServer),
        nameof(SyncConflictReason.ParentMissingOnServer),
        SyncModelRun.SettledUpsertKind,
    ];

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
    internal static async Task<(
        IReadOnlyList<ModelFailure> Failures,
        IReadOnlyList<string> Log
    )> RunSeedAsync(
        int seed,
        CancellationToken ct,
        SyncModelWorldOptions? options = null,
        CoverageCounter? coverage = null,
        ConcurrentDictionary<string, (int Checked, int WithBasis)>? basisTally = null,
        CoverageCounter? outcomes = null
    )
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

            var ops = generator.Next(states);

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

        return (run.Failures, run.Log);
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
        var coverage = new CoverageCounter(SyncModelDimensions.Table);
        var outcomes = new CoverageCounter(SyncModelOutcomeDimensions.Table);
        var basisTally = new ConcurrentDictionary<string, (int Checked, int WithBasis)>(
            StringComparer.Ordinal
        );

        // 種ごとに世界（SQLite の DB 3 つ）を作るので、種どうしは何も共有しない。並列度は CPU 数で抑える
        await RunObservedAsync(
            [coverage, outcomes],
            () =>
                Parallel.ForEachAsync(
                    plan.Seeds,
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
                            outcomes: outcomes
                        );
                        elapsed[seed] = watch.Elapsed;
                    }
                )
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
            var classified = known.Classify(
                failures.Select(f => new EngineFailure(
                    SyncModelKnownFailures.RandomScope,
                    f.Signature,
                    f.Detail,
                    seed,
                    f
                )),
                plan,
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
                plan.ReproductionFor(seed)
            );
        }

        output.WriteLine($"seeds: {plan.Seeds.Count}");

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
        // 載っていない失敗だけを見るので、起きないことは全部の種をまとめて照合する）
        known
            .Classify(
                results.SelectMany(p =>
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
        coverageReport.AssertGaps(SyncModelDimensions.Gaps, SyncModelDimensions.DirectScope, plan);
        outcomeReport.AssertGaps(
            SyncModelOutcomeDimensions.Gaps,
            SyncModelDimensions.DirectScope,
            plan
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
        DisplayName = "[Sync/モデル] 割り込みの行は、点 × 形の全積をそろえ、どの行も割り込みを 1 つだけ仕掛けて同期する"
    )]
    public void InterruptScenarios_CoverEveryPointAndShape()
    {
        // 宣言の網（行の点と形）。点が実際に当たったかは、各行の実行が not-fired の違反で見る
        var declared = SyncModelScenarios
            .All.SelectMany(s => s.Ops.OfType<ArmInterruptOp>().Select(arm => (s.Name, arm.Point)))
            .ToList();
        CoverageGuard.ContainsAll(
            "interrupt rows (point x shape)",
            SyncModelScenarios
                .InterruptCells.Where(cell => declared.Contains((cell.Name, cell.Point)))
                .Select(cell => $"{cell.Point}/{cell.Shape}"),
            Enum.GetValues<ModelInterruptPoint>()
                .SelectMany(point =>
                    SyncModelScenarios.InterruptShapes.Select(shape => $"{point}/{shape}")
                )
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
    }

    [Theory(
        DisplayName = "[Sync/モデル] 決定的なシナリオは、既知の失敗の一覧に載った署名の組どおりに失敗する（載っていなければ失敗しない）"
    )]
    [MemberData(nameof(ScenarioNames))]
    public async Task Scenario_FailsExactlyAsListed(string name)
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
                known.FixedIn.Should().BeOneOf("区切り 30", "区切り 31", "修正 C");
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
