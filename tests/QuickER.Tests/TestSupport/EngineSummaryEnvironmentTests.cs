using AwesomeAssertions;
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
    public static TheoryData<string> Engines() => new("Dsl", "Name", "DocumentFile", "CanvasInput");

    [Theory(DisplayName = "[指紋] 要約は外側のカルチャ（ja-JP・en-US）に依らない")]
    [MemberData(nameof(Engines))]
    public void Summary_DoesNotDependOnCulture(string engineId) =>
        SummaryEnvironmentGuard.AssertCultureIndependent(
            engineId,
            SummaryEnvironmentGuard.FirstSeeds(SeedsOf(engineId)),
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
            SummaryEnvironmentGuard.FirstSeeds(SeedsOf(engineId)),
            SummaryOf(engineId)
        );

    /// <summary>時差のガードの自己検査の行（行 Id → 要約・赤になるか）</summary>
    private static readonly Dictionary<string, (Func<string> Summary, bool Red)> OffsetProbes = new(
        StringComparer.Ordinal
    )
    {
        // 負の TimeSpan は符号と時・分の並びを持つが、時差ではない
        ["timespan-negative-tick"] = (
            () => "t=" + SeedSummary.Object(TimeSpan.FromTicks(-10)),
            false
        ),
        ["timespan-negative-list"] = (
            () =>
                SeedSummary.Object(
                    new List<object?>
                    {
                        TimeSpan.Zero,
                        TimeSpan.FromHours(-1),
                        -new TimeSpan(34, 22, 59, 59),
                    }
                ),
            false
        ),
        ["datetime-utc"] = (
            () => SeedSummary.Object(new DateTime(2026, 2, 1, 8, 0, 0, DateTimeKind.Utc)),
            false
        ),
        ["datetime-local-written-by-summary"] = (
            () => SeedSummary.Object(new DateTime(2026, 2, 1, 8, 0, 0, DateTimeKind.Local)),
            false
        ),
        // 往復の書式で書いた Local の日時は機械の時差を持つ（時差 0 の機械でも +00:00）
        ["datetime-local-round-trip"] = (
            () =>
                "at="
                + new DateTime(2026, 2, 1, 8, 0, 0, DateTimeKind.Local).ToString(
                    "o",
                    System.Globalization.CultureInfo.InvariantCulture
                ),
            true
        ),
        ["datetime-local-round-trip-without-fraction"] = (
            () =>
                "at="
                + new DateTime(2026, 2, 1, 8, 0, 0, DateTimeKind.Local).ToString(
                    "yyyy-MM-ddTHH:mm:sszzz",
                    System.Globalization.CultureInfo.InvariantCulture
                ),
            true
        ),
        // 往復の書式の DateTimeOffset は時差を時刻の直後に書くので、時差の形として名指しする
        ["datetimeoffset-plus-nine"] = (
            () =>
                "at="
                + new DateTimeOffset(2026, 2, 1, 8, 0, 0, TimeSpan.FromHours(9)).ToString(
                    "o",
                    System.Globalization.CultureInfo.InvariantCulture
                ),
            true
        ),
        ["datetimeoffset-minus-five"] = (
            () =>
                "at="
                + new DateTimeOffset(2026, 2, 1, 8, 0, 0, TimeSpan.FromHours(-5)).ToString(
                    "o",
                    System.Globalization.CultureInfo.InvariantCulture
                ),
            true
        ),
        // 要約は DateTimeOffset の時差（値のデータ）を時刻と @ で隔てて書くので、時差の形にならない（簡易 DSL の DateTimeOffset の値の池）
        ["datetimeoffset-written-by-summary"] = (
            () =>
                SeedSummary.Object(
                    new List<object?>
                    {
                        new DateTimeOffset(2026, 2, 1, 8, 0, 0, TimeSpan.FromHours(9)),
                        new DateTimeOffset(2026, 2, 1, 8, 0, 0, 500, TimeSpan.FromHours(-5)),
                        new DateTimeOffset(2026, 2, 1, 8, 0, 0, TimeSpan.Zero),
                    }
                ),
            false
        ),
    };

    /// <summary>時差のガードの自己検査の行 Id</summary>
    public static TheoryData<string> OffsetProbeIds() => [.. OffsetProbes.Keys];

    /// <remarks>
    /// 時差の形は日時の秒・小数秒の直後だけを見る。負の TimeSpan（簡易 DSL の TimeSpan の値の池）を時差と取り違えず、
    /// 往復の書式の Local の日時と DateTimeOffset の時差は名指しする。
    /// </remarks>
    [Theory(
        DisplayName = "[指紋] 時差のガードは日時の時差だけを名指しし、負の TimeSpan を時差としない"
    )]
    [MemberData(nameof(OffsetProbeIds))]
    public void UtcOffsetGuard_FlagsOnlyOffsetsOfDates(string id)
    {
        var (summary, red) = OffsetProbes[id];
        var check = () => SummaryEnvironmentGuard.AssertNoUtcOffset(id, [1], _ => summary());

        if (red)
        {
            check.Should().Throw<Xunit.Sdk.XunitException>().WithMessage("*時差の形*");
        }
        else
        {
            check.Should().NotThrow();
        }
    }

    private static Func<int, string> SummaryOf(string engineId) =>
        engineId switch
        {
            "Dsl" => EngineFingerprintDriftTests.DslSummary,
            "Name" => EngineFingerprintDriftTests.NameSummary,
            "DocumentFile" => Gui.ViewModels.DocumentFileGenerator.Summary,
            "CanvasInput" => Gui.Behaviors.CanvasInputGenerator.Summary,
            _ => throw new ArgumentOutOfRangeException(nameof(engineId), engineId, null),
        };

    /// <summary>エンジンの種の決め方（簡易 DSL と名前は同じ決め方を共有する）</summary>
    private static SeedSettings SeedsOf(string engineId) =>
        engineId switch
        {
            "DocumentFile" => Gui.ViewModels.DocumentFileGenerator.Seeds,
            "CanvasInput" => Gui.Behaviors.CanvasInputGenerator.Seeds,
            _ => NameFuzz.SeedSettings,
        };
}
