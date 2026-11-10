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
                    "ctrl-at-press",
                    "押下の時点の Ctrl（空白の押下で加算の矩形選択・カードの Ctrl クリックで選択の切り替え）",
                    "G5",
                    "Ctrl は Space の押下の間のパンとグループの移動の 2 番目のクリックにだけ混ぜる。矩形選択とカードのドラッグは押下の時点の Ctrl で分岐する",
                    NotGenerated.Unguarded
                ),
                NotGenerated.Pending(
                    "wheel-and-reorder-edges",
                    "Shift とホイール・幅の下限とズームの端・列一覧の 3 行目以降の並べ替え",
                    "入力の列の残りの区切り（未起動）",
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
