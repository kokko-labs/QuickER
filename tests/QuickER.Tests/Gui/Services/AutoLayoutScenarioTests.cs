using QuickER.Tests.TestSupport.Engines;
using Xunit.Sdk;

namespace QuickER.Tests.Gui.Services;

/// <summary>自動整列の表（図の形 × 整列の種類の全組の共通の性質と、元のテストの行の指標）を流す</summary>
public sealed class AutoLayoutScenarioTests
{
    /// <summary>図の形 × 整列の種類の全組</summary>
    public static TheoryData<string, string> Combos
    {
        get
        {
            var data = new TheoryData<string, string>();

            foreach (var shape in LayoutShapes.All.Keys)
            {
                foreach (var kind in Enum.GetValues<LayoutKind>())
                {
                    data.Add(shape, kind.ToString());
                }
            }

            return data;
        }
    }

    /// <summary>行の Id</summary>
    public static TheoryData<string> Rows =>
        new(AutoLayoutScenarioRows.Table.Select(row => row.Id));

    [Theory(
        DisplayName = "どの図の形・どの整列でも、例外を出さず・決定的で・表が重ならず・置いた表が動き、追記は宣言した置き場所へ置く"
    )]
    [MemberData(nameof(Combos))]
    public void Combo_KeepsCommonProperties(string shape, string kind)
    {
        var layoutKind = Enum.Parse<LayoutKind>(kind);
        var diagram = LayoutRunner.AssertCommonProperties(
            $"{shape} × {kind}",
            () => LayoutShapes.Build(shape, layoutKind),
            layoutKind
        );

        if (layoutKind == LayoutKind.Append)
        {
            var declared = LayoutShapes.AppendPlacementOf(shape);
            var actual = LayoutShapes.ObservePlacement(diagram);

            if (actual != declared)
            {
                throw new XunitException(
                    $"{shape} × {kind}: 追記の置き場所が {actual?.ToString() ?? "下でも右でもない"}（宣言 {declared}）"
                );
            }
        }
    }

    [Theory(DisplayName = "整列の行は、共通の性質に加えて行の指標の閾値を満たす")]
    [MemberData(nameof(Rows))]
    public void Row_MeetsMetrics(string id)
    {
        var row = AutoLayoutScenarioRows.Table[id];
        var scenario = row.Input;
        var label = $"行 {row.Id}（{row.Description}）";
        var diagram = LayoutRunner.AssertCommonProperties(
            label,
            scenario.Build,
            scenario.Kind,
            scenario.Columns
        );
        var problems = scenario
            .Metrics.Select(metric => (metric.Description, Problem: metric.Verify(diagram)))
            .Where(result => result.Problem is not null)
            .Select(result => $"{result.Description}: {result.Problem}")
            .ToList();

        if (problems.Count > 0)
        {
            throw new XunitException(
                label
                    + Environment.NewLine
                    + "  "
                    + string.Join(Environment.NewLine + "  ", problems)
            );
        }
    }

    [Fact(DisplayName = "整列の表は、図の形 × 整列の種類の組を宣言どおりに網羅する")]
    public void Rows_CoverLayoutDimensions()
    {
        var counter = new CoverageCounter(AutoLayoutDimensions.Table);
        AutoLayoutDimensions.ObserveDeclarations(counter);

        var report = counter.Report();
        report.WriteSummary();
        report.WriteGapUsage(AutoLayoutDimensions.Gaps, AutoLayoutDimensions.Scope);
        report.AssertGaps(AutoLayoutDimensions.Gaps, AutoLayoutDimensions.Scope);
    }
}
