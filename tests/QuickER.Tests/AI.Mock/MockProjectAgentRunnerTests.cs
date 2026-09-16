using System.IO;
using AwesomeAssertions;
using QuickER.AI.Mock;
using MockStrings = QuickER.AI.Mock.Resources.Strings;

namespace QuickER.Tests.AI.Mock;

/// <summary>
/// <see cref="MockProjectAgentRunner"/> のバックエンド非依存なオーケストレーション（ログ保全・成果物検証・
/// 最終ビルド分岐・タイムアウト・中断・エージェント成否の合成）を、フェイクのエージェント／ビルド検証器で
/// 検証するテストクラス。プロンプト・起動オプションはエージェント側（<see cref="ClaudeCodeMockProjectAgent"/>）で検証する。
/// </summary>
public class MockProjectAgentRunnerTests
{
    /// <summary>スクリプト化した挙動を返すフェイクのモックプロジェクトエージェント</summary>
    private sealed class FakeMockProjectAgent : IMockProjectAgent
    {
        /// <summary>RunAsync 実行時に走らせる副作用（成果物生成のシミュレーション等）</summary>
        public Action<string>? OnRun { get; set; }

        /// <summary>返すエージェント結果</summary>
        public MockProjectAgentOutcome Outcome { get; set; } = new(true, null, false);

        /// <summary>RunAsync でスローする例外（タイムアウト・中断のシミュレーション）</summary>
        public Exception? ThrowOnRun { get; set; }

        public MockProjectAgentRequest? CapturedRequest { get; private set; }
        public bool Interrupted { get; private set; }
        public bool Available { get; set; } = true;

        /// <summary>
        /// 最終ビルドが境界越えになると宣言するか（既定 false＝Claude Code / API キー相当）。
        /// </summary>
        public bool EscapesSandbox { get; set; }

        public bool IsAvailable() => Available;

        public bool FinalBuildEscapesSandbox => EscapesSandbox;

        public Task<MockProjectAgentOutcome> RunAsync(
            MockProjectAgentRequest request,
            Action<string> onProgress,
            CancellationToken cancellationToken
        )
        {
            CapturedRequest = request;
            onProgress("作業を開始します。\n");
            OnRun?.Invoke(request.WorkingDirectory);

            if (ThrowOnRun is not null)
            {
                throw ThrowOnRun;
            }

            return Task.FromResult(Outcome);
        }

        public Task InterruptAsync()
        {
            Interrupted = true;
            return Task.CompletedTask;
        }
    }

    /// <summary>スクリプト化したビルド結果を返すフェイクビルド検証器</summary>
    private sealed class FakeBuildRunner : IBuildRunner
    {
        public bool BuildSuccess { get; set; } = true;
        public bool DotnetAvailable { get; set; } = true;
        public int BuildCallCount { get; private set; }

        /// <summary>最後に渡されたビルド対象（ソリューションのフルパス）</summary>
        public string? CapturedSolutionPath { get; private set; }

        /// <summary>ビルド開始時点の状態を観測するフック（obj/bin の有無など）</summary>
        public Action? OnBuild { get; set; }

        /// <summary>
        /// ビルドが「終わらない」状況の再現（true で待たされる）。
        /// </summary>
        /// <remarks>
        /// 無限待ちにすると、専用タイムアウトを外す変異でテストが失敗でなくハングする。
        /// タイムアウト（ミリ秒単位）より十分長く、テストの実行時間としては短い値で待つ。
        /// </remarks>
        public bool SlowBuild { get; set; }

        public async Task<BuildRunResult> BuildAsync(
            string solutionFilePath,
            CancellationToken cancellationToken = default
        )
        {
            BuildCallCount++;
            CapturedSolutionPath = solutionFilePath;
            OnBuild?.Invoke();

            if (SlowBuild)
            {
                await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
            }

            return new BuildRunResult(BuildSuccess, "build output");
        }

        public Task<bool> IsDotnetAvailableAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(DotnetAvailable);
    }

    /// <summary>テストで使うプロジェクト名（スキャフォールドのレイアウト規則に合わせてフォルダを作る）</summary>
    private const string ProjectName = "AcmeMock";

    private static string NewTempFolder()
    {
        var folder = Path.Combine(Path.GetTempPath(), "QuickERTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        return folder;
    }

    private static void Cleanup(string folder)
    {
        if (Directory.Exists(folder))
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    /// <summary>プロジェクトフォルダ（<c>{出力フォルダ}/{プロジェクト名}/</c>）のパス</summary>
    private static string ProjectDirectory(string outputDirectory) =>
        Path.Combine(outputDirectory, ProjectName);

    /// <summary>成果物を書き出す副作用（プロジェクトフォルダ配下の csproj + xaml）を返す</summary>
    private static Action<string> WriteArtifacts() =>
        dir =>
        {
            var project = ProjectDirectory(dir);
            Directory.CreateDirectory(project);
            File.WriteAllText(Path.Combine(project, $"{ProjectName}.csproj"), "<Project/>");
            File.WriteAllText(Path.Combine(project, "MainWindow.xaml"), "<Window/>");
        };

    /// <summary>成功パス: 成果物あり・ビルド成功で全体成功し、進捗・ログが保全されることを検証する</summary>
    [Fact(DisplayName = "成果物あり・ビルド成功で全体成功しログを保全する")]
    public async Task RunAsync_SuccessPath()
    {
        var folder = NewTempFolder();
        var agent = new FakeMockProjectAgent { OnRun = WriteArtifacts() };
        var build = new FakeBuildRunner { BuildSuccess = true };
        var runner = new MockProjectAgentRunner(agent, build);
        var progress = new List<string>();

        try
        {
            var result = await runner.RunAsync(
                folder,
                "AcmeMock",
                additionalInstructions: null,
                "sonnet",
                progress.Add,
                TestContext.Current.CancellationToken
            );

            result.Success.Should().BeTrue();
            result.ArtifactsPresent.Should().BeTrue();
            result.BuildSucceeded.Should().BeTrue();
            build.BuildCallCount.Should().Be(1);

            // 要求はそのままエージェントへ渡る
            agent.CapturedRequest!.WorkingDirectory.Should().Be(folder);
            agent.CapturedRequest!.ProjectName.Should().Be("AcmeMock");
            agent.CapturedRequest!.Model.Should().Be("sonnet");

            // ログが出力フォルダへ書き出される
            File.Exists(result.LogPath).Should().BeTrue();
            result.LogPath.Should().EndWith(MockProjectAgentRunner.LogFileName);
            var log = File.ReadAllText(result.LogPath);
            log.Should().Contain("作業を開始します");

            // 進捗が転送されている
            string.Concat(progress).Should().Contain("作業を開始します");
        }
        finally
        {
            Cleanup(folder);
        }
    }

    /// <summary>Blazor プロファイルでは成果物検証が csproj＋.razor で成立する（xaml 不要）ことを検証する</summary>
    [Fact(DisplayName = "Blazor は csproj＋.razor で成果物検証が成立する")]
    public async Task RunAsync_BlazorArtifacts_CsprojAndRazor()
    {
        var folder = NewTempFolder();
        // Blazor の成果物は csproj と .razor（xaml は無い）
        var agent = new FakeMockProjectAgent
        {
            OnRun = dir =>
            {
                var project = ProjectDirectory(dir);
                Directory.CreateDirectory(project);
                File.WriteAllText(Path.Combine(project, $"{ProjectName}.csproj"), "<Project/>");
                File.WriteAllText(Path.Combine(project, "Home.razor"), "@page \"/\"");
            },
        };
        var build = new FakeBuildRunner { BuildSuccess = true };
        var runner = new MockProjectAgentRunner(
            agent,
            build,
            timeout: null,
            profile: MockProjectTargetProfile.Blazor
        );

        try
        {
            var result = await runner.RunAsync(
                folder,
                "AcmeMock",
                additionalInstructions: null,
                "sonnet",
                _ => { },
                TestContext.Current.CancellationToken
            );

            result.Success.Should().BeTrue();
            result.ArtifactsPresent.Should().BeTrue();
            result.BuildSucceeded.Should().BeTrue();
            build.BuildCallCount.Should().Be(1);
        }
        finally
        {
            Cleanup(folder);
        }
    }

    /// <summary>エージェント成功でも成果物が無ければビルドを試みず失敗になることを検証する</summary>
    [Fact(DisplayName = "成果物が無ければビルドせず失敗")]
    public async Task RunAsync_NoArtifacts_Fails()
    {
        var folder = NewTempFolder();
        // OnRun を設定しない = 成果物を作らない
        var agent = new FakeMockProjectAgent();
        var build = new FakeBuildRunner();
        var runner = new MockProjectAgentRunner(agent, build);

        try
        {
            var result = await runner.RunAsync(
                folder,
                "AcmeMock",
                additionalInstructions: null,
                string.Empty,
                _ => { },
                TestContext.Current.CancellationToken
            );

            result.Success.Should().BeFalse();
            result.ArtifactsPresent.Should().BeFalse();
            build.BuildCallCount.Should().Be(0);
        }
        finally
        {
            Cleanup(folder);
        }
    }

    /// <summary>成果物はあるがビルドが失敗した場合、全体失敗になることを検証する</summary>
    [Fact(DisplayName = "ビルド失敗なら全体失敗")]
    public async Task RunAsync_BuildFails_Fails()
    {
        var folder = NewTempFolder();
        var agent = new FakeMockProjectAgent { OnRun = WriteArtifacts() };
        var build = new FakeBuildRunner { BuildSuccess = false };
        var runner = new MockProjectAgentRunner(agent, build);

        try
        {
            var result = await runner.RunAsync(
                folder,
                "AcmeMock",
                additionalInstructions: null,
                string.Empty,
                _ => { },
                TestContext.Current.CancellationToken
            );

            result.ClientSucceeded.Should().BeTrue();
            result.ArtifactsPresent.Should().BeTrue();
            result.BuildSucceeded.Should().BeFalse();
            result.Success.Should().BeFalse();
        }
        finally
        {
            Cleanup(folder);
        }
    }

    /// <summary>エージェントが失敗を自己申告したら、成果物があってもビルドを試みず全体失敗になることを検証する</summary>
    [Fact(DisplayName = "エージェント失敗申告なら成果物ありでも失敗")]
    public async Task RunAsync_AgentReportsFailure_Fails()
    {
        var folder = NewTempFolder();
        var agent = new FakeMockProjectAgent
        {
            OnRun = WriteArtifacts(),
            Outcome = new MockProjectAgentOutcome(false, "エージェントが失敗しました", false),
        };
        var build = new FakeBuildRunner();
        var runner = new MockProjectAgentRunner(agent, build);

        try
        {
            var result = await runner.RunAsync(
                folder,
                "AcmeMock",
                additionalInstructions: null,
                string.Empty,
                _ => { },
                TestContext.Current.CancellationToken
            );

            result.ClientSucceeded.Should().BeFalse();
            result.Success.Should().BeFalse();
            // 成果物があればビルド検証自体は走るが（既存挙動）、自己申告失敗のため全体は失敗確定
            build.BuildCallCount.Should().Be(1);
            result.Message.Should().Contain("エージェントが失敗しました");
            // 失敗文言はバックエンド中立（Codex / API キー実行でも「Claude Code」と出さない）
            result.Message.Should().NotContain("Claude Code");
        }
        finally
        {
            Cleanup(folder);
        }
    }

    /// <summary>全体タイムアウトで打ち切られたとき、タイムアウト扱い・ビルド未実行・ログ保全になることを検証する</summary>
    [Fact(DisplayName = "タイムアウトで打ち切り・ビルド未実行")]
    public async Task RunAsync_Timeout()
    {
        var folder = NewTempFolder();
        // 例外を投げてキャンセル状態をシミュレート。外部トークンは未キャンセルなのでタイムアウト扱いになる。
        var agent = new FakeMockProjectAgent { ThrowOnRun = new OperationCanceledException() };
        var build = new FakeBuildRunner();
        var runner = new MockProjectAgentRunner(agent, build, timeout: TimeSpan.FromMinutes(30));

        try
        {
            var result = await runner.RunAsync(
                folder,
                "AcmeMock",
                additionalInstructions: null,
                string.Empty,
                _ => { },
                TestContext.Current.CancellationToken
            );

            result.TimedOut.Should().BeTrue();
            result.Success.Should().BeFalse();
            build.BuildCallCount.Should().Be(0);
            File.Exists(result.LogPath).Should().BeTrue();
        }
        finally
        {
            Cleanup(folder);
        }
    }

    /// <summary>外部キャンセルで中断されたとき、中断扱い・ビルド未実行になることを検証する</summary>
    [Fact(DisplayName = "外部キャンセルで中断・ビルド未実行")]
    public async Task RunAsync_Canceled()
    {
        var folder = NewTempFolder();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var agent = new FakeMockProjectAgent { ThrowOnRun = new OperationCanceledException() };
        var build = new FakeBuildRunner();
        var runner = new MockProjectAgentRunner(agent, build);

        try
        {
            var result = await runner.RunAsync(
                folder,
                "AcmeMock",
                additionalInstructions: null,
                string.Empty,
                _ => { },
                cts.Token
            );

            result.Canceled.Should().BeTrue();
            result.TimedOut.Should().BeFalse();
            result.Success.Should().BeFalse();
            build.BuildCallCount.Should().Be(0);
        }
        finally
        {
            Cleanup(folder);
        }
    }

    /// <summary>
    /// 成果物の探索がプロジェクトフォルダ配下に限られ、出力フォルダ直下の無関係なプロジェクトでは
    /// 合格しないことを検証する（A3: 別プロジェクトと同居しているだけで「生成できた」と誤判定しない）。
    /// </summary>
    [Fact(DisplayName = "出力フォルダ直下の無関係な csproj・UI ファイルでは成果物検証が通らない")]
    public async Task RunAsync_ArtifactsOutsideProjectFolder_NotCounted()
    {
        var folder = NewTempFolder();
        var agent = new FakeMockProjectAgent
        {
            // プロジェクトフォルダの「外」（出力フォルダ直下）にだけ成果物らしきファイルを置く
            OnRun = dir =>
            {
                File.WriteAllText(Path.Combine(dir, "Unrelated.csproj"), "<Project/>");
                File.WriteAllText(Path.Combine(dir, "Unrelated.xaml"), "<Window/>");
            },
        };
        var build = new FakeBuildRunner();
        var runner = new MockProjectAgentRunner(agent, build);

        try
        {
            var result = await runner.RunAsync(
                folder,
                ProjectName,
                additionalInstructions: null,
                string.Empty,
                _ => { },
                TestContext.Current.CancellationToken
            );

            result.ArtifactsPresent.Should().BeFalse();
            result.Success.Should().BeFalse();
            build.BuildCallCount.Should().Be(0);
        }
        finally
        {
            Cleanup(folder);
        }
    }

    /// <summary>
    /// 最終ビルドの対象が「自分のソリューションのフルパス」で渡されることを検証する
    /// （M1: フォルダ指定のままだと、出力フォルダに別のソリューションがあると MSB1011 で落ちる）。
    /// </summary>
    [Fact(DisplayName = "最終ビルドは自分のソリューションのパスを明示して渡す")]
    public async Task RunAsync_PassesOwnSolutionPathToBuild()
    {
        var folder = NewTempFolder();
        var agent = new FakeMockProjectAgent { OnRun = WriteArtifacts() };
        var build = new FakeBuildRunner();
        var runner = new MockProjectAgentRunner(agent, build);

        try
        {
            // 出力フォルダに別のソリューションが同居していても、対象は自分の .sln で一意に決まる
            File.WriteAllText(Path.Combine(folder, "Other.sln"), string.Empty);

            await runner.RunAsync(
                folder,
                ProjectName,
                additionalInstructions: null,
                string.Empty,
                _ => { },
                TestContext.Current.CancellationToken
            );

            build.CapturedSolutionPath.Should().Be(Path.Combine(folder, $"{ProjectName}.sln"));
        }
        finally
        {
            Cleanup(folder);
        }
    }

    /// <summary>
    /// 最終ビルドの直前にビルド中間物（obj / bin）が削除されることを検証する
    /// （M2: obj/*.targets はワイルドカード import で読み込まれるため、残すと AI のコードがビルドで走る）。
    /// </summary>
    [Fact(DisplayName = "最終ビルドの直前に obj / bin を削除する")]
    public async Task RunAsync_DeletesBuildOutputsBeforeFinalBuild()
    {
        var folder = NewTempFolder();
        var project = ProjectDirectory(folder);
        var agent = new FakeMockProjectAgent
        {
            OnRun = dir =>
            {
                WriteArtifacts()(dir);
                // AI が書いた（ことにする）ビルド中間物
                Directory.CreateDirectory(Path.Combine(project, "obj"));
                File.WriteAllText(
                    Path.Combine(project, "obj", $"{ProjectName}.csproj.injected.targets"),
                    "<Project/>"
                );
                Directory.CreateDirectory(Path.Combine(project, "bin"));
                File.WriteAllText(Path.Combine(project, "bin", "stale.dll"), "x");
            },
        };

        var objExistsAtBuild = true;
        var binExistsAtBuild = true;
        var build = new FakeBuildRunner
        {
            OnBuild = () =>
            {
                objExistsAtBuild = Directory.Exists(Path.Combine(project, "obj"));
                binExistsAtBuild = Directory.Exists(Path.Combine(project, "bin"));
            },
        };
        var runner = new MockProjectAgentRunner(agent, build);

        try
        {
            var result = await runner.RunAsync(
                folder,
                ProjectName,
                additionalInstructions: null,
                string.Empty,
                _ => { },
                TestContext.Current.CancellationToken
            );

            result.Success.Should().BeTrue();
            build.BuildCallCount.Should().Be(1);
            objExistsAtBuild.Should().BeFalse();
            binExistsAtBuild.Should().BeFalse();
        }
        finally
        {
            Cleanup(folder);
        }
    }

    /// <summary>
    /// obj がジャンクション（reparse point）でも、リンクだけが外れてリンク先の中身は残ることを検証する
    /// （N2: 出力フォルダの外にあるフォルダを巻き添えで消さない）。
    /// </summary>
    [Fact(DisplayName = "obj がジャンクションでもリンク先の中身は消さない")]
    public async Task RunAsync_ObjIsJunction_KeepsLinkTargetContents()
    {
        var folder = NewTempFolder();
        var project = ProjectDirectory(folder);
        var linkTarget = NewTempFolder();
        var keepFile = Path.Combine(linkTarget, "precious.txt");
        File.WriteAllText(keepFile, "keep me");

        var agent = new FakeMockProjectAgent
        {
            OnRun = dir =>
            {
                WriteArtifacts()(dir);
                CreateJunction(Path.Combine(project, "obj"), linkTarget);
            },
        };
        var build = new FakeBuildRunner();
        var runner = new MockProjectAgentRunner(agent, build);

        try
        {
            await runner.RunAsync(
                folder,
                ProjectName,
                additionalInstructions: null,
                string.Empty,
                _ => { },
                TestContext.Current.CancellationToken
            );

            // リンクの実体は外れる（ビルド中間物として扱われる）が、リンク先は無傷
            Directory.Exists(Path.Combine(project, "obj")).Should().BeFalse();
            File.Exists(keepFile).Should().BeTrue();
            File.ReadAllText(keepFile).Should().Be("keep me");
        }
        finally
        {
            Cleanup(folder);
            Cleanup(linkTarget);
        }
    }

    /// <summary>
    /// 最終ビルドが専用タイムアウトで打ち切られたとき、タイムアウト扱いで報告されることを検証する
    /// （外部トークンには時間制限が無いため、これが無いと応答しない dotnet でランが永久に終わらない）。
    /// </summary>
    [Fact(DisplayName = "最終ビルドがタイムアウトしたらタイムアウトとして報告する")]
    public async Task RunAsync_FinalBuildTimeout_ReportsTimedOut()
    {
        var folder = NewTempFolder();
        var agent = new FakeMockProjectAgent { OnRun = WriteArtifacts() };
        var build = new FakeBuildRunner { SlowBuild = true };
        var runner = new MockProjectAgentRunner(
            agent,
            build,
            timeout: null,
            profile: null,
            buildTimeout: TimeSpan.FromMilliseconds(50)
        );

        try
        {
            var result = await runner.RunAsync(
                folder,
                ProjectName,
                additionalInstructions: null,
                string.Empty,
                _ => { },
                TestContext.Current.CancellationToken
            );

            result.TimedOut.Should().BeTrue();
            result.Canceled.Should().BeFalse();
            result.BuildSucceeded.Should().BeFalse();
            result.Success.Should().BeFalse();
            // 全体タイムアウトではなく「最終ビルドのタイムアウト」として文言が分かれている
            result.Message.Should().Be(MockStrings.Mock_Result_BuildTimedOut);
        }
        finally
        {
            Cleanup(folder);
        }
    }

    /// <summary>
    /// ジャンクション（ディレクトリの reparse point）を作る。
    /// </summary>
    /// <remarks>
    /// ジャンクションの作成に公開 API は無く、シンボリックリンクは特権（開発者モード）を要するため、
    /// 特権不要な <c>mklink /J</c> を使う。テストは Windows 専用プロジェクトにある。
    /// </remarks>
    private static void CreateJunction(string linkPath, string targetPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(linkPath)!);

        using var process = System.Diagnostics.Process.Start(
            new System.Diagnostics.ProcessStartInfo
            {
                FileName = "cmd.exe",
                ArgumentList = { "/c", "mklink", "/J", linkPath, targetPath },
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            }
        );
        process!.WaitForExit();
        process.ExitCode.Should().Be(0, "テスト前提のジャンクションを作成できる必要がある");
        new DirectoryInfo(linkPath)
            .Attributes.HasFlag(FileAttributes.ReparsePoint)
            .Should()
            .BeTrue();
    }

    /// <summary>中断要求がエージェントの InterruptAsync へ転送されることを検証する</summary>
    [Fact(DisplayName = "中断はエージェントの InterruptAsync を呼ぶ")]
    public async Task InterruptAsync_ForwardsToAgent()
    {
        var agent = new FakeMockProjectAgent();
        var runner = new MockProjectAgentRunner(agent, new FakeBuildRunner());

        await runner.InterruptAsync();

        agent.Interrupted.Should().BeTrue();
    }

    /// <summary>可用性判定がエージェント／ビルド検証器へ委譲されることを検証する</summary>
    [Fact(DisplayName = "可用性判定はエージェント・ビルド検証器へ委譲する")]
    public async Task Availability_DelegatesToDependencies()
    {
        var agent = new FakeMockProjectAgent { Available = false };
        var build = new FakeBuildRunner { DotnetAvailable = false };
        var runner = new MockProjectAgentRunner(agent, build);

        runner.IsClaudeAvailable().Should().BeFalse();
        (await runner.IsDotnetAvailableAsync(TestContext.Current.CancellationToken))
            .Should()
            .BeFalse();

        agent.Available = true;
        build.DotnetAvailable = true;

        runner.IsClaudeAvailable().Should().BeTrue();
        (await runner.IsDotnetAvailableAsync(TestContext.Current.CancellationToken))
            .Should()
            .BeTrue();
    }
}
