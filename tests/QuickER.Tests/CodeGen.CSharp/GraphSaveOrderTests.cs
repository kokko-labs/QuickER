using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using AwesomeAssertions;
using QuickER.Runtime;
using Xunit;

namespace QuickER.Tests.CodeGen.CSharp;

/// <summary>
/// ランタイムパッケージの <see cref="GraphSaveOrder"/> が、削除を含む保存を「削除しない書き込み → 削除 → 後回しの行」の
/// 3 段に分け、削除する行の値を取る行だけを後回しにすることを検証するテストクラス。
/// </summary>
/// <remarks>
/// 実装先ごとの書く順は実行時のテスト（BackendParity・GraphSaveOrderRuntimeTests）が見る。ここでは規則そのもの
/// （段の始点の並び・どの値を削除する行の値として集めるか・どう突き合わせるか・どの行を飛ばすか）を、手書きのエンティティで
/// 直接確かめる。
/// </remarks>
public class GraphSaveOrderTests
{
    [Table("parents")]
    [UniqueConstraint("Name", Name = "UQ_parents_name")]
    private sealed class ParentEntity : EntityBaseCore
    {
        [Key]
        [Column("parent_id")]
        public int ParentId { get; set; }

        [Column("name")]
        public string? Name { get; set; }

        [NavigationReference("parents", "parent_id", "children", "parent_id", true, true, false)]
        public List<ChildEntity> Children { get; set; } = [];
    }

    [Table("children")]
    [UniqueConstraint("ParentId", "Code", Name = "UQ_children_parent_id_code")]
    private sealed class ChildEntity : EntityBaseCore
    {
        [Key]
        [Column("child_id")]
        public int ChildId { get; set; }

        [Column("parent_id")]
        public int ParentId { get; set; }

        [Column("code")]
        public string Code { get; set; } = string.Empty;
    }

    private static ParentEntity Parent(int id, string? name, RowState state) =>
        new()
        {
            ParentId = id,
            Name = name,
            RowState = state,
        };

    private static ChildEntity Child(int id, int parentId, string code, RowState state) =>
        new()
        {
            ChildId = id,
            ParentId = parentId,
            Code = code,
            RowState = state,
        };

    [Fact(
        DisplayName = "GraphSaveOrder: 行を削除しない保存は計画を作らない（グラフの順のまま保存する）"
    )]
    public void For_WithoutDeletion_ReturnsNull()
    {
        var roots = new[] { Parent(1, "A", RowState.Added), Parent(2, "B", RowState.Updated) };

        GraphSaveOrder.For(roots, cascadeSave: true, cascadeDelete: true).Should().BeNull();
    }

    [Fact(
        DisplayName = "GraphSaveOrder: 削除を含む保存は、削除しない書き込み・削除・後回しの行の順に段の始点を並べる"
    )]
    public void For_OrdersStartingPointsInThreeStages()
    {
        var deleted = Parent(1, "A", RowState.Removed);
        var updated = Parent(2, "B", RowState.Updated);
        var added = Parent(3, "C", RowState.Added);
        var takesValue = Parent(4, "A", RowState.Added);

        var order = GraphSaveOrder.For(
            [deleted, takesValue, updated, added],
            cascadeSave: true,
            cascadeDelete: true
        )!;

        order.StartingPoints.Should().Equal(updated, added, deleted, takesValue);
        order.Deletions.Should().Equal(deleted);
        order.HeldBack.Should().Equal(takesValue);
    }

    [Fact(
        DisplayName = "GraphSaveOrder: 削除する行の主キー・一意な値を取る新しい行と、一意な値（主キーを除く）を取る更新する行を後回しにし、拾った順に並べる"
    )]
    public void For_HoldsBackOnlyRowsThatTakeDeletedValues()
    {
        var deleted = Parent(1, "Dan", RowState.Removed);
        var takesName = Parent(2, "Dan", RowState.Added);
        var takesKey = Parent(1, "Other", RowState.Added);
        var unrelated = Parent(3, "Eve", RowState.Added);
        var updatedTakesName = Parent(4, "Dan", RowState.Updated);
        var updatedSameKey = Parent(1, "Zed", RowState.Updated);

        var order = GraphSaveOrder.For(
            [deleted, takesName, unrelated, updatedTakesName, takesKey, updatedSameKey],
            cascadeSave: true,
            cascadeDelete: true
        )!;

        order
            .HeldBack.Should()
            .Equal(
                [takesName, updatedTakesName, takesKey],
                "値が重ならない行と、主キーが重なるだけの更新する行は並びどおり"
            );
        order.Defers(unrelated).Should().BeFalse();
        order.Defers(updatedSameKey).Should().BeFalse("更新する行の主キーは数えない");
    }

    [Fact(
        DisplayName = "GraphSaveOrder: 文字列は大文字小文字を区別せずに突き合わせ、null を含む値は何にも重ならない"
    )]
    public void For_MatchesTextIgnoringCase_AndNullMatchesNothing()
    {
        var upperCase = Parent(3, "DAN", RowState.Added);
        var withoutName = Parent(4, null, RowState.Added);

        var order = GraphSaveOrder.For(
            [
                Parent(1, "dan", RowState.Removed),
                Parent(2, null, RowState.Removed),
                upperCase,
                withoutName,
            ],
            cascadeSave: true,
            cascadeDelete: true
        )!;

        order.HeldBack.Should().Equal(upperCase);
        order.Defers(withoutName).Should().BeFalse();
    }

    [Fact(
        DisplayName = "GraphSaveOrder: 削除が連鎖するときは、削除する行の下の既存の行の値も集める（新しい行の値は集めない）"
    )]
    public void For_CollectsTheSubtreeOfADeletedRow_WhenDeletionCascades()
    {
        GraphSaveOrder Plan(
            bool cascadeDelete,
            out ChildEntity takesExisting,
            out ChildEntity takesNew
        )
        {
            var deleted = Parent(1, "A", RowState.Removed);
            deleted.Children.Add(Child(10, 1, "x", RowState.Unchanged));
            deleted.Children.Add(Child(11, 1, "y", RowState.Added));
            var other = Parent(2, "B", RowState.Unchanged);
            takesExisting = Child(20, 1, "x", RowState.Added);
            takesNew = Child(21, 1, "y", RowState.Added);
            other.Children.Add(takesExisting);
            other.Children.Add(takesNew);

            return GraphSaveOrder.For([deleted, other], cascadeSave: true, cascadeDelete)!;
        }

        var cascading = Plan(cascadeDelete: true, out var existing, out var added);
        cascading
            .HeldBack.Should()
            .Equal([existing], "既存の子の値は削除で空き、新しい子は保存先に行を持たない");
        cascading.Defers(added).Should().BeFalse();

        var notCascading = Plan(cascadeDelete: false, out existing, out _);
        notCascading.HeldBack.Should().BeEmpty("子は削除されない");
        notCascading.Defers(existing).Should().BeFalse();
    }

    [Fact(DisplayName = "GraphSaveOrder: カスケードの子をたどらない保存は、子の削除の印を数えない")]
    public void For_WithoutCascadeSave_IgnoresRemovedChildren()
    {
        var root = Parent(1, "A", RowState.Unchanged);
        var removed = Child(10, 1, "x", RowState.Removed);
        root.Children.Add(removed);

        GraphSaveOrder.For([root], cascadeSave: false, cascadeDelete: true).Should().BeNull();

        var order = GraphSaveOrder.For([root], cascadeSave: true, cascadeDelete: true)!;
        order.StartingPoints.Should().Equal(root, removed);
        order.Defers(removed).Should().BeTrue("子の削除は 2 段目の始点になる");
    }

    [Fact(DisplayName = "GraphSaveOrder: 型の違う行どうしは、主キーの値が同じでも重ならない")]
    public void For_DoesNotMatchAcrossEntityTypes()
    {
        var root = Parent(2, "B", RowState.Unchanged);
        var child = Child(1, 2, "z", RowState.Added);
        root.Children.Add(child);

        var order = GraphSaveOrder.For(
            [Parent(1, "A", RowState.Removed), root],
            cascadeSave: true,
            cascadeDelete: true
        )!;

        order.HeldBack.Should().BeEmpty();
        order.Defers(child).Should().BeFalse();
    }

    [Fact(
        DisplayName = "GraphSaveOrder: 後回しの行の下の削除は 2 段目へ回し、後回しの行の下の行は個別には後回しにしない"
    )]
    public void For_DeletionsBelowAHeldBackRowGoToTheSecondStage()
    {
        var deleted = Parent(1, "Dan", RowState.Removed);
        var heldBack = Parent(2, "Dan", RowState.Updated);
        var removedChild = Child(10, 2, "a", RowState.Removed);
        var addedChild = Child(11, 2, "a", RowState.Added);
        heldBack.Children.Add(removedChild);
        heldBack.Children.Add(addedChild);

        var order = GraphSaveOrder.For(
            [deleted, heldBack],
            cascadeSave: true,
            cascadeDelete: true
        )!;

        order.StartingPoints.Should().Equal(deleted, removedChild, heldBack);
        order.Defers(heldBack).Should().BeTrue();
        order.Defers(removedChild).Should().BeTrue("削除は 2 段目で済ませ、3 段目の歩きでは飛ばす");
        order
            .Defers(addedChild)
            .Should()
            .BeFalse("後回しの行の下の行は、後回しの行と一緒に 3 段目で書く");
    }
}
