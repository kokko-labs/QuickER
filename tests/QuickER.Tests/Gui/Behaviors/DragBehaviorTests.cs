using AwesomeAssertions;
using QuickER.Behaviors;
using QuickER.UndoRedo;

namespace QuickER.Tests.Gui.Behaviors;

/// <summary>
/// <see cref="DragBehavior"/> のうち、実マウス入力を伴わずに検証できるロジックを扱うテストクラス。
/// </summary>
/// <remarks>
/// マウス押下 → 移動 → 解放の実経路はヘッドレスでは再現できないため、リサイズ完了時の
/// 「幅が変わったときだけ変更世代を進める」判定だけを <see cref="DragBehavior.MarkWidthChanged"/> として
/// 切り出し、その単位で検証する。
/// </remarks>
public class DragBehaviorTests
{
    /// <summary>幅が変化したリサイズはダーティ判定用の変更世代を進めることを検証する</summary>
    [Fact(DisplayName = "リサイズで幅が変わると変更世代が進む")]
    public void MarkWidthChanged_WidthChanged_BumpsGeneration()
    {
        var mgr = new UndoRedoManager();
        var before = mgr.ChangeGeneration;

        DragBehavior.MarkWidthChanged(mgr, oldWidth: 200, newWidth: 260);

        mgr.ChangeGeneration.Should().NotBe(before);
        mgr.CanUndo.Should().BeFalse("幅変更は Undo 履歴へ積まない");
    }

    /// <summary>幅が変わらないリサイズ（掴んで戻した等）では変更世代が動かないことを検証する</summary>
    [Fact(DisplayName = "リサイズで幅が変わらなければ変更世代は動かない")]
    public void MarkWidthChanged_SameWidth_KeepsGeneration()
    {
        var mgr = new UndoRedoManager();
        var before = mgr.ChangeGeneration;

        DragBehavior.MarkWidthChanged(mgr, oldWidth: 200, newWidth: 200);

        mgr.ChangeGeneration.Should().Be(before);
    }

    /// <summary>添付プロパティ未設定（マネージャ null）でも例外にならないことを検証する</summary>
    [Fact(DisplayName = "マネージャ未設定でも例外にならない")]
    public void MarkWidthChanged_NullManager_DoesNotThrow()
    {
        var act = () => DragBehavior.MarkWidthChanged(null, oldWidth: 200, newWidth: 260);

        act.Should().NotThrow();
    }
}
