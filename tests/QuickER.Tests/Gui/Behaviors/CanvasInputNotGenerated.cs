using QuickER.Tests.TestSupport.Engines;

namespace QuickER.Tests.Gui.Behaviors;

/// <summary>入力の列の乱数の手の列の生成器（<see cref="CanvasInputGenerator"/>）が作らない形の宣言</summary>
/// <remarks>純粋なデータ（Engines の型と BCL だけを参照する）。</remarks>
internal static class CanvasInputNotGenerated
{
    /// <summary>宣言</summary>
    public static NotGeneratedDeclaration Declaration { get; } =
        new(
            "CanvasInputGenerator",
            [ProtectionNames.EnginePart(EngineIds.DiagramEdit, "CanvasInput")],
            [
                NotGenerated.Pending(
                    "wheel-and-reorder-edges",
                    "Shift とホイール・幅の下限とズームの端・列一覧の 3 行目以降の並べ替え",
                    "きっかけ待ち（入力の列の残りの区切り）",
                    "ホイールは修飾キーなし、ズームは端に届かない範囲、列一覧の並べ替えは先頭の 2 行だけ。ビューポートのビヘイビアと列一覧の並べ替えはこれらで分岐する",
                    NotGenerated.Unguarded
                ),
                NotGenerated.Accepted(
                    "collapsible-column",
                    "列の折り畳み（CollapsibleColumnBehavior）の操作",
                    "列の折り畳みはキャンバスの入力の持ち主（Space・パン・ドラッグ）を読まない独立のビヘイビアで、単体のテストが受け持つ",
                    "`CollapsibleColumnBehaviorTests`"
                ),
                NotGenerated.Accepted(
                    "window-switch-while-pressed",
                    "マウスのボタンを押したままの別のウィンドウの行き来・キャンバスの付け外し",
                    "押したまま非アクティブになる・外れると、キャプチャが OS の都合で外れ、仕様のモデルで扱わない。行き来と付け外しはボタンを押していないときにだけ置く",
                    NotGenerated.Unguarded
                ),
            ]
        );
}
