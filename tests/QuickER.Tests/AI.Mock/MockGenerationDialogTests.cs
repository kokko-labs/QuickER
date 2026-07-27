using System.IO;
using System.Threading;
using FluentAssertions;
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
                // API キーは実 %APPDATA% の ApiKeyStore ではなくメモリ上のストアへ隔離する
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
}
