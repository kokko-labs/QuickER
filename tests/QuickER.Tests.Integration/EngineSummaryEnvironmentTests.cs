using QuickER.Tests.TestSupport.Engines;

namespace QuickER.Tests.Integration;

/// <summary>
/// Integration にある乱数のエンジンの種の要約（<see cref="EngineFingerprintDriftTests"/> と同じもの）が、機械の言語と
/// タイムゾーンに依らないことを確かめる（<see cref="SummaryEnvironmentGuard"/>・生成だけを呼ぶので Docker 不要）
/// </summary>
public sealed class EngineSummaryEnvironmentTests
{
    /// <summary>エンジンの名前（同期計画は方言ごと）</summary>
    public static TheoryData<string> Engines() =>
        new(
            "SyncModel",
            "SyncPlan.sqlite",
            "SyncPlan.mysql",
            "SyncPlan.sqlserver",
            "SyncPlan.postgresql",
            "SyncPlan.oracle",
            "BackendParity"
        );

    [Theory(DisplayName = "[指紋] 要約は外側のカルチャ（ja-JP・en-US）に依らない")]
    [MemberData(nameof(Engines))]
    public void Summary_DoesNotDependOnCulture(string engineId) =>
        WithSummary(
            engineId,
            (settings, summary) =>
                SummaryEnvironmentGuard.AssertCultureIndependent(
                    engineId,
                    SummaryEnvironmentGuard.FirstSeeds(settings),
                    summary
                )
        );

    /// <remarks>
    /// 赤になるのは時差のある機械（日本時間など）だけ（<see cref="SummaryEnvironmentGuard.AssertNoUtcOffset"/>）。
    /// </remarks>
    [Theory(DisplayName = "[指紋] 要約に機械の時差が入らない")]
    [MemberData(nameof(Engines))]
    public void Summary_HasNoUtcOffset(string engineId) =>
        WithSummary(
            engineId,
            (settings, summary) =>
                SummaryEnvironmentGuard.AssertNoUtcOffset(
                    engineId,
                    SummaryEnvironmentGuard.FirstSeeds(settings),
                    summary
                )
        );

    /// <summary>エンジンの種の決め方と要約（ドリフトテストと同じもの）を渡す（同期計画は方言を作って破棄する）</summary>
    private static void WithSummary(string engineId, Action<SeedSettings, Func<int, string>> use)
    {
        switch (engineId)
        {
            case "SyncModel":
                use(
                    GeneratedRuntime.SyncModelRuntimeTests.Seeds,
                    EngineFingerprintDriftTests.SyncModelSummary
                );
                return;
            case "BackendParity":
                use(
                    GeneratedRuntime.BackendParityRuntimeTests.Seeds,
                    EngineFingerprintDriftTests.ParitySummary
                );
                return;
        }

        const string prefix = "SyncPlan.";

        if (!engineId.StartsWith(prefix, StringComparison.Ordinal))
        {
            throw new ArgumentOutOfRangeException(nameof(engineId), engineId, null);
        }

        var dialectName = engineId[prefix.Length..];
        var dialect = EngineFingerprintDriftTests.CreateDialect(dialectName);

        try
        {
            use(
                EngineFingerprintDriftTests.SyncPlanSeeds(dialectName),
                seed => EngineFingerprintDriftTests.SyncPlanSummary(dialect, seed)
            );
        }
        finally
        {
            (dialect as IDisposable)?.Dispose();
        }
    }
}
