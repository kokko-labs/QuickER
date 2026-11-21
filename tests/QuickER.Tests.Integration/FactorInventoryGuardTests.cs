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
    private static IReadOnlyList<InventoryExclusion> Exclusions { get; } =
    [
        new InventoryExclusion(
            "SyncOptions",
            "DownloadRowLimit",
            EngineIds.SyncModel,
            "P4",
            "乱数の同期の手は降ろす行の上限を製品の既定（300,000 行）のまま渡し、乱数の世界の行数は上限に届かない。"
                + "上限を超えて断る形・超えても降ろす形・引数の検査は、決まった形の行（新しいエンジンの決まった形の行の"
                + "上限の枠と row-limit の行、決定的なシナリオの dl-row-limit の行）が受け持つ。"
                + "乱数の手に小さい上限の水準を足すには、断られた実行を判定役が扱う必要がある"
        ),
    ];

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
