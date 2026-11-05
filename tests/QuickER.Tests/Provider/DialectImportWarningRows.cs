using System.Globalization;
using System.Text.RegularExpressions;
using QuickER.Provider;
using QuickER.Tests.TestSupport.Engines;

namespace QuickER.Tests.Provider;

/// <summary>取込警告の表の文の種類</summary>
internal static class DialectImportWarningTexts
{
    /// <summary>箇所ごとの 1 文（<see cref="SchemaImportWarningText.Describe"/>）</summary>
    public const string Describe = "describe";

    /// <summary>種別ごとの見出し（<see cref="SchemaImportWarningText.Heading"/>）</summary>
    public const string Heading = "heading";

    /// <summary>すべての文の種類（次元表の水準の並び）</summary>
    public static IReadOnlyList<string> All { get; } = [Describe, Heading];
}

/// <summary>取込警告の表の言語</summary>
internal static class DialectImportWarningCultures
{
    /// <summary>英語</summary>
    public const string English = "en";

    /// <summary>日本語</summary>
    public const string Japanese = "ja";

    /// <summary>英語と日本語を 1 ケースで比べる（見出し）</summary>
    public const string Both = "en+ja";

    /// <summary>すべての言語（次元表の水準の並び）</summary>
    public static IReadOnlyList<string> All { get; } = [English, Japanese, Both];
}

/// <summary>取込警告の表の 1 行</summary>
/// <param name="Kind">取込警告の種別</param>
/// <param name="Text">文の種類（<see cref="DialectImportWarningTexts"/>）</param>
/// <param name="Culture">言語（<see cref="DialectImportWarningCultures"/>）</param>
/// <param name="WithDetail">警告に詳細を持たせるか（計算列は詳細の有無で文面が分かれる）</param>
internal sealed record DialectImportWarningCase(
    SchemaImportWarningKind Kind,
    string Text,
    string Culture,
    bool WithDetail
);

/// <summary>
/// 取込警告の表（全種別の文言を英日で整形できること＝埋め込み漏れ・訳漏れが無いことを、種別ごとの行で固定する）
/// </summary>
/// <remarks>
/// <para>
/// 全種別 × 英日の 1 文と、全種別の見出しを固定する。行は種別の全値から作る
/// （水準もループも同じ列挙から作るので、種別を足すと自動で行と水準が増えて流れる）。
/// </para>
/// <para>
/// 期待は次の 4 つ：空でない・書式の穴が無い・改行が無い・対象の名前を名指しする。名前だけを並べる種別
/// （<see cref="SchemaImportWarningKinds.IsAggregated"/>）は名前から始まり、それ以外は英日で文面が違う。見出しは件数を含み英日で違う。
/// </para>
/// </remarks>
internal static class DialectImportWarningRows
{
    private static readonly Regex Placeholder = new(@"\{[0-9]+\}", RegexOptions.CultureInvariant);

    /// <summary>表</summary>
    public static ScenarioTable<DialectImportWarningCase> Table { get; } =
        new(EngineIds.Dialect, AllRows());

    private static IEnumerable<ScenarioRow<DialectImportWarningCase>> AllRows()
    {
        foreach (var kind in Enum.GetValues<SchemaImportWarningKind>())
        {
            foreach (
                var culture in new[]
                {
                    DialectImportWarningCultures.English,
                    DialectImportWarningCultures.Japanese,
                }
            )
            {
                yield return Describe(kind, culture, withDetail: true);

                // 計算列は詳細（式）が無いときに別の文面へ振り分ける
                if (kind == SchemaImportWarningKind.ComputedColumnExpressionLost)
                {
                    yield return Describe(kind, culture, withDetail: false);
                }
            }

            yield return new(
                $"heading-{kind}",
                null,
                "すべての種別の見出しは件数を入れて英日で書ける",
                new DialectImportWarningCase(
                    kind,
                    DialectImportWarningTexts.Heading,
                    DialectImportWarningCultures.Both,
                    WithDetail: false
                )
            );
        }
    }

    private static ScenarioRow<DialectImportWarningCase> Describe(
        SchemaImportWarningKind kind,
        string culture,
        bool withDetail
    ) =>
        new(
            $"describe-{kind}-{culture}{(withDetail ? "" : "-no-detail")}",
            null,
            "すべての取込警告の種別を英日で 1 行に整形できる（埋め込み漏れ・訳漏れが無い）",
            new DialectImportWarningCase(
                kind,
                DialectImportWarningTexts.Describe,
                culture,
                withDetail
            )
        );

    /// <summary>行を流して照合し、問題を返す（無ければ空）</summary>
    /// <param name="row">行</param>
    public static IReadOnlyList<string> Check(DialectImportWarningCase row) =>
        row.Text == DialectImportWarningTexts.Describe ? CheckDescribe(row) : CheckHeading(row);

    private static List<string> CheckDescribe(DialectImportWarningCase row)
    {
        var problems = new List<string>();
        var warning = new SchemaImportWarning(
            row.Kind,
            "tbl_x",
            "subj_y",
            row.WithDetail ? "det_z" : ""
        );
        var other =
            row.Culture == DialectImportWarningCultures.English
                ? DialectImportWarningCultures.Japanese
                : DialectImportWarningCultures.English;
        var text = WithCulture(row.Culture, () => SchemaImportWarningText.Describe(warning));
        var otherText = WithCulture(other, () => SchemaImportWarningText.Describe(warning));

        if (string.IsNullOrWhiteSpace(text))
        {
            problems.Add("文が空");
        }

        if (Placeholder.IsMatch(text))
        {
            problems.Add($"書式の穴が埋まっていない：{text}");
        }

        if (text.Contains('\n') || text.Contains('\r'))
        {
            problems.Add($"改行がある：{text}");
        }

        if (
            !text.Contains("tbl_x", StringComparison.Ordinal)
            && !text.Contains("subj_y", StringComparison.Ordinal)
        )
        {
            problems.Add($"対象の名前を名指ししない：{text}");
        }

        if (SchemaImportWarningKinds.IsAggregated(row.Kind))
        {
            // 外した種別の項目は名前（と、あれば親・本表の名前）だけ。内容は見出しが言う
            if (!text.StartsWith("tbl_x", StringComparison.Ordinal))
            {
                problems.Add($"名前だけを並べる種別なのに名前から始まらない：{text}");
            }
        }
        else if (text == otherText)
        {
            problems.Add($"英語と日本語の文面が同じ：{text}");
        }

        return problems;
    }

    private static List<string> CheckHeading(DialectImportWarningCase row)
    {
        var problems = new List<string>();
        var en = WithCulture(
            DialectImportWarningCultures.English,
            () => SchemaImportWarningText.Heading(row.Kind, 37)
        );
        var ja = WithCulture(
            DialectImportWarningCultures.Japanese,
            () => SchemaImportWarningText.Heading(row.Kind, 37)
        );

        if (
            !en.Contains("37", StringComparison.Ordinal)
            || !ja.Contains("37", StringComparison.Ordinal)
        )
        {
            problems.Add($"件数を含まない：{en} / {ja}");
        }

        if (en == ja)
        {
            problems.Add($"英語と日本語の見出しが同じ：{en}");
        }

        if (Placeholder.IsMatch(en) || Placeholder.IsMatch(ja))
        {
            problems.Add($"書式の穴が埋まっていない：{en} / {ja}");
        }

        return problems;
    }

    /// <summary>UI の言語（<see cref="CultureInfo.CurrentUICulture"/>）を切り替えて文を作る（静的な Strings.Culture は変えない）</summary>
    private static T WithCulture<T>(string culture, Func<T> body)
    {
        var previous = CultureInfo.CurrentUICulture;

        try
        {
            CultureInfo.CurrentUICulture = new CultureInfo(culture);

            return body();
        }
        finally
        {
            CultureInfo.CurrentUICulture = previous;
        }
    }
}
