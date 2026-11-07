using AwesomeAssertions;
using QuickER.Tests.TestSupport.Engines;

namespace QuickER.Tests.TestSupport.Guards;

/// <summary>英語の文言の表と必須の文言の表を当てる・判定の自己検査・表の整合・網羅の照合</summary>
public sealed class PhraseTests
{
    /// <summary>英語の文言の行の Id</summary>
    public static TheoryData<string> EnglishIds => [.. EnglishTextRows.All.Select(row => row.Id)];

    /// <summary>必須の文言の行の Id</summary>
    public static TheoryData<string> PhraseIds => [.. PhraseRows.All.Select(row => row.Id)];

    /// <summary>判定の自己検査の行（種類・本文・含む語〔末尾の種類は末尾にあるべき文 1 つ〕・違反があるか）</summary>
    internal static readonly (
        string Kind,
        string Text,
        string[] Contains,
        bool Violates
    )[] JudgeProbes =
    [
        (GuardTextDimensions.English, "plain English text", [], false),
        // CJK の 1 文字（数値から作る＝このファイルに実文字を置かない）
        (GuardTextDimensions.English, "text " + (char)0x65E5 + " mixed", [], true),
        (
            GuardTextDimensions.Phrase,
            "Respond in the language of the user.",
            ["language of the user"],
            false
        ),
        (GuardTextDimensions.Phrase, "Respond in English.", ["language of the user"], true),
        (
            GuardTextDimensions.PhraseSuffix,
            "Use the tools.\n\n# Response language\nDefault to English.",
            ["# Response language\nDefault to English."],
            false
        ),
        // 含んではいるが末尾ではない（後ろに別の節が続く）
        (
            GuardTextDimensions.PhraseSuffix,
            "# Response language\nDefault to English.\n\nUse the tools.",
            ["# Response language\nDefault to English."],
            true
        ),
    ];

    /// <summary>判定の自己検査の行の番号</summary>
    public static TheoryData<int> JudgeProbeIndexes => [.. Enumerable.Range(0, JudgeProbes.Length)];

    [Theory(
        DisplayName = "英語の文言の表：出どころの文字列に CJK が無い（例外は理由つきの一覧と件数まで一致）"
    )]
    [MemberData(nameof(EnglishIds))]
    public void English_Holds(string id)
    {
        var row = EnglishTextRows.ById(id);
        var texts = row.Texts().ToList();

        texts
            .Count.Should()
            .BeGreaterThanOrEqualTo(
                row.MinimumTexts,
                $"[{row.Id}] 走査する文字列が少なすぎる（出どころを読めずに空振りしていないか）"
            );

        var found = texts
            .Where(text => CjkText.Pattern.IsMatch(text.Text))
            .GroupBy(text => text.Where, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
        var samples = texts
            .Where(text => CjkText.Pattern.IsMatch(text.Text))
            .ToLookup(text => text.Where, text => text.Text, StringComparer.Ordinal);

        GuardKnownList.AssertMatches(
            found,
            row.Known.Select(entry => (entry.Where, entry.Count)),
            (where, count) => $"{where} x{count}",
            $"[{row.Id}] {row.Because}（CJK を含む。正当なら理由を添えて例外の一覧へ）",
            $"[{row.Id}] 例外の一覧にあるのに CJK が無い（一覧から消す）",
            (where, count) => $"{where} x{count}：「{samples[where].First()}」",
            sortLines: true
        );
    }

    [Theory(DisplayName = "必須の文言の表：出どころの文字列が決めた語を含み、決めた語を含まない")]
    [MemberData(nameof(PhraseIds))]
    public void Phrases_Hold(string id)
    {
        var row = PhraseRows.ById(id);
        var text = row.Text();

        PhraseProblems(text, row.Contains, row.NotContains, row.EndsWith)
            .Should()
            .BeEmpty($"[{row.Id}] {row.Because}");
    }

    [Theory(DisplayName = "文言の表の自己検査：判定は違反の無い文を通し、違反を拾う")]
    [MemberData(nameof(JudgeProbeIndexes))]
    public void Judge_DetectsViolation(int index)
    {
        var (kind, text, contains, violates) = JudgeProbes[index];
        var violated = kind switch
        {
            GuardTextDimensions.English => CjkText.Pattern.IsMatch(text),
            GuardTextDimensions.PhraseSuffix => PhraseProblems(
                text,
                [],
                [],
                contains.Single()
            ).Count > 0,
            _ => PhraseProblems(text, contains, []).Count > 0,
        };

        violated.Should().Be(violates, $"{kind}: {text}");
    }

    [Fact(DisplayName = "文言の表：Id が一意で、守ることと語と例外の理由が空でない")]
    public void Table_IsWellFormed()
    {
        EnglishTextRows
            .All.Select(row => row.Id)
            .Concat(PhraseRows.All.Select(row => row.Id))
            .Should()
            .OnlyHaveUniqueItems();
        EnglishTextRows
            .All.Where(row =>
                string.IsNullOrWhiteSpace(row.Because)
                || row.Known.Any(entry =>
                    string.IsNullOrWhiteSpace(entry.Reason) || entry.Count < 1
                )
            )
            .Select(row => row.Id)
            .Should()
            .BeEmpty("守ること・例外の理由を書き、件数は 1 以上");
        PhraseRows
            .All.Where(row =>
                string.IsNullOrWhiteSpace(row.Because)
                || row.Contains.Count + row.NotContains.Count == 0 && row.EndsWith is null
                || row.Contains.Concat(row.NotContains).Any(string.IsNullOrEmpty)
                || row.EndsWith is { Length: 0 }
            )
            .Select(row => row.Id)
            .Should()
            .BeEmpty(
                "守ることと、含む語・含まない語・末尾の文のどれかを 1 つ以上書く（空の語・空の末尾は書かない）"
            );
    }

    [Fact(DisplayName = "[網羅] 文言の表の判定の自己検査は、種類 × 結果の組を網羅する")]
    public void Coverage_MatchesGaps()
    {
        var counter = new CoverageCounter(GuardTextDimensions.PhraseTable);

        foreach (var probe in JudgeProbes)
        {
            counter.Observe(
                new Dictionary<string, string>
                {
                    [GuardTextDimensions.Kind] = probe.Kind,
                    [GuardTextDimensions.Outcome] = probe.Violates
                        ? GuardTextDimensions.Disagrees
                        : GuardTextDimensions.Agrees,
                }
            );
        }

        var report = counter.Report();
        report.WriteSummary();
        report.AssertGaps(CoverageGap.Set(EngineIds.Guard, []), GuardTextDimensions.PhraseScope);
    }

    /// <summary>含むべき語が無い・含んではいけない語がある・末尾にあるべき文で終わらない（序数）</summary>
    internal static IReadOnlyList<string> PhraseProblems(
        string text,
        IReadOnlyList<string> contains,
        IReadOnlyList<string> notContains,
        string? endsWith = null
    ) =>
        [
            .. contains
                .Where(phrase => !text.Contains(phrase, StringComparison.Ordinal))
                .Select(phrase => $"含まない：「{phrase}」"),
            .. notContains
                .Where(phrase => text.Contains(phrase, StringComparison.Ordinal))
                .Select(phrase => $"含んではいけない：「{phrase}」"),
            .. endsWith is not null && !text.EndsWith(endsWith, StringComparison.Ordinal)
                ? [$"末尾にない：「{endsWith}」"]
                : Array.Empty<string>(),
        ];
}
