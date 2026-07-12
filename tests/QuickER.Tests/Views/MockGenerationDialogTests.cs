using System.IO;
using System.Threading;
using FluentAssertions;
using QuickER.AI;
using QuickER.AI.Mock;
using QuickER.Model;
using QuickER.Provider;
using QuickER.SqlServer;

namespace QuickER.Tests.Views;

/// <summary>
/// <see cref="MockGenerationDialog"/> の BAML 読み込み（InitializeComponent）が成功することを検証する。
/// WebView2 コントロールを含む XAML の参照解決・リソース解決の回帰（XamlParseException）を防ぐ。
/// ヘッドレスでは WebView2 のコア初期化や実描画は検証できないため、実操作の確認は実起動で行う。
/// </summary>
public class MockGenerationDialogTests
{
    /// <summary>現在の ER 図を供給する最小スタブ</summary>
    private sealed class StubDiagramSource : IMockDiagramSource
    {
        public bool IsEmpty => true;

        public ErDiagram GetDiagram() => new();

        public DatabaseProviderRegistry Providers { get; } = new([new SqlServerProvider()]);
    }

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
                var viewModel = new MockGenerationDialogViewModel(
                    new StubDiagramSource(),
                    new SyncUiDispatcher(),
                    files: null,
                    codexSettingsStore: new CodexAppServerSettingsStore(settingsFolder),
                    apiKeyEngineFactory: null,
                    codexEngineFactory: null,
                    claudeCodeEngineFactory: null
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
