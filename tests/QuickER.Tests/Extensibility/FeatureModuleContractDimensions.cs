using QuickER.Tests.TestSupport.Engines;

namespace QuickER.Tests.Extensibility;

/// <summary>機能モジュールの契約の表の次元表・網羅しない組の一覧・台帳の行</summary>
/// <remarks>軸の値（アダプタを持つもの）× 契約の全組を行にするので、原理的に起こり得ない組も網羅しない組も無い</remarks>
internal static class FeatureModuleContractDimensions
{
    /// <summary>網羅しない組の照合の範囲</summary>
    public const string Scope = "os-boundary-feature-modules";

    /// <summary>モジュール</summary>
    public const string Module = "Module";

    /// <summary>契約</summary>
    public const string Contract = "Contract";

    /// <summary>次元表（モジュールの水準は軸のアダプタを持つ値）</summary>
    public static DimensionTable Table { get; } =
        new(
            EngineIds.OsBoundary,
            [
                new Factor(
                    Module,
                    FeatureModuleAxis.Axis.Adapters.Select(a => new Level(a.AxisValue)).ToList(),
                    "機能モジュール（軸の正本はカタログのモジュールの Id）"
                ),
                new Factor(
                    Contract,
                    Enum.GetValues<FeatureModuleContract>()
                        .Select(v => new Level(v.ToString()))
                        .ToList(),
                    "IFeatureModule の契約"
                ),
            ]
        );

    /// <summary>網羅しない組の一覧（無い）</summary>
    public static CoverageGapSet Gaps => CoverageGap.Set(EngineIds.OsBoundary, []);

    /// <summary>台帳の行</summary>
    public static EngineDescriptor Descriptor =>
        new(
            EngineIds.OsBoundary,
            "FeatureModules",
            "機能モジュールの契約の表（モジュール × 契約・AI チャット・モック生成・コード生成・DB ツール）",
            Table,
            FeatureModuleContractRows.Table.Count,
            [
                "tests/QuickER.Tests/Extensibility/FeatureModuleContractDimensions.cs",
                "tests/QuickER.Tests/Extensibility/FeatureModuleContractRows.cs",
                "tests/QuickER.Tests/Extensibility/FeatureModuleContractTests.cs",
            ],
            [
                "src/QuickER.Extensibility/IFeatureModule.cs",
                "src/QuickER.AI.Chat/AiChatFeatureModule.cs",
                "src/QuickER.AI.Mock/MockGenerationFeatureModule.cs",
                "src/QuickER.CodeGen.UI/CodeGenerationFeatureModule.cs",
                "src/QuickER.Db.UI/DbToolsFeatureModule.cs",
            ]
        );

    /// <summary>1 行を数える</summary>
    public static void Observe(CoverageCounter counter, FeatureModuleContractCase row) =>
        counter.Observe(
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [Module] = row.Adapter.AxisValue,
                [Contract] = row.Contract.ToString(),
            }
        );
}
