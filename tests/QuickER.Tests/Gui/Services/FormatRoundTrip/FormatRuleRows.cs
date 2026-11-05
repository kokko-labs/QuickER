using System.Text.RegularExpressions;
using QuickER.Model;
using QuickER.Provider.SqlServer;
using QuickER.Services;
using QuickER.Tests.TestSupport.Engines;

namespace QuickER.Tests.Gui.Services.FormatRoundTrip;

/// <summary>規則の行の表（形式の固有の入力の取込の規則）</summary>
/// <remarks>主題ごとの行は <c>FormatRuleRows.{形式}.cs</c> の private メソッドが返し、<see cref="AllRows"/> に足す。</remarks>
internal static partial class FormatRuleRows
{
    /// <summary>規則の行の表</summary>
    public static ScenarioTable<FormatRule> Table { get; } =
        // 行の期待が resx から組む文（断り方・警告）を、ケースと同じ UI のカルチャで組む（FormatUiCulture）
        FormatUiCulture.Run(() =>
            new ScenarioTable<FormatRule>(EngineIds.FormatRoundTrip, AllRows())
        );

    private static IEnumerable<ScenarioRow<FormatRule>> AllRows() =>
        [.. Dbml(), .. Mermaid(), .. Excel(), .. CSharpReverse()];

    /// <summary>DBML の規則の行を始める</summary>
    private static FormatRuleBuilder DbmlRule(string id, string description) =>
        new(nameof(DiagramExportFormat.Dbml), id, description);

    /// <summary>Mermaid の規則の行を始める</summary>
    private static FormatRuleBuilder MermaidRule(string id, string description) =>
        new(nameof(DiagramExportFormat.Mermaid), id, description);

    /// <summary>行を LF でつなぐ</summary>
    private static string Lf(params string[] lines) => string.Join("\n", lines);

    /// <summary>行を <see cref="Environment.NewLine"/> でつなぐ</summary>
    private static string Nl(params string[] lines) => string.Join(Environment.NewLine, lines);

    /// <summary>
    /// 取り込んだ図の SQL Server の DDL が制約名を持ち、それがすべて違う（大文字小文字を無視。
    /// 元の <c>ImportedConstraintNameAssertions.ShouldHaveDistinctDdlConstraintNames</c> と同じ）
    /// </summary>
    private static (string, Func<ErDiagram, bool>) DistinctDdlConstraintNames() =>
        (
            "SQL Server の DDL の制約名が空でなく、大文字小文字を無視してすべて違う",
            diagram =>
            {
                var names = Regex
                    .Matches(new SqlServerDdlGenerator().Build(diagram), @"CONSTRAINT \[([^\]]+)\]")
                    .Select(match => match.Groups[1].Value.ToUpperInvariant())
                    .ToList();

                return names.Count > 0
                    && names.Distinct(StringComparer.Ordinal).Count() == names.Count;
            }
        );

    /// <summary>取込が断る例外の「型名: メッセージ」のパターン（取込が断る例外の型は InvalidDataException）</summary>
    private static string InvalidData(string message) => "InvalidDataException: " + message;
}
