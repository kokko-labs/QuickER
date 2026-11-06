using System.Diagnostics;
using System.IO;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using QuickER.AI.UI;
using QuickER.Extensibility;
using QuickER.Services;
using QuickER.Tests.TestDoubles;
using QuickER.Tests.TestSupport.Engines;
using static QuickER.Tests.Gui.Services.Lifecycle.LifecycleEntrance;
using static QuickER.Tests.Gui.Services.Lifecycle.LifecycleFault;

namespace QuickER.Tests.Gui.Services.Lifecycle;

/// <summary>
/// アプリの一生の表：クラッシュ（QuickER.Gui の <c>CrashHandlingService</c>・<c>CrashLogShutdownFailureReporter</c>）。
/// 実ファイルの書き込みは行の一時フォルダへ隔離し、ユーザーの <c>%LOCALAPPDATA%\QuickER</c> を汚さない。
/// 再入ガードは静的な状態なので、<c>HandleCrash</c> を流す行は行ごとに初期化し、終わりにも戻す
/// </summary>
internal static partial class LifecycleRows
{
    /// <summary>入れ子の例外（内側に根本原因を持つ）</summary>
    private static Exception NestedException() =>
        new InvalidOperationException("外側の失敗", new FileNotFoundException("内側の根本原因"));

    /// <summary>再入ガード（静的な状態）を行の前に初期化し、行の終わりにも戻す</summary>
    private static void ResetCrashGuard(LifecycleObservation o)
    {
        CrashHandlingService.ResetForTests();
        o.OnDispose(CrashHandlingService.ResetForTests);
    }

    private static IEnumerable<ScenarioRow<LifecycleCase>> CrashRows()
    {
        yield return Row(
            "crash-write-log-writes-file-and-returns-path-none",
            "WriteCrashLog: 指定フォルダへ書き出しフルパスを返す",
            WriteCrashLog,
            None,
            o =>
                o.Bag["path"] = CrashHandlingService.WriteCrashLog(
                    NestedException(),
                    "1.2.3",
                    o.Folder
                ),
            o =>
            {
                var path = o.Get<string?>("path");

                path.Should().NotBeNull();
                Path.GetFileName(path!).Should().StartWith("crash-").And.EndWith(".log");
                Path.GetDirectoryName(path!).Should().Be(o.Folder);

                var content = File.ReadAllText(path!);
                content.Should().Contain("1.2.3");
                content.Should().Contain("外側の失敗");
                content.Should().Contain("内側の根本原因");
            }
        );

        // 未観測タスク例外は 1 回の GC から連続発火しうるため、秒精度の名前では証跡が失われる
        yield return Row(
            "crash-write-log-consecutive-writes-do-not-overwrite-same-second",
            "WriteCrashLog: 同一秒内の連続書き出しでも上書きしない",
            WriteCrashLog,
            SameSecond,
            o =>
            {
                o.Bag["first"] = CrashHandlingService.WriteCrashLog(
                    new InvalidOperationException("1 件目"),
                    "1.2.3",
                    o.Folder
                );
                o.Bag["second"] = CrashHandlingService.WriteCrashLog(
                    new InvalidOperationException("2 件目"),
                    "1.2.3",
                    o.Folder
                );
            },
            o =>
            {
                var first = o.Get<string?>("first");
                var second = o.Get<string?>("second");

                first.Should().NotBeNull();
                second.Should().NotBeNull();
                second.Should().NotBe(first);

                Directory.GetFiles(o.Folder, "crash-*.log").Should().HaveCount(2);
                File.ReadAllText(first!).Should().Contain("1 件目");
                File.ReadAllText(second!).Should().Contain("2 件目");
            }
        );

        // 既存ファイルを「フォルダ」の位置に置く＝Directory.CreateDirectory が必ず失敗する状況は OS の仕掛けでしか作れない
        yield return Row(
            "crash-write-log-unwritable-path-returns-null-unwritable-path",
            "WriteCrashLog: 書き込み不能なパスでは null を返す",
            WriteCrashLog,
            UnwritablePath,
            o =>
            {
                var file = Path.Combine(o.Folder, $"crash-block-{Guid.NewGuid()}.tmp");
                File.WriteAllText(file, "block");

                o.Bag["path"] = CrashHandlingService.WriteCrashLog(
                    NestedException(),
                    "1.2.3",
                    file
                );
            },
            o => o.Get<string?>("path").Should().BeNull()
        );

        yield return Row(
            "crash-handle-runs-steps-in-order-none",
            "HandleCrash: 緊急保存 → ログ → 機能モジュールの後始末 → ダイアログの順に実行する",
            HandleCrash,
            None,
            o =>
            {
                ResetCrashGuard(o);

                CrashHandlingService.HandleCrash(
                    NestedException(),
                    "1.2.3",
                    () => o.Calls.Add("save"),
                    () => o.Calls.Add("close"),
                    logPath =>
                    {
                        o.Calls.Add("dialog");
                        o.Bag["receivedLogPath"] = logPath;
                    },
                    o.Folder
                );
            },
            o =>
            {
                o.Calls.Should().Equal("save", "close", "dialog");
                var receivedLogPath = o.Get<string?>("receivedLogPath");
                receivedLogPath.Should().NotBeNull();
                File.Exists(receivedLogPath!).Should().BeTrue();
            }
        );

        yield return Row(
            "crash-handle-emergency-save-failure-still-logs-and-shows-dialog-emergency-save-throws",
            "HandleCrash: 緊急保存が失敗してもログとダイアログは実行される",
            HandleCrash,
            EmergencySaveThrows,
            o =>
            {
                ResetCrashGuard(o);
                o.Bag["dialogShown"] = false;

                CrashHandlingService.HandleCrash(
                    NestedException(),
                    "1.2.3",
                    () => throw new InvalidOperationException("緊急保存の二次例外"),
                    () => { },
                    logPath =>
                    {
                        o.Bag["dialogShown"] = true;
                        o.Bag["receivedLogPath"] = logPath;
                    },
                    o.Folder
                );
            },
            o =>
            {
                o.Get<bool>("dialogShown").Should().BeTrue();
                o.Get<string?>("receivedLogPath").Should().NotBeNull();
            }
        );

        // 終了処理を止めないため、ダイアログが投げても呼び出し元へ伝播しない
        yield return Row(
            "crash-handle-dialog-failure-does-not-propagate-dialog-throws",
            "HandleCrash: ダイアログ表示の失敗は呼び出し元へ伝播しない",
            HandleCrash,
            DialogThrows,
            o =>
            {
                ResetCrashGuard(o);

                CrashHandlingService.HandleCrash(
                    NestedException(),
                    "1.2.3",
                    () => { },
                    () => { },
                    _ => throw new InvalidOperationException("ダイアログの二次例外"),
                    o.Folder
                );
            },
            _ => { }
        );

        yield return Row(
            "crash-handle-second-call-is-ignored-by-reentry-guard-reentry",
            "HandleCrash: 2 回目の呼び出しは再入ガードで何もしない",
            HandleCrash,
            Reentry,
            o =>
            {
                ResetCrashGuard(o);
                var saveCount = 0;
                var dialogCount = 0;

                for (var i = 0; i < 2; i++)
                {
                    CrashHandlingService.HandleCrash(
                        NestedException(),
                        "1.2.3",
                        () => saveCount++,
                        () => { },
                        _ => dialogCount++,
                        o.Folder
                    );
                }

                o.Bag["saveCount"] = saveCount;
                o.Bag["dialogCount"] = dialogCount;
            },
            o =>
            {
                o.Get<int>("saveCount").Should().Be(1);
                o.Get<int>("dialogCount").Should().Be(1);
                Directory.GetFiles(o.Folder, "crash-*.log").Should().ContainSingle();
            }
        );

        yield return Row(
            "crash-handle-module-close-failure-still-shows-dialog-step-throws",
            "HandleCrash: 機能モジュールの後始末が失敗してもダイアログは表示される",
            HandleCrash,
            StepThrows,
            o =>
            {
                ResetCrashGuard(o);
                o.Bag["dialogShown"] = false;

                o.Bag["handled"] = CrashHandlingService.HandleCrash(
                    NestedException(),
                    "1.2.3",
                    () => { },
                    () => throw new InvalidOperationException("後始末の二次例外"),
                    _ => o.Bag["dialogShown"] = true,
                    o.Folder
                );
            },
            o =>
            {
                o.Get<bool>("handled").Should().BeTrue();
                o.Get<bool>("dialogShown").Should().BeTrue();
            }
        );

        // 破棄がハングする後始末が上限で戻ることは、実時間の上限（5 秒）でしか確かめられない
        yield return Row(
            "crash-handle-failing-and-hanging-modules-reach-dialog-within-bound-hanging-cleanup",
            "HandleCrash: 投げる・ハングする後始末が混ざっても上限で戻りダイアログまで届く",
            HandleCrash,
            HangingCleanup,
            o =>
            {
                ResetCrashGuard(o);

                var log = new List<string>();
                var hangingEngine = new HangingAsyncDisposable();
                o.OnDispose(hangingEngine.Release);
                o.Bag["log"] = log;
                o.Bag["dialogShown"] = false;
                var modules = new IFeatureModule[]
                {
                    new ClosingModule(
                        "throws",
                        log,
                        () => throw new InvalidOperationException("壊れた UI")
                    ),
                    new ClosingModule(
                        "hangs",
                        log,
                        () =>
                            EngineShutdown.DisposeAll(TimeSpan.FromMilliseconds(300), hangingEngine)
                    ),
                    new ClosingModule("last", log),
                };
                var shutdown = new FeatureModuleShutdownOnce(
                    modules,
                    new ServiceCollection().BuildServiceProvider(),
                    o.Reporter
                );

                var stopwatch = Stopwatch.StartNew();

                CrashHandlingService.HandleCrash(
                    NestedException(),
                    "1.2.3",
                    () => { },
                    shutdown.CloseAll,
                    _ => o.Bag["dialogShown"] = true,
                    o.Folder
                );

                stopwatch.Stop();
                o.Bag["elapsed"] = stopwatch.Elapsed;
            },
            o =>
            {
                o.Get<List<string>>("log").Should().Equal("throws", "hangs", "last");
                o.Reporter.Steps.Should().Equal("FeatureModule.throws.OnMainWindowClosing");
                o.Get<bool>("dialogShown").Should().BeTrue("後始末の失敗・ハングで報告を止めない");
                o.Get<TimeSpan>("elapsed")
                    .Should()
                    .BeLessThan(TimeSpan.FromSeconds(5), "ハングした破棄は上限で戻る");
            }
        );

        yield return Row(
            "crash-handle-ui-crash-reentry-during-dialog-exits-once-after-dialog-reentry",
            "HandleUiCrash: ダイアログ表示中の再入は終了させず、ダイアログの後に 1 回だけ終了する",
            HandleUiCrash,
            Reentry,
            o =>
            {
                ResetCrashGuard(o);
                var events = o.Calls;

                CrashHandlingService.HandleUiCrash(
                    NestedException(),
                    "1.2.3",
                    () => events.Add("save"),
                    () => events.Add("close"),
                    _ =>
                    {
                        events.Add("dialog-open");

                        // ダイアログの入れ子のメッセージループで、もう一度 UI 例外が起きた
                        CrashHandlingService.HandleUiCrash(
                            new InvalidOperationException("2 回目"),
                            "1.2.3",
                            () => events.Add("save-2"),
                            () => events.Add("close-2"),
                            _ => events.Add("dialog-2"),
                            () => events.Add("exit-2"),
                            o.Folder
                        );

                        events.Add("dialog-closed");
                    },
                    () => events.Add("exit"),
                    o.Folder
                );
            },
            o => o.Calls.Should().Equal("save", "close", "dialog-open", "dialog-closed", "exit")
        );

        yield return Row(
            "crash-report-writes-shutdown-prefixed-log-with-step-name-none",
            "Report: shutdown- 接頭辞のログへ段の名前と例外を書き出す",
            ShutdownFailureReport,
            None,
            o =>
            {
                var reporter = new CrashLogShutdownFailureReporter(o.Folder);

                reporter.Report(
                    "AiChat.SaveSettings",
                    new InvalidOperationException(
                        "設定の保存に失敗",
                        new IOException("ディスクフル")
                    )
                );
            },
            o =>
            {
                Directory.GetFiles(o.Folder, "crash-*.log").Should().BeEmpty();
                var logs = Directory.GetFiles(o.Folder, "shutdown-*.log");
                logs.Should().ContainSingle();

                var content = File.ReadAllText(logs[0]);
                content.Should().Contain("AiChat.SaveSettings");
                content.Should().Contain("設定の保存に失敗");
                content.Should().Contain("ディスクフル");
            }
        );

        // 既存ファイルを「フォルダ」の位置に置く＝Directory.CreateDirectory が必ず失敗する状況は OS の仕掛けでしか作れない
        yield return Row(
            "crash-report-unwritable-path-does-not-throw-unwritable-path",
            "Report: 書き込み不能な保存先でも例外を投げない",
            ShutdownFailureReport,
            UnwritablePath,
            o =>
            {
                var file = Path.Combine(o.Folder, $"shutdown-block-{Guid.NewGuid()}.tmp");
                File.WriteAllText(file, "block");
                var reporter = new CrashLogShutdownFailureReporter(file);

                reporter.Report("AiChat.Close", new InvalidOperationException("失敗"));
            },
            _ => { }
        );
    }

    /// <summary>破棄が解放されるまで終わらない（ハングした破棄を模す）エンジン</summary>
    private sealed class HangingAsyncDisposable : IAsyncDisposable
    {
        private readonly TaskCompletionSource _release = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );

        public ValueTask DisposeAsync() => new(_release.Task);

        public void Release() => _release.TrySetResult();
    }
}
