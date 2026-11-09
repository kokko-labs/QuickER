using QuickER.Tests.TestSupport.Engines;

namespace QuickER.Tests.TestSupport;

/// <summary>影響の地図の宣言のうち、手で書くもの（理由つきの守りの無い領域）</summary>
/// <remarks>
/// 単位（<see cref="ImpactUnits.Enumerate"/>）は、どれかの守りの範囲にあるか、ここに理由つきで載っているかのちょうど一方
/// （影響の地図のガードが照合する）。プロジェクトを載せたら、その下のフォルダは書かない。新しいプロジェクト・フォルダを足したら、
/// 守りの範囲に入れるか、ここに理由を書く。
/// </remarks>
internal static class ImpactMapDeclarations
{
    /// <summary>理由つきの守りの無い領域</summary>
    public static IReadOnlyList<UnprotectedArea> Unprotected { get; } =
    [
        new(
            "src/QuickER.Gui/Views",
            UnprotectedKind.Accepted,
            "ダイアログ（バージョン情報・印刷の設定）の code-behind とカードの枠の描画。仕様の軸を持つ状態は ViewModel にあり、エンジンはそちらを守る。画面の振る舞いは手書きの画面のテストが守る"
        ),
    ];
}
