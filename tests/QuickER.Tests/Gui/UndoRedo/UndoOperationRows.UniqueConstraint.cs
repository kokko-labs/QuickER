using AwesomeAssertions;
using QuickER.Model;
using QuickER.UndoRedo;
using QuickER.ViewModels;

namespace QuickER.Tests.Gui.UndoRedo;

internal static partial class UndoOperationRows
{
    /// <summary>
    /// 一意制約の追加・削除・構成列の編集・制約名の変更と、制約を巻き込む列の削除：確定した編集だけが履歴 1 件になり、未保存になる
    /// （MainViewModelUniqueConstraintTests の図）
    /// </summary>
    private static IEnumerable<UndoRow> UniqueConstraintRows()
    {
        yield return Row(
                "unique-constraint-add-marks-dirty-and-round-trips",
                "制約の追加は Undo で消え Redo で戻る／制約の編集（追加・構成列・制約名）はダーティ判定に乗る（追加）"
            )
            .Diagram(SavableUniqueConstraintDiagram)
            .Prepare("保存して未保存でない状態にする", SaveClean)
            .Operate(
                UndoEntry.Screen,
                "AddUniqueConstraintCommand",
                vm => vm.AddUniqueConstraintCommand.Execute(null)
            )
            .Commands(typeof(AddUniqueConstraintCommand))
            .Check(
                UndoStage.Operated,
                "制約が 1 本増える",
                vm => vm.SelectedEntity!.UniqueConstraints.Should().ContainSingle()
            );

        yield return Row(
                "unique-constraint-member-first-commit-marks-dirty",
                "制約の編集（追加・構成列・制約名）はダーティ判定に乗る（構成列）"
            )
            .Diagram(SavableUniqueConstraintDiagram)
            .Prepare(
                "制約を足して保存し、空スロットを出す",
                vm =>
                {
                    vm.AddUniqueConstraintCommand.Execute(null);
                    SaveClean(vm);
                    vm.AddUniqueConstraintMemberSlotCommand.Execute(
                        vm.SelectedEntity!.UniqueConstraints[0]
                    );
                }
            )
            .Operate(
                UndoEntry.Screen,
                "空スロットで Code を選ぶ",
                vm =>
                    vm.SelectedEntity!.UniqueConstraints[0].Members[0].SelectedColumn =
                        SelectedColumnNamed(vm, "Code")
            )
            .Commands(typeof(ChangeUniqueConstraintColumnsCommand));

        yield return Row(
                "unique-constraint-name-change-marks-dirty-and-round-trips",
                "制約名の変更は Undo / Redo で往復する／制約の編集（追加・構成列・制約名）はダーティ判定に乗る（制約名）"
            )
            .Diagram(SavableUniqueConstraintDiagram)
            .Prepare(
                "制約を足して保存する",
                vm =>
                {
                    vm.AddUniqueConstraintCommand.Execute(null);
                    SaveClean(vm);
                }
            )
            .Operate(
                UndoEntry.Tracker,
                "Name = UQ_Item_Code",
                vm => vm.SelectedEntity!.UniqueConstraints[0].Name = "UQ_Item_Code"
            )
            .Commands(typeof(PropertyChangeCommand))
            .Check(
                UndoStage.Undone,
                "制約名が空へ戻る",
                vm => vm.SelectedEntity!.UniqueConstraints[0].Name.Should().BeEmpty()
            )
            .Check(
                UndoStage.Redone,
                "制約名が戻る",
                vm => vm.SelectedEntity!.UniqueConstraints[0].Name.Should().Be("UQ_Item_Code")
            );

        yield return Row(
                "unique-constraint-member-commit-pending-slot-appends",
                "空スロットで列を選ぶと末尾へ積まれ、Undo で元の構成へ戻る"
            )
            .Diagram(ItemDiagram)
            .Prepare(
                "制約を足し、空スロットで Kind を選んで確定する",
                vm =>
                {
                    vm.AddUniqueConstraintCommand.Execute(null);
                    var constraint = vm.SelectedEntity!.UniqueConstraints[0];
                    constraint.Members.Should().BeEmpty("制約を足しただけでは構成列行は無い");

                    // ＋（列行の追加）はビュー状態だけを変え、モデルにも履歴にも触らない
                    vm.AddUniqueConstraintMemberSlotCommand.Execute(constraint);
                    constraint.Members.Should().ContainSingle();
                    constraint.Members[0].IsPendingSlot.Should().BeTrue();
                    constraint.ColumnIds.Should().BeEmpty();
                    constraint
                        .CanAddMember.Should()
                        .BeFalse("空スロットが出ている間はもう 1 行足せない（同時に 1 つまで）");

                    // 空スロットで列を選んだ時点で正本へ確定する（＝コンボボックスの選択と同じ経路）
                    var kind = SelectedColumnNamed(vm, "Kind");
                    constraint.Members[0].SelectedColumn = kind;
                    constraint.ColumnIds.Should().Equal(kind.Id);
                    constraint.Members[0].IsPendingSlot.Should().BeFalse();
                    constraint.CanAddMember.Should().BeTrue();
                }
            )
            .Operate(
                UndoEntry.Screen,
                "空スロットを出して Code を選ぶ",
                vm =>
                {
                    var constraint = vm.SelectedEntity!.UniqueConstraints[0];
                    vm.AddUniqueConstraintMemberSlotCommand.Execute(constraint);
                    constraint.Members[1].SelectedColumn = SelectedColumnNamed(vm, "Code");
                }
            )
            .Commands(typeof(ChangeUniqueConstraintColumnsCommand))
            .Check(
                UndoStage.Operated,
                "行の並び（＝宣言順）で末尾へ積まれる",
                vm =>
                {
                    var kind = SelectedColumnNamed(vm, "Kind");
                    var code = SelectedColumnNamed(vm, "Code");
                    var constraint = vm.SelectedEntity!.UniqueConstraints[0];
                    constraint.ColumnIds.Should().Equal(kind.Id, code.Id);
                    constraint
                        .Members.Select(member => member.SelectedColumn)
                        .Should()
                        .Equal(kind, code);
                }
            )
            .Check(
                UndoStage.Undone,
                "行も作り直される",
                vm =>
                    vm.SelectedEntity!.UniqueConstraints[0]
                        .Members.Select(member => member.SelectedColumn)
                        .Should()
                        .Equal([SelectedColumnNamed(vm, "Kind")], "行も作り直される")
            )
            .Check(
                UndoStage.Redone,
                "行も戻る",
                vm =>
                    vm.SelectedEntity!.UniqueConstraints[0]
                        .Members.Select(member => member.SelectedColumn)
                        .Should()
                        .Equal(SelectedColumnNamed(vm, "Kind"), SelectedColumnNamed(vm, "Code"))
            );

        yield return Row(
                "unique-constraint-member-replace-keeps-position",
                "既存行で列を選び直すとその位置が差し替わり、Undo で戻る"
            )
            .Diagram(ItemDiagram)
            .Prepare("構成列 (Code, Kind) の制約を置く", AddCodeKindConstraint)
            .Operate(
                UndoEntry.Screen,
                "1 行目で Id を選び直す",
                vm =>
                    vm.SelectedEntity!.UniqueConstraints[0].Members[0].SelectedColumn =
                        SelectedColumnNamed(vm, "Id")
            )
            .Commands(typeof(ChangeUniqueConstraintColumnsCommand))
            .Check(
                UndoStage.Operated,
                "位置を保ったまま列だけ差し替わり、外れた Code が他の行の候補へ戻る",
                vm =>
                {
                    var constraint = vm.SelectedEntity!.UniqueConstraints[0];
                    constraint
                        .ColumnIds.Should()
                        .Equal(
                            [SelectedColumnNamed(vm, "Id").Id, SelectedColumnNamed(vm, "Kind").Id],
                            "位置は保ったまま列だけ差し替わる"
                        );
                    constraint
                        .Members[1]
                        .AvailableColumns.Select(column => column.Name)
                        .Should()
                        .Equal(["Code", "Kind"], "解放された Code が他行の候補へ戻る");
                }
            );

        yield return Row(
                "unique-constraint-member-remove-row-is-one-history",
                "行の × は構成列を 1 つ外し、空スロットの × は履歴を汚さず取り消すだけ（行の ×）"
            )
            .Diagram(ItemDiagram)
            .Prepare("構成列 (Code, Kind) の制約を置く", AddCodeKindConstraint)
            .Operate(
                UndoEntry.Screen,
                "RemoveUniqueConstraintMemberCommand（1 行目）",
                vm =>
                    vm.RemoveUniqueConstraintMemberCommand.Execute(
                        vm.SelectedEntity!.UniqueConstraints[0].Members[0]
                    )
            )
            .Commands(typeof(ChangeUniqueConstraintColumnsCommand))
            .Check(
                UndoStage.Operated,
                "Code が外れる",
                vm =>
                    vm.SelectedEntity!.UniqueConstraints[0]
                        .ColumnIds.Should()
                        .Equal(SelectedColumnNamed(vm, "Kind").Id)
            );

        yield return Row(
                "unique-constraint-member-remove-pending-slot-pushes-nothing",
                "行の × は構成列を 1 つ外し、空スロットの × は履歴を汚さず取り消すだけ（空スロットの ×）"
            )
            .Diagram(ItemDiagram)
            .Prepare(
                "構成列 (Code, Kind) の制約を置き、空スロットを出す",
                vm =>
                {
                    AddCodeKindConstraint(vm);
                    vm.AddUniqueConstraintMemberSlotCommand.Execute(
                        vm.SelectedEntity!.UniqueConstraints[0]
                    );
                }
            )
            .Operate(
                UndoEntry.Screen,
                "RemoveUniqueConstraintMemberCommand（空スロット）",
                vm =>
                    vm.RemoveUniqueConstraintMemberCommand.Execute(
                        vm.SelectedEntity!.UniqueConstraints[0].Members[^1]
                    )
            )
            .NoHistory()
            .Check(
                UndoStage.Operated,
                "空スロットだけが消える",
                vm => vm.SelectedEntity!.UniqueConstraints[0].Members.Should().HaveCount(2)
            );

        yield return Row(
                "unique-constraint-remove-restores-position",
                "制約の削除は Undo で元の位置へ戻る"
            )
            .Diagram(ItemDiagram)
            .Prepare(
                "制約を 2 本足す",
                vm =>
                {
                    vm.AddUniqueConstraintCommand.Execute(null);
                    vm.AddUniqueConstraintCommand.Execute(null);
                    Remember(vm, "first", vm.SelectedEntity!.UniqueConstraints[0]);
                    Remember(vm, "second", vm.SelectedEntity.UniqueConstraints[1]);
                }
            )
            .Operate(
                UndoEntry.Screen,
                "RemoveUniqueConstraintCommand（1 本目）",
                vm =>
                    vm.RemoveUniqueConstraintCommand.Execute(
                        Recall<UniqueConstraintViewModel>(vm, "first")
                    )
            )
            .Commands(typeof(RemoveUniqueConstraintCommand))
            .Check(
                UndoStage.Operated,
                "2 本目だけが残る",
                vm =>
                    vm.SelectedEntity!.UniqueConstraints.Should()
                        .Equal(Recall<UniqueConstraintViewModel>(vm, "second"))
            )
            .Check(
                UndoStage.Undone,
                "同じインスタンスが元の位置へ戻る",
                vm =>
                    vm.SelectedEntity!.UniqueConstraints.Should()
                        .Equal(
                            Recall<UniqueConstraintViewModel>(vm, "first"),
                            Recall<UniqueConstraintViewModel>(vm, "second")
                        )
            );

        yield return Row(
                "unique-constraint-remove-column-removes-dependent-constraints",
                "列を削除するとその列を含む制約も同じ Undo 単位で消え、Undo で両方戻る"
            )
            .Diagram(ItemDiagram)
            .Prepare(
                "Code だけ・Code + Kind・Kind だけの 3 本の制約を置く",
                vm =>
                {
                    var entity = vm.SelectedEntity!;
                    var code = SelectedColumnNamed(vm, "Code");
                    var kind = SelectedColumnNamed(vm, "Kind");
                    entity.UniqueConstraints.Add(
                        new UniqueConstraintViewModel(
                            entity,
                            new UniqueConstraint { ColumnIds = { code.Id } }
                        )
                    );
                    entity.UniqueConstraints.Add(
                        new UniqueConstraintViewModel(
                            entity,
                            new UniqueConstraint { ColumnIds = { code.Id, kind.Id } }
                        )
                    );
                    var kindOnly = new UniqueConstraintViewModel(
                        entity,
                        new UniqueConstraint { ColumnIds = { kind.Id } }
                    );
                    entity.UniqueConstraints.Add(kindOnly);
                    Remember(vm, "kindOnly", kindOnly);
                    vm.UndoRedo.Clear();
                }
            )
            .Operate(
                UndoEntry.Screen,
                "RemoveColumnCommand（Code）",
                vm => vm.RemoveColumnCommand.Execute(SelectedColumnNamed(vm, "Code"))
            )
            .Commands(typeof(RemoveColumnCommand))
            .Check(
                UndoStage.Operated,
                "Code を含む 2 本だけが巻き添えで消える",
                vm =>
                {
                    var entity = vm.SelectedEntity!;
                    entity.Columns.Select(column => column.Name).Should().Equal("Id", "Kind");
                    entity
                        .UniqueConstraints.Should()
                        .Equal(
                            [Recall<UniqueConstraintViewModel>(vm, "kindOnly")],
                            "Code を含む 2 件だけが巻き添えで消えるべき"
                        );
                }
            )
            .Check(
                UndoStage.Undone,
                "残っていた制約は同じインスタンスのまま元の位置にいる",
                vm =>
                    vm.SelectedEntity!.UniqueConstraints[2]
                        .Should()
                        .BeSameAs(Recall<UniqueConstraintViewModel>(vm, "kindOnly"))
            );
    }

    /// <summary>選択中のテーブル Item へ構成列 (Code, Kind) の制約を置き、履歴を空にする</summary>
    private static void AddCodeKindConstraint(MainViewModel vm)
    {
        var entity = vm.SelectedEntity!;
        entity.UniqueConstraints.Add(
            new UniqueConstraintViewModel(
                entity,
                new UniqueConstraint
                {
                    ColumnIds =
                    {
                        SelectedColumnNamed(vm, "Code").Id,
                        SelectedColumnNamed(vm, "Kind").Id,
                    },
                }
            )
        );
        vm.UndoRedo.Clear();
    }
}
