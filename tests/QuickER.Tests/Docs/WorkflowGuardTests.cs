using System;
using System.Collections.Generic;
using System.Diagnostics;
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
/// </remarks>
public sealed class WorkflowGuardTests
{
    /// <summary>リポジトリのルート</summary>
    private static readonly string Root = NeutralResxFiles.FindRepositoryRoot();

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
        Regex
            .IsMatch(body, @"(?m)^\s+TEMP: \$\{\{ runner\.temp \}\}$")
            .Should()
            .BeTrue("TEMP はランナーの一時領域を指す");
        Regex
            .IsMatch(body, @"(?m)^\s+TMP: \$\{\{ runner\.temp \}\}$")
            .Should()
            .BeTrue("TMP はランナーの一時領域を指す");
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

    /// <summary>配布物を作る vpk は、アプリが参照する Velopack と同じ版を入れる</summary>
    /// <remarks>
    /// 版が食い違うと、配布物の形式とアプリ内の自動更新の仕組みが合わなくなり得る。
    /// その破損はリリース後の自動更新まで分からないので、Directory.Packages.props と並べて照合する。
    /// </remarks>
    [Fact(
        DisplayName = "release.yml が入れる vpk の版は Directory.Packages.props の Velopack の版と同じ"
    )]
    public void ReleaseVpk_MatchesVelopackPackageVersion()
    {
        var velopack = Regex.Match(
            File.ReadAllText(Path.Combine(Root, "Directory.Packages.props")),
            @"<PackageVersion\s+Include=""Velopack""\s+Version=""(?<version>[^""]+)""\s*/>"
        );
        velopack.Success.Should().BeTrue("Directory.Packages.props に Velopack の版がある");

        Regex
            .Matches(
                ReadWorkflow("release.yml"),
                @"dotnet tool install --global vpk --version (?<version>\S+)"
            )
            .Select(match => match.Groups["version"].Value)
            .Should()
            .ContainSingle()
            .Which.Should()
            .Be(velopack.Groups["version"].Value);
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

    /// <summary>ワークフローの本文を読む</summary>
    private static string ReadWorkflow(string fileName) =>
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
