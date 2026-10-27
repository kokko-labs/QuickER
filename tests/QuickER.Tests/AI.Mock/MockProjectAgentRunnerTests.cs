using System.IO;
using AwesomeAssertions;
using QuickER.AI;
using QuickER.AI.Mock;
using QuickER.Tests.TestSupport;
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

        /// <summary>
        /// RunAsync の中で、トークンが取り消されるまで待つか（中断のシミュレーション。5 秒で諦める＝
        /// 中断が届かない変異でもテストがハングせず失敗する）。
        /// </summary>
        public bool WaitForCancellation { get; set; }

        /// <summary>RunAsync の始めに流す進捗の文章</summary>
        public string ProgressText { get; set; } = "作業を開始します。\n";

        /// <summary>RunAsync が呼ばれたスレッド</summary>
        public int? RunThreadId { get; private set; }

        public MockProjectAgentRequest? CapturedRequest { get; private set; }
        public bool Interrupted { get; private set; }
        public bool Available { get; set; } = true;

        /// <summary>
        /// 最終ビルドが境界越えになると宣言するか（既定 false＝Claude Code / API キー相当）。
        /// </summary>
        public bool EscapesSandbox { get; set; }

        public bool IsAvailable() => Available;

        /// <summary>実行ログに出すバックエンド名</summary>
        public string BackendName { get; set; } = "FakeBackend";

        public bool FinalBuildEscapesSandbox => EscapesSandbox;

        public async Task<MockProjectAgentOutcome> RunAsync(
            MockProjectAgentRequest request,
            Action<string> onProgress,
            CancellationToken cancellationToken
        )
        {
            RunThreadId = Environment.CurrentManagedThreadId;
            CapturedRequest = request;
            onProgress(ProgressText);
            OnRun?.Invoke(request.WorkingDirectory);

            if (ThrowOnRun is not null)
            {
                throw ThrowOnRun;
            }

            if (WaitForCancellation)
            {
                await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
            }

            return Outcome;
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

        /// <summary>BuildAsync で投げる例外（dotnet を起動できない等のシミュレーション）</summary>
        public Exception? ThrowOnBuild { get; set; }

        /// <summary>ビルドが始まったことの合図（中断をビルド中に届けるテスト用）</summary>
        public TaskCompletionSource BuildStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<BuildRunResult> BuildAsync(
            string solutionFilePath,
            CancellationToken cancellationToken = default
        )
        {
            BuildCallCount++;
            CapturedSolutionPath = solutionFilePath;
            OnBuild?.Invoke();
            BuildStarted.TrySetResult();

            if (ThrowOnBuild is not null)
            {
                throw ThrowOnBuild;
            }

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
                ReparsePointTestHelper.CreateJunction(Path.Combine(project, "obj"), linkTarget);
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
    /// 実行器が OCE 以外の例外を投げても、ランは失敗の結果として終わり、原因を含むログが残ることを検証する
    /// （例外のまま抜けると結果もログも残らず、利用者には原因が分からない）。
    /// </summary>
    [Fact(DisplayName = "実行器の想定外の例外は失敗の結果に畳み、ログを残す")]
    public async Task RunAsync_AgentThrows_ReturnsFailureAndWritesLog()
    {
        var folder = NewTempFolder();
        var agent = new FakeMockProjectAgent
        {
            ThrowOnRun = new InvalidOperationException("agent exploded"),
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

            result.Success.Should().BeFalse();
            result.ClientSucceeded.Should().BeFalse();
            result.Canceled.Should().BeFalse();
            result.TimedOut.Should().BeFalse();
            result.Message.Should().Contain("agent exploded");
            File.ReadAllText(result.LogPath).Should().Contain("agent exploded");
        }
        finally
        {
            Cleanup(folder);
        }
    }

    /// <summary>最終ビルドを実行できない例外は、ビルド失敗の結果として終わり、原因がログに残ることを検証する</summary>
    [Fact(DisplayName = "最終ビルドの想定外の例外はビルド失敗に畳み、ログを残す")]
    public async Task RunAsync_BuildRunnerThrows_ReturnsBuildFailureAndWritesLog()
    {
        var folder = NewTempFolder();
        var agent = new FakeMockProjectAgent { OnRun = WriteArtifacts() };
        var build = new FakeBuildRunner
        {
            ThrowOnBuild = new InvalidOperationException("dotnet missing"),
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

            result.Success.Should().BeFalse();
            result.BuildSucceeded.Should().BeFalse();
            result.Message.Should().Be(MockStrings.Mock_Result_BuildFailed);
            File.ReadAllText(result.LogPath).Should().Contain("dotnet missing");
        }
        finally
        {
            Cleanup(folder);
        }
    }

    /// <summary>
    /// 結果へ畳めない場所（確認の呼び出し）で例外が出ても、そこまでのログは書き出されることを検証する。
    /// </summary>
    [Fact(DisplayName = "想定外の例外で抜けてもログは書き出す")]
    public async Task RunAsync_UnexpectedException_StillWritesLog()
    {
        var folder = NewTempFolder();
        var project = ProjectDirectory(folder);
        var agent = new FakeMockProjectAgent
        {
            EscapesSandbox = true,
            OnRun = dir =>
            {
                WriteArtifacts()(dir);
                File.WriteAllText(Path.Combine(project, "Directory.Build.props"), "<Project/>");
            },
        };
        var build = new FakeBuildRunner();
        var runner = new MockProjectAgentRunner(
            agent,
            build,
            confirmBuild: _ => throw new InvalidOperationException("dialog failed")
        );

        try
        {
            var run = () =>
                runner.RunAsync(
                    folder,
                    ProjectName,
                    additionalInstructions: null,
                    string.Empty,
                    _ => { },
                    TestContext.Current.CancellationToken
                );

            await run.Should().ThrowAsync<InvalidOperationException>();
            build.BuildCallCount.Should().Be(0);

            var logPath = Path.Combine(folder, MockProjectAgentRunner.LogFileName);
            File.Exists(logPath).Should().BeTrue();
            File.ReadAllText(logPath).Should().Contain("dialog failed");
        }
        finally
        {
            Cleanup(folder);
        }
    }

    /// <summary>
    /// 外部トークンを立てずに InterruptAsync だけを呼んでも、エージェント実行が止まり、
    /// タイムアウトでなく中断として分類されることを検証する。
    /// </summary>
    [Fact(DisplayName = "InterruptAsync だけでもエージェント実行を止め、中断として分類する")]
    public async Task InterruptAsync_DuringAgentRun_ClassifiesAsCanceled()
    {
        var folder = NewTempFolder();
        var agent = new FakeMockProjectAgent
        {
            OnRun = WriteArtifacts(),
            WaitForCancellation = true,
        };
        var build = new FakeBuildRunner();
        var runner = new MockProjectAgentRunner(agent, build);

        try
        {
            var run = runner.RunAsync(
                folder,
                ProjectName,
                additionalInstructions: null,
                string.Empty,
                _ => { },
                TestContext.Current.CancellationToken
            );

            // エージェントが走り始めてから中断する
            while (agent.CapturedRequest is null)
            {
                await Task.Delay(10, TestContext.Current.CancellationToken);
            }

            await runner.InterruptAsync();
            var result = await run;

            result.Canceled.Should().BeTrue();
            result.TimedOut.Should().BeFalse();
            result.Message.Should().Be(MockStrings.Mock_Result_Canceled);
            build.BuildCallCount.Should().Be(0);
        }
        finally
        {
            Cleanup(folder);
        }
    }

    /// <summary>InterruptAsync が最終ビルドの最中にも効き、中断として分類されることを検証する</summary>
    [Fact(DisplayName = "InterruptAsync は最終ビルド中にも効く")]
    public async Task InterruptAsync_DuringFinalBuild_ClassifiesAsCanceled()
    {
        var folder = NewTempFolder();
        var agent = new FakeMockProjectAgent { OnRun = WriteArtifacts() };
        var build = new FakeBuildRunner { SlowBuild = true };
        var runner = new MockProjectAgentRunner(agent, build);

        try
        {
            var run = runner.RunAsync(
                folder,
                ProjectName,
                additionalInstructions: null,
                string.Empty,
                _ => { },
                TestContext.Current.CancellationToken
            );

            await build.BuildStarted.Task.WaitAsync(TestContext.Current.CancellationToken);
            await runner.InterruptAsync();
            var result = await run;

            result.Canceled.Should().BeTrue();
            result.TimedOut.Should().BeFalse();
            result.BuildSucceeded.Should().BeFalse();
        }
        finally
        {
            Cleanup(folder);
        }
    }

    /// <summary>ランが終わったあとの InterruptAsync は何もせず、例外にならないことを検証する</summary>
    [Fact(DisplayName = "ラン終了後の InterruptAsync は例外にならない")]
    public async Task InterruptAsync_AfterRun_DoesNotThrow()
    {
        var folder = NewTempFolder();
        var agent = new FakeMockProjectAgent { OnRun = WriteArtifacts() };
        var runner = new MockProjectAgentRunner(agent, new FakeBuildRunner());

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

            var interrupt = () => runner.InterruptAsync();
            await interrupt.Should().NotThrowAsync();
        }
        finally
        {
            Cleanup(folder);
        }
    }

    /// <summary>
    /// 変更検知のスナップショット（全ファイルのハッシュ）が呼び出し元のスレッド（UI スレッド）で走らないことを
    /// 検証する。スナップショットが読むファイルへ oplock を掛けて、スナップショットをそのファイルを開く途中で止め、
    /// その間に単一スレッドの同期コンテキスト（呼び出し元の代役）へ戻りが返り、そのスレッドが別の処理を受け付けることを見る
    /// （スナップショットが呼び出し元で同期に走れば、止まっている間はランが戻らず、そのスレッドも空かない）。
    /// </summary>
    [Fact(DisplayName = "スナップショットは呼び出し元のスレッドで走らない")]
    public async Task RunAsync_CapturesSnapshotOffCallerThread()
    {
        var folder = NewTempFolder();
        var agent = new FakeMockProjectAgent { EscapesSandbox = true, OnRun = WriteArtifacts() };
        var runner = new MockProjectAgentRunner(
            agent,
            new FakeBuildRunner(),
            confirmBuild: _ => true
        );

        // 拡張子が比較の対象（UI 層のソース・静的資産でない）なので、スナップショットはこのファイルを開いてハッシュする
        var held = Path.Combine(folder, "held.json");
        File.WriteAllText(held, "{}");
        var oplock = FileOplock.Acquire(held);
        var callerFree = new TaskCompletionSource<int>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        Task? run = null;

        try
        {
            var running = SingleThreadContext.RunAsync(() =>
            {
                run = runner.RunAsync(
                    folder,
                    ProjectName,
                    additionalInstructions: null,
                    string.Empty,
                    _ => { },
                    TestContext.Current.CancellationToken
                );

                // ランが戻ったあと、呼び出し元のスレッドが次の処理を受け付けるか
                SynchronizationContext.Current!.Post(
                    _ => callerFree.TrySetResult(Environment.CurrentManagedThreadId),
                    null
                );

                return run;
            });

            oplock
                .WaitForBreak(TimeSpan.FromSeconds(30))
                .Should()
                .BeTrue("スナップショットは held.json を開こうとして止まる");
            var freeThreadId = await callerFree
                .Task.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken)
                .ContinueWith(
                    task => task.IsCompletedSuccessfully ? task.Result : (int?)null,
                    TaskScheduler.Default
                );

            freeThreadId
                .Should()
                .NotBeNull("スナップショットが止まっている間に、呼び出し元のスレッドは空いている");
            run!.IsCompleted.Should().BeFalse("スナップショットは止まったまま");

            oplock.Dispose();
            var callerThreadId = await running;

            freeThreadId.Should().Be(callerThreadId);
            agent.RunThreadId.Should().NotBeNull();
            agent.RunThreadId.Should().NotBe(callerThreadId);
        }
        finally
        {
            oplock.Dispose();
            Cleanup(folder);
        }
    }

    /// <summary>
    /// 専用スレッド 1 本で継続を回す同期コンテキスト（UI スレッドの代役）。渡した処理をそのスレッドで始め、
    /// 完了まで継続を同じスレッドへ戻す。戻り値はそのスレッドの ID。
    /// </summary>
    private sealed class SingleThreadContext : SynchronizationContext
    {
        private readonly System.Collections.Concurrent.BlockingCollection<(
            SendOrPostCallback Callback,
            object? State
        )> _queue = new();

        public override void Post(SendOrPostCallback d, object? state) => _queue.Add((d, state));

        public static Task<int> RunAsync(Func<Task> start)
        {
            var completion = new TaskCompletionSource<int>(
                TaskCreationOptions.RunContinuationsAsynchronously
            );

            var thread = new Thread(() =>
            {
                var context = new SingleThreadContext();
                SetSynchronizationContext(context);

                try
                {
                    var task = start();
                    task.ContinueWith(_ => context._queue.CompleteAdding(), TaskScheduler.Default);

                    foreach (var (callback, state) in context._queue.GetConsumingEnumerable())
                    {
                        callback(state);
                    }

                    task.GetAwaiter().GetResult();
                    completion.SetResult(Environment.CurrentManagedThreadId);
                }
                catch (Exception ex)
                {
                    completion.SetException(ex);
                }
            })
            {
                IsBackground = true,
            };
            thread.Start();
            return completion.Task;
        }
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

    /// <summary>
    /// 実行の完了・失敗の行は実行器のバックエンド名を、成果物チェックの行はターゲットの UI ファイルの拡張子を出し、
    /// 「Claude Code」「xaml」を固定で出さないことを、英語と日本語の文言で検証する。
    /// </summary>
    [Theory(DisplayName = "実行ログはバックエンド名と UI ファイルの拡張子を出す")]
    [InlineData("en", "Codex", "blazor", true, true)]
    [InlineData("en", "API Key", "blazor", false, true)]
    [InlineData("en", "Copilot", "wpf", true, false)]
    [InlineData("en", "Claude Code", "wpf", false, false)]
    [InlineData("ja", "Codex", "blazor", false, false)]
    [InlineData("ja", "API キー", "wpf", true, true)]
    public async Task RunAsync_LogNamesBackendAndUiExtension(
        string uiCulture,
        string backendName,
        string target,
        bool agentSucceeds,
        bool writeArtifacts
    )
    {
        var folder = NewTempFolder();
        var profile =
            target == "blazor" ? MockProjectTargetProfile.Blazor : MockProjectTargetProfile.Wpf;
        var uiFile = target == "blazor" ? "Home.razor" : "MainWindow.xaml";
        var agent = new FakeMockProjectAgent
        {
            BackendName = backendName,
            Outcome = new MockProjectAgentOutcome(
                agentSucceeds,
                agentSucceeds ? null : "boom",
                false
            ),
            OnRun = writeArtifacts
                ? dir =>
                {
                    var project = ProjectDirectory(dir);
                    Directory.CreateDirectory(project);
                    File.WriteAllText(Path.Combine(project, $"{ProjectName}.csproj"), "<Project/>");
                    File.WriteAllText(Path.Combine(project, uiFile), "<x/>");
                }
                : null,
        };
        var runner = new MockProjectAgentRunner(
            agent,
            new FakeBuildRunner(),
            timeout: null,
            profile: profile
        );
        var progress = new List<string>();
        var previous = System.Globalization.CultureInfo.CurrentUICulture;
        System.Globalization.CultureInfo.CurrentUICulture = new System.Globalization.CultureInfo(
            uiCulture
        );

        try
        {
            await runner.RunAsync(
                folder,
                ProjectName,
                additionalInstructions: null,
                string.Empty,
                progress.Add,
                TestContext.Current.CancellationToken
            );

            var log = string.Concat(progress);
            var extension = target == "blazor" ? ".razor" : ".xaml";
            var (clientLine, artifactLine) = (uiCulture, agentSucceeds, writeArtifacts) switch
            {
                ("en", true, true) => (
                    $"== {backendName} execution completed ==",
                    $"Artifact check: found the .csproj file and the {extension} files."
                ),
                ("en", false, true) => (
                    $"== {backendName} execution failed: boom ==",
                    $"Artifact check: found the .csproj file and the {extension} files."
                ),
                ("en", true, false) => (
                    $"== {backendName} execution completed ==",
                    $"Artifact check: the .csproj file or the {extension} files were not found."
                ),
                ("en", false, false) => (
                    $"== {backendName} execution failed: boom ==",
                    $"Artifact check: the .csproj file or the {extension} files were not found."
                ),
                ("ja", true, true) => (
                    $"== {backendName} の実行が完了しました ==",
                    $"成果物チェック: .csproj と {extension} のファイルを確認しました。"
                ),
                _ => (
                    $"== {backendName} の実行が失敗しました: boom ==",
                    $"成果物チェック: .csproj または {extension} のファイルが見つかりません。"
                ),
            };

            log.Should().Contain(clientLine).And.Contain(artifactLine);

            if (backendName != "Claude Code")
            {
                log.Should().NotContain("Claude Code");
            }

            if (target == "blazor")
            {
                log.Should().NotContain("xaml");
            }
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentUICulture = previous;
            Cleanup(folder);
        }
    }

    /// <summary>実行器 4 つのバックエンド名が生成画面のタブの表記と同じことを検証する</summary>
    [Fact(DisplayName = "実行器のバックエンド名はタブの表記と同じ")]
    public void Agents_BackendNames_MatchTabHeaders()
    {
        new ClaudeCodeMockProjectAgent(new ClaudeCodeProcessClient())
            .BackendName.Should()
            .Be("Claude Code");
        new CodexMockProjectAgent(new CodexAppServerClient()).BackendName.Should().Be("Codex");
        new CopilotMockProjectAgent(new CopilotRuntimeClient()).BackendName.Should().Be("Copilot");
        new ApiKeyMockProjectAgent(
            (_, _) => throw new InvalidOperationException(),
            new FakeBuildRunner()
        )
            .BackendName.Should()
            .Be(MockStrings.Mock_TabApiKey);
    }

    /// <summary>
    /// プロジェクトフォルダに、画面を <paramref name="screenCount"/> 個持つ設計（design/mock/mock.json）を置き、
    /// csproj と <paramref name="uiFiles"/> の UI ファイルを書く副作用を返す
    /// </summary>
    private static Action<string> WriteProjectWithScreens(
        int screenCount,
        params (string Path, string Content)[] uiFiles
    ) =>
        dir =>
        {
            var project = ProjectDirectory(dir);
            var design = Path.Combine(project, "design", "mock");
            Directory.CreateDirectory(design);
            var store = MockFolderStore.CreateNew(design, ProjectName, "# schema");

            for (var i = 0; i < screenCount; i++)
            {
                store.SaveScreen(
                    $"Screen{i}.html",
                    $"Screen {i}",
                    "role",
                    "<!DOCTYPE html><html><body></body></html>",
                    Array.Empty<MockTransition>(),
                    "init"
                );
            }

            File.WriteAllText(Path.Combine(project, $"{ProjectName}.csproj"), "<Project/>");

            foreach (var (path, content) in uiFiles)
            {
                var full = Path.Combine(project, path);
                Directory.CreateDirectory(Path.GetDirectoryName(full)!);
                File.WriteAllText(full, content);
            }
        };

    /// <summary>決まった進捗の副作用でランナーを 1 回走らせる</summary>
    private static async Task<(MockProjectAgentResult Result, string Log)> RunWithAsync(
        MockProjectTargetProfile profile,
        Action<string> onRun
    )
    {
        var folder = NewTempFolder();
        var agent = new FakeMockProjectAgent { OnRun = onRun };
        var runner = new MockProjectAgentRunner(
            agent,
            new FakeBuildRunner(),
            timeout: null,
            profile: profile
        );
        var progress = new List<string>();

        try
        {
            var result = await runner.RunAsync(
                folder,
                ProjectName,
                additionalInstructions: null,
                string.Empty,
                progress.Add,
                TestContext.Current.CancellationToken
            );

            return (result, string.Concat(progress));
        }
        finally
        {
            Cleanup(folder);
        }
    }

    /// <summary>
    /// Blazor では @page の行の数が設計の画面の数に届かなければ、ビルドが通っても全体を失敗にし、数を名指しすることを検証する
    /// （App.razor・Routes.razor・レイアウトは @page を持たないので数えない）
    /// </summary>
    [Fact(DisplayName = "Blazor は @page の数が画面の数に届かなければ失敗にする")]
    public async Task RunAsync_BlazorScreensMissing_Fails()
    {
        var (result, log) = await RunWithAsync(
            MockProjectTargetProfile.Blazor,
            WriteProjectWithScreens(
                3,
                ("Components/App.razor", "<html></html>"),
                ("Components/Routes.razor", "<Router />"),
                ("Components/Layout/MainLayout.razor", "@inherits LayoutComponentBase"),
                ("Components/Pages/Index.razor", "@page \"/\"\n<h1>Home</h1>")
            )
        );

        result.Success.Should().BeFalse();
        result.ScreensMissing.Should().BeTrue();
        result.ScreensExpected.Should().Be(3);
        result.ScreenArtifactsFound.Should().Be(1);
        result.BuildSucceeded.Should().BeTrue();
        result
            .Message.Should()
            .Be(string.Format(MockStrings.Mock_Result_ScreensMissingFormat, 3, 1));
        log.Should().Contain(string.Format(MockStrings.Mock_Run_ScreensMissingFormat, 3, 1));
    }

    /// <summary>
    /// Blazor では 1 つのファイルに複数の @page があればその数だけ数え、画面の数に届けば合格することを検証する
    /// </summary>
    [Fact(DisplayName = "Blazor は 1 ファイルの複数の @page も数え、届けば合格")]
    public async Task RunAsync_BlazorMultiplePageDirectives_Pass()
    {
        var (result, log) = await RunWithAsync(
            MockProjectTargetProfile.Blazor,
            WriteProjectWithScreens(
                3,
                ("Components/App.razor", "<html></html>"),
                (
                    "Components/Pages/Products.razor",
                    "@page \"/products\"\n@page \"/products/{Id:int}\"\n<h1/>"
                ),
                ("Components/Pages/Index.razor", "  @page \"/\"\n<h1/>")
            )
        );

        result.Success.Should().BeTrue();
        result.ScreensMissing.Should().BeFalse();
        result.ScreenArtifactsFound.Should().Be(3);
        log.Should().NotContain(string.Format(MockStrings.Mock_Run_ScreensMissingFormat, 3, 3));
    }

    /// <summary>
    /// WPF では App.xaml を除いた .xaml が画面の数に届かなくても失敗にせず、警告の行だけを出すことを検証する
    /// （1 つの Window の中で画面を切り替える作り方を誤って失敗にしない）
    /// </summary>
    [Fact(DisplayName = "WPF は画面のファイルが足りなくても警告だけで失敗にしない")]
    public async Task RunAsync_WpfScreensMissing_WarnsOnly()
    {
        var (result, log) = await RunWithAsync(
            MockProjectTargetProfile.Wpf,
            WriteProjectWithScreens(
                3,
                ("App.xaml", "<Application/>"),
                ("MainWindow.xaml", "<Window/>")
            )
        );

        result.Success.Should().BeTrue();
        result.ScreensMissing.Should().BeFalse();
        result.ScreensExpected.Should().Be(3);
        result.ScreenArtifactsFound.Should().Be(1);
        log.Should().Contain(string.Format(MockStrings.Mock_Run_ScreensMissingWarningFormat, 3, 1));
    }

    /// <summary>設計（mock.json）が無ければ、画面の数は確かめず従来どおりの判定にすることを検証する</summary>
    [Fact(DisplayName = "設計が無ければ画面の数は確かめない")]
    public async Task RunAsync_NoDesign_SkipsScreenCount()
    {
        var (result, _) = await RunWithAsync(MockProjectTargetProfile.Blazor, WriteArtifacts());

        result.ScreensExpected.Should().BeNull();
        result.ScreenArtifactsFound.Should().BeNull();
        result.ScreensMissing.Should().BeFalse();
    }

    /// <summary>
    /// 実行器の文章が改行で終わらなくても、ランナーの次の行は新しい行から始まることを検証する
    /// </summary>
    [Fact(DisplayName = "実行器の文章が改行で終わらなくても、ランナーの行は新しい行から始まる")]
    public async Task RunAsync_RunnerLinesAfterUnterminatedAgentText_StartOnNewLine()
    {
        var folder = NewTempFolder();
        var agent = new FakeMockProjectAgent
        {
            OnRun = WriteArtifacts(),
            ProgressText = "書きかけ",
        };
        var runner = new MockProjectAgentRunner(agent, new FakeBuildRunner());
        var progress = new List<string>();

        try
        {
            await runner.RunAsync(
                folder,
                ProjectName,
                additionalInstructions: null,
                string.Empty,
                progress.Add,
                TestContext.Current.CancellationToken
            );

            var log = string.Concat(progress);
            var after = log[
                (log.IndexOf("書きかけ", StringComparison.Ordinal) + "書きかけ".Length)..
            ];
            after.Should().StartWith("\n");
        }
        finally
        {
            Cleanup(folder);
        }
    }
}
