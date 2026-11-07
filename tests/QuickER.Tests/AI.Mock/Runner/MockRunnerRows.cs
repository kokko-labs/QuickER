using System.Globalization;
using System.IO;
using AwesomeAssertions;
using QuickER.AI.Mock;
using QuickER.Tests.AI.Mock.Agents;
using QuickER.Tests.TestSupport.Engines;
using MockStrings = QuickER.AI.Mock.Resources.Strings;

namespace QuickER.Tests.AI.Mock.Runner;

/// <summary>ランナー（<see cref="MockProjectAgentRunner"/>）の流れの表：実行器 → 成果物 → 画面の数 → 最終ビルド → 結果の分類</summary>
/// <remarks>
/// 行は元のテストの入力と期待を持ち、ランの結果を分類して行の <see cref="MockRunnerScenario.Result"/> と照合する
/// （分類の照合はテストが全行に掛ける）。実行器とビルド検証器は偽物で、実行器の違いは実行器の網が見る。
/// </remarks>
internal static class MockRunnerRows
{
    private static MockAgentTarget Wpf => MockAgentTarget.Wpf;

    private static MockAgentTarget Blazor => MockAgentTarget.Blazor;

    /// <summary>行の表</summary>
    public static ScenarioTable<MockRunnerScenario> Table =>
        new(EngineIds.Ai, [.. Flow(), .. Interrupt(), .. Delegation(), .. Screens(), .. Log()]);

    /// <summary>実行器 → 成果物 → 最終ビルドの流れ</summary>
    private static IEnumerable<ScenarioRow<MockRunnerScenario>> Flow() =>
        [
            Row(
                "run-success-keeps-log-and-forwards-progress",
                "成果物があり最終ビルドが通れば成功し、要求をそのまま実行器へ渡し、進捗を転送してログを出力フォルダへ書く",
                Wpf,
                MockRunResult.Success,
                async c =>
                {
                    c.Agent.OnRun = MockRunnerContext.WriteArtifacts();
                    var result = await c.RunAsync(c.CreateRunner(), "sonnet");

                    result.ArtifactsPresent.Should().BeTrue();
                    result.BuildSucceeded.Should().BeTrue();
                    c.Build.BuildCallCount.Should().Be(1);
                    c.Agent.CapturedRequest!.WorkingDirectory.Should().Be(c.Folder);
                    c.Agent.CapturedRequest!.ProjectName.Should().Be(MockRunnerContext.ProjectName);
                    c.Agent.CapturedRequest!.Model.Should().Be("sonnet");
                    File.Exists(result.LogPath).Should().BeTrue();
                    result.LogPath.Should().EndWith(MockProjectAgentRunner.LogFileName);
                    File.ReadAllText(result.LogPath).Should().Contain("作業を開始します");
                    c.ProgressText.Should().Contain("作業を開始します");
                    return result;
                }
            ),
            Row(
                "artifacts-blazor-csproj-and-razor-pass",
                "Blazor は csproj と .razor で成果物の検証が通る（xaml は要らない）",
                Blazor,
                MockRunResult.Success,
                async c =>
                {
                    c.Agent.OnRun = dir =>
                        MockRunnerContext.WriteProjectFiles(dir, ("Home.razor", "@page \"/\""));
                    var result = await c.RunAsync(c.CreateRunner(), "sonnet");

                    result.ArtifactsPresent.Should().BeTrue();
                    result.BuildSucceeded.Should().BeTrue();
                    c.Build.BuildCallCount.Should().Be(1);
                    return result;
                }
            ),
            .. BothTargets(
                "artifacts-missing-skips-build",
                "実行器が成功しても成果物が無ければ、最終ビルドを試みずに失敗する",
                MockRunResult.ArtifactsMissing,
                async c =>
                {
                    var result = await c.RunAsync(c.CreateRunner());

                    result.ArtifactsPresent.Should().BeFalse();
                    c.Build.BuildCallCount.Should().Be(0);
                    return result;
                }
            ),
            Row(
                "artifacts-outside-project-folder-not-counted",
                "出力フォルダ直下の無関係な csproj・UI のファイルでは成果物の検証が通らない（探索はプロジェクトフォルダ配下だけ）",
                Wpf,
                MockRunResult.ArtifactsMissing,
                async c =>
                {
                    c.Agent.OnRun = dir =>
                    {
                        File.WriteAllText(Path.Combine(dir, "Unrelated.csproj"), "<Project/>");
                        File.WriteAllText(Path.Combine(dir, "Unrelated.xaml"), "<Window/>");
                    };
                    var result = await c.RunAsync(c.CreateRunner());

                    result.ArtifactsPresent.Should().BeFalse();
                    c.Build.BuildCallCount.Should().Be(0);
                    return result;
                }
            ),
            .. BothTargets(
                "build-failure-fails-run",
                "成果物があっても最終ビルドが失敗すれば全体を失敗にする",
                MockRunResult.BuildFailed,
                async c =>
                {
                    c.Agent.OnRun = c.WriteTargetArtifacts();
                    c.Build.BuildSuccess = false;
                    var result = await c.RunAsync(c.CreateRunner());

                    result.ClientSucceeded.Should().BeTrue();
                    result.ArtifactsPresent.Should().BeTrue();
                    result.BuildSucceeded.Should().BeFalse();
                    return result;
                }
            ),
            Row(
                "build-targets-own-solution-path",
                "最終ビルドの対象は自分のソリューションのフルパス（出力フォルダに別のソリューションがあっても揺れない）",
                Wpf,
                MockRunResult.Success,
                async c =>
                {
                    c.Agent.OnRun = MockRunnerContext.WriteArtifacts();
                    File.WriteAllText(Path.Combine(c.Folder, "Other.sln"), string.Empty);
                    var result = await c.RunAsync(c.CreateRunner());

                    c.Build.CapturedSolutionPath.Should()
                        .Be(Path.Combine(c.Folder, $"{MockRunnerContext.ProjectName}.sln"));
                    return result;
                }
            ),
            .. BothTargets(
                "build-timeout-reports-timed-out",
                "最終ビルドが専用のタイムアウトで打ち切られたら、タイムアウト（最終ビルドの文言）として報告する",
                MockRunResult.BuildTimedOut,
                async c =>
                {
                    c.Agent.OnRun = c.WriteTargetArtifacts();
                    c.Build.SlowBuild = true;
                    var result = await c.RunAsync(
                        c.CreateRunner(buildTimeout: TimeSpan.FromMilliseconds(50))
                    );

                    result.TimedOut.Should().BeTrue();
                    result.Canceled.Should().BeFalse();
                    result.BuildSucceeded.Should().BeFalse();
                    result.Message.Should().Be(MockStrings.Mock_Result_BuildTimedOut);
                    return result;
                }
            ),
            Row(
                "build-exception-folds-into-build-failure-and-log",
                "最終ビルドを起動できない例外はビルドの失敗に畳み、原因をログに残す",
                Wpf,
                MockRunResult.BuildFailed,
                async c =>
                {
                    c.Agent.OnRun = MockRunnerContext.WriteArtifacts();
                    c.Build.ThrowOnBuild = new InvalidOperationException("dotnet missing");
                    var result = await c.RunAsync(c.CreateRunner());

                    result.BuildSucceeded.Should().BeFalse();
                    result.Message.Should().Be(MockStrings.Mock_Result_BuildFailed);
                    File.ReadAllText(result.LogPath).Should().Contain("dotnet missing");
                    return result;
                }
            ),
            Row(
                "agent-reported-failure-fails-run-with-neutral-text",
                "実行器が失敗を申告したら成果物があっても全体を失敗にし、文言はバックエンドに依らない",
                Wpf,
                MockRunResult.ClientFailed,
                async c =>
                {
                    c.Agent.OnRun = MockRunnerContext.WriteArtifacts();
                    c.Agent.Outcome = new MockProjectAgentOutcome(
                        false,
                        "エージェントが失敗しました",
                        false
                    );
                    var result = await c.RunAsync(c.CreateRunner());

                    result.ClientSucceeded.Should().BeFalse();
                    // 成果物があればビルドの検証自体は走る（申告の失敗で全体は失敗のまま）
                    c.Build.BuildCallCount.Should().Be(1);
                    result.Message.Should().Contain("エージェントが失敗しました");
                    result.Message.Should().NotContain("Claude Code");
                    return result;
                }
            ),
            .. BothTargets(
                "agent-not-logged-in-reports-not-logged-in",
                "実行器が未ログインを申告したら、成果物が無いまま最終ビルドを試みず、結果の文を未ログインの案内にする（申告の失敗の文より優先）",
                MockRunResult.NotLoggedIn,
                async c =>
                {
                    // 根拠：MockProjectAgentOutcome の XmlDoc（NotLoggedIn＝未ログインが原因の失敗）と、
                    // 結果の文の組み立ての順（src/QuickER.AI.Mock/MockProjectAgentRunner.cs:750-753 は申告の失敗より先に未ログインを見る）
                    c.Agent.Outcome = new MockProjectAgentOutcome(false, "login required", true);
                    var result = await c.RunAsync(c.CreateRunner());

                    result.ClientSucceeded.Should().BeFalse();
                    result.ArtifactsPresent.Should().BeFalse();
                    c.Build.BuildCallCount.Should().Be(0);
                    result.Message.Should().Be(MockStrings.Mock_Result_NotLoggedIn);
                    return result;
                }
            ),
            .. BothTargets(
                "boundary-declined-confirmation-skips-build-as-not-verified",
                "境界越えの実行器がビルドの設定として読み得るファイルを書き、最終ビルドの前の確認を取り消したら、ビルドせずに未検証（成功でない）で完了する",
                MockRunResult.BuildDeclined,
                async c =>
                {
                    // 根拠：src/QuickER.AI.Mock/CLAUDE.md「最終ビルド前の変更検知」
                    // （キャンセルはビルドを実行せず BuildDeclined で未検証として完了し、成功扱いにしない）
                    c.Agent.EscapesSandbox = true;
                    c.Agent.OnRun = dir =>
                    {
                        c.WriteTargetArtifacts()(dir);
                        File.WriteAllText(
                            Path.Combine(c.ProjectDirectory, "Directory.Build.props"),
                            "<Project/>"
                        );
                    };
                    IReadOnlyList<string>? confirmed = null;
                    var result = await c.RunAsync(
                        c.CreateRunner(confirmBuild: changed =>
                        {
                            confirmed = changed;
                            return false;
                        })
                    );

                    confirmed.Should().NotBeNull().And.ContainMatch("*Directory.Build.props");
                    result.Success.Should().BeFalse();
                    result.BuildDeclined.Should().BeTrue();
                    result.ArtifactsPresent.Should().BeTrue();
                    c.Build.BuildCallCount.Should().Be(0);
                    return result;
                }
            ),
            Row(
                "agent-exception-folds-into-failure-and-log",
                "実行器の想定外の例外は失敗の結果に畳み、原因を結果とログに残す",
                Wpf,
                MockRunResult.ClientFailed,
                async c =>
                {
                    c.Agent.ThrowOnRun = new InvalidOperationException("agent exploded");
                    var result = await c.RunAsync(c.CreateRunner());

                    result.ClientSucceeded.Should().BeFalse();
                    result.Canceled.Should().BeFalse();
                    result.TimedOut.Should().BeFalse();
                    result.Message.Should().Contain("agent exploded");
                    File.ReadAllText(result.LogPath).Should().Contain("agent exploded");
                    return result;
                }
            ),
            .. BothTargets(
                "agent-cancellation-without-user-is-timeout",
                "利用者の中断でない取り消しの例外はタイムアウトとして扱い、ビルドせずログを残す",
                MockRunResult.TimedOut,
                async c =>
                {
                    c.Agent.ThrowOnRun = new OperationCanceledException();
                    var result = await c.RunAsync(
                        c.CreateRunner(timeout: TimeSpan.FromMinutes(30))
                    );

                    result.TimedOut.Should().BeTrue();
                    c.Build.BuildCallCount.Should().Be(0);
                    File.Exists(result.LogPath).Should().BeTrue();
                    return result;
                }
            ),
            .. BothTargets(
                "agent-cancellation-by-caller-token-is-canceled",
                "外部のトークンが立った取り消しは中断として扱い、ビルドしない",
                MockRunResult.Canceled,
                async c =>
                {
                    using var cts = new CancellationTokenSource();
                    await cts.CancelAsync();
                    c.Agent.ThrowOnRun = new OperationCanceledException();
                    var result = await c.RunAsync(c.CreateRunner(), cancellationToken: cts.Token);

                    result.Canceled.Should().BeTrue();
                    result.TimedOut.Should().BeFalse();
                    c.Build.BuildCallCount.Should().Be(0);
                    return result;
                }
            ),
            .. BothTargets(
                "boundary-confirm-exception-still-writes-log",
                "結果へ畳めない場所（最終ビルドの前の確認）の例外で抜けても、ビルドせずそこまでのログを書く",
                MockRunResult.Thrown,
                async c =>
                {
                    c.Agent.EscapesSandbox = true;
                    c.Agent.OnRun = dir =>
                    {
                        c.WriteTargetArtifacts()(dir);
                        File.WriteAllText(
                            Path.Combine(c.ProjectDirectory, "Directory.Build.props"),
                            "<Project/>"
                        );
                    };
                    var runner = c.CreateRunner(confirmBuild: _ =>
                        throw new InvalidOperationException("dialog failed")
                    );

                    var run = () => c.RunAsync(runner);

                    await run.Should().ThrowAsync<InvalidOperationException>();
                    c.Build.BuildCallCount.Should().Be(0);
                    var logPath = Path.Combine(c.Folder, MockProjectAgentRunner.LogFileName);
                    File.Exists(logPath).Should().BeTrue();
                    File.ReadAllText(logPath).Should().Contain("dialog failed");
                    return null;
                }
            ),
            Row(
                "progress-runner-lines-start-on-new-line",
                "実行器の文章が改行で終わらなくても、ランナーの次の行は新しい行から始まる",
                Wpf,
                MockRunResult.Success,
                async c =>
                {
                    c.Agent.OnRun = MockRunnerContext.WriteArtifacts();
                    c.Agent.ProgressText = "書きかけ";
                    var result = await c.RunAsync(c.CreateRunner());

                    var log = c.ProgressText;
                    log[(log.IndexOf("書きかけ", StringComparison.Ordinal) + "書きかけ".Length)..]
                        .Should()
                        .StartWith("\n");
                    return result;
                }
            ),
        ];

    /// <summary>InterruptAsync（外部のトークンを立てない中断）</summary>
    private static IEnumerable<ScenarioRow<MockRunnerScenario>> Interrupt() =>
        [
            Row(
                "interrupt-during-agent-is-canceled",
                "InterruptAsync だけでも実行器の実行を止め、タイムアウトでなく中断として分類する",
                Wpf,
                MockRunResult.Canceled,
                async c =>
                {
                    c.Agent.OnRun = MockRunnerContext.WriteArtifacts();
                    c.Agent.WaitForCancellation = true;
                    var runner = c.CreateRunner();
                    var run = c.RunAsync(runner);

                    // 実行器が走り始めてから中断する
                    var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(60);

                    while (c.Agent.CapturedRequest is null && DateTime.UtcNow < deadline)
                    {
                        await Task.Delay(10, c.CancellationToken);
                    }

                    await runner.InterruptAsync();
                    var result = await run;

                    result.Canceled.Should().BeTrue();
                    result.TimedOut.Should().BeFalse();
                    result.Message.Should().Be(MockStrings.Mock_Result_Canceled);
                    c.Build.BuildCallCount.Should().Be(0);
                    return result;
                }
            ),
            Row(
                "interrupt-during-build-is-canceled",
                "InterruptAsync は最終ビルドの最中にも効き、中断として分類する",
                Wpf,
                MockRunResult.Canceled,
                async c =>
                {
                    c.Agent.OnRun = MockRunnerContext.WriteArtifacts();
                    c.Build.SlowBuild = true;
                    var runner = c.CreateRunner();
                    var run = c.RunAsync(runner);

                    await c.Build.BuildStarted.Task.WaitAsync(c.CancellationToken);
                    await runner.InterruptAsync();
                    var result = await run;

                    result.Canceled.Should().BeTrue();
                    result.TimedOut.Should().BeFalse();
                    result.BuildSucceeded.Should().BeFalse();
                    return result;
                }
            ),
            Row(
                "interrupt-after-run-is-harmless",
                "ランが終わった後の InterruptAsync は何もせず、例外にならない",
                Wpf,
                MockRunResult.Success,
                async c =>
                {
                    c.Agent.OnRun = MockRunnerContext.WriteArtifacts();
                    var runner = c.CreateRunner();
                    var result = await c.RunAsync(runner);

                    var interrupt = () => runner.InterruptAsync();
                    await interrupt.Should().NotThrowAsync();
                    return result;
                }
            ),
        ];

    /// <summary>ランの外の委譲</summary>
    private static IEnumerable<ScenarioRow<MockRunnerScenario>> Delegation() =>
        [
            .. BothTargets(
                "delegation-interrupt-forwards-to-agent",
                "中断の要求は実行器の InterruptAsync へ渡る",
                MockRunResult.NotRun,
                async c =>
                {
                    await c.CreateRunner().InterruptAsync();

                    c.Agent.Interrupted.Should().BeTrue();
                    return null;
                }
            ),
            Row(
                "delegation-availability-follows-agent-and-build",
                "可用性の判定は実行器とビルド検証器へ委ねる",
                Wpf,
                MockRunResult.NotRun,
                async c =>
                {
                    var runner = c.CreateRunner();
                    c.Agent.Available = false;
                    c.Build.DotnetAvailable = false;

                    runner.IsClaudeAvailable().Should().BeFalse();
                    (await runner.IsDotnetAvailableAsync(c.CancellationToken)).Should().BeFalse();

                    c.Agent.Available = true;
                    c.Build.DotnetAvailable = true;

                    runner.IsClaudeAvailable().Should().BeTrue();
                    (await runner.IsDotnetAvailableAsync(c.CancellationToken)).Should().BeTrue();
                    return null;
                }
            ),
        ];

    /// <summary>画面の数の確認（足りないときに失敗にするかはターゲットの宣言）</summary>
    private static IEnumerable<ScenarioRow<MockRunnerScenario>> Screens() =>
        [
            Row(
                "screens-blazor-shortfall-fails-run",
                "Blazor は @page の行の数が設計の画面の数に届かなければ、ビルドが通っても失敗にして数を名指しする（App・Routes・レイアウトは数えない）",
                Blazor,
                MockRunResult.ScreensMissing,
                async c =>
                {
                    c.Agent.OnRun = MockRunnerContext.WriteProjectWithScreens(
                        3,
                        ("Components/App.razor", "<html></html>"),
                        ("Components/Routes.razor", "<Router />"),
                        ("Components/Layout/MainLayout.razor", "@inherits LayoutComponentBase"),
                        ("Components/Pages/Index.razor", "@page \"/\"\n<h1>Home</h1>")
                    );
                    var result = await c.RunAsync(c.CreateRunner());

                    result.ScreensMissing.Should().BeTrue();
                    result.ScreensExpected.Should().Be(3);
                    result.ScreenArtifactsFound.Should().Be(1);
                    result.BuildSucceeded.Should().BeTrue();
                    result
                        .Message.Should()
                        .Be(string.Format(MockStrings.Mock_Result_ScreensMissingFormat, 3, 1));
                    c.ProgressText.Should()
                        .Contain(string.Format(MockStrings.Mock_Run_ScreensMissingFormat, 3, 1));
                    return result;
                }
            ),
            Row(
                "screens-blazor-multiple-page-directives-count",
                "Blazor は 1 ファイルの複数の @page もその数だけ数え、画面の数に届けば合格する",
                Blazor,
                MockRunResult.Success,
                async c =>
                {
                    c.Agent.OnRun = MockRunnerContext.WriteProjectWithScreens(
                        3,
                        ("Components/App.razor", "<html></html>"),
                        (
                            "Components/Pages/Products.razor",
                            "@page \"/products\"\n@page \"/products/{Id:int}\"\n<h1/>"
                        ),
                        ("Components/Pages/Index.razor", "  @page \"/\"\n<h1/>")
                    );
                    var result = await c.RunAsync(c.CreateRunner());

                    result.ScreensMissing.Should().BeFalse();
                    result.ScreenArtifactsFound.Should().Be(3);
                    c.ProgressText.Should()
                        .NotContain(string.Format(MockStrings.Mock_Run_ScreensMissingFormat, 3, 3));
                    return result;
                }
            ),
            Row(
                "screens-wpf-shortfall-warns-only",
                "WPF は App.xaml を除いた .xaml が画面の数に届かなくても失敗にせず、警告の行だけを出す",
                Wpf,
                MockRunResult.Success,
                async c =>
                {
                    c.Agent.OnRun = MockRunnerContext.WriteProjectWithScreens(
                        3,
                        ("App.xaml", "<Application/>"),
                        ("MainWindow.xaml", "<Window/>")
                    );
                    var result = await c.RunAsync(c.CreateRunner());

                    result.ScreensMissing.Should().BeFalse();
                    result.ScreensExpected.Should().Be(3);
                    result.ScreenArtifactsFound.Should().Be(1);
                    c.ProgressText.Should()
                        .Contain(
                            string.Format(MockStrings.Mock_Run_ScreensMissingWarningFormat, 3, 1)
                        );
                    return result;
                }
            ),
            Row(
                "screens-without-design-not-counted",
                "設計（mock.json）が無ければ、成果物があっても画面の数は確かめない",
                Blazor,
                MockRunResult.Success,
                async c =>
                {
                    c.Agent.OnRun = dir =>
                        MockRunnerContext.WriteProjectFiles(dir, ("Home.razor", "@page \"/\""));
                    var result = await c.RunAsync(c.CreateRunner());

                    result.ScreensExpected.Should().BeNull();
                    result.ScreenArtifactsFound.Should().BeNull();
                    result.ScreensMissing.Should().BeFalse();
                    return result;
                }
            ),
        ];

    /// <summary>実行ログのバックエンド名と UI のファイルの拡張子（元の Theory の 6 ケース）</summary>
    private static IEnumerable<ScenarioRow<MockRunnerScenario>> Log() =>
        new (
            string Culture,
            string Backend,
            MockAgentTarget Target,
            bool AgentSucceeds,
            bool WriteArtifacts,
            MockRunResult Result
        )[]
        {
            ("en", "Codex", Blazor, true, true, MockRunResult.Success),
            ("en", "API Key", Blazor, false, true, MockRunResult.ClientFailed),
            ("en", "Copilot", Wpf, true, false, MockRunResult.ArtifactsMissing),
            ("en", "Claude Code", Wpf, false, false, MockRunResult.ClientFailed),
            ("ja", "Codex", Blazor, false, false, MockRunResult.ClientFailed),
            ("ja", "API キー", Wpf, true, true, MockRunResult.Success),
        }.Select(
            (log, index) =>
                Row(
                    $"log-names-backend-and-ui-extension-{index + 1}-{log.Culture}-{log.Target.Name}",
                    $"実行ログの完了・失敗の行はバックエンド名（{log.Backend}）を、成果物の行は UI のファイルの拡張子を出し、「Claude Code」「xaml」を固定で出さない（{log.Culture}）",
                    log.Target,
                    log.Result,
                    c =>
                        LogNamesBackendAndUiExtensionAsync(
                            c,
                            log.Culture,
                            log.Backend,
                            log.AgentSucceeds,
                            log.WriteArtifacts
                        )
                )
        );

    /// <summary>実行ログの行の本体（UI のカルチャを置いて流し、元へ戻す。期待の文は元のテストと同じ固定の文）</summary>
    private static async Task<MockProjectAgentResult?> LogNamesBackendAndUiExtensionAsync(
        MockRunnerContext c,
        string uiCulture,
        string backendName,
        bool agentSucceeds,
        bool writeArtifacts
    )
    {
        var blazor = c.Scenario.Target == Blazor;
        var uiFile = blazor ? "Home.razor" : "MainWindow.xaml";
        c.Agent.BackendName = backendName;
        c.Agent.Outcome = new MockProjectAgentOutcome(
            agentSucceeds,
            agentSucceeds ? null : "boom",
            false
        );
        c.Agent.OnRun = writeArtifacts
            ? dir => MockRunnerContext.WriteProjectFiles(dir, (uiFile, "<x/>"))
            : null;
        var previous = CultureInfo.CurrentUICulture;
        MockProjectAgentResult result;

        try
        {
            CultureInfo.CurrentUICulture = new CultureInfo(uiCulture);
            result = await c.RunAsync(c.CreateRunner());
        }
        finally
        {
            CultureInfo.CurrentUICulture = previous;
        }

        var log = c.ProgressText;
        var extension = blazor ? ".razor" : ".xaml";
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

        if (blazor)
        {
            log.Should().NotContain("xaml");
        }

        return result;
    }

    /// <summary>行を作る</summary>
    /// <param name="id">行の Id（{段}-{守っていること}）</param>
    /// <param name="description">説明</param>
    /// <param name="target">ターゲット</param>
    /// <param name="result">期待する結果の分類</param>
    /// <param name="body">流して表明する本体</param>
    private static ScenarioRow<MockRunnerScenario> Row(
        string id,
        string description,
        MockAgentTarget target,
        MockRunResult result,
        Func<MockRunnerContext, Task<MockProjectAgentResult?>> body
    ) => new(id, null, description, new MockRunnerScenario(target, result, body));

    /// <summary>ターゲットに依らない流れの行を、両方のターゲットで作る（Id の末尾に <c>-{ターゲット名}</c>）</summary>
    /// <remarks>
    /// ランナーの流れはターゲットに依らず、違いはプロファイルの成果物の検索パターンと画面の数の宣言だけ。
    /// 本体は成果物を <see cref="MockRunnerContext.WriteTargetArtifacts"/> で書き、ターゲットを名指しして分岐しない
    /// </remarks>
    /// <param name="id">行の Id の幹</param>
    /// <param name="description">説明</param>
    /// <param name="result">期待する結果の分類</param>
    /// <param name="body">流して表明する本体</param>
    private static IEnumerable<ScenarioRow<MockRunnerScenario>> BothTargets(
        string id,
        string description,
        MockRunResult result,
        Func<MockRunnerContext, Task<MockProjectAgentResult?>> body
    ) =>
        MockAgentTarget.All.Select(target =>
            Row($"{id}-{target.Name}", description, target, result, body)
        );
}
