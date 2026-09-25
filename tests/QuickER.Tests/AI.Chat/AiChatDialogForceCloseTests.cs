using System.IO;
using System.Windows;
using AwesomeAssertions;
using QuickER.AI;
using QuickER.AI.Chat;
using QuickER.AI.UI;
using QuickER.Tests.AI;
using QuickER.Tests.TestDoubles;
using QuickER.Tests.TestSupport;

namespace QuickER.Tests.AI.Chat;

/// <summary>
/// アプリ終了経路（<see cref="AiChatDialog.ForceClose"/>）が、ウィンドウを閉じる前に
/// エンジンの破棄まで通すことを検証するテストクラス。
/// </summary>
/// <remarks>
/// VM 単体テストでは ForceClose の配線切れを検出できないため、実ダイアログを画面外へ
/// <c>Show</c> してから ForceClose を呼び、フェイククライアントの破棄回数で到達を確かめる。
/// </remarks>
public class AiChatDialogForceCloseTests
{
    private sealed class SyncUiDispatcher : IUiDispatcher
    {
        public T Invoke<T>(Func<T> func) => func();
    }

    /// <summary>
    /// ForceClose が中断だけで終わらず、常駐プロセスを抱えるエンジンまで破棄することを検証する。
    /// </summary>
    [Fact(DisplayName = "ForceClose はエンジンの破棄まで通す")]
    public void ForceClose_DisposesEngines()
    {
        WpfApplicationTestSupport.RunSta(() =>
        {
            WpfApplicationTestSupport.EnsureApplicationResources();

            var folder = Path.Combine(
                Path.GetTempPath(),
                "QuickERTests",
                Guid.NewGuid().ToString("N")
            );
            var codex = new FakeCodexAppServerClient();
            var claudeCode = new DisposeTrackingClaudeCodeClient();
            var copilot = new FakeCopilotRuntimeClient();
            var keyStore = new InMemoryApiKeyStore();

            try
            {
                var vm = new AiChatDialogViewModel(
                    host: null,
                    dispatcher: new SyncUiDispatcher(),
                    settingsStore: new AiSettingsStore(folder),
                    codexClient: codex,
                    claudeCodeClient: claudeCode,
                    copilotClient: copilot,
                    apiKeyLoader: keyStore.Load,
                    apiKeySaver: keyStore.Save
                );

                var dialog = WpfApplicationTestSupport.LoadXamlComponent(() =>
                    new AiChatDialog(vm)
                );

                // 画面外＋非アクティブで Show し、テスト実行中に開発者のデスクトップを妨げない
                dialog.WindowStartupLocation = WindowStartupLocation.Manual;
                dialog.Left = -4000;
                dialog.Top = -4000;
                dialog.ShowActivated = false;
                dialog.ShowInTaskbar = false;

                dialog.Show();
                WpfApplicationTestSupport.DoEvents();

                dialog.ForceClose();

                codex.DisposeCount.Should().Be(1, "codex app-server は常駐するため破棄が要る");
                claudeCode.DisposeCount.Should().Be(1);
                copilot.DisposeCount.Should().Be(1, "copilot ランタイムは常駐するため破棄が要る");
            }
            finally
            {
                if (Directory.Exists(folder))
                {
                    Directory.Delete(folder, recursive: true);
                }
            }
        });
    }
}
