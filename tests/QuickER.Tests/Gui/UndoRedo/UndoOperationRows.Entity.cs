using AwesomeAssertions;
using QuickER.Model;
using QuickER.UndoRedo;
using QuickER.ViewModels;

namespace QuickER.Tests.Gui.UndoRedo;

internal static partial class UndoOperationRows
{
    /// <summary>
    /// テーブルの追加・貼り付け・複製・一括削除・一括色変更・移動・自動整列：1 つの操作が履歴 1 件になり、Undo / Redo で戻る
    /// （MainViewModelTests・MainViewModelBulkOperationTests・DuplicateEntityCommandTests・EntityLifecycleUndoTests・CommandTests の図）
    /// </summary>
    private static IEnumerable<UndoRow> EntityRows()
    {
        yield return Row(
                "entity-add-adds-one-and-round-trips",
                "AddEntityCommand 実行でエンティティが 1 件増える／Undo で追加が取り消され、Redo で復活する"
            )
            .Diagram(EmptyDiagram)
            .Operate(UndoEntry.Screen, "AddEntityCommand", vm => vm.AddEntityCommand.Execute(null))
            .Commands(typeof(AddEntityCommand))
            .Check(
                UndoStage.Operated,
                "1 件増え、追加したテーブルが選択される",
                vm =>
                {
                    vm.Entities.Should().HaveCount(1);
                    vm.SelectedEntity.Should().Be(vm.Entities[0]);
                    Remember(vm, "added", vm.Entities[0]);
                }
            )
            .Check(
                UndoStage.Redone,
                "Redo で同じテーブルのインスタンスが戻る",
                vm => vm.Entities.Should().Contain(Recall<EntityViewModel>(vm, "added"))
            );

        yield return Row(
                "entity-add-command-direct-adds-given-table",
                "AddEntityCommand: Undo で取り除かれ、Redo で再追加される"
            )
            .Diagram(EmptyDiagram)
            .Operate(
                UndoEntry.Screen,
                "UndoRedo.Execute(AddEntityCommand(T))（コマンドを直接。画面の AddEntity と同じ呼び出しで、足すテーブルだけが違う）",
                vm =>
                {
                    var entity = PlainEntity("T");
                    Remember(vm, "added", entity);
                    vm.UndoRedo.Execute(new AddEntityCommand(vm, entity));
                }
            )
            .Commands(typeof(AddEntityCommand))
            .Check(
                UndoStage.Operated,
                "渡したテーブルが足される",
                vm => vm.Entities.Should().Contain(Recall<EntityViewModel>(vm, "added"))
            )
            .Check(
                UndoStage.Undone,
                "渡したテーブルが取り除かれる",
                vm => vm.Entities.Should().NotContain(Recall<EntityViewModel>(vm, "added"))
            );

        yield return Row(
                "entity-paste-repeat-shifts-name-and-position",
                "エンティティ貼り付けは連続実行で位置と名前がずれ、Undo/Redo できる"
            )
            .Diagram(EmptyDiagram)
            .Prepare(
                "テーブル Order を (90, 140) に置いて選び、コピーして 1 回貼り付ける",
                vm =>
                {
                    vm.AddEntityCommand.Execute(null);
                    var original = vm.Entities[0];
                    original.TableName = "Order";
                    original.X = 90;
                    original.Y = 140;
                    vm.OnEntityClicked(original);
                    vm.CopySelectedEntityCommand.Execute(null);
                    vm.PasteCopiedEntityCommand.Execute(null);
                }
            )
            .Operate(
                UndoEntry.Screen,
                "PasteCopiedEntityCommand（2 回目）",
                vm => vm.PasteCopiedEntityCommand.Execute(null)
            )
            .Commands(typeof(AddEntityCommand))
            .Check(
                UndoStage.Operated,
                "貼り付けるたびに名前と位置がずれる",
                vm =>
                {
                    TableOrder(vm).Should().Equal("Order", "Order_Copy", "Order_Copy2");
                    vm.Entities[1].X.Should().Be(120);
                    vm.Entities[1].Y.Should().Be(170);
                    vm.Entities[2].X.Should().Be(150);
                    vm.Entities[2].Y.Should().Be(200);
                }
            );

        yield return Row(
                "entity-duplicate-redo-restores-same-instance",
                "Undo / Redo: 複製の追加・削除が往復する"
            )
            .Diagram(DuplicateSourceDiagram)
            .Prepare(
                "テーブル X を選ぶ",
                vm =>
                {
                    vm.SelectedEntity = vm.Entities[0];
                    Remember(vm, "source", vm.Entities[0]);
                }
            )
            .Operate(
                UndoEntry.Screen,
                "DuplicateSelectedEntityCommand",
                vm => vm.DuplicateSelectedEntityCommand.Execute(null)
            )
            .Commands(typeof(DuplicateEntityCommand))
            .Check(
                UndoStage.Operated,
                "複製が足される",
                vm =>
                {
                    vm.Entities.Should().HaveCount(2);
                    var source = Recall<EntityViewModel>(vm, "source");
                    Remember(vm, "duplicate", vm.Entities.Single(entity => entity != source));
                }
            )
            .Check(
                UndoStage.Undone,
                "複製が外れる",
                vm => vm.Entities.Should().NotContain(Recall<EntityViewModel>(vm, "duplicate"))
            )
            .Check(
                UndoStage.Redone,
                "Redo で同じ複製のインスタンスが戻る",
                vm => vm.Entities.Should().Contain(Recall<EntityViewModel>(vm, "duplicate"))
            );

        yield return Row(
                "entity-duplicate-undo-removes-queries-of-copy",
                "テーブルの複製の Undo で、そのテーブルのクエリも外れ、Redo で元の位置へ戻る"
            )
            .Diagram(FirstSecondDiagram)
            .Prepare("First をクリックして選ぶ", vm => vm.OnEntityClicked(vm.Entities[0]))
            .Operate(
                UndoEntry.Screen,
                "DuplicateSelectedEntityCommand のあと、複製のクエリ OnCopy を足す（クエリの追加は履歴に積まない）",
                vm =>
                {
                    vm.DuplicateSelectedEntityCommand.Execute(null);
                    var duplicate = vm.Entities[^1];
                    var query = new QueryDefinition { EntityId = duplicate.Id, Name = "OnCopy" };
                    vm.Queries.Add(query);
                    Remember(vm, "duplicate", duplicate);
                    Remember(vm, "query", query);
                }
            )
            .Commands(typeof(DuplicateEntityCommand))
            .Check(
                UndoStage.Undone,
                "複製と、そのクエリが外れる",
                vm =>
                {
                    vm.Entities.Should().NotContain(Recall<EntityViewModel>(vm, "duplicate"));
                    vm.Queries.Should().NotContain(Recall<QueryDefinition>(vm, "query"));
                }
            )
            .Check(
                UndoStage.Redone,
                "Redo で同じ複製と同じクエリのインスタンスが戻る",
                vm =>
                {
                    vm.Entities.Should().Contain(Recall<EntityViewModel>(vm, "duplicate"));
                    vm.Queries.Should()
                        .ContainSingle()
                        .Which.Should()
                        .BeSameAs(Recall<QueryDefinition>(vm, "query"));
                }
            );

        yield return Row(
                "entity-bulk-delete-single-history",
                "一括削除: 3 個選択→削除でエンティティ・接続線が消え、1 回の Undo で全復元"
            )
            .Diagram(BulkChainDiagram)
            .Prepare(
                "全テーブルを選ぶ",
                vm =>
                {
                    vm.SelectAllEntitiesCommand.Execute(null);
                    vm.SelectedEntities.Should().HaveCount(3);
                }
            )
            .Operate(
                UndoEntry.Screen,
                "RemoveSelectedEntityCommand（3 個選択）",
                vm => vm.RemoveSelectedEntityCommand.Execute(null)
            )
            .Commands(typeof(GroupRemoveEntitiesCommand))
            .Check(
                UndoStage.Operated,
                "全テーブル・接続リレーションが消える",
                vm =>
                {
                    vm.Entities.Should().BeEmpty();
                    vm.Relationships.Should().BeEmpty();
                }
            );

        yield return Row(
                "entity-bulk-delete-via-delete-selected",
                "一括削除: Delete 経路（DeleteSelectedCommand）も選択中全員に作用"
            )
            .Diagram(BulkThreeDiagram)
            .Prepare("全テーブルを選ぶ", vm => vm.SelectAllEntitiesCommand.Execute(null))
            .Operate(
                UndoEntry.Screen,
                "DeleteSelectedCommand（3 個選択）",
                vm => vm.DeleteSelectedCommand.Execute(null)
            )
            .Commands(typeof(GroupRemoveEntitiesCommand))
            .Check(UndoStage.Operated, "全テーブルが消える", vm => vm.Entities.Should().BeEmpty());

        yield return Row(
                "entity-single-selection-delete-removes-only-primary",
                "単一選択: 削除は単一エンティティに作用（複合化しない）"
            )
            .Diagram(BulkTwoDiagram)
            .Prepare(
                "A だけを選ぶ",
                vm =>
                {
                    var a = TableNamed(vm, "A");
                    Remember(vm, "remaining", TableNamed(vm, "B"));
                    vm.SelectedEntity = a;
                    a.IsSelected = true;
                    vm.IsMultiSelectionActive.Should().BeFalse();
                }
            )
            .Operate(
                UndoEntry.Screen,
                "RemoveSelectedEntityCommand（1 個選択）",
                vm => vm.RemoveSelectedEntityCommand.Execute(null)
            )
            .Commands(typeof(RemoveEntityCommand))
            .Check(
                UndoStage.Operated,
                "A だけが消え B が残る",
                vm =>
                    vm.Entities.Should()
                        .ContainSingle()
                        .Which.Should()
                        .Be(Recall<EntityViewModel>(vm, "remaining"))
            );

        yield return Row(
                "entity-bulk-color-change-single-history",
                "一括色変更: 3 個選択→色変更で全員反映、1 回の Undo で全員元の色（履歴 1 エントリ）"
            )
            .Diagram(BulkThreeColoredDiagram)
            .Prepare("全テーブルを選ぶ", vm => vm.SelectAllEntitiesCommand.Execute(null))
            .Operate(
                UndoEntry.Screen,
                "BulkChangeTitleColorCommand(#ABCDEF)",
                vm => vm.BulkChangeTitleColorCommand.Execute("#ABCDEF")
            )
            .Commands(typeof(GroupChangeTitleColorCommand))
            .Check(
                UndoStage.Operated,
                "全員へ反映される",
                vm =>
                    vm.Entities.Select(entity => entity.TitleBackgroundColor)
                        .Should()
                        .Equal("#ABCDEF", "#ABCDEF", "#ABCDEF")
            );

        yield return Row(
                "entity-bulk-color-change-normalizes-notation",
                "一括色変更: 別表記で指定した色は正規化して適用し、Undo / Redo で往復する"
            )
            .Diagram(BulkTwoColoredDiagram)
            .Prepare("全テーブルを選ぶ", vm => vm.SelectAllEntitiesCommand.Execute(null))
            .Operate(
                UndoEntry.Screen,
                "BulkChangeTitleColorCommand(#ffcc00)",
                vm => vm.BulkChangeTitleColorCommand.Execute("#ffcc00")
            )
            .Commands(typeof(GroupChangeTitleColorCommand))
            .Check(
                UndoStage.Operated,
                "正規化した色で適用し、既に同じ色のメンバーはそのまま",
                vm =>
                {
                    TableNamed(vm, "A").TitleBackgroundColor.Should().Be("#FFCC00");
                    TableNamed(vm, "B")
                        .TitleBackgroundColor.Should()
                        .Be("#FFCC00", "既に同じ色のメンバーは変更対象から外れる");
                }
            );

        yield return Row(
                "entity-move-redo-applies-target-position",
                "MoveEntityCommand: Execute / Undo で座標が往復する"
            )
            .Diagram(MovableTableDiagram)
            .Operate(
                UndoEntry.Behavior,
                "(10, 20) から (100, 200) へ動かして MoveEntityCommand を Push（DragBehavior と同じ。Redo が Execute を通る）",
                vm =>
                {
                    var entity = vm.Entities[0];
                    entity.X = 100;
                    entity.Y = 200;
                    vm.UndoRedo.Push(new MoveEntityCommand(entity, 10, 20, 100, 200));
                }
            )
            .Commands(typeof(MoveEntityCommand));

        yield return Row(
                "entity-group-move-single-undo-restores-all",
                "GroupMoveEntitiesCommand: 単一 Undo で全メンバーが元座標へ戻る"
            )
            .Diagram(MovableTablesDiagram)
            .Operate(
                UndoEntry.Behavior,
                "2 テーブルを同じデルタ (+30, +40) で動かして GroupMoveEntitiesCommand を Push（DragBehavior と同じ）",
                vm =>
                {
                    var a = vm.Entities[0];
                    var b = vm.Entities[1];
                    a.X = 40;
                    a.Y = 50;
                    b.X = 80;
                    b.Y = 100;
                    vm.UndoRedo.Push(
                        new GroupMoveEntitiesCommand(
                            new List<(EntityViewModel, double, double, double, double)>
                            {
                                (a, 10, 10, 40, 50),
                                (b, 50, 60, 80, 100),
                            }
                        )
                    );
                }
            )
            .Commands(typeof(GroupMoveEntitiesCommand));

        yield return Row("entity-auto-layout-grid-round-trips", "格子整列は Undo/Redo できる")
            .Diagram(EmptyDiagram)
            .Prepare(
                "テーブルを 2 つ足し、(500, 300) と (120, 700) に置く",
                vm =>
                {
                    vm.AddEntityCommand.Execute(null);
                    vm.AddEntityCommand.Execute(null);
                    vm.Entities[0].X = 500;
                    vm.Entities[0].Y = 300;
                    vm.Entities[1].X = 120;
                    vm.Entities[1].Y = 700;
                }
            )
            .Operate(
                UndoEntry.Screen,
                "AutoLayoutGridCommand",
                vm => vm.AutoLayoutGridCommand.Execute(null)
            )
            .Commands(typeof(ArrangeEntitiesCommand))
            .Check(
                UndoStage.Operated,
                "座標が変わる",
                vm =>
                    vm.Entities.Select(entity => (entity.X, entity.Y))
                        .Should()
                        .NotEqual([(500d, 300d), (120d, 700d)])
            );

        yield return Row("entity-auto-layout-tree-round-trips", "木整列は Undo/Redo できる")
            .Diagram(EmptyDiagram)
            .Prepare(
                "テーブルを 2 つ足してリレーションで結び、(600, 500) と (100, 150) に置く",
                vm =>
                {
                    vm.AddEntityCommand.Execute(null);
                    vm.AddEntityCommand.Execute(null);
                    vm.Entities[1]
                        .Columns.Add(
                            new ColumnViewModel(new Column { Name = "ParentId", DataType = "int" })
                        );
                    CreateOneToMany(vm);
                    vm.Entities[0].X = 600;
                    vm.Entities[0].Y = 500;
                    vm.Entities[1].X = 100;
                    vm.Entities[1].Y = 150;
                }
            )
            .Operate(
                UndoEntry.Screen,
                "AutoLayoutTreeCommand",
                vm => vm.AutoLayoutTreeCommand.Execute(null)
            )
            .Commands(typeof(ArrangeEntitiesCommand))
            .Check(
                UndoStage.Operated,
                "座標が変わる",
                vm =>
                    vm.Entities.Select(entity => (entity.X, entity.Y))
                        .Should()
                        .NotEqual([(600d, 500d), (100d, 150d)])
            );

        foreach (var row in CopyConstraintNameRows())
        {
            yield return row;
        }
    }

    /// <summary>
    /// 表の複製・貼り付けと制約名：写した一意制約の名前が図の制約名とぶつかるときは名前を外し（合成名は DDL と同じ解決で決まる）、
    /// 外部キーは写さない（図は <see cref="NamedConstraintsDiagram"/>。DDL の制約名がすべて違うことは共通の表明が見る）
    /// </summary>
    private static IEnumerable<UndoRow> CopyConstraintNameRows()
    {
        yield return Row(
                "entity-duplicate-twice-drops-colliding-unique-constraint-name",
                "同じ表を 2 回複製すると、どちらの複製の一意制約も名前を外し、合成名がそれぞれの表名で決まる"
            )
            .Diagram(() => NamedConstraintsDiagram())
            .Operate(
                UndoEntry.Screen,
                "Order を選んで DuplicateSelectedEntityCommand を 2 回（2 回目の前にも Order を選び直す）",
                vm =>
                {
                    var order = TableNamed(vm, "Order");
                    vm.SelectedEntity = order;
                    vm.DuplicateSelectedEntityCommand.Execute(null);
                    vm.SelectedEntity = order;
                    vm.DuplicateSelectedEntityCommand.Execute(null);
                }
            )
            .Commands(typeof(DuplicateEntityCommand), typeof(DuplicateEntityCommand))
            .Check(UndoStage.Operated, "複製の一意制約は名前を外す", CopiesDropName)
            .Check(UndoStage.Redone, "Redo の後も複製の一意制約は名前を持たない", CopiesDropName);

        yield return Row(
                "entity-paste-twice-drops-colliding-unique-constraint-name",
                "同じ表を 2 回貼り付けると、どちらの貼り付けの一意制約も名前を外し、合成名がそれぞれの表名で決まる"
            )
            .Diagram(() => NamedConstraintsDiagram())
            .Prepare("Order をコピーする", vm => vm.CopySelectedEntityCommand.Execute(null))
            .Operate(
                UndoEntry.Screen,
                "PasteCopiedEntityCommand を 2 回",
                vm =>
                {
                    vm.PasteCopiedEntityCommand.Execute(null);
                    vm.PasteCopiedEntityCommand.Execute(null);
                }
            )
            .Commands(typeof(AddEntityCommand), typeof(AddEntityCommand))
            .Check(UndoStage.Operated, "貼り付けた一意制約は名前を外す", CopiesDropName)
            .Check(
                UndoStage.Redone,
                "Redo の後も貼り付けた一意制約は名前を持たない",
                CopiesDropName
            );

        yield return Row(
                "entity-duplicate-synthesized-name-taken-gets-number",
                "複製の合成名がほかの表の明示の名前とぶつかると、番号つきの名前になる"
            )
            .Diagram(() => NamedConstraintsDiagram(copyNameTaken: true))
            .Operate(
                UndoEntry.Screen,
                "DuplicateSelectedEntityCommand（Order を選んだ状態）",
                vm => vm.DuplicateSelectedEntityCommand.Execute(null)
            )
            .Commands(typeof(DuplicateEntityCommand))
            .Check(
                UndoStage.Operated,
                "複製の一意制約は名前を持たず、解決された名前は UQ_Order_Copy_Code_2",
                vm =>
                {
                    var constraint = TableNamed(vm, "Order_Copy").UniqueConstraints.Single();
                    constraint.Name.Should().BeEmpty();
                    constraint.ResolvedName.Should().Be("UQ_Order_Copy_Code_2");
                    vm.Relationships.Should().ContainSingle();
                }
            );

        yield return Row(
                "entity-duplicate-long-table-synthesized-name-fits-limit",
                "表名が長く合成名が SQL Server の上限（128）を超える表を複製しても、解決された名前は上限に収まる"
            )
            .Diagram(() => NamedConstraintsDiagram(LongOrderTableName))
            .Operate(
                UndoEntry.Screen,
                "DuplicateSelectedEntityCommand（長い名前の表を選んだ状態）",
                vm => vm.DuplicateSelectedEntityCommand.Execute(null)
            )
            .Commands(typeof(DuplicateEntityCommand))
            .Check(
                UndoStage.Operated,
                "複製の一意制約は名前を持たず、解決された名前は 128 文字以内で元の名前と違う",
                vm =>
                {
                    var constraint = TableNamed(vm, LongOrderTableName + "_Copy")
                        .UniqueConstraints.Single();
                    constraint.Name.Should().BeEmpty();
                    constraint.ResolvedName.Length.Should().BeLessThanOrEqualTo(128);
                    constraint.ResolvedName.Should().NotBe("UQ_Order_Code");
                    vm.Relationships.Should().ContainSingle();
                }
            );
    }

    /// <summary>
    /// 元の Order は名前 UQ_Order_Code を保ち、複製（Order_Copy・Order_Copy2）の一意制約は名前を持たず、解決された名前は
    /// それぞれの表名の合成名。外部キーは元の 1 本だけ
    /// </summary>
    private static void CopiesDropName(MainViewModel vm)
    {
        TableNamed(vm, "Order").UniqueConstraints.Single().Name.Should().Be("UQ_Order_Code");

        foreach (var copy in new[] { "Order_Copy", "Order_Copy2" })
        {
            var constraint = TableNamed(vm, copy).UniqueConstraints.Single();
            constraint.Name.Should().BeEmpty(copy + " の一意制約は名前を外す");
            constraint.ResolvedName.Should().Be($"UQ_{copy}_Code");
        }

        vm.Relationships.Should()
            .ContainSingle()
            .Which.Target.Should()
            .BeSameAs(TableNamed(vm, "Order"), "外部キーは複製しない");
    }
}
