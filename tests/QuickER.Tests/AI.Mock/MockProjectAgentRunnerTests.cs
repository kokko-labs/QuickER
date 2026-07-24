using System.IO;
using FluentAssertions;
using QuickER.AI.Mock;

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

        public bool IsAvailable() => Available;

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

        public Task<BuildRunResult> BuildAsync(
            string workingDirectory,
            CancellationToken cancellationToken = default
        )
        {
            BuildCallCount++;
            return Task.FromResult(new BuildRunResult(BuildSuccess, "build output"));
        }

        public Task<bool> IsDotnetAvailableAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(DotnetAvailable);
    }

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

    /// <summary>成果物を書き出す副作用（csproj + xaml）を返す</summary>
    private static Action<string> WriteArtifacts() =>
        dir =>
        {
            File.WriteAllText(Path.Combine(dir, "App.csproj"), "<Project/>");
            File.WriteAllText(Path.Combine(dir, "MainWindow.xaml"), "<Window/>");
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
                File.WriteAllText(Path.Combine(dir, "App.csproj"), "<Project/>");
                File.WriteAllText(Path.Combine(dir, "Home.razor"), "@page \"/\"");
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
