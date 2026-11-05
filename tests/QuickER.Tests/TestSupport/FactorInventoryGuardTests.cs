using AwesomeAssertions;
using QuickER.Tests.TestSupport.Engines;

namespace QuickER.Tests.TestSupport;

/// <summary>
/// QuickER.Tests が受け持つ製品の仕様の軸（<see cref="FactorInventoryAssignment"/>）の棚卸し：すべての項目が、QuickER.Tests の
/// エンジンの台帳（<see cref="EngineRegistry"/>）の次元表の Source に載っているか、理由つきで除外されているかのちょうど一方
/// </summary>
/// <remarks>
/// <para>
/// 軸の型は製品の型を完全修飾で書く（QuickER.Tests は生成フィクスチャの写しもコンパイルするので、名前だけでは別の型を指し得る）。
/// </para>
/// <para>
/// 除外の項目の名前は文字列で書き、軸から生成しない（製品に値・プロパティを足すと、載せるか除外するまで赤になる）。
/// </para>
/// </remarks>
public sealed class FactorInventoryGuardTests
{
    /// <summary>QuickER.Tests が検査する軸</summary>
    private static IReadOnlyList<InventoryAxis> Axes { get; } =
    [
        InventoryAxis.Enum<global::QuickER.Model.ForeignKeyReferentialAction>(
            "ForeignKeyReferentialAction"
        ),
        InventoryAxis.Enum<global::QuickER.Model.RelationshipType>("RelationshipType"),
        // 真偽で設定できるプロパティだけ。GeneratesRemoteContracts・GeneratesRepositoryContract・EffectiveSplitFilesByCategory は
        // ほかの設定から計算する読み取り専用の値で、利用者が選ぶ次元ではない（元の設定の組み合わせを流せば決まる）ので軸にしない
        InventoryAxis.Properties(
            "CodeGenerationOptions",
            typeof(global::QuickER.CodeGen.CSharp.CodeGenerationOptions),
            property => property.PropertyType == typeof(bool) && OptionKeys.IsWritable(property)
        ),
        InventoryAxis.Enum<global::QuickER.Provider.CanonicalTypeKind>("CanonicalTypeKind"),
        InventoryAxis.Enum<global::QuickER.Provider.SchemaImportWarningKind>(
            "SchemaImportWarningKind"
        ),
        InventoryAxis.Enum<global::QuickER.Services.ExportOmissionKind>("ExportOmissionKind"),
    ];

    /// <summary>まだどのエンジンの次元表にも載っていない項目（軸・項目・受け持つエンジン・区切り・理由）</summary>
    private static IReadOnlyList<InventoryExclusion> Exclusions { get; } = [];

    [Fact(
        DisplayName = "[棚卸し] QuickER.Tests が検査する軸は、軸の割り当ての表の QuickER.Tests の軸とちょうど一致する"
    )]
    public void Axes_MatchAssignment() =>
        Axes.Select(axis => axis.Name)
            .Should()
            .Equal(FactorInventoryAssignment.AxesFor(FactorInventoryAssignment.Tests));

    [Fact(
        DisplayName = "[棚卸し] QuickER.Tests の軸のすべての項目は、エンジンの次元表の Source か理由つきの除外のちょうど一方にある"
    )]
    public void EveryMember_IsSourcedOrExcluded() =>
        FactorInventoryGuard.Verify(Axes, EngineRegistry.All, Exclusions);
}
