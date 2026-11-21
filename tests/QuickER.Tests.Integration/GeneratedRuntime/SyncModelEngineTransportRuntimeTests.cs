using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using QuickER.Tests.TestSupport;
using QuickER.Tests.TestSupport.Engines;
using Xunit;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>
/// 新しいエンジンの端末（<see cref="SyncModelEngineDevice"/>）を、直結のほかの転送で流すための共有の宣言（既知の失敗の一覧・
/// 上限に依る決定的な行の選び方）。流し方の本体は今の端末の転送のクラスと同じ（<see cref="SyncModelSignatures.RunSeedsAsync"/>）。
/// </summary>
internal static class SyncModelEngineTransports
{
    /// <summary>
    /// 新しいエンジンの端末の転送の乱数の種の既知の失敗の一覧（署名は <see cref="SyncModelSignatures.Normalize(string)"/> で正規化して
    /// 照合する）。今の端末の一覧とは別に持ち、空で始める（載せる項目は、新しいエンジンの端末で出た失敗から理由つきで足す）
    /// </summary>
    public static KnownFailureSet Known { get; } =
        SyncModelKnownFailures.Build(
            "SyncModel.transport.engine",
            [],
            SyncModelSignatures.Normalize
        );

    /// <summary>
    /// 新しいエンジンの端末で SQL Server でだけ起きる既知の失敗（<see cref="SyncModelKnownFailures.Entry"/> で作る）。空で始める
    /// </summary>
    public static IReadOnlyList<KnownEntry> SqlServerOnly { get; } = [];

    /// <summary>
    /// 実 SQL Server で流す上限に依る決定的なシナリオの名前。行の中身で選ぶ：期待の手 <see cref="WhenServerFixesCeilingOp"/>
    /// を持ち（入れ子の枝の中も見る）、実 SQL Server の転送で流れ、新しいエンジンの端末の控え
    /// （<see cref="SyncSupportAdoSqliteEngineRuntimeTests.LedgerPath"/>）の結果が <c>unsupported</c> でない行
    /// </summary>
    public static IReadOnlyList<string> CeilingScenarioNames()
    {
        var unsupported = LedgerOutcomes()
            .Where(pair => pair.Value == "unsupported")
            .Select(pair => pair.Key)
            .ToHashSet(StringComparer.Ordinal);

        return
        [
            .. SyncModelScenarios
                .All.Where(scenario =>
                    scenario.RunsOn(SyncModelTransport.SqlServer)
                    && scenario.Ops.Any(DependsOnCeiling)
                    && !unsupported.Contains(scenario.Name)
                )
                .Select(scenario => scenario.Name),
        ];
    }

    /// <summary>操作が、取得の上限を固定するかで期待の枝を選ぶ手か（入れ子の枝の中も見る）</summary>
    private static bool DependsOnCeiling(ModelOp op) =>
        op switch
        {
            WhenServerFixesCeilingOp => true,
            WhenEngineDeclaresOp engine => engine
                .IfTrue.Concat(engine.IfFalse)
                .Any(DependsOnCeiling),
            WhenServerSameUniqueOp unique => unique
                .IfSame.Concat(unique.IfDifferent)
                .Any(DependsOnCeiling),
            _ => false,
        };

    /// <summary>新しいエンジンの端末の控えの、シナリオの名前 → 結果</summary>
    private static Dictionary<string, string> LedgerOutcomes()
    {
        var outcomes = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (
            var line in File.ReadLines(
                Path.Combine(
                    EngineRepository.Root(),
                    SyncSupportAdoSqliteEngineRuntimeTests.LedgerPath
                )
            )
        )
        {
            var cells = line.Split('|');

            // 表の行は「| シナリオ | 結果 | 詳細 | 今のエンジン |」（見出しと区切りの行は結果の値に当たらない）
            if (cells.Length >= 4 && cells[2].Trim() is "green" or "unsupported" or "differs")
            {
                outcomes[cells[1].Trim()] = cells[2].Trim();
            }
        }

        outcomes.Should().NotBeEmpty("新しいエンジンの端末の控えに表の行がある");

        return outcomes;
    }
}

/// <summary>
/// 新しいエンジンの端末（<see cref="SyncModelEngineDevice"/>）の乱数の種を、HTTP 越し（in-process の Kestrel・SQLite のサーバー役）で
/// 流す（コンテナなし）。種の決め方・本体・正規化は今の端末の HTTP のクラス（<see cref="SyncModelHttpRuntimeTests"/>）と同じで、
/// 既知の失敗の一覧は <see cref="SyncModelEngineTransports.Known"/>。
/// </summary>
[Trait("Category", "Integration")]
[RuntimeCell(RuntimeFeatures.SyncSupport, RuntimeTarget.Remote, Server = RuntimeTarget.AdoSqlite)]
public sealed class SyncSupportRemoteAdoSqliteEngineRuntimeTests(ITestOutputHelper output)
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    private static readonly SyncModelWorldOptions Http = SyncModelEngineDevice.On(
        new SyncModelWorldOptions { Http = true, Name = "http" }
    );

    [Fact(
        DisplayName = "[Sync/新しいエンジン/HTTP] 乱数の操作の列で、既知の失敗の外の違反が出ない（新しいエンジンの端末）"
    )]
    // 夜間でも普段の種の数で流す（夜間の数は新しいエンジンの端末の直結の乱数が受け持ち、転送の差は普段の数で見る）
    [Trait(NightlyEngineTrait.Name, NightlyEngineTrait.NotSelected)]
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
            [(plan, plan.Seeds), (widePlan, widePlan.Seeds)],
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
/// 新しいエンジンの端末（<see cref="SyncModelEngineDevice"/>）を、実 SQL Server のサーバー役（直結の差分ソース・本物の rowversion と
/// <c>MIN_ACTIVE_ROWVERSION()</c>）で流す（Docker）。乱数の種は今の端末の実 SQL Server のクラス
/// （<see cref="SyncModelSqlServerRuntimeTests"/>）と同じく、失敗の注入なしの組（余り 0・1）と注入の組（余り 2・3）を別の Fact で流す。
/// </summary>
/// <remarks>
/// 決定的なシナリオは、取得の上限を固定するかで期待が変わる行だけを流す（選び方は
/// <see cref="SyncModelEngineTransports.CeilingScenarioNames"/>）。直結の新しいエンジンの端末と同じ違反（正規化した署名）になることを
/// 表明する。
/// </remarks>
[Trait("Category", "Integration")]
[Trait("RequiresDocker", "true")]
[Collection(SqlServerSecondContainerCollection.Name)]
[RuntimeCell(RuntimeFeatures.SyncSupport, RuntimeTarget.AdoSqlServer)]
public sealed class SyncSupportAdoSqlServerEngineRuntimeTests(
    SqlServerContainerFixture fixture,
    ITestOutputHelper output
)
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    private SyncModelWorldOptions Options =>
        SyncModelEngineDevice.On(
            new SyncModelWorldOptions
            {
                CreateServer = async (faults, ct) =>
                    await SyncModelSqlServerServer.CreateAsync(
                        fixture.ConnectionString,
                        faults,
                        ct
                    ),
                Name = "sqlserver",
            }
        );

    public static TheoryData<string> CeilingScenarioNames() =>
        [.. SyncModelEngineTransports.CeilingScenarioNames()];

    [Theory(
        DisplayName = "[Sync/新しいエンジン/SqlServer] 上限に依る決定的なシナリオは、実 SQL Server でも直結と同じ違反になる（新しいエンジンの端末）"
    )]
    [MemberData(nameof(CeilingScenarioNames))]
    public async Task CeilingScenario_MatchesDirect(string name)
    {
        Assert.SkipUnless(fixture.IsAvailable, fixture.UnavailableReason);

        var scenario = SyncModelScenarios.Get(name);
        var (direct, _) = await scenario.RunAsync(
            Ct,
            SyncModelEngineDevice.On(SyncModelWorldOptions.Direct)
        );
        var (sqlServer, log) = await scenario.RunAsync(Ct, Options);
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
                "SQL Server must behave like the direct source on the new engine's device.\n"
                    + "sqlserver failures:\n"
                    + string.Join("\n", sqlServer.Select(f => "  " + f.Signature))
                    + "\nops:\n"
                    + string.Join("\n", log.Select(l => "  " + l))
            );
    }

    [Fact(
        DisplayName = "[Sync/新しいエンジン/SqlServer] 乱数の操作の列（失敗の注入なしの組）で、既知の失敗の外の違反が出ない（新しいエンジンの端末）"
    )]
    // 夜間でも普段の種の数で流す（夜間の数は新しいエンジンの端末の直結の乱数が受け持ち、サーバー役の差は普段の数で見る）
    [Trait(NightlyEngineTrait.Name, NightlyEngineTrait.NotSelected)]
    public async Task RandomSeeds_OnlyKnownFailures()
    {
        Assert.SkipUnless(fixture.IsAvailable, fixture.UnavailableReason);

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

        coverage.Report().WriteSummary(output);
        output.WriteLine(report);

        foreach (var (spelling, count) in spellingTally.OrderBy(p => p.Key))
        {
            output.WriteLine($"refused spelled upserts {spelling}: {count}");
        }

        report.Should().BeEmpty();

        // 照合順序が同じとみなす別表記が、既定の種の数で実際に断られている（今の端末の実 SQL Server のクラスと同じ空振りの守り）
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
        DisplayName = "[Sync/新しいエンジン/SqlServer] 乱数の操作の列（失敗の注入の組）で、既知の失敗の外の違反が出ない（新しいエンジンの端末）"
    )]
    // 夜間でも普段の種の数で流す（上の Fact と同じ）
    [Trait(NightlyEngineTrait.Name, NightlyEngineTrait.NotSelected)]
    public async Task RandomFaultSeeds_OnlyKnownFailures()
    {
        Assert.SkipUnless(fixture.IsAvailable, fixture.UnavailableReason);

        var (plan, widePlan) = SyncModelSignatures.ResolvePlans(
            SyncModelSignatures.Seeds,
            SyncModelSignatures.WideSeeds
        );
        var report = await SyncModelSignatures.RunSeedsAsync(
            [
                (plan, SyncModelSignatures.SqlServerFaultSeeds(plan)),
                (widePlan, SyncModelSignatures.SqlServerFaultSeeds(widePlan)),
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
}
