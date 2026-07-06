using System.IO;
using FluentAssertions;
using QuickER.AI;

namespace QuickER.Tests.Services.Chat;

/// <summary>
/// <see cref="MockProjectAgentRunner"/> のオーケストレーション（プロンプト・ログ保全・成果物検証・
/// 最終ビルド分岐・タイムアウト・中断）を、フェイクの Claude Code クライアント／ビルド検証器で検証するテストクラス。
/// </summary>
public class MockProjectAgentRunnerTests
{
    /// <summary>スクリプト化した挙動を返すフェイク Claude Code クライアント</summary>
    private sealed class FakeClaudeCodeClient : IClaudeCodeClient
    {
        /// <summary>RunTurnAsync 実行時に走らせる副作用（成果物生成のシミュレーション等）</summary>
        public Action<string>? OnRun { get; set; }

        /// <summary>返すターン結果</summary>
        public ClaudeCodeTurnOutcome Outcome { get; set; } = new(true, null, "s1", false);

        /// <summary>RunTurnAsync でスローする例外（タイムアウト・中断のシミュレーション）</summary>
        public Exception? ThrowOnRun { get; set; }

        public ClaudeCodeLaunchOptions? CapturedOptions { get; private set; }
        public string? CapturedPrompt { get; private set; }
        public bool Interrupted { get; private set; }
        public bool Available { get; set; } = true;

        public bool IsAvailable() => Available;

        public Task<ClaudeCodeTurnOutcome> RunTurnAsync(
            string prompt,
            string? resumeSessionId,
            ClaudeCodeLaunchOptions options,
            Action<string> onAssistantText,
            CancellationToken cancellationToken
        )
        {
            CapturedPrompt = prompt;
            CapturedOptions = options;
            onAssistantText("作業を開始します。\n");
            OnRun?.Invoke(options.WorkingDirectory);

            if (ThrowOnRun is not null)
            {
                throw ThrowOnRun;
            }

            return Task.FromResult(Outcome);
        }

        public Task<ClaudeLoginProbeResult> ProbeLoginAsync(CancellationToken cancellationToken) =>
            Task.FromResult(ClaudeLoginProbeResult.LoggedIn);

        public void Interrupt() => Interrupted = true;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
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

    /// <summary>成功パス: 成果物あり・ビルド成功で全体成功し、ログが書き出されることを検証する</summary>
    [Fact(DisplayName = "成果物あり・ビルド成功で全体成功しログを保全する")]
    public async Task RunAsync_SuccessPath()
    {
        var folder = NewTempFolder();
        var client = new FakeClaudeCodeClient { OnRun = WriteArtifacts() };
        var build = new FakeBuildRunner { BuildSuccess = true };
        var runner = new MockProjectAgentRunner(client, build);
        var progress = new List<string>();

        try
        {
            var result = await runner.RunAsync(
                folder,
                "AcmeMock",
                "sonnet",
                progress.Add,
                TestContext.Current.CancellationToken
            );

            result.Success.Should().BeTrue();
            result.ArtifactsPresent.Should().BeTrue();
            result.BuildSucceeded.Should().BeTrue();
            build.BuildCallCount.Should().Be(1);

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

    /// <summary>プロンプト・起動オプションにヘッドレス許可と規約が含まれることを検証する</summary>
    [Fact(DisplayName = "起動オプションはヘッドレス許可・規約プロンプトを含む")]
    public async Task RunAsync_PromptAndOptions()
    {
        var folder = NewTempFolder();
        var client = new FakeClaudeCodeClient { OnRun = WriteArtifacts() };
        var runner = new MockProjectAgentRunner(client, new FakeBuildRunner());

        try
        {
            await runner.RunAsync(
                folder,
                "AcmeMock",
                "sonnet",
                _ => { },
                TestContext.Current.CancellationToken
            );

            var options = client.CapturedOptions!;
            options.PermissionMode.Should().Be("acceptEdits");
            options.AdditionalAllowedTools.Should().Contain("Edit");
            options.AdditionalAllowedTools.Should().Contain("Write");
            options.AdditionalAllowedTools.Should().Contain("Bash");
            // MCP（ER ツール）は使わせない
            options.McpConfigPath.Should().BeEmpty();
            options.WorkingDirectory.Should().Be(folder);

            // プロンプトはデザイン仕様・README 規約・読み取り専用を案内する
            client.CapturedPrompt.Should().Contain("design/mock.html");
            client.CapturedPrompt.Should().Contain("README-QuickER.md");
            client.CapturedPrompt.Should().Contain("Generated/");
        }
        finally
        {
            Cleanup(folder);
        }
    }

    /// <summary>クライアント成功でも成果物が無ければビルドを試みず失敗になることを検証する</summary>
    [Fact(DisplayName = "成果物が無ければビルドせず失敗")]
    public async Task RunAsync_NoArtifacts_Fails()
    {
        var folder = NewTempFolder();
        // OnRun を設定しない = 成果物を作らない
        var client = new FakeClaudeCodeClient();
        var build = new FakeBuildRunner();
        var runner = new MockProjectAgentRunner(client, build);

        try
        {
            var result = await runner.RunAsync(
                folder,
                "AcmeMock",
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
        var client = new FakeClaudeCodeClient { OnRun = WriteArtifacts() };
        var build = new FakeBuildRunner { BuildSuccess = false };
        var runner = new MockProjectAgentRunner(client, build);

        try
        {
            var result = await runner.RunAsync(
                folder,
                "AcmeMock",
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

    /// <summary>全体タイムアウトで打ち切られたとき、タイムアウト扱い・ビルド未実行・ログ保全になることを検証する</summary>
    [Fact(DisplayName = "タイムアウトで打ち切り・ビルド未実行")]
    public async Task RunAsync_Timeout()
    {
        var folder = NewTempFolder();
        // 例外を投げてキャンセル状態をシミュレート。外部トークンは未キャンセルなのでタイムアウト扱いになる。
        var client = new FakeClaudeCodeClient { ThrowOnRun = new OperationCanceledException() };
        var build = new FakeBuildRunner();
        var runner = new MockProjectAgentRunner(client, build, timeout: TimeSpan.FromMinutes(30));

        try
        {
            var result = await runner.RunAsync(
                folder,
                "AcmeMock",
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

        var client = new FakeClaudeCodeClient { ThrowOnRun = new OperationCanceledException() };
        var build = new FakeBuildRunner();
        var runner = new MockProjectAgentRunner(client, build);

        try
        {
            var result = await runner.RunAsync(
                folder,
                "AcmeMock",
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

    /// <summary>中断要求がクライアントの Interrupt を呼ぶことを検証する</summary>
    [Fact(DisplayName = "中断はクライアントの Interrupt を呼ぶ")]
    public async Task InterruptAsync_CallsClientInterrupt()
    {
        var client = new FakeClaudeCodeClient();
        var runner = new MockProjectAgentRunner(client, new FakeBuildRunner());

        await runner.InterruptAsync();

        client.Interrupted.Should().BeTrue();
    }
}
