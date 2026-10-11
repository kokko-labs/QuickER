using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
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

    /// <summary>既知の失敗の一覧を正規化した集合（乱数の種の照合用）</summary>
    public static IReadOnlySet<string> Known(IEnumerable<string> extra) =>
        SyncModelKnownFailures
            .All.Select(k => Normalize(k.Signature))
            .Concat(extra.Select(Normalize))
            .ToHashSet(StringComparer.Ordinal);

    /// <summary>派生（HTTP・実 SQL Server）で流す乱数の種の数（環境変数 QUICKER_SYNC_MODEL_TRANSPORT_SEEDS・既定 10）</summary>
    public static int SeedCount =>
        int.TryParse(
            Environment.GetEnvironmentVariable("QUICKER_SYNC_MODEL_TRANSPORT_SEEDS"),
            System.Globalization.NumberStyles.None,
            System.Globalization.CultureInfo.InvariantCulture,
            out var count
        )
        && count > 0
            ? count
            : 10;

    /// <summary>種を並列に流し、既知の失敗（正規化）に当たらない失敗のある種を報告する</summary>
    public static async Task<string> RunSeedsAsync(
        IEnumerable<int> seeds,
        SyncModelWorldOptions options,
        IReadOnlySet<string> known,
        int parallelism,
        CancellationToken ct
    )
    {
        var results =
            new ConcurrentDictionary<int, (IReadOnlyList<ModelFailure>, IReadOnlyList<string>)>();

        await Parallel.ForEachAsync(
            seeds,
            new ParallelOptions { MaxDegreeOfParallelism = parallelism, CancellationToken = ct },
            async (seed, token) =>
                results[seed] = await SyncModelRuntimeTests.RunSeedAsync(seed, token, options)
        );

        var report = new System.Text.StringBuilder();

        foreach (var (seed, (failures, log)) in results.OrderBy(p => p.Key))
        {
            var unknown = failures.Where(f => !known.Contains(Normalize(f.Signature))).ToList();

            if (unknown.Count == 0)
            {
                continue;
            }

            report.AppendLine($"seed {seed} ({options.Name}):");

            foreach (var failure in failures)
            {
                var mark = known.Contains(Normalize(failure.Signature)) ? "known" : "NEW";
                report.AppendLine(
                    $"  [{mark}] {failure.Signature} -> {Normalize(failure.Signature)}"
                );
            }

            report.AppendLine("  ops:");

            foreach (var line in log)
            {
                report.AppendLine($"    {line}");
            }
        }

        return report.ToString();
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
public sealed class SyncModelHttpRuntimeTests(ITestOutputHelper output)
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    private static readonly SyncModelWorldOptions Http = new() { Http = true, Name = "http" };

    public static TheoryData<string> ScenarioNames() => SyncModelRuntimeTests.ScenarioNames();

    [Theory(
        DisplayName = "[Sync/モデル/HTTP] 決定的なシナリオは、HTTP 越しでも直結と同じ違反（正規化した署名）になる"
    )]
    [MemberData(nameof(ScenarioNames))]
    public async Task Scenario_MatchesDirect(string name)
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
    public async Task RandomSeeds_OnlyKnownFailures()
    {
        var report = await SyncModelSignatures.RunSeedsAsync(
            Enumerable.Range(0, SyncModelSignatures.SeedCount),
            Http,
            SyncModelSignatures.Known([]),
            Math.Clamp(Environment.ProcessorCount, 1, 4),
            Ct
        );

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
public sealed class SyncModelSqlServerRuntimeTests(
    SqlServerContainerFixture fixture,
    ITestOutputHelper output
)
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    /// <summary>SQL Server でだけ起きる既知の失敗（指摘の番号・直す区切り・シナリオ・署名）</summary>
    internal static IReadOnlyList<KnownSyncModelFailure> SqlServerOnly { get; } = [];

    private SyncModelWorldOptions Options =>
        new()
        {
            CreateServer = async (_, ct) =>
                await SyncModelSqlServerServer.CreateAsync(fixture.ConnectionString, ct),
            Name = "sqlserver",
        };

    public static TheoryData<string> ScenarioNames()
    {
        var data = new TheoryData<string>();

        foreach (var scenario in SyncModelScenarios.All.Where(s => !s.ArmsServerFaults))
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
                    .Where(k => k.Scenario == name)
                    .Select(k => SyncModelSignatures.Normalize(k.Signature))
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
        foreach (var known in SqlServerOnly)
        {
            SyncModelScenarios.All.Should().Contain(s => s.Name == known.Scenario);
            known.Section.Should().BeOneOf(30, 31);
        }
    }

    [Fact(
        DisplayName = "[Sync/モデル/SqlServer] 乱数の操作の列（失敗の注入なしの組）で、既知の失敗の外の違反が出ない"
    )]
    public async Task RandomSeeds_OnlyKnownFailures()
    {
        Assert.SkipUnless(fixture.IsAvailable, fixture.UnavailableReason);

        var seeds = Enumerable
            .Range(0, SyncModelSignatures.SeedCount * 4)
            .Where(seed => seed % 4 is 0 or 1)
            .Take(SyncModelSignatures.SeedCount);
        var report = await SyncModelSignatures.RunSeedsAsync(
            seeds,
            Options,
            SyncModelSignatures.Known(SqlServerOnly.Select(k => k.Signature)),
            2,
            Ct
        );

        output.WriteLine(report);
        report.Should().BeEmpty();
    }
}
