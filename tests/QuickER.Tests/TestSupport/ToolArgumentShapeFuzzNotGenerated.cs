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
                NotGenerated.Accepted(
                    "nested-objects",
                    "配列の要素でも辞書でもない入れ子のオブジェクト",
                    "ツールの入力スキーマ（ErDiagramToolCatalog・CodeGenToolSet）にこの形が無い。現れたら ToolArgumentFuzz.Generate が名指しして例外で止める",
                    "`DiagramEdit/ToolArgument`"
                ),
            ]
        );
}
