using System.IO;
using System.Threading;
using AwesomeAssertions;
using QuickER.AI;
using QuickER.AI.Mock;
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
}
