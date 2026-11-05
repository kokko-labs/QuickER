using QuickER.Tests.TestSupport.Engines;

namespace QuickER.Tests.Gui.Services;

/// <summary>
/// 自動整列の行の表（元のテストの図・整列・指標の閾値）。どの行も共通の性質（<see cref="LayoutRunner.AssertCommonProperties"/>）を
/// 先に当ててから行の指標を当てる。流すのは <see cref="AutoLayoutScenarioTests"/>
/// </summary>
/// <remarks>
/// 主題ごとの行は <c>AutoLayoutScenarioRows.{主題}.cs</c> の private メソッドが返し、<see cref="AllRows"/> に足す。
/// 図は形の表（<see cref="LayoutShapes"/>）の形と同じなら <see cref="LayoutRow.Shape"/>、違えば元のテストの図のまま
/// <see cref="LayoutRow.Custom"/>。指標は <see cref="LayoutMetrics"/> を使い、閾値は元のテストの値のまま。
/// </remarks>
internal static partial class AutoLayoutScenarioRows
{
    /// <summary>表</summary>
    public static ScenarioTable<LayoutScenario> Table { get; } =
        new(EngineIds.DiagramEdit, AllRows().Select(row => row.Build()));

    private static IEnumerable<LayoutRow> AllRows() =>
        [.. EmptyRows(), .. ServiceRows(), .. AppendRows()];

    private static LayoutRow Row(string id, string description) => new(id, description);

    /// <summary>空の図（どの整列も例外を出さない）</summary>
    private static IEnumerable<LayoutRow> EmptyRows()
    {
        yield return Row(
                "grid-empty-does-not-throw",
                "LayoutGrid: 空コレクションでも例外にならない"
            )
            .Shape("empty", LayoutKind.Grid);

        yield return Row(
                "force-empty-does-not-throw",
                "LayoutForceDirected: 空コレクションでも例外にならない"
            )
            .Shape("empty", LayoutKind.Force);
    }
}
