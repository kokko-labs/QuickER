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

    /// <summary>
    /// 1 つの種だけを流す環境変数（普通の種も広い世界の種もこの 1 つで流せる。解決は <see cref="ResolvePlans"/>）
    /// </summary>
    public const string SeedVariable = "QUICKER_SYNC_MODEL_TRANSPORT_SEED";

    /// <summary>
    /// 夜間の既定の種の数。夜間のジョブ（目安 50 分未満）の 3 夜の実測（2026-11-24・25）で、100＋広い世界 20 の種の乱数の Fact が
    /// 1 つ 0.4〜3.0 分・integration-nightly-rest のテストの段が 13.1〜21.1 分だったので、この数でも rest の段は遅いランナーで約 30 分に収まる
    /// </summary>
    public const int NightlySeedCount = 200;

    /// <summary>
    /// 派生（HTTP・実 SQL Server）で流す乱数の種の数（環境変数 QUICKER_SYNC_MODEL_TRANSPORT_SEEDS・既定 10・夜間は
    /// <see cref="NightlySeedCount"/>・0 から）。種の番号は転送ごとに <see cref="HttpSeeds(SeedPlan)"/>・
    /// <see cref="SqlServerSeeds(SeedPlan)"/>・<see cref="SqlServerFaultSeeds(SeedPlan)"/> が選ぶ
    /// </summary>
    public static SeedSettings Seeds { get; } =
        new(
            "QUICKER_SYNC_MODEL_TRANSPORT_SEEDS",
            10,
            NightlySeedCount,
            firstSeed: 0,
            singleVariable: SeedVariable
        );

    /// <summary>
    /// 夜間の広い世界の種の既定の数（<see cref="NightlySeedCount"/> と同じ実測から決めた）
    /// </summary>
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
            firstSeed: SyncModelWorldShape.WideSeedBase,
            singleVariable: SeedVariable
        );

    /// <summary>
    /// 普通の種と広い世界の種の決め方を解決する。単独の種が指定されたら、その種を番号の範囲が合う側の計画にだけ載せ、
    /// もう一方の計画の種は空にする（同じ種を 2 回流さない。世界の形は種の番号から決まる）
    /// </summary>
    /// <param name="seeds">普通の種の決め方</param>
    /// <param name="wideSeeds">広い世界の種の決め方（単独の種の変数は <paramref name="seeds"/> と同じ）</param>
    public static (SeedPlan Plan, SeedPlan WidePlan) ResolvePlans(
        SeedSettings seeds,
        SeedSettings wideSeeds
    )
    {
        var plan = SeedSource.Resolve(seeds);
        var widePlan = SeedSource.Resolve(wideSeeds);

        if (!plan.IsSingle || !widePlan.IsSingle)
        {
            return (plan, widePlan);
        }

        return SyncModelWorldShape.IsWide(plan.Seeds[0])
            ? (plan with { Seeds = [] }, widePlan)
            : (plan, widePlan with { Seeds = [] });
    }

    /// <summary>HTTP の派生が流す種の間隔（<see cref="HttpSeeds(int, int)"/>）</summary>
    public const int HttpSeedStride = 5;

    /// <summary>HTTP の派生が流す種（単独の種はその種。ほかは <see cref="HttpSeeds(int, int)"/>）</summary>
    /// <param name="plan">決め方</param>
    public static IReadOnlyList<int> HttpSeeds(SeedPlan plan) =>
        plan.IsSingle ? plan.Seeds : HttpSeeds(plan.Settings.FirstSeed, plan.Seeds.Count);

    /// <summary>
    /// HTTP の派生が流す種（開始番号から <see cref="HttpSeedStride"/> 刻みに <paramref name="count"/> 個）
    /// </summary>
    /// <remarks>
    /// 生成器はモード×ポリシーを種を 4 で割った商から 6 組へ順に、種の組（Extras・Faults）を 4 で割った余りから決める
    /// （<see cref="SyncModelGenerator"/>）。続いた番号から取ると、普段の 10 種が Versioned の 3 組だけになる。5 は 4 とも 24 とも
    /// 互いに素なので、5 刻みの 10 種は 6 組と 4 つの組を全部作る（<c>EngineFingerprintDriftTests.SyncModel_TransportSeedsAreRecorded</c> が
    /// 生成器のコンストラクタで確かめる）
    /// </remarks>
    /// <param name="first">開始番号</param>
    /// <param name="count">数</param>
    public static IReadOnlyList<int> HttpSeeds(int first, int count) =>
        [.. Enumerable.Range(0, count).Select(index => first + HttpSeedStride * index)];

    /// <summary>
    /// 実 SQL Server の派生が流す種（失敗の注入なしの組＝種を 4 で割った余り 0・1。単独の種はその余りのときだけ。ほかは
    /// <see cref="SqlServerSeeds(int, int)"/>）
    /// </summary>
    /// <param name="plan">決め方</param>
    public static IReadOnlyList<int> SqlServerSeeds(SeedPlan plan) =>
        plan.IsSingle
            ? [.. plan.Seeds.Where(seed => seed % 4 is 0 or 1)]
            : SqlServerSeeds(plan.Settings.FirstSeed, plan.Seeds.Count);

    /// <summary>
    /// 実 SQL Server の派生が流す失敗の注入なしの種（j 番目〔0 から〕が開始番号＋4j＋j を 2 で割った余り。開始番号は 4 の倍数）
    /// </summary>
    /// <remarks>
    /// 商（モード×ポリシー）が 1 種ごとに進むので、普段の 10 種で 6 組を全部作り、余りが 0（足さない）と 1（Extras）を交互に取る
    /// （続いた番号から余り 0・1 だけを取ると、商が 2 種ごとにしか進まず、普段の 10 種で 後勝ち × LocalWins を作らない）
    /// </remarks>
    /// <param name="first">開始番号</param>
    /// <param name="count">数</param>
    public static IReadOnlyList<int> SqlServerSeeds(int first, int count) =>
        [.. Enumerable.Range(0, count).Select(index => first + 4 * index + index % 2)];

    /// <summary>
    /// 実 SQL Server の派生が流す失敗の注入の組の種（種を 4 で割った余り 2・3。単独の種はその余りのときだけ。ほかは
    /// <see cref="SqlServerFaultSeeds(int, int)"/>）
    /// </summary>
    /// <param name="plan">決め方</param>
    public static IReadOnlyList<int> SqlServerFaultSeeds(SeedPlan plan) =>
        plan.IsSingle
            ? [.. plan.Seeds.Where(seed => seed % 4 is 2 or 3)]
            : SqlServerFaultSeeds(plan.Settings.FirstSeed, plan.Seeds.Count);

    /// <summary>
    /// 実 SQL Server の派生が流す失敗の注入の組の種（j 番目が開始番号＋4j＋2＋j を 2 で割った余り。決め方は
    /// <see cref="SqlServerSeeds(int, int)"/> と同じで、余りが 2〔Faults〕と 3〔両方〕を交互に取る）
    /// </summary>
    /// <param name="first">開始番号</param>
    /// <param name="count">数</param>
    public static IReadOnlyList<int> SqlServerFaultSeeds(int first, int count) =>
        [.. Enumerable.Range(0, count).Select(index => first + 4 * index + 2 + index % 2)];

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
        // 単独の種で、この組（失敗の注入なし・あり）が受け持たない種なら何も流さない（もう一方のテストが流す）
        if (
            ranges.Count > 0
            && ranges[0].Plan.IsSingle
            && ranges.All(range => range.Seeds.Count == 0)
        )
        {
            TestContext.Current.TestOutputHelper?.WriteLine(
                "この種はもう一方のテスト（失敗の注入なし・ありの組の片方）が受け持つので、このテストでは流さない"
            );
            return "";
        }

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
/// 直結のほかの転送（HTTP・実 SQL Server・HTTP × 実 SQL Server）のテストが使う、新しいエンジンの端末
/// （<see cref="SyncModelEngineDevice"/>）の既知の失敗の一覧
/// </summary>
internal static class SyncModelEngineTransports
{
    /// <summary>
    /// 転送の乱数の種の既知の失敗の一覧（署名は <see cref="SyncModelSignatures.Normalize(string)"/> で正規化して照合する）。空で
    /// 始める（載せる項目は、新しいエンジンの端末で出た失敗から理由つきで足す）
    /// </summary>
    public static KnownFailureSet Known { get; } =
        SyncModelKnownFailures.Build(
            "SyncModel.transport.engine",
            [],
            SyncModelSignatures.Normalize
        );

    /// <summary>
    /// SQL Server でだけ起きる既知の失敗（<see cref="SyncModelKnownFailures.Entry"/> で作る＝指摘の番号・直す区切り・シナリオ・
    /// 署名）。決定的なシナリオの比較では直結の違反に足して比べる。一覧は厳格で、載せたシナリオがそのとおりに失敗しなければ赤。
    /// 空で始める
    /// </summary>
    public static IReadOnlyList<KnownEntry> SqlServerOnly { get; } = [];
}

/// <summary>
/// 同期エンジンのモデルベーステストを HTTP 越し（in-process の Kestrel・生成された HTTP の差分ソースと同期専用の
/// エンドポイント）で流す派生。端末は新しいエンジンの端末（<see cref="SyncModelEngineDevice"/>）。決定的なシナリオは直結の
/// 新しいエンジンの端末と同じ違反（正規化した署名）になること、乱数の種は既知の失敗
/// （<see cref="SyncModelEngineTransports.Known"/>）にしか当たらないことを確かめる。
/// </summary>
/// <remarks>
/// 直結に無いもの（アンカーの Base64 の往復・409 の競合の写し・バイナリのエンドポイントを通る blob のコピー）が、操作の
/// 列の組み合わせの中で直結と同じ結果になることを見る。サーバー側の失敗の注入は 500 として届く。
/// </remarks>
[Trait("Category", "Integration")]
[MatrixAxisValue(SyncModelTransportAxis.Name, nameof(SyncModelTransport.Http))]
[RuntimeCell(RuntimeFeatures.SyncSupport, RuntimeTarget.Remote, Server = RuntimeTarget.AdoSqlite)]
[RuntimeCell(RuntimeFeatures.SyncRefresh, RuntimeTarget.Remote, Server = RuntimeTarget.AdoSqlite)]
public sealed class SyncModelHttpRuntimeTests(ITestOutputHelper output) : ISyncModelTransportTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    /// <summary>テストが流す世界の組み方（転送とサーバー役。端末は既定の新しいエンジンの端末。転送の軸の名乗りの照合も読む）</summary>
    internal static readonly SyncModelWorldOptions Http = new() { Http = true, Name = "http" };

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

    /// <summary>
    /// 決定的なシナリオを直結と HTTP で、どちらも新しいエンジンの端末で流し、同じ違反（正規化した署名）になることを表明する。
    /// 直結の側は <see cref="SyncModelDirectRuns"/> の結果（違反の無い結果はプロセスの中で 1 回だけ流す）
    /// </summary>
    internal static async Task AssertMatchesDirect(string name)
    {
        var scenario = SyncModelScenarios.Get(name);
        var (direct, _) = await SyncModelDirectRuns.RunAsync(scenario, Ct);
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
        var (plan, widePlan) = SyncModelSignatures.ResolvePlans(
            SyncModelSignatures.Seeds,
            SyncModelSignatures.WideSeeds
        );
        plan.WriteWarnings(output);
        widePlan.WriteWarnings(output);
        var coverage = new CoverageCounter(SyncModelDimensions.Table);
        var report = await SyncModelSignatures.RunSeedsAsync(
            [
                (plan, SyncModelSignatures.HttpSeeds(plan)),
                (widePlan, SyncModelSignatures.HttpSeeds(widePlan)),
            ],
            Http,
            SyncModelEngineTransports.Known,
            Math.Clamp(Environment.ProcessorCount, 1, 4),
            Ct,
            coverage
        );

        // 網羅は観測と要約だけ（種が少なく、網羅しない組は新しいエンジンの端末の直結の乱数の種で照合する）
        coverage.Report().WriteSummary(output);
        output.WriteLine(report);
        report.Should().BeEmpty();
    }
}

/// <summary>
/// 同期エンジンのモデルベーステストを、実 SQL Server のサーバー役（生成された SQL Server 方言のリポジトリと直結の差分
/// ソース・本物の rowversion と <c>MIN_ACTIVE_ROWVERSION()</c>）で流す派生（Docker）。端末は新しいエンジンの端末
/// （<see cref="SyncModelEngineDevice"/>）。
/// </summary>
/// <remarks>
/// <para>
/// サーバー側の失敗の注入と割り込みの点は、生成物の外側の包み（<see cref="SyncModelFaultingRepository{TEntity, TKey}"/> ほか）が
/// SQLite の代役と同じ点で起こす。乱数の種は、失敗の注入なしの組（余り 0・1）と注入の組（余り 2・3）を別の Fact で流し、既知の失敗の
/// 一覧は <see cref="SyncModelEngineTransports.Known"/>。取得の上限は実行の始めに固定する
/// （<see cref="ISyncModelServerRole.FixesFetchCeilingAtRunStart"/>）ので、実行の途中にサーバーへ書かれた行は次の実行で降りる
/// （決まった形の行の期待は <see cref="WhenServerFixesCeilingOp"/> で書く）。
/// </para>
/// <para>
/// 決定的なシナリオは直結の新しいエンジンの端末と比べ、SQL Server でだけ起きる既知の失敗
/// （<see cref="SyncModelEngineTransports.SqlServerOnly"/>）を直結の違反に足す。
/// </para>
/// </remarks>
[Trait("Category", "Integration")]
[Trait("RequiresDocker", "true")]
[Collection(SqlServerSecondContainerCollection.Name)]
[MatrixAxisValue(SyncModelTransportAxis.Name, nameof(SyncModelTransport.SqlServer))]
[RuntimeCell(RuntimeFeatures.SyncSupport, RuntimeTarget.AdoSqlServer)]
[RuntimeCell(RuntimeFeatures.SyncRefresh, RuntimeTarget.AdoSqlServer)]
public sealed class SyncModelSqlServerRuntimeTests(
    SqlServerContainerFixture fixture,
    ITestOutputHelper output
) : ISyncModelTransportTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    private SyncModelWorldOptions Options => OptionsFor(fixture);

    /// <summary>
    /// 実 SQL Server のサーバー役と既定の新しいエンジンの端末で世界を組む設定（分けたシナリオのクラスと共有する。転送の軸の
    /// 名乗りの照合も読む）
    /// </summary>
    internal static SyncModelWorldOptions OptionsFor(SqlServerContainerFixture fixture) =>
        new()
        {
            CreateServer = async (faults, ct) =>
                await SyncModelSqlServerServer.CreateAsync(fixture.ConnectionString, faults, ct),
            Name = "sqlserver",
        };

    /// <summary>転送とサーバー役の組み方（コンテナにはサーバー役を作るときにだけ触れる）</summary>
    SyncModelWorldOptions ISyncModelTransportTests.WorldOptions => Options;

    /// <summary>
    /// 実 SQL Server で流す決定的なシナリオの名前のうち、このクラスが受け持つ分（残りは <see cref="SyncModelScenarioParts"/> の
    /// 分けたクラス）
    /// </summary>
    public static TheoryData<string> ScenarioNames() =>
        SyncModelScenarioParts.Names(SyncModelTransport.SqlServer, 0);

    [Theory(
        DisplayName = "[Sync/モデル/SqlServer] 決定的なシナリオは、実 SQL Server でも直結と同じ違反（＋SQL Server だけの既知の失敗）になる"
    )]
    [MemberData(nameof(ScenarioNames))]
    public async Task Scenario_MatchesDirect(string name)
    {
        Assert.SkipUnless(fixture.IsAvailable, fixture.UnavailableReason);
        await AssertMatchesDirect(name, Options);
    }

    /// <summary>
    /// 決定的なシナリオを直結と実 SQL Server のサーバー役（<paramref name="options"/>）で、どちらも新しいエンジンの端末で流し、
    /// 直結と同じ違反（＋SQL Server だけの既知の失敗）になることを表明する。直結の側は <see cref="SyncModelDirectRuns"/> の結果
    /// （違反の無い結果はプロセスの中で 1 回だけ流す）
    /// </summary>
    /// <param name="name">シナリオの名前</param>
    /// <param name="options">実 SQL Server のサーバー役の世界の組み方（端末は新しいエンジンの端末）</param>
    internal static async Task AssertMatchesDirect(string name, SyncModelWorldOptions options)
    {
        var scenario = SyncModelScenarios.Get(name);
        var (direct, _) = await SyncModelDirectRuns.RunAsync(scenario, Ct);
        var (sqlServer, log) = await scenario.RunAsync(Ct, options);
        var expected = SyncModelSignatures
            .Normalize(direct)
            .Concat(
                SyncModelEngineTransports
                    .SqlServerOnly.Where(k => k.Scope == name)
                    .Select(k => SyncModelSignatures.Normalize(k.Signature ?? ""))
            )
            .ToHashSet(StringComparer.Ordinal);

        SyncModelSignatures
            .Normalize(sqlServer)
            .Should()
            .BeEquivalentTo(
                expected,
                $"SQL Server ({options.Name}) must behave like the direct source (plus the SQL Server only known failures).\n"
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
        new KnownFailureSet("SyncModel.sqlserver-only", SyncModelEngineTransports.SqlServerOnly)
            .Entries.Should()
            .NotBeNull();

        foreach (var known in SyncModelEngineTransports.SqlServerOnly)
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
        var (plan, widePlan) = SyncModelSignatures.ResolvePlans(
            SyncModelSignatures.Seeds,
            SyncModelSignatures.WideSeeds
        );
        plan.WriteWarnings(output);
        widePlan.WriteWarnings(output);
        var coverage = new CoverageCounter(SyncModelDimensions.Table);
        var spellingTally = new ConcurrentDictionary<UniqueSpelling, int>();
        var report = await SyncModelSignatures.RunSeedsAsync(
            [
                (plan, SyncModelSignatures.SqlServerSeeds(plan)),
                (widePlan, SyncModelSignatures.SqlServerSeeds(widePlan)),
            ],
            Options,
            SyncModelEngineTransports.Known,
            2,
            Ct,
            coverage,
            spellingTally
        );

        // 網羅は観測と要約だけ（失敗の注入の組を流さず、網羅しない組は新しいエンジンの端末の直結の乱数の種で照合する）
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

    [Fact(
        DisplayName = "[Sync/モデル/SqlServer] 乱数の操作の列（失敗の注入の組）で、既知の失敗の外の違反が出ない"
    )]
    [Trait(NightlyEngineTrait.Name, NightlyEngineTrait.Selected)]
    public async Task RandomFaultSeeds_OnlyKnownFailures()
    {
        Assert.SkipUnless(fixture.IsAvailable, fixture.UnavailableReason);
        output.WriteLine(await RunFaultSeedsAsync(Options));
    }

    /// <summary>失敗の注入の組（余り 2・3）の種を流し、既知の失敗の外の違反が無いことを表明する（報告を返す）</summary>
    internal static async Task<string> RunFaultSeedsAsync(SyncModelWorldOptions options)
    {
        var (plan, widePlan) = SyncModelSignatures.ResolvePlans(
            SyncModelSignatures.Seeds,
            SyncModelSignatures.WideSeeds
        );
        var report = await SyncModelSignatures.RunSeedsAsync(
            [
                (plan, SyncModelSignatures.SqlServerFaultSeeds(plan)),
                (widePlan, SyncModelSignatures.SqlServerFaultSeeds(widePlan)),
            ],
            options,
            SyncModelEngineTransports.Known,
            2,
            Ct,
            new CoverageCounter(SyncModelDimensions.Table)
        );

        report.Should().BeEmpty();

        return report;
    }
}

/// <summary>
/// 同期エンジンのモデルベーステストを、HTTP 越し（in-process の Kestrel・生成された HTTP の差分ソースと同期専用の
/// エンドポイント）× 実 SQL Server のサーバー役で流す派生（Docker）。
/// </summary>
/// <remarks>
/// 実 SQL Server の事実（本物の rowversion・<c>MIN_ACTIVE_ROWVERSION()</c>・照合順序・外部キーのエラー）が、HTTP の
/// シリアライズ（版の Base64・400 / 409 / 500 の分類）と組み合わさった形を見る。サーバー側の失敗の注入は Kestrel の中で
/// 起き、500 として届く。端末は新しいエンジンの端末（<see cref="SyncModelEngineDevice"/>）で、比べる相手（直結の新しいエンジンの
/// 端末）と既知の失敗の一覧（<see cref="SyncModelEngineTransports"/>）は <see cref="SyncModelSqlServerRuntimeTests"/> と同じ。
/// </remarks>
[Trait("Category", "Integration")]
[Trait("RequiresDocker", "true")]
[Collection(SqlServerContainerCollection.Name)]
[MatrixAxisValue(SyncModelTransportAxis.Name, nameof(SyncModelTransport.SqlServerHttp))]
[RuntimeCell(
    RuntimeFeatures.SyncSupport,
    RuntimeTarget.Remote,
    Server = RuntimeTarget.AdoSqlServer
)]
public sealed class SyncSupportRemoteAdoSqlServerRuntimeTests(
    SqlServerContainerFixture fixture,
    ITestOutputHelper output
) : ISyncModelTransportTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    /// <summary>テストが流す世界の組み方（転送とサーバー役。端末は既定の新しいエンジンの端末。転送の軸の名乗りの照合も読む）</summary>
    private SyncModelWorldOptions Options =>
        new()
        {
            CreateServer = async (faults, ct) =>
                await SyncModelSqlServerServer.CreateAsync(fixture.ConnectionString, faults, ct),
            Http = true,
            Name = "sqlserverhttp",
        };

    /// <summary>転送とサーバー役の組み方（コンテナにはサーバー役を作るときにだけ触れる）</summary>
    SyncModelWorldOptions ISyncModelTransportTests.WorldOptions => Options;

    /// <summary>HTTP × 実 SQL Server で流す決定的なシナリオの名前</summary>
    internal static IReadOnlyList<string> ScenarioNames() =>
        [
            .. SyncModelScenarios
                .All.Where(s => s.RunsOn(SyncModelTransport.SqlServerHttp))
                .Select(s => s.Name),
        ];

    /// <summary>夜間に決定的なシナリオを流す並列度（シナリオごとにデータベースを作る。乱数の種と同じ 2）</summary>
    private const int NightlyScenarioParallelism = 2;

    /// <summary>
    /// 決定的なシナリオを、夜間（<see cref="EngineProfile.Nightly"/>）にだけ全部流す（普段の CI では流さない＝ユーザーの決定
    /// 2026-11-13 の振り分け。実 SQL Server の直結の Theory が普段の網を持ち、HTTP のシリアライズとの組み合わせは夜間に見る）。
    /// </summary>
    /// <remarks>
    /// プロファイルは種の決め方（<see cref="SeedSource.Resolve"/>）から読む（読み方の写しを作らない）。夜間のワークフローは
    /// <see cref="NightlyEngineTrait"/> の目印でこのテストを選ぶ。行ごとの Theory にしないのは、普段のプロファイルで行を出さない
    /// Theory がスキップ（スキップ 0 の原則に当たる）になるため。失敗はシナリオの名前と違反を並べて 1 つの報告にする。
    /// </remarks>
    [Fact(
        DisplayName = "[Sync/モデル/SqlServer/HTTP] 決定的なシナリオは、HTTP 越しの実 SQL Server でも直結と同じ違反（＋SQL Server だけの既知の失敗）になる（夜間だけ）"
    )]
    [Trait(NightlyEngineTrait.Name, NightlyEngineTrait.Selected)]
    public async Task Scenarios_MatchDirect_Nightly()
    {
        Assert.SkipUnless(fixture.IsAvailable, fixture.UnavailableReason);

        var names = ScenarioNames();
        names.Should().NotBeEmpty();

        if (SeedSource.Resolve(SyncModelSignatures.Seeds).Profile != EngineProfile.Nightly)
        {
            output.WriteLine(
                $"regular profile: the {names.Count} scenarios run only in the nightly profile"
            );
            return;
        }

        var failures = new ConcurrentBag<string>();
        await Parallel.ForEachAsync(
            names,
            new ParallelOptions
            {
                MaxDegreeOfParallelism = NightlyScenarioParallelism,
                CancellationToken = Ct,
            },
            async (name, _) =>
            {
                try
                {
                    await SyncModelSqlServerRuntimeTests.AssertMatchesDirect(name, Options);
                }
                catch (Exception e) when (e is not OperationCanceledException)
                {
                    failures.Add($"{name}:\n{e.Message}");
                }
            }
        );

        output.WriteLine($"nightly profile: ran {names.Count} scenarios");
        string.Join("\n\n", failures.OrderBy(f => f, StringComparer.Ordinal)).Should().BeEmpty();
    }

    [Fact(
        DisplayName = "[Sync/モデル/SqlServer/HTTP] 乱数の操作の列（失敗の注入なしの組）で、既知の失敗の外の違反が出ない"
    )]
    [Trait(NightlyEngineTrait.Name, NightlyEngineTrait.Selected)]
    public async Task RandomSeeds_OnlyKnownFailures()
    {
        Assert.SkipUnless(fixture.IsAvailable, fixture.UnavailableReason);

        var (plan, widePlan) = SyncModelSignatures.ResolvePlans(
            SyncModelSignatures.Seeds,
            SyncModelSignatures.WideSeeds
        );
        plan.WriteWarnings(output);
        widePlan.WriteWarnings(output);
        var report = await SyncModelSignatures.RunSeedsAsync(
            [
                (plan, SyncModelSignatures.SqlServerSeeds(plan)),
                (widePlan, SyncModelSignatures.SqlServerSeeds(widePlan)),
            ],
            Options,
            SyncModelEngineTransports.Known,
            2,
            Ct,
            new CoverageCounter(SyncModelDimensions.Table)
        );

        output.WriteLine(report);
        report.Should().BeEmpty();
    }

    [Fact(
        DisplayName = "[Sync/モデル/SqlServer/HTTP] 乱数の操作の列（失敗の注入の組）で、既知の失敗の外の違反が出ない"
    )]
    [Trait(NightlyEngineTrait.Name, NightlyEngineTrait.Selected)]
    public async Task RandomFaultSeeds_OnlyKnownFailures()
    {
        Assert.SkipUnless(fixture.IsAvailable, fixture.UnavailableReason);
        output.WriteLine(await SyncModelSqlServerRuntimeTests.RunFaultSeedsAsync(Options));
    }
}
