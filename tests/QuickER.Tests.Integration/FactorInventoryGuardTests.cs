using AwesomeAssertions;
using QuickER.Tests.TestSupport.Engines;
using ParityFixture = QuickER.Tests.GeneratedBackendParityFixture;
using SyncFixture = QuickER.Tests.GeneratedSyncModelFixture;

namespace QuickER.Tests.Integration;

/// <summary>
/// Integration が受け持つ製品の仕様の軸（<see cref="FactorInventoryAssignment"/>）の棚卸し：すべての項目が、Integration の
/// エンジンの台帳（<see cref="EngineRegistry"/>）の次元表の Source に載っているか、理由つきで除外されているかのちょうど一方
/// </summary>
/// <remarks>
/// <para>
/// 同期の型（SyncConflictReason・SyncMode・SyncConflictPolicy・SyncLocalRowChangeReason・SyncOptions）は、エンジンの次元表と同じく
/// 生成フィクスチャの写し（<c>QuickER.Tests.GeneratedSyncModelFixture</c>）を、ConcurrencyMode と SaveOperation は
/// <c>QuickER.Tests.GeneratedBackendParityFixture</c> の写しを反射する（Integration は QuickER.Runtime.Sync を参照しない。
/// 次元表と違う写しを反射すると、名前が同じでも別の型として載っていないことになる）。
/// SyncDialectCapabilities は製品の型（QuickER.Provider）。
/// </para>
/// <para>
/// 除外の項目の名前は文字列で書き、軸から生成しない（製品に値・プロパティを足すと、載せるか除外するまで赤になる）。
/// </para>
/// </remarks>
public sealed class FactorInventoryGuardTests
{
    /// <summary>Integration が検査する軸</summary>
    private static IReadOnlyList<InventoryAxis> Axes { get; } =
    [
        InventoryAxis.Properties(
            "SyncDialectCapabilities",
            typeof(global::QuickER.Provider.SyncDialectCapabilities)
        ),
        InventoryAxis.Enum<SyncFixture.SyncConflictReason>("SyncConflictReason"),
        InventoryAxis.Enum<SyncFixture.SyncMode>("SyncMode"),
        InventoryAxis.Enum<SyncFixture.SyncConflictPolicy>("SyncConflictPolicy"),
        InventoryAxis.Enum<SyncFixture.SyncLocalRowChangeReason>("SyncLocalRowChangeReason"),
        InventoryAxis.Properties("SyncOptions", typeof(SyncFixture.SyncOptions)),
        InventoryAxis.Enum<ParityFixture.ConcurrencyMode>("ConcurrencyMode"),
        InventoryAxis.Enum<ParityFixture.SaveOperation>("SaveOperation"),
    ];

    /// <summary>まだどのエンジンの次元表にも載っていない項目（軸・項目・受け持つエンジン・区切り・理由）</summary>
    private static IReadOnlyList<InventoryExclusion> Exclusions { get; } = [];

    [Fact(
        DisplayName = "[棚卸し] Integration が検査する軸は、軸の割り当ての表の Integration の軸とちょうど一致する"
    )]
    public void Axes_MatchAssignment() =>
        Axes.Select(axis => axis.Name)
            .Should()
            .Equal(FactorInventoryAssignment.AxesFor(FactorInventoryAssignment.Integration));

    [Fact(
        DisplayName = "[棚卸し] Integration の軸のすべての項目は、エンジンの次元表の Source か理由つきの除外のちょうど一方にある"
    )]
    public void EveryMember_IsSourcedOrExcluded() =>
        FactorInventoryGuard.Verify(Axes, EngineRegistry.All, Exclusions);
}
