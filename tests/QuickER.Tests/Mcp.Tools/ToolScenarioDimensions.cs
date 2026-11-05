using QuickER.Mcp;
using QuickER.Tests.TestSupport;
using QuickER.Tests.TestSupport.Engines;

namespace QuickER.Tests.Mcp.Tools;

/// <summary>
/// ツールの行（<see cref="ToolScenarioRows"/>）の次元表・網羅しない組の一覧・台帳の行
/// </summary>
/// <remarks>
/// ケースは、行の呼び出し 1 つ × ホスト 1 つ。観測は行の宣言（ツール名・宣言した成否）とホストの値を読むだけで、
/// 行を流さない（<see cref="ToolScenarioTests"/> の網羅の Fact）。種を持たないので、網羅しない組は毎回照合する。
/// ツールの水準は製品のツールの定義（<see cref="ErDiagramToolCatalog.GetDefinitions"/>）から作る。
/// </remarks>
internal static class ToolScenarioDimensions
{
    /// <summary>網羅しない組の照合の範囲</summary>
    public const string Scope = "tool-scenario";

    /// <summary>ツール</summary>
    public const string Tool = "Tool";

    /// <summary>ホスト</summary>
    public const string Host = "Host";

    /// <summary>宣言した結果（受け付け・断る）</summary>
    public const string Outcome = "Outcome";

    /// <summary>受け付ける</summary>
    public const string Accepted = "Accepted";

    /// <summary>断る</summary>
    public const string Rejected = "Rejected";

    /// <summary>次元表</summary>
    public static DimensionTable Table { get; } =
        new(
            EngineIds.DiagramEdit,
            [
                new Factor(
                    Tool,
                    ErDiagramToolCatalog
                        .GetDefinitions()
                        .Select(definition => new Level(definition.Name))
                        .ToList(),
                    "呼び出すツール（ER 図のツールの定義の正本）"
                ),
                new Factor(
                    Host,
                    Enum.GetValues<ToolHostKind>()
                        .Select(host => new Level(host.ToString(), AxisMember.Of(host)))
                        .ToList(),
                    "ツールを実行するホスト"
                ),
                new Factor(Outcome, [new Level(Accepted), new Level(Rejected)], "行が宣言した結果"),
            ],
            null,
            IsValid
        );

    /// <summary>網羅しない組の一覧</summary>
    public static CoverageGapSet Gaps { get; } = CoverageGap.Set(EngineIds.DiagramEdit, []);

    /// <summary>台帳の行</summary>
    public static EngineDescriptor Descriptor { get; } =
        new(
            EngineIds.DiagramEdit,
            "Tools",
            "ER 図のツールの決定的な行（両ホスト）",
            Table,
            ToolScenarioRows.Table.Count,
            [
                "tests/QuickER.Tests/Mcp.Tools/ToolScenario.cs",
                "tests/QuickER.Tests/Mcp.Tools/ToolScenarioDimensions.cs",
                "tests/QuickER.Tests/Mcp.Tools/ToolScenarioRows.cs",
                "tests/QuickER.Tests/Mcp.Tools/ToolScenarioTests.cs",
                "tests/QuickER.Tests/Mcp.Tools/ToolSchemaProjection.cs",
                "tests/QuickER.Tests/TestSupport/TwoHostToolHarness.cs",
            ],
            [
                "src/QuickER.Gui/Services/ErDiagramDynamicTools.cs",
                "src/QuickER.Mcp.Tools",
                "src/QuickER.AI.Chat/ErDiagramHostChatAdapter.cs",
            ]
        );

    /// <summary>行の宣言を数える（行を流さない）</summary>
    /// <param name="counter">数える先</param>
    public static void ObserveDeclarations(CoverageCounter counter)
    {
        foreach (var row in ToolScenarioRows.Table)
        {
            foreach (var call in row.Input.Calls)
            {
                foreach (var host in Enum.GetValues<ToolHostKind>())
                {
                    counter.Observe(
                        new Dictionary<string, string>(StringComparer.Ordinal)
                        {
                            [Tool] = call.Tool,
                            [Host] = host.ToString(),
                            [Outcome] = call.ExpectSuccess ? Accepted : Rejected,
                        }
                    );
                }
            }
        }
    }

    /// <summary>原理的に起こり得ない組のときだけ偽（判断に要る因子が割り当てに無ければ真）</summary>
    private static bool IsValid(IReadOnlyDictionary<string, string> assignment)
    {
        assignment.TryGetValue(Tool, out var tool);
        assignment.TryGetValue(Outcome, out var outcome);

        // get_diagram_summary と list_queries は引数を取らず、開けている図を読むだけなので断らない
        // （図を開けないときの失敗はホストのファイルの状態の検査＝DocumentErDiagramToolHostTests が持つ。表の行は図を開いて流す）
        return !(tool is "get_diagram_summary" or "list_queries" && outcome == Rejected);
    }
}
