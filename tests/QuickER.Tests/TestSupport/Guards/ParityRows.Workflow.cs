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
        ];

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
