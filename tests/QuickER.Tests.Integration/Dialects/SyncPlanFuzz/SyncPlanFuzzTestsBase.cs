using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using QuickER.Dialects;
using QuickER.Provider;
using QuickER.Tests.TestSupport.Engines;
using Xunit.Sdk;

namespace QuickER.Tests.Integration.Dialects.SyncPlanFuzz;

/// <summary>
/// 同期計画の総当たり。DB 側の図（とデータ）と目標の図・差分の選び方をランダムに作り、実 DB で
/// 「計画を適用すると成功するか、事前に警告が出ている」「適用後に再取込すると、選んだ項目の差分だけが消えている」を確かめる。
/// </summary>
/// <remarks>
/// <para>
/// 1 つの種の流れと表明の一覧は <see cref="SyncPlanFuzzRunner"/>、図・変更・選び方の作り方は
/// <see cref="SyncPlanFuzzGenerator"/>。生成器は自己参照・同じ列に外部キー 2 本・同じ列ペアに 2 本・計算列・
/// 大文字小文字だけ違う名前・名前付きの一意制約などの形（<see cref="FuzzShape.Required"/>）を一定の確率で必ず混ぜ、
/// 種の集合全体でそれらが全部出たことも表明する（生成器が形を作らなくなったら赤）。普段の種が少ない方言は、普段の種に
/// 出ない形を形の種（<see cref="ShapeSeeds"/>・<see cref="SyncPlanShapeSeeds"/>）で決定的に出して表明に足す。
/// </para>
/// <para>
/// 乱数の種は固定（1 から方言の既定の種の数まで。夜間はその方言の夜間の数まで）。既知の失敗（<see cref="SyncPlanFuzzKnownFailures"/>）は形で除き、失敗した種は種・方言・変更・選んだ項目・スクリプトを
/// まとめて報告する。生成器・ランナーの予期しない例外も、その種の失敗として記録して次の種へ進む（<see cref="SyncPlanFuzzRunner.RunGuardedAsync"/>）。環境変数 <c>QUICKER_SYNC_FUZZ_SEED</c> に種を書くとその種だけを流し（形の表明はしない）、
/// <c>QUICKER_SYNC_FUZZ_SEEDS</c> に数を書くと種の数を変えられる。
/// </para>
/// </remarks>
[Trait("Category", "Integration")]
public abstract class SyncPlanFuzzTestsBase
{
    /// <summary>派生クラスが受け持つ方言の軸の名前（値は <see cref="DialectNames"/>）</summary>
    public const string DialectAxis = "dialect";

    /// <summary>1 つの種だけを流す環境変数</summary>
    public const string SeedVariable = "QUICKER_SYNC_FUZZ_SEED";

    /// <summary>種の数を変える環境変数</summary>
    public const string SeedCountVariable = "QUICKER_SYNC_FUZZ_SEEDS";

    /// <summary>全種の結果をこのパスのファイルへ追記する環境変数（調べるとき用）</summary>
    public const string LogVariable = "QUICKER_SYNC_FUZZ_LOG";

    /// <summary>報告に全文を載せる失敗の数（残りは種の一覧だけ）</summary>
    private const int DetailedFailures = 8;

    /// <summary>必須の形（<see cref="FuzzShape.Required"/>）が全部出たことを表明する最小の種の数</summary>
    internal const int ShapeCoverageMinimumSeeds = 50;

    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    /// <summary>この方言で流す種の決め方（普段と夜間の既定の種の数）</summary>
    internal abstract SeedSettings SeedSettings { get; }

    /// <summary>
    /// この方言の形の種（普段の種の範囲の外で、普段の種に出ない実 DB の必須の形を出す種。<see cref="SyncPlanShapeSeeds"/>）
    /// </summary>
    internal virtual IReadOnlyList<FuzzShapeSeed> ShapeSeeds => [];

    /// <summary>方言の既定の種の数から、流す種の決め方を作る（1 から・単独の種は <see cref="SeedVariable"/>）</summary>
    /// <param name="regularCount">方言の普段の既定の種の数</param>
    /// <param name="nightlyCount">方言の夜間の既定の種の数</param>
    public static SeedSettings SeedSettingsFor(int regularCount, int nightlyCount) =>
        new(SeedCountVariable, regularCount, nightlyCount, singleVariable: SeedVariable);

    /// <summary>方言（DB）が使えないときのスキップ理由（使えるなら null）</summary>
    protected abstract string? UnavailableReason { get; }

    /// <summary>方言の部品を作る（DB には触れない＝方言の名前は Docker なしで読める）</summary>
    internal abstract SyncPlanFuzzDialect CreateDialect();

    /// <summary>
    /// 期待の行のうち実 DB の段を持つ行（<see cref="SyncPlanExpectationRows.ForRealDatabase"/>）を、この方言の実 DB で流す。
    /// 汎用の表明 1〜5 と、行の実 DB の段の期待を照合する（既知の失敗の一覧を通さない＝違反はすべて赤）。
    /// </summary>
    /// <remarks>
    /// 種を使わない（<see cref="SeedSource"/> を呼ばない）ので夜間の目印（<see cref="NightlyEngineTrait"/>）は付けない＝夜間の
    /// 絞り込み（<c>NightlyEngine=true</c>）には入らず、普段の CI で流す（行は決定的で、夜間に流し直しても結果は同じ）。
    /// 網羅の計数には数えない（決定的な行と同じ扱い）。
    /// </remarks>
    [Fact(
        DisplayName = "[Integration] 同期計画の期待の行（実 DB）: 汎用の表明と行の実 DB の期待を満たす"
    )]
    public async Task ExpectationRows_MatchOnRealDatabase()
    {
        Assert.SkipWhen(UnavailableReason is not null, UnavailableReason ?? string.Empty);

        var dialect = CreateDialect();

        try
        {
            var rows = SyncPlanExpectationRows.ForRealDatabase(dialect.Name).ToList();
            var problems = new List<string>();

            foreach (var row in rows)
            {
                problems.AddRange(await SyncPlanRealRunner.RunAsync(row, dialect, Ct));
            }

            TestContext.Current.TestOutputHelper?.WriteLine(
                $"{dialect.Name}: real-database expectation rows {rows.Count}, problems {problems.Count}"
            );

            if (problems.Count > 0)
            {
                throw new XunitException(string.Join("\n\n", problems));
            }

            if (SyncPlanExpectationRows.MinimumRealRowsFor(dialect.Name) is { } minimum)
            {
                CoverageGuard.AtLeast(
                    $"{dialect.Name}: 実 DB で照合した期待の行",
                    rows.Count,
                    minimum
                );
            }
            else
            {
                rows.Should().BeEmpty("実 DB の段の行の数の下限を書いていない方言は行を持たない");
            }
        }
        finally
        {
            (dialect as IDisposable)?.Dispose();
        }
    }

    [Fact(
        DisplayName = "[Integration] 同期計画の総当たり: 成功か事前の警告・選んだ差分だけが消える"
    )]
    [Trait(NightlyEngineTrait.Name, NightlyEngineTrait.Selected)]
    public async Task RandomPlans_SucceedOrWarn_AndApplyOnlySelectedItems()
    {
        Assert.SkipWhen(UnavailableReason is not null, UnavailableReason ?? string.Empty);

        var plan = SeedSource.Resolve(SeedSettings);
        plan.WriteWarnings();
        var seeds = plan.Seeds;

        var dialect = CreateDialect();
        var outcomes = new List<FuzzSeedOutcome>();
        var scenarioOutcomes = new List<FuzzSeedOutcome>();
        var coverageOutcomes = new List<FuzzSeedOutcome>();
        var shapeSeedOutcomes = new List<FuzzSeedOutcome>();
        var coverage = new CoverageCounter(SyncPlanFuzzDimensions.Table);

        try
        {
            // 決定的な行（SyncPlanScenarios）を乱数の種の前に流す（種を 1 つだけ流すときは流さない）。行は網羅の計数に入れない
            if (!plan.IsSingle)
            {
                // 形の行（D4）のうち 2 因子の網羅に要らない行は、夜間の実行でだけ流す
                foreach (
                    var row in SyncPlanScenarios
                        .For(dialect.Name)
                        .Where(row => row.Input.RegularRun || plan.Profile == EngineProfile.Nightly)
                )
                {
                    scenarioOutcomes.Add(
                        await SyncPlanFuzzRunner.RunGuardedAsync(
                            dialect.Name,
                            0,
                            () =>
                                SyncPlanFuzzRunner.RunCaseAsync(
                                    dialect,
                                    row.Input.Create(dialect),
                                    new FuzzSeedOutcome
                                    {
                                        Seed = 0,
                                        Dialect = dialect.Name,
                                        ScenarioId = row.Id,
                                    },
                                    Ct
                                ),
                            row.Id
                        )
                    );
                }

                // 網羅の行（SyncPlanCoverageRows）は、決定的な行と違って網羅の計数に数える（乱数の種で欠ける組を埋める行）
                foreach (var row in SyncPlanCoverageRows.ForRealDatabase(dialect.Name))
                {
                    var covered = await SyncPlanFuzzRunner.RunGuardedAsync(
                        dialect.Name,
                        0,
                        () =>
                            SyncPlanFuzzRunner.RunCaseAsync(
                                dialect,
                                SyncPlanCoverageRows.Create(row, dialect),
                                new FuzzSeedOutcome
                                {
                                    Seed = 0,
                                    Dialect = dialect.Name,
                                    ScenarioId = row.Id,
                                },
                                Ct
                            ),
                        row.Id
                    );
                    coverageOutcomes.Add(covered);
                    SyncPlanFuzzDimensions.Observe(coverage, covered);
                }
            }

            foreach (var seed in seeds)
            {
                var outcome = await SyncPlanFuzzRunner.RunGuardedAsync(
                    dialect.Name,
                    seed,
                    () => SyncPlanFuzzRunner.RunAsync(dialect, seed, Ct)
                );
                outcomes.Add(outcome);
                SyncPlanFuzzDimensions.Observe(coverage, outcome);
            }

            // 形の種は、流した種の範囲に無いものだけを乱数の種の後に流す（単独の種では流さない）
            foreach (var seed in SyncPlanShapeSeeds.ToRun(plan, ShapeSeeds))
            {
                var outcome = await SyncPlanFuzzRunner.RunGuardedAsync(
                    dialect.Name,
                    seed,
                    () => SyncPlanFuzzRunner.RunAsync(dialect, seed, Ct)
                );
                shapeSeedOutcomes.Add(outcome);
                SyncPlanFuzzDimensions.Observe(coverage, outcome);
            }
        }
        finally
        {
            (dialect as IDisposable)?.Dispose();
        }

        // 形の種は普通の種と同じく既知の失敗の照合へ渡す（範囲は方言名・再現は単独の種の変数）
        var failures = outcomes
            .Concat(shapeSeedOutcomes)
            .Where(o => o.Violations.Count > 0)
            .ToList();
        var output = TestContext.Current.TestOutputHelper;
        output?.WriteLine(
            $"{dialect.Name}: seeds {outcomes.Count}, committed {outcomes.Count(o => o.Committed)}, "
                + $"failed with a warning {outcomes.Count(o => !o.Committed && o.Violations.Count == 0)}, "
                + $"violations {outcomes.Count(o => o.Violations.Count > 0)}"
        );

        if (!plan.IsSingle && ShapeSeeds.Count > 0)
        {
            output?.WriteLine(
                $"{dialect.Name}: shape seeds declared [{string.Join(", ", ShapeSeeds.Select(s => s.Seed))}], "
                    + $"run after the seeds [{string.Join(", ", shapeSeedOutcomes.Select(o => o.Seed))}], "
                    + $"violations {shapeSeedOutcomes.Count(o => o.Violations.Count > 0)}"
            );
        }

        foreach (var scenario in scenarioOutcomes)
        {
            output?.WriteLine(
                $"{dialect.Name}: scenario {scenario.ScenarioId}: committed {scenario.Committed}, "
                    + $"violations {scenario.Violations.Count} ({SyncPlanScenarios.Table[scenario.ScenarioId!].Description})"
            );
        }

        output?.WriteLine(
            $"{dialect.Name}: coverage rows {coverageOutcomes.Count}, committed {coverageOutcomes.Count(o => o.Committed)}, "
                + $"violations {coverageOutcomes.Count(o => o.Violations.Count > 0)}"
        );

        // 表明 1b の例外の当たり方と、失敗した種の警告の内訳（警告の中身は測るだけ＝失敗を予告しない種類の警告しか無いのに
        // 失敗した種を数える。強めるかは件数を見て決める）
        foreach (
            var note in outcomes
                .SelectMany(o => o.Notes.Distinct())
                .GroupBy(n => n)
                .OrderBy(g => g.Key, StringComparer.Ordinal)
        )
        {
            output?.WriteLine(
                $"note {note.Key}: {note.Count()} seed(s) "
                    + $"[{string.Join(", ", outcomes.Where(o => o.Notes.Contains(note.Key)).Select(o => o.Seed))}]"
            );
        }

        var failedRuns = outcomes.Where(o => !o.Committed && o.Plan is not null).ToList();

        foreach (
            var kind in failedRuns
                .SelectMany(o => o.Plan!.Warnings.Select(w => w.Kind).Distinct())
                .GroupBy(k => k)
                .OrderBy(g => g.Key)
        )
        {
            output?.WriteLine($"failed run warned {kind.Key}: {kind.Count()} seed(s)");
        }

        var nonPredicting = failedRuns
            .Where(o =>
                o.Plan!.Warnings.Count > 0
                && o.Plan.Warnings.All(w =>
                    SyncPlanStaticRules.NonPredictingWarnings.Contains(w.Kind)
                )
            )
            .Select(o => o.Seed)
            .ToList();
        output?.WriteLine(
            $"failed runs warned only with non-predicting kinds: {nonPredicting.Count} [{string.Join(", ", nonPredicting)}]"
        );

        // 調べるとき用: 全種の結果をファイルへ書き出す（違反の無い種も含む）
        if (Environment.GetEnvironmentVariable(LogVariable) is { Length: > 0 } log)
        {
            await System.IO.File.AppendAllLinesAsync(
                log,
                scenarioOutcomes
                    .Concat(coverageOutcomes)
                    .Concat(outcomes)
                    .Concat(shapeSeedOutcomes)
                    .Select(o => o.Describe()),
                Ct
            );
        }

        // 網羅の宣言の誤りは種の結果より先に赤にする。要約はどの方言でも書く
        var coverageReport = coverage.Report();
        coverageReport.WriteSummary(output);
        // 網羅しない組の一覧は、普段の既定の種の数での網羅に合わせて書いてある（5 方言とも普段の数が閾値以上。普段の数が閾値を
        // 下回る方言を足したら、夜間に種が増えて閾値を超えても一覧が無いので照合しない）
        var checksGaps =
            !plan.IsSingle
            && seeds.Count >= ShapeCoverageMinimumSeeds
            && plan.Settings.RegularCount >= ShapeCoverageMinimumSeeds;

        if (checksGaps)
        {
            coverageReport.WriteGapUsage(SyncPlanFuzzDimensions.Gaps, dialect.Name, output);
        }

        // 既知の失敗の照合と必須の形の表明は、両方を集めてから 1 つの失敗にする（どちらかの赤がもう一方を隠さない）
        var problems = new List<string>();

        // 必須の形の表明の門は SyncPlanShapeSeeds.AssertsRequiredShapes（普段の既定の種の数が十分に多い方言か、形の種を
        // 宣言した方言だけ・流す種が普段の既定の種を全部含むときだけ）。表明の対象は種と形の種の形の和
        var assertsRequiredShapes = SyncPlanShapeSeeds.AssertsRequiredShapes(
            plan,
            ShapeCoverageMinimumSeeds,
            ShapeSeeds
        );

        if (
            !plan.IsSingle
            && !assertsRequiredShapes
            && SyncPlanShapeSeeds.HasShapeAssertion(plan, ShapeCoverageMinimumSeeds, ShapeSeeds)
        )
        {
            output?.WriteLine(
                $"{dialect.Name}: required shapes not asserted (seeds {seeds.Count} < regular default {plan.Settings.RegularCount})"
            );
        }

        if (assertsRequiredShapes)
        {
            try
            {
                CoverageGuard.ContainsAll(
                    $"{dialect.Name}: 種の集合全体に出た図の形（形の種を含む）",
                    outcomes.Concat(shapeSeedOutcomes).SelectMany(o => o.Shapes),
                    FuzzShape.RequiredFor(dialect, realDatabase: true)
                );
            }
            catch (XunitException ex)
            {
                problems.Add(ex.Message);
            }
        }

        // 形の種が宣言した形を出しているか（形の種は種の指紋の記録の外なので、これが記録の代わりの守り）。単独の種でない
        // 実行では、種の数に依らず必ず照合する（流れなかった形の種も名指しする）
        problems.AddRange(
            SyncPlanShapeSeeds.Problems(
                dialect.Name,
                plan,
                ShapeSeeds,
                outcomes.Concat(shapeSeedOutcomes)
            )
        );

        // 既知の失敗（形で名指しする）を除いた失敗だけを赤にする。一覧に載っているのに起きなかった項目も、
        // 既定の種の数で流したときは赤にする（直ったなら一覧から外す）
        var report = new EngineReport($"SyncPlan.{dialect.Name}", DetailedFailures);

        // 乱数の種の失敗を先に、決定的な行の失敗を後に並べる（報告の詳細は先頭から書く）
        foreach (var failure in failures)
        {
            report.Add(
                SyncPlanFuzzKnownFailures.ToFailure(failure),
                plan.ReproductionFor(failure.Seed)
            );
        }

        foreach (
            var failure in scenarioOutcomes
                .Concat(coverageOutcomes)
                .Where(o => o.Violations.Count > 0)
        )
        {
            report.Add(
                SyncPlanFuzzKnownFailures.ToFailure(failure),
                $"{dialect.Name} の同期計画の総当たりのテストを流す（行 `{failure.ScenarioId}` は種の前に必ず流れる。行だけを流す手段は無い）"
            );
        }

        var scenarioScope = SyncPlanScenarios.ScopeOf(dialect.Name);
        var known = SyncPlanFuzzKnownFailures.Set.Classify(
            report.Failures,
            plan,
            scope =>
                scope == dialect.Name
                || (!plan.IsSingle && scope == scenarioScope)
                || (
                    !plan.IsSingle
                    && plan.Profile == EngineProfile.Nightly
                    && scope == scenarioScope + SyncPlanScenarios.NightlyScopeSuffix
                )
        );
        report.WriteSummary(output, known);

        if (
            known.Problem(
                "every plan must either succeed or warn beforehand, and apply exactly the selected items. "
                    + $"Failed seeds: {string.Join(", ", known.Unknown.Select(f => f.Seed))}",
                report
            ) is
            { } knownProblem
        )
        {
            problems.Add(knownProblem);
        }

        // 合格側の行は、規則が失敗を予言し（偽陽性の守りが空振りしていない）、違反が 0
        problems.AddRange(SyncPlanScenarios.PassRowProblems(scenarioOutcomes));

        // 来歴の行の合格の行は、既定の同期がコミットし、取り込み直した差分が空（4m-F1 の行は既知の失敗の一覧が受け持つ）
        problems.AddRange(SyncPlanHistoryRows.RealProblems(scenarioOutcomes));

        // 網羅の行が、出すはずの変更の種類と強制した選び方を出している（出さなくなった行が網羅を黙って戻さない）
        problems.AddRange(SyncPlanCoverageRows.MissingKindProblems(coverageOutcomes));

        if (problems.Count > 0)
        {
            throw new XunitException(string.Join("\n\n", problems));
        }

        // 網羅しない組は、種の結果を表明してから照合する（網羅の赤が種の失敗を隠さない）。照合は種が十分に多い方言だけ
        // （SyncPlanFuzzDimensions の説明を参照）
        if (checksGaps)
        {
            coverageReport.AssertGaps(SyncPlanFuzzDimensions.Gaps, dialect.Name, plan);
        }
    }
}

/// <summary>形の種 1 つ（種と、その種が出すはずの実 DB の必須の形）</summary>
/// <param name="Seed">実 DB の種（方言の普段の種の範囲の外）</param>
/// <param name="Shapes">この種が出すはずの形（<see cref="FuzzShape.RequiredFor"/> の実 DB の形）</param>
internal sealed record FuzzShapeSeed(int Seed, IReadOnlyList<string> Shapes);

/// <summary>
/// 同期計画の総当たりの形の種（普段の既定の種の数が少ない方言で、普段の種に出ない実 DB の必須の形を決定的に出す種）の
/// 流し方と照合
/// </summary>
/// <remarks>
/// 形の種は、夜間の種の数で流した記録から、普段の種に欠ける必須の形を出す種を貪欲に選んだもの。単独の種でない実行では、
/// 流した種の範囲に無い形の種を乱数の種の後に流し（<see cref="ToRun"/>）、必須の形の表明の対象に足す。形の種は種の指紋の
/// 記録の外にあるので、宣言した形を出さなくなったら（生成器を変えて種のケースが変わったら）<see cref="Problems"/> が種を名指しして
/// 赤にする。単独の種の実行では流さず、照合もしない。
/// </remarks>
internal static class SyncPlanShapeSeeds
{
    /// <summary>形の種を宣言しているか、普段の既定の種の数が十分に多い（必須の形を表明しうる方言か）</summary>
    /// <param name="plan">流す種</param>
    /// <param name="minimumRegularSeeds">形の種なしで表明する普段の既定の種の数の下限</param>
    /// <param name="declared">方言の形の種</param>
    public static bool HasShapeAssertion(
        SeedPlan plan,
        int minimumRegularSeeds,
        IReadOnlyList<FuzzShapeSeed> declared
    ) => plan.Settings.RegularCount >= minimumRegularSeeds || declared.Count > 0;

    /// <summary>
    /// 必須の形を表明するか（単独の種でなく、表明しうる方言で、流す種が普段の既定の種を全部含む＝数が既定以上のとき）。
    /// 既定より少ない種を明示した実行（変異テストの SQLite の 100 種）では、確率でしか出ない形の欠けを赤にしない
    /// </summary>
    /// <param name="plan">流す種</param>
    /// <param name="minimumRegularSeeds">形の種なしで表明する普段の既定の種の数の下限</param>
    /// <param name="declared">方言の形の種</param>
    public static bool AssertsRequiredShapes(
        SeedPlan plan,
        int minimumRegularSeeds,
        IReadOnlyList<FuzzShapeSeed> declared
    ) =>
        !plan.IsSingle
        && HasShapeAssertion(plan, minimumRegularSeeds, declared)
        && plan.Seeds.Count >= plan.Settings.RegularCount;

    /// <summary>乱数の種の後に流す形の種（単独の種なら無し・流した種の範囲にある種は重ねて流さない）</summary>
    /// <param name="plan">流す種</param>
    /// <param name="declared">方言の形の種</param>
    public static IReadOnlyList<int> ToRun(SeedPlan plan, IReadOnlyList<FuzzShapeSeed> declared)
    {
        if (plan.IsSingle)
        {
            return [];
        }

        var seeds = plan.Seeds.ToHashSet();
        return declared
            .Select(s => s.Seed)
            .Where(seed => !seeds.Contains(seed))
            .Distinct()
            .ToList();
    }

    /// <summary>
    /// 形の種が宣言した形を出していない問題（単独の種なら無し）。形の種の結果が無い（流れなかった）ときも名指しする
    /// </summary>
    /// <param name="dialect">方言名</param>
    /// <param name="plan">流す種</param>
    /// <param name="declared">方言の形の種</param>
    /// <param name="outcomes">乱数の種と形の種の結果</param>
    public static IReadOnlyList<string> Problems(
        string dialect,
        SeedPlan plan,
        IReadOnlyList<FuzzShapeSeed> declared,
        IEnumerable<FuzzSeedOutcome> outcomes
    )
    {
        if (plan.IsSingle)
        {
            return [];
        }

        var bySeed = outcomes
            .Where(o => o.ScenarioId is null)
            .GroupBy(o => o.Seed)
            .ToDictionary(g => g.Key, g => g.First());
        var problems = new List<string>();

        foreach (var shapeSeed in declared)
        {
            if (!bySeed.TryGetValue(shapeSeed.Seed, out var outcome))
            {
                problems.Add($"{dialect}: shape seed {shapeSeed.Seed} was not run");
                continue;
            }

            var missing = shapeSeed.Shapes.Where(s => !outcome.Shapes.Contains(s)).ToList();

            if (missing.Count > 0)
            {
                problems.Add(
                    $"{dialect}: shape seed {shapeSeed.Seed} no longer produces [{string.Join(", ", missing)}] "
                        + $"(produced [{string.Join(", ", outcome.Shapes.Order(StringComparer.Ordinal))}]). "
                        + "Pick the shape seeds again from a nightly-count run (SyncPlanFuzz/CLAUDE.md「照合のまとめ方」)"
                );
            }
        }

        return problems;
    }
}

/// <summary>
/// 形の種の宣言と、流し方・照合の門（単独の種・既定・明示の数・夜間）を DB なしで固定する（形の種の照合が、種の指定の
/// 仕方で黙って外れないこと）
/// </summary>
public sealed class SyncPlanShapeSeedGuardTests
{
    /// <summary>形の種を宣言する方言 × 種の指定の仕方</summary>
    public static TheoryData<string, string> Modes()
    {
        var data = new TheoryData<string, string>();

        foreach (var dialect in new[] { DialectNames.Oracle })
        {
            foreach (
                var mode in new[]
                {
                    "single",
                    "default",
                    "explicit-fewer",
                    "explicit-more",
                    "nightly",
                }
            )
            {
                data.Add(dialect, mode);
            }
        }

        return data;
    }

    [Theory(
        DisplayName = "形の種は、単独の種では流さず照合もせず、ほかの指定では範囲の外だけを流して必ず照合する"
    )]
    [MemberData(nameof(Modes))]
    public void ShapeSeeds_RunAndAreCheckedUnlessSingle(string dialect, string mode)
    {
        var (settings, declared) = Declared(dialect);
        var plan = SeedSource.Resolve(
            settings,
            name =>
                mode switch
                {
                    "single" when name == SyncPlanFuzzTestsBase.SeedVariable => "7",
                    "explicit-fewer" when name == SyncPlanFuzzTestsBase.SeedCountVariable => "2",
                    "explicit-more" when name == SyncPlanFuzzTestsBase.SeedCountVariable => "80",
                    "nightly" when name == SeedSource.ProfileVariable => SeedSource.NightlyValue,
                    _ => null,
                }
        );
        var toRun = SyncPlanShapeSeeds.ToRun(plan, declared);
        var asserts = SyncPlanShapeSeeds.AssertsRequiredShapes(
            plan,
            SyncPlanFuzzTestsBase.ShapeCoverageMinimumSeeds,
            declared
        );
        var notRun = SyncPlanShapeSeeds.Problems(dialect, plan, declared, []);

        if (mode == "single")
        {
            toRun.Should().BeEmpty("単独の種では形の種を流さない");
            asserts.Should().BeFalse("単独の種では必須の形を表明しない");
            notRun.Should().BeEmpty("単独の種では形の種を照合しない");
            return;
        }

        // 流した種の範囲の外の形の種だけを流す（範囲の中の種は乱数の種の結果で照合する）
        toRun
            .Should()
            .Equal(declared.Select(s => s.Seed).Where(seed => !plan.Seeds.Contains(seed)));
        asserts
            .Should()
            .Be(mode != "explicit-fewer", "既定より少ない数を明示した実行では表明しない");

        // 形の種の結果が無ければ、種の数に依らず名指しする（黙って外れない）
        notRun.Should().Equal(declared.Select(s => $"{dialect}: shape seed {s.Seed} was not run"));

        // 宣言した形を出していれば問題は無く、1 つ欠ければその種を名指しする
        var produced = declared
            .Select(s =>
            {
                var outcome = new FuzzSeedOutcome { Seed = s.Seed, Dialect = dialect };
                outcome.Shapes.UnionWith(s.Shapes);
                return outcome;
            })
            .ToList();
        SyncPlanShapeSeeds.Problems(dialect, plan, declared, produced).Should().BeEmpty();
        produced[0].Shapes.Remove(declared[0].Shapes[0]);
        SyncPlanShapeSeeds
            .Problems(dialect, plan, declared, produced)
            .Should()
            .ContainSingle()
            .Which.Should()
            .Contain($"shape seed {declared[0].Seed} no longer produces [{declared[0].Shapes[0]}]");
    }

    [Theory(
        DisplayName = "形の種は普段の種の範囲の外にあり、方言の実 DB の必須の形だけを名指しする"
    )]
    [InlineData(DialectNames.Oracle)]
    public void ShapeSeeds_AreOutsideRegularSeedsAndNameRequiredShapes(string dialect)
    {
        var (settings, declared) = Declared(dialect);
        var required = FuzzShape.RequiredFor(
            EngineFingerprintDriftTests.CreateDialect(dialect),
            realDatabase: true
        );

        declared.Should().NotBeEmpty();
        declared.Select(s => s.Seed).Should().OnlyHaveUniqueItems();
        declared.Should().OnlyContain(s => s.Seed > settings.RegularCount && s.Shapes.Count > 0);
        declared.SelectMany(s => s.Shapes).Should().OnlyContain(shape => required.Contains(shape));
    }

    [Fact(
        DisplayName = "普段の種だけで必須の形がそろう方言は形の種を宣言せず、どの方言も既定の数で必須の形を表明する"
    )]
    public void DialectsWithManyRegularSeeds_DeclareNoShapeSeeds()
    {
        // fixture は渡さない（形の種と種の決め方は fixture に触れない）
        var withoutShapeSeeds = new SyncPlanFuzzTestsBase[]
        {
            new SqliteSyncPlanFuzzTests(),
            new MySqlSyncPlanFuzzTests(null!),
            new SqlServerSyncPlanFuzzTests(null!),
            new PostgreSqlSyncPlanFuzzTests(null!),
        };

        foreach (var tests in withoutShapeSeeds)
        {
            tests.ShapeSeeds.Should().BeEmpty();
        }

        // 必須の形の表明と網羅しない組の照合は、どの方言も普段の既定の種の数で動く（普段の数が門の下限を下回ると黙って外れる）
        foreach (var tests in withoutShapeSeeds.Append(new OracleSyncPlanFuzzTests(null!)))
        {
            tests
                .SeedSettings.RegularCount.Should()
                .BeGreaterThanOrEqualTo(SyncPlanFuzzTestsBase.ShapeCoverageMinimumSeeds);
            SyncPlanShapeSeeds
                .AssertsRequiredShapes(
                    SeedSource.Resolve(tests.SeedSettings, _ => null),
                    SyncPlanFuzzTestsBase.ShapeCoverageMinimumSeeds,
                    tests.ShapeSeeds
                )
                .Should()
                .BeTrue();
        }
    }

    private static (SeedSettings Settings, IReadOnlyList<FuzzShapeSeed> Declared) Declared(
        string dialect
    ) =>
        dialect switch
        {
            DialectNames.Oracle => (
                OracleSyncPlanFuzzTests.Seeds,
                OracleSyncPlanFuzzTests.ShapeSeedList
            ),
            _ => throw new ArgumentOutOfRangeException(nameof(dialect), dialect, null),
        };
}

/// <summary>SQLite（実ファイル・Docker 不要＝CI の両ジョブで常時実行）</summary>
[MatrixAxisValue(SyncPlanFuzzTestsBase.DialectAxis, DialectNames.Sqlite)]
public sealed class SqliteSyncPlanFuzzTests : SyncPlanFuzzTestsBase
{
    /// <summary>既定の種の数</summary>
    public const int DefaultSeeds = 2000;

    /// <summary>夜間の既定の種の数</summary>
    public const int NightlySeeds = 20000;

    /// <summary>種の決め方</summary>
    public static SeedSettings Seeds { get; } = SeedSettingsFor(DefaultSeeds, NightlySeeds);

    internal override SeedSettings SeedSettings => Seeds;

    protected override string? UnavailableReason => null;

    internal override SyncPlanFuzzDialect CreateDialect() => new SqliteSyncPlanFuzzDialect();
}

/// <summary>MySQL 8.4（Testcontainers）</summary>
[MatrixAxisValue(SyncPlanFuzzTestsBase.DialectAxis, DialectNames.MySql)]
[Collection(MySqlContainerCollection.Name)]
[Trait("RequiresDocker", "true")]
public sealed class MySqlSyncPlanFuzzTests(MySqlContainerFixture fixture) : SyncPlanFuzzTestsBase
{
    /// <summary>既定の種の数</summary>
    public const int DefaultSeeds = 150;

    /// <summary>夜間の既定の種の数</summary>
    public const int NightlySeeds = 1500;

    /// <summary>種の決め方</summary>
    public static SeedSettings Seeds { get; } = SeedSettingsFor(DefaultSeeds, NightlySeeds);

    internal override SeedSettings SeedSettings => Seeds;

    protected override string? UnavailableReason =>
        fixture.IsAvailable ? null : fixture.UnavailableReason;

    internal override SyncPlanFuzzDialect CreateDialect() => new MySqlSyncPlanFuzzDialect(fixture);
}

/// <summary>SQL Server 2022（Testcontainers）</summary>
[MatrixAxisValue(SyncPlanFuzzTestsBase.DialectAxis, DialectNames.SqlServer)]
[Collection(SqlServerContainerCollection.Name)]
[Trait("RequiresDocker", "true")]
public sealed class SqlServerSyncPlanFuzzTests(SqlServerContainerFixture fixture)
    : SyncPlanFuzzTestsBase
{
    /// <summary>既定の種の数</summary>
    public const int DefaultSeeds = 100;

    /// <summary>夜間の既定の種の数</summary>
    public const int NightlySeeds = 1000;

    /// <summary>種の決め方</summary>
    public static SeedSettings Seeds { get; } = SeedSettingsFor(DefaultSeeds, NightlySeeds);

    internal override SeedSettings SeedSettings => Seeds;

    protected override string? UnavailableReason =>
        fixture.IsAvailable ? null : fixture.UnavailableReason;

    internal override SyncPlanFuzzDialect CreateDialect() =>
        new SqlServerSyncPlanFuzzDialect(fixture);
}

/// <summary>PostgreSQL（Testcontainers）</summary>
/// <remarks>
/// 普段の種の数は、網羅しない組の照合と必須の形の表明の門（<see cref="SyncPlanFuzzTestsBase.ShapeCoverageMinimumSeeds"/>）を超える
/// 100（2026-11-14 の測定で 1 種あたり約 50 ms・600 種で 1 分。必須の形は 43 種までに全部出るので形の種は要らない）。
/// 普段の種で欠ける変更の種類と選び方の組は、網羅の行（<see cref="SyncPlanCoverageRows"/>）が埋める。
/// </remarks>
[MatrixAxisValue(SyncPlanFuzzTestsBase.DialectAxis, DialectNames.PostgreSql)]
[Collection(PostgreSqlContainerCollection.Name)]
[Trait("RequiresDocker", "true")]
public sealed class PostgreSqlSyncPlanFuzzTests(PostgreSqlContainerFixture fixture)
    : SyncPlanFuzzTestsBase
{
    /// <summary>既定の種の数</summary>
    public const int DefaultSeeds = 100;

    /// <summary>夜間の既定の種の数</summary>
    public const int NightlySeeds = 600;

    /// <summary>種の決め方</summary>
    public static SeedSettings Seeds { get; } = SeedSettingsFor(DefaultSeeds, NightlySeeds);

    internal override SeedSettings SeedSettings => Seeds;

    protected override string? UnavailableReason =>
        fixture.IsAvailable ? null : fixture.UnavailableReason;

    internal override SyncPlanFuzzDialect CreateDialect() =>
        new PostgreSqlSyncPlanFuzzDialect(fixture);
}

/// <summary>Oracle（Testcontainers）</summary>
/// <remarks>
/// Oracle は 1 種ごとの DDL・取込がほかの方言より遅い（2026-11-14 の測定で、決定的な行を含めて 300 種で 3 分 40 秒）。普段の種の数は
/// 網羅しない組の照合と必須の形の表明の門（<see cref="SyncPlanFuzzTestsBase.ShapeCoverageMinimumSeeds"/>）を少し超える 60 にとどめ、
/// 普段の種で欠ける変更の種類と選び方の組は網羅の行（<see cref="SyncPlanCoverageRows"/>、1 行は種 1 つ分）で埋める。
/// 普段の 60 種に出ない必須の形は形の種（<see cref="ShapeSeedList"/>）が出す。コンテナは Oracle のほかの統合テストと共有する。
/// </remarks>
[MatrixAxisValue(SyncPlanFuzzTestsBase.DialectAxis, DialectNames.Oracle)]
[Collection(OracleContainerCollection.Name)]
[Trait("RequiresDocker", "true")]
public sealed class OracleSyncPlanFuzzTests(OracleContainerFixture fixture) : SyncPlanFuzzTestsBase
{
    /// <summary>既定の種の数</summary>
    public const int DefaultSeeds = 60;

    /// <summary>夜間の既定の種の数</summary>
    public const int NightlySeeds = 120;

    /// <summary>種の決め方</summary>
    public static SeedSettings Seeds { get; } = SeedSettingsFor(DefaultSeeds, NightlySeeds);

    /// <summary>形の種（実 DB の 300 種の記録から選んだ。普段の 60 種に出ない形を出す）</summary>
    internal static IReadOnlyList<FuzzShapeSeed> ShapeSeedList { get; } =
    [new(102, [FuzzShape.DropUniqueMemberAlone])];

    internal override SeedSettings SeedSettings => Seeds;

    internal override IReadOnlyList<FuzzShapeSeed> ShapeSeeds => ShapeSeedList;

    protected override string? UnavailableReason =>
        fixture.IsAvailable ? null : fixture.UnavailableReason;

    internal override SyncPlanFuzzDialect CreateDialect() => new OracleSyncPlanFuzzDialect(fixture);
}
