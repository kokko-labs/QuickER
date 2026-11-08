using AwesomeAssertions;
using QuickER.Documents;
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

        // 列が外れても名前付きクエリは書き換えず、外れた列を参照するクエリをステータスバーで名指しする
        // （削除・列の追加の Undo のどちらの経路でも。参照は書き換えないので、列が戻れば参照も生き返る）

        yield return Row(
                "column-remove-referenced-by-query-warns-and-undo-revives-references",
                "列を削除すると、その列を参照するクエリをステータスバーで名指しし、クエリは書き換えず、Undo で参照が生き返る"
            )
            .Diagram(QueryReferencesDiagram)
            .Prepare("Order を選ぶ", vm => vm.SelectedEntity = TableNamed(vm, "Order"))
            .Operate(
                UndoEntry.Screen,
                "RemoveColumnCommand（Order.CustomerId）",
                vm => vm.RemoveColumnCommand.Execute(SelectedColumnNamed(vm, "CustomerId"))
            )
            .Commands(typeof(RemoveColumnCommand))
            .Check(
                UndoStage.Operated,
                "ステータスが参照するクエリ 4 本と列を名指しし、参照しないクエリは名指ししない",
                vm =>
                {
                    vm.StatusMessage.Should()
                        .ContainAll(
                            "CustomerId",
                            "ByCustomerParameter",
                            "ByCustomerSort",
                            "CustomerProjection",
                            "ByCustomerCondition"
                        );
                    vm.StatusMessage.Should().NotContainAny("ByOrderId", "CustomerById");
                }
            )
            .Check(
                UndoStage.Undone,
                "クエリの列の参照がすべて Order の列へ解決する",
                vm =>
                {
                    var columnIds = TableNamed(vm, "Order")
                        .Columns.Select(column => column.Id)
                        .ToHashSet();
                    vm.Queries.SelectMany(QueryColumnIds)
                        .Should()
                        .OnlyContain(id => columnIds.Contains(id));
                }
            );

        yield return Row(
                "column-add-undo-referenced-by-query-warns",
                "列の追加を Undo して列が外れると、その列を参照するクエリをステータスバーで名指しする"
            )
            .Diagram(MemoQueryDiagram)
            .Operate(
                UndoEntry.Screen,
                "UndoRedo.Execute(AddColumnCommand(Order, Id を決めた列 Memo))（クエリが先に Memo を参照している）",
                vm =>
                    vm.UndoRedo.Execute(
                        new AddColumnCommand(
                            TableNamed(vm, "Order").Columns,
                            new ColumnViewModel(
                                new Column
                                {
                                    Id = MemoColumnId,
                                    Name = "Memo",
                                    DataType = "nvarchar(50)",
                                    IsNullable = true,
                                }
                            )
                        )
                    )
            )
            .Commands(typeof(AddColumnCommand))
            .Check(
                UndoStage.Operated,
                "列を足しただけではクエリを名指ししない",
                vm => vm.StatusMessage.Should().NotContainAny("ByMemoParameter", "ByMemoCondition")
            )
            .Check(
                UndoStage.Undone,
                "Undo で列が外れると、参照するクエリ 2 本を名指しする",
                vm =>
                    vm.StatusMessage.Should()
                        .ContainAll("Memo", "ByMemoParameter", "ByMemoCondition")
            );

        yield return Row(
                "column-remove-not-referenced-does-not-warn",
                "参照の切れたクエリを持つ図へ一括置換しても、どのクエリも参照しない列を削除しても、ステータスを変えない"
            )
            .Diagram(QueryReferencesDiagram)
            .Prepare(
                "ステータスを控え、Order.CustomerId の無い（4 本の参照が切れた）図に前から切れているクエリ Stale を足して一括置換し、Order を選ぶ",
                vm =>
                {
                    Remember(vm, "status", vm.StatusMessage);
                    var model = vm.ToDiagramModel();
                    var order = model.Entities.Single(entity => entity.TableName == "Order");
                    order.Columns.RemoveAll(column => column.Name == "CustomerId");
                    model.Queries.Add(
                        new QueryDefinition
                        {
                            EntityId = order.Id,
                            Name = "Stale",
                            Condition = "Missing = 1",
                            OrderBy = { new QueryOrdering { ColumnId = Guid.NewGuid() } },
                        }
                    );
                    vm.ReplaceDiagramFromModule(model);
                    vm.SelectedEntity = TableNamed(vm, "Order");
                }
            )
            .Operate(
                UndoEntry.Screen,
                "RemoveColumnCommand（Order.Note）",
                vm => vm.RemoveColumnCommand.Execute(SelectedColumnNamed(vm, "Note"))
            )
            .Commands(typeof(RemoveColumnCommand))
            .Check(
                UndoStage.Operated,
                "ステータスは一括置換の前のまま",
                vm => vm.StatusMessage.Should().Be(Recall<string>(vm, "status"))
            );

        yield return Row(
                "column-query-references-table-removal-does-not-warn",
                "列を参照するクエリを持つテーブルを削除しても（Undo / Redo も）、ステータスを変えない"
            )
            .Diagram(QueryReferencesDiagram)
            .Prepare(
                "Order を選び、ステータスを控える",
                vm =>
                {
                    vm.SelectedEntity = TableNamed(vm, "Order");
                    Remember(vm, "status", vm.StatusMessage);
                }
            )
            .Operate(
                UndoEntry.Screen,
                "RemoveSelectedEntityCommand（Order。クエリはテーブルと一緒に取り除かれる）",
                vm => vm.RemoveSelectedEntityCommand.Execute(null)
            )
            .Commands(typeof(RemoveEntityCommand))
            .Check(
                UndoStage.Operated,
                "ステータスは操作の前のまま",
                vm => vm.StatusMessage.Should().Be(Recall<string>(vm, "status"))
            )
            .Check(
                UndoStage.Undone,
                "ステータスは操作の前のまま",
                vm => vm.StatusMessage.Should().Be(Recall<string>(vm, "status"))
            )
            .Check(
                UndoStage.Redone,
                "ステータスは操作の前のまま",
                vm => vm.StatusMessage.Should().Be(Recall<string>(vm, "status"))
            );
    }

    /// <summary>
    /// Order(OrderId PK, CustomerId, Note) と Customer(CustomerId PK) に、Order.CustomerId を 4 種
    /// （パラメータ・並び順・射影フィールド・条件）で参照するクエリと、参照しないクエリ 2 本（Order の OrderId だけ・
    /// Customer の同じ名前の列）を置いた図
    /// </summary>
    private static MainViewModel QueryReferencesDiagram()
    {
        var vm = new MainViewModel();
        var orderId = new Column
        {
            Name = "OrderId",
            DataType = "int",
            IsPrimaryKey = true,
        };
        var customerId = new Column { Name = "CustomerId", DataType = "int" };
        var note = new Column
        {
            Name = "Note",
            DataType = "nvarchar(50)",
            IsNullable = true,
        };
        var order = new EntityViewModel(
            new Entity { TableName = "Order", Columns = { orderId, customerId, note } },
            new EntityLayout()
        );
        var customer = new EntityViewModel(
            new Entity
            {
                TableName = "Customer",
                Columns =
                {
                    new Column
                    {
                        Name = "CustomerId",
                        DataType = "int",
                        IsPrimaryKey = true,
                    },
                },
            },
            new EntityLayout()
        );
        vm.Entities.Add(order);
        vm.Entities.Add(customer);
        vm.Queries.Add(
            new QueryDefinition
            {
                EntityId = order.Id,
                Name = "ByCustomerParameter",
                Parameters =
                {
                    new QueryParameter { Name = "customerId", SourceColumnId = customerId.Id },
                },
            }
        );
        vm.Queries.Add(
            new QueryDefinition
            {
                EntityId = order.Id,
                Name = "ByCustomerSort",
                OrderBy = { new QueryOrdering { ColumnId = customerId.Id } },
            }
        );
        vm.Queries.Add(
            new QueryDefinition
            {
                EntityId = order.Id,
                Name = "CustomerProjection",
                Returns = QueryReturnShape.Projection,
                ResultTypeName = "CustomerRow",
                Fields =
                {
                    new ProjectionField { Name = "Customer", SourceColumnId = customerId.Id },
                },
            }
        );
        vm.Queries.Add(
            new QueryDefinition
            {
                EntityId = order.Id,
                Name = "ByCustomerCondition",
                Condition = "CustomerId = 1",
            }
        );
        vm.Queries.Add(
            new QueryDefinition
            {
                EntityId = order.Id,
                Name = "ByOrderId",
                Condition = "OrderId = 1",
            }
        );
        vm.Queries.Add(
            new QueryDefinition
            {
                EntityId = customer.Id,
                Name = "CustomerById",
                Condition = "CustomerId = 1",
            }
        );

        return vm;
    }

    /// <summary><see cref="MemoQueryDiagram"/> のクエリが参照する、まだ図に無い列 Memo の Id</summary>
    private static readonly Guid MemoColumnId = new("6d1f3c2a-8b4e-4f7a-9c11-2e5d7a9b0c31");

    /// <summary>
    /// Order(OrderId PK) に、まだ図に無い列 Memo（<see cref="MemoColumnId"/>）をパラメータと条件で参照するクエリ 2 本を置いた図
    /// （列を後から足し、その追加を Undo する経路を作る）
    /// </summary>
    private static MainViewModel MemoQueryDiagram()
    {
        var vm = new MainViewModel();
        var order = new EntityViewModel(
            new Entity
            {
                TableName = "Order",
                Columns =
                {
                    new Column
                    {
                        Name = "OrderId",
                        DataType = "int",
                        IsPrimaryKey = true,
                    },
                },
            },
            new EntityLayout()
        );
        vm.Entities.Add(order);
        vm.Queries.Add(
            new QueryDefinition
            {
                EntityId = order.Id,
                Name = "ByMemoParameter",
                Parameters =
                {
                    new QueryParameter { Name = "memo", SourceColumnId = MemoColumnId },
                },
            }
        );
        vm.Queries.Add(
            new QueryDefinition
            {
                EntityId = order.Id,
                Name = "ByMemoCondition",
                Condition = "Memo = 'x'",
            }
        );

        return vm;
    }

    /// <summary>クエリが Id で参照する列（パラメータ・並び順・射影フィールド）</summary>
    private static IEnumerable<Guid> QueryColumnIds(QueryDefinition query) =>
        query
            .Parameters.Select(parameter => parameter.SourceColumnId)
            .Concat(query.Fields.Select(field => field.SourceColumnId))
            .OfType<Guid>()
            .Concat(query.OrderBy.Select(ordering => ordering.ColumnId));
}
