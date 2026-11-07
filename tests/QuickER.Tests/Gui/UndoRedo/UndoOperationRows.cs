using QuickER.Model;
using QuickER.Tests.TestSupport.Engines;
using QuickER.UndoRedo;
using QuickER.ViewModels;

namespace QuickER.Tests.Gui.UndoRedo;

/// <summary>
/// VM の操作の往復の表（画面・追跡の自動記録・ツール・ビヘイビアの入口から積む履歴の件数と型、Undo / Redo で図が並びまで戻ること）
/// </summary>
/// <remarks>
/// <para>
/// コマンドを直接 Execute / Undo する <see cref="UndoCommandRoundTripTests"/> は、どの操作がどのコマンドを何件積むか・
/// 表とリレーションの並びの復元を見ない。この表がそこを受ける。流すのは <see cref="UndoOperationTests"/>。
/// </para>
/// <para>
/// 主題ごとの行は <c>UndoOperationRows.{主題}.cs</c> の private メソッドが返し、<see cref="AllRows"/> に足す。
/// 図は元のテストの図のまま <c>UndoOperationRows.Fixtures.cs</c> に 1 図 1 か所で置く。行の説明は元のテストの DisplayName。
/// </para>
/// </remarks>
internal static partial class UndoOperationRows
{
    /// <summary>表</summary>
    public static ScenarioTable<UndoOperationScenario> Table { get; } =
        new(EngineIds.DiagramEdit, AllRows().Select(row => row.Build()));

    private static IEnumerable<UndoRow> AllRows() =>
        [
            .. RemovalOrderRows(),
            .. RemovalPositionRows(),
            .. EntityRows(),
            .. ColumnRows(),
            .. RelationshipRows(),
            .. PrimaryKeyRows(),
            .. UniqueConstraintRows(),
            .. SelectionRows(),
            .. ToolRows(),
            .. TargetDbmsRows(),
        ];

    private static UndoRow Row(string id, string description) => new(id, description);

    /// <summary>
    /// 削除の位置（<c>RemovalOrderRoundTripTests</c> の図）：削除を Undo すると元の位置・元の並びへ戻る
    /// （名前の無い外部キーの既定名は並びの先頭から割り当てるので、末尾へ戻ると既定名が入れ替わる）
    /// </summary>
    private static IEnumerable<UndoRow> RemovalOrderRows()
    {
        yield return Row(
                "removal-order-remove-relationship-restores-position",
                "リレーションの削除を Undo すると、元の位置へ戻る（名前のない外部キーの既定名が入れ替わらない）"
            )
            .Diagram(RemovalOrderDiagram)
            .Prepare(
                "先頭のリレーションを選ぶ",
                vm => vm.SelectedRelationship = vm.Relationships[0]
            )
            .Operate(
                UndoEntry.Screen,
                "RemoveSelectedRelationshipCommand",
                vm => vm.RemoveSelectedRelationshipCommand.Execute(null)
            )
            .Commands(typeof(RemoveRelationshipCommand));
    }

    /// <summary>同じ親子を結ぶ名前のないリレーション 2 本と、一意制約 2 本、列 4 本を持つ図（RemovalOrderRoundTripTests の図）</summary>
    private static MainViewModel RemovalOrderDiagram()
    {
        var parentId = new Column
        {
            Name = "Id",
            DataType = "int",
            IsPrimaryKey = true,
            IsNullable = false,
        };
        var parentCode = new Column
        {
            Name = "Code",
            DataType = "nvarchar(10)",
            IsNullable = false,
        };
        var parentName = new Column
        {
            Name = "Name",
            DataType = "nvarchar(50)",
            IsNullable = true,
        };
        var parentNote = new Column
        {
            Name = "Note",
            DataType = "nvarchar(50)",
            IsNullable = true,
        };
        var parent = new Entity { TableName = "Parent" };
        parent.Columns.Add(parentId);
        parent.Columns.Add(parentCode);
        parent.Columns.Add(parentName);
        parent.Columns.Add(parentNote);
        parent.UniqueConstraints.Add(
            new UniqueConstraint { Name = "UQ_Parent_Code", ColumnIds = { parentCode.Id } }
        );
        parent.UniqueConstraints.Add(
            new UniqueConstraint { Name = "UQ_Parent_Name", ColumnIds = { parentName.Id } }
        );

        var childId = new Column
        {
            Name = "Id",
            DataType = "int",
            IsPrimaryKey = true,
            IsNullable = false,
        };
        var childMain = new Column
        {
            Name = "MainParentId",
            DataType = "int",
            IsNullable = true,
        };
        var childSub = new Column
        {
            Name = "SubParentId",
            DataType = "int",
            IsNullable = true,
        };
        var child = new Entity { TableName = "Child" };
        child.Columns.Add(childId);
        child.Columns.Add(childMain);
        child.Columns.Add(childSub);

        Relationship Link(Column childColumn) =>
            new()
            {
                SourceEntityId = parent.Id,
                TargetEntityId = child.Id,
                Type = RelationshipType.OneToMany,
                ConstraintName = null,
                ColumnPairs =
                {
                    new RelationshipColumnPair
                    {
                        SourceColumnId = parentId.Id,
                        TargetColumnId = childColumn.Id,
                    },
                },
            };

        var vm = new MainViewModel();
        vm.ReplaceDiagramFromModule(
            new ErDiagram
            {
                Entities = { parent, child },
                Relationships = { Link(childMain), Link(childSub) },
            }
        );

        return vm;
    }
}
