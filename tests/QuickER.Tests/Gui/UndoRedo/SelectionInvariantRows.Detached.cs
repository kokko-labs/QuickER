using AwesomeAssertions;
using QuickER.Tests.Gui.ViewModels;
using QuickER.ViewModels;

namespace QuickER.Tests.Gui.UndoRedo;

internal static partial class SelectionInvariantRows
{
    /// <summary>図から外れた要素（旧図の要素・外した制約やリレーションの行）への書き込みが、履歴にも復元にも届かない</summary>
    private static IEnumerable<SelectionRow> DetachedWriteRows()
    {
        yield return Row(
                "detached-replace-diagram-edits-of-previous-elements-do-not-reach-history",
                "図を置換すると旧要素の編集は履歴へ届かない"
            )
            .Diagram(() =>
            {
                var vm = new MainViewModel();
                vm.ReplaceDiagramFromModule(DiagramChangeTrackerDetachTests.BuildDiagram("Old"));

                return vm;
            })
            .Step(
                "旧図の要素を覚える",
                context =>
                {
                    var oldEntity = context.Vm.Entities[0];
                    context.Remember("entity", oldEntity);
                    context.Remember("column", oldEntity.Columns[0]);
                    context.Remember("constraint", oldEntity.UniqueConstraints[0]);
                    context.Remember("relationship", context.Vm.Relationships[0]);
                }
            )
            .Step(
                "旧図とは Guid がまったく重ならない図で丸ごと置換する",
                context =>
                {
                    context.Vm.ReplaceDiagramFromModule(
                        DiagramChangeTrackerDetachTests.BuildDiagram("New")
                    );
                    context.Remember("generation", context.Vm.UndoRedo.ChangeGeneration);
                }
            )
            .Step(
                "旧要素のプロパティを書き換える",
                context =>
                {
                    context.Recall<EntityViewModel>("entity").TableName = "GhostTable";
                    context.Recall<EntityViewModel>("entity").Memo = "GhostMemo";
                    context.Recall<ColumnViewModel>("column").Name = "GhostColumn";
                    context.Recall<ColumnViewModel>("column").DataType = "nvarchar(10)";
                    context.Recall<UniqueConstraintViewModel>("constraint").Name = "GhostUnique";
                    context.Recall<RelationshipViewModel>("relationship").ConstraintName =
                        "GhostFk";
                }
            )
            .Expect(
                "図から外れた要素の変更は変更世代を動かさず、幽霊コマンドが Undo スタックへ積まれない",
                context =>
                {
                    context
                        .Vm.UndoRedo.ChangeGeneration.Should()
                        .Be(context.Recall<int>("generation"));
                    context.Vm.UndoRedo.CanUndo.Should().BeFalse();
                }
            )
            .Covers(SelectionPointer.Candidate, SelectionDetach.Replace);

        yield return Row(
                "detached-write-to-removed-constraint-row-keeps-undo-intact",
                "図から外れた一意制約の行への書き込みは、Undo で戻る制約を変えない"
            )
            .Diagram(() => CandidateRowReconcileTests.BuildUniqueConstraint().Vm)
            .Step(
                "制約の先頭の行を覚える",
                context =>
                {
                    var constraint = context.Vm.Entities.Single().UniqueConstraints.Single();
                    context.Remember("constraint", constraint);
                    context.Remember("member", constraint.Members[0]);
                }
            )
            .Step(
                "制約を削除する",
                context =>
                    context.Vm.RemoveUniqueConstraintCommand.Execute(
                        context.Recall<UniqueConstraintViewModel>("constraint")
                    )
            )
            .Step(
                "外れた行の選択を落とす",
                context =>
                    context.Recall<UniqueConstraintMemberViewModel>("member").SelectedColumn = null
            )
            .Step("Undo", (MainViewModel vm) => vm.UndoCommand.Execute(null))
            .Expect(
                "図から外れた制約の行への書き込みは正本を変えない",
                context =>
                {
                    var entity = context.Vm.Entities.Single();
                    var constraint = context.Recall<UniqueConstraintViewModel>("constraint");

                    entity
                        .UniqueConstraints.Should()
                        .ContainSingle()
                        .Which.Should()
                        .BeSameAs(constraint);
                    CandidateRowReconcileTests
                        .MemberNames(entity, constraint)
                        .Should()
                        .Equal("A", "B");
                }
            )
            .Covers(SelectionPointer.Candidate, SelectionDetach.Delete, SelectionDetach.Undo);

        yield return Row(
                "detached-write-to-removed-relationship-row-keeps-undo-intact",
                "図から外れたリレーションの行への書き込みは、Undo で戻るリレーションを変えない"
            )
            .Diagram(() => CandidateRowReconcileTests.BuildCompositeForeignKey().Vm)
            .Step(
                "リレーションの先頭の行を覚え、リレーションを選ぶ",
                context =>
                {
                    var relationship = context.Vm.Relationships.Single();
                    context.Remember("relationship", relationship);
                    context.Remember("row", relationship.ColumnPairRows[0]);
                    context.Vm.OnRelationshipClicked(relationship);
                }
            )
            .Step(
                "選択中のリレーションを削除する",
                (MainViewModel vm) => vm.RemoveSelectedRelationshipCommand.Execute(null)
            )
            .Step(
                "外れた行の選択を落とす",
                context =>
                    context.Recall<RelationshipColumnPairViewModel>("row").SelectedSourceColumn =
                        null
            )
            .Step("Undo", (MainViewModel vm) => vm.UndoCommand.Execute(null))
            .Expect(
                "図から外れたリレーションの行への書き込みは正本を変えない",
                context =>
                {
                    var relationship = context.Recall<RelationshipViewModel>("relationship");

                    context
                        .Vm.Relationships.Should()
                        .ContainSingle()
                        .Which.Should()
                        .BeSameAs(relationship);
                    CandidateRowReconcileTests
                        .ModelPairNames(relationship)
                        .Should()
                        .Equal(("Id", "ParentId"), ("Code", "ParentCode"));
                }
            )
            .Covers(SelectionPointer.Candidate, SelectionDetach.Delete, SelectionDetach.Undo);

        yield return Row(
                "detached-write-to-constraint-row-of-removed-entity-keeps-undo-intact",
                "図から外れたテーブルの一意制約の行への書き込みは、Undo で戻る制約を変えない"
            )
            .Diagram(() => CandidateRowReconcileTests.BuildUniqueConstraint().Vm)
            .Step(
                "制約の先頭の行を覚える",
                context =>
                {
                    var constraint = context.Vm.Entities.Single().UniqueConstraints.Single();
                    context.Remember("constraint", constraint);
                    context.Remember("member", constraint.Members[0]);
                }
            )
            .Step(
                "選択中のテーブルを削除する",
                (MainViewModel vm) => vm.RemoveSelectedEntityCommand.Execute(null)
            )
            .Step(
                "外れた行の選択を落とす",
                context =>
                    context.Recall<UniqueConstraintMemberViewModel>("member").SelectedColumn = null
            )
            .Step("Undo", (MainViewModel vm) => vm.UndoCommand.Execute(null))
            .Expect(
                "図から外れたテーブルの制約の行への書き込みは正本を変えない",
                context =>
                    CandidateRowReconcileTests
                        .MemberNames(
                            context.Vm.Entities.Single(),
                            context.Recall<UniqueConstraintViewModel>("constraint")
                        )
                        .Should()
                        .Equal("A", "B")
            )
            .Covers(SelectionPointer.Candidate, SelectionDetach.Delete, SelectionDetach.Undo);
    }
}
