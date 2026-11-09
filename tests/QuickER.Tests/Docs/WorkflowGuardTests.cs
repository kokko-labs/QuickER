using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using AwesomeAssertions;
using QuickER.Tests.Resources;
using QuickER.Tests.TestSupport.Engines;
using Xunit;

namespace QuickER.Tests.Docs;

/// <summary>
/// GitHub Actions のワークフローとパック用スクリプトの決まりを検査する。
/// </summary>
/// <remarks>
/// <para>
/// ワークフローは CI でしか動かず、書き間違えても手元のビルドやテストでは気付けない。
/// しかも失敗が隠れる形で壊れることが多い（パックの失敗が後続の成功で上書きされる・
/// 文書だけの変更でガードが走らない、など）。ここでは壊れたら静かに効かなくなる決まりだけを、
/// ファイルを読んで検査する。
/// </para>
/// <para>
/// スクリプトは GitHub Actions の pwsh のステップと同じ包み方（<c>$ErrorActionPreference = 'stop'</c>＋
/// 末尾の <c>exit $LASTEXITCODE</c>）で実際に動かす。パック用スクリプトの <c>dotnet</c> は偽物（PATH の先頭に置いた
/// <c>dotnet.cmd</c>）に差し替え、検証スクリプトも差し替えるので、本物のパックは走らない。
/// 公開済みの版の確認は、localhost に立てた偽の flat container を照会させる。
/// </para>
/// <para>
/// ワークフローどうし・ワークフローと正本の値の一致は照合の表（<c>ParityTests</c> の行 <c>nightly-actions-pinned-like-other-workflows</c>・
/// <c>nightly-jobs</c>・<c>nightly-job-test-commands</c>・<c>nightly-job-engine-profile</c>・<c>release-vpk-version</c>・
/// <c>release-vpk-pack-runtime</c>・<c>pack-velopack-vpk-pack-runtime</c>）へ移した。ここに残すのは 1 ファイルの書き方の規則と、
/// スクリプトを実際に動かす検査と、行が呼ぶ YAML の読み取り器。
/// </para>
/// </remarks>
public sealed class WorkflowGuardTests
{
    /// <summary>リポジトリのルート</summary>
    internal static readonly string Root = NeutralResxFiles.FindRepositoryRoot();

    /// <summary>パック用スクリプト（公開パッケージの一覧の正本）のパス</summary>
    private static readonly string PackScriptPath = Path.Combine(
        Root,
        "scripts",
        "pack-packages.ps1"
    );

    /// <summary>パッケージの一覧は pack-packages.ps1 だけにあり、ワークフローは写さずに呼ぶ</summary>
    [Fact(
        DisplayName = "パッケージの一覧は pack-packages.ps1 だけにあり、ci.yml と publish.yml はそれを呼ぶ"
    )]
    public void PackageList_LivesOnlyInPackScript()
    {
        foreach (var workflow in new[] { "ci.yml", "publish.yml", "release.yml" })
        {
            ReadWorkflow(workflow)
                .Should()
                .NotMatchRegex(
                    @"dotnet pack\s+\S+\.csproj",
                    "{0} に pack の一覧を写すと、pack-packages.ps1 と片方だけ更新されて食い違う",
                    workflow
                );
        }

        ReadWorkflow("ci.yml").Should().Contain("./scripts/pack-packages.ps1 -OutputDirectory");

        ReadWorkflow("publish.yml")
            .Should()
            .MatchRegex(
                @"\./scripts/pack-packages\.ps1 -OutputDirectory [^\r\n]*-ContinuousIntegrationBuild",
                "公開用のパックはパス情報を残さない決定的ビルドで行う"
            );

        File.ReadAllText(Path.Combine(Root, "scripts", "verify-packages.ps1"))
            .Should()
            .MatchRegex(
                @"\[Parameter\(Mandatory = \$true\)\]\[int\]\$ExpectedPackageCount\s*\)",
                "期待本数を既定値で持つと、一覧の本数の写しになる（pack-packages.ps1 が一覧の本数を渡す）"
            );
    }

    /// <summary>pack-packages.ps1 は 1 本ずつ失敗を検知し、一覧の本数で検証を呼ぶ</summary>
    /// <remarks>
    /// GitHub Actions の pwsh はネイティブコマンドの非 0 で止まらないので、検査を外すと
    /// 先頭のパックが失敗しても後続の成功でスクリプト全体が成功に見える。
    /// </remarks>
    [Theory(
        DisplayName = "pack-packages.ps1 はどのパックの失敗も、検証の失敗も、終了コードで伝える"
    )]
    [InlineData("QuickER.Runtime.csproj", 0)]
    [InlineData("QuickER.Runtime.Sqlite.csproj", 0)]
    [InlineData("", 3)]
    public void PackScript_PropagatesEveryFailure(string failingProject, int verifyExitCode)
    {
        var run = RunPackScript(failingProject, verifyExitCode);

        run.ExitCode.Should().NotBe(0, run.Output);
    }

    /// <summary>すべて成功すれば 0 で終わり、全部をパックしてから一覧の本数で検証する</summary>
    [Fact(DisplayName = "pack-packages.ps1 は一覧の全部をパックし、一覧の本数を検証へ渡す")]
    public void PackScript_PacksAllAndPassesCountToVerify()
    {
        var expected = PackScriptProjectFileNames();
        var run = RunPackScript(failingProject: "", verifyExitCode: 0);

        run.ExitCode.Should().Be(0, run.Output);
        run.PackedProjects.Should().Equal(expected);
        run.VerifiedCount.Should().Be(expected.Count.ToString());
    }

    /// <summary>検証へ渡す本数は一覧から数え、別の場所に書かない</summary>
    /// <remarks>一覧の先頭を 1 本消した写しを動かし、検証が受け取る本数も 1 本減ることを見る。</remarks>
    [Fact(DisplayName = "pack-packages.ps1 の一覧を減らすと、検証へ渡す本数も追従する")]
    public void PackScript_VerifiedCountFollowsList()
    {
        var expected = PackScriptProjectFileNames().Skip(1).ToList();
        var firstEntry = new Regex(@"^\s*'src/[^']+\.csproj',\r?\n", RegexOptions.Multiline);
        var run = RunPackScript(
            failingProject: "",
            verifyExitCode: 0,
            transformScript: script => firstEntry.Replace(script, "", 1)
        );

        run.ExitCode.Should().Be(0, run.Output);
        run.PackedProjects.Should().Equal(expected);
        run.VerifiedCount.Should().Be(expected.Count.ToString());
    }

    /// <summary>テストが読むファイルを CI の除外に入れない</summary>
    /// <remarks>
    /// 文書のガード（docs の索引・CLAUDE.md・ライセンス文書のパリティ）は docs/ とルート直下のファイルを
    /// 読む。それらを paths-ignore に入れると、文書だけの変更ではガードが走らずにマージできてしまう。
    /// GitHub Actions のパスの照合では <c>*</c> は <c>/</c> を越えず、<c>**</c> は越える。
    /// </remarks>
    [Fact(DisplayName = "ci.yml の paths-ignore は docs/ とルート直下のファイルを除外しない")]
    public void PathsIgnore_DoesNotSkipFilesTheGuardsRead()
    {
        var lists = PathsIgnoreLists(ReadWorkflow("ci.yml"));

        lists.Should().HaveCount(2, "push と pull_request の両方に paths-ignore がある");
        lists[1].Should().Equal(lists[0], "push と pull_request で除外の範囲をそろえる");

        var readByGuards = Directory
            .EnumerateFiles(Path.Combine(Root, "docs"), "*", SearchOption.AllDirectories)
            .Concat(Directory.EnumerateFiles(Root))
            .Select(path => Path.GetRelativePath(Root, path).Replace('\\', '/'))
            .ToList();

        readByGuards.Should().Contain("CLAUDE.md");

        foreach (var pattern in lists[0])
        {
            var matcher = GlobToRegex(pattern);

            readByGuards
                .Where(path => matcher.IsMatch(path))
                .Should()
                .BeEmpty("paths-ignore の {0} がテストの読むファイルを除外している", pattern);
        }
    }

    /// <summary>実行の取り消しは PR だけに効かせる</summary>
    [Fact(DisplayName = "ci.yml は PR の実行だけを取り消し、main への push の実行は取り消さない")]
    public void Concurrency_CancelsOnlyPullRequests()
    {
        var workflow = ReadWorkflow("ci.yml");

        workflow.Should().Contain("cancel-in-progress: ${{ github.event_name == 'pull_request' }}");
        workflow.Should().NotMatchRegex(@"cancel-in-progress:\s*true");
    }

    /// <summary>サンプルは slnx のビルドを使い、ステップの中でビルドし直さない</summary>
    /// <remarks>
    /// サンプルの csproj は QuickER.slnx に入っている。ステップの中でビルドし直すと重複するうえ、
    /// 複数行の run では途中のビルドの失敗が後続の成功で隠れる。
    /// </remarks>
    [Fact(DisplayName = "ci.yml の dotnet build は QuickER.slnx の 1 回だけ")]
    public void SampleSteps_DoNotRebuild()
    {
        Regex
            .Matches(ReadWorkflow("ci.yml"), @"dotnet build[^\r\n]*")
            .Select(match => match.Value)
            .Should()
            .Equal("dotnet build QuickER.slnx");
    }

    /// <summary>Windows のテストの一時ファイルはランナーの一時領域へ置く</summary>
    /// <remarks>
    /// テストは SQLite の実ファイル DB や偽の app-server の一時フォルダを Path.GetTempPath() の下に作る。既定の TEMP
    /// （C: のユーザープロファイルの下）はファイルの作成と削除が遅く、手順の env が消えると全体の時間が静かに延びる。
    /// </remarks>
    [Fact(
        DisplayName = "ci.yml の Windows のテストの手順は TEMP と TMP をランナーの一時領域へ向ける"
    )]
    public void WindowsTestStep_PutsTempFilesOnRunnerTemp()
    {
        var lines = ReadWorkflow("ci.yml").ReplaceLineEndings("\n").Split('\n');
        var start = Array.FindIndex(lines, line => line.Trim() == "- name: テスト");

        start.Should().BeGreaterThanOrEqualTo(0, "Windows のジョブにテストの手順がある");
        var body = string.Join(
            "\n",
            lines
                .Skip(start + 1)
                .TakeWhile(line => !line.TrimStart().StartsWith("- ", StringComparison.Ordinal))
        );
        body.Should().Contain("dotnet test QuickER.slnx --no-build");
        AssertTempOnRunnerTemp(body);
    }

    /// <summary>夜間のワークフローは定時と手動でだけ動く</summary>
    /// <remarks>
    /// push・pull_request で動くと、夜間の種の数（1 ジョブ 30〜60 分）が普段の変更のたびに走る。
    /// </remarks>
    [Fact(DisplayName = "nightly.yml のトリガーは schedule と workflow_dispatch だけ")]
    public void Nightly_RunsOnlyOnScheduleAndDispatch()
    {
        var workflow = ReadWorkflow("nightly.yml");

        TopLevelBlockKeys(workflow, "on")
            .Should()
            .BeEquivalentTo(
                ["schedule", "workflow_dispatch"],
                "push・pull_request などで動かさない"
            );
        workflow.Should().Contain("- cron: \"0 18 * * *\"");
    }

    /// <summary>夜間のワークフローの GITHUB_TOKEN は読み取り専用</summary>
    [Fact(DisplayName = "nightly.yml の permissions は contents: read だけ")]
    public void Nightly_PermissionsAreReadOnly()
    {
        TopLevelBlock(ReadWorkflow("nightly.yml"), "permissions")
            .Select(line => line.Trim())
            .Where(line => line.Length > 0 && !line.StartsWith('#'))
            .Should()
            .Equal("contents: read");
    }

    /// <summary>夜間の実 DB のジョブは厳格モードで流す</summary>
    [Fact(DisplayName = "nightly.yml の Ubuntu のジョブは QUICKER_REQUIRE_DOCKER=1 で流す")]
    public void Nightly_IntegrationJob_IsStrict()
    {
        var step = NightlyTestStep(Jobs(ReadWorkflow("nightly.yml"))["integration-nightly"]);

        Regex
            .IsMatch(step, @"(?m)^\s+QUICKER_REQUIRE_DOCKER: ""1""$")
            .Should()
            .BeTrue("コンテナ起動の失敗をスキップ緑にしない");
    }

    /// <summary>夜間の Windows のジョブも一時ファイルをランナーの一時領域へ置く</summary>
    [Fact(
        DisplayName = "nightly.yml の Windows のジョブは TEMP と TMP をランナーの一時領域へ向ける"
    )]
    public void Nightly_WindowsJob_PutsTempFilesOnRunnerTemp()
    {
        AssertTempOnRunnerTemp(NightlyTestStep(Jobs(ReadWorkflow("nightly.yml"))["unit-nightly"]));
    }

    /// <summary>夜間のジョブは時間の上限（60 分）を持つ</summary>
    [Fact(DisplayName = "nightly.yml の各ジョブは timeout-minutes を 60 以下に持つ")]
    public void Nightly_JobsTimeOutWithinBudget()
    {
        foreach (var (job, body) in Jobs(ReadWorkflow("nightly.yml")))
        {
            var match = Regex.Match(body, @"(?m)^    timeout-minutes: (?<minutes>\d+)\s*$");

            match.Success.Should().BeTrue($"{job} に timeout-minutes がある");
            int.Parse(match.Groups["minutes"].Value, CultureInfo.InvariantCulture)
                .Should()
                .BeInRange(1, 60, $"{job} は夜間の予算（1 ジョブ 60 分）に収める");
        }
    }

    /// <summary>夜間のジョブはテストの結果（TRX）を書き、失敗した夜にも成果物として残す</summary>
    /// <remarks>
    /// コンソールのログにはテストの出力（網羅の要約・夜間なら埋まる組・種の数の警告）が出ず、方言の派生は同じ表示名なので、
    /// TRX が無いと夜間に何が深く流れたのかを読めない。<c>if: always()</c> が無いと、確かめたい失敗の夜にだけ結果が残らない。
    /// </remarks>
    [Fact(
        DisplayName = "nightly.yml の各ジョブは TRX を書き、if: always() の upload-artifact で保存する"
    )]
    public void Nightly_JobsKeepTestResultsAsArtifacts()
    {
        var jobs = Jobs(ReadWorkflow("nightly.yml"));
        var artifactNames = new List<string>();

        foreach (var job in NightlyJobs.Keys)
        {
            var testStep = NightlyTestStep(jobs[job]);

            testStep
                .Should()
                .Contain(
                    $"--report-trx --report-trx-filename {job}.trx",
                    $"{job} はテストの結果を TRX で書く（MTP の TrxReport 拡張。テストの出力が StdOut に入る）"
                )
                .And.Contain(
                    $"--results-directory \"{NightlyResultsDirectory}\"",
                    $"{job} は TRX をランナーの一時領域へ書く"
                )
                .And.Contain(
                    "--output detailed",
                    $"{job} はコンソールのログにも各テストの結果を残す"
                );

            var uploads = JobSteps(jobs[job])
                .Where(step =>
                    step.Contains("uses: actions/upload-artifact@", StringComparison.Ordinal)
                )
                .ToList();

            uploads.Should().ContainSingle($"{job} に TRX を保存する手順がちょうど 1 つある");

            var upload = uploads[0];

            Regex
                .IsMatch(upload, @"(?m)^\s+if: always\(\)\s*$")
                .Should()
                .BeTrue($"{job} は失敗した夜にも結果を残す");
            Regex
                .IsMatch(
                    upload,
                    $@"(?m)^\s+path: {Regex.Escape(NightlyResultsDirectory)}/\*\.trx\s*$"
                )
                .Should()
                .BeTrue($"{job} はテストの手順が書いた TRX を保存する");

            var retention = Regex.Match(upload, @"(?m)^\s+retention-days: (?<days>\d+)\s*$");

            retention.Success.Should().BeTrue($"{job} は保持日数を持つ");
            int.Parse(retention.Groups["days"].Value, CultureInfo.InvariantCulture)
                .Should()
                .BeGreaterThanOrEqualTo(1);

            var name = Regex.Match(upload, @"(?m)^\s+name: (?<name>\S+)\s*$");

            name.Success.Should().BeTrue($"{job} の成果物に名前がある");
            artifactNames.Add(name.Groups["name"].Value);
        }

        artifactNames.Should().OnlyHaveUniqueItems("ジョブごとに別の成果物の名前にする");
    }

    /// <summary>CI の Windows のテストは TRX を書き、失敗した実行にも成果物として残す</summary>
    /// <remarks>
    /// MTP のコンソールは表示名だけを出すので、テストのクラスごとの時間（P5 の時間の比較）は TRX の完全修飾名で読む。
    /// <c>QuickER.slnx</c> は 2 つのプロジェクトを流すので、TRX のファイル名は指定しない（指定するとプロジェクトどうしで同じ名前に上書きする）。
    /// </remarks>
    [Fact(
        DisplayName = "ci.yml の Windows のテストは TRX を書き、if: always() の upload-artifact で保存する"
    )]
    public void WindowsTestStep_KeepsTestResultsAsArtifacts()
    {
        var job = Jobs(ReadWorkflow("ci.yml"))["build-and-test"];
        var testSteps = JobSteps(job)
            .Where(step => step.Contains("dotnet test QuickER.slnx", StringComparison.Ordinal))
            .ToList();

        testSteps
            .Should()
            .ContainSingle(
                "全件のテストの手順はちょうど 1 つ（単発のテストの報告の dotnet test は別の手順）"
            );

        var testStep = testSteps[0];

        testStep
            .Should()
            .Contain(
                $"--report-trx --results-directory \"{CiResultsDirectory}\"",
                "TRX をランナーの一時領域へ書く"
            )
            .And.NotContain("--report-trx-filename", "2 つのプロジェクトが同じ名前に上書きしない");
        AssertKeptArtifact(job, $"{CiResultsDirectory}/*.trx", "build-and-test");
    }

    /// <summary>変異テストのワークフローは定時と手動でだけ動き、毎晩と週 1 回の 2 つの定時を持つ</summary>
    [Fact(
        DisplayName = "mutation.yml のトリガーは schedule の 2 つ（毎晩・週 1 回）と workflow_dispatch だけ"
    )]
    public void Mutation_RunsOnlyOnScheduleAndDispatch()
    {
        var workflow = ReadWorkflow("mutation.yml");

        TopLevelBlockKeys(workflow, "on")
            .Should()
            .BeEquivalentTo(
                ["schedule", "workflow_dispatch"],
                "push・pull_request で数時間の変異テストを流さない"
            );
        Regex
            .Matches(workflow, @"(?m)^\s+- cron: ""(?<cron>[^""]+)""\s*$")
            .Select(match => match.Groups["cron"].Value)
            .Should()
            .Equal([MutationNightlyCron, MutationWeeklyCron], "毎晩の Model と週 1 回の Provider");
    }

    /// <summary>変異テストのワークフローの GITHUB_TOKEN は読み取り専用で、同時実行の組は夜間のワークフローと別・取り消さない</summary>
    [Fact(
        DisplayName = "mutation.yml の permissions は contents: read だけで、concurrency は mutation- の組・取り消さない"
    )]
    public void Mutation_IsReadOnlyAndNotCancelled()
    {
        var workflow = ReadWorkflow("mutation.yml");

        TopLevelBlock(workflow, "permissions")
            .Select(line => line.Trim())
            .Where(line => line.Length > 0 && !line.StartsWith('#'))
            .Should()
            .Equal("contents: read");

        var concurrency = TopLevelBlock(workflow, "concurrency")
            .Select(line => line.Trim())
            .Where(line => line.Length > 0 && !line.StartsWith('#'))
            .ToList();

        concurrency
            .Should()
            .Equal(
                [
                    "group: mutation-${{ github.event.schedule || inputs.scope }}",
                    "cancel-in-progress: false",
                ],
                "毎晩と週 1 回を別の組にし（長い週 1 回が毎晩を待たせない）、走っている夜の結果を取り消さない"
            );
    }

    /// <summary>変異テストのワークフローの各ジョブは、時間の上限を既定（360 分）に任せず明示する</summary>
    [Fact(DisplayName = "mutation.yml の各ジョブは timeout-minutes を 360 未満で明示する")]
    public void Mutation_JobsTimeOutExplicitly()
    {
        var jobs = Jobs(ReadWorkflow("mutation.yml"));

        jobs.Keys.Should().BeEquivalentTo(["plan", "mutation"]);

        foreach (var (job, body) in jobs)
        {
            var match = Regex.Match(body, @"(?m)^    timeout-minutes: (?<minutes>\d+)\s*$");

            match.Success.Should().BeTrue($"{job} に timeout-minutes がある");
            int.Parse(match.Groups["minutes"].Value, CultureInfo.InvariantCulture)
                .Should()
                .BeInRange(1, 359, $"{job} はジョブの上限（6 時間）の手前で止める");
        }
    }

    /// <summary>
    /// 変異テストは、ツールのマニフェストの版の Stryker を起動スクリプト（scripts/run-mutation.ps1）越しに範囲の設定と並列度 4 で呼び、
    /// 結果を if: always() の成果物に残す。点数で赤にする引数をコマンドに足さない
    /// </summary>
    /// <remarks>
    /// ワークフローが dotnet stryker を直に呼ぶと、テストだけのソリューションの写しを作る手順（Stryker が ../ のパスでテストを
    /// 0 件と数えるのを避ける）を通らない。版を書いた <c>dotnet tool install</c> はマニフェストと別の版を入れ得る。
    /// 並列度を書かないと Stryker の既定（コアの半分＝4 vCPU で 2）になり、Model でも時間の上限に届く見込み。
    /// <c>--break-at</c>・<c>-b</c>・<c>--threshold-*</c> は設定の thresholds を上書きする。
    /// ci.yml の build-and-test は、全件のテストの後に、起動スクリプトの <c>-TestsOnly</c> で provider-sync-planner の環境のテストを
    /// 流す（Stryker の最初のテストの実行と同じ環境でだけ落ちるテストを、週 1 回の実行より前に PR で捕まえる）。
    /// 手順が無い・スイッチが無い・<c>continue-on-error</c> があると、その環境の失敗が週 1 回まで隠れる。
    /// </remarks>
    [Fact(
        DisplayName = "mutation.yml は tool restore の Stryker を起動スクリプトから範囲の設定と並列度 4 で呼び、点数で赤にせず、結果を成果物に残す。ci.yml は同じ起動スクリプトの -TestsOnly で同期の計画の範囲の環境のテストを全件の後に流す"
    )]
    public void Mutation_RunsStrykerFromToolManifestAndKeepsReports()
    {
        var workflow = ReadWorkflow("mutation.yml");
        var job = Jobs(workflow)["mutation"];
        var steps = JobSteps(job);

        workflow
            .Should()
            .NotContain("dotnet tool install", "Stryker の版の正本は .config/dotnet-tools.json");

        // 起動スクリプトの範囲ごとの表の変数は、どのワークフローにも書かない（普段の CI・夜間で Docker を飛ばしたり種を減らしたりしない。
        // 厳格モードの QUICKER_REQUIRE_DOCKER は CI・夜間の ubuntu のジョブが立てるので、ここでは見ない）
        foreach (
            var file in Directory.EnumerateFiles(
                Path.Combine(Root, ".github", "workflows"),
                "*.yml"
            )
        )
        {
            File.ReadAllText(file)
                .Should()
                .NotContainAny(
                    MutationScriptVariables.Where(name => name != "QUICKER_REQUIRE_DOCKER"),
                    $"{Path.GetFileName(file)} は変異テストの起動スクリプトだけが立てる変数を書かない"
                );
        }
        workflow
            .Should()
            .NotContain(
                "dotnet stryker",
                "Stryker は起動スクリプトから呼ぶ（直に呼ぶとソリューションの写しを作る手順を通らない）"
            );
        steps
            .Where(step => Regex.IsMatch(step, @"(?m)^\s+run: dotnet tool restore\s*$"))
            .Should()
            .ContainSingle("Stryker はマニフェストから入れる");

        var strykerSteps = steps
            .Where(step => step.Contains(MutationScriptCall, StringComparison.Ordinal))
            .ToList();

        strykerSteps.Should().ContainSingle("変異テストの手順はちょうど 1 つ");

        var stryker = strykerSteps[0];

        Regex
            .IsMatch(stryker, @"(?m)^\s+working-directory:")
            .Should()
            .BeFalse("起動スクリプトが自分でリポジトリのルートへ移る");
        stryker
            .Should()
            .Contain(
                $"{MutationScriptCall} -Target ${{{{ matrix.target }}}} -OutputDirectory \"{MutationResultsDirectory}\" -Concurrency 4",
                "範囲の設定を選び、結果をランナーの一時領域へ書き、並列度を 4 vCPU に合わせる"
            );
        Regex
            .IsMatch(stryker, @"\s(?:--break-at|-b|--threshold-high|--threshold-low)\b")
            .Should()
            .BeFalse("点数で赤にする引数を足さない（設定の thresholds.break 0 が正本）");
        AssertTempOnRunnerTemp(stryker);
        AssertKeptArtifact(job, MutationResultsDirectory, "mutation");

        // ci.yml：変異テストの環境のテストは、起動スクリプトのスイッチで呼ぶ手順がちょうど 1 つ（全件のテストの後・失敗で赤）
        var ciSteps = JobSteps(Jobs(ReadWorkflow("ci.yml"))["build-and-test"]);
        var environmentSteps = ciSteps
            .Select((step, index) => (step, index))
            .Where(pair => pair.step.Contains(MutationScriptCall, StringComparison.Ordinal))
            .ToList();

        environmentSteps
            .Should()
            .ContainSingle("ci.yml の変異テストの環境のテストの手順はちょうど 1 つ");

        var (environmentStep, environmentIndex) = environmentSteps[0];

        environmentStep
            .Should()
            .Contain(
                $"{MutationScriptCall} -Target provider-sync-planner -TestsOnly -OutputDirectory \"${{{{ runner.temp }}}}/mutation-env\"",
                "Stryker を呼ばず、同期の計画の範囲の表の環境で表のテストだけを流す"
            )
            .And.NotContain("continue-on-error", "その環境の失敗で CI を赤にする");
        Regex
            .IsMatch(environmentStep, @"(?m)^\s+shell: pwsh\s*$")
            .Should()
            .BeTrue("起動スクリプトは pwsh で動かす");
        environmentIndex
            .Should()
            .BeGreaterThan(
                ciSteps.FindIndex(step =>
                    step.Contains("dotnet test QuickER.slnx", StringComparison.Ordinal)
                ),
                "全件のテストの後に流す（ビルドを使い回す）"
            );
        AssertTempOnRunnerTemp(environmentStep);
    }

    /// <summary>
    /// 起動スクリプトは、範囲のテストだけのソリューションの ../ を外した写しをルートに作ってから、ルートで Stryker を範囲の設定・写し・
    /// 並列度・ログつきで呼び、成功でも失敗でも写しを消し、Stryker の失敗を終了コードで伝える。写しの名前は .gitignore にある。
    /// 範囲ごとの表の環境変数は Stryker を呼ぶ間だけ立て（外し）、終わったら元へ戻し、実行の環境を結果のフォルダに書く
    /// </summary>
    /// <remarks>
    /// Stryker 5 は ../ で始まるパスのソリューションではテストを 0 件と数えて止まる。写しが残るとルートで引数なしの
    /// dotnet build / dotnet test がソリューションが 2 つで止まる。GitHub Actions の pwsh はネイティブコマンドの非 0 で止まらない。
    /// 偽の dotnet は呼ばれたときの作業フォルダ・引数・写しの中身・環境変数を記録し、指定の終了コードで終わる。
    /// 呼び出し元は厳格モード（QUICKER_REQUIRE_DOCKER=1）と同期計画の種の数（7）を立てておき、provider-sync-planner の行では
    /// Stryker から厳格モードが見えず（QUICKER_SKIP_DOCKER と両立しない）、種の数が表の値で上書きされ、終わった後は元の値に戻ることを見る。
    /// 期待の値はスクリプトの表から読まずにここへ書く（表の値を写すと、表を壊しても緑のまま）。
    /// <c>-TestsOnly</c> の行は、Stryker の代わりに表のテストのプロジェクトを表の絞り込みで <c>dotnet test --no-build</c> で呼び、
    /// 写しを作らず、変数の立て外し・戻し・実行の環境の記録は Stryker の行と同じであることを見る（ci.yml が PR でこの経路を呼ぶ）。
    /// </remarks>
    [Theory(
        DisplayName = "run-mutation.ps1 は範囲のソリューションの写しを作ってからルートで Stryker を呼び（-TestsOnly なら写しを作らずに表のテストを流し）、範囲の環境変数をその間だけ立て、成否に依らず写しを消して環境を戻し、失敗を伝える"
    )]
    [InlineData("model", 0, false)]
    [InlineData("model", 1, false)]
    [InlineData("provider-sync-planner", 0, false)]
    [InlineData("provider-sync-planner", 1, false)]
    [InlineData("provider-sync-planner", 0, true)]
    [InlineData("provider-sync-planner", 1, true)]
    public void MutationScript_RunsStrykerOnRootCopyAndRemovesIt(
        string target,
        int strykerExitCode,
        bool testsOnly
    )
    {
        var (solution, environmentAtStryker) = target switch
        {
            "provider-sync-planner" => (
                "QuickER.TestsWithIntegration.slnx",
                "QUICKER_REQUIRE_DOCKER= QUICKER_SKIP_DOCKER=1 QUICKER_SYNC_FUZZ_SEEDS=100 QUICKER_SYNC_CAPNET_SEEDS=500"
            ),
            _ => (
                "QuickER.TestsOnly.slnx",
                "QUICKER_REQUIRE_DOCKER=1 QUICKER_SKIP_DOCKER= QUICKER_SYNC_FUZZ_SEEDS=7 QUICKER_SYNC_CAPNET_SEEDS="
            ),
        };
        var run = RunMutationScript(target, strykerExitCode, testsOnly);

        if (strykerExitCode == 0)
        {
            run.ExitCode.Should().Be(0, run.Output);
        }
        else
        {
            run.ExitCode.Should()
                .NotBe(
                    0,
                    "Stryker（-TestsOnly ならテスト）の失敗を終了コードで伝える\n" + run.Output
                );
        }

        run.WorkingDirectory.Should()
            .Be(run.RepositoryRoot, "dotnet はルートを作業フォルダにして呼ぶ\n" + run.Output);

        if (testsOnly)
        {
            run.Arguments.Should()
                .Be(
                    "test tests/QuickER.Tests.Integration/QuickER.Tests.Integration.csproj --no-build --filter FullyQualifiedName~QuickER.Tests.Integration.Dialects.SyncPlanFuzz --output detailed",
                    "範囲の表のテストのプロジェクトを、表の絞り込みでビルドを使い回して流す"
                );
            run.CopiedProjects.Should().BeEmpty("-TestsOnly ではソリューションの写しを作らない");
            File.Exists(
                    Path.Combine(
                        Root,
                        "tests",
                        "QuickER.Tests.Integration",
                        "QuickER.Tests.Integration.csproj"
                    )
                )
                .Should()
                .BeTrue("表のテストのプロジェクトは実在する");
            Directory
                .Exists(
                    Path.Combine(
                        Root,
                        "tests",
                        "QuickER.Tests.Integration",
                        "Dialects",
                        "SyncPlanFuzz"
                    )
                )
                .Should()
                .BeTrue("絞り込みの名前空間（フォルダに追従）のテストが実在する");
        }
        else
        {
            run.Arguments.Should()
                .Be(
                    $"stryker -f stryker/{target}.json -s {MutationSolutionCopy} -O {run.OutputDirectory} -c 3 --skip-version-check -L",
                    "範囲の設定・ルートの写し・結果のフォルダ（絶対パス）・並列度・ログのファイルを渡す"
                );
            run.CopiedProjects.Should()
                .Equal(
                    StrykerSolutionProjects(solution).Select(path => path[3..]),
                    $"写しは範囲のソリューション（{solution}）の各パスから先頭の ../ を外したもの（ルートから同じプロジェクトを指す）"
                );
            run.CopiedProjects.Should()
                .OnlyContain(
                    path => File.Exists(Path.Combine(Root, path)),
                    "写しのパスはリポジトリのルートから実在のプロジェクトを指す"
                );
        }
        run.CopyLeftBehind.Should().BeFalse("成否に依らず実行の後に写しを消す");
        File.ReadAllLines(Path.Combine(Root, ".gitignore"))
            .Should()
            .Contain($"/{MutationSolutionCopy}", "写しは誤ってコミットされない");
        run.EnvironmentAtStryker.Should()
            .Be(
                environmentAtStryker,
                "範囲の表の変数を Stryker を呼ぶ間だけ立て、外す\n" + run.Output
            );
        run.EnvironmentAfter.Should()
            .Be(MutationScriptParentEnvironment, "成否に依らず、呼び出し元の環境を元に戻す");

        using var record = System.Text.Json.JsonDocument.Parse(
            run.RunEnvironmentJson
                ?? throw new Xunit.Sdk.XunitException("run-environment.json が無い")
        );
        var root = record.RootElement;

        root.GetProperty("target").GetString().Should().Be(target);
        root.GetProperty("solution").GetString().Should().Be($"stryker/{solution}");
        string.Join(
                ' ',
                root.GetProperty("set")
                    .EnumerateObject()
                    .Select(variable => $"{variable.Name}={variable.Value.GetString()}")
            )
            .Should()
            .Be(
                target == "provider-sync-planner"
                    ? "QUICKER_SKIP_DOCKER=1 QUICKER_SYNC_FUZZ_SEEDS=100 QUICKER_SYNC_CAPNET_SEEDS=500"
                    : "",
                "実行の環境に立てた変数を書く"
            );
        root.GetProperty("removed")
            .EnumerateArray()
            .Select(variable => variable.GetString())
            .Should()
            .Equal(
                target == "provider-sync-planner" ? ["QUICKER_REQUIRE_DOCKER"] : [],
                "実行の環境に外した変数を書く"
            );
    }

    /// <summary>
    /// 起動スクリプトは、無い範囲を Stryker を呼ぶ前に断り、写しも作らない。表に流すテストの無い範囲を <c>-TestsOnly</c> で呼んでも、
    /// 何もせずに断る（CI の範囲の取り違えを「何もしないで緑」にしない）
    /// </summary>
    [Theory(
        DisplayName = "run-mutation.ps1 は無い範囲と、-TestsOnly で流すテストを表に持たない範囲を、dotnet を呼ぶ前に断る"
    )]
    [InlineData("no-such-target", false)]
    [InlineData("model", true)]
    public void MutationScript_RefusesUnknownTarget(string target, bool testsOnly)
    {
        var run = RunMutationScript(target, strykerExitCode: 0, testsOnly);

        run.ExitCode.Should().NotBe(0, run.Output);
        run.Arguments.Should().BeEmpty("断る範囲では dotnet を呼ばない");
        run.CopyLeftBehind.Should().BeFalse();
        run.EnvironmentAfter.Should().Be(MutationScriptParentEnvironment, "環境を変えない");
    }

    /// <summary>
    /// 変異テストのジョブは Stryker の後に、前回の要約をキャッシュから戻し、地図の単位ごとに要約し（QuickER.Tests の入口を環境変数で呼ぶ）、
    /// 要約を次の実行へ渡す
    /// </summary>
    /// <remarks>
    /// 入口の絞り込みが外れると 0 件で終了コード 8（要約の手順が赤）、環境変数が欠けると入口は何もしないで緑になり要約が黙って消える。
    /// キャッシュの戻しと保存は同じ path と鍵の形（範囲ごと）でないと、前回の要約が見つからず増減が毎回「前回なし」になる。
    /// </remarks>
    [Fact(
        DisplayName = "mutation.yml は Stryker の後に前回の要約を戻し、地図の単位ごとに要約し、要約を次の実行へ渡す"
    )]
    public void Mutation_SummarizesByImpactMapUnit()
    {
        var steps = JobSteps(Jobs(ReadWorkflow("mutation.yml"))["mutation"]);
        var stryker = steps.FindIndex(step =>
            step.Contains(MutationScriptCall, StringComparison.Ordinal)
        );
        var restore = steps.FindIndex(step =>
            step.Contains("uses: actions/cache/restore@", StringComparison.Ordinal)
        );
        var summary = steps.FindIndex(step =>
            step.Contains(
                $"dotnet test tests/QuickER.Tests/QuickER.Tests.csproj --filter \"FullyQualifiedName~{nameof(TestSupport.MutationSummaryTests)}.{nameof(TestSupport.MutationSummaryTests.WriteSummary_FromEnvironment)}\"",
                StringComparison.Ordinal
            )
        );
        var save = steps.FindIndex(step =>
            step.Contains("uses: actions/cache/save@", StringComparison.Ordinal)
        );

        new[] { stryker, restore, summary, save }
            .Should()
            .BeInAscendingOrder("Stryker → 前回の要約を戻す → 要約する → 次の実行へ渡す")
            .And.NotContain(-1, "4 つの手順がそろっている");

        const string summaryDirectory = "${{ runner.temp }}/stryker/${{ matrix.target }}/summary";
        const string key = "mutation-summary-${{ matrix.target }}-${{ github.run_id }}";

        foreach (var (step, name) in new[] { (steps[restore], "戻し"), (steps[save], "保存") })
        {
            Regex
                .IsMatch(step, $@"(?m)^\s+path: {Regex.Escape(summaryDirectory)}\s*$")
                .Should()
                .BeTrue($"{name}は要約のフォルダを指す");
            Regex
                .IsMatch(step, $@"(?m)^\s+key: {Regex.Escape(key)}\s*$")
                .Should()
                .BeTrue($"{name}は範囲ごとの鍵を使う");
        }

        Regex
            .IsMatch(
                steps[restore],
                @"(?m)^\s+restore-keys: mutation-summary-\$\{\{ matrix\.target \}\}-\s*$"
            )
            .Should()
            .BeTrue("戻しは同じ範囲のいちばん新しい要約を前置きの鍵で探す");
        Regex
            .Match(steps[restore], @"actions/cache/restore@(?<pin>\S+ # \S+)")
            .Groups["pin"]
            .Value.Should()
            .Be(
                Regex
                    .Match(steps[save], @"actions/cache/save@(?<pin>\S+ # \S+)")
                    .Groups["pin"]
                    .Value,
                "戻しと保存は同じ版の actions/cache を使う"
            );

        foreach (
            var (variable, value) in new[]
            {
                (
                    TestSupport.MutationSummaryTests.ReportDirectoryVariable,
                    MutationResultsDirectory
                ),
                (TestSupport.MutationSummaryTests.TargetVariable, "${{ matrix.target }}"),
                (TestSupport.MutationSummaryTests.SummaryDirectoryVariable, summaryDirectory),
            }
        )
        {
            Regex
                .IsMatch(steps[summary], $@"(?m)^\s+{variable}: {Regex.Escape(value)}\s*$")
                .Should()
                .BeTrue($"要約の手順は {variable} を {value} にする（欠けると入口が何もしない）");
        }
    }

    /// <summary>CI は単発のテストの増減を報告し、数では赤にしない</summary>
    /// <remarks>
    /// build-and-test が、ビルドの後・テストの前に「基点の数を戻す → 数えて Step Summary に書く → main への push なら残す」を持つ。
    /// 数えの入口は数で表明しない（<see cref="TestSupport.OneOffTestCensusTests"/> の行 <c>oneoff-report-large-increase-only-reports</c>）ので、
    /// 段は入口が例外を出さない限り緑になる。段に continue-on-error を付けない（数えられないときは赤にする）。
    /// </remarks>
    [Fact(
        DisplayName = "ci.yml の build-and-test は単発のテストの増減を報告し、数では赤にしない（基点の数を戻し、数え、push なら残す）"
    )]
    public void OneOffTests_ReportedWithoutFailingOnCounts()
    {
        var steps = JobSteps(Jobs(ReadWorkflow("ci.yml"))["build-and-test"]);
        var build = steps.FindIndex(step =>
            step.Contains("run: dotnet build QuickER.slnx", StringComparison.Ordinal)
        );
        var restore = steps.FindIndex(step =>
            step.Contains("uses: actions/cache/restore@", StringComparison.Ordinal)
        );
        var census = steps.FindIndex(step =>
            step.Contains(
                $"dotnet test tests/QuickER.Tests/QuickER.Tests.csproj --no-build --filter \"FullyQualifiedName~{nameof(TestSupport.OneOffTestCensusTests)}.{nameof(TestSupport.OneOffTestCensusTests.WriteSummary_FromEnvironment)}\"",
                StringComparison.Ordinal
            )
        );
        var save = steps.FindIndex(step =>
            step.Contains("uses: actions/cache/save@", StringComparison.Ordinal)
        );
        var test = steps.FindIndex(step =>
            step.Contains("dotnet test QuickER.slnx", StringComparison.Ordinal)
        );

        new[] { build, restore, census, save, test }
            .Should()
            .BeInAscendingOrder("ビルド → 基点の数を戻す → 数えて報告する → 残す → テスト")
            .And.NotContain(-1, "5 つの手順がそろっている");

        const string directory = "${{ runner.temp }}/oneoff-tests";
        const string baseCommit =
            "${{ github.event_name == 'pull_request' && github.event.pull_request.base.sha || github.event.before }}";

        foreach (var (step, name) in new[] { (steps[restore], "戻し"), (steps[save], "保存") })
        {
            Regex
                .IsMatch(step, $@"(?m)^\s+path: {Regex.Escape(directory)}\s*$")
                .Should()
                .BeTrue($"{name}は同じパスを使う（キャッシュはパスが違うと当たらない）");
            Regex
                .IsMatch(step, @"actions/cache/(restore|save)@[0-9a-f]{40} # v\S+")
                .Should()
                .BeTrue($"{name}のアクションは commit SHA で固定する");
        }

        Regex
            .IsMatch(steps[restore], $@"(?m)^\s+key: oneoff-tests-{Regex.Escape(baseCommit)}\s*$")
            .Should()
            .BeTrue("戻しは基点（PR は向き先の先端・push は直前のコミット）の鍵で探す");
        Regex
            .IsMatch(steps[restore], @"(?m)^\s+restore-keys: oneoff-tests-\s*$")
            .Should()
            .BeTrue("基点の数が無ければ、前置きの鍵でいちばん新しい数を戻す（報告は近似と書く）");
        Regex
            .IsMatch(steps[save], @"(?m)^\s+key: oneoff-tests-\$\{\{ github\.sha \}\}\s*$")
            .Should()
            .BeTrue("保存はこのコミットの鍵");
        Regex
            .IsMatch(steps[save], @"(?m)^\s+if: github\.event_name == 'push'\s*$")
            .Should()
            .BeTrue("PR の数は main から読めないので、push のときだけ残す");
        Regex
            .Match(steps[restore], @"actions/cache/restore@(?<pin>\S+ # \S+)")
            .Groups["pin"]
            .Value.Should()
            .Be(
                Regex
                    .Match(steps[save], @"actions/cache/save@(?<pin>\S+ # \S+)")
                    .Groups["pin"]
                    .Value,
                "戻しと保存は同じ版の actions/cache を使う"
            );

        foreach (
            var (variable, value) in new[]
            {
                (TestSupport.OneOffTestCensusTests.DirectoryVariable, directory),
                (TestSupport.OneOffTestCensusTests.CommitVariable, "${{ github.sha }}"),
                (TestSupport.OneOffTestCensusTests.BaseCommitVariable, baseCommit),
            }
        )
        {
            Regex
                .IsMatch(steps[census], $@"(?m)^\s+{variable}: {Regex.Escape(value)}\s*$")
                .Should()
                .BeTrue(
                    $"数えの手順は {variable} を {value} にする（欠けると入口が何もしないか、基点を名指しできない）"
                );
        }

        foreach (
            var (step, name) in new[]
            {
                (steps[restore], "戻し"),
                (steps[census], "数え"),
                (steps[save], "保存"),
            }
        )
        {
            Regex
                .IsMatch(step, @"(?m)^\s+continue-on-error:")
                .Should()
                .BeFalse(
                    $"{name}の手順は continue-on-error に頼らない（数えられないときは赤にし、数では入口が赤にしない）"
                );
        }

        steps[census]
            .Should()
            .NotContain(
                "--ignore-exit-code",
                "数えの入口の失敗（道具が動かない）を緑に化けさせない"
            );
    }

    /// <summary>毎晩の変異テストの cron</summary>
    internal const string MutationNightlyCron = "30 18 * * *";

    /// <summary>週 1 回の変異テストの cron</summary>
    internal const string MutationWeeklyCron = "0 20 * * 6";

    /// <summary>CI の Windows のテストが TRX を書くフォルダ（テストの手順と保存の手順で同じ値）</summary>
    private const string CiResultsDirectory = "${{ runner.temp }}/test-results";

    /// <summary>変異テストが結果を書くフォルダ（Stryker の手順と保存の手順で同じ値）</summary>
    private const string MutationResultsDirectory =
        "${{ runner.temp }}/stryker/${{ matrix.target }}";

    /// <summary>変異テストの起動スクリプトの呼び方（mutation.yml の手順と手元の実行が呼ぶ）</summary>
    private const string MutationScriptCall = "./scripts/run-mutation.ps1";

    /// <summary>起動スクリプトが実行の間だけルートに作るテストだけのソリューションの写しの名前</summary>
    private const string MutationSolutionCopy = "QuickER.TestsOnly.stryker.slnx";

    /// <summary>Stryker のテストだけのソリューション（正本）のプロジェクトのパスの並び</summary>
    private static IReadOnlyList<string> StrykerSolutionProjects(string solution) =>
        ProjectPathsOf(Path.Combine(Root, "stryker", solution));

    /// <summary>起動スクリプトの範囲ごとの表が立てる・外す環境変数（偽の dotnet と呼び出し元が記録する順）</summary>
    private static readonly string[] MutationScriptVariables =
    [
        "QUICKER_REQUIRE_DOCKER",
        "QUICKER_SKIP_DOCKER",
        "QUICKER_SYNC_FUZZ_SEEDS",
        "QUICKER_SYNC_CAPNET_SEEDS",
    ];

    /// <summary>起動スクリプトを呼ぶ前に呼び出し元が立てておく環境（厳格モードと種の数。ほかは立てない）</summary>
    private const string MutationScriptParentEnvironment =
        "QUICKER_REQUIRE_DOCKER=1 QUICKER_SKIP_DOCKER= QUICKER_SYNC_FUZZ_SEEDS=7 QUICKER_SYNC_CAPNET_SEEDS=";

    /// <summary>.slnx のプロジェクトのパスの並び（書かれた順）</summary>
    private static IReadOnlyList<string> ProjectPathsOf(string solutionPath) =>
        [
            .. System
                .Xml.Linq.XDocument.Load(solutionPath)
                .Descendants("Project")
                .Select(project => (string?)project.Attribute("Path") ?? ""),
        ];

    /// <summary>ジョブが path の結果を、if: always() と保持日数つきの upload-artifact（ちょうど 1 つ）で残すことを表明する</summary>
    private static void AssertKeptArtifact(string jobBody, string path, string job)
    {
        var uploads = JobSteps(jobBody)
            .Where(step =>
                step.Contains("uses: actions/upload-artifact@", StringComparison.Ordinal)
            )
            .ToList();

        uploads.Should().ContainSingle($"{job} に結果を保存する手順がちょうど 1 つある");
        Regex
            .IsMatch(uploads[0], @"(?m)^\s+if: always\(\)\s*$")
            .Should()
            .BeTrue($"{job} は失敗した実行にも結果を残す");
        Regex
            .IsMatch(uploads[0], $@"(?m)^\s+path: {Regex.Escape(path)}\s*$")
            .Should()
            .BeTrue($"{job} は手順が書いた結果を保存する");
        Regex
            .IsMatch(uploads[0], @"(?m)^\s+retention-days: [1-9][0-9]*\s*$")
            .Should()
            .BeTrue($"{job} は保持日数を持つ");
    }

    /// <summary>リリースのタグはビルドしたコミットに付ける</summary>
    [Fact(DisplayName = "release.yml の gh release create はタグをビルドしたコミットへ付ける")]
    public void ReleaseTag_TargetsBuiltCommit()
    {
        Regex
            .Matches(ReadWorkflow("release.yml"), @"gh release create[^\r\n]*")
            .Select(match => match.Value)
            .Should()
            .ContainSingle()
            .Which.Should()
            .Contain("--target \"${{ github.sha }}\"");
    }

    /// <summary>既存リリースの照会の失敗を「既存リリースなし」と取り違えない</summary>
    /// <remarks>
    /// 取り違えると差分更新用の取得を飛ばし、差分更新パッケージの無いリリースが成功扱いで出る。
    /// GitHub Actions の pwsh はネイティブコマンドの非 0 で止まらないので、直後に終了コードを見る。
    /// </remarks>
    [Fact(DisplayName = "release.yml は既存リリースの照会（gh api）の失敗で止まる")]
    public void ExistingReleaseLookup_FailsOnGhApiError()
    {
        ReadWorkflow("release.yml")
            .Should()
            .MatchRegex(
                @"gh api [^\r\n]*\r?\n(?:[ ]*#[^\r\n]*\r?\n)*[ ]*if \(\$LASTEXITCODE -ne 0\) \{[^\r\n]*exit 1 \}"
            );
    }

    /// <summary>公開済みの版の確認は本番の公開だけで push の前に走り、push は --skip-duplicate を残す</summary>
    [Fact(
        DisplayName = "publish.yml は push の前に公開済みの版を確認し、push は --skip-duplicate のまま"
    )]
    public void Publish_ChecksPublishedVersionBeforePush()
    {
        var workflow = ReadWorkflow("publish.yml");
        var check = Regex.Match(
            workflow,
            @"if: \$\{\{ inputs\.dry_run == false \}\}\r?\n[ ]*run: \./scripts/check-unpublished-version\.ps1 -NupkgDirectory "
        );
        var push = workflow.IndexOf("dotnet nuget push", StringComparison.Ordinal);

        check
            .Success.Should()
            .BeTrue(
                "確認は dry_run=false のときだけ走る（dry_run は nuget.org の状態に依存しない）"
            );
        push.Should().BeGreaterThan(check.Index, "確認は push より前に置く");
        workflow.Should().Contain("--skip-duplicate", "途中で失敗した公開をやり直せるように残す");
    }

    /// <summary>公開済みの版の確認は、全部が公開済みのときと照会できないときだけ止める</summary>
    /// <remarks>
    /// 全部が公開済みなら VersionPrefix の上げ忘れ（push の --skip-duplicate が全部を飛ばして成功になる）。
    /// 一部だけなら途中で失敗した公開のやり直しなので続ける。照会の失敗は判断できないので止める。
    /// </remarks>
    [Theory(
        DisplayName = "check-unpublished-version.ps1 は全部が公開済みのときと照会の失敗だけで止まる"
    )]
    [InlineData("all-published", false)]
    [InlineData("partially-published", true)]
    [InlineData("none-published", true)]
    [InlineData("other-version-published", true)]
    [InlineData("feed-error", false)]
    public void CheckUnpublishedVersion_StopsOnlyWhenEverythingIsPublished(
        string scenario,
        bool shouldContinue
    )
    {
        // 公開するのは QuickER.Alpha 1.2.0 と QuickER.Beta 1.2.0 の 2 本。値は flat container の versions
        // （null は 500 を返す・キーが無ければ 404 を返す）
        var feed = scenario switch
        {
            "all-published" => new Dictionary<string, string[]?>
            {
                ["quicker.alpha"] = ["1.1.0", "1.2.0"],
                ["quicker.beta"] = ["1.2.0"],
            },
            "partially-published" => new Dictionary<string, string[]?>
            {
                ["quicker.alpha"] = ["1.2.0"],
            },
            "none-published" => new Dictionary<string, string[]?>(),
            "other-version-published" => new Dictionary<string, string[]?>
            {
                ["quicker.alpha"] = ["1.1.0"],
                ["quicker.beta"] = ["1.1.0"],
            },
            "feed-error" => new Dictionary<string, string[]?> { ["quicker.alpha"] = null },
            _ => throw new ArgumentOutOfRangeException(nameof(scenario)),
        };

        var run = RunCheckUnpublishedVersion(
            feed,
            ("QuickER.Alpha", "1.2.0"),
            ("QuickER.Beta", "1.2.0")
        );

        if (shouldContinue)
        {
            run.ExitCode.Should().Be(0, run.Output);
        }
        else
        {
            run.ExitCode.Should().NotBe(0, run.Output);
        }

        if (scenario == "partially-published")
        {
            // 公開済みの名前を出してから続ける（push で飛ばされるものが分かる）
            run.Output.Should().Contain("やり直し").And.Contain("QuickER.Alpha 1.2.0");
        }
    }

    /// <summary>夜間のワークフローのジョブと、それが流すテストのプロジェクト</summary>
    internal static readonly IReadOnlyDictionary<string, string> NightlyJobs = new Dictionary<
        string,
        string
    >
    {
        ["integration-nightly"] =
            "tests/QuickER.Tests.Integration/QuickER.Tests.Integration.csproj",
        ["unit-nightly"] = "tests/QuickER.Tests/QuickER.Tests.csproj",
    };

    /// <summary>夜間のジョブが TRX を書くフォルダ（テストの手順と保存の手順で同じ値）</summary>
    private const string NightlyResultsDirectory = "${{ runner.temp }}/nightly-results";

    /// <summary>手順の本文が TEMP と TMP をランナーの一時領域へ向けていることを表明する（ci.yml と nightly.yml で共有）</summary>
    private static void AssertTempOnRunnerTemp(string stepBody)
    {
        Regex
            .IsMatch(stepBody, @"(?m)^\s+TEMP: \$\{\{ runner\.temp \}\}$")
            .Should()
            .BeTrue("TEMP はランナーの一時領域を指す");
        Regex
            .IsMatch(stepBody, @"(?m)^\s+TMP: \$\{\{ runner\.temp \}\}$")
            .Should()
            .BeTrue("TMP はランナーの一時領域を指す");
    }

    /// <summary>ワークフローの uses の値（出てくる順）</summary>
    internal static List<string> UsesLines(string workflow) =>
        Regex
            .Matches(workflow, @"(?m)^\s*(?:-\s+)?uses:\s*(?<value>\S[^\r\n]*?)\s*$")
            .Select(match => match.Groups["value"].Value)
            .ToList();

    /// <summary>最上位のキーの本文の行（次の最上位のキーの手前まで）</summary>
    internal static List<string> TopLevelBlock(string workflow, string key)
    {
        var lines = workflow.ReplaceLineEndings("\n").Split('\n');
        var start = Array.FindIndex(lines, line => line == key + ":");

        start.Should().BeGreaterThanOrEqualTo(0, $"最上位のキー {key} がある");

        return lines
            .Skip(start + 1)
            .TakeWhile(line => line.Length == 0 || line[0] == ' ' || line[0] == '#')
            .ToList();
    }

    /// <summary>最上位のキーの直下（字下げ 2）のキー</summary>
    private static List<string> TopLevelBlockKeys(string workflow, string key) =>
        TopLevelBlock(workflow, key)
            .Select(line => Regex.Match(line, @"^  (?<key>[\w-]+):"))
            .Where(match => match.Success)
            .Select(match => match.Groups["key"].Value)
            .ToList();

    /// <summary>jobs のジョブごとの本文（ジョブ名 → 本文）</summary>
    internal static Dictionary<string, string> Jobs(string workflow)
    {
        var jobs = new Dictionary<string, string>(StringComparer.Ordinal);
        string? current = null;
        var body = new StringBuilder();

        foreach (var line in TopLevelBlock(workflow, "jobs"))
        {
            var header = Regex.Match(line, @"^  (?<job>[\w-]+):\s*$");

            if (header.Success)
            {
                if (current is not null)
                {
                    jobs[current] = body.ToString();
                }

                current = header.Groups["job"].Value;
                body.Clear();
                continue;
            }

            body.Append(line).Append('\n');
        }

        if (current is not null)
        {
            jobs[current] = body.ToString();
        }

        return jobs;
    }

    /// <summary>ジョブの steps を手順ごとの本文に分ける（字下げ 6 の <c>- </c> で始まる行から次の手順の手前まで）</summary>
    private static List<string> JobSteps(string jobBody) =>
        Regex.Split(jobBody, @"(?m)^(?=      - )").Skip(1).ToList();

    /// <summary>ジョブの中の dotnet test の手順（ちょうど 1 つ）の本文</summary>
    internal static string NightlyTestStep(string jobBody)
    {
        var steps = JobSteps(jobBody)
            .Where(step => step.Contains("dotnet test", StringComparison.Ordinal))
            .ToList();

        steps.Should().ContainSingle("ジョブの dotnet test の手順はちょうど 1 つ");

        return steps[0];
    }

    /// <summary>ワークフローの本文を読む</summary>
    internal static string ReadWorkflow(string fileName) =>
        File.ReadAllText(Path.Combine(Root, ".github", "workflows", fileName));

    /// <summary>pack-packages.ps1 の一覧に並ぶ csproj のファイル名を、並び順のまま読む</summary>
    private static IReadOnlyList<string> PackScriptProjectFileNames()
    {
        var names = Regex
            .Matches(File.ReadAllText(PackScriptPath), @"'src/[^/']+/(?<name>[^/']+\.csproj)'")
            .Select(match => match.Groups["name"].Value)
            .ToList();

        names.Should().NotBeEmpty("pack-packages.ps1 から一覧を読み取れなかった");

        return names;
    }

    /// <summary>ワークフローの paths-ignore の一覧を、出てくる順にすべて読む</summary>
    private static List<List<string>> PathsIgnoreLists(string workflow)
    {
        var lists = new List<List<string>>();
        List<string>? current = null;

        foreach (var line in workflow.Split('\n').Select(l => l.TrimEnd('\r')))
        {
            if (line.Trim() == "paths-ignore:")
            {
                current = [];
                lists.Add(current);
                continue;
            }

            if (current is null)
            {
                continue;
            }

            var item = Regex.Match(line, @"^\s*-\s*""(?<pattern>[^""]+)""\s*$");

            if (item.Success)
            {
                current.Add(item.Groups["pattern"].Value);
            }
            else if (!line.TrimStart().StartsWith('#'))
            {
                current = null;
            }
        }

        return lists;
    }

    /// <summary>GitHub Actions のパスのパターンを正規表現へ変える（<c>**</c> と <c>*</c> だけを扱う）</summary>
    private static Regex GlobToRegex(string pattern)
    {
        var body = Regex
            .Escape(pattern)
            .Replace(@"\*\*", "\u0000")
            .Replace(@"\*", "[^/]*")
            .Replace("\u0000", ".*");

        return new Regex($"^{body}$");
    }

    /// <summary>変異テストの起動スクリプトを偽の dotnet で動かした結果</summary>
    /// <param name="ExitCode">スクリプトの終了コード</param>
    /// <param name="Output">スクリプトの出力</param>
    /// <param name="RepositoryRoot">偽のリポジトリのルート</param>
    /// <param name="OutputDirectory">結果のフォルダとして解かれるはずの絶対パス</param>
    /// <param name="WorkingDirectory">偽の dotnet が呼ばれたときの作業フォルダ（呼ばれなければ空）</param>
    /// <param name="Arguments">偽の dotnet が受けた引数（空白でつなぐ・呼ばれなければ空）</param>
    /// <param name="CopiedProjects">偽の dotnet が呼ばれたときにルートにあった写しのプロジェクトのパス</param>
    /// <param name="CopyLeftBehind">スクリプトの後に写しがルートに残っているか</param>
    /// <param name="EnvironmentAtStryker">偽の dotnet が見た表の変数（「名前=値」を空白でつなぐ・呼ばれなければ空）</param>
    /// <param name="EnvironmentAfter">スクリプトの後の呼び出し元の表の変数（同じ形）</param>
    /// <param name="RunEnvironmentJson">結果のフォルダの run-environment.json の本文（無ければ null）</param>
    private sealed record MutationScriptRun(
        int ExitCode,
        string Output,
        string RepositoryRoot,
        string OutputDirectory,
        string WorkingDirectory,
        string Arguments,
        IReadOnlyList<string> CopiedProjects,
        bool CopyLeftBehind,
        string EnvironmentAtStryker,
        string EnvironmentAfter,
        string? RunEnvironmentJson
    );

    /// <summary>
    /// 一時フォルダに偽のリポジトリ（起動スクリプト・テストだけのソリューションの正本 2 本・範囲 model と provider-sync-planner の設定）を作り、
    /// その外の作業フォルダから相対パスの結果のフォルダと並列度 3 で起動スクリプトを動かす。
    /// 呼び出し元は <see cref="MutationScriptParentEnvironment"/> の環境を立てて呼び、スクリプトの後の環境を記録する（失敗しても）。
    /// 偽の dotnet は作業フォルダ・引数（1 つずつ）・その時点の写し・表の変数を記録し、<paramref name="strykerExitCode"/> で終わる。
    /// <paramref name="testsOnly"/> なら並列度の代わりに <c>-TestsOnly</c> を渡す。
    /// </summary>
    private static MutationScriptRun RunMutationScript(
        string target,
        int strykerExitCode,
        bool testsOnly
    )
    {
        using var temp = new TestSupport.TestTempFolder();
        var repository = Path.Combine(temp.Path, "repo");
        var fakeBinDirectory = Path.Combine(temp.Path, "bin");
        var workingLog = Path.Combine(temp.Path, "wd.txt");
        var argumentsLog = Path.Combine(temp.Path, "args.txt");
        var environmentLog = Path.Combine(temp.Path, "env.txt");
        var environmentAfterLog = Path.Combine(temp.Path, "env-after.txt");
        var copySnapshot = Path.Combine(temp.Path, "copy.slnx");

        Directory.CreateDirectory(Path.Combine(repository, "scripts"));
        Directory.CreateDirectory(Path.Combine(repository, "stryker"));
        Directory.CreateDirectory(fakeBinDirectory);
        File.Copy(
            Path.Combine(Root, "scripts", "run-mutation.ps1"),
            Path.Combine(repository, "scripts", "run-mutation.ps1")
        );

        foreach (
            var solution in new[] { "QuickER.TestsOnly.slnx", "QuickER.TestsWithIntegration.slnx" }
        )
        {
            File.Copy(
                Path.Combine(Root, "stryker", solution),
                Path.Combine(repository, "stryker", solution)
            );
        }

        File.WriteAllText(Path.Combine(repository, "stryker", "model.json"), "{}");
        File.WriteAllText(Path.Combine(repository, "stryker", "provider-sync-planner.json"), "{}");

        // 引数は 1 行に 1 つ（引用の付け方に依らず値を比べる）。写しは呼ばれた時点のものを取っておく。
        // 表の変数は「名前=値」で 1 行に 1 つ（立っていない変数はバッチファイルの中で空に展開される）
        File.WriteAllText(
            Path.Combine(fakeBinDirectory, "dotnet.cmd"),
            "@echo off\r\n"
                + $">\"{workingLog}\" cd\r\n"
                + $"if exist \"{MutationSolutionCopy}\" copy /Y \"{MutationSolutionCopy}\" \"{copySnapshot}\" >nul\r\n"
                + string.Concat(
                    MutationScriptVariables.Select(name =>
                        $">>\"{environmentLog}\" echo {name}=%{name}%\r\n"
                    )
                )
                + ":next\r\n"
                + "if \"%~1\"==\"\" goto done\r\n"
                + $">>\"{argumentsLog}\" echo %~1\r\n"
                + "shift\r\n"
                + "goto next\r\n"
                + ":done\r\n"
                + $"exit /b {strykerExitCode}\r\n"
        );

        // 呼び出し元の環境を立て、スクリプトの後（失敗しても）の環境を記録する。
        // 後ろで LASTEXITCODE を 0 に戻し、偽の dotnet の終了コードの持ち越しでなく、スクリプト自身が失敗を伝えることを見る
        // （pwsh -File で手元から呼ぶと、ネイティブコマンドの非 0 は終了コードにならない）
        var parent = string.Concat(
            MutationScriptParentEnvironment
                .Split(' ')
                .Select(pair => pair.Split('=', 2))
                .Select(pair =>
                    pair[1].Length == 0
                        ? $"Remove-Item -LiteralPath env:{pair[0]} -ErrorAction SilentlyContinue; "
                        : $"$env:{pair[0]} = '{pair[1]}'; "
                )
        );
        var record = string.Join(
            "; ",
            MutationScriptVariables.Select(name =>
                $"Add-Content -LiteralPath '{environmentAfterLog}' -Value \"{name}=$env:{name}\""
            )
        );
        var step = RunStep(
            temp.Path,
            $"{parent}try {{ ./repo/scripts/run-mutation.ps1 -Target {target} -OutputDirectory out {(testsOnly ? "-TestsOnly" : "-Concurrency 3")} }} finally {{ {record} }}; $global:LASTEXITCODE = 0",
            fakeBinDirectory
        );
        var runEnvironment = Path.Combine(temp.Path, "out", "run-environment.json");

        return new MutationScriptRun(
            step.ExitCode,
            step.Output,
            repository,
            Path.Combine(temp.Path, "out"),
            File.Exists(workingLog) ? File.ReadAllText(workingLog).Trim() : "",
            File.Exists(argumentsLog)
                ? string.Join(' ', File.ReadAllLines(argumentsLog).Select(line => line.Trim()))
                : "",
            File.Exists(copySnapshot) ? ProjectPathsOf(copySnapshot) : [],
            File.Exists(Path.Combine(repository, MutationSolutionCopy)),
            JoinedLines(environmentLog),
            JoinedLines(environmentAfterLog),
            File.Exists(runEnvironment) ? File.ReadAllText(runEnvironment) : null
        );

        static string JoinedLines(string path) =>
            File.Exists(path)
                ? string.Join(' ', File.ReadAllLines(path).Select(line => line.Trim()))
                : "";
    }

    /// <summary>パック用スクリプトを、偽の dotnet と偽の検証スクリプトで動かした結果</summary>
    private sealed record PackScriptRun(
        int ExitCode,
        string Output,
        IReadOnlyList<string> PackedProjects,
        string VerifiedCount
    );

    /// <summary>
    /// 一時フォルダへ写したパック用スクリプトを pwsh で動かす。
    /// 偽の dotnet は <paramref name="failingProject"/> のパックだけを失敗させ、
    /// 偽の検証スクリプトは渡された本数を記録して <paramref name="verifyExitCode"/> で終わる。
    /// <paramref name="transformScript"/> を渡すと、写したスクリプトの本文をそれで書き換えてから動かす。
    /// </summary>
    private static PackScriptRun RunPackScript(
        string failingProject,
        int verifyExitCode,
        Func<string, string>? transformScript = null
    )
    {
        var workDirectory = Path.Combine(
            Path.GetTempPath(),
            "QuickER",
            "pack-script-test",
            Guid.NewGuid().ToString("N")
        );
        var scriptsDirectory = Path.Combine(workDirectory, "scripts");
        var fakeBinDirectory = Path.Combine(workDirectory, "bin");
        var packLog = Path.Combine(workDirectory, "packed.txt");
        var verifyLog = Path.Combine(workDirectory, "verified.txt");

        Directory.CreateDirectory(scriptsDirectory);
        Directory.CreateDirectory(fakeBinDirectory);

        try
        {
            var script = File.ReadAllText(PackScriptPath);

            File.WriteAllText(
                Path.Combine(scriptsDirectory, "pack-packages.ps1"),
                transformScript is null ? script : transformScript(script)
            );

            File.WriteAllText(
                Path.Combine(scriptsDirectory, "verify-packages.ps1"),
                "param([string]$NupkgDirectory, [int]$ExpectedPackageCount)\r\n"
                    + $"Set-Content -LiteralPath '{verifyLog}' -Value $ExpectedPackageCount\r\n"
                    + $"exit {verifyExitCode}\r\n"
            );

            // 第 2 引数がパックする csproj のパス。ファイル名を記録し、指定のものだけ失敗させる
            File.WriteAllText(
                Path.Combine(fakeBinDirectory, "dotnet.cmd"),
                "@echo off\r\n"
                    + $">>\"{packLog}\" echo %~nx2\r\n"
                    + $"if /I \"%~nx2\"==\"{failingProject}\" exit /b 1\r\n"
                    + "exit /b 0\r\n"
            );

            var step = RunStep(
                workDirectory,
                $"./scripts/pack-packages.ps1 -OutputDirectory '{Path.Combine(workDirectory, "nupkg")}'",
                fakeBinDirectory
            );

            return new PackScriptRun(
                step.ExitCode,
                step.Output,
                File.Exists(packLog)
                    ? File.ReadAllLines(packLog).Select(line => line.Trim()).ToList()
                    : [],
                File.Exists(verifyLog) ? File.ReadAllText(verifyLog).Trim() : ""
            );
        }
        finally
        {
            Directory.Delete(workDirectory, recursive: true);
        }
    }

    /// <summary>
    /// 偽の flat container（localhost）を立て、指定の .nupkg を置いたフォルダで公開済みの版の確認を動かす。
    /// <paramref name="feed"/> に無い id は 404、値が null の id は 500 を返す。
    /// </summary>
    private static (int ExitCode, string Output) RunCheckUnpublishedVersion(
        IReadOnlyDictionary<string, string[]?> feed,
        params (string Id, string Version)[] packages
    )
    {
        var workDirectory = Path.Combine(
            Path.GetTempPath(),
            "QuickER",
            "check-version-test",
            Guid.NewGuid().ToString("N")
        );
        var scriptsDirectory = Path.Combine(workDirectory, "scripts");
        var nupkgDirectory = Path.Combine(workDirectory, "nupkg");

        Directory.CreateDirectory(scriptsDirectory);
        Directory.CreateDirectory(nupkgDirectory);

        var port = FreeLoopbackPort();
        using var listener = new HttpListener();
        listener.Prefixes.Add($"http://localhost:{port}/");
        listener.Start();
        var serving = ServeFeedAsync(listener, feed);

        try
        {
            File.Copy(
                Path.Combine(Root, "scripts", "check-unpublished-version.ps1"),
                Path.Combine(scriptsDirectory, "check-unpublished-version.ps1")
            );

            foreach (var (id, version) in packages)
            {
                WriteNupkg(Path.Combine(nupkgDirectory, $"{id}.{version}.nupkg"), id, version);
            }

            return RunStep(
                workDirectory,
                $"./scripts/check-unpublished-version.ps1 -NupkgDirectory '{nupkgDirectory}' -FeedBaseUrl 'http://localhost:{port}'"
            );
        }
        finally
        {
            listener.Stop();
            serving.Wait(TimeSpan.FromSeconds(10));
            Directory.Delete(workDirectory, recursive: true);
        }
    }

    /// <summary>flat container の index.json を返す（id は URL の最初の区切り）</summary>
    private static async Task ServeFeedAsync(
        HttpListener listener,
        IReadOnlyDictionary<string, string[]?> feed
    )
    {
        while (listener.IsListening)
        {
            HttpListenerContext context;

            try
            {
                context = await listener.GetContextAsync();
            }
            catch (Exception exception)
                when (exception is HttpListenerException or ObjectDisposedException)
            {
                return;
            }

            var id = context.Request.Url!.AbsolutePath.Trim('/').Split('/')[0];

            if (!feed.TryGetValue(id, out var versions))
            {
                context.Response.StatusCode = 404;
            }
            else if (versions is null)
            {
                context.Response.StatusCode = 500;
            }
            else
            {
                var body = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { versions }));
                context.Response.StatusCode = 200;
                context.Response.ContentType = "application/json";
                await context.Response.OutputStream.WriteAsync(body);
            }

            context.Response.Close();
        }
    }

    /// <summary>id と版だけを持つ .nuspec を入れた .nupkg（zip）を作る</summary>
    private static void WriteNupkg(string path, string id, string version)
    {
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        using var writer = new StreamWriter(zip.CreateEntry($"{id}.nuspec").Open());

        writer.Write(
            $"<?xml version=\"1.0\"?><package><metadata><id>{id}</id><version>{version}</version></metadata></package>"
        );
    }

    /// <summary>ループバックの空いているポートを 1 つ選ぶ</summary>
    private static int FreeLoopbackPort()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();

        try
        {
            return ((IPEndPoint)probe.LocalEndpoint).Port;
        }
        finally
        {
            probe.Stop();
        }
    }

    /// <summary>
    /// GitHub Actions の pwsh のステップと同じ包み方で 1 行を実行する（<c>$ErrorActionPreference = 'stop'</c>・
    /// 末尾で <c>$LASTEXITCODE</c> を終了コードにする）。出力は UTF-8 で受け取る。
    /// <paramref name="pathPrefix"/> を渡すと PATH の先頭に置く。
    /// </summary>
    private static (int ExitCode, string Output) RunStep(
        string workDirectory,
        string runLine,
        string? pathPrefix = null
    )
    {
        var stepScript = Path.Combine(workDirectory, "step.ps1");

        File.WriteAllText(
            stepScript,
            "$ErrorActionPreference = 'stop'\r\n"
                + "[Console]::OutputEncoding = [System.Text.Encoding]::UTF8\r\n"
                + runLine
                + "\r\n"
                + "if ((Test-Path -LiteralPath variable:\\LASTEXITCODE)) { exit $LASTEXITCODE }\r\n"
        );

        var startInfo = new ProcessStartInfo("pwsh")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            UseShellExecute = false,
            WorkingDirectory = workDirectory,
        };

        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-NonInteractive");
        startInfo.ArgumentList.Add("-Command");
        startInfo.ArgumentList.Add($". '{stepScript}'");

        if (pathPrefix is not null)
        {
            startInfo.Environment["PATH"] =
                pathPrefix + Path.PathSeparator + Environment.GetEnvironmentVariable("PATH");
        }

        using var process = Process.Start(startInfo)!;
        var standardError = process.StandardError.ReadToEndAsync();
        var standardOutput = process.StandardOutput.ReadToEnd();

        process
            .WaitForExit(TimeSpan.FromMinutes(1))
            .Should()
            .BeTrue("pwsh が 1 分以内に終わらない");

        return (process.ExitCode, standardOutput + standardError.Result);
    }
}
