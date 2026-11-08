using System.IO;
using System.Text.Json;
using AwesomeAssertions;
using QuickER.Tests.TestSupport.Engines;

namespace QuickER.Tests.TestSupport;

/// <summary>変異テストの結果の要約（<see cref="MutationSummaries"/>）の決まった形の行と、夜間のジョブが呼ぶ入口</summary>
/// <remarks>
/// 行は小さな架空の mutation-report.json・ログ・ソースから要約を作り、単位への写し・状態ごとの数・Safe Mode・Dubious・
/// 前回からの増減を表明する。入口（<see cref="WriteSummary_FromEnvironment"/>）は環境変数があるときだけ本物の結果を要約する
/// （mutation.yml の要約の手順が呼ぶ。普段の実行では何もしない）。
/// </remarks>
public sealed class MutationSummaryTests
{
    /// <summary>要約に渡す結果の場所（Stryker の -O のフォルダ。reports/mutation-report.json と logs/*.txt を読む）</summary>
    internal const string ReportDirectoryVariable = "QUICKER_MUTATION_REPORT_DIR";

    /// <summary>範囲の名前（stryker の設定のファイル名から拡張子を除いたもの）</summary>
    internal const string TargetVariable = "QUICKER_MUTATION_TARGET";

    /// <summary>要約を書くフォルダ（前回の要約 summary.json があれば増減に使い、今回の summary.json・summary.md で置き換える）</summary>
    internal const string SummaryDirectoryVariable = "QUICKER_MUTATION_SUMMARY_DIR";

    private const string Root = "C:/repo";
    private const string Model = "src/QuickER.Model";
    private const string CodeGen = "src/QuickER.CodeGen.CSharp";
    private const string Queries = "src/QuickER.CodeGen.CSharp/Queries";

    private static readonly ImpactMap Map = new([Model, CodeGen, Queries], [], []);

    /// <summary>行の入力：結果の JSON・ログ・ソース（相対パス → 本文）・前回の要約の JSON・期待（要約と Markdown を受ける）</summary>
    internal sealed record Case(
        string Report,
        string Log,
        IReadOnlyDictionary<string, string> Sources,
        string? Previous,
        Action<MutationSummary, string> Expect
    )
    {
        /// <summary>設定の mutate の範囲（既定は全部）</summary>
        public Func<string, bool> Scope { get; init; } = _ => true;
    }

    private static readonly IReadOnlyDictionary<string, string> NoSources = new Dictionary<
        string,
        string
    >(StringComparer.Ordinal);

    /// <summary>決まった形の行</summary>
    internal static ScenarioTable<Case> Rows { get; } =
        new(
            "MutationSummary",
            [
                new(
                    "mutation-summary-counts-by-unit",
                    null,
                    "状態ごとの数を単位ごとに数え、スコアは (Killed＋Timeout) ÷ (Killed＋Timeout＋Survived＋NoCoverage)（CompileError・Ignored は母数の外）",
                    new(
                        Report(
                            (
                                $"{Root}/{Model}/ConstraintNames.cs",
                                [
                                    M("Killed"),
                                    M("Killed"),
                                    M("Timeout"),
                                    M("Survived"),
                                    M("NoCoverage"),
                                    M("CompileError"),
                                    M("Ignored"),
                                ]
                            ),
                            ($"{Root}/{CodeGen}/GeneratedLiteralText.cs", [M("Survived")])
                        ),
                        "",
                        NoSources,
                        null,
                        (summary, _) =>
                        {
                            summary
                                .Units.Should()
                                .Equal(
                                    new MutationUnitCounts(CodeGen, 1, 0, 1, 0, 0, 0, 0, 0),
                                    new MutationUnitCounts(Model, 7, 2, 1, 1, 1, 1, 1, 0)
                                );
                            summary.Units[1].Score.Should().Be(60.0);
                            summary.Total.Detectable.Should().Be(6);
                        }
                    )
                ),
                new(
                    "mutation-summary-deepest-unit",
                    null,
                    "ファイルは地図のいちばん深い単位へ写す（Queries のファイルはプロジェクトでなく Queries の単位）",
                    new(
                        Report(($"{Root}/{Queries}/QueryConditionTypeChecker.cs", [M("Killed")])),
                        "",
                        NoSources,
                        null,
                        (summary, _) =>
                            summary.Units.Select(unit => unit.Unit).Should().Equal(Queries)
                    )
                ),
                new(
                    "mutation-summary-runner-checkout-path",
                    null,
                    "リポジトリの根の外の絶対パス（CI のランナーのチェックアウト）は最後の /src/ から後ろで写し、src の外は単位に入らないファイルとして名指しする",
                    new(
                        Report(
                            (@"D:\a\QuickER\QuickER\src\QuickER.Model\Entity.cs", [M("Killed")]),
                            ($"{Root}/tests/QuickER.Tests/Probe.cs", [M("Survived")])
                        ),
                        "",
                        NoSources,
                        null,
                        (summary, markdown) =>
                        {
                            summary.Units.Select(unit => unit.Unit).Should().Equal(Model);
                            summary.UnmappedFiles.Should().Equal("tests/QuickER.Tests/Probe.cs");
                            markdown
                                .Should()
                                .Contain(
                                    "どの単位にも入らないファイル：tests/QuickER.Tests/Probe.cs"
                                );
                        }
                    )
                ),
                new(
                    "mutation-summary-safe-mode-methods",
                    null,
                    "Safe Mode はログの組（コンパイルできなかったファイル → 外したメソッド）から読み、外れた数はそのメソッドの中の CompileError だけを数える（同じ組は 1 つにする）",
                    new(
                        Report(
                            (
                                $"{Root}/{Model}/ConstraintNames.cs",
                                [
                                    M("CompileError", line: 4, column: 13),
                                    M("CompileError", line: 5, column: 17),
                                    M("CompileError", line: 9, column: 13),
                                    M("Killed", line: 4, column: 13),
                                ]
                            )
                        ),
                        string.Join(
                            "\n",
                            $"[11:48:26 WRN] An unidentified mutation in {Root}/{Model}/ConstraintNames.cs resulted in a compile error (at 734:59) with id: CS0165",
                            "[11:48:26 INF] Safe Mode! Stryker will remove all mutations in ResolveCore and mark them as 'compile error'.",
                            $"[11:48:27 WRN] An unidentified mutation in {Root}/{Model}/ConstraintNames.cs resulted in a compile error (at 734:59) with id: CS0165",
                            "[11:48:27 INF] Safe Mode! Stryker will remove all mutations in ResolveCore and mark them as 'compile error'."
                        ),
                        new Dictionary<string, string>(StringComparer.Ordinal)
                        {
                            [$"{Model}/ConstraintNames.cs"] = string.Join(
                                "\n",
                                "class C",
                                "{",
                                "    void ResolveCore()",
                                "    {",
                                "        var a = 1;",
                                "    }",
                                "    void Other()",
                                "    {",
                                "        var b = 2;",
                                "    }",
                                "}"
                            ),
                        },
                        null,
                        (summary, markdown) =>
                        {
                            summary
                                .SafeMode.Should()
                                .Equal(
                                    new SafeModeRemoval(
                                        $"{Model}/ConstraintNames.cs",
                                        "ResolveCore",
                                        2
                                    )
                                );
                            markdown
                                .Should()
                                .Contain("Safe Mode で外れたメソッド 1 個（変異 2 件）")
                                .And.Contain(
                                    $"| `{Model}/ConstraintNames.cs` の `ResolveCore` | 2 |"
                                );
                        }
                    )
                ),
                new(
                    "mutation-summary-out-of-scope-files",
                    null,
                    "設定の mutate の範囲の外のファイル（どの変異も mutate の除外の Ignored か、範囲で印を付ける前の CompileError）は、単位にも Safe Mode にも出さない（Stryker 5 は全部の変異をコンパイルしてから範囲で印を付ける）",
                    new(
                        Report(
                            (
                                $"{Root}/{CodeGen}/CSharpCodeGenerationService.cs",
                                [
                                    M("CompileError", line: 3, column: 9),
                                    M("Ignored", reason: "Removed by mutate filter"),
                                ]
                            ),
                            (
                                $"{Root}/{Queries}/QueryConditionTypeChecker.cs",
                                [
                                    M("Killed"),
                                    M("Ignored", reason: "Removed by block already covered filter"),
                                ]
                            )
                        ),
                        string.Join(
                            "\n",
                            $"[11:48:26 WRN] An unidentified mutation in {Root}/{CodeGen}/CSharpCodeGenerationService.cs resulted in a compile error (at 9:9) with id: CS0165",
                            "[11:48:26 INF] Safe Mode! Stryker will remove all mutations in Bind and mark them as 'compile error'."
                        ),
                        new Dictionary<string, string>(StringComparer.Ordinal)
                        {
                            [$"{CodeGen}/CSharpCodeGenerationService.cs"] =
                                "class S\n{\n    void Bind()\n    {\n    }\n}",
                        },
                        null,
                        (summary, markdown) =>
                        {
                            summary
                                .Units.Should()
                                .Equal(new MutationUnitCounts(Queries, 2, 1, 0, 0, 0, 0, 1, 0));
                            summary.SafeMode.Should().BeEmpty();
                            summary.UnmappedFiles.Should().BeEmpty();
                            markdown.Should().NotContain("CSharpCodeGenerationService");
                        }
                    )
                    {
                        Scope = StrykerMutateScope.FromConfig(
                            CodeGen,
                            ["**/Queries/QueryConditionTypeChecker.cs"]
                        ),
                    }
                ),
                new(
                    "mutation-summary-scope-excludes-files",
                    null,
                    "範囲は設定の mutate のファイル単位の指定で決める（**/*.cs から ! のファイルを除く形の除外したファイルは、変異が流れていても数えない）",
                    new(
                        Report(
                            ($"{Root}/{Model}/Column.cs", [M("Killed")]),
                            ($"{Root}/{Model}/Entity.cs", [M("Survived")])
                        ),
                        "",
                        NoSources,
                        null,
                        (summary, _) =>
                            summary
                                .Units.Should()
                                .Equal(new MutationUnitCounts(Model, 1, 1, 0, 0, 0, 0, 0, 0))
                    )
                    {
                        Scope = StrykerMutateScope.FromConfig(Model, ["**/*.cs", "!**/Entity.cs"]),
                    }
                ),
                new(
                    "mutation-summary-dubious-tests",
                    null,
                    "Dubious のテストはログの ID を重ねずに数え、単位ごとにはそのテストが通る流した変異（Killed・Survived・Timeout）を数える",
                    new(
                        Report(
                            (
                                $"{Root}/{Model}/Column.cs",
                                [
                                    M(
                                        "Killed",
                                        coveredBy: ["aaaaaaaaaaaaaaaa", "cccccccccccccccc"]
                                    ),
                                    M("Survived", coveredBy: ["bbbbbbbbbbbbbbbb"]),
                                    M("Survived", coveredBy: ["cccccccccccccccc"]),
                                    M("NoCoverage", coveredBy: ["aaaaaaaaaaaaaaaa"]),
                                ]
                            )
                        ),
                        string.Join(
                            "\n",
                            "[21:16:53 WRN] MtpRunner-1: Timed out waiting for coverage relay ack for test aaaaaaaaaaaaaaaa; marking as Dubious",
                            "[21:16:54 WRN] MtpRunner-2: Timed out waiting for coverage relay ack for test aaaaaaaaaaaaaaaa; marking as Dubious",
                            "[21:15:15 WRN] MtpRunner-1: Test run timed out while capturing per-test coverage for bbbbbbbbbbbbbbbb; marking as Dubious"
                        ),
                        NoSources,
                        null,
                        (summary, markdown) =>
                        {
                            summary.DubiousTests.Should().Be(2);
                            summary.Units.Single().Dubious.Should().Be(2);
                            markdown
                                .Should()
                                .Contain("Dubious のテスト 2 件（それが通る流した変異 2 件。");
                        }
                    )
                ),
                new(
                    "mutation-summary-delta-from-previous",
                    null,
                    "前回の要約があれば、単位ごとの数とスコアに前回からの増減を添え、前回に無い単位は「新」と書く",
                    new(
                        Report(
                            (
                                $"{Root}/{Model}/Column.cs",
                                [M("Killed"), M("Killed"), M("Survived")]
                            ),
                            ($"{Root}/{Queries}/QueryConditionParser.cs", [M("Killed")])
                        ),
                        "",
                        NoSources,
                        MutationSummaries.ToJson(
                            new MutationSummary(
                                "model",
                                [new MutationUnitCounts(Model, 3, 1, 2, 0, 0, 0, 0, 0)],
                                [],
                                0,
                                []
                            )
                        ),
                        (_, markdown) =>
                            markdown
                                .Should()
                                .Contain(
                                    $"| `{Model}` | 3 | 2（+1） | 1（-1） | 0 | 0 | 0 | 66.7% |"
                                )
                                .And.Contain($"| `{Queries}` | 1（新） | 1（新） |")
                                .And.Contain("前回から +41.7 pt")
                    )
                ),
                new(
                    "mutation-summary-no-previous",
                    null,
                    "前回の要約が無い（初回・キャッシュが消えた）か読めなければ、増減を書かず「前回なし」と書く",
                    new(
                        Report(($"{Root}/{Model}/Column.cs", [M("Killed")])),
                        "",
                        NoSources,
                        "{ not json",
                        (_, markdown) =>
                            markdown
                                .Should()
                                .Contain("・前回なし")
                                .And.Contain($"| `{Model}` | 1 | 1 | 0 | 0 | 0 | 0 | 100.0% |")
                    )
                ),
            ]
        );

    /// <summary>行の Id</summary>
    public static TheoryData<string> RowIds => Rows.Ids();

    [Theory(DisplayName = "変異テストの要約：決まった形の行")]
    [MemberData(nameof(RowIds))]
    public void Row_Holds(string id)
    {
        var input = Rows[id].Input;
        var summary = MutationSummaries.Summarize(
            "model",
            input.Report,
            input.Log,
            Root,
            Map.UnitOf,
            path => input.Sources.TryGetValue(path, out var source) ? source : null,
            input.Scope
        );
        var markdown = MutationSummaries.Render(
            summary,
            MutationSummaries.FromJson(input.Previous)
        );

        input.Expect(summary, markdown);
        MutationSummaries
            .FromJson(MutationSummaries.ToJson(summary))
            .Should()
            .BeEquivalentTo(summary, "要約の JSON は次の実行の「前回」としてそのまま読み戻せる");
    }

    /// <summary>
    /// 夜間のジョブの入口：環境変数で受けた Stryker の結果を地図の単位ごとに要約し、summary.md・summary.json を書いて Step Summary に足す
    /// </summary>
    /// <remarks>
    /// 環境変数が無ければ何もしない（普段の実行）。結果のフォルダがあるのに JSON が無いときは赤にする（要約の手順は Stryker が
    /// 成功したときだけ走る）。単位は地図と同じ宣言から作った <see cref="ImpactMap.UnitOf"/> で決める。
    /// </remarks>
    [Fact(DisplayName = "変異テストの要約：夜間のジョブの入口（環境変数が無ければ何もしない）")]
    public void WriteSummary_FromEnvironment()
    {
        var reportDirectory = Environment.GetEnvironmentVariable(ReportDirectoryVariable);

        if (string.IsNullOrWhiteSpace(reportDirectory))
        {
            return;
        }

        var target = Environment.GetEnvironmentVariable(TargetVariable);
        var summaryDirectory = Environment.GetEnvironmentVariable(SummaryDirectoryVariable);

        target.Should().NotBeNullOrWhiteSpace($"{TargetVariable} が要る");
        summaryDirectory.Should().NotBeNullOrWhiteSpace($"{SummaryDirectoryVariable} が要る");

        var reportPath = Path.Combine(reportDirectory, "reports", "mutation-report.json");

        File.Exists(reportPath).Should().BeTrue($"Stryker の結果 {reportPath} がある");

        var logDirectory = Path.Combine(reportDirectory, "logs");
        var log = Directory.Exists(logDirectory)
            ? string.Join(
                "\n",
                Directory
                    .EnumerateFiles(logDirectory, "*.txt")
                    .Order(StringComparer.Ordinal)
                    .Select(File.ReadAllText)
            )
            : "";
        var root = WorkflowGuardRoot();
        var map = ImpactMapSources.Build(root);
        var summary = MutationSummaries.Summarize(
            target!,
            File.ReadAllText(reportPath),
            log,
            root,
            map.UnitOf,
            path =>
            {
                var full = Path.Combine(root, path);
                return File.Exists(full) ? File.ReadAllText(full) : null;
            },
            StrykerMutateScope.FromConfigFile(Path.Combine(root, "stryker", target + ".json"))
        );

        Directory.CreateDirectory(summaryDirectory!);

        var previousPath = Path.Combine(summaryDirectory!, "summary.json");
        var previous = File.Exists(previousPath)
            ? MutationSummaries.FromJson(File.ReadAllText(previousPath))
            : null;
        var markdown = MutationSummaries.Render(summary, previous);

        global::QuickER.Platform.AtomicFile.WriteAllText(
            previousPath,
            MutationSummaries.ToJson(summary)
        );
        global::QuickER.Platform.AtomicFile.WriteAllText(
            Path.Combine(summaryDirectory!, "summary.md"),
            markdown
        );

        var stepSummary = Environment.GetEnvironmentVariable("GITHUB_STEP_SUMMARY");

        if (!string.IsNullOrWhiteSpace(stepSummary))
        {
            File.AppendAllText(stepSummary, markdown + "\n");
        }
    }

    private static string WorkflowGuardRoot() => Docs.WorkflowGuardTests.Root;

    /// <summary>架空の結果の JSON（ファイルごとの変異）</summary>
    private static string Report(params (string File, MutantSpec[] Mutants)[] files) =>
        JsonSerializer.Serialize(
            new
            {
                schemaVersion = "2",
                files = files.ToDictionary(
                    file => file.File,
                    file => new
                    {
                        language = "cs",
                        mutants = file.Mutants.Select(
                            (mutant, index) =>
                                new
                                {
                                    id = index.ToString(
                                        System.Globalization.CultureInfo.InvariantCulture
                                    ),
                                    mutatorName = "Probe",
                                    status = mutant.Status,
                                    location = new
                                    {
                                        start = new { line = mutant.Line, column = mutant.Column },
                                        end = new
                                        {
                                            line = mutant.Line,
                                            column = mutant.Column + 1,
                                        },
                                    },
                                    coveredBy = mutant.CoveredBy,
                                    statusReason = mutant.Reason,
                                }
                        ),
                    }
                ),
            }
        );

    /// <summary>架空の変異</summary>
    private sealed record MutantSpec(
        string Status,
        int Line,
        int Column,
        string[] CoveredBy,
        string? Reason
    );

    private static MutantSpec M(
        string status,
        int line = 1,
        int column = 1,
        string[]? coveredBy = null,
        string? reason = null
    ) => new(status, line, column, coveredBy ?? [], reason);
}
