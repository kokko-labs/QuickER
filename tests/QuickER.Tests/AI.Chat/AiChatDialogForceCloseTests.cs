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

    /// <summary>エンジン 3 つのクライアントがそれぞれ 1 回ずつ破棄されるまで待ち、そうなったかを返す。</summary>
    /// <remarks>
    /// 破棄はスレッドプールで走り、<see cref="AiChatDialog.ForceClose"/> は上限（3 秒）までしか待たない。
    /// 全スイートの並列実行でプールが飽和すると上限内に始まらないことがあるため、長めの上限で到達を待つ
    /// （ForceClose が破棄を起動しなければ、待っても回数は 0 のまま）。モック生成側（MockGenerationDialogTests の AllProbesDisposed）と同じ作法
    /// </remarks>
    private static bool AllEnginesDisposed(
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
    /// ターンの実行中の ForceClose が、実行中のターンの中断を要求し、中断だけで終わらず、常駐プロセスを抱えるエンジンまで破棄することを検証する。
    /// </summary>
    /// <remarks>VM の段の順と期待はチャットのダイアログの表（ChatDialogTests の行 shutdown-force-close-steps-*）も見る。ここはウィンドウの配線を見る。</remarks>
    [Fact(DisplayName = "ターンの実行中の ForceClose は中断を要求し、エンジンの破棄まで通す")]
    public void ForceClose_DisposesEngines()
    {
        WpfApplicationTestSupport.RunSta(() =>
        {
            WpfApplicationTestSupport.EnsureApplicationResources();

            using var temp = new TestTempFolder();
            var folder = Path.Combine(temp.Path, "settings");
            var codex = new FakeCodexAppServerClient();
            var claudeCode = new DisposeTrackingClaudeCodeClient();
            var copilot = new FakeCopilotRuntimeClient();
            var keyStore = new InMemoryApiKeyStore();

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

            // ターンを実行中にしてから閉じる（ForceClose の最初の段 RequestInterrupt はターン中だけ中断を要求する）
            ChatDialogHarness.StartCopilotTurn(vm);

            dialog.ForceClose();

            AllEnginesDisposed(codex, claudeCode, copilot)
                .Should()
                .BeTrue("codex app-server・copilot ランタイムは常駐するため破棄が要る");
            copilot.AbortCallCount.Should().Be(1, "実行中のターンは打ち切る");
            codex.DisposeCount.Should().Be(1);
            claudeCode.DisposeCount.Should().Be(1);
            copilot.DisposeCount.Should().Be(1);
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
            using var temp = new TestTempFolder();
            var blockedFolder = Path.Combine(temp.Path, "blocked");
            File.WriteAllText(blockedFolder, "block");

            var codex = new FakeCodexAppServerClient();
            var claudeCode = new DisposeTrackingClaudeCodeClient();
            var copilot = new FakeCopilotRuntimeClient();
            var keyStore = new InMemoryApiKeyStore();
            var reporter = new RecordingShutdownFailureReporter();

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
            AllEnginesDisposed(codex, claudeCode, copilot)
                .Should()
                .BeTrue("保存の失敗で常駐プロセスの停止へ届かなくなってはいけない");
            codex.DisposeCount.Should().Be(1);
            claudeCode.DisposeCount.Should().Be(1);
            copilot.DisposeCount.Should().Be(1);
            closed.Should().BeTrue("保存の失敗でウィンドウが閉じ残ってはいけない");
        });
    }
}
