using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using AwesomeAssertions;
using QuickER.AI;
using QuickER.AI.Mock;
using QuickER.AI.UI;
using QuickER.Model;
using QuickER.Tests.AI;
using QuickER.Tests.TestDoubles;
using QuickER.Tests.TestSupport;

namespace QuickER.Tests.AI.Mock;

/// <summary>
/// <see cref="MockGenerationDialog"/> の BAML 読み込み（InitializeComponent）が成功することを検証する。
/// WebView2 コントロールを含む XAML の参照解決・リソース解決の回帰（XamlParseException）を防ぐ。
/// ヘッドレスでは WebView2 のコア初期化や実描画は検証できないため、実操作の確認は実起動で行う。
/// </summary>
/// <remarks>
/// フォーカスの移動と流したキー入力に頼るテストを含むので、ほかのテストのウィンドウにアクティブを
/// 奪われないよう並列に走らせない（<see cref="WindowActivationTestCollection"/>）。
/// </remarks>
[Collection(WindowActivationTestCollection.Name)]
public class MockGenerationDialogTests
{
    /// <summary>同期実行のディスパッチャスタブ</summary>
    private sealed class SyncUiDispatcher : IUiDispatcher
    {
        public T Invoke<T>(Func<T> func) => func();
    }

    /// <summary>中断要求だけを記録するモックプロジェクト生成器のスタブ</summary>
    private sealed class InterruptRecordingGenerator : IMockProjectGenerator
    {
        public bool Interrupted { get; private set; }

        public IReadOnlyList<MockProjectTarget> Targets { get; } =
        [MockProjectTarget.Blazor, MockProjectTarget.Wpf];

        public bool IsAgentAvailable(ErChatBackendKind backend) => true;

        public Task<bool> IsDotnetAvailableAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(true);

        public Task<MockProjectGenerationResult> GenerateAsync(
            ErDiagram diagram,
            string mockFolder,
            string? additionalInstructions,
            string outputDirectory,
            string projectName,
            MockProjectTarget target,
            ErChatBackendKind backend,
            string model,
            string modelProvider,
            Action<string> onProgress,
            Func<IReadOnlyList<string>, bool>? confirmBuild = null,
            CancellationToken cancellationToken = default
        ) =>
            Task.FromResult(
                new MockProjectGenerationResult(true, string.Empty, outputDirectory, null)
            );

        public Task InterruptAsync()
        {
            Interrupted = true;
            return Task.CompletedTask;
        }
    }

    /// <summary>STA スレッド上でダイアログを構築し、InitializeComponent が例外を投げないことを検証する</summary>
    [Fact(DisplayName = "MockGenerationDialog の InitializeComponent が例外を投げない")]
    public void InitializeComponent_DoesNotThrow()
    {
        Exception? captured = null;

        var thread = new Thread(() =>
        {
            try
            {
                // App レベルのリソース（コンバータ等）を実アプリと同様に供給する。
                // 生成は共有ヘルパーで直列化し、並列テストとの Application 二重生成競合を防ぐ
                WpfApplicationTestSupport.EnsureApplicationResources();

                var settingsFolder = Path.Combine(
                    Path.GetTempPath(),
                    "QuickERTests",
                    Guid.NewGuid().ToString("N")
                );
                // API キーは実 %LOCALAPPDATA% の ApiKeyStore ではなくメモリ上のストアへ隔離する
                var keyStore = new InMemoryApiKeyStore();
                var viewModel = new MockGenerationDialogViewModel(
                    new StubDiagramSource(new ErDiagram()),
                    new SyncUiDispatcher(),
                    files: null,
                    settingsStore: new AiSettingsStore(settingsFolder),
                    apiKeyEngineFactory: null,
                    codexEngineFactory: null,
                    claudeCodeEngineFactory: null,
                    apiKeyLoader: keyStore.Load,
                    apiKeySaver: keyStore.Save
                );

                // InitializeComponent がここで実行される。WebView2 を含む XAML の
                // 名前空間・型解決に失敗すると XamlParseException が送出される。
                // BAML ロードは並列テストと競合しないよう直列化する
                _ = WpfApplicationTestSupport.LoadXamlComponent(() =>
                    new MockGenerationDialog(viewModel, new RecordingShutdownFailureReporter())
                );
            }
            catch (Exception ex)
            {
                captured = ex;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();
        thread.Join();

        captured.Should().BeNull();
    }

    /// <summary>
    /// アプリ終了時の強制クローズが、実行中のモックプロジェクト生成を中断してから閉じることを検証する。
    /// </summary>
    /// <remarks>
    /// 中断せずに閉じると、生成が起動した claude / codex / copilot / dotnet の子プロセスが孤児として残る。
    /// </remarks>
    [Fact(DisplayName = "ForceClose は実行中のモックプロジェクト生成を中断する")]
    public void ForceClose_InterruptsRunningGeneration()
    {
        Exception? captured = null;
        var generator = new InterruptRecordingGenerator();

        var thread = new Thread(() =>
        {
            try
            {
                WpfApplicationTestSupport.EnsureApplicationResources();

                var settingsFolder = Path.Combine(
                    Path.GetTempPath(),
                    "QuickERTests",
                    Guid.NewGuid().ToString("N")
                );
                var keyStore = new InMemoryApiKeyStore();
                var viewModel = new MockGenerationDialogViewModel(
                    new StubDiagramSource(new ErDiagram()),
                    new SyncUiDispatcher(),
                    files: null,
                    settingsStore: new AiSettingsStore(settingsFolder),
                    apiKeyEngineFactory: null,
                    codexEngineFactory: null,
                    claudeCodeEngineFactory: null,
                    copilotEngineFactory: null,
                    mockProjectGenerator: generator,
                    apiKeyLoader: keyStore.Load,
                    apiKeySaver: keyStore.Save
                );
                // 生成の実行中に終了させる状況を作る
                viewModel.IsMockGenInProgress = true;

                var dialog = WpfApplicationTestSupport.LoadXamlComponent(() =>
                    new MockGenerationDialog(viewModel, new RecordingShutdownFailureReporter())
                );
                dialog.ForceClose();
            }
            catch (Exception ex)
            {
                captured = ex;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();
        thread.Join();

        captured.Should().BeNull();
        generator.Interrupted.Should().BeTrue();
    }

    /// <summary>
    /// 途中の段（設定保存）が失敗しても、後続の中断・エンジン破棄・クローズまで到達し、
    /// 失敗した段の名前が記録されることを検証する。
    /// </summary>
    /// <remarks>
    /// 終了時の設定保存はディスクフル・権限・保存先フォルダのパスに同名のファイルがある等で落ちる。
    /// そこで連鎖が止まると、生成が起動した claude / codex / copilot / dotnet の子プロセスが孤児として残る。
    /// </remarks>
    [Fact(DisplayName = "ForceClose は設定保存が失敗しても中断とクローズまで進む")]
    public void ForceClose_WhenSaveSettingsThrows_StillInterruptsAndCloses()
    {
        Exception? captured = null;
        var generator = new InterruptRecordingGenerator();
        var reporter = new RecordingShutdownFailureReporter();
        var closed = false;

        // 保存先フォルダと同じパスにファイルを置く＝Directory.CreateDirectory が必ず失敗する
        var blockedFolder = Path.Combine(
            Path.GetTempPath(),
            $"QuickERTests-blocked-{Guid.NewGuid():N}"
        );
        File.WriteAllText(blockedFolder, "block");

        var thread = new Thread(() =>
        {
            try
            {
                WpfApplicationTestSupport.EnsureApplicationResources();

                var keyStore = new InMemoryApiKeyStore();
                var viewModel = new MockGenerationDialogViewModel(
                    new StubDiagramSource(new ErDiagram()),
                    new SyncUiDispatcher(),
                    files: null,
                    settingsStore: new AiSettingsStore(blockedFolder),
                    apiKeyEngineFactory: null,
                    codexEngineFactory: null,
                    claudeCodeEngineFactory: null,
                    copilotEngineFactory: null,
                    mockProjectGenerator: generator,
                    apiKeyLoader: keyStore.Load,
                    apiKeySaver: keyStore.Save
                );
                viewModel.IsMockGenInProgress = true;

                var dialog = WpfApplicationTestSupport.LoadXamlComponent(() =>
                    new MockGenerationDialog(viewModel, reporter)
                );
                dialog.Closed += (_, _) => closed = true;
                dialog.ForceClose();
            }
            catch (Exception ex)
            {
                captured = ex;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();
        thread.Join();

        try
        {
            captured.Should().BeNull();
            reporter.Steps.Should().Equal("MockGeneration.SaveSettings");
            generator.Interrupted.Should().BeTrue("保存の失敗より前の中断は完了していること");
            closed.Should().BeTrue("保存の失敗でウィンドウが閉じ残ってはいけない");
        }
        finally
        {
            File.Delete(blockedFolder);
        }
    }

    /// <summary>STA スレッドで処理を実行し、投げた例外を返す（無ければ null）</summary>
    private static Exception? RunOnSta(Action body)
    {
        Exception? captured = null;

        var thread = new Thread(() =>
        {
            try
            {
                body();
            }
            catch (Exception ex)
            {
                captured = ex;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();
        thread.Join();

        return captured;
    }

    /// <summary>プローブ用エンジン 3 つのクライアントを差し替えてダイアログを構築する</summary>
    private static MockGenerationDialog CreateDialogWithProbes(
        AiSettingsStore settingsStore,
        RecordingShutdownFailureReporter reporter,
        FakeCodexAppServerClient codex,
        DisposeTrackingClaudeCodeClient claudeCode,
        FakeCopilotRuntimeClient copilot
    )
    {
        WpfApplicationTestSupport.EnsureApplicationResources();

        var keyStore = new InMemoryApiKeyStore();
        var viewModel = new MockGenerationDialogViewModel(
            new StubDiagramSource(new ErDiagram()),
            new SyncUiDispatcher(),
            files: null,
            settingsStore: settingsStore,
            apiKeyEngineFactory: null,
            codexEngineFactory: null,
            claudeCodeEngineFactory: null,
            copilotEngineFactory: null,
            mockProjectGenerator: new InterruptRecordingGenerator(),
            apiKeyLoader: keyStore.Load,
            apiKeySaver: keyStore.Save
        );

        return WpfApplicationTestSupport.LoadXamlComponent(() =>
            new MockGenerationDialog(viewModel, reporter, codex, claudeCode, copilot)
        );
    }

    /// <summary>
    /// プローブ用エンジン 3 つのクライアントがそれぞれ 1 回ずつ破棄されたかを返す。
    /// </summary>
    /// <remarks>
    /// 破棄はスレッドプールで走り、<see cref="MockGenerationDialog.ForceClose"/> は上限（3 秒）までしか待たない。
    /// 全スイートの並列実行でプールが飽和すると上限内に始まらないことがあるため、長めの上限で到達を待つ
    /// （ForceClose が破棄を起動しなければ、待っても回数は 0 のまま）。
    /// </remarks>
    private static bool AllProbesDisposed(
        FakeCodexAppServerClient codex,
        DisposeTrackingClaudeCodeClient claudeCode,
        FakeCopilotRuntimeClient copilot
    ) =>
        SpinWait.SpinUntil(
            () =>
                codex.DisposeCount == 1
                && claudeCode.DisposeCount == 1
                && copilot.DisposeCount == 1,
            TimeSpan.FromSeconds(30)
        );

    /// <summary>
    /// アプリ終了時の強制クローズが、接続状態の確認に使うプローブ用エンジン 3 つまで破棄することを検証する。
    /// </summary>
    /// <remarks>
    /// プローブは会話のエンジンとは別にダイアログが持つ。破棄しないと、確認のために起動した
    /// codex app-server・copilot ランタイムの子プロセスがアプリ終了後も残る。
    /// エンジンの破棄はクライアントの破棄を呼ぶので、クライアント側の回数で観測する。
    /// </remarks>
    [Fact(DisplayName = "ForceClose はプローブ用エンジン 3 つを破棄する")]
    public void ForceClose_DisposesProbeEngines()
    {
        var codex = new FakeCodexAppServerClient();
        var claudeCode = new DisposeTrackingClaudeCodeClient();
        var copilot = new FakeCopilotRuntimeClient();
        var reporter = new RecordingShutdownFailureReporter();
        var settingsFolder = Path.Combine(
            Path.GetTempPath(),
            "QuickERTests",
            Guid.NewGuid().ToString("N")
        );

        var captured = RunOnSta(() =>
            CreateDialogWithProbes(
                    new AiSettingsStore(settingsFolder),
                    reporter,
                    codex,
                    claudeCode,
                    copilot
                )
                .ForceClose()
        );

        captured.Should().BeNull();
        reporter.Steps.Should().BeEmpty();
        AllProbesDisposed(codex, claudeCode, copilot).Should().BeTrue();
    }

    /// <summary>
    /// ダイアログが前面に戻ったとき（Activated）、図に依存する会話開始の可否を読み直すことを検証する。
    /// </summary>
    /// <remarks>
    /// 図の供給元は変更を通知しないため、メイン画面で図を編集して戻ってきた時点が唯一の読み直しの機会。
    /// ヘッドレスでは実際に前面化できないので、Activated を発火させる OnActivated を直接呼ぶ。
    /// </remarks>
    [Fact(DisplayName = "ダイアログが前面に戻ると会話開始の可否を読み直す")]
    public void Activated_RefreshesDiagramDependentState()
    {
        var reporter = new RecordingShutdownFailureReporter();
        var settingsFolder = Path.Combine(
            Path.GetTempPath(),
            "QuickERTests",
            Guid.NewGuid().ToString("N")
        );
        var raised = 0;

        var captured = RunOnSta(() =>
        {
            var dialog = CreateDialogWithProbes(
                new AiSettingsStore(settingsFolder),
                reporter,
                new FakeCodexAppServerClient(),
                new DisposeTrackingClaudeCodeClient(),
                new FakeCopilotRuntimeClient()
            );
            dialog.ViewModel.StartConversationCommand.CanExecuteChanged += (_, _) => raised++;

            typeof(System.Windows.Window)
                .GetMethod(
                    "OnActivated",
                    System.Reflection.BindingFlags.Instance
                        | System.Reflection.BindingFlags.NonPublic
                )!
                .Invoke(dialog, [EventArgs.Empty]);
        });

        captured.Should().BeNull();
        raised.Should().BeGreaterThan(0);
    }

    /// <summary>
    /// 途中の段（設定保存）が失敗しても、プローブ用エンジンの破棄まで届くことを検証する。
    /// </summary>
    [Fact(DisplayName = "ForceClose は設定保存が失敗してもプローブ用エンジンを破棄する")]
    public void ForceClose_WhenSaveSettingsThrows_StillDisposesProbeEngines()
    {
        var codex = new FakeCodexAppServerClient();
        var claudeCode = new DisposeTrackingClaudeCodeClient();
        var copilot = new FakeCopilotRuntimeClient();
        var reporter = new RecordingShutdownFailureReporter();

        // 保存先フォルダと同じパスにファイルを置く＝Directory.CreateDirectory が必ず失敗する
        var blockedFolder = Path.Combine(
            Path.GetTempPath(),
            $"QuickERTests-blocked-{Guid.NewGuid():N}"
        );
        File.WriteAllText(blockedFolder, "block");

        try
        {
            var captured = RunOnSta(() =>
                CreateDialogWithProbes(
                        new AiSettingsStore(blockedFolder),
                        reporter,
                        codex,
                        claudeCode,
                        copilot
                    )
                    .ForceClose()
            );

            captured.Should().BeNull();
            reporter.Steps.Should().Equal("MockGeneration.SaveSettings");
            AllProbesDisposed(codex, claudeCode, copilot).Should().BeTrue();
        }
        finally
        {
            File.Delete(blockedFolder);
        }
    }

    // ── 1-7 の対応（プローブの失敗・プレビューの URI・モックフォルダ欄の確定） ──

    /// <summary>claude CLI の有無の確認そのものが失敗するクライアント（作業フォルダの作成失敗などの代役）</summary>
    private sealed class ThrowingClaudeCodeClient : IClaudeCodeClient
    {
        public bool IsAvailable() => throw new IOException("probe failure");

        public Task<ClaudeCodeTurnOutcome> RunTurnAsync(
            string prompt,
            string? resumeSessionId,
            ClaudeCodeLaunchOptions options,
            Action<string> onAssistantText,
            CancellationToken cancellationToken
        ) => Task.FromResult(new ClaudeCodeTurnOutcome(true, null, null, false));

        public Task<ClaudeLoginProbeResult> ProbeLoginAsync(CancellationToken cancellationToken) =>
            Task.FromResult(ClaudeLoginProbeResult.LoggedIn);

        public void Interrupt() { }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    /// <summary>
    /// 接続タブの切替と「再確認」で接続状態の確認が例外を投げても、ウィンドウの外へ漏れず、ステータスに出ることを検証する。
    /// </summary>
    /// <remarks>
    /// タブ切替からは確認を完了を待たずに起動し、「再確認」は <c>AsyncRelayCommand</c> で例外をディスパッチャへ
    /// 投げ直す。ディスパッチャの同期コンテキストを入れておき、漏れた例外がディスパッチャを回した時点で表へ出るようにする。
    /// タブの選択は束縛で VM へ届くので、ダイアログを画面外へ表示して視覚ツリー（DataContext の継承）を組ませる。
    /// </remarks>
    [Fact(DisplayName = "接続状態の確認が失敗してもウィンドウの外へ漏れずステータスに出る")]
    public void ProbeFailure_IsReportedInStatus_NotThrown()
    {
        var settingsFolder = Path.Combine(
            Path.GetTempPath(),
            "QuickERTests",
            Guid.NewGuid().ToString("N")
        );
        string? statusAfterTab = null;
        string? statusAfterRefresh = null;

        var captured = RunOnSta(() =>
        {
            SynchronizationContext.SetSynchronizationContext(
                new System.Windows.Threading.DispatcherSynchronizationContext(
                    System.Windows.Threading.Dispatcher.CurrentDispatcher
                )
            );
            WpfApplicationTestSupport.EnsureApplicationResources();

            var keyStore = new InMemoryApiKeyStore();
            var viewModel = new MockGenerationDialogViewModel(
                new StubDiagramSource(new ErDiagram()),
                new SyncUiDispatcher(),
                files: null,
                settingsStore: new AiSettingsStore(settingsFolder),
                apiKeyEngineFactory: null,
                codexEngineFactory: null,
                claudeCodeEngineFactory: null,
                copilotEngineFactory: null,
                mockProjectGenerator: new InterruptRecordingGenerator(),
                apiKeyLoader: keyStore.Load,
                apiKeySaver: keyStore.Save
            );
            var dialog = WpfApplicationTestSupport.LoadXamlComponent(() =>
                new MockGenerationDialog(
                    viewModel,
                    new RecordingShutdownFailureReporter(),
                    new FakeCodexAppServerClient(),
                    new ThrowingClaudeCodeClient(),
                    new FakeCopilotRuntimeClient()
                )
            );

            dialog.WindowStartupLocation = WindowStartupLocation.Manual;
            dialog.Left = -4000;
            dialog.Top = -4000;
            dialog.ShowActivated = false;
            dialog.ShowInTaskbar = false;
            dialog.Show();

            try
            {
                WpfApplicationTestSupport.DoEvents();

                // Claude Code タブへ切り替える＝束縛経由で接続方式が確定し、プローブが起動する
                ((TabControl)dialog.FindName("BackendTabs")).SelectedIndex = 2;
                WpfApplicationTestSupport.DoEvents();
                statusAfterTab = viewModel.StatusMessage;

                viewModel.StatusMessage = string.Empty;
                dialog.ClaudeCodeRefreshCommand.ExecuteAsync(null).GetAwaiter().GetResult();
                WpfApplicationTestSupport.DoEvents();
                statusAfterRefresh = viewModel.StatusMessage;
            }
            finally
            {
                dialog.ForceClose();
            }
        });

        var expected = string.Format(
            QuickER.AI.Mock.Resources.Strings.Mock_ConnectionCheckFailedFormat,
            "probe failure"
        );
        captured.Should().BeNull(captured?.ToString());
        statusAfterTab.Should().Be(expected);
        statusAfterRefresh.Should().Be(expected);
    }

    /// <summary>
    /// プレビューへ渡すファイル URI が、<c>#</c>・<c>?</c>・<c>%</c> を含むパスを元のファイルのまま指すことを検証する。
    /// </summary>
    /// <remarks>
    /// 暗黙のファイルパスとして解釈させると、これらはパスの文字としてエスケープされ、フラグメントやクエリにならない
    /// （.NET 10 で実測）。<see cref="UriBuilder"/> で組むと <c>%20</c> を空白へ復号して別のファイルを指す。
    /// </remarks>
    [Theory(DisplayName = "プレビューの URI は記号を含むパスを元のファイルのまま指す")]
    [InlineData(@"C:\mocks\a#b\Order List.html")]
    [InlineData(@"C:\mocks\x#.html")]
    [InlineData(@"C:\mocks\b?c\x%20y.html")]
    [InlineData(@"\\server\share\a#b\x.html")]
    public void ToPreviewUri_KeepsPathCharacters(string path)
    {
        var uri = MockGenerationDialog.ToPreviewUri(path);

        uri.IsFile.Should().BeTrue();
        uri.LocalPath.Should().Be(path);
        uri.Fragment.Should().BeEmpty();
        uri.Query.Should().BeEmpty();
    }

    /// <summary>非空の図・API キー（ローカル LLM）で会話を始められる ViewModel を作る</summary>
    private static MockGenerationDialogViewModel CreateReadyViewModel(
        string settingsFolder,
        InMemoryApiKeyStore? keyStore = null
    )
    {
        keyStore ??= new InMemoryApiKeyStore();
        var viewModel = new MockGenerationDialogViewModel(
            new StubDiagramSource(
                new ErDiagram { Entities = { new Entity { TableName = "Customer" } } }
            ),
            new SyncUiDispatcher(),
            files: null,
            settingsStore: new AiSettingsStore(settingsFolder),
            apiKeyEngineFactory: (_, toolHost) => new FakeChatEngine(toolHost),
            codexEngineFactory: null,
            claudeCodeEngineFactory: null,
            copilotEngineFactory: null,
            mockProjectGenerator: new InterruptRecordingGenerator(),
            apiKeyLoader: keyStore.Load,
            apiKeySaver: keyStore.Save
        );
        viewModel.Connection.ApiProvider = AiProvider.LocalLlm;
        return viewModel;
    }

    /// <summary>ダイアログを画面外・非アクティブで表示して検証を実行し、最後に閉じる</summary>
    private static void WithShownDialog(
        MockGenerationDialogViewModel viewModel,
        Action<MockGenerationDialog> assert
    )
    {
        WpfApplicationTestSupport.EnsureApplicationResources();

        var dialog = WpfApplicationTestSupport.LoadXamlComponent(() =>
            new MockGenerationDialog(
                viewModel,
                new RecordingShutdownFailureReporter(),
                new FakeCodexAppServerClient(),
                new DisposeTrackingClaudeCodeClient(),
                new FakeCopilotRuntimeClient()
            )
        );
        dialog.WindowStartupLocation = WindowStartupLocation.Manual;
        dialog.Left = -4000;
        dialog.Top = -4000;
        dialog.ShowActivated = false;
        dialog.ShowInTaskbar = false;
        dialog.Show();

        try
        {
            dialog.UpdateLayout();
            WpfApplicationTestSupport.DoEvents();
            assert(dialog);
        }
        finally
        {
            dialog.ForceClose();
        }
    }

    /// <summary>
    /// ターン実行中は接続方式タブが全て無効化されて半透明になり、ターンが終われば戻ることを検証する
    /// （押してから断って戻すのでなく、押せなくする）。
    /// </summary>
    [Fact(DisplayName = "ターン実行中は接続方式タブが全て無効化され半透明になる")]
    public void BackendTabs_DisabledDuringTurn_AndReenabledAfter()
    {
        var settingsFolder = Path.Combine(
            Path.GetTempPath(),
            "QuickERTests",
            Guid.NewGuid().ToString("N")
        );

        try
        {
            var captured = RunOnSta(() =>
            {
                var viewModel = CreateReadyViewModel(settingsFolder);

                WithShownDialog(
                    viewModel,
                    dialog =>
                    {
                        var tabItems = ((TabControl)dialog.FindName("BackendTabs"))
                            .Items.Cast<TabItem>()
                            .ToList();
                        tabItems.Should().HaveCount(4);

                        Func<TabItem, double> borderOpacity = tab =>
                            ((Border)tab.Template.FindName("Bd", tab)).Opacity;

                        tabItems.Should().OnlyContain(t => t.IsEnabled && borderOpacity(t) == 1.0);

                        viewModel.IsTurnInProgress = true;
                        dialog.UpdateLayout();
                        WpfApplicationTestSupport.DoEvents();

                        tabItems
                            .Should()
                            .OnlyContain(
                                t => !t.IsEnabled && borderOpacity(t) == 0.5,
                                "ターン実行中は全タブが押せず半透明になる"
                            );

                        viewModel.IsTurnInProgress = false;
                        dialog.UpdateLayout();
                        WpfApplicationTestSupport.DoEvents();

                        tabItems.Should().OnlyContain(t => t.IsEnabled && borderOpacity(t) == 1.0);
                    }
                );
            });

            captured.Should().BeNull(captured?.ToString());
        }
        finally
        {
            if (Directory.Exists(settingsFolder))
            {
                Directory.Delete(settingsFolder, recursive: true);
            }
        }
    }

    /// <summary>
    /// 前回使った接続方式（保存した設定）が、表示時にタブの選択と VM の接続方式の両方へ反映されることを検証する。
    /// </summary>
    [Fact(DisplayName = "表示時に前回の接続方式のタブが選ばれ VM もその方式になる")]
    public void InitialBackend_IsShownOnLoad()
    {
        var settingsFolder = Path.Combine(
            Path.GetTempPath(),
            "QuickERTests",
            Guid.NewGuid().ToString("N")
        );

        try
        {
            var captured = RunOnSta(() =>
            {
                var saver = CreateReadyViewModel(settingsFolder);
                saver.Connection.SelectedBackend = ErChatBackendKind.Codex;
                saver.SaveSettings();
                var viewModel = CreateReadyViewModel(settingsFolder);

                WithShownDialog(
                    viewModel,
                    dialog =>
                    {
                        var tabControl = (TabControl)dialog.FindName("BackendTabs");
                        var tabItems = tabControl.Items.Cast<TabItem>().ToList();

                        viewModel.Connection.SelectedBackend.Should().Be(ErChatBackendKind.Codex);
                        tabControl.SelectedIndex.Should().Be(1);
                        tabItems
                            .Select(t => t.IsSelected)
                            .Should()
                            .Equal(false, true, false, false);
                        tabControl.SelectedContent.Should().BeSameAs(tabItems[1].Content);
                    }
                );
            });

            captured.Should().BeNull(captured?.ToString());
        }
        finally
        {
            if (Directory.Exists(settingsFolder))
            {
                Directory.Delete(settingsFolder, recursive: true);
            }
        }
    }

    /// <summary>モックフォルダ欄にフォーカスを置いて文字列を打ち込んだ状態にする（ViewModel へはまだ確定しない）</summary>
    private static TextBox TypeIntoMockFolderBox(MockGenerationDialog dialog, string text)
    {
        var box = (TextBox)dialog.FindName("MockFolderBox");
        box.Focus();
        WpfApplicationTestSupport.DoEvents();
        box.IsKeyboardFocused.Should().BeTrue("入力欄にフォーカスがある状態を作る");
        box.Text = text;
        return box;
    }

    /// <summary>
    /// モックフォルダ欄は、入力した直後には確定せず、入力が止まってしばらくすると確定することを検証する。
    /// </summary>
    [Fact(DisplayName = "モックフォルダ欄は入力が止まってから確定する")]
    public void MockFolderBox_CommitsAfterTypingPauses()
    {
        var baseFolder = Path.Combine(
            Path.GetTempPath(),
            "QuickERTests",
            Guid.NewGuid().ToString("N")
        );
        var target = Path.Combine(baseFolder, "mock");
        string? immediately = null;
        string? later = null;

        var captured = RunOnSta(() =>
            WithShownDialog(
                CreateReadyViewModel(Path.Combine(baseFolder, "settings")),
                dialog =>
                {
                    TypeIntoMockFolderBox(dialog, target);
                    immediately = dialog.ViewModel.MockFolder;

                    var deadline = DateTime.UtcNow.AddSeconds(5);

                    while (dialog.ViewModel.MockFolder != target && DateTime.UtcNow < deadline)
                    {
                        WpfApplicationTestSupport.DoEvents();
                        Thread.Sleep(20);
                    }

                    later = dialog.ViewModel.MockFolder;
                }
            )
        );

        captured.Should().BeNull(captured?.ToString());
        immediately.Should().BeEmpty("キー入力ごとには確定しない");
        later.Should().Be(target);
    }

    /// <summary>モックフォルダ欄で Enter を押すと、待たずに確定することを検証する。</summary>
    [Fact(DisplayName = "モックフォルダ欄は Enter で即座に確定する")]
    public void MockFolderBox_CommitsOnEnter()
    {
        var baseFolder = Path.Combine(
            Path.GetTempPath(),
            "QuickERTests",
            Guid.NewGuid().ToString("N")
        );
        var target = Path.Combine(baseFolder, "mock");
        string? afterEnter = null;

        var captured = RunOnSta(() =>
            WithShownDialog(
                CreateReadyViewModel(Path.Combine(baseFolder, "settings")),
                dialog =>
                {
                    var box = TypeIntoMockFolderBox(dialog, target);
                    dialog.ViewModel.MockFolder.Should().BeEmpty();

                    box.RaiseEvent(
                        new KeyEventArgs(
                            Keyboard.PrimaryDevice,
                            PresentationSource.FromVisual(box)!,
                            0,
                            Key.Enter
                        )
                        {
                            RoutedEvent = Keyboard.PreviewKeyDownEvent,
                        }
                    );
                    afterEnter = dialog.ViewModel.MockFolder;
                }
            )
        );

        captured.Should().BeNull(captured?.ToString());
        afterEnter.Should().Be(target);
    }

    /// <summary>
    /// フォルダ欄を書き換えた直後に「新しい会話」を押すと、マウスを押した時点で欄が確定し、
    /// 実行可否の判定と実行が新しいフォルダで行われることを検証する（実測: 確定は判定より前）。
    /// </summary>
    /// <remarks>
    /// ボタンはマウスを押した時点で自分へフォーカスを移す（<see cref="System.Windows.Controls.Primitives.ButtonBase"/>
    /// の OnMouseLeftButtonDown が最初に Focus を呼ぶ）。欄はフォーカスを失うと確定するので、クリック
    /// （マウスを離した時点の OnClick＝実行可否を確かめてから実行）より前に確定が済む。
    /// ヘッドレスでは本物のクリックを送れないため、マウスを押すイベントを実ボタンへ送り、離した時点の処理は
    /// OnClick を直接呼んで再現する。
    /// </remarks>
    [Fact(DisplayName = "フォルダ欄を書き換えて直ちに新しい会話を押すと、新しいフォルダで始まる")]
    public void StartConversation_RightAfterEditing_UsesEditedFolder()
    {
        var baseFolder = Path.Combine(
            Path.GetTempPath(),
            "QuickERTests",
            Guid.NewGuid().ToString("N")
        );
        var oldFolder = Path.Combine(baseFolder, "old");
        var newFolder = Path.Combine(baseFolder, "new");
        string? folderAfterMouseDown = null;
        var buttonFocusedAfterMouseDown = false;

        try
        {
            var captured = RunOnSta(() =>
            {
                var viewModel = CreateReadyViewModel(Path.Combine(baseFolder, "settings"));
                viewModel.MockFolder = oldFolder;

                WithShownDialog(
                    viewModel,
                    dialog =>
                    {
                        var button = (Button)dialog.FindName("StartConversationButton");
                        button.IsEnabled.Should().BeTrue("元のフォルダで会話を始められる状態から");
                        TypeIntoMockFolderBox(dialog, newFolder);
                        viewModel.MockFolder.Should().Be(oldFolder, "まだ確定していない");

                        PressAndClick(
                            button,
                            out folderAfterMouseDown,
                            out buttonFocusedAfterMouseDown,
                            viewModel
                        );
                    }
                );
            });

            captured.Should().BeNull(captured?.ToString());
            buttonFocusedAfterMouseDown
                .Should()
                .BeTrue("マウスを押した時点でボタンがフォーカスを取る");
            folderAfterMouseDown.Should().Be(newFolder, "フォーカスが移った時点で欄が確定する");
            File.Exists(Path.Combine(newFolder, MockManifest.ManifestFileName))
                .Should()
                .BeTrue("会話は新しいフォルダで始まる");
            Directory.Exists(oldFolder).Should().BeFalse("確定前の古いフォルダには何も作らない");
        }
        finally
        {
            if (Directory.Exists(baseFolder))
            {
                Directory.Delete(baseFolder, recursive: true);
            }
        }
    }

    /// <summary>
    /// フォルダ欄を会話に使えない値へ書き換えた直後に「新しい会話」を押すと、確定でボタンが無効になり、会話が始まらないことを検証する。
    /// </summary>
    [Fact(
        DisplayName = "フォルダ欄を使えない値へ書き換えて直ちに新しい会話を押しても、会話は始まらない"
    )]
    public void StartConversation_RightAfterEditingToBlockedValue_DoesNotStart()
    {
        var baseFolder = Path.Combine(
            Path.GetTempPath(),
            "QuickERTests",
            Guid.NewGuid().ToString("N")
        );
        var oldFolder = Path.Combine(baseFolder, "old");
        var enabledAfterMouseDown = true;
        var messages = -1;

        try
        {
            var captured = RunOnSta(() =>
            {
                var viewModel = CreateReadyViewModel(Path.Combine(baseFolder, "settings"));
                viewModel.MockFolder = oldFolder;

                WithShownDialog(
                    viewModel,
                    dialog =>
                    {
                        var button = (Button)dialog.FindName("StartConversationButton");
                        // 中身があるのに mock.json の無いフォルダ＝モックフォルダでないので会話に使えない
                        var notMockFolder = Path.Combine(baseFolder, "not-mock");
                        Directory.CreateDirectory(notMockFolder);
                        File.WriteAllText(
                            Path.Combine(notMockFolder, "index.html"),
                            "<html></html>"
                        );
                        TypeIntoMockFolderBox(dialog, notMockFolder);

                        PressAndClick(
                            button,
                            out _,
                            out _,
                            viewModel,
                            () => enabledAfterMouseDown = button.IsEnabled
                        );
                        messages = viewModel.Messages.Count;
                    }
                );
            });

            captured.Should().BeNull(captured?.ToString());
            enabledAfterMouseDown.Should().BeFalse("確定した値では会話を始められない");
            messages.Should().Be(0, "会話は始まらない");
            Directory.Exists(oldFolder).Should().BeFalse("確定前の古いフォルダで始めてはいけない");
        }
        finally
        {
            if (Directory.Exists(baseFolder))
            {
                Directory.Delete(baseFolder, recursive: true);
            }
        }
    }

    /// <summary>
    /// 実ボタンへマウスを押すイベントを送り、その時点の状態を控えてから、マウスを離した時点の処理（OnClick）を呼ぶ。
    /// </summary>
    private static void PressAndClick(
        Button button,
        out string folderAfterMouseDown,
        out bool buttonFocusedAfterMouseDown,
        MockGenerationDialogViewModel viewModel,
        Action? afterMouseDown = null
    )
    {
        button.RaiseEvent(
            new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
            {
                RoutedEvent = UIElement.MouseLeftButtonDownEvent,
            }
        );
        folderAfterMouseDown = viewModel.MockFolder;
        buttonFocusedAfterMouseDown = button.IsKeyboardFocused;
        afterMouseDown?.Invoke();

        typeof(System.Windows.Controls.Primitives.ButtonBase)
            .GetMethod(
                "OnClick",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic
            )!
            .Invoke(button, null);
        WpfApplicationTestSupport.DoEvents();
    }

    /// <summary>
    /// API キー欄へ打ち込んだキーは、打鍵ごとではなく欄から離れた時点で保存されることを検証する（実 XAML の配線）。
    /// </summary>
    [Fact(DisplayName = "モック生成の API キー欄は離れた時点でキーを保存する")]
    public void ApiKeyBox_SavesKeyWhenFocusLeaves()
    {
        var baseFolder = Path.Combine(
            Path.GetTempPath(),
            "QuickERTests",
            Guid.NewGuid().ToString("N")
        );
        var keyStore = new InMemoryApiKeyStore();
        var savedWhileTyping = true;
        string? savedAfterLeaving = null;

        var captured = RunOnSta(() =>
        {
            var viewModel = CreateReadyViewModel(Path.Combine(baseFolder, "settings"), keyStore);
            viewModel.Connection.ApiProvider = AiProvider.OpenAI;

            WithShownDialog(
                viewModel,
                dialog =>
                {
                    var box = (PasswordBox)dialog.FindName("ApiKeyBox");
                    box.Focus();
                    WpfApplicationTestSupport.DoEvents();
                    box.IsKeyboardFocused.Should().BeTrue("API キー欄にフォーカスがある状態を作る");

                    box.Password = "sk-typed";
                    WpfApplicationTestSupport.DoEvents();
                    viewModel.Connection.ApiKey.Should().Be("sk-typed");
                    savedWhileTyping = keyStore.Entries.ContainsKey("OpenAiApiKey");

                    ((TextBox)dialog.FindName("UserInputBox")).Focus();
                    WpfApplicationTestSupport.DoEvents();
                    savedAfterLeaving = keyStore.Entries.GetValueOrDefault("OpenAiApiKey");
                }
            );
        });

        captured.Should().BeNull(captured?.ToString());
        savedWhileTyping.Should().BeFalse("打鍵ごとには保存しない");
        savedAfterLeaving.Should().Be("sk-typed");
    }

    // ── 1-8 の対応（× で閉じたときの設定保存・チャット欄の自動スクロール） ──

    /// <summary>設定の保存先と確認・エラー表示を差し替えた ViewModel を作る</summary>
    private static MockGenerationDialogViewModel CreateViewModelWithDialogs(
        string settingsFolder,
        StubDialogService dialogs
    )
    {
        var keyStore = new InMemoryApiKeyStore();

        return new MockGenerationDialogViewModel(
            new StubDiagramSource(new ErDiagram()),
            new SyncUiDispatcher(),
            files: null,
            settingsStore: new AiSettingsStore(settingsFolder),
            apiKeyEngineFactory: null,
            codexEngineFactory: null,
            claudeCodeEngineFactory: null,
            copilotEngineFactory: null,
            mockProjectGenerator: new InterruptRecordingGenerator(),
            dialogService: dialogs,
            apiKeyLoader: keyStore.Load,
            apiKeySaver: keyStore.Save
        );
    }

    /// <summary>
    /// × で閉じたときに設定の保存が失敗しても、例外を漏らさずウィンドウを隠し、
    /// 隠した後でエラーを 1 回知らせる（理由はステータスにも残る）ことを検証する。
    /// </summary>
    [Fact(DisplayName = "× で閉じたとき設定の保存が失敗しても隠れてエラーを知らせる")]
    public void CloseButton_WhenSaveFails_HidesAndReportsError()
    {
        // 保存先フォルダと同じパスにファイルを置く＝設定の書き込みが必ず失敗する
        var blockedFolder = Path.Combine(
            Path.GetTempPath(),
            $"QuickERTests-blocked-{Guid.NewGuid():N}"
        );
        File.WriteAllText(blockedFolder, "block");
        var dialogs = new StubDialogService();

        try
        {
            WpfApplicationTestSupport.RunSta(() =>
            {
                var viewModel = CreateViewModelWithDialogs(blockedFolder, dialogs);

                WithShownDialog(
                    viewModel,
                    dialog =>
                    {
                        var visibleWhenErrorShown = true;
                        dialogs.OnBeforeShow = () => visibleWhenErrorShown = dialog.IsVisible;

                        var close = dialog.Close;
                        close.Should().NotThrow();

                        dialog.IsVisible.Should().BeFalse("保存の失敗でも × で隠れる");
                        visibleWhenErrorShown.Should().BeFalse("エラーは隠した後に出す");
                        dialogs.ErrorMessages.Should().ContainSingle();
                        viewModel.StatusMessage.Should().Be(dialogs.ErrorMessages[0]);
                    }
                );
            });
        }
        finally
        {
            File.Delete(blockedFolder);
        }
    }

    /// <summary>
    /// ストリーミングで最後の吹き出しが伸びたとき、末尾を見ていればチャット欄が追従することを検証する
    /// （吹き出しの本文が伸びるだけではメッセージ一覧は変わらないため、一覧の変更だけでは追従しない）。
    /// </summary>
    [Fact(DisplayName = "ストリーミングで吹き出しが伸びると末尾を見ているチャット欄は追従する")]
    public void StreamingGrowth_WhenAtBottom_FollowsToEnd()
    {
        var folder = Path.Combine(Path.GetTempPath(), "QuickERTests", Guid.NewGuid().ToString("N"));

        try
        {
            WpfApplicationTestSupport.RunSta(() =>
            {
                var viewModel = CreateViewModelWithDialogs(folder, new StubDialogService());

                WithShownDialog(
                    viewModel,
                    dialog =>
                    {
                        for (var i = 0; i < 30; i++)
                        {
                            viewModel.Messages.Add(
                                new ErChatMessage
                                {
                                    Role = ErChatMessageRole.Assistant,
                                    Content = $"message {i}",
                                }
                            );
                        }

                        WpfApplicationTestSupport.DoEvents();
                        var viewer = dialog.ChatScrollViewer;
                        var scrollableBefore = viewer.ScrollableHeight;
                        scrollableBefore.Should().BeGreaterThan(0, "末尾がある状態を作る");

                        // 新しい吹き出しの ScrollToEnd は位置を「無限大」として覚え、以後の伸びにも自然に付いていく。
                        // 追従が切れるのは利用者が手で動かした後なので、手で末尾まで戻した状態（有限の位置）を作る
                        viewer.ScrollToVerticalOffset(scrollableBefore);
                        WpfApplicationTestSupport.DoEvents();
                        viewer
                            .VerticalOffset.Should()
                            .Be(scrollableBefore, "末尾を見ている状態を作る");

                        var last = viewModel.Messages[^1];

                        for (var i = 0; i < 20; i++)
                        {
                            last.Content += "\nstreamed line " + i;
                        }

                        WpfApplicationTestSupport.DoEvents();

                        viewer.ScrollableHeight.Should().BeGreaterThan(scrollableBefore);
                        viewer.VerticalOffset.Should().Be(viewer.ScrollableHeight);
                    }
                );
            });
        }
        finally
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }
        }
    }

    /// <summary>
    /// 回答の吹き出しは Markdown 表示の部品を使い、応答の終わりとウィンドウのトグルに従って
    /// テキスト表示と Markdown 表示を切り替えることを、実 XAML の配線で検証する。
    /// </summary>
    [Fact(DisplayName = "回答の吹き出しは応答の終わりとトグルに従って Markdown 表示を切り替える")]
    public void AssistantBubble_FollowsStreamingAndToggle()
    {
        var folder = Path.Combine(Path.GetTempPath(), "QuickERTests", Guid.NewGuid().ToString("N"));

        try
        {
            WpfApplicationTestSupport.RunSta(() =>
            {
                var vm = CreateViewModelWithDialogs(folder, new StubDialogService());

                WithShownDialog(
                    vm,
                    dialog =>
                    {
                        vm.Messages.Add(
                            new ErChatMessage
                            {
                                Role = ErChatMessageRole.Assistant,
                                Content = "**done**",
                            }
                        );
                        vm.Messages.Add(
                            new ErChatMessage
                            {
                                Role = ErChatMessageRole.Assistant,
                                Content = "**partial",
                                IsStreaming = true,
                            }
                        );
                        WpfApplicationTestSupport.DoEvents();

                        var views = FindVisualChildren<AssistantMessageView>(dialog).ToList();
                        views.Should().HaveCount(2);
                        views[0].IsShowingMarkdown.Should().BeTrue("応答済みの回答");
                        views[0].LinkOpener.Should().BeSameAs(vm.LinkOpener);
                        views[1].IsShowingMarkdown.Should().BeFalse("応答中の回答");

                        vm.IsTurnInProgress = true;
                        vm.IsTurnInProgress = false;
                        views[1].IsShowingMarkdown.Should().BeTrue("ターンが終わったら切り替わる");

                        var toggle = FindVisualChildren<CheckBox>(dialog)
                            .Single(c =>
                                Equals(
                                    c.Content,
                                    QuickER.AI.Mock.Resources.Strings.Mock_ShowMarkdown
                                )
                            );
                        toggle.IsChecked.Should().BeTrue();
                        toggle.IsChecked = false;
                        vm.Connection.ShowAssistantMarkdown.Should().BeFalse();
                        views.Should().OnlyContain(v => !v.IsShowingMarkdown);

                        vm.Connection.ShowAssistantMarkdown = true;
                        views.Should().OnlyContain(v => v.IsShowingMarkdown);
                    }
                );
            });
        }
        finally
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }
        }
    }

    /// <summary>ビジュアルツリーから指定型の子孫を列挙する</summary>
    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject root)
        where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);

            if (child is T match)
            {
                yield return match;
            }

            foreach (var nested in FindVisualChildren<T>(child))
            {
                yield return nested;
            }
        }
    }
}
