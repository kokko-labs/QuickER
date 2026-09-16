using System.IO;
using System.Threading;
using AwesomeAssertions;
using QuickER.AI;
using QuickER.AI.Mock;
using QuickER.Model;
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
                    new MockGenerationDialog(viewModel)
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
                    new MockGenerationDialog(viewModel)
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
}
