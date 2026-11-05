using AwesomeAssertions;
using QuickER.UndoRedo;
using QuickER.ViewModels;

namespace QuickER.Tests.Gui.UndoRedo;

internal static partial class UndoOperationRows
{
    /// <summary>
    /// 主キーの編集（構成列の置き換え・実効順の並べ替え）：膜が何列動いても履歴 1 件、変更が無ければ積まない
    /// （図は複合主キーと NULL 許容の列を持つ 1 テーブル）
    /// </summary>
    private static IEnumerable<UndoRow> PrimaryKeyRows()
    {
        yield return Row(
                "primary-key-move-up-reorders-in-one-history",
                "主キー列の上移動は実効順を変え、1 回の Undo で戻る"
            )
            .Diagram(() => TenantRegionDiagram(pinPrimaryKeyOrder: true))
            .Operate(
                UndoEntry.Screen,
                "MovePrimaryKeyColumnUpCommand（2 行目）",
                vm =>
                    vm.MovePrimaryKeyColumnUpCommand.Execute(
                        vm.SelectedEntity!.PrimaryKeyMembers[1]
                    )
            )
            .Commands(typeof(SetPrimaryKeyCommand))
            .Check(
                UndoStage.Operated,
                "実効順と主キーの行が入れ替わる",
                vm =>
                {
                    var entity = vm.SelectedEntity!;
                    PrimaryKeyOrder(entity).Should().Equal("RegionCode", "TenantId");
                    entity
                        .PrimaryKeyMembers.Select(member => member.Column.Name)
                        .Should()
                        .Equal("RegionCode", "TenantId");
                }
            );

        yield return Row(
                "primary-key-move-down-reorders-in-one-history",
                "主キー列の下移動は実効順を変え、1 回の Undo で戻る"
            )
            .Diagram(() => TenantRegionDiagram(pinPrimaryKeyOrder: true))
            .Operate(
                UndoEntry.Screen,
                "MovePrimaryKeyColumnDownCommand（1 行目）",
                vm =>
                    vm.MovePrimaryKeyColumnDownCommand.Execute(
                        vm.SelectedEntity!.PrimaryKeyMembers[0]
                    )
            )
            .Commands(typeof(SetPrimaryKeyCommand))
            .Check(
                UndoStage.Operated,
                "実効順が入れ替わる",
                vm => PrimaryKeyOrder(vm.SelectedEntity!).Should().Equal("RegionCode", "TenantId")
            );

        yield return Row(
                "primary-key-move-at-boundary-does-nothing",
                "端の行の移動は何も起こさない（履歴も積まない）"
            )
            .Diagram(() => TenantRegionDiagram(pinPrimaryKeyOrder: true))
            .Operate(
                UndoEntry.Screen,
                "先頭の行の上移動と、末尾の行の下移動",
                vm =>
                {
                    var entity = vm.SelectedEntity!;
                    vm.MovePrimaryKeyColumnUpCommand.Execute(entity.PrimaryKeyMembers[0]);
                    vm.MovePrimaryKeyColumnDownCommand.Execute(entity.PrimaryKeyMembers[1]);
                }
            )
            .NoHistory();

        yield return Row(
                "primary-key-apply-nullable-column-becomes-not-null",
                "NULL 許容列を主キーへ含めると NOT NULL になり、Undo で NULL 許容が戻る"
            )
            .Diagram(() => TenantRegionDiagram(pinPrimaryKeyOrder: true))
            .Operate(
                UndoEntry.Screen,
                "ApplyPrimaryKey(TenantId, Code)",
                vm =>
                    vm.ApplyPrimaryKey(
                        vm.SelectedEntity!,
                        [vm.SelectedEntity!.Columns[0], SelectedColumnNamed(vm, "Code")]
                    )
            )
            .Commands(typeof(SetPrimaryKeyCommand))
            .Check(
                UndoStage.Operated,
                "Code が主キーになって NOT NULL になり、実効順が置き換わる",
                vm =>
                {
                    var code = SelectedColumnNamed(vm, "Code");
                    code.IsPrimaryKey.Should().BeTrue();
                    code.IsNullable.Should().BeFalse();
                    PrimaryKeyOrder(vm.SelectedEntity!).Should().Equal("TenantId", "Code");
                }
            );

        yield return Row(
                "primary-key-apply-dropped-column-keeps-nullability",
                "主キーから外した列の NULL 許容は Undo / Redo をまたいで据え置かれる"
            )
            .Diagram(() => TenantRegionDiagram(pinPrimaryKeyOrder: true))
            .Operate(
                UndoEntry.Screen,
                "ApplyPrimaryKey(TenantId)",
                vm => vm.ApplyPrimaryKey(vm.SelectedEntity!, [vm.SelectedEntity!.Columns[0]])
            )
            .Commands(typeof(SetPrimaryKeyCommand))
            .Check(
                UndoStage.Operated,
                "RegionCode は主キーから外れるが NOT NULL のまま",
                vm =>
                {
                    var regionCode = SelectedColumnNamed(vm, "RegionCode");
                    regionCode.IsPrimaryKey.Should().BeFalse();
                    regionCode
                        .IsNullable.Should()
                        .BeFalse("主キーから外しただけで NULL 許容を変えてはならない");
                }
            );

        yield return Row(
                "primary-key-apply-same-configuration-pushes-nothing",
                "同じ主キー構成・同じ順序の再適用では履歴を積まない"
            )
            .Diagram(() => TenantRegionDiagram(pinPrimaryKeyOrder: true))
            .Operate(
                UndoEntry.Screen,
                "ApplyPrimaryKey(TenantId, RegionCode)（今と同じ）",
                vm =>
                    vm.ApplyPrimaryKey(
                        vm.SelectedEntity!,
                        [vm.SelectedEntity!.Columns[0], vm.SelectedEntity.Columns[1]]
                    )
            )
            .NoHistory();

        yield return Row(
                "primary-key-apply-pinning-same-effective-order-pushes-history",
                "順序未設定の図へ同じ並びを明示するとピン留めとして履歴を積む"
            )
            .Diagram(() => TenantRegionDiagram(pinPrimaryKeyOrder: false))
            .Prepare(
                "主キーの順序は未設定で、実効順は宣言順",
                vm =>
                {
                    vm.SelectedEntity!.PrimaryKeyColumnIds.Should().BeEmpty();
                    PrimaryKeyOrder(vm.SelectedEntity).Should().Equal("TenantId", "RegionCode");
                }
            )
            .Operate(
                UndoEntry.Screen,
                "ApplyPrimaryKey(TenantId, RegionCode)（実効順と同じ並びを明示）",
                vm =>
                    vm.ApplyPrimaryKey(
                        vm.SelectedEntity!,
                        [vm.SelectedEntity!.Columns[0], vm.SelectedEntity.Columns[1]]
                    )
            )
            .Commands(typeof(SetPrimaryKeyCommand))
            .Check(
                UndoStage.Operated,
                "並びが保存される",
                vm =>
                {
                    var entity = vm.SelectedEntity!;
                    entity
                        .PrimaryKeyColumnIds.Should()
                        .Equal(entity.Columns[0].Id, entity.Columns[1].Id);
                }
            );
    }
}
