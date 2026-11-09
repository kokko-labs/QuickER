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
/// 進行中状態は静的フィールドで保持されるが、UI スレッドの持ち物として <c>[ThreadStatic]</c> が
/// 付いているため、並列実行される他クラスのテストとは互いに干渉しない（下の隔離テストが固定）。
/// 同じ状態を書くテストクラスどうしは <c>CanvasInteractionState</c> コレクションで直列化する
/// （xunit はコレクション内を並列実行しない）。
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

    /// <summary>進行中状態がスレッドごとに独立しており、他スレッドのキャンセルで消えないことを検証する</summary>
    /// <remarks>
    /// 進行中状態からフィールドの <c>[ThreadStatic]</c> を外すと、外部変更を処理する並列テスト
    /// （<c>HandleExternalModification</c> を通るものすべて）のキャンセルが、こちらのドラッグを
    /// 途中で消してしまう。消えたことは後続の表明でしか分からないので散発的な失敗に化ける。
    /// </remarks>
    [Fact(DisplayName = "ドラッグの進行中状態はスレッドごとに独立する")]
    public void InteractionState_IsIsolatedPerThread()
    {
        var entity = CreateEntity("A", 10, 20);
        DragBehavior.BeginInteractionForTests(entity, startX: 10, startY: 20);

        // ドラッグ中の逐次反映を模す
        entity.X = 330;
        entity.Y = 260;

        var sawInteraction = true;
        var other = new Thread(() =>
        {
            sawInteraction = DragBehavior.IsInteractionActiveForTests;

            try
            {
                DragBehavior.CancelActiveDrag();
            }
            catch
            {
                // 隔離が壊れていると他スレッドの UI 要素へ触れて落ちる。
                // 未処理例外はテストホストごと落として失敗を読めなくするため、ここで握る
                // （壊れていることは下の表明が報告する）
            }
        });
        other.Start();
        other.Join();

        sawInteraction.Should().BeFalse("別スレッドからは進行中に見えない");
        DragBehavior
            .IsInteractionActiveForTests.Should()
            .BeTrue("他スレッドのキャンセルでは消えない");
        entity.X.Should().Be(330, "他スレッドのキャンセルは座標を戻さない");

        DragBehavior.CancelActiveDrag();

        entity.X.Should().Be(10, "自スレッドのキャンセルは従来どおり戻す");
        entity.Y.Should().Be(20);
    }

    /// <summary>ラバーバンドの進行中状態もスレッドごとに独立することを検証する</summary>
    [Fact(DisplayName = "ラバーバンドの進行中状態はスレッドごとに独立する")]
    public void SelectionState_IsIsolatedPerThread()
    {
        WpfApplicationTestSupport.RunSta(() =>
        {
            var main = new MainViewModel();
            var surface = new Grid { DataContext = main };
            main.IsRubberBandVisible = true;

            RubberBandBehavior.BeginSelectionForTests(surface);

            var sawSelection = true;
            var other = new Thread(() =>
            {
                sawSelection = RubberBandBehavior.IsSelectionActiveForTests;

                try
                {
                    RubberBandBehavior.CancelActiveSelection();
                }
                catch
                {
                    // 上と同じ理由（隔離が壊れていると他スレッドの UI 要素へ触れて落ちる）
                }
            });
            other.Start();
            other.Join();

            sawSelection.Should().BeFalse("別スレッドからは進行中に見えない");
            RubberBandBehavior
                .IsSelectionActiveForTests.Should()
                .BeTrue("他スレッドのキャンセルでは消えない");
            main.IsRubberBandVisible.Should().BeTrue("他スレッドのキャンセルは矩形を消さない");

            RubberBandBehavior.CancelActiveSelection();

            main.IsRubberBandVisible.Should().BeFalse("自スレッドのキャンセルは従来どおり消す");
        });
    }
}
