using QuickER.Tests.TestSupport.Engines;

namespace QuickER.Tests.TestSupport;

/// <summary>
/// ツールの引数の形の総当たり（<see cref="ToolArgumentShapeFuzzRunner"/>・種を使わない網羅型）が作らない崩し方の宣言
/// </summary>
/// <remarks>純粋なデータ（Engines の型と BCL だけを参照する）。</remarks>
internal static class ToolArgumentShapeFuzzNotGenerated
{
    /// <summary>宣言</summary>
    public static NotGeneratedDeclaration Declaration { get; } =
        new(
            "ToolArgumentShapeFuzzRunner",
            [ProtectionNames.EnginePart(EngineIds.DiagramEdit, "ToolArgument")],
            [
                NotGenerated.Pending(
                    "non-object-arguments",
                    "引数の全体がオブジェクトでない（配列・文字列・数値・null）",
                    "G5",
                    "崩し方は引数のオブジェクトの中のプロパティだけを変える。入口の形の検査（ToolArgumentShape）は引数の全体がオブジェクトでない形で分岐する",
                    NotGenerated.Unguarded
                ),
                NotGenerated.Pending(
                    "nested-objects-and-later-elements",
                    "配列を介さないオブジェクトの入れ子・配列の 2 番目以降の要素の中",
                    "ツールの引数の残りの区切り（未起動）",
                    "崩し方は配列の最初の要素の中と、最上位のプロパティだけを変える。形の検査は入れ子と要素の位置ごとに分岐する",
                    NotGenerated.Unguarded
                ),
            ]
        );
}
