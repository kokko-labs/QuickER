using QuickER.Model;
using QuickER.Tests.GeneratedFixture;
using QuickER.Tests.TestSupport.Engines;

namespace QuickER.Tests.Gui.Services.FormatRoundTrip;

/// <summary>形の行の表（全形式で共通の表明を流す図の形）</summary>
/// <remarks>主題ごとの行は <c>FormatShapeRows.{主題}.cs</c> の private メソッドが返し、<see cref="AllRows"/> に足す。</remarks>
internal static partial class FormatShapeRows
{
    /// <summary>形の行の表</summary>
    public static ScenarioTable<FormatShape> Table { get; } =
        // 行の期待が resx から組む文（断り方・警告）を、ケースと同じ UI のカルチャで組む（FormatUiCulture）
        FormatUiCulture.Run(() =>
            new ScenarioTable<FormatShape>(EngineIds.FormatRoundTrip, AllRows())
        );

    private static IEnumerable<ScenarioRow<FormatShape>> AllRows() =>
        [
            .. Seed(),
            .. DbmlRows(),
            .. MermaidRows(),
            .. TableDefinitionRows(),
            .. CSharpReverseRows(),
            .. InjectionRows(),
        ];

    /// <summary>形の行を始める</summary>
    private static FormatShapeBuilder Shape(string id, string description) => new(id, description);

    private static IEnumerable<ScenarioRow<FormatShape>> Seed() =>
        [
            Shape(
                    "fixture-generated",
                    "生成コードのフィクスチャ図（SQL Server の型の全カバレッジ）"
                )
                .Build(GeneratedFixtureDefinition.Build)
                .CultureProbe(),
            // CultureInvariantExportTests（もう 1 つの図 GeneratedFixture は上の行が流す）
            Shape(
                    "culture-turkish-i",
                    "Mermaid・DBML の出力／HTML と Excel の定義書／Mermaid・DBML・Excel の取込の結果は、書式のカルチャに依らずインバリアントと同じ（TurkishI の図）"
                )
                .Build(TurkishIDiagram)
                .CultureProbe(),
        ];
}
