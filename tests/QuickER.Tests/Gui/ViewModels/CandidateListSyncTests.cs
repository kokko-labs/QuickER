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

    /// <summary>通知について確かめること</summary>
    public enum Notifications
    {
        /// <summary>確かめない</summary>
        NotChecked,

        /// <summary>何も通知しない</summary>
        None,

        /// <summary>項目を外さず、Move だけで通知する</summary>
        MovesOnly,
    }

    /// <summary>項目の並びの表記（A〜D の文字の並び。空文字列は空）から項目を作る</summary>
    private static Item[] Items(string letters) =>
        [
            .. letters.Select(letter =>
                new[] { A, B, C, D }.Single(item => item.Name == $"{letter}")
            ),
        ];

    [Theory(
        DisplayName = "差分同期は、現状の並びを重複なく望む並びへ合わせ、通知は決まった形に収める"
    )]
    [InlineData("ABC", "BAC", Notifications.NotChecked)] // 途中の項目を先頭へ移しても重複せず、望む並びになる
    [InlineData("ABC", "CDA", Notifications.NotChecked)] // 移動・挿入・削除が同時でも望む並びになる
    [InlineData("ABC", "CAB", Notifications.MovesOnly)] // 残る項目は外さずに Move で動かす（選択中の項目を ItemsSource から一度も外さない）
    [InlineData("BACBC", "ABC", Notifications.NotChecked)] // 重複して並んだ壊れた状態からも、重複のない望む並びへ戻る
    [InlineData("ABC", "ABC", Notifications.None)] // 同じ並びなら何も通知しない
    [InlineData("AB", "", Notifications.NotChecked)] // 空へ合わせると全部消える
    [InlineData("", "CA", Notifications.NotChecked)] // 空から合わせると全部入る
    public void SyncTo_ProducesDesiredOrder(
        string current,
        string target,
        Notifications notifications
    )
    {
        var candidates = new ObservableCollection<Item>(Items(current));
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

        CandidateListSync.SyncTo(candidates, Items(target));

        candidates.Should().Equal(Items(target));

        if (notifications == Notifications.None)
        {
            actions.Should().BeEmpty("同じ並びなら何も通知しない");
        }

        if (notifications == Notifications.MovesOnly)
        {
            removed.Should().BeEmpty("並びの違いだけなら項目を外さない");
            actions.Should().OnlyContain(action => action == NotifyCollectionChangedAction.Move);
        }
    }
}
