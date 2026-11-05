using AwesomeAssertions;
using QuickER.Model;
using QuickER.ViewModels;

namespace QuickER.Tests.Gui.UndoRedo;

internal static partial class SelectionInvariantRows
{
    /// <summary>検索結果と選択が、削除・読み直しのあとも図に居る要素だけを指す</summary>
    private static IEnumerable<SelectionRow> SearchRows()
    {
        yield return Row(
                "search-next-match-after-deleting-match-skips-detached-entity",
                "検索結果の表が削除されたあとに Enter を押しても、外れたテーブルは選ばれない"
            )
            .Diagram(SelectionInvariantTests.Build)
            .Step(
                "「Ord」で検索する",
                context =>
                {
                    context.Vm.SearchQuery = "Ord";
                    context.Remember("orders", context.Vm.SearchResults[0].Entity);
                }
            )
            .Expect(
                "検索結果は 1 件",
                (MainViewModel vm) => vm.SearchResults.Should().ContainSingle()
            )
            .Step(
                "検索結果の表を選んで削除する",
                context =>
                {
                    context.Vm.SelectedEntity = context.Recall<EntityViewModel>("orders");
                    context.Vm.RemoveSelectedEntityCommand.Execute(null);
                }
            )
            .Step("次の候補へ進む", (MainViewModel vm) => vm.GoToNextMatchCommand.Execute(null))
            .Expect(
                "削除済みのテーブルを選ばない",
                context =>
                    context
                        .Vm.SelectedEntity.Should()
                        .NotBe(context.Recall<EntityViewModel>("orders"))
            )
            .Covers(SelectionPointer.SearchResults, SelectionDetach.Delete)
            .Covers(SelectionPointer.SelectedEntity, SelectionDetach.Delete);

        yield return Row(
                "search-stale-result-after-reload-keeps-queries-of-live-entity",
                "図を読み直したあとに古い検索結果から選んで削除しても、生きている表の名前付きクエリは消えない"
            )
            .Diagram(SelectionInvariantTests.Build)
            .Step(
                "図のモデルを取り、「Ord」で検索する",
                context =>
                {
                    context.Remember("model", context.Vm.ToDiagramModel());
                    context.Vm.SearchQuery = "Ord";
                }
            )
            .Expect(
                "検索結果は 1 件",
                (MainViewModel vm) => vm.SearchResults.Should().ContainSingle()
            )
            .Step(
                "同じ Id の図を読み直す（外部変更の自動再読込・取込。旧 ViewModel は図から外れる）",
                context => context.Vm.ReplaceDiagramFromModule(context.Recall<ErDiagram>("model"))
            )
            .Step("次の候補へ進む", (MainViewModel vm) => vm.GoToNextMatchCommand.Execute(null))
            .Step("選択を削除する", (MainViewModel vm) => vm.DeleteSelectedCommand.Execute(null))
            .Expect(
                "クエリは、その表が図に居る間だけ存在する（居ない表のクエリは残らず、居る表のクエリは消えない）",
                context =>
                {
                    foreach (var query in context.Recall<ErDiagram>("model").Queries)
                    {
                        var tableIsInDiagram = context.Vm.Entities.Any(entity =>
                            entity.Id == query.EntityId
                        );

                        context
                            .Vm.Queries.Any(live => live.Name == query.Name)
                            .Should()
                            .Be(
                                tableIsInDiagram,
                                "名前付きクエリ {0} は、その表が図に居る間だけ存在する",
                                query.Name
                            );
                    }
                }
            )
            .Covers(SelectionPointer.SearchResults, SelectionDetach.Reload)
            .Covers(
                SelectionPointer.SelectedEntity,
                SelectionDetach.Reload,
                SelectionDetach.Delete
            );

        yield return Row(
                "search-remove-selected-entity-of-detached-entity-does-nothing",
                "図に居ないテーブルを選択したまま削除しても、何もせず履歴も積まず、同じ Id の生きている表のクエリは消えない"
            )
            .Diagram(SelectionInvariantTests.Build)
            .Step(
                "図のモデルと 2 つ目のテーブルを覚える",
                context =>
                {
                    context.Remember("model", context.Vm.ToDiagramModel());
                    context.Remember("ghost", context.Vm.Entities[1]);
                }
            )
            .Step(
                "同じ Id の図を読み直す（旧 ViewModel は図から外れ、同じ Id の生きている表ができる）",
                context => context.Vm.ReplaceDiagramFromModule(context.Recall<ErDiagram>("model"))
            )
            .Expect(
                "旧テーブルは図に居らず、クエリは 1 本",
                context =>
                {
                    context
                        .Vm.Entities.Should()
                        .NotContain(context.Recall<EntityViewModel>("ghost"));
                    context.Vm.Queries.Should().ContainSingle();
                }
            )
            .Step(
                "防御の確認：図に居ない VM を選択に入れて削除する",
                context =>
                {
                    context.Vm.SelectedEntity = context.Recall<EntityViewModel>("ghost");
                    context.Vm.RemoveSelectedEntityCommand.Execute(null);
                }
            )
            .Expect(
                "何もせず履歴も積まず、同じ Id の生きている表のクエリは消えない",
                (MainViewModel vm) =>
                {
                    vm.UndoRedo.CanUndo.Should().BeFalse("履歴を積まない");
                    vm.Entities.Should().HaveCount(2);
                    vm.Queries.Should().ContainSingle("同じ Id の生きている表のクエリは消えない");
                    vm.SelectedEntity.Should().BeNull();
                }
            )
            .Covers(
                SelectionPointer.SelectedEntity,
                SelectionDetach.Reload,
                SelectionDetach.Delete
            );

        yield return Row(
                "search-results-pruned-when-entity-leaves-diagram",
                "検索結果の表が図から外れると、その結果だけが取り除かれ、フォーカスは同じ結果を指し続ける"
            )
            .Diagram(SelectionInvariantTests.Build)
            .Step("「r」で検索する", (MainViewModel vm) => vm.SearchQuery = "r")
            .Expect(
                "検索結果は Customers・Orders の順",
                (MainViewModel vm) =>
                    vm
                        .SearchResults.Select(result => result.Entity.TableName)
                        .Should()
                        .Equal("Customers", "Orders")
            )
            .Step(
                "2 番目（フォーカス中ではない）の結果の表を選んで削除する",
                (MainViewModel vm) =>
                {
                    vm.SelectedEntity = vm.Entities[1];
                    vm.RemoveSelectedEntityCommand.Execute(null);
                }
            )
            .Expect(
                "その結果だけが取り除かれ、先頭を指し続ける",
                (MainViewModel vm) =>
                {
                    vm.SearchResults.Select(result => result.Entity.TableName)
                        .Should()
                        .Equal("Customers");
                    vm.SelectedSearchResult!.Entity.TableName.Should().Be("Customers");
                    vm.MatchCountText.Should().Be("1/1");
                }
            )
            .Step(
                "Undo で表を戻し、Enter で取り直して先へ進める",
                (MainViewModel vm) =>
                {
                    vm.UndoCommand.Execute(null);
                    vm.GoToNextMatchCommand.Execute(null);
                }
            )
            .Expect(
                "結果は Enter で取り直され、Orders を選ぶ",
                (MainViewModel vm) =>
                {
                    vm.SearchResults.Select(result => result.Entity.TableName)
                        .Should()
                        .Equal("Customers", "Orders");
                    vm.SelectedEntity!.TableName.Should().Be("Orders");
                }
            )
            .Covers(SelectionPointer.SearchResults, SelectionDetach.Delete, SelectionDetach.Undo);

        yield return Row(
                "search-next-match-after-reload-selects-live-entity",
                "図を読み直したあとの Enter は、読み直した図の一致するテーブルを選ぶ"
            )
            .Diagram(SelectionInvariantTests.Build)
            .Step(
                "「Ord」で検索し、図のモデルを取る",
                context =>
                {
                    context.Vm.SearchQuery = "Ord";
                    context.Remember("model", context.Vm.ToDiagramModel());
                }
            )
            .Step(
                "図を読み直し、次の候補へ進む",
                context =>
                {
                    context.Vm.ReplaceDiagramFromModule(context.Recall<ErDiagram>("model"));
                    context.Vm.GoToNextMatchCommand.Execute(null);
                }
            )
            .Expect(
                "読み直した図の Orders を選ぶ",
                (MainViewModel vm) =>
                {
                    vm.SelectedEntity.Should().NotBeNull();
                    vm.SelectedEntity!.TableName.Should().Be("Orders");
                    vm.Entities.Should().Contain(vm.SelectedEntity);
                }
            )
            .Covers(SelectionPointer.SearchResults, SelectionDetach.Reload)
            .Covers(SelectionPointer.SelectedEntity, SelectionDetach.Reload);

        yield return Row(
                "search-prune-before-focus-keeps-focus-on-same-result",
                "フォーカスより前の結果の表が図から外れると、フォーカスは同じ結果を指し続け、位置の表示も詰まる"
            )
            .Diagram(FourTablesDiagram)
            .Step(
                "「Table」で検索し、Enter で 3 番目まで進める",
                (MainViewModel vm) =>
                {
                    vm.SearchQuery = "Table";
                    vm.GoToNextMatchCommand.Execute(null);
                    vm.GoToNextMatchCommand.Execute(null);
                }
            )
            .Expect(
                "フォーカスは TableC（3/4）",
                (MainViewModel vm) =>
                {
                    vm.SelectedSearchResult!.Entity.TableName.Should().Be("TableC");
                    vm.MatchCountText.Should().Be("3/4");
                }
            )
            .Step(
                "フォーカス中より前の 1 番目の表を選んで削除する",
                (MainViewModel vm) =>
                {
                    vm.SelectedEntity = vm.Entities.Single(e => e.TableName == "TableA");
                    vm.RemoveSelectedEntityCommand.Execute(null);
                }
            )
            .Expect(
                "フォーカスは同じ結果を指し続け、取り除いた分だけ位置も詰まる",
                (MainViewModel vm) =>
                {
                    vm.SelectedSearchResult!.Entity.TableName.Should().Be("TableC");
                    vm.MatchCountText.Should().Be("2/3");
                }
            )
            .Covers(SelectionPointer.SearchResults, SelectionDetach.Delete);
    }

    /// <summary>TableA〜TableD の 4 テーブル（末尾への頭打ちでは救えない件数）</summary>
    private static MainViewModel FourTablesDiagram()
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

        return vm;
    }
}
