using QuickER.Provider;
using QuickER.Tests.TestSupport.Engines;

namespace QuickER.Tests.Provider;

/// <summary>取込警告の表の次元表・網羅しない組の一覧・台帳の行</summary>
/// <remarks>
/// 1 ケース＝表の 1 行。種別の水準は <see cref="SchemaImportWarningKind"/> の全値で、因子の棚卸しの Source になる
/// （行も同じ列挙から作るので、種別を足すと水準と行が同時に増える）。種を持たないので、網羅しない組は毎回照合する。
/// </remarks>
internal static class DialectImportWarningDimensions
{
    /// <summary>網羅しない組の照合の範囲</summary>
    public const string Scope = "dialect-import-warnings";

    /// <summary>取込警告の種別</summary>
    public const string Kind = "Kind";

    /// <summary>文の種類</summary>
    public const string Text = "Text";

    /// <summary>言語</summary>
    public const string Culture = "Culture";

    /// <summary>次元表</summary>
    public static DimensionTable Table { get; } =
        new(
            EngineIds.Dialect,
            [
                new Factor(
                    Kind,
                    Enum.GetValues<SchemaImportWarningKind>()
                        .Select(value => new Level(value.ToString(), AxisMember.Of(value)))
                        .ToList(),
                    "取込警告の種別"
                ),
                new Factor(
                    Text,
                    DialectImportWarningTexts.All.Select(name => new Level(name)).ToList(),
                    "文の種類"
                ),
                new Factor(
                    Culture,
                    DialectImportWarningCultures.All.Select(name => new Level(name)).ToList(),
                    "言語"
                ),
            ],
            [new FactorGroup([Kind, Text, Culture])],
            IsValid
        );

    /// <summary>網羅しない組の一覧（無い）</summary>
    public static CoverageGapSet Gaps { get; } = CoverageGap.Set(EngineIds.Dialect, []);

    /// <summary>台帳の行</summary>
    public static EngineDescriptor Descriptor { get; } =
        new(
            EngineIds.Dialect,
            "ImportWarnings",
            "取込警告の表（全種別の文言を英日で整形できる・見出しは件数を含む）",
            Table,
            0,
            [
                "tests/QuickER.Tests/Provider/DialectImportWarningRows.cs",
                "tests/QuickER.Tests/Provider/DialectImportWarningDimensions.cs",
                "tests/QuickER.Tests/Provider/DialectImportWarningTableTests.cs",
            ],
            ["src/QuickER.Provider"]
        );

    /// <summary>1 ケースを数える</summary>
    /// <param name="counter">計数</param>
    /// <param name="row">行</param>
    public static void Observe(CoverageCounter counter, DialectImportWarningCase row) =>
        counter.Observe(
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [Kind] = row.Kind.ToString(),
                [Text] = row.Text,
                [Culture] = row.Culture,
            }
        );

    /// <summary>原理的に起こり得ない組み合わせのときだけ偽（判断に要る因子が割り当てに無ければ真）</summary>
    private static bool IsValid(IReadOnlyDictionary<string, string> assignment)
    {
        assignment.TryGetValue(Text, out var text);
        assignment.TryGetValue(Culture, out var culture);

        // 見出しは英日を 1 ケースで比べ（英日で違うことを見る）、1 文は言語ごとに 1 ケース（他方の言語と比べる）
        if (text is not null && culture is not null)
        {
            return (text == DialectImportWarningTexts.Heading)
                == (culture == DialectImportWarningCultures.Both);
        }

        return true;
    }
}
