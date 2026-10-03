using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using QuickER.AI.UI;
using QuickER.Extensibility;
using QuickER.Services;
using QuickER.Tests.Resources;
using QuickER.Tests.TestDoubles;

namespace QuickER.Tests.Gui.Services;

/// <summary>
/// クラッシュ処理（<see cref="CrashHandlingService"/>）の整形・ログ書き出し・実行順序を検証するテストクラス。
/// </summary>
/// <remarks>
/// 実ファイル書き込みは一時フォルダへ隔離し（<c>baseDirOverride</c>）、ユーザーの
/// <c>%LOCALAPPDATA%\QuickER</c> を汚さない。再入ガードは静的状態のため各ケース冒頭で初期化する。
/// </remarks>
public class CrashHandlingServiceTests
{
    /// <summary>入れ子の例外（内側に根本原因を持つ）を組み立てる</summary>
    private static Exception CreateNestedException() =>
        new InvalidOperationException("外側の失敗", new FileNotFoundException("内側の根本原因"));

    /// <summary>詳細整形に例外型・メッセージ・内部例外・バージョンが含まれることを検証する</summary>
    [Fact(DisplayName = "FormatDetails: 例外連鎖・バージョンが本文へ含まれる")]
    public void FormatDetails_ContainsExceptionChainAndVersion()
    {
        var details = CrashHandlingService.FormatDetails(CreateNestedException(), "9.9.9+abcdef");

        details.Should().Contain("9.9.9+abcdef");
        details.Should().Contain("System.InvalidOperationException");
        details.Should().Contain("外側の失敗");
        details.Should().Contain("System.IO.FileNotFoundException");
        details.Should().Contain("内側の根本原因");
    }

    /// <summary>スタックトレースを持つ実例外で、トレース本文が詳細へ含まれることを検証する</summary>
    [Fact(DisplayName = "FormatDetails: 実際に投げられた例外のスタックトレースを含む")]
    public void FormatDetails_ContainsStackTrace()
    {
        Exception captured;

        try
        {
            throw new InvalidOperationException("投げられた例外");
        }
        catch (InvalidOperationException ex)
        {
            captured = ex;
        }

        var details = CrashHandlingService.FormatDetails(captured, "0.0.1");

        details.Should().Contain(nameof(FormatDetails_ContainsStackTrace));
    }

    /// <summary>指定フォルダへログが書き出され、パスと内容が返ることを検証する</summary>
    [Fact(DisplayName = "WriteCrashLog: 指定フォルダへ書き出しフルパスを返す")]
    public void WriteCrashLog_WritesFileAndReturnsPath()
    {
        var folder = CreateTempFolder();

        try
        {
            var path = CrashHandlingService.WriteCrashLog(CreateNestedException(), "1.2.3", folder);

            path.Should().NotBeNull();
            Path.GetFileName(path!).Should().StartWith("crash-").And.EndWith(".log");
            Path.GetDirectoryName(path!).Should().Be(folder);

            var content = File.ReadAllText(path!);
            content.Should().Contain("1.2.3");
            content.Should().Contain("外側の失敗");
            content.Should().Contain("内側の根本原因");
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    /// <summary>同一秒内に連続して書き出しても互いを上書きせず別ファイルになることを検証する</summary>
    /// <remarks>未観測タスク例外は 1 回の GC から連続発火しうるため、秒精度の名前では証跡が失われる</remarks>
    [Fact(DisplayName = "WriteCrashLog: 同一秒内の連続書き出しでも上書きしない")]
    public void WriteCrashLog_ConsecutiveWritesWithinSameSecond_DoNotOverwrite()
    {
        var folder = CreateTempFolder();

        try
        {
            var first = CrashHandlingService.WriteCrashLog(
                new InvalidOperationException("1 件目"),
                "1.2.3",
                folder
            );
            var second = CrashHandlingService.WriteCrashLog(
                new InvalidOperationException("2 件目"),
                "1.2.3",
                folder
            );

            first.Should().NotBeNull();
            second.Should().NotBeNull();
            second.Should().NotBe(first);

            Directory.GetFiles(folder, "crash-*.log").Should().HaveCount(2);
            File.ReadAllText(first!).Should().Contain("1 件目");
            File.ReadAllText(second!).Should().Contain("2 件目");
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    /// <summary>保存先フォルダを作れない場合に例外を漏らさず null を返すことを検証する</summary>
    [Fact(DisplayName = "WriteCrashLog: 書き込み不能なパスでは null を返す")]
    public void WriteCrashLog_UnwritablePath_ReturnsNull()
    {
        // 既存ファイルを「フォルダ」として指定する＝Directory.CreateDirectory が必ず失敗する状況
        var file = Path.Combine(Path.GetTempPath(), $"crash-block-{Guid.NewGuid()}.tmp");
        File.WriteAllText(file, "block");

        try
        {
            CrashHandlingService
                .WriteCrashLog(CreateNestedException(), "1.2.3", file)
                .Should()
                .BeNull();
        }
        finally
        {
            File.Delete(file);
        }
    }

    /// <summary>緊急保存 → ログ → 機能モジュールの後始末 → ダイアログの順に実行され、ダイアログへログパスが渡ることを検証する</summary>
    [Fact(
        DisplayName = "HandleCrash: 緊急保存 → ログ → 機能モジュールの後始末 → ダイアログの順に実行する"
    )]
    public void HandleCrash_RunsStepsInOrder()
    {
        CrashHandlingService.ResetForTests();

        var folder = CreateTempFolder();
        var steps = new List<string>();
        string? receivedLogPath = null;

        try
        {
            CrashHandlingService.HandleCrash(
                CreateNestedException(),
                "1.2.3",
                () => steps.Add("save"),
                () => steps.Add("close"),
                logPath =>
                {
                    steps.Add("dialog");
                    receivedLogPath = logPath;
                },
                folder
            );

            steps.Should().Equal("save", "close", "dialog");
            receivedLogPath.Should().NotBeNull();
            File.Exists(receivedLogPath!).Should().BeTrue();
        }
        finally
        {
            CrashHandlingService.ResetForTests();
            Directory.Delete(folder, recursive: true);
        }
    }

    /// <summary>緊急保存が例外を投げても、ログ書き出しとダイアログ表示が続行されることを検証する</summary>
    [Fact(DisplayName = "HandleCrash: 緊急保存が失敗してもログとダイアログは実行される")]
    public void HandleCrash_EmergencySaveThrows_ContinuesToLogAndDialog()
    {
        CrashHandlingService.ResetForTests();

        var folder = CreateTempFolder();
        string? receivedLogPath = null;
        var dialogShown = false;

        try
        {
            var act = () =>
                CrashHandlingService.HandleCrash(
                    CreateNestedException(),
                    "1.2.3",
                    () => throw new InvalidOperationException("緊急保存の二次例外"),
                    () => { },
                    logPath =>
                    {
                        dialogShown = true;
                        receivedLogPath = logPath;
                    },
                    folder
                );

            act.Should().NotThrow();
            dialogShown.Should().BeTrue();
            receivedLogPath.Should().NotBeNull();
        }
        finally
        {
            CrashHandlingService.ResetForTests();
            Directory.Delete(folder, recursive: true);
        }
    }

    /// <summary>ダイアログ表示が例外を投げても呼び出し元へ伝播しないことを検証する（終了処理を止めない）</summary>
    [Fact(DisplayName = "HandleCrash: ダイアログ表示の失敗は呼び出し元へ伝播しない")]
    public void HandleCrash_ShowDialogThrows_DoesNotPropagate()
    {
        CrashHandlingService.ResetForTests();

        var folder = CreateTempFolder();

        try
        {
            var act = () =>
                CrashHandlingService.HandleCrash(
                    CreateNestedException(),
                    "1.2.3",
                    () => { },
                    () => { },
                    _ => throw new InvalidOperationException("ダイアログの二次例外"),
                    folder
                );

            act.Should().NotThrow();
        }
        finally
        {
            CrashHandlingService.ResetForTests();
            Directory.Delete(folder, recursive: true);
        }
    }

    /// <summary>2 回目以降の呼び出しが再入ガードで何もしないことを検証する</summary>
    [Fact(DisplayName = "HandleCrash: 2 回目の呼び出しは再入ガードで何もしない")]
    public void HandleCrash_SecondCall_IsIgnored()
    {
        CrashHandlingService.ResetForTests();

        var folder = CreateTempFolder();
        var saveCount = 0;
        var dialogCount = 0;

        try
        {
            for (var i = 0; i < 2; i++)
            {
                CrashHandlingService.HandleCrash(
                    CreateNestedException(),
                    "1.2.3",
                    () => saveCount++,
                    () => { },
                    _ => dialogCount++,
                    folder
                );
            }

            saveCount.Should().Be(1);
            dialogCount.Should().Be(1);
            Directory.GetFiles(folder, "crash-*.log").Should().ContainSingle();
        }
        finally
        {
            CrashHandlingService.ResetForTests();
            Directory.Delete(folder, recursive: true);
        }
    }

    /// <summary>機能モジュールの後始末が例外を投げても、報告ダイアログまで届くことを検証する</summary>
    [Fact(DisplayName = "HandleCrash: 機能モジュールの後始末が失敗してもダイアログは表示される")]
    public void HandleCrash_CloseFeatureModulesThrows_StillShowsDialog()
    {
        CrashHandlingService.ResetForTests();

        var folder = CreateTempFolder();
        var dialogShown = false;

        try
        {
            var handled = CrashHandlingService.HandleCrash(
                CreateNestedException(),
                "1.2.3",
                () => { },
                () => throw new InvalidOperationException("後始末の二次例外"),
                _ => dialogShown = true,
                folder
            );

            handled.Should().BeTrue();
            dialogShown.Should().BeTrue();
        }
        finally
        {
            CrashHandlingService.ResetForTests();
            Directory.Delete(folder, recursive: true);
        }
    }

    /// <summary>
    /// 実際の後始末の経路（1 回だけ実行のガード → モジュールごとの受け止め）で、投げるモジュールと
    /// 破棄がハングするモジュールが混ざっていても、上限で戻って残りのモジュールとダイアログまで届くことを検証する
    /// </summary>
    [Fact(
        DisplayName = "HandleCrash: 投げる・ハングする後始末が混ざっても上限で戻りダイアログまで届く"
    )]
    public void HandleCrash_WithFailingAndHangingModules_ReachesDialogWithinBound()
    {
        CrashHandlingService.ResetForTests();

        var folder = CreateTempFolder();
        var log = new List<string>();
        var reporter = new RecordingShutdownFailureReporter();
        var hangingEngine = new HangingAsyncDisposable();
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
                () => EngineShutdown.DisposeAll(TimeSpan.FromMilliseconds(300), hangingEngine)
            ),
            new ClosingModule("last", log, () => { }),
        };
        var shutdown = new FeatureModuleShutdownOnce(
            modules,
            new ServiceCollection().BuildServiceProvider(),
            reporter
        );
        var dialogShown = false;

        try
        {
            var stopwatch = Stopwatch.StartNew();

            CrashHandlingService.HandleCrash(
                CreateNestedException(),
                "1.2.3",
                () => { },
                shutdown.CloseAll,
                _ => dialogShown = true,
                folder
            );

            stopwatch.Stop();
            log.Should().Equal("throws", "hangs", "last");
            reporter.Steps.Should().Equal("FeatureModule.throws.OnMainWindowClosing");
            dialogShown.Should().BeTrue("後始末の失敗・ハングで報告を止めない");
            stopwatch
                .Elapsed.Should()
                .BeLessThan(TimeSpan.FromSeconds(5), "ハングした破棄は上限で戻る");
        }
        finally
        {
            hangingEngine.Release();
            CrashHandlingService.ResetForTests();
            Directory.Delete(folder, recursive: true);
        }
    }

    /// <summary>報告ダイアログの表示中に起きた 2 回目の例外は終了させず、最初の呼び出しがダイアログの後に 1 回だけ終了させることを検証する</summary>
    [Fact(
        DisplayName = "HandleUiCrash: ダイアログ表示中の再入は終了させず、ダイアログの後に 1 回だけ終了する"
    )]
    public void HandleUiCrash_ReentryDuringDialog_DoesNotExitUntilDialogCloses()
    {
        CrashHandlingService.ResetForTests();

        var folder = CreateTempFolder();
        var events = new List<string>();

        try
        {
            CrashHandlingService.HandleUiCrash(
                CreateNestedException(),
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
                        folder
                    );

                    events.Add("dialog-closed");
                },
                () => events.Add("exit"),
                folder
            );

            events.Should().Equal("save", "close", "dialog-open", "dialog-closed", "exit");
        }
        finally
        {
            CrashHandlingService.ResetForTests();
            Directory.Delete(folder, recursive: true);
        }
    }

    /// <summary>取りこぼした Task の例外（AggregateException）の内側を、2 件目以降も含めて全件出すことを検証する</summary>
    [Fact(DisplayName = "FormatDetails: AggregateException の内側を全件出す")]
    public void FormatDetails_AggregateException_ListsEveryInnerException()
    {
        var aggregate = new AggregateException(
            new InvalidOperationException("1 件目の失敗"),
            new AggregateException(
                new TimeoutException("入れ子の 1 件目"),
                new IOException("入れ子の 2 件目", new FileNotFoundException("最内段"))
            )
        );

        var details = CrashHandlingService.FormatDetails(aggregate, "1.2.3");

        details.Should().Contain("1 件目の失敗");
        details.Should().Contain("入れ子の 1 件目");
        details.Should().Contain("入れ子の 2 件目");
        details.Should().Contain("最内段");
        details.Should().Contain("[Inner exception (1) [2/2]]");
        details.Should().Contain("[Inner exception (2) [2/2]]");
    }

    /// <summary>同じ時刻の名前を選ぶ同時の書き手が、互いの記録を上書きしないことを検証する</summary>
    /// <remarks>
    /// 「存在確認 → 上書き書き込み」では、確認と書き込みの間に同じ名前を選んだ別の書き手と後勝ちになる。
    /// 時刻を固定して同じ名前を選ばせ、全員の記録が別々のファイルとして残ることを確かめる。
    /// </remarks>
    [Fact(DisplayName = "WriteCrashLog: 同じ名前を選ぶ同時の書き手でも互いを上書きしない")]
    public void WriteCrashLog_ConcurrentWritersWithSameTimestamp_DoNotOverwrite()
    {
        var folder = CreateTempFolder();
        const int writers = 64;

        try
        {
            using var start = new Barrier(writers);
            var paths = new string?[writers];

            // スレッドプールの増加を待たずに全員を同時に走らせるため、専用スレッドを使う
            var threads = Enumerable
                .Range(0, writers)
                .Select(i => new Thread(() =>
                {
                    start.SignalAndWait();
                    paths[i] = CrashHandlingService.WriteCrashLog(
                        new InvalidOperationException($"writer-{i:D2}"),
                        "1.2.3",
                        folder,
                        CrashHandlingService.CrashLogPrefix,
                        "20261003-120000-000"
                    );
                }))
                .ToList();

            threads.ForEach(thread => thread.Start());
            threads.ForEach(thread => thread.Join());

            paths.Should().OnlyContain(path => path != null);
            paths.Distinct().Should().HaveCount(writers);
            Directory.GetFiles(folder, "crash-*.log").Should().HaveCount(writers);

            for (var i = 0; i < writers; i++)
            {
                File.ReadAllText(paths[i]!).Should().Contain($"writer-{i:D2}");
            }
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    /// <summary>App の UI 例外の受け皿が、再入を判断する HandleUiCrash と後始末の共有インスタンスを使うことをソース上で固定する</summary>
    /// <remarks>
    /// <c>App</c> は WPF の <c>Application</c> 派生でテストから実体化できないため、判断を切り出した
    /// <see cref="CrashHandlingService.HandleUiCrash"/> 側を直接検証する。その形が意味を持つのは <c>App</c> が
    /// 確かにそれを使っているときだけなので、無条件の <c>Environment.Exit</c> へ戻っていないことをここで見張る。
    /// </remarks>
    [Fact(DisplayName = "App の UI 例外の受け皿は HandleUiCrash と後始末の共有インスタンスを使う")]
    public void App_UsesHandleUiCrashWithSharedModuleShutdown()
    {
        var source = File.ReadAllText(
            Path.Combine(NeutralResxFiles.FindRepositoryRoot(), "src", "QuickER.Gui", "App.xaml.cs")
        );

        source.Should().Contain("CrashHandlingService.HandleUiCrash(");
        source.Should().Contain("_featureModuleShutdown?.CloseAll()");
        source.Should().NotContain("CrashHandlingService.HandleCrash(");
        Regex
            .Matches(source, @"^\s*Environment\.Exit\(", RegexOptions.Multiline)
            .Should()
            .BeEmpty("終了は HandleUiCrash が処理を担ったときだけ行う");
    }

    /// <summary>後始末の処理を差し込める検証用モジュール</summary>
    private sealed class ClosingModule(string id, List<string> log, Action onClosing)
        : IFeatureModule
    {
        public string Id { get; } = id;

        public void ConfigureServices(IServiceCollection services) { }

        public IReadOnlyList<FeatureToolbarItem> CreateToolbarItems(IServiceProvider services) =>
            [];

        public void OnMainWindowClosing(IServiceProvider services)
        {
            log.Add(Id);
            onClosing();
        }
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

    /// <summary>バージョン解決が空でない文字列を返すことを検証する（属性欠落時も既定表記へ落ちる）</summary>
    [Fact(DisplayName = "ResolveAppVersion: 空でないバージョン文字列を返す")]
    public void ResolveAppVersion_ReturnsNonEmpty()
    {
        CrashHandlingService.ResolveAppVersion().Should().NotBeNullOrWhiteSpace();
    }

    /// <summary>テスト隔離用の一時フォルダを作成する</summary>
    private static string CreateTempFolder()
    {
        var folder = Path.Combine(Path.GetTempPath(), $"quicker-crash-{Guid.NewGuid():N}");
        Directory.CreateDirectory(folder);
        return folder;
    }
}
