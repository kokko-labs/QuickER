using QuickER.Model;
using QuickER.Services;
using QuickER.Tests.TestSupport.Engines;

namespace QuickER.Tests.Gui.Services.FormatRoundTrip;

/// <summary>形式の往復の次元表・網羅しない組の一覧・台帳の行</summary>
/// <remarks>
/// <para>
/// 形の行の部分（RoundTrip）は「形式 × 図の特徴」。図の特徴は行の図の中身から機械で決め（<see cref="FormatShapeFeatures"/>）、
/// 1 ケース（形の行 × 形式）は図にある特徴ごとに 1 回数える（観測の単位＝形式 × 図の特徴）。
/// 形の行は全形式で流れるので、元のテストが 1 形式で持っていた特徴も全形式で網羅される。
/// </para>
/// <para>
/// 告知の部分（Omissions）は「告知する形式 × 告知の種類」。形の行のケースで告知された種類ごとに 1 回数える。
/// 規則の部分（Rules）は「取込のできる形式 × 規則の種類（受理・拒否・警告）」。規則の行ごとに 1 回数える。
/// どれも種を持たないので、網羅しない組は毎回照合する。
/// </para>
/// </remarks>
internal static class FormatRoundTripDimensions
{
    /// <summary>網羅しない組の照合の範囲</summary>
    public const string Scope = "format-round-trip";

    /// <summary>形式</summary>
    public const string Format = "Format";

    /// <summary>図の特徴</summary>
    public const string Feature = "Feature";

    /// <summary>告知の種類</summary>
    public const string Omission = "Omission";

    /// <summary>規則の種類</summary>
    public const string RuleKind = "RuleKind";

    /// <summary>形の行の次元表（形式 × 図の特徴）</summary>
    public static DimensionTable RoundTripTable { get; } =
        new(
            EngineIds.FormatRoundTrip,
            [
                new Factor(
                    Format,
                    FormatAxis.Axis.Adapters.Select(a => new Level(a.AxisValue)).ToList(),
                    "形式（FormatAxis の正本の値のうちアダプタを持つもの）"
                ),
                new Factor(
                    Feature,
                    FormatShapeFeatures.All.Select(f => new Level(f.Name, f.Source)).ToList(),
                    "図の特徴（図の中身から機械で決める・FormatShapeFeatures）"
                ),
            ]
        );

    /// <summary>告知の次元表（告知する形式 × 告知の種類）</summary>
    public static DimensionTable OmissionTable { get; } =
        new(
            EngineIds.FormatRoundTrip,
            [
                new Factor(
                    Format,
                    NotifyingAdapters().Select(a => new Level(a.AxisValue)).ToList(),
                    "告知する形式（ReportableOmissions が空でないアダプタ）"
                ),
                new Factor(
                    Omission,
                    Enum.GetValues<ExportOmissionKind>()
                        .Select(kind => new Level(kind.ToString(), AxisMember.Of(kind)))
                        .ToList(),
                    "告知の種類（ExportOmissionKind の全値）"
                ),
            ],
            null,
            IsValidOmission
        );

    /// <summary>規則の次元表（取込のできる形式 × 規則の種類）</summary>
    public static DimensionTable RuleTable { get; } =
        new(
            EngineIds.FormatRoundTrip,
            [
                new Factor(
                    Format,
                    FormatAxis.ImportableNames.Select(name => new Level(name)).ToList(),
                    "取込のできる形式"
                ),
                new Factor(
                    RuleKind,
                    Enum.GetValues<FormatRuleKind>().Select(k => new Level(k.ToString())).ToList(),
                    "規則の種類（受理・拒否・警告）"
                ),
            ],
            null,
            IsValidRule
        );

    /// <summary>マージの経路</summary>
    public const string MergePath = "MergePath";

    /// <summary>取込の図の出どころ</summary>
    public const string MergeSource = "MergeSource";

    /// <summary>マージの次元表（経路 × 取込の図の出どころ）</summary>
    public static DimensionTable MergeTable { get; } =
        new(
            EngineIds.FormatRoundTrip,
            [
                new Factor(
                    MergePath,
                    Enum.GetValues<FormatMergePath>().Select(p => new Level(p.ToString())).ToList(),
                    "マージの経路（Id の引き継ぎだけ・Excel 定義書のマージ取込・C# リバースのマージ取込）"
                ),
                new Factor(
                    MergeSource,
                    Enum.GetValues<FormatMergeSource>()
                        .Select(s => new Level(s.ToString()))
                        .ToList(),
                    "取込の図の出どころ（図・形式の固有の入力）"
                ),
            ],
            null,
            IsValidMerge
        );

    /// <summary>マージの網羅しない組の一覧</summary>
    public static CoverageGapSet MergeGaps { get; } =
        CoverageGap.Set(EngineIds.FormatRoundTrip, FormatShapeFeatures.MergeGaps);

    /// <summary>形の行の網羅しない組の一覧</summary>
    public static CoverageGapSet RoundTripGaps { get; } =
        CoverageGap.Set(EngineIds.FormatRoundTrip, FormatShapeFeatures.Gaps);

    /// <summary>告知の網羅しない組の一覧</summary>
    public static CoverageGapSet OmissionGaps { get; } =
        CoverageGap.Set(EngineIds.FormatRoundTrip, FormatShapeFeatures.OmissionGaps);

    /// <summary>規則の網羅しない組の一覧</summary>
    public static CoverageGapSet RuleGaps { get; } =
        CoverageGap.Set(EngineIds.FormatRoundTrip, FormatShapeFeatures.RuleGaps);

    private static readonly string[] SourceFiles =
    [
        "tests/QuickER.Tests/Gui/Services/FormatRoundTrip/FormatAdapters.cs",
        "tests/QuickER.Tests/Gui/Services/FormatRoundTrip/FormatFacet.cs",
        "tests/QuickER.Tests/Gui/Services/FormatRoundTrip/FormatSchemaProjection.cs",
        "tests/QuickER.Tests/Gui/Services/FormatRoundTrip/FormatRoundTripRunner.cs",
        "tests/QuickER.Tests/Gui/Services/FormatRoundTrip/FormatRoundTripTests.cs",
        "tests/QuickER.Tests/Gui/Services/FormatRoundTrip/FormatRoundTripCaseTests.cs",
        "tests/QuickER.Tests/Gui/Services/FormatRoundTrip/FormatRoundTripDimensions.cs",
    ];

    private static readonly string[] Covers =
    [
        "src/QuickER.Gui/Services/DbmlExporter.cs",
        "src/QuickER.Gui/Services/DbmlImporter.cs",
        "src/QuickER.Gui/Services/DbmlLiteral.cs",
        "src/QuickER.Gui/Services/MermaidExporter.cs",
        "src/QuickER.Gui/Services/MermaidImporter.cs",
        "src/QuickER.Gui/Services/ExportTextSanitizer.cs",
        "src/QuickER.Gui/Services/TableDefinitionDocumentExporter.cs",
        "src/QuickER.Gui/Services/TableDefinitionDocumentImporter.cs",
        "src/QuickER.Gui/Services/TableDefinitionHtmlExporter.cs",
        "src/QuickER.Gui/Services/TableDefinitionMergePostProcessor.cs",
        "src/QuickER.CodeReverse.CSharp",
        "src/QuickER.Provider/DiagramMergeReconciler.cs",
    ];

    /// <summary>台帳の行（形の行）</summary>
    public static EngineDescriptor RoundTripDescriptor { get; } =
        new(
            EngineIds.FormatRoundTrip,
            "RoundTrip",
            "形の行 × 形式の往復・告知・注入・マージの共通の表明とカルチャの代表の形",
            RoundTripTable,
            FormatShapeRows.Table.Count * FormatAxis.Axis.Adapters.Count,
            [.. SourceFiles, "tests/QuickER.Tests/Gui/Services/FormatRoundTrip/FormatShapeRows.cs"],
            Covers
        );

    /// <summary>台帳の行（告知）</summary>
    public static EngineDescriptor OmissionDescriptor { get; } =
        new(
            EngineIds.FormatRoundTrip,
            "Omissions",
            "告知する形式 × 告知の種類（形の行のケースで告知された種類）",
            OmissionTable,
            FormatShapeRows.Table.Count * NotifyingAdapters().Count,
            [.. SourceFiles, "tests/QuickER.Tests/Gui/Services/FormatRoundTrip/FormatShapeRows.cs"],
            Covers
        );

    /// <summary>台帳の行（規則の行）</summary>
    public static EngineDescriptor RuleDescriptor { get; } =
        new(
            EngineIds.FormatRoundTrip,
            "Rules",
            "形式の規則の行（取込の受理・拒否・警告と不動点）",
            RuleTable,
            FormatRuleRows.Table.Count,
            [.. SourceFiles, "tests/QuickER.Tests/Gui/Services/FormatRoundTrip/FormatRuleRows.cs"],
            Covers
        );

    /// <summary>台帳の行（マージの行）</summary>
    public static EngineDescriptor MergeDescriptor { get; } =
        new(
            EngineIds.FormatRoundTrip,
            "Merge",
            "マージの行（現在の図と取込の図 → マージ後の図・Id の引き継ぎ・クエリ）と、形の行のマージで戻る面の表明",
            MergeTable,
            FormatMergeRows.Table.Count,
            [
                .. SourceFiles,
                "tests/QuickER.Tests/Gui/Services/FormatRoundTrip/FormatMerge.cs",
                "tests/QuickER.Tests/Gui/Services/FormatRoundTrip/FormatMergeRows.cs",
            ],
            Covers
        );

    /// <summary>マージの行を数える</summary>
    public static void ObserveMerge(CoverageCounter counter, FormatMerge merge) =>
        counter.Observe(
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [MergePath] = merge.Path.ToString(),
                [MergeSource] = merge.Source.ToString(),
            }
        );

    /// <summary>告知する形式</summary>
    public static IReadOnlyList<FormatAdapter> NotifyingAdapters() =>
        FormatAxis.Axis.Adapters.Where(a => a.ReportableOmissions.Count > 0).ToList();

    /// <summary>形の行 × 形式の 1 ケースを数える（図にある特徴ごとに 1 回）</summary>
    public static void ObserveShape(CoverageCounter counter, string format, ErDiagram diagram)
    {
        foreach (var feature in FormatShapeFeatures.Of(diagram))
        {
            counter.Observe(
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    [Format] = format,
                    [Feature] = feature,
                }
            );
        }
    }

    /// <summary>告知された種類を数える</summary>
    public static void ObserveOmissions(
        CoverageCounter counter,
        string format,
        IEnumerable<ExportOmissionKind> kinds
    )
    {
        foreach (var kind in kinds)
        {
            counter.Observe(
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    [Format] = format,
                    [Omission] = kind.ToString(),
                }
            );
        }
    }

    /// <summary>規則の行を数える</summary>
    public static void ObserveRule(CoverageCounter counter, string format, FormatRuleKind kind) =>
        counter.Observe(
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [Format] = format,
                [RuleKind] = kind.ToString(),
            }
        );

    /// <summary>原理的に起こり得ない組のときだけ偽（判断に要る因子が割り当てに無ければ真）</summary>
    private static bool IsValidMerge(IReadOnlyDictionary<string, string> assignment)
    {
        // Id の引き継ぎだけの経路は形式を通らないので、形式の固有の入力を持てない
        if (
            assignment.TryGetValue(MergePath, out var path)
            && assignment.TryGetValue(MergeSource, out var source)
        )
        {
            return !(
                path == nameof(FormatMergePath.Reconcile)
                && source == nameof(FormatMergeSource.Input)
            );
        }

        return true;
    }

    /// <summary>原理的に起こり得ない組のときだけ偽（判断に要る因子が割り当てに無ければ真）</summary>
    private static bool IsValidRule(IReadOnlyDictionary<string, string> assignment)
    {
        // 取込の結果に警告の口を持たない形式（DBML・Mermaid・Excel の取込は図だけを返す）は、警告の規則を持てない
        if (
            assignment.TryGetValue(Format, out var format)
            && assignment.TryGetValue(RuleKind, out var kind)
            && kind == nameof(FormatRuleKind.Warn)
        )
        {
            return FormatAxis.Axis.Get(format).ReportsImportWarnings;
        }

        return true;
    }

    /// <summary>原理的に起こり得ない組のときだけ偽（判断に要る因子が割り当てに無ければ真）</summary>
    private static bool IsValidOmission(IReadOnlyDictionary<string, string> assignment)
    {
        // その形式が告知しえない種類（構文で運ぶ情報）は告知されない：DetectOmissions はその種類を返す枝を持たない
        if (
            assignment.TryGetValue(Format, out var format)
            && assignment.TryGetValue(Omission, out var omission)
        )
        {
            return FormatAxis
                .Axis.Get(format)
                .ReportableOmissions.Contains(Enum.Parse<ExportOmissionKind>(omission));
        }

        return true;
    }
}
