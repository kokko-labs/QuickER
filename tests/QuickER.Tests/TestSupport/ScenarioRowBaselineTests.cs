using AwesomeAssertions;
using QuickER.Tests.TestSupport.Engines;

namespace QuickER.Tests.TestSupport;

/// <summary>
/// 新しい行の札と減るだけの写し（<see cref="ScenarioRowBaseline"/>）の自己検査：架空の表と写しで、札の無い新しい行・種別の無い理由・
/// 台帳に無い Id・写しに無い表を名指しし、書き直しの中身が Id を足さないことと、整った入力を通すことを確かめる
/// </summary>
public sealed class ScenarioRowBaselineTests
{
    private const string Reason = "既存の水準の答えの変わらない側を並べる対照の行（自己検査）";

    private static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> Recorded =
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
        {
            ["t"] = ["a", "b"],
        };

    private static BaselineTable Table(params BaselineEntry[] entries) => new("t", entries);

    private static BaselineEntry Entry(string id, RowTag? tag = null) => new(id, tag);

    /// <summary>照合の行（ラベル → 表・写し・問題に含まれるべき断片。断片が null なら問題 0）</summary>
    private static readonly Dictionary<
        string,
        (
            IReadOnlyList<BaselineTable> Tables,
            IReadOnlyDictionary<string, IReadOnlyList<string>> Baseline,
            string? Expected
        )
    > CheckRows = new(StringComparer.Ordinal)
    {
        ["same-rows"] = ([Table(Entry("a"), Entry("b"))], Recorded, null),
        ["new-row-with-ledger-id"] = (
            [Table(Entry("a"), Entry("b"), Entry("c", RowTag.Ledger("known")))],
            Recorded,
            null
        ),
        ["new-row-with-kinded-reason"] = (
            [Table(Entry("a"), Entry("c", RowTag.Reason(RowTagKind.ExistingLevelControl, Reason)))],
            Recorded,
            null
        ),
        ["new-row-without-tag"] = (
            [Table(Entry("a"), Entry("b"), Entry("c"))],
            Recorded,
            "写しに無い行 c に札が無い"
        ),
        ["new-row-reason-without-kind"] = (
            [Table(Entry("a"), Entry("c", new RowTag(RowTagKind.Unspecified, Reason)))],
            Recorded,
            "札の種別が無い"
        ),
        ["new-row-vague-reason"] = (
            [Table(Entry("a"), Entry("c", RowTag.Reason(RowTagKind.NewFeature, "TBD")))],
            Recorded,
            "札の理由が具体的でない"
        ),
        ["new-row-unknown-ledger-id"] = (
            [Table(Entry("a"), Entry("c", RowTag.Ledger("missing")))],
            Recorded,
            "台帳に無い項目の Id missing"
        ),
        ["new-row-exclusion-without-reason"] = (
            [Table(Entry("a"), Entry("c", RowTag.Ledger("known").Except("oracle", " ")))],
            Recorded,
            "外す方言・実装先 oracle の理由が空"
        ),
        ["table-missing-from-baseline"] = (
            [Table(Entry("a")), new BaselineTable("u", [Entry("x")])],
            Recorded,
            "u: 写しに表が無い"
        ),
        ["baseline-table-not-listed"] = (
            [Table(Entry("a"))],
            new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
            {
                ["t"] = ["a"],
                ["old"] = ["x"],
            },
            "写しの表 old が札の対象の表の一覧に無い"
        ),
        ["duplicate-id"] = ([Table(Entry("a"), Entry("a"))], Recorded, "Id a が重複している"),
    };

    public static TheoryData<string> CheckLabels => [.. CheckRows.Keys];

    [Theory(DisplayName = "[札] 照合は札の無い新しい行と壊れた札を名指しし、整った表を通す")]
    [MemberData(nameof(CheckLabels))]
    public void Check_ReportsUntaggedRows(string label)
    {
        var (tables, baseline, expected) = CheckRows[label];
        var problems = ScenarioRowBaseline.Check(tables, baseline, id => id == "known").Problems;

        if (expected is null)
        {
            problems.Should().BeEmpty();
        }
        else
        {
            problems
                .Should()
                .Contain(problem => problem.Contains(expected, StringComparison.Ordinal));
        }
    }

    /// <summary>書き直しの中身の行（ラベル → 表・写し・書き直しの中身の期待＝表の名前 → Id）</summary>
    private static readonly Dictionary<
        string,
        (
            IReadOnlyList<BaselineTable> Tables,
            IReadOnlyDictionary<string, IReadOnlyList<string>> Baseline,
            IReadOnlyDictionary<string, string[]> Kept
        )
    > KeptRows = new(StringComparer.Ordinal)
    {
        // 消えた行は落ちる
        ["removed-row-is-dropped"] = (
            [Table(Entry("b"))],
            Recorded,
            new Dictionary<string, string[]> { ["t"] = ["b"] }
        ),
        // 足した行（札つきでも）は書き直しで写しに入らない
        ["new-row-is-not-added"] = (
            [Table(Entry("a"), Entry("b"), Entry("c", RowTag.Ledger("known")))],
            Recorded,
            new Dictionary<string, string[]> { ["t"] = ["a", "b"] }
        ),
        // 写しから表の項目を消しても、書き直しはその表を書き戻さない
        ["table-missing-from-baseline-is-not-written"] = (
            [Table(Entry("a")), new BaselineTable("u", [Entry("x")])],
            Recorded,
            new Dictionary<string, string[]> { ["t"] = ["a"] }
        ),
    };

    public static TheoryData<string> KeptLabels => [.. KeptRows.Keys];

    [Theory(DisplayName = "[札] 書き直しの中身は写しの今もある Id だけで、Id も表も足さない")]
    [MemberData(nameof(KeptLabels))]
    public void Kept_OnlyDropsIds(string label)
    {
        var (tables, baseline, kept) = KeptRows[label];
        var result = ScenarioRowBaseline.Check(tables, baseline, id => id == "known");

        result.Kept.Keys.Should().BeEquivalentTo(kept.Keys);

        foreach (var (name, ids) in kept)
        {
            result.Kept[name].Should().Equal(ids);
        }
    }

    [Fact(DisplayName = "[札] 出力は写しに無い行の数を台帳と理由に分け、写しは読み書きで往復する")]
    public void Summary_CountsLedgerAndReasons_AndBaselineRoundTrips()
    {
        var result = ScenarioRowBaseline.Check(
            [
                Table(
                    Entry("a"),
                    Entry("c", RowTag.Ledger("known")),
                    Entry("d", RowTag.Reason(RowTagKind.Rename, Reason)),
                    Entry("e")
                ),
            ],
            Recorded,
            id => id == "known"
        );

        result.Summary().Should().StartWith("写しに無い行 3 本（台帳 1・理由 1）");
        result.Summary().Should().Contain("t e: 札なし");

        var text = ScenarioRowBaseline.Render(Recorded);
        text.Should().NotContain("\r");
        ScenarioRowBaseline.Parse(text)["t"].Should().Equal("a", "b");
    }
}
