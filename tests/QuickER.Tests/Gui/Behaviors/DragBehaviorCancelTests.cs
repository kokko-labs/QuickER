using System.Windows.Controls;
using AwesomeAssertions;
using QuickER.Behaviors;
using QuickER.Model;
using QuickER.Tests.TestSupport;
using QuickER.UndoRedo;
using QuickER.ViewModels;

namespace QuickER.Tests.Gui.Behaviors;

/// <summary>
/// 進行中のマウス操作（移動・リサイズ・グループ移動・範囲選択）のキャンセルを検証するテストクラス。
/// </summary>
/// <remarks>
/// <para>
/// キャンセルの意味論は「ドラッグ開始時点の状態へ戻す＋履歴は不変」。座標・幅はドラッグ中に
/// 逐次 ViewModel へ反映されているため、静的状態を落とすだけでは「位置は動いたのに Undo 履歴に無い」
/// 変更が図に残る（ダーティ時の確認ダイアログで「続行」を選んだ経路では、その位置が戻せないまま残る）。
/// </para>
/// <para>
/// 逆に <c>EndDrag</c> 相当を走らせて <c>MoveEntityCommand</c> を積むコミット方式は採れない
/// （クリーンな文書がコミットでダーティ化し、外部変更の分岐が自動再読込から確認ダイアログへ化ける）。
/// </para>
/// <para>
/// 進行中状態は静的フィールドで保持されるため、同じ状態を触るテストは
/// <c>CanvasInteractionState</c> コレクションで直列化する（xunit はコレクション内を並列実行しない）。
/// </para>
/// </remarks>
[Collection("CanvasInteractionState")]
public class DragBehaviorCancelTests : IDisposable
{
    public DragBehaviorCancelTests() => ResetStaticState();

    public void Dispose() => ResetStaticState();

    /// <summary>ビヘイビアの静的な進行中状態を初期化する（テスト間の持ち越しを断つ）</summary>
    private static void ResetStaticState()
    {
        DragBehavior.CancelActiveDrag();
        RubberBandBehavior.CancelActiveSelection();
    }

    /// <summary>座標を指定したエンティティ ViewModel を作る</summary>
    private static EntityViewModel CreateEntity(string tableName, double x, double y)
    {
        var vm = new EntityViewModel(new Entity { TableName = tableName }) { X = x, Y = y };
        return vm;
    }

    /// <summary>単一移動のキャンセルで座標が開始位置へ戻り、履歴が動かないことを検証する</summary>
    [Fact(DisplayName = "移動のキャンセルで座標がドラッグ開始位置へ戻る")]
    public void CancelActiveDrag_Move_RestoresStartPosition()
    {
        var manager = new UndoRedoManager();
        var entity = CreateEntity("A", 10, 20);

        DragBehavior.BeginInteractionForTests(entity, startX: 10, startY: 20);

        // ドラッグ中の逐次反映を模す
        entity.X = 300;
        entity.Y = 400;

        var generationBefore = manager.ChangeGeneration;
        DragBehavior.CancelActiveDrag();

        entity.X.Should().Be(10);
        entity.Y.Should().Be(20);
        manager.ChangeGeneration.Should().Be(generationBefore, "履歴・変更世代には触れない");
        manager.CanUndo.Should().BeFalse();
    }

    /// <summary>リサイズのキャンセルで幅が開始値へ戻ることを検証する</summary>
    [Fact(DisplayName = "リサイズのキャンセルで幅がドラッグ開始値へ戻る")]
    public void CancelActiveDrag_Resize_RestoresStartWidth()
    {
        var entity = CreateEntity("A", 0, 0);
        entity.Width = 200;

        DragBehavior.BeginInteractionForTests(
            entity,
            startX: 0,
            startY: 0,
            startWidth: 200,
            resizing: true
        );

        entity.Width = 480;

        DragBehavior.CancelActiveDrag();

        entity.Width.Should().Be(200);
    }

    /// <summary>グループ移動のキャンセルで全メンバーが開始位置へ戻ることを検証する</summary>
    [Fact(DisplayName = "グループ移動のキャンセルで全メンバーが開始位置へ戻る")]
    public void CancelActiveDrag_GroupMove_RestoresAllMembers()
    {
        var first = CreateEntity("A", 10, 20);
        var second = CreateEntity("B", 110, 220);

        DragBehavior.BeginInteractionForTests(
            first,
            startX: 10,
            startY: 20,
            groupMembers: [(first, 10, 20), (second, 110, 220)]
        );

        first.X = 60;
        first.Y = 70;
        second.X = 160;
        second.Y = 270;

        DragBehavior.CancelActiveDrag();

        first.X.Should().Be(10);
        first.Y.Should().Be(20);
        second.X.Should().Be(110);
        second.Y.Should().Be(220);
    }

    /// <summary>キャンセル後は進行中状態が残らず、後続のマウス解放が選択トグルに化けないことを検証する</summary>
    /// <remarks>
    /// <c>OnMouseUp</c> の先頭ガードは「移動中でもリサイズ中でもなければ即 return」で、
    /// その条件そのものを <see cref="DragBehavior.IsInteractionActiveForTests"/> が表す。
    /// </remarks>
    [Fact(DisplayName = "キャンセル後は進行中状態が残らない（後続 MouseUp が素通りする）")]
    public void CancelActiveDrag_ClearsInteractionState()
    {
        var entity = CreateEntity("A", 0, 0);
        DragBehavior.BeginInteractionForTests(entity, startX: 0, startY: 0);
        DragBehavior.IsInteractionActiveForTests.Should().BeTrue();

        DragBehavior.CancelActiveDrag();

        DragBehavior.IsInteractionActiveForTests.Should().BeFalse();
    }

    /// <summary>進行中でないときのキャンセルが無害（例外なし・副作用なし）であることを検証する</summary>
    [Fact(DisplayName = "進行中でなければキャンセルは何もしない")]
    public void CancelActiveDrag_WhenIdle_DoesNothing()
    {
        var act = () => DragBehavior.CancelActiveDrag();

        act.Should().NotThrow();
        DragBehavior.IsInteractionActiveForTests.Should().BeFalse();
    }

    /// <summary>範囲選択のキャンセルで矩形が消え、交差選択が確定しないことを検証する</summary>
    [Fact(DisplayName = "範囲選択のキャンセルで矩形が消え選択は確定しない")]
    public void CancelActiveSelection_HidesRubberBandWithoutApplyingSelection()
    {
        WpfApplicationTestSupport.RunSta(() =>
        {
            var main = new MainViewModel();
            var entity = CreateEntity("A", 0, 0);
            main.Entities.Add(entity);

            var surface = new Grid { DataContext = main };
            main.IsRubberBandVisible = true;
            main.RubberBandX = 0;
            main.RubberBandY = 0;
            main.RubberBandWidth = 1000;
            main.RubberBandHeight = 1000;

            RubberBandBehavior.BeginSelectionForTests(surface);
            RubberBandBehavior.IsSelectionActiveForTests.Should().BeTrue();

            RubberBandBehavior.CancelActiveSelection();

            RubberBandBehavior.IsSelectionActiveForTests.Should().BeFalse();
            main.IsRubberBandVisible.Should().BeFalse("矩形は消す");
            entity.IsSelected.Should().BeFalse("交差していても選択は確定しない");
        });
    }
}
