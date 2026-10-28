using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using QuickER.Dialects;
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
/// 種の集合全体でそれらが全部出たことも表明する（生成器が形を作らなくなったら赤）。
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
    private const int ShapeCoverageMinimumSeeds = 50;

    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    /// <summary>この方言で流す種の決め方（普段と夜間の既定の種の数）</summary>
    internal abstract SeedSettings SeedSettings { get; }

    /// <summary>方言の既定の種の数から、流す種の決め方を作る（1 から・単独の種は <see cref="SeedVariable"/>）</summary>
    /// <param name="regularCount">方言の普段の既定の種の数</param>
    /// <param name="nightlyCount">方言の夜間の既定の種の数</param>
    public static SeedSettings SeedSettingsFor(int regularCount, int nightlyCount) =>
        new(SeedCountVariable, regularCount, nightlyCount, singleVariable: SeedVariable);

    /// <summary>方言（DB）が使えないときのスキップ理由（使えるなら null）</summary>
    protected abstract string? UnavailableReason { get; }

    /// <summary>方言の部品を作る（DB には触れない＝方言の名前は Docker なしで読める）</summary>
    internal abstract SyncPlanFuzzDialect CreateDialect();

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
        var coverage = new CoverageCounter(SyncPlanFuzzDimensions.Table);

        try
        {
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
        }
        finally
        {
            (dialect as IDisposable)?.Dispose();
        }

        var failures = outcomes.Where(o => o.Violations.Count > 0).ToList();
        var output = TestContext.Current.TestOutputHelper;
        output?.WriteLine(
            $"{dialect.Name}: seeds {outcomes.Count}, committed {outcomes.Count(o => o.Committed)}, "
                + $"failed with a warning {outcomes.Count(o => !o.Committed && o.Violations.Count == 0)}, "
                + $"violations {failures.Count}"
        );

        // 調べるとき用: 全種の結果をファイルへ書き出す（違反の無い種も含む）
        if (Environment.GetEnvironmentVariable(LogVariable) is { Length: > 0 } log)
        {
            await System.IO.File.AppendAllLinesAsync(log, outcomes.Select(o => o.Describe()), Ct);
        }

        // 網羅の宣言の誤りは種の結果より先に赤にする。要約はどの方言でも書く
        var coverageReport = coverage.Report();
        coverageReport.WriteSummary(output);
        // 網羅しない組の一覧は、普段の既定の種の数が十分に多い方言だけに書いてある。夜間に種が増えて閾値を超えた方言
        // （PostgreSQL・Oracle）は一覧が無いので照合しない
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

        // 必須の形の表明は、普段の既定の種の数が十分に多い方言だけ（少ない種では確率的に出ない形がある。夜間に種が増えた
        // 方言でも表明しない＝普段の CI で確かめていない形の欠けで夜間だけ赤にしない）
        if (
            !plan.IsSingle
            && seeds.Count >= ShapeCoverageMinimumSeeds
            && plan.Settings.RegularCount >= ShapeCoverageMinimumSeeds
        )
        {
            try
            {
                CoverageGuard.ContainsAll(
                    $"{dialect.Name}: 種の集合全体に出た図の形",
                    outcomes.SelectMany(o => o.Shapes),
                    FuzzShape.Required
                );
            }
            catch (XunitException ex)
            {
                problems.Add(ex.Message);
            }
        }

        // 既知の失敗（形で名指しする）を除いた失敗だけを赤にする。一覧に載っているのに起きなかった項目も、
        // 既定の種の数で流したときは赤にする（直ったなら一覧から外す）
        var report = new EngineReport($"SyncPlan.{dialect.Name}", DetailedFailures);

        foreach (var failure in failures)
        {
            report.Add(
                SyncPlanFuzzKnownFailures.ToFailure(failure),
                plan.ReproductionFor(failure.Seed)
            );
        }

        var known = SyncPlanFuzzKnownFailures.Set.Classify(
            report.Failures,
            plan,
            scope => scope == dialect.Name
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

/// <summary>PostgreSQL（Testcontainers・主キー・外部キーの逆引きの DO ブロックを通すため少ない種で流す）</summary>
[MatrixAxisValue(SyncPlanFuzzTestsBase.DialectAxis, DialectNames.PostgreSql)]
[Collection(PostgreSqlContainerCollection.Name)]
[Trait("RequiresDocker", "true")]
public sealed class PostgreSqlSyncPlanFuzzTests(PostgreSqlContainerFixture fixture)
    : SyncPlanFuzzTestsBase
{
    /// <summary>既定の種の数</summary>
    public const int DefaultSeeds = 15;

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

/// <summary>Oracle（Testcontainers・既定では少ない種だけ流す）</summary>
/// <remarks>
/// Oracle は 1 種ごとの DDL・取込がほかの方言より桁違いに遅く、ほかの方言と同じ種の数を流すと実行時間の予算を超える。
/// そのため既定では <see cref="DefaultOracleSeedCount"/> 種だけを常に流し（スキップにはしない＝厳格モードでスキップ 0 を保つ）、
/// 環境変数 <c>QUICKER_SYNC_FUZZ_ORACLE=1</c> のときに <see cref="ExtendedOracleSeedCount"/> 種へ増やす。
/// コンテナは Oracle のほかの統合テストと共有する。
/// </remarks>
[MatrixAxisValue(SyncPlanFuzzTestsBase.DialectAxis, DialectNames.Oracle)]
[Collection(OracleContainerCollection.Name)]
[Trait("RequiresDocker", "true")]
public sealed class OracleSyncPlanFuzzTests(OracleContainerFixture fixture) : SyncPlanFuzzTestsBase
{
    /// <summary>Oracle の種を増やす環境変数</summary>
    public const string ExtendVariable = "QUICKER_SYNC_FUZZ_ORACLE";

    /// <summary>既定で流す種の数</summary>
    public const int DefaultOracleSeedCount = 4;

    /// <summary><see cref="ExtendVariable"/> を立てたときの種の数</summary>
    public const int ExtendedOracleSeedCount = 30;

    /// <summary>夜間の既定の種の数（<see cref="ExtendedOracleSeedCount"/> 以上＝夜間は <see cref="ExtendVariable"/> に依らずこの数）</summary>
    public const int NightlySeeds = 120;

    /// <summary>種の決め方（<paramref name="extended"/> なら普段の数を <see cref="ExtendedOracleSeedCount"/> にする）</summary>
    /// <param name="extended"><see cref="ExtendVariable"/> を立てたか</param>
    public static SeedSettings SeedsFor(bool extended) =>
        SeedSettingsFor(extended ? ExtendedOracleSeedCount : DefaultOracleSeedCount, NightlySeeds);

    internal override SeedSettings SeedSettings =>
        SeedsFor(Environment.GetEnvironmentVariable(ExtendVariable) == "1");

    protected override string? UnavailableReason =>
        fixture.IsAvailable ? null : fixture.UnavailableReason;

    internal override SyncPlanFuzzDialect CreateDialect() => new OracleSyncPlanFuzzDialect(fixture);
}
