using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using QuickER.Tests.TestSupport;
using QuickER.Tests.TestSupport.Engines;
using Xunit;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>
/// 転送経路・サーバー役をまたいで署名を比べるための正規化（同じ不具合が、経路やサーバーによって別の例外の型・文言で
/// 届くため）。
/// </summary>
/// <remarks>
/// 例外の署名は「入口のメソッド＋違反の種類（外部キー・一意制約・NOT NULL・その他）＋推定した形」へ、表明 2・3・5 は
/// 表の名前を外した形（<see cref="SyncModelRuntimeTests.Family"/>）へそろえる。HTTP 越しではサーバー側の例外が
/// 500 の応答として、実 SQL Server では SqlException として届き、どちらも直結・SQLite の例外と型も文言も違う。
/// </remarks>
internal static class SyncModelSignatures
{
    private static readonly Regex ThrowRegex = new(
        @"^1/(?:quiesce-)?throw:(?<phase>[^:]*):(?<rest>.*)$",
        RegexOptions.CultureInvariant | RegexOptions.Singleline
    );

    public static string Normalize(string signature)
    {
        var match = ThrowRegex.Match(signature);

        if (!match.Success)
        {
            return SyncModelRuntimeTests.Family(signature);
        }

        var phase = match.Groups["phase"].Value;
        var entry = phase.Contains('~', StringComparison.Ordinal)
            ? phase[..phase.IndexOf('~', StringComparison.Ordinal)]
            : phase.Split('>')[0].Split('.')[^1];
        var rest = match.Groups["rest"].Value;
        var diagnosis = Regex.Match(rest, @"\[(?:delete|apply):[^\]]*\]$").Value;
        var kind =
            rest.Contains("FOREIGN KEY", StringComparison.OrdinalIgnoreCase) ? "FK"
            : rest.Contains("UNIQUE", StringComparison.OrdinalIgnoreCase)
            || rest.Contains("duplicate key", StringComparison.OrdinalIgnoreCase)
                ? "UNIQUE"
            : rest.Contains("NOT NULL", StringComparison.OrdinalIgnoreCase)
            || rest.Contains("value NULL", StringComparison.OrdinalIgnoreCase)
                ? "NOTNULL"
            : "other";

        return $"1/throw:{entry}:{kind}{diagnosis}";
    }

    public static IReadOnlySet<string> Normalize(IEnumerable<ModelFailure> failures) =>
        failures.Select(f => Normalize(f.Signature)).ToHashSet(StringComparer.Ordinal);

    /// <summary>既知の失敗の一覧（乱数の種の照合用＝署名を <see cref="Normalize(string)"/> で正規化して照合する）</summary>
    /// <param name="extra">直結の一覧に足す決定的なシナリオの項目（SQL Server だけの既知の失敗）</param>
    public static KnownFailureSet Known(IReadOnlyList<KnownEntry> extra) =>
        SyncModelKnownFailures.Build(
            "SyncModel.transport",
            [.. SyncModelKnownFailures.Scenarios, .. extra],
            Normalize
        );

    /// <summary>夜間の既定の種の数</summary>
    public const int NightlySeedCount = 200;

    /// <summary>
    /// 派生（HTTP・実 SQL Server）で流す乱数の種（環境変数 QUICKER_SYNC_MODEL_TRANSPORT_SEEDS・既定 10・夜間は
    /// <see cref="NightlySeedCount"/>・0 から）
    /// </summary>
    public static SeedSettings Seeds { get; } =
        new("QUICKER_SYNC_MODEL_TRANSPORT_SEEDS", 10, NightlySeedCount, firstSeed: 0);

    /// <summary>夜間の広い世界の種の既定の数</summary>
    public const int NightlyWideSeedCount = 40;

    /// <summary>
    /// 派生で流す広い世界の種（端末 3 台・環境変数 QUICKER_SYNC_MODEL_TRANSPORT_WIDE_SEEDS・既定 4・夜間は
    /// <see cref="NightlyWideSeedCount"/>・<see cref="SyncModelWorldShape.WideSeedBase"/> から）
    /// </summary>
    public static SeedSettings WideSeeds { get; } =
        new(
            "QUICKER_SYNC_MODEL_TRANSPORT_WIDE_SEEDS",
            4,
            NightlyWideSeedCount,
            firstSeed: SyncModelWorldShape.WideSeedBase
        );

    /// <summary>
    /// 実 SQL Server の派生が流す種（失敗の注入なしの組＝種を 4 で割った余り 0・1 から、決め方の数だけ開始番号から順に）
    /// </summary>
    /// <param name="plan">決め方</param>
    public static IReadOnlyList<int> SqlServerSeeds(SeedPlan plan) =>
        Enumerable
            .Range(plan.Settings.FirstSeed, plan.Seeds.Count * 4)
            .Where(seed => seed % 4 is 0 or 1)
            .Take(plan.Seeds.Count)
            .ToList();

    /// <summary>種を並列に流し、既知の失敗（正規化）に当たらない失敗のある種を報告する</summary>
    /// <param name="ranges">
    /// 流す種の範囲（決め方と、その決め方で流す種。既存の範囲の種と広い世界の種＝<see cref="SyncModelWorldShape"/>。種ごとに、その範囲の
    /// 決め方で照合し再現の手順を書く）
    /// </param>
    /// <param name="options">世界の作り方</param>
    /// <param name="known">既知の失敗の一覧</param>
    /// <param name="parallelism">並列度</param>
    /// <param name="ct">取り消し</param>
    /// <param name="coverage">操作を次元表のケースとして数える先（観測と要約だけ＝網羅しない組は照合しない）</param>
    public static async Task<string> RunSeedsAsync(
        IReadOnlyList<(SeedPlan Plan, IReadOnlyList<int> Seeds)> ranges,
        SyncModelWorldOptions options,
        KnownFailureSet known,
        int parallelism,
        CancellationToken ct,
        CoverageCounter? coverage = null,
        ConcurrentDictionary<UniqueSpelling, int>? spellingTally = null
    )
    {
        var results =
            new ConcurrentDictionary<int, (IReadOnlyList<ModelFailure>, IReadOnlyList<string>)>();

        async Task RunAllAsync() =>
            await Parallel.ForEachAsync(
                ranges.SelectMany(range => range.Seeds),
                new ParallelOptions
                {
                    MaxDegreeOfParallelism = parallelism,
                    CancellationToken = ct,
                },
                async (seed, token) =>
                    results[seed] = await SyncModelRuntimeTests.RunSeedAsync(
                        seed,
                        token,
                        options,
                        coverage,
                        spellingTally: spellingTally
                    )
            );

        if (coverage is null)
        {
            await RunAllAsync();
        }
        else
        {
            await SyncModelRuntimeTests.RunObservedAsync(coverage, RunAllAsync);
        }

        var report = new EngineReport($"SyncModel.{options.Name}");

        foreach (var (seed, (failures, log)) in results.OrderBy(p => p.Key))
        {
            var plan = ranges.First(range => range.Seeds.Contains(seed)).Plan;
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

            if (classified.Unknown.Count == 0)
            {
                continue;
            }

            report.Add(
                SyncModelRuntimeTests.SeedFailure(seed, failures, classified, log, Normalize),
                plan.ReproductionFor(seed)
            );
        }

        return report.Failures.Count == 0 ? "" : $"{options.Name}:\n{report.Render()}";
    }
}

/// <summary>
/// 同期エンジンのモデルベーステストを HTTP 越し（in-process の Kestrel・生成された HTTP の差分ソースと同期専用の
/// エンドポイント）で流す派生。決定的なシナリオは直結と同じ違反（正規化した署名）になること、乱数の種は既知の失敗にしか
/// 当たらないことを確かめる。
/// </summary>
/// <remarks>
/// 直結に無いもの（アンカーの Base64 の往復・409 の競合の写し・バイナリのエンドポイントを通る blob のコピー）が、操作の
/// 列の組み合わせの中で直結と同じ結果になることを見る。サーバー側の失敗の注入は 500 として届く。
/// </remarks>
[Trait("Category", "Integration")]
[MatrixAxisValue(SyncModelTransportAxis.Name, nameof(SyncModelTransport.Http))]
[RuntimeCell(RuntimeFeatures.SyncSupport, RuntimeTarget.Remote, Server = RuntimeTarget.AdoSqlite)]
public sealed class SyncModelHttpRuntimeTests(ITestOutputHelper output) : ISyncModelTransportTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    private static readonly SyncModelWorldOptions Http = new() { Http = true, Name = "http" };

    SyncModelWorldOptions ISyncModelTransportTests.WorldOptions => Http;

    /// <summary>
    /// HTTP で流す決定的なシナリオの名前のうち、このクラスが受け持つ分（残りは <see cref="SyncModelScenarioParts"/> の
    /// 分けたクラスが受け持つ）
    /// </summary>
    public static TheoryData<string> ScenarioNames() =>
        SyncModelScenarioParts.Names(SyncModelTransport.Http, 0);

    [Theory(
        DisplayName = "[Sync/モデル/HTTP] 決定的なシナリオは、HTTP 越しでも直結と同じ違反（正規化した署名）になる"
    )]
    [MemberData(nameof(ScenarioNames))]
    public Task Scenario_MatchesDirect(string name) => AssertMatchesDirect(name);

    /// <summary>決定的なシナリオを直結と HTTP で流し、同じ違反（正規化した署名）になることを表明する</summary>
    internal static async Task AssertMatchesDirect(string name)
    {
        var scenario = SyncModelScenarios.Get(name);
        var (direct, _) = await scenario.RunAsync(Ct);
        var (http, log) = await scenario.RunAsync(Ct, Http);

        SyncModelSignatures
            .Normalize(http)
            .Should()
            .BeEquivalentTo(
                SyncModelSignatures.Normalize(direct),
                "HTTP must behave like the direct source.\nhttp failures:\n"
                    + string.Join("\n", http.Select(f => "  " + f.Signature))
                    + "\nops:\n"
                    + string.Join("\n", log.Select(l => "  " + l))
            );
    }

    [Fact(DisplayName = "[Sync/モデル/HTTP] 乱数の操作の列で、既知の失敗の外の違反が出ない")]
    [Trait(NightlyEngineTrait.Name, NightlyEngineTrait.Selected)]
    public async Task RandomSeeds_OnlyKnownFailures()
    {
        var plan = SeedSource.Resolve(SyncModelSignatures.Seeds);
        plan.WriteWarnings(output);
        var widePlan = SeedSource.Resolve(SyncModelSignatures.WideSeeds);
        widePlan.WriteWarnings(output);
        var coverage = new CoverageCounter(SyncModelDimensions.Table);
        var report = await SyncModelSignatures.RunSeedsAsync(
            [(plan, plan.Seeds), (widePlan, widePlan.Seeds)],
            Http,
            SyncModelSignatures.Known([]),
            Math.Clamp(Environment.ProcessorCount, 1, 4),
            Ct,
            coverage
        );

        // 網羅は観測と要約だけ（種が少なく、網羅しない組は直結の乱数の種で照合する）
        coverage.Report().WriteSummary(output);
        output.WriteLine(report);
        report.Should().BeEmpty();
    }
}

/// <summary>
/// 同期エンジンのモデルベーステストを、実 SQL Server のサーバー役（生成された SQL Server 方言のリポジトリと直結の差分
/// ソース・本物の rowversion と <c>MIN_ACTIVE_ROWVERSION()</c>）で流す派生（Docker）。
/// </summary>
/// <remarks>
/// <para>
/// サーバー側の失敗を仕掛けるシナリオと種の組（余り 2・3）は流さない（生成された差分ソースへ失敗の口をつながないため）。
/// </para>
/// <para>
/// SQL Server でだけ起きる既知の失敗（<see cref="SqlServerOnly"/>）は、直結の違反に足して比べる。一覧は厳格で、載せた
/// シナリオがそのとおりに失敗しなければ赤。
/// </para>
/// </remarks>
[Trait("Category", "Integration")]
[Trait("RequiresDocker", "true")]
[Collection(SqlServerContainerCollection.Name)]
[MatrixAxisValue(SyncModelTransportAxis.Name, nameof(SyncModelTransport.SqlServer))]
[RuntimeCell(RuntimeFeatures.SyncSupport, RuntimeTarget.AdoSqlServer)]
public sealed class SyncModelSqlServerRuntimeTests(
    SqlServerContainerFixture fixture,
    ITestOutputHelper output
) : ISyncModelTransportTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    /// <summary>SQL Server でだけ起きる既知の失敗（<see cref="SyncModelKnownFailures.Entry"/> で作る＝指摘の番号・直す区切り・シナリオ・署名）</summary>
    internal static IReadOnlyList<KnownEntry> SqlServerOnly { get; } = [];

    private SyncModelWorldOptions Options =>
        new()
        {
            CreateServer = async (_, ct) =>
                await SyncModelSqlServerServer.CreateAsync(fixture.ConnectionString, ct),
            Name = "sqlserver",
        };

    /// <summary>世界の組み方（コンテナにはサーバー役を作るときにだけ触れる）</summary>
    SyncModelWorldOptions ISyncModelTransportTests.WorldOptions => Options;

    public static TheoryData<string> ScenarioNames()
    {
        var data = new TheoryData<string>();

        foreach (
            var scenario in SyncModelScenarios.All.Where(s =>
                s.RunsOn(SyncModelTransport.SqlServer)
            )
        )
        {
            data.Add(scenario.Name);
        }

        return data;
    }

    [Theory(
        DisplayName = "[Sync/モデル/SqlServer] 決定的なシナリオは、実 SQL Server でも直結と同じ違反（＋SQL Server だけの既知の失敗）になる"
    )]
    [MemberData(nameof(ScenarioNames))]
    public async Task Scenario_MatchesDirect(string name)
    {
        Assert.SkipUnless(fixture.IsAvailable, fixture.UnavailableReason);

        var scenario = SyncModelScenarios.Get(name);
        var (direct, _) = await scenario.RunAsync(Ct);
        var (sqlServer, log) = await scenario.RunAsync(Ct, Options);
        var expected = SyncModelSignatures
            .Normalize(direct)
            .Concat(
                SqlServerOnly
                    .Where(k => k.Scope == name)
                    .Select(k => SyncModelSignatures.Normalize(k.Signature ?? ""))
            )
            .ToHashSet(StringComparer.Ordinal);

        SyncModelSignatures
            .Normalize(sqlServer)
            .Should()
            .BeEquivalentTo(
                expected,
                "SQL Server must behave like the direct source (plus the SQL Server only known failures).\n"
                    + "sqlserver failures:\n"
                    + string.Join("\n", sqlServer.Select(f => "  " + f.Signature))
                    + "\nops:\n"
                    + string.Join("\n", log.Select(l => "  " + l))
            );
    }

    [Fact(
        DisplayName = "[Sync/モデル/SqlServer] SQL Server だけの既知の失敗は、存在するシナリオを名指しする"
    )]
    public void SqlServerOnly_AreWellFormed()
    {
        // 指摘の番号・署名の有無・重複は、一覧（KnownFailureSet）が構築時に検査する
        new KnownFailureSet("SyncModel.sqlserver-only", SqlServerOnly)
            .Entries.Should()
            .NotBeNull();

        foreach (var known in SqlServerOnly)
        {
            SyncModelScenarios.All.Should().Contain(s => s.Name == known.Scope);
            known.Match.Should().Be(KnownMatch.ExactSignature);
            known.FixedIn.Should().BeOneOf("区切り 30", "区切り 31");
        }
    }

    [Fact(
        DisplayName = "[Sync/モデル/SqlServer] 乱数の操作の列（失敗の注入なしの組）で、既知の失敗の外の違反が出ない"
    )]
    [Trait(NightlyEngineTrait.Name, NightlyEngineTrait.Selected)]
    public async Task RandomSeeds_OnlyKnownFailures()
    {
        Assert.SkipUnless(fixture.IsAvailable, fixture.UnavailableReason);

        // 失敗の注入なしの組（余り 0・1）から、種の数だけ（範囲の開始番号から順に）。広い世界の種も同じ選び方
        var plan = SeedSource.Resolve(SyncModelSignatures.Seeds);
        plan.WriteWarnings(output);
        var widePlan = SeedSource.Resolve(SyncModelSignatures.WideSeeds);
        widePlan.WriteWarnings(output);
        var coverage = new CoverageCounter(SyncModelDimensions.Table);
        var spellingTally = new ConcurrentDictionary<UniqueSpelling, int>();
        var report = await SyncModelSignatures.RunSeedsAsync(
            [
                (plan, SyncModelSignatures.SqlServerSeeds(plan)),
                (widePlan, SyncModelSignatures.SqlServerSeeds(widePlan)),
            ],
            Options,
            SyncModelSignatures.Known(SqlServerOnly),
            2,
            Ct,
            coverage,
            spellingTally
        );

        // 網羅は観測と要約だけ（失敗の注入の組を流さず、網羅しない組は直結の乱数の種で照合する）
        coverage.Report().WriteSummary(output);
        output.WriteLine(report);

        foreach (var (spelling, count) in spellingTally.OrderBy(p => p.Key))
        {
            output.WriteLine($"refused spelled upserts {spelling}: {count}");
        }

        report.Should().BeEmpty();

        // 照合順序が同じとみなす別表記（大文字小文字・末尾の空白だけの違い）が、既定の種の数で実際に断られている
        // （網羅の計数は直結の種でしか照合しないので、実 SQL Server でだけ現れる断りはここで空振りを止める。同じ値〔Exact・
        // Empty〕はどのサーバー役でも断られるので、直結の種の網羅が受け持つ）
        if (plan.IsDefaultCount)
        {
            foreach (
                var spelling in new[]
                {
                    UniqueSpelling.AsciiCase,
                    UniqueSpelling.NonAsciiCase,
                    UniqueSpelling.TrailingSpace,
                }
            )
            {
                CoverageGuard.AtLeast(
                    $"spelled upserts refused by SQL Server ({spelling})",
                    spellingTally.GetValueOrDefault(spelling),
                    1
                );
            }
        }
    }
}
