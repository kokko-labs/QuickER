using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using AwesomeAssertions;
using QuickER.Runtime;
using Xunit;

namespace QuickER.Tests.CodeGen.CSharp;

/// <summary>
/// ランタイムパッケージの <see cref="GraphSaveOrder"/> が、削除する行と主キー・一意な値が重なる新しい行だけを後回しにすることを
/// 検証するテストクラス。
/// </summary>
/// <remarks>
/// 実装先ごとの書く順は実行時のテスト（BackendParity・GraphSaveOrderRuntimeTests）が見る。ここでは規則そのもの
/// （どの値を削除する行の値として集めるか・どう突き合わせるか）を、手書きのエンティティで直接確かめる。
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

    [Fact(DisplayName = "GraphSaveOrder: 行を削除しない保存は計画を作らない（何も後回しにしない）")]
    public void For_WithoutDeletion_ReturnsNull()
    {
        var roots = new[] { Parent(1, "A", RowState.Added), Parent(2, "B", RowState.Updated) };

        GraphSaveOrder.For(roots, cascadeSave: true, cascadeDelete: true).Should().BeNull();
    }

    [Fact(
        DisplayName = "GraphSaveOrder: 削除する行の一意な値・主キーを取る新しい行だけを後回しにし、拾った順に並べる"
    )]
    public void TryHoldBack_HoldsBackOnlyNewRowsThatTakeDeletedValues()
    {
        var deleted = Parent(1, "Dan", RowState.Removed);
        var takesName = Parent(2, "Dan", RowState.Added);
        var takesKey = Parent(1, "Other", RowState.Added);
        var unrelated = Parent(3, "Eve", RowState.Added);
        var updated = Parent(4, "Dan", RowState.Updated);

        var order = GraphSaveOrder.For([deleted], cascadeSave: true, cascadeDelete: true)!;

        order.TryHoldBack(unrelated).Should().BeFalse("値が重ならない新しい行は並びどおり");
        order.TryHoldBack(updated).Should().BeFalse("後回しにするのは新しい行だけ");
        order.TryHoldBack(takesName).Should().BeTrue("一意制約の値が重なる");
        order.TryHoldBack(takesKey).Should().BeTrue("主キーが重なる");
        order.HeldBack.Should().Equal(takesName, takesKey);
    }

    [Fact(
        DisplayName = "GraphSaveOrder: 文字列は大文字小文字を区別せずに突き合わせ、null を含む値は何にも重ならない"
    )]
    public void TryHoldBack_MatchesTextIgnoringCase_AndNullMatchesNothing()
    {
        var order = GraphSaveOrder.For(
            [Parent(1, "dan", RowState.Removed), Parent(2, null, RowState.Removed)],
            cascadeSave: true,
            cascadeDelete: true
        )!;

        order.TryHoldBack(Parent(3, "DAN", RowState.Added)).Should().BeTrue();
        order.TryHoldBack(Parent(4, null, RowState.Added)).Should().BeFalse();
    }

    [Fact(
        DisplayName = "GraphSaveOrder: 削除が連鎖するときは、削除する行の下の既存の行の値も集める（新しい行の値は集めない）"
    )]
    public void For_CollectsTheSubtreeOfADeletedRow_WhenDeletionCascades()
    {
        var deleted = Parent(1, "A", RowState.Removed);
        deleted.Children.Add(Child(10, 1, "x", RowState.Unchanged));
        deleted.Children.Add(Child(11, 1, "y", RowState.Added));

        var cascading = GraphSaveOrder.For([deleted], cascadeSave: true, cascadeDelete: true)!;
        cascading
            .TryHoldBack(Child(20, 1, "x", RowState.Added))
            .Should()
            .BeTrue("既存の子の値は削除で空く");
        cascading
            .TryHoldBack(Child(21, 1, "y", RowState.Added))
            .Should()
            .BeFalse("新しい子は保存先に行を持たない");

        var notCascading = GraphSaveOrder.For([deleted], cascadeSave: true, cascadeDelete: false)!;
        notCascading
            .TryHoldBack(Child(20, 1, "x", RowState.Added))
            .Should()
            .BeFalse("子は削除されない");
    }

    [Fact(DisplayName = "GraphSaveOrder: カスケードの子をたどらない保存は、子の削除の印を数えない")]
    public void For_WithoutCascadeSave_IgnoresRemovedChildren()
    {
        var root = Parent(1, "A", RowState.Unchanged);
        root.Children.Add(Child(10, 1, "x", RowState.Removed));

        GraphSaveOrder.For([root], cascadeSave: false, cascadeDelete: true).Should().BeNull();
        GraphSaveOrder.For([root], cascadeSave: true, cascadeDelete: true).Should().NotBeNull();
    }

    [Fact(DisplayName = "GraphSaveOrder: 型の違う行どうしは、主キーの値が同じでも重ならない")]
    public void TryHoldBack_DoesNotMatchAcrossEntityTypes()
    {
        var order = GraphSaveOrder.For(
            [Parent(1, "A", RowState.Removed)],
            cascadeSave: true,
            cascadeDelete: true
        )!;

        order.TryHoldBack(Child(1, 9, "z", RowState.Added)).Should().BeFalse();
    }
}
