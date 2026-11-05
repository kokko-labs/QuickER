using QuickER.Tests.TestSupport.Engines;

namespace QuickER.Tests.Mcp.Tools;

/// <summary>
/// ER 図のツールの決定的な行の表（両ホスト＝GUI の <c>ErDiagramDynamicTools</c> と MCP の <c>DocumentErDiagramToolHost</c> で
/// 同じ期待を流す。流すのは <see cref="ToolScenarioTests"/>）
/// </summary>
/// <remarks>
/// <para>
/// 主題ごとの行は <c>ToolScenarioRows.{主題}.cs</c> の private メソッドが返し、<see cref="AllRows"/> に足す。行の Id は
/// 「{主題}-{守っていること}-{形}」の英小文字とハイフン、説明は元のテストの DisplayName。
/// </para>
/// <para>
/// 行は元のテストと同じ入力（方言・前提の図・引数）で同じ期待を書く。元が片方のホストでしか確かめていなかった行も両ホストで流す
/// （対応表に「元より広い」と書く）。ホストで値が違う面は <see cref="ToolExpect.ForHost"/> に理由つきで書く。
/// 静的なデータは式形式のプロパティ（partial の初期化順で null にならない）。
/// </para>
/// </remarks>
internal static partial class ToolScenarioRows
{
    /// <summary>表</summary>
    public static ScenarioTable<ToolScenario> Table { get; } =
        new(EngineIds.DiagramEdit, AllRows().Select(row => row.Build()));

    /// <summary>
    /// Customer(CustomerId PK, Name) と Order(OrderId PK, CustomerId, ShipToCustomerId) の図（2 ホストのテストの共通の前提）
    /// </summary>
    public static IEnumerable<(string Tool, object Args)> CustomerOrder =>
        [
            ("add_entity", new { table_name = "Customer" }),
            (
                "add_column",
                new
                {
                    table_name = "Customer",
                    column_name = "CustomerId",
                    data_type = "int",
                    is_primary_key = true,
                }
            ),
            (
                "add_column",
                new
                {
                    table_name = "Customer",
                    column_name = "Name",
                    data_type = "nvarchar(50)",
                }
            ),
            ("add_entity", new { table_name = "Order" }),
            (
                "add_column",
                new
                {
                    table_name = "Order",
                    column_name = "OrderId",
                    data_type = "int",
                    is_primary_key = true,
                }
            ),
            (
                "add_column",
                new
                {
                    table_name = "Order",
                    column_name = "CustomerId",
                    data_type = "int",
                }
            ),
            (
                "add_column",
                new
                {
                    table_name = "Order",
                    column_name = "ShipToCustomerId",
                    data_type = "int",
                }
            ),
        ];

    private static IEnumerable<ToolRow> AllRows() =>
        [
            .. ValidationRows(),
            .. NamesRows(),
            .. NotesRows(),
            .. RemoveEntityRows(),
            .. EntityRows(),
            .. ColumnRows(),
            .. RelationshipRows(),
            .. SummaryRows(),
            .. ForeignKeyMarkRows(),
            .. PrimaryKeyRows(),
            .. UniqueConstraintRows(),
            .. NoOpRows(),
            .. QueryRows(),
        ];

    private static ToolRow Row(string id, string description) => new(id, description);
}
