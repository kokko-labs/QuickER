using System.IO;
using AwesomeAssertions;
using QuickER.Tests.TestSupport.Engines;

namespace QuickER.Tests.TestSupport;

/// <summary>単発のテストの数え方と報告（<see cref="OneOffTestCensus"/>）の決まった形の行と、CI が呼ぶ入口</summary>
/// <remarks>
/// 行は一時フォルダに架空のテストのソースの木を作り、架空の守りで数えて、数と報告の Markdown を表明する。入口
/// （<see cref="WriteSummary_FromEnvironment"/>）は環境変数があるときだけ本物のリポジトリを数える（ci.yml の build-and-test が呼ぶ。
/// 普段の実行では何もしない）。このファイルはテストの土台の自己検査で、<see cref="ImpactMapSources.ScenarioTableExemptions"/> に
/// 載せて単発のテストにも数えない。
/// </remarks>
public sealed class OneOffTestCensusTests
{
    /// <summary>基点の数を読み、今回の数を書くフォルダ（キャッシュの戻しと保存が同じパスを使う）</summary>
    internal const string DirectoryVariable = "QUICKER_ONEOFF_DIR";

    /// <summary>今回のコミット</summary>
    internal const string CommitVariable = "QUICKER_ONEOFF_COMMIT";

    /// <summary>比べたい基点のコミット（PR は向き先の先端・push は直前のコミット）</summary>
    internal const string BaseCommitVariable = "QUICKER_ONEOFF_BASE_COMMIT";

    /// <summary>数を書くファイルの名前（フォルダの中）</summary>
    internal const string CensusFileName = "census.json";

    private const string Unit = "tests/QuickER.Tests/Unit";
    private const string Engine = "tests/QuickER.Tests/Engine";
    private const string Integration = "tests/QuickER.Tests.Integration/Runtime";

    /// <summary>架空の守り（エンジンの部分 1 つ。ソースはファイル・部分ファイルを持つ本体・フォルダ）</summary>
    private static readonly IReadOnlyList<Protection> Protections =
    [
        new(
            "Sample/Part",
            ProtectionKind.Engine,
            "架空のエンジン",
            [$"{Engine}/EngineTests.cs", $"{Engine}/EngineRows.cs", $"{Engine}/Judges"],
            ["src/QuickER.Model"],
            ""
        ),
    ];

    private static readonly IReadOnlySet<string> Exempt = new HashSet<string>(
        StringComparer.Ordinal
    )
    {
        $"{Unit}/SelfCheckTests.cs",
    };

    /// <summary>行の入力：テストのソースの木（相対パス → 本文）・基点の数・比べたい基点・期待（数と Markdown を受ける）</summary>
    internal sealed record Case(
        IReadOnlyDictionary<string, string> Files,
        OneOffCensus? Baseline,
        string ExpectedBase,
        Action<OneOffCensus, string> Expect
    );

    private const string TwoFacts = """
        public class A
        {
            [Fact] public void One() { }
            [Fact(DisplayName = "x")] public void Two() { }
            public void Helper() { }
        }
        """;

    /// <summary>決まった形の行</summary>
    internal static ScenarioTable<Case> Rows { get; } =
        new(
            "OneOffTestCensus",
            [
                new(
                    "oneoff-counts-facts-and-theories-outside-protections",
                    null,
                    "守りのソースの外のファイルの [Fact] と [Theory] をメソッドで数え（属性の名前の形・修飾を問わず、属性の無いメソッドは数えない）、守りのソースのメソッドは参考の数に分ける",
                    new(
                        Files(
                            (
                                $"{Unit}/HandTests.cs",
                                """
                                public class HandTests
                                {
                                    [Fact] public void A() { }
                                    [Xunit.FactAttribute] public void B() { }
                                    [Theory, InlineData(1), InlineData(2)] public void C(int x) { }
                                    [Xunit.Theory] [Trait("k", "v")] public void D() { }
                                    [global::Xunit.TheoryAttribute] public void E() { }
                                    public void NotATest() { }
                                    // [Fact] コメントの中は数えない
                                    private const string S = "[Fact] 文字列の中も数えない";
                                }
                                """
                            ),
                            ($"{Engine}/EngineTests.cs", TwoFacts),
                            ($"{Integration}/RuntimeTests.cs", TwoFacts)
                        ),
                        null,
                        "",
                        (census, _) =>
                        {
                            census.Facts.Should().Be(4, "HandTests の 2 と Integration の 2");
                            census.Theories.Should().Be(3);
                            census.ProtectedMethods.Should().Be(2, "エンジンのソースの 2");
                            census
                                .Files.Should()
                                .BeEquivalentTo(
                                    new Dictionary<string, int>
                                    {
                                        [$"{Unit}/HandTests.cs"] = 5,
                                        [$"{Integration}/RuntimeTests.cs"] = 2,
                                    },
                                    "QuickER.Tests と Integration の両方を数え、守りのソースのファイルは載せない"
                                );
                        }
                    )
                ),
                new(
                    "oneoff-partials-and-folders-of-protections-are-not-one-off",
                    null,
                    "守りのソースの部分ファイル（{名前}.*.cs）とフォルダの宣言の下のファイルは守りのソースに数える",
                    new(
                        Files(
                            ($"{Engine}/EngineRows.Extra.cs", TwoFacts),
                            ($"{Engine}/Judges/Deep/JudgeTests.cs", TwoFacts),
                            ($"{Engine}/EngineRows.cs", "public partial class EngineRows { }"),
                            ($"{Engine}/OtherTests.cs", TwoFacts)
                        ),
                        null,
                        "",
                        (census, _) =>
                        {
                            census.ProtectedMethods.Should().Be(4);
                            census
                                .Files.Keys.Should()
                                .BeEquivalentTo(
                                    [$"{Engine}/OtherTests.cs"],
                                    "宣言の無い同じフォルダのファイルは単発"
                                );
                        }
                    )
                ),
                new(
                    "oneoff-reads-every-conditional-branch",
                    null,
                    "条件コンパイルの外れる分岐の中のテストも数え、分岐の外のテストを重ねて数えない",
                    new(
                        Files(
                            (
                                $"{Unit}/BranchTests.cs",
                                """
                                public class BranchTests
                                {
                                    [Fact] public void Always() { }
                                #if !DEBUG
                                    [Fact] public void ReleaseOnly() { }
                                #else
                                    [Theory] public void DebugOnly() { }
                                #endif
                                }
                                """
                            )
                        ),
                        null,
                        "",
                        (census, _) =>
                        {
                            census.Facts.Should().Be(2);
                            census.Theories.Should().Be(1);
                        }
                    )
                ),
                new(
                    "oneoff-skips-build-output-and-exempt-files",
                    null,
                    "bin / obj の中と、数えないファイル（テストの土台の自己検査）は数えない",
                    new(
                        Files(
                            ($"{Unit}/bin/Debug/CopiedTests.cs", TwoFacts),
                            ($"{Unit}/obj/GeneratedTests.cs", TwoFacts),
                            ($"{Unit}/SelfCheckTests.cs", TwoFacts),
                            ($"{Unit}/KeptTests.cs", TwoFacts)
                        ),
                        null,
                        "",
                        (census, _) =>
                            census.Files.Keys.Should().BeEquivalentTo([$"{Unit}/KeptTests.cs"])
                    )
                ),
                new(
                    "oneoff-report-lists-increases-before-totals",
                    null,
                    "基点がそろえば、増えたファイル（メソッドの増減つき）とフォルダを、減ったものと合計より先に書く",
                    new(
                        Files(
                            ($"{Unit}/HandTests.cs", TwoFacts),
                            ($"{Unit}/NewTests.cs", TwoFacts),
                            ($"{Integration}/RuntimeTests.cs", TwoFacts)
                        ),
                        Census(
                            "base000000000000",
                            ($"{Unit}/HandTests.cs", 1),
                            ($"{Integration}/RuntimeTests.cs", 2),
                            ($"{Integration}/GoneTests.cs", 3)
                        ),
                        "base000000000000",
                        (_, markdown) =>
                        {
                            markdown.Should().Contain("比べた基点：`base000`");
                            markdown
                                .Should()
                                .Contain("### 増えたファイル（2 ファイル・メソッド +3）")
                                .And.Contain($"| `{Unit}/NewTests.cs` | 0 | 2 | +2 |")
                                .And.Contain($"| `{Unit}/HandTests.cs` | 1 | 2 | +1 |")
                                .And.Contain($"- `{Unit}`：+3")
                                .And.Contain("### 減ったファイル（1 ファイル・メソッド -3）")
                                .And.Contain($"| `{Integration}/GoneTests.cs` | 3 | 0 | -3 |")
                                .And.Contain(
                                    "- 単発のテスト：6（Fact 6・Theory 0）・基点 6・増減 0"
                                )
                                .And.NotContain(
                                    $"`{Integration}/RuntimeTests.cs` |",
                                    "変わらないファイルは載せない"
                                );
                            markdown
                                .IndexOf("### 増えたファイル", StringComparison.Ordinal)
                                .Should()
                                .BeLessThan(
                                    markdown.IndexOf("### 減ったファイル", StringComparison.Ordinal)
                                )
                                .And.BeLessThan(
                                    markdown.IndexOf("### 合計", StringComparison.Ordinal),
                                    "見るのは増減なので合計を後ろに置く"
                                );
                        }
                    )
                ),
                new(
                    "oneoff-report-approximate-baseline",
                    null,
                    "基点のコミットの数が無く、別のコミットの数を戻したときは「近似」と比べたコミットを書いて比べる",
                    new(
                        Files(($"{Unit}/HandTests.cs", TwoFacts)),
                        Census("older00000000000", ($"{Unit}/HandTests.cs", 2)),
                        "base000000000000",
                        (_, markdown) =>
                            markdown
                                .Should()
                                .Contain(
                                    "比べた基点：近似（基点 `base000` の数がキャッシュに無く、いちばん新しい `older00` の数と比べた）"
                                )
                                .And.Contain("### 増えたファイル（0 ファイル・メソッド 0）")
                                .And.Contain("（なし）")
                    )
                ),
                new(
                    "oneoff-report-without-baseline",
                    null,
                    "基点の数が無いときは、その旨と合計だけを書く（増減の節を書かない）",
                    new(
                        Files(($"{Unit}/HandTests.cs", TwoFacts)),
                        null,
                        "base000000000000",
                        (_, markdown) =>
                        {
                            markdown
                                .Should()
                                .Contain(
                                    "比べた基点：なし（基点 `base000` の数がキャッシュに無く、ほかのコミットの数も無い）。合計だけを書く。"
                                )
                                .And.Contain("- 単発のテスト：2（Fact 2・Theory 0）")
                                .And.NotContain("増減")
                                .And.NotContain("### 増えたファイル");
                        }
                    )
                ),
                new(
                    "oneoff-report-large-increase-only-reports",
                    null,
                    "大きく増えても報告するだけ（例外にしない）。名指しは上限まで、残りは件数で書く",
                    new(
                        Files([
                            .. Enumerable
                                .Range(0, OneOffTestCensus.ListedFiles + 5)
                                .Select(i =>
                                    (
                                        $"{Unit}/Many{i.ToString("D2", System.Globalization.CultureInfo.InvariantCulture)}Tests.cs",
                                        TwoFacts
                                    )
                                ),
                        ]),
                        Census("base000000000000"),
                        "base000000000000",
                        (census, markdown) =>
                        {
                            census.Total.Should().Be((OneOffTestCensus.ListedFiles + 5) * 2);
                            markdown
                                .Should()
                                .Contain("### 増えたファイル（25 ファイル・メソッド +50）")
                                .And.Contain("| ほか 5 ファイル | | | |")
                                .And.Contain("・基点 0・増減 +50");
                        }
                    )
                ),
            ]
        );

    /// <summary>行の Id</summary>
    public static TheoryData<string> RowIds => Rows.Ids();

    [Theory(DisplayName = "単発のテストの数え方と報告：決まった形の行")]
    [MemberData(nameof(RowIds))]
    public void Row_Holds(string id)
    {
        var input = Rows[id].Input;
        using var folder = new TestTempFolder();

        foreach (var (path, source) in input.Files)
        {
            var full = Path.Combine(folder.Path, path);
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllText(full, source);
        }

        var census = OneOffTestCensus.Count(folder.Path, Protections, Exempt, "head000000000000");
        var markdown = OneOffTestCensus.Render(census, input.Baseline, input.ExpectedBase);

        input.Expect(census, markdown);
        OneOffTestCensus
            .FromJson(OneOffTestCensus.ToJson(census))
            .Should()
            .BeEquivalentTo(census, "数の JSON は次の実行の基点としてそのまま読み戻せる");
    }

    /// <summary>
    /// CI の入口：基点の数（キャッシュから戻した census.json）と比べて今回の数を Step Summary に書き、今回の数で census.json を置き換える
    /// </summary>
    /// <remarks>
    /// 環境変数が無ければ何もしない（普段の実行）。数では赤にしない。赤になるのは数えられないとき（地図を作れない・ソースを読めない・
    /// 基点の JSON が壊れている）だけ。
    /// </remarks>
    [Fact(DisplayName = "単発のテストの報告：CI の入口（環境変数が無ければ何もしない）")]
    public void WriteSummary_FromEnvironment()
    {
        var directory = Environment.GetEnvironmentVariable(DirectoryVariable);

        if (string.IsNullOrWhiteSpace(directory))
        {
            return;
        }

        var commit = Environment.GetEnvironmentVariable(CommitVariable) ?? "";
        var expectedBase = Environment.GetEnvironmentVariable(BaseCommitVariable) ?? "";
        var censusPath = Path.Combine(directory, CensusFileName);
        var baseline = File.Exists(censusPath)
            ? OneOffTestCensus.FromJson(File.ReadAllText(censusPath))
            : null;
        var current = OneOffTestCensus.FromRepository(Docs.WorkflowGuardTests.Root, commit);
        var markdown = OneOffTestCensus.Render(current, baseline, expectedBase);

        Directory.CreateDirectory(directory);
        global::QuickER.Platform.AtomicFile.WriteAllText(
            censusPath,
            OneOffTestCensus.ToJson(current)
        );
        global::QuickER.Platform.AtomicFile.WriteAllText(
            Path.Combine(directory, "summary.md"),
            markdown
        );

        var stepSummary = Environment.GetEnvironmentVariable("GITHUB_STEP_SUMMARY");

        if (!string.IsNullOrWhiteSpace(stepSummary))
        {
            File.AppendAllText(stepSummary, markdown + "\n");
        }

        TestContext.Current.TestOutputHelper?.WriteLine(markdown);
    }

    private static IReadOnlyDictionary<string, string> Files(
        params (string Path, string Source)[] files
    ) => files.ToDictionary(file => file.Path, file => file.Source, StringComparer.Ordinal);

    private static OneOffCensus Census(string commit, params (string Path, int Methods)[] files) =>
        new(
            commit,
            files.Sum(file => file.Methods),
            0,
            0,
            files.ToDictionary(file => file.Path, file => file.Methods, StringComparer.Ordinal)
        );
}
