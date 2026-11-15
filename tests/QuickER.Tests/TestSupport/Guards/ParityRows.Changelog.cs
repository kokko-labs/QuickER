using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using QuickER.Tests.Docs;

namespace QuickER.Tests.TestSupport.Guards;

/// <summary>照合の表：Changelog（CHANGELOG の Unreleased の項目の書式・本文の文の数・行の字数）</summary>
/// <remarks>
/// 正本＝範囲のすべての項目（と書式の違反の行）・写し＝上限を満たす項目・比べ方 Multiset。違反した項目は「正本だけにある」として、
/// ファイル・行・要約の先頭・測った値で名指しされる（<c>workflow-test-commands-use-mtp-options</c> と同じ形）。
/// 自己検査の行は、合成の CHANGELOG で期待する違反の並び（正本）と、読み取りが返す違反の並び（写し）を比べる。
/// </remarks>
internal static partial class ParityRows
{
    private static IEnumerable<ParityRow> Changelog() =>
        [
            new(
                "changelog-unreleased-entries-within-limits",
                "CHANGELOG（英日）の Unreleased の項目は「- **要約** — 本文」の 1 行で、本文は 2 文以内、行は上限の字数に収まる"
                    + "（CONTRIBUTING の「書き方」。文章だけの規則では、区切りごとに項目を書き足すうちに 1 項目が伸び続ける）",
                ParityCompare.Multiset,
                () => ChangelogEntries.DescribeAll(ChangelogEntries.UnreleasedHeading),
                () => ChangelogEntries.DescribeWithinLimits(ChangelogEntries.UnreleasedHeading)
            )
            {
                // Unreleased はリリースの直後に空になる。読み取りが空振りしないことは証人の行と自己検査の行が示す
                MinimumCanon = 0,
            },
            new(
                "changelog-0-4-0-entries-within-limits",
                "証人の行：確定した 0.4.0 の節（英日 130 項目ずつ）を同じ読み取りで読み、上限を満たす"
                    + "（本物のファイルの書式＝「- **」と「** — 」の区切りが読み取りと合っていること、上限が実在の節で通ることを示す。"
                    + "上限を 0.4.0 より厳しくするときは、証人の節を新しい版へ移す）",
                ParityCompare.Multiset,
                () => ChangelogEntries.DescribeAll(WitnessHeading),
                () => ChangelogEntries.DescribeWithinLimits(WitnessHeading)
            )
            {
                MinimumCanon = 260,
            },
            new(
                "changelog-entry-limits-probe-ja",
                "自己検査の行：合成の日本語の CHANGELOG で、本文 3 文・Breaking 以外の 251 字・Breaking の 281 字・太字で始まらない行・"
                    + "折り返した続きの行を名指しし、2 文の項目・Breaking の 280 字・括弧とインラインのコードの中の句点・本文の無い項目・"
                    + "Unreleased の後ろの版の項目を通す",
                ParityCompare.Sequence,
                () => ChangelogEntryProbes.JapaneseExpected,
                () =>
                    ChangelogEntries.ViolationKeys(
                        ChangelogEntryProbes.JapaneseText(),
                        "probe.ja.md",
                        ChangelogEntries.UnreleasedHeading
                    )
            )
            {
                MinimumCanon = 6,
            },
            new(
                "changelog-entry-limits-probe-en",
                "自己検査の行：合成の英語の CHANGELOG で、本文 3 文・Breaking 以外の 461 字・Breaking の 521 字・太字で始まらない行・"
                    + "折り返した続きの行を名指しし、2 文の項目・Breaking の 520 字・括弧とインラインのコードの中のピリオド・"
                    + "小文字が続く略語（e.g.）と小数・本文の無い項目・Unreleased の後ろの版の項目を通す",
                ParityCompare.Sequence,
                () => ChangelogEntryProbes.EnglishExpected,
                () =>
                    ChangelogEntries.ViolationKeys(
                        ChangelogEntryProbes.EnglishText(),
                        "probe.md",
                        ChangelogEntries.UnreleasedHeading
                    )
            )
            {
                MinimumCanon = 6,
            },
            new(
                "changelog-entry-limits-probe-empty-unreleased",
                "自己検査の行：空の Unreleased（種類の見出しだけのものも）は通し、すぐ下の版の節の長い項目・3 文の項目・太字で始まらない行を数えない",
                ParityCompare.Sequence,
                () => [],
                () =>
                    ChangelogEntries.ViolationKeys(
                        ChangelogEntryProbes.EmptyUnreleasedText(),
                        "probe.ja.md",
                        ChangelogEntries.UnreleasedHeading
                    )
            )
            {
                MinimumCanon = 0,
            },
        ];

    /// <summary>証人の節（確定した履歴で動かない）</summary>
    private const string WitnessHeading = "0.4.0";
}

/// <summary>CHANGELOG の節の項目を読み、書式・本文の文の数・行の字数を上限と照らす</summary>
/// <remarks>
/// 主な歯止めは本文の文の数（2 文まで）で、行の字数の上限は膨らんだ外れ値を止めるもの。字数の上限は、確定した 0.4.0 の節
/// （日本語の最大は Breaking 271 字・ほか 246 字、英語は 468 字・457 字）が通る値から決めた。Breaking は移行の文を必ず入れるので
/// ほかより 1 句長い上限を持つ。
/// </remarks>
internal static class ChangelogEntries
{
    /// <summary>本文の文の数の上限</summary>
    internal const int MaxSentences = 2;

    /// <summary>日本語の行の字数の上限（Breaking 以外）</summary>
    internal const int JapaneseLineLimit = 250;

    /// <summary>日本語の行の字数の上限（Breaking changes）</summary>
    internal const int JapaneseBreakingLineLimit = 280;

    /// <summary>英語の行の字数の上限（Breaking 以外）</summary>
    internal const int EnglishLineLimit = 460;

    /// <summary>英語の行の字数の上限（Breaking changes）</summary>
    internal const int EnglishBreakingLineLimit = 520;

    /// <summary>Unreleased の節の見出しの名前</summary>
    internal const string UnreleasedHeading = "Unreleased";

    private const string BreakingSection = "Breaking changes";
    private const string ItemPrefix = "- **";
    private const string BodySeparator = "** — ";
    private const int SummaryHeadLength = 40;

    /// <summary>読む CHANGELOG（英日）</summary>
    private static readonly string[] Files = ["CHANGELOG.md", "CHANGELOG.ja.md"];

    private static readonly Regex InlineCode = new("`[^`]*`", RegexOptions.CultureInvariant);
    private static readonly Regex FullWidthParentheses = new(
        "（[^（）]*）",
        RegexOptions.CultureInvariant
    );
    private static readonly Regex HalfWidthParentheses = new(
        @"\([^()]*\)",
        RegexOptions.CultureInvariant
    );

    /// <summary>英語の文の区切り：文末の記号の後の空白で、続く文字が大文字・バッククォート・引用符・開き括弧のとき</summary>
    private static readonly Regex EnglishSentenceBoundary = new(
        @"(?<=[.!?])\s+(?=[A-Z`""'(])",
        RegexOptions.CultureInvariant
    );

    /// <summary>英日の CHANGELOG の節のすべての項目と書式の違反の行の名指し（照合の正本）</summary>
    internal static IReadOnlyList<string> DescribeAll(string heading) =>
        [.. Read(heading).Select(entry => entry.Describe())];

    /// <summary>英日の CHANGELOG の節の、上限を満たす項目の名指し（照合の写し）</summary>
    internal static IReadOnlyList<string> DescribeWithinLimits(string heading) =>
        [
            .. Read(heading)
                .Where(entry => entry.Problems.Count == 0)
                .Select(entry => entry.Describe()),
        ];

    /// <summary>合成の CHANGELOG の違反を「行:種類」の並びで返す（自己検査の写し）</summary>
    internal static IReadOnlyList<string> ViolationKeys(string text, string file, string heading) =>
        [
            .. Parse(text, file, heading)
                .Where(entry => entry.Problems.Count > 0)
                .Select(entry => $"{entry.Line}:{string.Join("+", entry.Problems)}"),
        ];

    private static IEnumerable<ChangelogEntry> Read(string heading) =>
        Files.SelectMany(file =>
            Parse(
                File.ReadAllText(
                    Path.Combine(LicenseDocumentParityTests.Root, file),
                    Encoding.UTF8
                ),
                file,
                heading
            )
        );

    /// <summary>節（<c>## [見出し]</c> の次の行から、次の <c>## [</c> の前まで）の項目と書式の違反の行を読む</summary>
    /// <remarks>
    /// 項目は種類の見出し（<c>### </c>）の下の <c>- **</c> で始まる行。種類の見出しの下で、空行・見出し以外の行が <c>- **</c> で
    /// 始まらなければ書式の違反（太字で始めない項目・2 行目へ折り返した続きの行がガードを素通りしないため）。要点（Highlights）の
    /// 箇条は最初の種類の見出しの前にあるので読まない。節の見出しが無ければ例外（読み取りの失敗を空の節として通さない）。
    /// </remarks>
    internal static IReadOnlyList<ChangelogEntry> Parse(string text, string file, string heading)
    {
        var lines = text.ReplaceLineEndings("\n").Split('\n');
        var start = Array.FindIndex(
            lines,
            line => line.StartsWith($"## [{heading}]", StringComparison.Ordinal)
        );

        if (start < 0)
        {
            throw new InvalidOperationException($"{file} に節「## [{heading}]」が見つからない");
        }

        var japanese = file.EndsWith(".ja.md", StringComparison.OrdinalIgnoreCase);
        var entries = new List<ChangelogEntry>();
        string? section = null;

        for (var index = start + 1; index < lines.Length; index++)
        {
            var line = lines[index];

            if (line.StartsWith("## [", StringComparison.Ordinal))
            {
                break;
            }

            if (line.StartsWith("### ", StringComparison.Ordinal))
            {
                section = line[4..].Trim();
                continue;
            }

            if (section is null || line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            entries.Add(Measure(file, index + 1, line, section == BreakingSection, japanese));
        }

        return entries;
    }

    private static ChangelogEntry Measure(
        string file,
        int lineNumber,
        string line,
        bool breaking,
        bool japanese
    )
    {
        if (!line.StartsWith(ItemPrefix, StringComparison.Ordinal))
        {
            return new(file, lineNumber, Head(line), 0, line.Length, 0, ["format"]);
        }

        var separator = line.IndexOf(BodySeparator, ItemPrefix.Length, StringComparison.Ordinal);
        var summary =
            separator < 0
                ? line[ItemPrefix.Length..].TrimEnd('*')
                : line[ItemPrefix.Length..separator];
        var body = separator < 0 ? "" : line[(separator + BodySeparator.Length)..];
        var sentences = japanese ? JapaneseSentences(body) : EnglishSentences(body);
        var limit = (japanese, breaking) switch
        {
            (true, true) => JapaneseBreakingLineLimit,
            (true, false) => JapaneseLineLimit,
            (false, true) => EnglishBreakingLineLimit,
            (false, false) => EnglishLineLimit,
        };
        var problems = new List<string>();

        if (sentences > MaxSentences)
        {
            problems.Add("sentences");
        }

        if (line.Length > limit)
        {
            problems.Add("length");
        }

        return new(file, lineNumber, Head(summary), sentences, line.Length, limit, problems);
    }

    /// <summary>日本語の本文の文の数（インラインのコードと括弧の中を除き、「。」で区切った空でない部分の数）</summary>
    internal static int JapaneseSentences(string body) =>
        StripAsides(body).Split('。').Count(part => !string.IsNullOrWhiteSpace(part));

    /// <summary>英語の本文の文の数（インラインのコードと括弧の中を除き、文の区切りで分けた空でない部分の数）</summary>
    internal static int EnglishSentences(string body) =>
        EnglishSentenceBoundary
            .Split(StripAsides(body))
            .Count(part => !string.IsNullOrWhiteSpace(part));

    /// <summary>インラインのコードと括弧（全角・半角。入れ子は内側から 3 段まで）の中を除く</summary>
    private static string StripAsides(string body)
    {
        var text = InlineCode.Replace(body, "");

        for (var depth = 0; depth < 3; depth++)
        {
            text = FullWidthParentheses.Replace(text, "");
            text = HalfWidthParentheses.Replace(text, "");
        }

        return text;
    }

    private static string Head(string text) =>
        text.Length <= SummaryHeadLength ? text : text[..SummaryHeadLength] + "…";
}

/// <summary>CHANGELOG の項目 1 つ（または書式の違反の行 1 つ）</summary>
/// <param name="File">ファイル</param>
/// <param name="Line">行（1 始まり）</param>
/// <param name="Summary">要約の先頭（書式の違反なら行の先頭）</param>
/// <param name="Sentences">本文の文の数</param>
/// <param name="Length">行の字数</param>
/// <param name="Limit">行の字数の上限（書式の違反なら 0）</param>
/// <param name="Problems">違反の種類（sentences・length・format）</param>
internal sealed record ChangelogEntry(
    string File,
    int Line,
    string Summary,
    int Sentences,
    int Length,
    int Limit,
    IReadOnlyList<string> Problems
)
{
    /// <summary>照合の報告に出す名指し</summary>
    public string Describe() =>
        Problems.Contains("format")
            ? $"{File}:{Line} 書式の違反（種類の見出しの下の行が「- **」で始まらない）「{Summary}」"
            : $"{File}:{Line}「{Summary}」本文 {Sentences} 文・行 {Length} 字（上限 {Limit}）";
}

/// <summary>CHANGELOG の長さのガードの自己検査の合成の入力</summary>
/// <remarks>
/// 行の長さは上限の定数から作らず数値で書く（定数から作ると、上限を緩めたときに入力も一緒に伸びて赤にならない）。
/// 上限を変えるときは、ここの数値と期待の並びも一緒に直す。
/// </remarks>
internal static class ChangelogEntryProbes
{
    /// <summary>日本語の合成の入力で期待する違反（行:種類）</summary>
    internal static readonly string[] JapaneseExpected =
    [
        "11:length",
        "15:sentences",
        "16:length",
        "19:format",
        "20:format",
        "21:sentences+length",
    ];

    /// <summary>英語の合成の入力で期待する違反（行:種類）</summary>
    internal static readonly string[] EnglishExpected =
    [
        "11:length",
        "15:sentences",
        "16:length",
        "20:format",
        "21:format",
        "22:sentences+length",
    ];

    /// <summary>日本語の合成の CHANGELOG（行の番号は期待の並びと対）</summary>
    internal static string JapaneseText() =>
        Lines(
            "# 変更履歴",
            "",
            "## [Unreleased]",
            "",
            "### Breaking changes",
            "",
            "#### 生成コード",
            "",
            "- **2 文の項目** — 一文目です。二文目です",
            Padded("- **Breaking の 280 字** — 移行の文です。", 280, 'あ'),
            Padded("- **Breaking の 281 字** — 移行の文です。", 281, 'あ'),
            "",
            "### Fixed",
            "",
            "- **3 文の項目** — 一つ目。二つ目。三つ目",
            Padded("- **251 字の項目** — 症状です。", 251, 'い'),
            "- **括弧とコードの句点** — 一文目（括弧の中。句点）です。`a。b` を使う二文目です",
            "- **本文の無い項目**",
            "- 太字で始まらない項目",
            "  2 行目へ折り返した続きの行",
            Padded("- **2 つの違反** — 一。二。三。", 251, 'う'),
            "",
            "## [0.4.0] - 2026-11-16",
            "",
            "### Fixed",
            "",
            Padded("- **版の節の項目** — 一。二。三。", 260, 'え')
        );

    /// <summary>英語の合成の CHANGELOG（行の番号は期待の並びと対）</summary>
    internal static string EnglishText() =>
        Lines(
            "# Changelog",
            "",
            "## [Unreleased]",
            "",
            "### Breaking changes",
            "",
            "#### Generated code",
            "",
            "- **Two sentences** — the first one. The second one",
            Padded("- **Breaking at 520** — migrate like this. ", 520, 'a'),
            Padded("- **Breaking at 521** — migrate like this. ", 521, 'a'),
            "",
            "### Fixed",
            "",
            "- **Three sentences** — one. Two. Three",
            Padded("- **Line of 461** — the symptom. ", 461, 'b'),
            "- **Asides** — a value (see the note. It is long) and `a. B` stay one sentence. The second one",
            "- **Abbreviations** — compare values, e.g. the 1.5 case, as C# does. The second one",
            "- **No body**",
            "- A plain entry without bold",
            "  a line wrapped onto a second line",
            Padded("- **Two problems** — one. Two. Three. ", 461, 'c'),
            "",
            "## [0.4.0] - 2026-11-16",
            "",
            "### Fixed",
            "",
            Padded("- **Released entry** — one. Two. Three. ", 470, 'd')
        );

    /// <summary>空の Unreleased（種類の見出しだけ）と、すぐ下の版の節に長い項目・3 文の項目・太字で始まらない行を置いた入力</summary>
    internal static string EmptyUnreleasedText() =>
        Lines(
            "# 変更履歴",
            "",
            "## [Unreleased]",
            "",
            "### Fixed",
            "",
            "## [0.4.0] - 2026-11-16",
            "",
            "### Fixed",
            "",
            Padded("- **版の節の長い項目** — 症状です。", 260, 'お'),
            "- **版の節の 3 文の項目** — 一。二。三",
            "- 版の節の太字で始まらない行"
        );

    private static string Lines(params string[] lines) => string.Join("\n", lines);

    /// <summary>行の字数がちょうど <paramref name="length"/> になるまで <paramref name="fill"/> で埋める</summary>
    private static string Padded(string prefix, int length, char fill) =>
        prefix.Length >= length
            ? throw new ArgumentException($"合成の行の前置きが長すぎる: {prefix}", nameof(prefix))
            : prefix + new string(fill, length - prefix.Length);
}
