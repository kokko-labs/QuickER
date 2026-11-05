using AwesomeAssertions;
using QuickER.Model;
using QuickER.UndoRedo;
using QuickER.ViewModels;

namespace QuickER.Tests.Gui.UndoRedo;

internal static partial class UndoOperationRows
{
    /// <summary>
    /// 列の追加・貼り付け・削除・並べ替え・主キーの切り替え：1 つの操作が履歴 1 件になり、Undo / Redo で戻る
    /// （MainViewModelTests・RelationshipViewModelTests・CommandTests の図）
    /// </summary>
    private static IEnumerable<UndoRow> ColumnRows()
    {
        yield return Row("column-add-selects-added-column", "カラム追加は Undo/Redo できる")
            .Diagram(EmptyDiagram)
            .Prepare(
                "テーブルを足して選ぶ（列は 1 本）",
                vm =>
                {
                    vm.AddEntityCommand.Execute(null);
                    vm.SelectedEntity = vm.Entities[0];
                }
            )
            .Operate(UndoEntry.Screen, "AddColumnCommand", vm => vm.AddColumnCommand.Execute(null))
            .Commands(typeof(AddColumnCommand))
            .Check(
                UndoStage.Operated,
                "列が 1 本増え、追加した末尾の列が選択される",
                vm =>
                {
                    vm.SelectedEntity!.Columns.Should().HaveCount(2);
                    vm.SelectedColumn.Should().Be(vm.SelectedEntity.Columns.Last());
                }
            );

        yield return Row(
                "column-add-command-direct-appends-given-column",
                "AddColumnCommand: Undo で取り除かれ、Redo で再追加される"
            )
            .Diagram(() => SingleTableDiagram())
            .Operate(
                UndoEntry.Screen,
                "UndoRedo.Execute(AddColumnCommand(列のないテーブル, Code))（コマンドを直接。画面の AddColumn と同じ呼び出しで、足す列だけが違う）",
                vm =>
                {
                    var column = new ColumnViewModel(
                        new Column { Name = "Code", DataType = "int" }
                    );
                    Remember(vm, "added", column);
                    vm.UndoRedo.Execute(new AddColumnCommand(vm.Entities[0].Columns, column));
                }
            )
            .Commands(typeof(AddColumnCommand))
            .Check(
                UndoStage.Operated,
                "渡した列が足される",
                vm => vm.Entities[0].Columns.Should().Contain(Recall<ColumnViewModel>(vm, "added"))
            )
            .Check(
                UndoStage.Undone,
                "渡した列が取り除かれる",
                vm =>
                    vm.Entities[0].Columns.Should().NotContain(Recall<ColumnViewModel>(vm, "added"))
            );

        yield return Row(
                "column-paste-inserts-below-selection",
                "カラム貼り付けは Undo/Redo できる"
            )
            .Diagram(EmptyDiagram)
            .Prepare(
                "テーブルを足して列 Code を加え、Code を選んでコピーする",
                vm =>
                {
                    vm.AddEntityCommand.Execute(null);
                    vm.SelectedEntity = vm.Entities[0];
                    vm.SelectedEntity.Columns.Add(
                        new ColumnViewModel(new Column { Name = "Code", DataType = "int" })
                    );
                    vm.SelectedColumn = vm.SelectedEntity.Columns[1];
                    vm.CopySelectedColumnCommand.Execute(null);
                }
            )
            .Operate(
                UndoEntry.Screen,
                "PasteCopiedColumnCommand",
                vm => vm.PasteCopiedColumnCommand.Execute(null)
            )
            .Commands(typeof(AddColumnCommand))
            .Check(
                UndoStage.Operated,
                "選択した列の直下へ貼り付く",
                vm =>
                    vm.SelectedEntity!.Columns.Select(column => column.Name)
                        .Should()
                        .Equal("ID", "Code", "Code")
            );

        yield return Row("column-remove-first-round-trips", "カラム削除は Undo/Redo できる")
            .Diagram(EmptyDiagram)
            .Prepare(
                "テーブルを足して列 B を加える",
                vm =>
                {
                    vm.AddEntityCommand.Execute(null);
                    vm.SelectedEntity = vm.Entities[0];
                    vm.SelectedEntity.Columns.Add(
                        new ColumnViewModel(new Column { Name = "B", DataType = "int" })
                    );
                }
            )
            .Operate(
                UndoEntry.Screen,
                "RemoveColumnCommand（先頭の列 ID）",
                vm => vm.RemoveColumnCommand.Execute(vm.SelectedEntity!.Columns[0])
            )
            .Commands(typeof(RemoveColumnCommand))
            .Check(
                UndoStage.Operated,
                "先頭の列が消える",
                vm => vm.SelectedEntity!.Columns.Select(column => column.Name).Should().Equal("B")
            );

        yield return Row(
                "column-remove-middle-restores-position",
                "RemoveColumnCommand: Undo で元の位置に復元される"
            )
            .Diagram(ThreeColumnsDiagram)
            .Prepare("テーブルを選ぶ", vm => vm.SelectedEntity = vm.Entities[0])
            .Operate(
                UndoEntry.Screen,
                "RemoveColumnCommand（中央の列 B）",
                vm => vm.RemoveColumnCommand.Execute(SelectedColumnNamed(vm, "B"))
            )
            .Commands(typeof(RemoveColumnCommand))
            .Check(
                UndoStage.Operated,
                "中央の列が消える",
                vm =>
                    vm.SelectedEntity!.Columns.Select(column => column.Name)
                        .Should()
                        .Equal("A", "C")
            );

        yield return Row(
                "column-remove-only-column",
                "MainViewModel.RemoveColumn で指定カラムが削除される"
            )
            .Diagram(EmptyDiagram)
            .Prepare(
                "テーブルを足し、その唯一の列を覚える",
                vm =>
                {
                    vm.AddEntityCommand.Execute(null);
                    Remember(vm, "column", vm.Entities[0].Columns[0]);
                }
            )
            .Operate(
                UndoEntry.Screen,
                "RemoveColumnCommand（唯一の列）",
                vm => vm.RemoveColumnCommand.Execute(Recall<ColumnViewModel>(vm, "column"))
            )
            .Commands(typeof(RemoveColumnCommand))
            .Check(
                UndoStage.Operated,
                "列が消える",
                vm =>
                    vm.Entities[0]
                        .Columns.Should()
                        .NotContain(Recall<ColumnViewModel>(vm, "column"))
            );

        yield return Row(
                "column-remove-foreign-key-child-restores-pair",
                "FK に設定済みのカラムを削除して Undo するとリレーションの FK 設定も復元される"
            )
            .Diagram(EmptyDiagram)
            .Prepare(
                "テーブルを 2 つ足し、子に NewTableId を加えてリレーションを作り、子を選ぶ",
                vm =>
                {
                    vm.AddEntityCommand.Execute(null);
                    vm.AddEntityCommand.Execute(null);
                    vm.Entities[1]
                        .Columns.Add(
                            new ColumnViewModel(
                                new Column { Name = "NewTableId", DataType = "int" }
                            )
                        );
                    CreateOneToMany(vm);
                    var childColumnId = vm.Relationships[0]
                        .ColumnPairs.Should()
                        .ContainSingle()
                        .Subject.TargetColumnId;
                    Remember(vm, "childColumnId", childColumnId);
                    vm.SelectedEntity = vm.Entities[1];
                }
            )
            .Operate(
                UndoEntry.Screen,
                "RemoveColumnCommand（外部キーの子列）",
                vm =>
                    vm.RemoveColumnCommand.Execute(
                        vm.Entities[1]
                            .Columns.First(column => column.Id == Recall<Guid>(vm, "childColumnId"))
                    )
            )
            .Commands(typeof(RemoveColumnCommand))
            .Check(
                UndoStage.Operated,
                "列ペアが外れる",
                vm => vm.Relationships[0].ColumnPairs.Should().BeEmpty()
            );

        yield return Row("column-order-change-round-trips", "カラム順変更は Undo/Redo できる")
            .Diagram(EmptyDiagram)
            .Prepare(
                "テーブルを足し、列を A・B・C にする",
                vm =>
                {
                    vm.AddEntityCommand.Execute(null);
                    var entity = vm.Entities[0];
                    entity.Columns[0].Name = "A";
                    entity.Columns.Add(
                        new ColumnViewModel(new Column { Name = "B", DataType = "int" })
                    );
                    entity.Columns.Add(
                        new ColumnViewModel(new Column { Name = "C", DataType = "int" })
                    );
                }
            )
            .Operate(
                UndoEntry.Behavior,
                "UndoRedo.Execute(MoveColumnOrderCommand(A → 2))（DataGridRowReorderBehavior と同じ呼び出し）",
                vm =>
                {
                    var columns = vm.Entities[0].Columns;
                    vm.UndoRedo.Execute(new MoveColumnOrderCommand(columns, columns[0], 2));
                }
            )
            .Commands(typeof(MoveColumnOrderCommand))
            .Check(
                UndoStage.Operated,
                "先頭の列が末尾へ移る",
                vm =>
                    vm.Entities[0]
                        .Columns.Select(column => column.Name)
                        .Should()
                        .Equal("B", "C", "A")
            );

        yield return Row(
                "column-primary-key-toggle-folds-nullable-into-one-history",
                "PK チェック変更に伴う NULL 変更も 1 回の Undo/Redo で往復する"
            )
            .Diagram(EmptyDiagram)
            .Prepare(
                "テーブルを足して選び、NULL 許容の列 Code を加える",
                vm =>
                {
                    vm.AddEntityCommand.Execute(null);
                    vm.SelectedEntity = vm.Entities[0];
                    vm.SelectedEntity.Columns.Add(
                        new ColumnViewModel(
                            new Column
                            {
                                Name = "Code",
                                DataType = "int",
                                IsNullable = true,
                            }
                        )
                    );
                }
            )
            .Operate(
                UndoEntry.Tracker,
                "Code.IsPrimaryKey = true",
                vm => vm.SelectedEntity!.Columns[1].IsPrimaryKey = true
            )
            .Commands(typeof(SnapshotChangeCommand))
            .Check(
                UndoStage.Operated,
                "主キーにすると NULL 不可になる",
                vm =>
                {
                    vm.SelectedEntity!.Columns[1].IsPrimaryKey.Should().BeTrue();
                    vm.SelectedEntity.Columns[1].IsNullable.Should().BeFalse();
                }
            );
    }
}
