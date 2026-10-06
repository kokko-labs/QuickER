using System.Collections.ObjectModel;
using System.Collections.Specialized;
using AwesomeAssertions;
using QuickER.ViewModels;

namespace QuickER.Tests.Gui.ViewModels;

/// <summary>
/// 選択候補コレクションの差分同期（<see cref="CandidateListSync"/>）を検証するテストクラス。
/// </summary>
/// <remarks>
/// 並べ替えを削除と挿入だけで追うと、残る項目の前へ同じ項目が挿入されて候補が重複する。
/// また選択中の項目を一度外すとコンボボックスが選択を落とすため、残る項目は外さず Move で動かすことも固定する。
/// </remarks>
public class CandidateListSyncTests
{
    /// <summary>テスト用の参照型の項目（同一性は参照で判定される）</summary>
    private sealed record Item(string Name);

    private static readonly Item A = new("A");
    private static readonly Item B = new("B");
    private static readonly Item C = new("C");
    private static readonly Item D = new("D");

    [Fact(DisplayName = "途中の項目を先頭へ移しても重複せず、望む並びになる")]
    public void MoveToFront_ProducesDesiredOrderWithoutDuplicates()
    {
        var candidates = new ObservableCollection<Item>([A, B, C]);

        CandidateListSync.SyncTo(candidates, [B, A, C]);

        candidates.Should().Equal(B, A, C);
    }

    [Fact(DisplayName = "移動・挿入・削除が同時でも望む並びになる")]
    public void MixedChanges_ProduceDesiredOrder()
    {
        var candidates = new ObservableCollection<Item>([A, B, C]);

        CandidateListSync.SyncTo(candidates, [C, D, A]);

        candidates.Should().Equal(C, D, A);
    }

    [Fact(
        DisplayName = "残る項目は外さずに Move で動かす（選択中の項目を ItemsSource から一度も外さない）"
    )]
    public void RetainedItems_AreMovedNotRemoved()
    {
        var candidates = new ObservableCollection<Item>([A, B, C]);
        var removed = new List<object>();
        var actions = new List<NotifyCollectionChangedAction>();
        candidates.CollectionChanged += (_, e) =>
        {
            actions.Add(e.Action);

            if (e.OldItems is not null && e.Action != NotifyCollectionChangedAction.Move)
            {
                removed.AddRange(e.OldItems.Cast<object>());
            }
        };

        CandidateListSync.SyncTo(candidates, [C, A, B]);

        candidates.Should().Equal(C, A, B);
        removed.Should().BeEmpty("並びの違いだけなら項目を外さない");
        actions.Should().OnlyContain(action => action == NotifyCollectionChangedAction.Move);
    }

    [Fact(DisplayName = "重複して並んだ壊れた状態からも、重複のない望む並びへ戻る")]
    public void DuplicatedState_IsRepaired()
    {
        var candidates = new ObservableCollection<Item>([B, A, C, B, C]);

        CandidateListSync.SyncTo(candidates, [A, B, C]);

        candidates.Should().Equal(A, B, C);
    }

    [Fact(DisplayName = "同じ並びなら何も通知しない")]
    public void SameOrder_RaisesNoNotification()
    {
        var candidates = new ObservableCollection<Item>([A, B, C]);
        var raised = 0;
        candidates.CollectionChanged += (_, _) => raised++;

        CandidateListSync.SyncTo(candidates, [A, B, C]);

        raised.Should().Be(0);
    }

    [Fact(DisplayName = "空へ合わせると全部消え、空から合わせると全部入る")]
    public void EmptyBoundaries()
    {
        var candidates = new ObservableCollection<Item>([A, B]);

        CandidateListSync.SyncTo(candidates, []);
        candidates.Should().BeEmpty();

        CandidateListSync.SyncTo(candidates, [C, A]);
        candidates.Should().Equal(C, A);
    }
}
