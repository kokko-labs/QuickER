using AwesomeAssertions;
using QuickER.Tests.TestSupport.Engines;
using QuickER.ViewModels;

namespace QuickER.Tests.Gui.UndoRedo;

/// <summary>
/// 選択の不変条件の表（図から外れる・戻る操作の手順と、各手順の後の <see cref="SelectionInvariant.Assert"/>）
/// </summary>
/// <remarks>
/// 主題ごとの行は <c>SelectionInvariantRows.{主題}.cs</c> の private メソッドが返し、<see cref="AllRows"/> に足す。
/// 行の説明は元のテストの DisplayName。流すのは <see cref="SelectionInvariantTests.Row_KeepsInvariants"/>。
/// </remarks>
internal static partial class SelectionInvariantRows
{
    /// <summary>表</summary>
    public static ScenarioTable<SelectionScenario> Table { get; } =
        new(EngineIds.DiagramEdit, AllRows().Select(row => row.Build()));

    private static IEnumerable<SelectionRow> AllRows() =>
        [
            .. LifecycleRows(),
            .. OperationRows(),
            .. SearchRows(),
            .. ColumnRows(),
            .. RelationshipModeRows(),
            .. DetachedWriteRows(),
            .. ReloadRows(),
        ];

    private static SelectionRow Row(string id, string description) => new(id, description);

    /// <summary>追加・複製・貼り付けの Undo / Redo</summary>
    private static IEnumerable<SelectionRow> LifecycleRows()
    {
        yield return Row(
                "selection-add-entity-undo-clears-selection",
                "テーブルの追加を Undo すると、選択が外れた追加分を指したままにならない"
            )
            .Diagram(SelectionInvariantTests.Build)
            .Step(
                "テーブルを追加する",
                context =>
                {
                    context.Vm.AddEntityCommand.Execute(null);
                    context.Remember("added", context.Vm.SelectedEntity);
                }
            )
            .Expect(
                "追加したテーブルは選択される",
                context => context.Recall<EntityViewModel?>("added").Should().NotBeNull()
            )
            .Step("Undo", (MainViewModel vm) => vm.UndoCommand.Execute(null))
            .Expect(
                "Undo で消えたテーブルを選択したままにしない",
                context =>
                {
                    context
                        .Vm.Entities.Should()
                        .NotContain(context.Recall<EntityViewModel>("added"));
                    context.Vm.SelectedEntity.Should().BeNull();
                }
            )
            .Step("Redo", (MainViewModel vm) => vm.RedoCommand.Execute(null))
            .Expect(
                "Redo で戻る",
                context =>
                    context.Vm.Entities.Should().Contain(context.Recall<EntityViewModel>("added"))
            )
            .Covers(SelectionPointer.SelectedEntity, SelectionDetach.Undo);
    }
}
