using AwesomeAssertions;
using QuickER.Tests.TestSupport.Engines;

namespace QuickER.Tests.TestSupport;

/// <summary>
/// 作らない形の宣言の検査（<see cref="NotGeneratedGuard"/> と、受け持つ守りの照合 <see cref="NotGeneratedGuardTests.GuardChecker"/>）の自己検査：
/// 壊した宣言を 1 つずつ渡すと名指しし、整った宣言は通す
/// </summary>
public sealed class NotGeneratedTests
{
    private const string Part = "Guard/Sample";
    private const string Reason = "製品の事実で作らない形の理由（自己検査）";

    private static NotGeneratedShape Accepted(
        string id = "sample-shape",
        string guard = NotGenerated.Unguarded
    ) => NotGenerated.Accepted(id, "形の説明", Reason, guard);

    private static NotGeneratedDeclaration Declaration(
        IReadOnlyList<NotGeneratedShape>? shapes = null,
        string? emptyReason = null,
        IReadOnlyList<string>? parts = null,
        string generator = "SampleGenerator"
    ) => new(generator, parts ?? [Part], shapes ?? [Accepted()], emptyReason);

    /// <summary>構造の行（ラベル → 宣言と、問題に含まれるべき断片。断片が null なら問題 0）</summary>
    private static readonly Dictionary<
        string,
        (IReadOnlyList<NotGeneratedDeclaration> Declarations, string? Expected)
    > StructureRows = new(StringComparer.Ordinal)
    {
        ["valid"] = ([Declaration()], null),
        ["unguarded-mark"] = ([Declaration([Accepted(guard: NotGenerated.Unguarded)])], null),
        ["valid-empty-with-reason"] = (
            [Declaration([], "生成器は水準の外の形を作らないので空")],
            null
        ),
        ["vague-accepted-reason"] = (
            [Declaration([NotGenerated.Accepted("a", "形", "TBD", NotGenerated.Unguarded)])],
            "理由が具体的でない"
        ),
        ["vague-pending-reason"] = (
            [Declaration([NotGenerated.Pending("a", "形", "G9", "短い", NotGenerated.Unguarded)])],
            "理由が具体的でない"
        ),
        ["vague-empty-reason"] = ([Declaration([], "TODO")], "無い理由が具体的でない"),
        ["empty-pending-reason"] = (
            [Declaration([NotGenerated.Pending("a", "形", "G9", " ", NotGenerated.Unguarded)])],
            "理由が空"
        ),
        ["pending-without-stage"] = (
            [Declaration([NotGenerated.Pending("a", "形", " ", Reason, NotGenerated.Unguarded)])],
            "直す区切り（FixedIn）が無い"
        ),
        ["accepted-with-stage"] = (
            [
                Declaration([
                    new NotGeneratedShape(
                        "a",
                        "形",
                        KnownKind.Accepted,
                        "G9",
                        Reason,
                        NotGenerated.Unguarded
                    ),
                ]),
            ],
            "直す区切り（FixedIn）を持たない"
        ),
        ["unmarked-empty-guard"] = ([Declaration([Accepted(guard: "")])], "受け持つ守りが空"),
        ["empty-shapes-without-reason"] = ([Declaration([])], "無い理由が無い"),
        ["shapes-with-empty-reason"] = ([Declaration(emptyReason: Reason)], "無い理由を持つ"),
        ["duplicate-id"] = ([Declaration([Accepted("a"), Accepted("a")])], "Id が重複している"),
        ["bad-id"] = ([Declaration([Accepted("Bad_Id")])], "Id の形でない"),
        ["empty-shape-text"] = (
            [Declaration([NotGenerated.Accepted("a", " ", Reason, NotGenerated.Unguarded)])],
            "形の説明が空"
        ),
        ["unknown-engine-part"] = (
            [Declaration(parts: ["Nope/Sample"])],
            "エンジンの部分の形でない"
        ),
        ["no-engine-parts"] = ([Declaration(parts: [])], "エンジンの部分が無い"),
        ["duplicate-engine-part"] = ([Declaration(parts: [Part, Part])], "重複している"),
        ["empty-generator"] = ([Declaration(generator: " ")], "生成器の名前が空"),
        ["duplicate-generator"] = ([Declaration(), Declaration()], "同じ生成器の宣言が 2 つある"),
    };

    public static TheoryData<string> StructureLabels => [.. StructureRows.Keys];

    [Theory(DisplayName = "[作らない形] 構造の検査は壊した宣言を名指しし、整った宣言を通す")]
    [MemberData(nameof(StructureLabels))]
    public void Structure_ReportsBrokenDeclarations(string label)
    {
        var (declarations, expected) = StructureRows[label];
        var problems = NotGeneratedGuard.Problems(declarations);

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

    /// <summary>受け持つ守りの行（ラベル → 守りの本文と、問題に含まれるべき断片。断片が null なら問題 0）</summary>
    private static readonly Dictionary<string, (string Guard, string? Expected)> GuardRows = new(
        StringComparer.Ordinal
    )
    {
        ["valid-map-name-with-row"] = ("`table:SyncPlanExpectations` の行 `plan-set-null-*`", null),
        ["valid-test-method"] = (
            "`QueryConditionParserTests.Parse_Literals`・`SyncPlan/Fuzz`",
            null
        ),
        ["fictitious-row-after-map-name"] = (
            "`SyncPlan/Fuzz`（行 `plan-no-such-row-for-self-check`）",
            "持ち主のソースに無い行"
        ),
        ["fictitious-row-after-class"] = (
            "`QueryConditionParserTests` の行 `plan-no-such-row-for-self-check`",
            "持ち主のソースに無い行"
        ),
        // 名前に続けない行 Id は tests/ の全体で照合する。このファイルも全体の本文に入るので、Id は実行時に組む（リテラルに書くと当たる）
        ["fictitious-row-nowhere"] = (
            "`SyncPlan/Fuzz` と行 `" + string.Join('-', "plan", "absent", "everywhere") + "`",
            "tests/ に無い行"
        ),
        ["fictitious-class"] = ("`NoSuchGuardForSelfCheckTests`", "tests/ に無いテストの名前"),
        ["fictitious-method"] = (
            "`QueryConditionParserTests.NoSuchMethodForSelfCheck`",
            "tests/ に無いテストの名前"
        ),
        ["fictitious-map-name"] = ("`SyncPlan/NoSuchPart`", "地図に無い名前"),
        ["unreadable-word"] = ("`SyncPlan/Fuzz`・`not a reference`", "読めない語"),
        ["row-id-without-name"] = ("行 `plan-set-null-*`", "地図の名前もテストの名前も無い"),
        ["plain-text"] = ("どこかのテスト", "地図の名前もテストの名前も無い"),
    };

    public static TheoryData<string> GuardLabels => [.. GuardRows.Keys];

    [Theory(
        DisplayName = "[作らない形] 受け持つ守りの照合は架空の名前・行 Id と読めない書き方を名指しし、実在する守りを通す"
    )]
    [MemberData(nameof(GuardLabels))]
    public void Guard_ReportsFictitiousReferences(string label)
    {
        var (guard, expected) = GuardRows[label];
        var problems = NotGeneratedGuard.ReferenceProblems(
            [Declaration([Accepted(guard: guard)])],
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
                .Contain(problem =>
                    problem.StartsWith("SampleGenerator/sample-shape: ", StringComparison.Ordinal)
                    && problem.Contains(expected, StringComparison.Ordinal)
                );
        }
    }

    [Fact(
        DisplayName = "[作らない形] 受け持つ守りの照合は、印つきの空の守りを照合の関数へ渡さない"
    )]
    public void Reference_SkipsUnguardedMark()
    {
        var seen = new List<string>();
        var problems = NotGeneratedGuard.ReferenceProblems(
            [Declaration([Accepted("a"), Accepted("b", guard: "`SyncPlan/Fuzz`")])],
            guard =>
            {
                seen.Add(guard);
                return ["問題"];
            }
        );

        seen.Should().Equal("`SyncPlan/Fuzz`");
        problems.Should().Equal("SampleGenerator/b: 問題");
    }

    /// <summary>テスト用の台帳（ソースの本文は <paramref name="source"/> で決める）</summary>
    private static IReadOnlyList<string> SeedUsing(string source) =>
        NotGeneratedGuard.SeedUsingParts(
            [
                new EngineDescriptor(
                    EngineIds.Guard,
                    "Sample",
                    "作らない形の自己検査",
                    new DimensionTable(
                        EngineIds.Guard,
                        [new Factor("F", [new Level("A")]), new Factor("G", [new Level("B")])]
                    ),
                    0,
                    ["tests/sample.cs"],
                    []
                ),
            ],
            _ => source
        );

    [Fact(
        DisplayName = "[作らない形] 種を使うのに宣言の無いエンジンの部分を名指しし、宣言があれば通す"
    )]
    public void Coverage_ReportsUndeclaredSeedUsingParts()
    {
        var seedUsing = SeedUsing("var plan = SeedSource.Resolve(Seeds);");

        seedUsing.Should().Equal(Part);
        NotGeneratedGuard.UndeclaredParts([], seedUsing).Should().Equal(Part);
        NotGeneratedGuard
            .UndeclaredParts([Declaration(parts: ["Guard/Other"])], seedUsing)
            .Should()
            .Equal(Part);
        NotGeneratedGuard.UndeclaredParts([Declaration()], seedUsing).Should().BeEmpty();
        SeedUsing("var rows = Table.Rows;").Should().BeEmpty();
    }
}
