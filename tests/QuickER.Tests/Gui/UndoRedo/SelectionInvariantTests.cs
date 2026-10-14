using AwesomeAssertions;
using QuickER.Model;
using QuickER.ViewModels;

namespace QuickER.Tests.Gui.UndoRedo;

/// <summary>
/// 選択・検索結果・作成中のリレーションの始点が、図に居る要素（<see cref="MainViewModel.Entities"/> など）だけを指すことを、
/// 図を変える操作と Undo / Redo の全経路で表明するテストクラス。
/// </summary>
/// <remarks>
/// <para>
/// 図から外れた ViewModel を選択が指したままだと、プロパティパネルが追跡の外れた要素を編集し（履歴にも未保存にもならない）、
/// Redo で同じインスタンスが戻ると記録のない編集ごと現れる。削除を実行すると、Id が同じ生きている表の
/// 名前付きクエリまで消える（再読込後に古い検索結果から選んだ場合）。ビルドも型検査も通るので、不変条件として固定する。
/// </para>
/// <para>
/// 不変条件: <c>SelectedEntity</c> は <c>Entities</c> の要素か null・<c>SelectedRelationship</c> は <c>Relationships</c> の要素か null・
/// <c>SelectedColumn</c> は <c>SelectedEntity</c> の列か null・<c>SearchResults</c> の各エンティティは <c>Entities</c> の要素・
/// 作成中の始点は <c>Entities</c> の要素か null。
/// </para>
/// </remarks>
public class SelectionInvariantTests
{
    /// <summary>顧客・注文の 2 テーブルとリレーション 1 本、注文のクエリ 1 本を持つ図を読み込む</summary>
    private static MainViewModel Build()
    {
        var customerId = new Column
        {
            Name = "Id",
            DataType = "int",
            IsPrimaryKey = true,
            IsNullable = false,
        };
        var customer = new Entity { TableName = "Customers" };
        customer.Columns.Add(customerId);

        var orderId = new Column
        {
            Name = "Id",
            DataType = "int",
            IsPrimaryKey = true,
            IsNullable = false,
        };
        var orderCustomerId = new Column
        {
            Name = "CustomerId",
            DataType = "int",
            IsNullable = true,
        };
        var order = new Entity { TableName = "Orders" };
        order.Columns.Add(orderId);
        order.Columns.Add(orderCustomerId);

        var relationship = new Relationship
        {
            SourceEntityId = customer.Id,
            TargetEntityId = order.Id,
            Type = RelationshipType.OneToMany,
            ConstraintName = "FK_Orders_Customers",
            ColumnPairs =
            {
                new RelationshipColumnPair
                {
                    SourceColumnId = customerId.Id,
                    TargetColumnId = orderCustomerId.Id,
                },
            },
        };

        var vm = new MainViewModel();
        vm.ReplaceDiagramFromModule(
            new ErDiagram
            {
                Entities = { customer, order },
                Relationships = { relationship },
                Queries =
                {
                    new QueryDefinition
                    {
                        EntityId = order.Id,
                        Name = "FindByCustomer",
                        Returns = QueryReturnShape.List,
                        Condition = "CustomerId = @customerId",
                        Parameters =
                        {
                            new QueryParameter { Name = "customerId", Type = "int" },
                        },
                    },
                },
            }
        );

        return vm;
    }

    /// <summary>不変条件を表明する。<paramref name="step"/> は失敗時にどの操作の直後かを示す</summary>
    private static void AssertInvariants(MainViewModel vm, string step)
    {
        if (vm.SelectedEntity is not null)
        {
            vm.Entities.Should()
                .Contain(vm.SelectedEntity, "{0}: 選択中のエンティティは図に居る", step);
        }

        if (vm.SelectedRelationship is not null)
        {
            vm.Relationships.Should()
                .Contain(vm.SelectedRelationship, "{0}: 選択中のリレーションは図に居る", step);
        }

        if (vm.SelectedColumn is not null)
        {
            (vm.SelectedEntity?.Columns)
                .Should()
                .NotBeNull(step + ": 列を選択しているならエンティティも選択している");
            vm.SelectedEntity!.Columns.Should()
                .Contain(vm.SelectedColumn, "{0}: 選択中の列は選択中のエンティティの列", step);
        }

        foreach (var result in vm.SearchResults)
        {
            vm.Entities.Should()
                .Contain(result.Entity, "{0}: 検索結果は図に居るエンティティだけ", step);
        }

        if (vm.PendingRelationshipSource is not null)
        {
            vm.Entities.Should()
                .Contain(vm.PendingRelationshipSource, "{0}: 作成中の始点は図に居る", step);
        }
    }

    /// <summary>操作→Undo→Redo→Undo の各段で不変条件を表明する</summary>
    private static void AssertRoundTrip(MainViewModel vm, string name, Action operation)
    {
        operation();
        AssertInvariants(vm, name + "（操作の直後）");

        vm.UndoCommand.Execute(null);
        AssertInvariants(vm, name + "（Undo の直後）");

        vm.RedoCommand.Execute(null);
        AssertInvariants(vm, name + "（Redo の直後）");

        vm.UndoCommand.Execute(null);
        AssertInvariants(vm, name + "（2 回目の Undo の直後）");
    }

    [Fact(DisplayName = "テーブルの追加を Undo すると、選択が外れた追加分を指したままにならない")]
    public void AddEntity_Undo_ClearsSelection() =>
        UndoKnownFailures.Run(
            nameof(AddEntity_Undo_ClearsSelection),
            () =>
            {
                var vm = Build();

                vm.AddEntityCommand.Execute(null);
                var added = vm.SelectedEntity;
                added.Should().NotBeNull("追加したテーブルは選択される");

                vm.UndoCommand.Execute(null);

                vm.Entities.Should().NotContain(added!);
                vm.SelectedEntity.Should().BeNull("Undo で消えたテーブルを選択したままにしない");
                AssertInvariants(vm, "追加の Undo");

                vm.RedoCommand.Execute(null);

                vm.Entities.Should().Contain(added!);
                AssertInvariants(vm, "追加の Redo");
            }
        );

    [Fact(DisplayName = "貼り付け・複製を Undo すると、選択が外れたテーブルを指したままにならない")]
    public void PasteAndDuplicate_Undo_ClearSelection() =>
        UndoKnownFailures.Run(
            nameof(PasteAndDuplicate_Undo_ClearSelection),
            () =>
            {
                var vm = Build();
                vm.SelectedEntity = vm.Entities[1];

                AssertRoundTrip(vm, "複製", () => vm.DuplicateSelectedEntityCommand.Execute(null));

                vm.SelectedEntity = vm.Entities[1];
                vm.CopySelectedEntityCommand.Execute(null);

                AssertRoundTrip(vm, "貼り付け", () => vm.PasteCopiedEntityCommand.Execute(null));

                vm.UndoCommand.Execute(null);
                vm.SelectedEntity.Should().BeNull("貼り付けを Undo したあとの選択は解除");
            }
        );

    [Fact(DisplayName = "列の追加を Undo すると、選択が外れた列を指したままにならない")]
    public void AddColumn_Undo_ClearsColumnSelection() =>
        UndoKnownFailures.Run(
            nameof(AddColumn_Undo_ClearsColumnSelection),
            () =>
            {
                var vm = Build();
                vm.SelectedEntity = vm.Entities[0];

                vm.AddColumnCommand.Execute(null);
                vm.SelectedColumn.Should().NotBeNull("追加した列は選択される");

                vm.UndoCommand.Execute(null);

                vm.SelectedColumn.Should().BeNull("Undo で消えた列を選択したままにしない");
                AssertInvariants(vm, "列追加の Undo");
            }
        );

    [Fact(
        DisplayName = "リレーションの追加を Undo すると、選択が外れたリレーションを指したままにならない"
    )]
    public void AddRelationship_Undo_ClearsRelationshipSelection() =>
        UndoKnownFailures.Run(
            nameof(AddRelationship_Undo_ClearsRelationshipSelection),
            () =>
            {
                var vm = Build();

                vm.StartAddOneToManyCommand.Execute(null);
                vm.OnEntityClicked(vm.Entities[1]);
                vm.OnEntityClicked(vm.Entities[0]);
                vm.Relationships.Should().HaveCount(2, "リレーションが 1 本増える");

                // 追加したリレーションを選択してから Undo する
                var added = vm.Relationships[^1];
                vm.SelectedRelationship = added;

                vm.UndoCommand.Execute(null);

                vm.Relationships.Should().NotContain(added);
                vm.SelectedRelationship.Should()
                    .BeNull("Undo で消えたリレーションを選択したままにしない");
                AssertInvariants(vm, "リレーション追加の Undo");

                vm.RedoCommand.Execute(null);
                AssertInvariants(vm, "リレーション追加の Redo");
            }
        );

    [Fact(DisplayName = "削除系の操作と Undo / Redo の往復でも、選択は図に居る要素だけを指す")]
    public void RemoveOperations_RoundTrip_KeepInvariants() =>
        UndoKnownFailures.Run(
            nameof(RemoveOperations_RoundTrip_KeepInvariants),
            () =>
            {
                var vm = Build();

                vm.SelectedEntity = vm.Entities[1];
                AssertRoundTrip(
                    vm,
                    "テーブルの削除",
                    () => vm.RemoveSelectedEntityCommand.Execute(null)
                );

                vm.SelectedRelationship = vm.Relationships[0];
                AssertRoundTrip(
                    vm,
                    "リレーションの削除",
                    () => vm.RemoveSelectedRelationshipCommand.Execute(null)
                );

                vm.SelectedEntity = vm.Entities[1];
                vm.SelectedColumn = vm.SelectedEntity.Columns[1];
                AssertRoundTrip(vm, "列の削除", () => vm.RemoveSelectedColumnCommand.Execute(null));
            }
        );

    [Fact(
        DisplayName = "作成中のリレーションの始点にしたテーブルの追加を Undo すると、作成モードが解ける"
    )]
    public void PendingRelationshipSource_UndoOfAdd_CancelsMode() =>
        UndoKnownFailures.Run(
            nameof(PendingRelationshipSource_UndoOfAdd_CancelsMode),
            () =>
            {
                var vm = Build();

                vm.AddEntityCommand.Execute(null);
                var added = vm.SelectedEntity!;
                vm.StartAddOneToManyCommand.Execute(null);
                vm.OnEntityClicked(added);

                vm.UndoCommand.Execute(null);

                AssertInvariants(vm, "始点の追加を Undo");
            }
        );

    [Fact(
        DisplayName = "検索結果の表が削除されたあとに Enter を押しても、外れたテーブルは選ばれない"
    )]
    public void Search_NextMatch_AfterDeletingMatch_DoesNotSelectDetachedEntity() =>
        UndoKnownFailures.Run(
            nameof(Search_NextMatch_AfterDeletingMatch_DoesNotSelectDetachedEntity),
            () =>
            {
                var vm = Build();
                vm.SearchQuery = "Ord";
                vm.SearchResults.Should().ContainSingle();
                var orders = vm.SearchResults[0].Entity;

                vm.SelectedEntity = orders;
                vm.RemoveSelectedEntityCommand.Execute(null);

                vm.GoToNextMatchCommand.Execute(null);

                vm.SelectedEntity.Should().NotBe(orders, "削除済みのテーブルを選ばない");
                AssertInvariants(vm, "削除後の次の候補");
            }
        );

    [Fact(
        DisplayName = "図を読み直したあとに古い検索結果から選んで削除しても、生きている表の名前付きクエリは消えない"
    )]
    public void Search_StaleResultAfterReload_DoesNotRemoveQueriesOfLiveEntity() =>
        UndoKnownFailures.Run(
            nameof(Search_StaleResultAfterReload_DoesNotRemoveQueriesOfLiveEntity),
            () =>
            {
                var vm = Build();
                var model = vm.ToDiagramModel();

                vm.SearchQuery = "Ord";
                vm.SearchResults.Should().ContainSingle();

                // 同じ Id の図を読み直す（外部変更の自動再読込・取込）。旧 ViewModel は図から外れる
                vm.ReplaceDiagramFromModule(model);

                vm.GoToNextMatchCommand.Execute(null);
                AssertInvariants(vm, "再読込後の次の候補");

                vm.DeleteSelectedCommand.Execute(null);
                AssertInvariants(vm, "選択の削除");

                // クエリは、その表が図に居る間だけ存在する（居ない表のクエリは残らず、居る表のクエリは消えない）
                foreach (var query in model.Queries)
                {
                    var tableIsInDiagram = vm.Entities.Any(entity => entity.Id == query.EntityId);

                    vm.Queries.Any(live => live.Name == query.Name)
                        .Should()
                        .Be(
                            tableIsInDiagram,
                            "名前付きクエリ {0} は、その表が図に居る間だけ存在する",
                            query.Name
                        );
                }
            }
        );

    [Fact(
        DisplayName = "図に居ないテーブルを選択したまま削除しても、何もせず履歴も積まず、同じ Id の生きている表のクエリは消えない"
    )]
    public void RemoveSelectedEntity_OfDetachedEntity_DoesNothing() =>
        UndoKnownFailures.Run(
            nameof(RemoveSelectedEntity_OfDetachedEntity_DoesNothing),
            () =>
            {
                var vm = Build();
                var model = vm.ToDiagramModel();
                var ghost = vm.Entities[1];

                // 同じ Id の図を読み直す。旧 ViewModel（ghost）は図から外れ、生きている同じ Id の表ができる
                vm.ReplaceDiagramFromModule(model);
                vm.Entities.Should().NotContain(ghost);
                vm.Queries.Should().ContainSingle();

                // 防御の確認: 何かの経路で図に居ない VM が選択に入っても、削除は何もしない
                vm.SelectedEntity = ghost;
                vm.RemoveSelectedEntityCommand.Execute(null);

                vm.UndoRedo.CanUndo.Should().BeFalse("履歴を積まない");
                vm.Entities.Should().HaveCount(2);
                vm.Queries.Should().ContainSingle("同じ Id の生きている表のクエリは消えない");
                vm.SelectedEntity.Should().BeNull();
            }
        );

    [Fact(
        DisplayName = "検索結果の表が図から外れると、その結果だけが取り除かれ、フォーカスは同じ結果を指し続ける"
    )]
    public void SearchResults_PrunedWhenEntityLeavesDiagram() =>
        UndoKnownFailures.Run(
            nameof(SearchResults_PrunedWhenEntityLeavesDiagram),
            () =>
            {
                var vm = Build();
                vm.SearchQuery = "r";
                vm.SearchResults.Select(result => result.Entity.TableName)
                    .Should()
                    .Equal("Customers", "Orders");

                // 2 番目（フォーカス中ではない）の結果の表を消すと、先頭を指し続ける
                vm.SelectedEntity = vm.Entities[1];
                vm.RemoveSelectedEntityCommand.Execute(null);

                vm.SearchResults.Select(result => result.Entity.TableName)
                    .Should()
                    .Equal("Customers");
                vm.SelectedSearchResult!.Entity.TableName.Should().Be("Customers");
                vm.MatchCountText.Should().Be("1/1");

                // Undo で表が戻っても、結果は入力・Enter の再評価までは増えない。Enter で取り直して先へ進める
                vm.UndoCommand.Execute(null);
                vm.GoToNextMatchCommand.Execute(null);

                vm.SearchResults.Select(result => result.Entity.TableName)
                    .Should()
                    .Equal("Customers", "Orders");
                vm.SelectedEntity!.TableName.Should().Be("Orders");
                AssertInvariants(vm, "Undo のあとの Enter");
            }
        );

    [Fact(DisplayName = "図を読み直したあとの Enter は、読み直した図の一致するテーブルを選ぶ")]
    public void Search_NextMatch_AfterReload_SelectsLiveEntity() =>
        UndoKnownFailures.Run(
            nameof(Search_NextMatch_AfterReload_SelectsLiveEntity),
            () =>
            {
                var vm = Build();
                vm.SearchQuery = "Ord";
                var model = vm.ToDiagramModel();

                vm.ReplaceDiagramFromModule(model);
                vm.GoToNextMatchCommand.Execute(null);

                vm.SelectedEntity.Should().NotBeNull();
                vm.SelectedEntity!.TableName.Should().Be("Orders");
                vm.Entities.Should().Contain(vm.SelectedEntity);
            }
        );

    [Fact(
        DisplayName = "フォーカスより前の結果の表が図から外れると、フォーカスは同じ結果を指し続け、位置の表示も詰まる"
    )]
    public void SearchResults_PruneBeforeFocus_KeepsFocusOnSameResult() =>
        UndoKnownFailures.Run(
            nameof(SearchResults_PruneBeforeFocus_KeepsFocusOnSameResult),
            () =>
            {
                var vm = new MainViewModel();
                vm.ReplaceDiagramFromModule(
                    new ErDiagram
                    {
                        Entities =
                        {
                            new Entity { TableName = "TableA" },
                            new Entity { TableName = "TableB" },
                            new Entity { TableName = "TableC" },
                            new Entity { TableName = "TableD" },
                        },
                    }
                );
                vm.SearchQuery = "Table";
                vm.GoToNextMatchCommand.Execute(null);
                vm.GoToNextMatchCommand.Execute(null);
                vm.SelectedSearchResult!.Entity.TableName.Should().Be("TableC");
                vm.MatchCountText.Should().Be("3/4");

                // フォーカス中（3 番目）より前の 1 番目の表を消す（末尾への頭打ちでは救えない 4 件で確かめる）
                vm.SelectedEntity = vm.Entities.Single(e => e.TableName == "TableA");
                vm.RemoveSelectedEntityCommand.Execute(null);

                vm.SelectedSearchResult!.Entity.TableName.Should()
                    .Be("TableC", "フォーカスは同じ結果を指し続ける");
                vm.MatchCountText.Should().Be("2/3", "取り除いた分だけ位置も詰まる");
                AssertInvariants(vm, "フォーカスより前の結果の削除");
            }
        );

    [Fact(
        DisplayName = "列の一覧が Reset されても、選択中の列が図に居るテーブルのものなら選択は解けない"
    )]
    public void ColumnsReset_ClearsSelectionOnlyWhenSelectedColumnLeavesDiagram() =>
        UndoKnownFailures.Run(
            nameof(ColumnsReset_ClearsSelectionOnlyWhenSelectedColumnLeavesDiagram),
            () =>
            {
                var vm = Build();
                vm.SelectedEntity = vm.Entities[1];
                var selected = vm.SelectedEntity.Columns[1];
                vm.SelectedColumn = selected;

                // 別のテーブルの列の一覧が Reset されても、選択中の列は図に居る
                vm.Entities[0].Columns.Clear();
                vm.SelectedColumn.Should().BeSameAs(selected);

                // 選択中の列を持つテーブル自身が Reset されたら、列は図から居なくなる
                vm.Entities[1].Columns.Clear();
                vm.SelectedColumn.Should().BeNull();
            }
        );

    [Fact(DisplayName = "列の並べ替えでは、選択中の列の選択が解けない")]
    public void MoveColumn_KeepsColumnSelection() =>
        UndoKnownFailures.Run(
            nameof(MoveColumn_KeepsColumnSelection),
            () =>
            {
                var vm = Build();
                vm.SelectedEntity = vm.Entities[1];
                var selected = vm.SelectedEntity.Columns[1];
                vm.SelectedColumn = selected;

                vm.SelectedEntity.Columns.Move(1, 0);

                vm.SelectedColumn.Should().BeSameAs(selected, "並べ替えは列が外れる操作ではない");
            }
        );

    [Fact(DisplayName = "列を選択したままテーブルを削除・図を置換すると、列の選択も解ける")]
    public void RemoveEntity_WithColumnSelected_ClearsColumnSelection() =>
        UndoKnownFailures.Run(
            nameof(RemoveEntity_WithColumnSelected_ClearsColumnSelection),
            () =>
            {
                var vm = Build();
                vm.SelectedEntity = vm.Entities[1];
                vm.SelectedColumn = vm.SelectedEntity.Columns[1];

                vm.RemoveSelectedEntityCommand.Execute(null);

                vm.SelectedColumn.Should().BeNull("削除したテーブルの列を選択したままにしない");
                AssertInvariants(vm, "テーブル削除");

                vm.UndoCommand.Execute(null);
                vm.SelectedEntity = vm.Entities[1];
                vm.SelectedColumn = vm.SelectedEntity.Columns[1];

                vm.ReplaceDiagramFromModule(new ErDiagram());

                vm.SelectedColumn.Should()
                    .BeNull("図を置換したあとも、外れた列を選択したままにしない");
                AssertInvariants(vm, "図の置換");
            }
        );

    [Fact(DisplayName = "図の置換・新規作成のあとも、検索結果と選択は図に居る要素だけを指す")]
    public void ReplaceAndNew_KeepInvariants() =>
        UndoKnownFailures.Run(
            nameof(ReplaceAndNew_KeepInvariants),
            () =>
            {
                var vm = Build();
                vm.SearchQuery = "Ord";
                vm.SelectedEntity = vm.Entities[1];

                vm.ReplaceDiagramFromModule(new ErDiagram());
                AssertInvariants(vm, "別の図への置換");

                vm.SearchQuery = "Ord";
                AssertInvariants(vm, "置換後の再検索");
            }
        );
}
