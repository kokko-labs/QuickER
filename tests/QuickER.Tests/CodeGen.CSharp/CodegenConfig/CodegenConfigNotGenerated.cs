using QuickER.Tests.TestSupport.Engines;

namespace QuickER.Tests.CodeGen.CSharp.CodegenConfig;

/// <summary>生成の構成のエンジンの網羅の配列（Errors・Success）の作らない形の宣言（空）</summary>
/// <remarks>純粋なデータ（Engines の型と BCL だけを参照する）。</remarks>
internal static class CodegenConfigNotGenerated
{
    /// <summary>宣言</summary>
    public static NotGeneratedDeclaration Declaration { get; } =
        new(
            "CodegenConfigDimensions",
            [
                ProtectionNames.EnginePart(EngineIds.CodegenConfig, "Errors"),
                ProtectionNames.EnginePart(EngineIds.CodegenConfig, "Success"),
            ],
            [],
            "種は夜間の網羅の配列の行の番号で、乱数で形を作る生成器を持たない。行は次元表の水準の組だけで、水準の外の形は次元表の isValid・網羅しない組の一覧・領域の述語が持つ"
        );
}
