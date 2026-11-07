using AwesomeAssertions;
using QuickER.Model;
using QuickER.Tests.Gui.ViewModels;
using QuickER.ViewModels;

namespace QuickER.Tests.Gui.UndoRedo;

internal static partial class SelectionInvariantRows
{
    /// <summary>
    /// 図の読み直し（開く・外部変更の自動再読込・取込）と置換のあとも、選択中の列・リレーション・作成中の始点・行の選択候補は
    /// 図に居る要素だけを指す
    /// </summary>
    /// <remarks>
    /// 読み直しは同じ Id の図を <c>ReplaceDiagramFromModule</c> で置き換える形で表す（旧 ViewModel は図から外れ、同じ Id の新しい ViewModel ができる）。
    /// 期待は <c>src/QuickER.Gui/ViewModels/CLAUDE.md</c> の「選択・検索結果・作成中リレーションの解除」「選択を当て直す順」。
    /// 検索結果の Reset の行は、製品で結果の一覧を空にする経路が <c>FillSearchResults</c>（<c>SearchResults.Clear()</c> して足し直す）だけのため、検索のし直しで表す。
    /// </remarks>
    private static IEnumerable<SelectionRow> ReloadRows()
    {
        yield return Row(
                "reload-selected-column-detaches-old-column",
                "図を読み直すと、選択中の列は旧図の列を指したままにならない"
            )
            .Diagram(SelectionInvariantTests.Build)
            .Step(
                "図のモデルを取り、2 つ目のテーブルの 2 列目を選ぶ",
                context =>
                {
                    context.Remember("model", context.Vm.ToDiagramModel());
                    context.Vm.SelectedEntity = context.Vm.Entities[1];
                    var selected = context.Vm.SelectedEntity.Columns[1];
                    context.Remember("old", selected);
                    context.Vm.SelectedColumn = selected;
                }
            )
            .Step(
                "同じ Id の図を読み直す（旧 ViewModel は図から外れる）",
                context => context.Vm.ReplaceDiagramFromModule(context.Recall<ErDiagram>("model"))
            )
            .Expect(
                "選択は解け、旧図の列は図のどの表にも居ない",
                context =>
                {
                    context.Vm.SelectedColumn.Should().BeNull();
                    context
                        .Vm.Entities.SelectMany(entity => entity.Columns)
                        .Should()
                        .NotContain(context.Recall<ColumnViewModel>("old"));
                }
            )
            .Covers(SelectionPointer.SelectedColumn, SelectionDetach.Reload);

        yield return Row(
                "reload-selected-relationship-detaches-old-relationship",
                "図を読み直すと、選択中のリレーションは旧図のリレーションを指したままにならない"
            )
            .Diagram(SelectionInvariantTests.Build)
            .Step(
                "図のモデルを取り、リレーションをクリックして選ぶ",
                context =>
                {
                    context.Remember("model", context.Vm.ToDiagramModel());
                    var relationship = context.Vm.Relationships[0];
                    context.Remember("old", relationship);
                    context.Vm.OnRelationshipClicked(relationship);
                }
            )
            .Expect(
                "リレーションを選んでいる",
                context =>
                    context
                        .Vm.SelectedRelationship.Should()
                        .BeSameAs(context.Recall<RelationshipViewModel>("old"))
            )
            .Step(
                "同じ Id の図を読み直す（旧 ViewModel は図から外れる）",
                context => context.Vm.ReplaceDiagramFromModule(context.Recall<ErDiagram>("model"))
            )
            .Expect(
                "選択は解け、旧図のリレーションは図に居ない",
                context =>
                {
                    context.Vm.SelectedRelationship.Should().BeNull();
                    context
                        .Vm.Relationships.Should()
                        .NotContain(context.Recall<RelationshipViewModel>("old"));
                }
            )
            .Covers(SelectionPointer.SelectedRelationship, SelectionDetach.Reload);

        yield return Row(
                "reload-pending-source-cancels-mode",
                "図を読み直すと、リレーション作成モードは旧図の始点を持ち越さずに解ける"
            )
            .Diagram(SelectionInvariantTests.Build)
            .Step(
                "図のモデルを取り、1 対多の作成モードで 1 つ目のテーブルを始点に選ぶ",
                context =>
                {
                    context.Remember("model", context.Vm.ToDiagramModel());
                    context.Vm.StartAddOneToManyCommand.Execute(null);
                    context.Vm.OnEntityClicked(context.Vm.Entities[0]);
                    context.Remember("old", context.Vm.Entities[0]);
                }
            )
            .Expect(
                "始点に選んだテーブルが始点になる",
                context =>
                    context
                        .Vm.PendingRelationshipSource.Should()
                        .BeSameAs(context.Recall<EntityViewModel>("old"))
            )
            .Step(
                "同じ Id の図を読み直す（旧 ViewModel は図から外れる）",
                context => context.Vm.ReplaceDiagramFromModule(context.Recall<ErDiagram>("model"))
            )
            .Expect(
                "作成モードが解け、始点は無く、リレーションは増えない",
                (MainViewModel vm) =>
                {
                    vm.IsRelationshipMode.Should().BeFalse();
                    vm.PendingRelationshipSource.Should().BeNull();
                    vm.Relationships.Should().ContainSingle();
                }
            )
            .Covers(SelectionPointer.PendingRelationshipSource, SelectionDetach.Reload);

        yield return Row(
                "reload-candidate-rows-point-only-at-live-columns",
                "図を読み直すと、行の選択候補は読み直した図の列だけを指し、旧図の行への書き込みは新しい制約へ届かない"
            )
            .Diagram(() => CandidateRowReconcileTests.BuildUniqueConstraint().Vm)
            .Step(
                "図のモデルを取り、一意制約の先頭の行を覚える",
                context =>
                {
                    var constraint = context.Vm.Entities.Single().UniqueConstraints.Single();
                    context.Remember("model", context.Vm.ToDiagramModel());
                    context.Remember("oldMember", constraint.Members[0]);
                }
            )
            .Step(
                "同じ Id の図を読み直す（旧 ViewModel は図から外れる）",
                context => context.Vm.ReplaceDiagramFromModule(context.Recall<ErDiagram>("model"))
            )
            .Expect(
                "新しい制約の行の候補は、読み直した図の列だけ",
                context =>
                {
                    var entity = context.Vm.Entities.Single();
                    var constraint = entity.UniqueConstraints.Single();

                    constraint
                        .Members.SelectMany(member => member.AvailableColumns)
                        .Should()
                        .OnlyContain(column => entity.Columns.Contains(column));
                    CandidateRowReconcileTests
                        .MemberNames(entity, constraint)
                        .Should()
                        .Equal("A", "B");
                }
            )
            .Step(
                "旧図の行の選択を落とす",
                context =>
                    context.Recall<UniqueConstraintMemberViewModel>("oldMember").SelectedColumn =
                        null
            )
            .Expect(
                "新しい制約の構成列も履歴も変わらない",
                context =>
                {
                    var entity = context.Vm.Entities.Single();

                    CandidateRowReconcileTests
                        .MemberNames(entity, entity.UniqueConstraints.Single())
                        .Should()
                        .Equal("A", "B");
                    context.Vm.UndoRedo.CanUndo.Should().BeFalse("履歴を積まない");
                }
            )
            .Covers(SelectionPointer.Candidate, SelectionDetach.Reload);

        yield return Row(
                "replace-selected-relationship-detaches-relationship",
                "図を置換すると、選択中のリレーションは図から外れたリレーションを指したままにならない"
            )
            .Diagram(SelectionInvariantTests.Build)
            .Step(
                "リレーションをクリックして選ぶ",
                context =>
                {
                    var relationship = context.Vm.Relationships[0];
                    context.Remember("old", relationship);
                    context.Vm.OnRelationshipClicked(relationship);
                }
            )
            .Expect(
                "リレーションを選んでいる",
                context =>
                    context
                        .Vm.SelectedRelationship.Should()
                        .BeSameAs(context.Recall<RelationshipViewModel>("old"))
            )
            .Step(
                "空の図へ置換する",
                (MainViewModel vm) => vm.ReplaceDiagramFromModule(new ErDiagram())
            )
            .Expect(
                "選択は解け、図にリレーションは無い",
                (MainViewModel vm) =>
                {
                    vm.SelectedRelationship.Should().BeNull();
                    vm.Relationships.Should().BeEmpty();
                }
            )
            .Covers(SelectionPointer.SelectedRelationship, SelectionDetach.Replace);

        yield return Row(
                "reset-search-results-rebuilt-from-live-entities",
                "検索のたびに結果の一覧は作り直され（Clear してから足す）、そのとき図に居るテーブルだけを指す。図から外したテーブルは、同じ語で取り直した結果にも現れない"
            )
            .Diagram(SelectionInvariantTests.Build)
            .Step("「r」で検索する", (MainViewModel vm) => vm.SearchQuery = "r")
            .Expect(
                "結果は Customers・Orders の順",
                (MainViewModel vm) =>
                    vm
                        .SearchResults.Select(result => result.Entity.TableName)
                        .Should()
                        .Equal("Customers", "Orders")
            )
            .Step(
                "Orders を選んで削除する（製品の削除の経路）",
                context =>
                {
                    var orders = context.Vm.Entities.Single(entity => entity.TableName == "Orders");
                    context.Remember("orders", orders);
                    context.Vm.SelectedEntity = orders;
                    context.Vm.DeleteSelectedCommand.Execute(null);
                }
            )
            .Step(
                "同じ語のまま次の候補へ進む（現在の図で結果を取り直す＝FillSearchResults）",
                (MainViewModel vm) => vm.GoToNextMatchCommand.Execute(null)
            )
            .Expect(
                "取り直した結果は Customers だけで、外した Orders を指さない",
                context =>
                {
                    context
                        .Vm.SearchResults.Select(result => result.Entity.TableName)
                        .Should()
                        .Equal("Customers");
                    context
                        .Vm.SearchResults.Select(result => result.Entity)
                        .Should()
                        .NotContain(context.Recall<EntityViewModel>("orders"));
                }
            )
            .Step("Undo で Orders を図へ戻す", (MainViewModel vm) => vm.UndoCommand.Execute(null))
            .Step("「r」から「Or」へ検索し直す", (MainViewModel vm) => vm.SearchQuery = "Or")
            .Expect(
                "結果は図へ戻った Orders を指す",
                (MainViewModel vm) =>
                    vm
                        .SearchResults.Select(result => result.Entity.TableName)
                        .Should()
                        .Equal("Orders")
            )
            .Step("「Ord」で検索し直す", (MainViewModel vm) => vm.SearchQuery = "Ord")
            .Expect(
                "結果は Orders だけに作り直される",
                (MainViewModel vm) =>
                    vm
                        .SearchResults.Select(result => result.Entity.TableName)
                        .Should()
                        .Equal("Orders")
            )
            .Step("検索語を空にする", (MainViewModel vm) => vm.SearchQuery = string.Empty)
            .Expect("結果は空になる", (MainViewModel vm) => vm.SearchResults.Should().BeEmpty())
            .Covers(SelectionPointer.SearchResults, SelectionDetach.Reset);
    }
}
