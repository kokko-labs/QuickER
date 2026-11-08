using System.IO;
using System.Text.RegularExpressions;
using QuickER.Tests.Docs;
using QuickER.Tests.TestSupport.Engines;

namespace QuickER.Tests.TestSupport.Guards;

/// <summary>照合の表：Workflow（GitHub Actions のワークフローと、正本の値・ほかのワークフロー・同じファイルの値）</summary>
internal static partial class ParityRows
{
    /// <summary>40 桁の commit SHA とバージョンコメントで固定した uses の値</summary>
    private static readonly Regex PinnedAction = new(
        @"^[\w.-]+/[\w.-]+@[0-9a-f]{40} # v\S+$",
        RegexOptions.CultureInvariant
    );

    private static IEnumerable<ParityRow> Workflow() =>
        [
            new(
                "nightly-actions-pinned-like-other-workflows",
                "nightly.yml のアクションは、ci.yml か release.yml と同じ 40 桁の commit SHA・同じバージョンコメントで固定する"
                    + "（正本は ci.yml・release.yml の uses のうち SHA とバージョンコメントで固定した値。タグは上流の付け替えで"
                    + "実行内容が黙って差し替わり、ほかのワークフローと同じ行なら Dependabot の更新もまとめて来る）",
                ParityCompare.CopyWithinCanon,
                () =>
                    [
                        .. WorkflowGuardTests
                            .UsesLines(WorkflowGuardTests.ReadWorkflow("ci.yml"))
                            .Concat(
                                WorkflowGuardTests.UsesLines(
                                    WorkflowGuardTests.ReadWorkflow("release.yml")
                                )
                            )
                            .Where(line => PinnedAction.IsMatch(line)),
                    ],
                () =>
                    NonEmpty(
                        WorkflowGuardTests.UsesLines(
                            WorkflowGuardTests.ReadWorkflow("nightly.yml")
                        ),
                        "nightly.yml から uses を読み取れなかった"
                    )
            ),
            new(
                "nightly-jobs",
                "nightly.yml のジョブは、夜間に流すテストのプロジェクトごとの 2 つ（テストの側の NightlyJobs）だけ",
                ParityCompare.Multiset,
                () => [.. WorkflowGuardTests.NightlyJobs.Keys],
                () => [.. NightlyWorkflowJobs().Keys]
            ),
            new(
                "nightly-job-test-commands",
                "nightly.yml の各ジョブの dotnet test の手順（ちょうど 1 つ）は、そのジョブのプロジェクトの NightlyEngine の目印の"
                    + "テストだけを流す（絞り込みが消えるとエンジン以外の全テストが流れて時間の上限を超える）",
                ParityCompare.Multiset,
                () =>
                    [
                        .. WorkflowGuardTests.NightlyJobs.Select(job =>
                            $"{job.Key}: dotnet test {job.Value} --filter \"{NightlyEngineTrait.Name}={NightlyEngineTrait.Selected}\""
                        ),
                    ],
                () =>
                    [
                        .. NightlyWorkflowJobs()
                            .SelectMany(job =>
                                Regex
                                    .Matches(
                                        WorkflowGuardTests.NightlyTestStep(job.Value),
                                        @"dotnet test \S+ --filter ""[^""\r\n]*"""
                                    )
                                    .Select(match => $"{job.Key}: {match.Value}")
                            ),
                    ]
            ),
            new(
                "nightly-job-engine-profile",
                $"nightly.yml の各ジョブの dotnet test の手順は {SeedSource.ProfileVariable} を {SeedSource.NightlyValue} にする"
                    + "（プロファイルが消えると普段の種の数で流れ、夜間に何も深く流さないのに緑になる）",
                ParityCompare.Multiset,
                () =>
                    [
                        .. WorkflowGuardTests.NightlyJobs.Keys.Select(job =>
                            $"{job}: {SeedSource.ProfileVariable}={SeedSource.NightlyValue}"
                        ),
                    ],
                () =>
                    [
                        .. NightlyWorkflowJobs()
                            .SelectMany(job =>
                                Regex
                                    .Matches(
                                        WorkflowGuardTests.NightlyTestStep(job.Value),
                                        $@"(?m)^\s+{SeedSource.ProfileVariable}: (?<value>[^\r\n]*)$"
                                    )
                                    .Select(match =>
                                        $"{job.Key}: {SeedSource.ProfileVariable}={match.Groups["value"].Value}"
                                    )
                            ),
                    ]
            ),
            new(
                "release-vpk-version",
                "release.yml が入れる vpk の版（ちょうど 1 つ）は Directory.Packages.props の Velopack の版と同じ"
                    + "（食い違うと配布物の形式とアプリ内の自動更新の仕組みが合わなくなり得て、リリース後まで分からない）",
                ParityCompare.Sequence,
                () =>
                {
                    var velopack = Regex.Match(
                        File.ReadAllText(
                            Path.Combine(WorkflowGuardTests.Root, "Directory.Packages.props")
                        ),
                        @"<PackageVersion\s+Include=""Velopack""\s+Version=""(?<version>[^""]+)""\s*/>"
                    );

                    return velopack.Success
                        ? [velopack.Groups["version"].Value]
                        : throw new InvalidOperationException(
                            "Directory.Packages.props に Velopack の版が無い"
                        );
                },
                () =>
                    [
                        .. Regex
                            .Matches(
                                WorkflowGuardTests.ReadWorkflow("release.yml"),
                                @"dotnet tool install --global vpk --version (?<version>\S+)"
                            )
                            .Select(match => match.Groups["version"].Value),
                    ]
            ),
            VpkPackRuntime("release-vpk-pack-runtime", ".github/workflows/release.yml"),
            VpkPackRuntime("pack-velopack-vpk-pack-runtime", "scripts/pack-velopack.ps1"),
            new(
                "test-projects-run-on-mtp",
                $"global.json の test.runner とすべてのテストのプロジェクト（xunit v3 を参照する csproj）は {MtpRunner} に"
                    + "そろえる（混在すると dotnet test QuickER.slnx が VSTest のプロジェクトを黙って飛ばし、件数にも出ないまま緑になる。"
                    + "Stryker.NET の MTP の実行器もこの形を前提にし、IsTestProject の印でテストのプロジェクトを見分ける）",
                ParityCompare.Multiset,
                () => [.. TestRunnerTargets().Select(target => $"{target}: {MtpRunner}")],
                () =>
                    [
                        $"global.json: {GlobalJsonTestRunner()}",
                        .. TestProjectFiles()
                            .Select(path =>
                                $"{RelativeToRoot(path)}: {TestProjectRunner(File.ReadAllText(path))}"
                            ),
                    ]
            )
            {
                MinimumCanon = 3,
            },
            new(
                "workflow-test-commands-use-mtp-options",
                "ワークフローの dotnet test の行は VSTest だけのオプション（--logger・--settings・--collect）を持たない"
                    + "（MTP の dotnet test はそれを受けず、終了コード 5 でテストを 1 件も流さずに止まる）",
                ParityCompare.Multiset,
                () => WorkflowTestCommands(),
                () =>
                    [.. WorkflowTestCommands().Where(command => !VstestOnlyOption.IsMatch(command))]
            ),
            new(
                "workflow-test-telemetry-opt-out",
                $"dotnet test か変異テストの起動スクリプト（中の Stryker が MTP のテストを流す）を流すワークフローは最上位の env に {TelemetryOptOut}: \"1\" を持つ"
                    + "（MTP は既定で利用状況を送る。CI から送らない）",
                ParityCompare.Multiset,
                () =>
                    NonEmpty(
                        [
                            .. WorkflowFiles()
                                .Where(file =>
                                    WorkflowGuardTests.ReadWorkflow(file) is var body
                                    && (
                                        body.Contains("dotnet test", StringComparison.Ordinal)
                                        || body.Contains(
                                            "run-mutation.ps1",
                                            StringComparison.Ordinal
                                        )
                                    )
                                )
                                .Select(file => $"{file}: \"1\""),
                        ],
                        "dotnet test か変異テストの起動スクリプトを流すワークフローが見つからない"
                    ),
                () =>
                    [
                        .. WorkflowFiles()
                            .SelectMany(file =>
                                WorkflowGuardTests
                                    .TopLevelBlock(WorkflowGuardTests.ReadWorkflow(file), "env")
                                    .Select(line =>
                                        Regex.Match(
                                            line,
                                            $@"^  {TelemetryOptOut}: (?<value>""[^""]*"")\s*$"
                                        )
                                    )
                                    .Where(match => match.Success)
                                    .Select(match => $"{file}: {match.Groups["value"].Value}")
                            ),
                    ]
            ),
            new(
                "mutation-actions-pinned-like-other-workflows",
                "mutation.yml のアクションのうち ci.yml か release.yml も使うものは、それと同じ 40 桁の commit SHA・同じバージョンコメントで固定する"
                    + "（nightly.yml と同じ理由。ほかのワークフローと同じ行なら Dependabot の更新もまとめて来る）",
                ParityCompare.CopyWithinCanon,
                () =>
                    [
                        .. WorkflowGuardTests
                            .UsesLines(WorkflowGuardTests.ReadWorkflow("ci.yml"))
                            .Concat(
                                WorkflowGuardTests.UsesLines(
                                    WorkflowGuardTests.ReadWorkflow("release.yml")
                                )
                            )
                            .Where(line => PinnedAction.IsMatch(line)),
                    ],
                () =>
                    NonEmpty(
                        [
                            .. WorkflowGuardTests
                                .UsesLines(WorkflowGuardTests.ReadWorkflow("mutation.yml"))
                                .Where(line =>
                                    OtherWorkflowActions().Contains(ActionRepository(line))
                                ),
                        ],
                        "mutation.yml から ci.yml か release.yml も使うアクションの uses を読み取れなかった"
                    )
            ),
            new(
                "mutation-actions-pinned-by-sha",
                "mutation.yml のアクションは、ほかのワークフローが使わないもの（actions/cache の restore / save）も含めて 40 桁の commit SHA と"
                    + "バージョンコメントで固定する（タグは上流の付け替えで実行内容が黙って差し替わる）",
                ParityCompare.Multiset,
                () =>
                    NonEmpty(
                        WorkflowGuardTests.UsesLines(
                            WorkflowGuardTests.ReadWorkflow("mutation.yml")
                        ),
                        "mutation.yml から uses を読み取れなかった"
                    ),
                () =>
                    [
                        .. WorkflowGuardTests
                            .UsesLines(WorkflowGuardTests.ReadWorkflow("mutation.yml"))
                            .Where(line => PinnedActionWithPath.IsMatch(line)),
                    ]
            ),
            new(
                "mutation-targets-match-stryker-configs",
                "mutation.yml の範囲を決める手順の一覧（毎晩と週 1 回の和）は、stryker の設定のファイル（拡張子を除いた名前）と"
                    + "ちょうど 1 回ずつ一致する（設定を足して一覧に載せ忘れると、その範囲は夜間に一度も流れない。一覧にあって設定が無いと、"
                    + "その行列の行だけが設定の誤りで毎回赤になる）",
                ParityCompare.Multiset,
                () =>
                    [
                        .. StrykerConfigFiles()
                            .Select(file => Path.GetFileNameWithoutExtension(file)),
                    ],
                () =>
                    NonEmpty(
                        [
                            .. Regex
                                .Matches(
                                    WorkflowGuardTests.ReadWorkflow("mutation.yml"),
                                    @"(?m)^\s+(?:nightly|weekly)='(?<list>[^'\r\n]*)'\s*$"
                                )
                                .SelectMany(match =>
                                    Regex
                                        .Matches(match.Groups["list"].Value, @"""(?<name>[^""]+)""")
                                        .Select(name => name.Groups["name"].Value)
                                ),
                        ],
                        "mutation.yml の範囲の一覧（nightly='…'・weekly='…'）を読み取れなかった"
                    )
            ),
            new(
                "stryker-tests-only-solution-projects",
                $"Stryker のテストだけのソリューション（{StrykerSolution}）は src の全プロジェクトと QuickER.Tests・偽の app-server の"
                    + "ちょうど 1 回ずつを持つ（Stryker は変異させるプロジェクトをソリューションから探すので、src に足したプロジェクトを"
                    + "載せ忘れると、そのプロジェクトの夜間の変異テストが設定の誤りで止まる。Integration を入れると test-projects の指定が効かない）。"
                    + "パスはどれも ../ で始める（起動スクリプトが先頭の ../ を外した写しをルートに作って Stryker へ渡す）",
                ParityCompare.Multiset,
                () =>
                    [
                        .. Directory
                            .EnumerateFiles(
                                Path.Combine(WorkflowGuardTests.Root, "src"),
                                "*.csproj",
                                SearchOption.AllDirectories
                            )
                            .Where(path => !SourceFiles.IsBuildOutput(path))
                            .Select(path => "../" + RelativeToRoot(path)),
                        "../tests/QuickER.Tests/QuickER.Tests.csproj",
                        "../tests/QuickER.Tests.FakeCodexAppServer/QuickER.Tests.FakeCodexAppServer.csproj",
                    ],
                () =>
                    NonEmpty(
                        [
                            .. System
                                .Xml.Linq.XDocument.Load(
                                    Path.Combine(WorkflowGuardTests.Root, StrykerSolution)
                                )
                                .Descendants("Project")
                                .Select(project => (string?)project.Attribute("Path") ?? ""),
                        ],
                        $"{StrykerSolution} からプロジェクトを読み取れなかった"
                    )
            )
            {
                MinimumCanon = 30,
            },
            new(
                "stryker-mutate-globs-match-files",
                "stryker の設定の mutate のファイルの指定（**/ で始まる後ろの一致と、! を付けたファイル単位の除外）は、どれも設定の project の src のフォルダの"
                    + "手で書いたソースのちょうど 1 つ以上に当たる（ファイルを改名・移動すると、夜間の変異テストが黙ってそのファイルを流さなくなる。"
                    + "範囲の除外や文字の位置はコンパイルの後に印を付けるだけで Safe Mode を避けられないので使わない）",
                ParityCompare.Multiset,
                () =>
                    [
                        .. StrykerConfigFiles()
                            .SelectMany(file =>
                                StrykerMutate(file).Select(pattern => $"{file}: {pattern}")
                            ),
                    ],
                () =>
                    [
                        .. StrykerConfigFiles()
                            .SelectMany(file =>
                                StrykerMutate(file)
                                    .Where(pattern => MutateGlobMatches(file, pattern))
                                    .Select(pattern => $"{file}: {pattern}")
                            ),
                    ]
            ),
            new(
                "stryker-configs-report-only",
                "stryker の設定は、どれも MTP の実行器を使い（ソリューションは設定では読まれないので、起動スクリプト scripts/run-mutation.ps1 が -s で渡す）、点数では赤にせず（thresholds.break が 0）、"
                    + "初回のテストの失敗では止める（変異テストの結果は報告するだけ。赤になるのは Stryker が動かなかったときだけ）",
                ParityCompare.Multiset,
                () =>
                    NonEmpty(
                        [
                            .. StrykerConfigFiles()
                                .Select(file =>
                                    $"{file}: test-runner=mtp, break=0, break-on-initial-test-failure=True"
                                ),
                        ],
                        "stryker の設定のファイルが無い"
                    ),
                () =>
                    [
                        .. StrykerConfigFiles()
                            .Select(file =>
                            {
                                var config = StrykerConfig(file);

                                return $"{file}: test-runner={Text(config, "test-runner")}, "
                                    + $"break={(config.TryGetProperty("thresholds", out var thresholds) ? Text(thresholds, "break") : "")}, "
                                    + $"break-on-initial-test-failure={Text(config, "break-on-initial-test-failure")}";
                            }),
                    ]
            ),
        ];

    /// <summary>40 桁の commit SHA とバージョンコメントで固定した uses の値（actions/cache/restore のような下のパスも受ける）</summary>
    private static readonly Regex PinnedActionWithPath = new(
        @"^[\w.-]+/[\w.-]+(?:/[\w.-]+)?@[0-9a-f]{40} # v\S+$",
        RegexOptions.CultureInvariant
    );

    /// <summary>uses の値のアクションのリポジトリ（owner/repo）</summary>
    private static string ActionRepository(string usesLine) =>
        string.Join('/', usesLine.Split('@')[0].Split('/').Take(2));

    /// <summary>ci.yml か release.yml が使うアクションのリポジトリ（owner/repo）</summary>
    private static HashSet<string> OtherWorkflowActions() =>
        [
            .. WorkflowGuardTests
                .UsesLines(WorkflowGuardTests.ReadWorkflow("ci.yml"))
                .Concat(
                    WorkflowGuardTests.UsesLines(WorkflowGuardTests.ReadWorkflow("release.yml"))
                )
                .Select(ActionRepository),
        ];

    /// <summary>Stryker のテストだけのソリューション（リポジトリの根からの相対パス）</summary>
    private const string StrykerSolution = "stryker/QuickER.TestsOnly.slnx";

    /// <summary>Stryker の設定のファイル名（stryker の *.json・序数の順）</summary>
    private static IReadOnlyList<string> StrykerConfigFiles() =>
        [
            .. Directory
                .EnumerateFiles(Path.Combine(WorkflowGuardTests.Root, "stryker"), "*.json")
                .Select(path => Path.GetFileName(path))
                .Order(StringComparer.Ordinal),
        ];

    /// <summary>Stryker の設定の stryker-config の要素</summary>
    private static System.Text.Json.JsonElement StrykerConfig(string fileName)
    {
        using var json = System.Text.Json.JsonDocument.Parse(
            File.ReadAllText(Path.Combine(WorkflowGuardTests.Root, "stryker", fileName))
        );

        return json.RootElement.GetProperty("stryker-config").Clone();
    }

    /// <summary>Stryker の設定の mutate の並び（無ければ空）</summary>
    private static IReadOnlyList<string> StrykerMutate(string fileName) =>
        StrykerConfig(fileName).TryGetProperty("mutate", out var mutate)
            ? [.. mutate.EnumerateArray().Select(item => item.GetString() ?? "")]
            : [];

    /// <summary>JSON の値を文字列で読む（無ければ空・真偽値は True / False）</summary>
    private static string Text(System.Text.Json.JsonElement element, string name) =>
        !element.TryGetProperty(name, out var value) ? ""
        : value.ValueKind == System.Text.Json.JsonValueKind.String ? value.GetString() ?? ""
        : value.ValueKind
            is System.Text.Json.JsonValueKind.True
                or System.Text.Json.JsonValueKind.False
            ? value.GetBoolean().ToString(System.Globalization.CultureInfo.InvariantCulture)
        : value.GetRawText();

    /// <summary>
    /// mutate の 1 つの指定が、設定の project（src の同じ名前のフォルダ）の手で書いたソースに 1 つ以上当たるか。
    /// 受ける形は「**/」に続く後ろの一致（ワイルドカードは「**/*.cs」のときだけ・文字の範囲は持たない）と、その前に ! を付けたファイル単位の除外だけ
    /// </summary>
    private static bool MutateGlobMatches(string configFile, string pattern)
    {
        var project = Text(StrykerConfig(configFile), "project");

        if (
            !StrykerMutateScope.IsSupported(pattern)
            || !project.EndsWith(".csproj", StringComparison.Ordinal)
        )
        {
            return false;
        }

        var folder = Path.Combine(WorkflowGuardTests.Root, "src", project[..^".csproj".Length]);

        return Directory.Exists(folder)
            && Directory
                .EnumerateFiles(folder, "*.cs", SearchOption.AllDirectories)
                .Where(path => !SourceFiles.IsBuildOutput(path))
                .Select(path =>
                    Path.GetRelativePath(folder, path).Replace(Path.DirectorySeparatorChar, '/')
                )
                .Any(relative => StrykerMutateScope.Matches(pattern, relative));
    }

    /// <summary>dotnet test を MTP で動かすときの実行器の名前（global.json の test.runner の値）</summary>
    private const string MtpRunner = "Microsoft.Testing.Platform";

    /// <summary>MTP の利用状況の送信を止める環境変数</summary>
    private const string TelemetryOptOut = "TESTINGPLATFORM_TELEMETRY_OPTOUT";

    /// <summary>VSTest の dotnet test だけが受けるオプション</summary>
    private static readonly Regex VstestOnlyOption = new(
        @"\s--(?:logger|settings|collect)\b",
        RegexOptions.CultureInvariant
    );

    /// <summary>ワークフローのファイル名（.github/workflows の *.yml・序数の順）</summary>
    private static IReadOnlyList<string> WorkflowFiles() =>
        [
            .. Directory
                .EnumerateFiles(
                    Path.Combine(WorkflowGuardTests.Root, ".github", "workflows"),
                    "*.yml"
                )
                .Select(path => Path.GetFileName(path))
                .Order(StringComparer.Ordinal),
        ];

    /// <summary>ワークフローの dotnet test の行（ファイル名つき・出てくる順）</summary>
    private static IReadOnlyList<string> WorkflowTestCommands() =>
        NonEmpty(
            [
                .. WorkflowFiles()
                    .SelectMany(file =>
                        Regex
                            .Matches(WorkflowGuardTests.ReadWorkflow(file), @"dotnet test [^\r\n]*")
                            .Select(match => $"{file}: {match.Value}")
                    ),
            ],
            "ワークフローから dotnet test の行を読み取れなかった"
        );

    /// <summary>テストのプロジェクト（tests の下の csproj のうち xunit v3 を参照するもの・序数の順）</summary>
    private static IReadOnlyList<string> TestProjectFiles() =>
        NonEmpty(
            [
                .. Directory
                    .EnumerateFiles(
                        Path.Combine(WorkflowGuardTests.Root, "tests"),
                        "*.csproj",
                        SearchOption.AllDirectories
                    )
                    .Where(path =>
                        Regex.IsMatch(
                            File.ReadAllText(path),
                            @"<PackageReference\s+Include=""xunit\.v3[.""]"
                        )
                    )
                    .Order(StringComparer.Ordinal),
            ],
            "tests の下に xunit v3 を参照する csproj が無い"
        );

    /// <summary>実行器をそろえる対象（global.json とテストのプロジェクト）</summary>
    private static IReadOnlyList<string> TestRunnerTargets() =>
        ["global.json", .. TestProjectFiles().Select(RelativeToRoot)];

    /// <summary>global.json の test.runner の値（無ければ VSTest）</summary>
    private static string GlobalJsonTestRunner()
    {
        using var json = System.Text.Json.JsonDocument.Parse(
            File.ReadAllText(Path.Combine(WorkflowGuardTests.Root, "global.json"))
        );

        return
            json.RootElement.TryGetProperty("test", out var test)
            && test.TryGetProperty("runner", out var runner)
            ? runner.GetString() ?? "VSTest"
            : "VSTest";
    }

    /// <summary>
    /// テストのプロジェクトの実行器（xunit.v3.mtp-v2 を参照し、UseMicrosoftTestingPlatformRunner と IsTestProject が true なら MTP・ほかは VSTest）。
    /// IsTestProject は以前 Microsoft.NET.Test.Sdk が付けていた印で、Stryker.NET はこれでテストのプロジェクトを見分ける（無いと初回の
    /// テスト実行で 0 件になり止まる）
    /// </summary>
    private static string TestProjectRunner(string csproj) =>
        Regex.IsMatch(csproj, @"<PackageReference\s+Include=""xunit\.v3\.mtp-v2""")
        && Regex.IsMatch(
            csproj,
            @"<UseMicrosoftTestingPlatformRunner>\s*true\s*</UseMicrosoftTestingPlatformRunner>"
        )
        && Regex.IsMatch(csproj, @"<IsTestProject>\s*true\s*</IsTestProject>")
            ? MtpRunner
            : "VSTest";

    /// <summary>リポジトリの根からの相対パス（区切りは /）</summary>
    private static string RelativeToRoot(string path) =>
        Path.GetRelativePath(WorkflowGuardTests.Root, path).Replace('\\', '/');

    /// <summary>nightly.yml のジョブ（ジョブ名 → 本文）</summary>
    private static Dictionary<string, string> NightlyWorkflowJobs() =>
        WorkflowGuardTests.Jobs(WorkflowGuardTests.ReadWorkflow("nightly.yml"));

    /// <summary>vpk pack の行の --runtime と、同じファイルの dotnet publish の -r（ちょうど 1 種）の照合</summary>
    private static ParityRow VpkPackRuntime(string id, string relativePath) =>
        new(
            id,
            $"{relativePath} の vpk pack の全行は、同じファイルの dotnet publish の -r（ランタイムは 1 種）と同じ --runtime を持つ"
                + "（vpk は --runtime が無いと x86 と見なし、配布物の Velopack の実行ファイルを x86 にする）",
            ParityCompare.Set,
            () =>
            {
                var runtimes = Regex
                    .Matches(
                        File.ReadAllText(Path.Combine(WorkflowGuardTests.Root, relativePath)),
                        @"dotnet publish [^\r\n]*?-r (?<runtime>\S+)"
                    )
                    .Select(match => match.Groups["runtime"].Value)
                    .Distinct()
                    .ToList();

                return runtimes.Count == 1
                    ? runtimes
                    : throw new InvalidOperationException(
                        $"{relativePath} の publish は 1 つのランタイムで行う（見つかった -r：[{string.Join(", ", runtimes)}]）"
                    );
            },
            () =>
                NonEmpty(
                    [
                        .. Regex
                            .Matches(
                                File.ReadAllText(
                                    Path.Combine(WorkflowGuardTests.Root, relativePath)
                                ),
                                @"vpk pack --[^\r\n]*"
                            )
                            .Select(match =>
                                Regex
                                    .Match(match.Value, @"--runtime (?<runtime>\S+)")
                                    .Groups["runtime"]
                                    .Value
                            ),
                    ],
                    $"{relativePath} に vpk pack を実行する行が無い"
                )
        );

    /// <summary>空なら読み取りの失敗として例外にする</summary>
    private static IReadOnlyList<string> NonEmpty(IReadOnlyList<string> items, string message) =>
        items.Count > 0 ? items : throw new InvalidOperationException(message);
}
