using QuickER.Tests.TestSupport.Engines;

namespace QuickER.Tests.CodeGen.CSharp.CodegenConfig;

/// <summary>
/// 生成の構成のエンジンの既知の失敗の一覧（範囲＝行の Id・署名＝段の名前・理由の断片＝失敗の文の言語に依らない部分）
/// </summary>
/// <remarks>
/// <para>
/// 行は決定的なので項目は種を持たない。照合は行ごとに、その行の項目だけを受け持って行う（<see cref="CodegenRunner.RunAsync"/>）。
/// 流していない行（普段の実行の夜間の行の残り）の項目は「起きない」と数えない。載っていない失敗・載っているのに起きない項目・
/// 理由の断片の食い違いは、どれも赤になる。
/// </para>
/// <para>
/// 今は空。項目を載せるときは、範囲に網羅の配列の行の Id を書く（<c>CodegenMatrixTests.KnownFailures_PointAtRows</c> が照合する）。
/// </para>
/// </remarks>
internal static class CodegenKnownFailures
{
    /// <summary>一覧</summary>
    public static KnownFailureSet Set { get; } = new(EngineIds.CodegenConfig, []);
}
