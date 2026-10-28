using QuickER.Tests.TestSupport.Engines;

namespace QuickER.Tests.TestSupport;

/// <summary>
/// QuickER.Tests にある乱数のエンジンの種の要約（<see cref="EngineFingerprintDriftTests"/> と同じもの）が、機械の言語と
/// タイムゾーンに依らないことを確かめる（<see cref="SummaryEnvironmentGuard"/>）
/// </summary>
/// <remarks>
/// ドリフトテストのクラスに置かないのは、<c>CultureInvariantGenerationTests</c> がドリフトテストのクラスの引数の無いテストを
/// カルチャを変えて流し直すため（引数を取る Theory はそこで呼べない）。
/// </remarks>
public sealed class EngineSummaryEnvironmentTests
{
    /// <summary>エンジンの名前</summary>
    public static TheoryData<string> Engines() => new("Dsl", "Name");

    [Theory(DisplayName = "[指紋] 要約は外側のカルチャ（ja-JP・en-US）に依らない")]
    [MemberData(nameof(Engines))]
    public void Summary_DoesNotDependOnCulture(string engineId) =>
        SummaryEnvironmentGuard.AssertCultureIndependent(
            engineId,
            SummaryEnvironmentGuard.FirstSeeds(NameFuzz.SeedSettings),
            SummaryOf(engineId)
        );

    /// <remarks>
    /// 赤になるのは時差のある機械（日本時間など）だけ（<see cref="SummaryEnvironmentGuard.AssertNoUtcOffset"/>）。
    /// </remarks>
    [Theory(DisplayName = "[指紋] 要約に機械の時差が入らない")]
    [MemberData(nameof(Engines))]
    public void Summary_HasNoUtcOffset(string engineId) =>
        SummaryEnvironmentGuard.AssertNoUtcOffset(
            engineId,
            SummaryEnvironmentGuard.FirstSeeds(NameFuzz.SeedSettings),
            SummaryOf(engineId)
        );

    private static Func<int, string> SummaryOf(string engineId) =>
        engineId switch
        {
            "Dsl" => EngineFingerprintDriftTests.DslSummary,
            "Name" => EngineFingerprintDriftTests.NameSummary,
            _ => throw new ArgumentOutOfRangeException(nameof(engineId), engineId, null),
        };
}
