using AwesomeAssertions;
using QuickER.Tests.TestSupport.Engines;

namespace QuickER.Tests.TestSupport;

/// <summary>
/// 分岐の軸の台帳の照合（<see cref="BranchAxisLedgerChecks"/> と、結び済みの判定役の照合 <see cref="BranchAxisLedgerGuardTests.BoundItemProblems"/>）
/// の自己検査：壊した項目を 1 つずつ渡すと名指しし、整った台帳を通す
/// </summary>
public sealed class BranchAxisLedgerChecksTests
{
    private const string Area = "area";
    private const string Reason = "この軸の水準は直した分岐の判定に入らない（自己検査）";

    private static readonly IReadOnlyList<StandardAxisArea> Areas =
    [
        new(
            Area,
            "自己検査の領域",
            [
                new(
                    "decl",
                    "方言の宣言",
                    "真と偽の方言で答えが変わるか",
                    RequiresDeclarationCheck: true
                ),
                new("data", "データ", "行の値で答えが変わるか"),
            ]
        ),
    ];

    private static IReadOnlyList<AxisAnswer> Answers(params AxisAnswer[]? answers) =>
        answers is { Length: > 0 }
            ? answers
            :
            [
                AxisAnswer.Cross(
                    "decl",
                    "暗黙コミットの方言",
                    "Caps.Flag：真の方言と偽の方言で計画が変わる"
                ),
                AxisAnswer.Irrelevant("data", Reason),
            ];

    private static BranchBinding Binding(
        IReadOnlyList<AbsentTarget>? absent = null,
        IReadOnlyList<LevelRef>? levels = null
    ) =>
        new(
            "SyncPlan/ShapeRows",
            levels ?? [new("TypeChange", "DivergentEnds")],
            "cell-row",
            ["oracle"],
            "evidence",
            absent ?? []
        );

    private static PlannedBinding Plan() =>
        new("述語の生成の表（区切り a）", "列の型 × 値", "PostgreSQL の EF Core 版", "答えが違う");

    private static BranchAxisItem Fixed(
        string judge = "`SyncPlan/ShapeRows`",
        BranchBinding? binding = null,
        IReadOnlyList<AxisAnswer>? answers = null,
        string id = "item"
    ) =>
        new(
            id,
            "指摘",
            Area,
            BranchAxisStatus.BoundFixed,
            null,
            binding ?? Binding(),
            judge,
            answers ?? Answers(),
            null
        );

    private static BranchAxisItem Planned(
        BranchAxisStatus status = BranchAxisStatus.Planned,
        string? fixedIn = "区切り a",
        string judge = "C# の評価との一致"
    ) => new("planned", "指摘", Area, status, Plan(), null, judge, Answers(), fixedIn);

    /// <summary>構造の行（ラベル → 項目と、問題に含まれるべき断片。断片が null なら問題 0）</summary>
    private static readonly Dictionary<
        string,
        (IReadOnlyList<BranchAxisItem> Items, string? Expected)
    > StructureRows = new(StringComparer.Ordinal)
    {
        ["valid-fixed"] = ([Fixed()], null),
        ["valid-planned"] = ([Planned()], null),
        ["valid-bound-unfixed-without-judge"] = (
            [
                Fixed(judge: "") with
                {
                    Status = BranchAxisStatus.BoundUnfixed,
                    FixedIn = "区切り c",
                },
            ],
            null
        ),
        ["valid-absent-target-with-heading-basis"] = (
            [
                Fixed(
                    binding: Binding([
                        new(
                            "sqlite",
                            "SQLite は一意制約を名前で見分けないので作らない",
                            "tests/X/CLAUDE.md「形の行」"
                        ),
                    ])
                ),
            ],
            null
        ),
        ["fixed-without-judge"] = ([Fixed(judge: " ")], "修正済みの項目に判定役が無い"),
        ["planned-marked-fixed"] = (
            [Planned(BranchAxisStatus.BoundFixed, fixedIn: null)],
            "結び済みの結び（表・水準・指摘のセル・証拠）が無い"
        ),
        ["planned-with-binding"] = (
            [Planned() with { Binding = Binding() }],
            "予定の項目が結び済みの結びを持つ"
        ),
        ["planned-without-plan"] = ([Planned() with { Plan = null }], "予定の結び"),
        ["planned-without-stage"] = ([Planned(fixedIn: " ")], "直す区切りが無い"),
        ["fixed-with-stage"] = ([Fixed() with { FixedIn = "区切り a" }], "直す区切りを持つ"),
        ["missing-axis-answer"] = (
            [Fixed(answers: [AxisAnswer.Irrelevant("data", Reason)])],
            "標準の軸 decl（方言の宣言）に答えていない"
        ),
        ["unknown-axis-answer"] = (
            [Fixed(answers: [.. Answers(), AxisAnswer.Irrelevant("nope", Reason)])],
            "領域 area に無い軸 nope"
        ),
        ["duplicate-axis-answer"] = (
            [Fixed(answers: [.. Answers(), AxisAnswer.Irrelevant("data", Reason)])],
            "軸 data に 2 回答えている"
        ),
        ["vague-irrelevant-reason"] = (
            [
                Fixed(
                    answers:
                    [
                        AxisAnswer.Cross("decl", "方言", "Caps.Flag：変わる"),
                        AxisAnswer.Irrelevant("data", "TBD"),
                    ]
                ),
            ],
            "軸 data の関係ない理由が具体的でない"
        ),
        ["declaration-axis-without-check"] = (
            [
                Fixed(
                    answers:
                    [
                        AxisAnswer.Cross("decl", "暗黙コミットの方言"),
                        AxisAnswer.Irrelevant("data", Reason),
                    ]
                ),
            ],
            "水準の選び方の点検"
        ),
        ["unknown-area"] = ([Fixed() with { Area = "nope" }], "知らない領域 nope"),
        ["bad-id"] = ([Fixed(id: "Bad_Id")], "Id の形でない"),
        ["duplicate-id"] = ([Fixed(), Fixed()], "Id が重複している"),
        ["absent-target-bad-basis"] = (
            [
                Fixed(
                    binding: Binding([
                        new("sqlite", "SQLite は一意制約を名前で見分けない", "根拠なし"),
                    ])
                ),
            ],
            "の形でない"
        ),
        ["absent-target-in-cell"] = (
            [
                Fixed(
                    binding: Binding([
                        new("oracle", "Oracle は作らない形の理由（自己検査）", "Caps.Flag"),
                    ])
                ),
            ],
            "指摘のセルの範囲の oracle を水準が出ないと書いている"
        ),
        ["duplicate-factor"] = (
            [Fixed(binding: Binding(levels: [new("F", "A"), new("F", "B")]))],
            "入力の因子 F が 2 回ある"
        ),
        ["bad-table-part"] = (
            [Fixed(binding: Binding() with { TablePart = "shape rows" })],
            "台帳の部分の名前の形でない"
        ),
    };

    public static TheoryData<string> StructureLabels => [.. StructureRows.Keys];

    [Theory(DisplayName = "[台帳] 構造の検査は壊した項目を名指しし、整った台帳を通す")]
    [MemberData(nameof(StructureLabels))]
    public void Structure_ReportsBrokenItems(string label)
    {
        var (items, expected) = StructureRows[label];
        var problems = BranchAxisLedgerChecks.Problems(items, Areas);

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

    [Fact(DisplayName = "[台帳] 根拠の照合は実在しない根拠だけを名指しし、根拠の形を読み分ける")]
    public void Basis_ReportsMissingBasis()
    {
        var item = Fixed(
            binding: Binding([
                new(
                    "sqlite",
                    "SQLite は作らない形の理由（自己検査）",
                    "tests/QuickER.Tests/TestSupport/CLAUDE.md「影響の地図」"
                ),
                new(
                    "mysql",
                    "MySQL は作らない形の理由（自己検査）",
                    "tests/QuickER.Tests/TestSupport/CLAUDE.md「架空の見出し」"
                ),
            ])
        );
        var root = EngineRepository.Root();
        var problems = BranchAxisLedgerChecks.BasisProblems(
            [item],
            basis =>
            {
                var (path, heading, _, _) = BranchAxisLedgerChecks.ParseBasis(basis);
                return BranchAxisLedgerChecks.HeadingExists(root, path!, heading!);
            }
        );

        problems.Should().ContainSingle().Which.Should().Contain("架空の見出し");
        BranchAxisLedgerChecks
            .ParseBasis("SyncDialectCapabilities.TransactionalDdl")
            .Should()
            .Be(((string?)null, (string?)null, "SyncDialectCapabilities", "TransactionalDdl"));
    }

    /// <summary>判定役の行（ラベル → 項目と、問題に含まれるべき断片。断片が null なら問題 0）</summary>
    private static readonly Dictionary<string, (BranchAxisItem Item, string? Expected)> JudgeRows =
        new(StringComparer.Ordinal)
        {
            ["valid-map-name"] = (Fixed("`SyncPlan/ShapeRows`（静的な規則 M1）"), null),
            ["fictitious-map-name"] = (Fixed("`SyncPlan/NoSuchPart`"), "地図に無い名前"),
            ["fictitious-test-name"] = (
                Fixed("`NoSuchLedgerJudgeTests`"),
                "tests/ に無いテストの名前"
            ),
            ["no-name"] = (Fixed("静的な規則 M1"), "地図の名前もテストの名前も無い"),
            ["planned-text-judge-is-not-checked"] = (Planned(), null),
            ["unknown-table-part"] = (
                Fixed(binding: Binding() with { TablePart = "SyncPlan/NoSuchTable" }),
                "生成の表 SyncPlan/NoSuchTable が影響の地図の守りに無い"
            ),
        };

    public static TheoryData<string> JudgeLabels => [.. JudgeRows.Keys];

    [Theory(
        DisplayName = "[台帳] 結び済みの判定役と生成の表の照合は架空の名前を名指しし、予定の項目の文は照合しない"
    )]
    [MemberData(nameof(JudgeLabels))]
    public void Judges_ReportFictitiousNames(string label)
    {
        var (item, expected) = JudgeRows[label];
        var problems = BranchAxisLedgerGuardTests.BoundItemProblems(
            [item],
            NotGeneratedGuardTests.GuardChecker()
        );

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
}
