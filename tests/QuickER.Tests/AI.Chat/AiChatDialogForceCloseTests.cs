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
                    new AiChatDialog(vm, new RecordingShutdownFailureReporter())
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

    /// <summary>
    /// 途中の段（設定保存）が失敗しても、後続のエンジン破棄とクローズまで到達し、
    /// 失敗した段の名前が記録されることを検証する。
    /// </summary>
    /// <remarks>
    /// 終了時の設定保存はディスクフル・権限・保存先フォルダのパスに同名のファイルがある等で落ちる。
    /// そこで連鎖が止まると常駐プロセス（codex app-server・copilot ランタイム）が孤児として残る。
    /// ここでは「保存先フォルダと同名のファイルが在る」状況を作って実際に保存を失敗させる。
    /// </remarks>
    [Fact(DisplayName = "ForceClose は設定保存が失敗してもエンジン破棄とクローズまで進む")]
    public void ForceClose_WhenSaveSettingsThrows_StillDisposesEnginesAndCloses()
    {
        WpfApplicationTestSupport.RunSta(() =>
        {
            WpfApplicationTestSupport.EnsureApplicationResources();

            // 保存先フォルダと同じパスにファイルを置く＝Directory.CreateDirectory が必ず失敗する
            var blockedFolder = Path.Combine(
                Path.GetTempPath(),
                $"QuickERTests-blocked-{Guid.NewGuid():N}"
            );
            File.WriteAllText(blockedFolder, "block");

            var codex = new FakeCodexAppServerClient();
            var claudeCode = new DisposeTrackingClaudeCodeClient();
            var copilot = new FakeCopilotRuntimeClient();
            var keyStore = new InMemoryApiKeyStore();
            var reporter = new RecordingShutdownFailureReporter();

            try
            {
                var vm = new AiChatDialogViewModel(
                    host: null,
                    dispatcher: new SyncUiDispatcher(),
                    settingsStore: new AiSettingsStore(blockedFolder),
                    codexClient: codex,
                    claudeCodeClient: claudeCode,
                    copilotClient: copilot,
                    apiKeyLoader: keyStore.Load,
                    apiKeySaver: keyStore.Save
                );

                // 前提の確認: この構成では設定保存が実際に落ちる（落ちないと受け止めの検証にならない）
                var save = vm.SaveSettings;
                save.Should().Throw<Exception>();

                var dialog = WpfApplicationTestSupport.LoadXamlComponent(() =>
                    new AiChatDialog(vm, reporter)
                );

                dialog.WindowStartupLocation = WindowStartupLocation.Manual;
                dialog.Left = -4000;
                dialog.Top = -4000;
                dialog.ShowActivated = false;
                dialog.ShowInTaskbar = false;

                var closed = false;
                dialog.Closed += (_, _) => closed = true;

                dialog.Show();
                WpfApplicationTestSupport.DoEvents();

                var act = dialog.ForceClose;
                act.Should().NotThrow();

                reporter.Steps.Should().Equal("AiChat.SaveSettings");
                codex
                    .DisposeCount.Should()
                    .Be(1, "保存の失敗で常駐プロセスの停止へ届かなくなってはいけない");
                claudeCode.DisposeCount.Should().Be(1);
                copilot.DisposeCount.Should().Be(1);
                closed.Should().BeTrue("保存の失敗でウィンドウが閉じ残ってはいけない");
            }
            finally
            {
                File.Delete(blockedFolder);
            }
        });
    }
}
