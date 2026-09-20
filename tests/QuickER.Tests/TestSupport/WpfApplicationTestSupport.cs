using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using AwesomeAssertions;
using QuickER.Converters;
using QuickER.Services;
using QuickER.ViewModels;

namespace QuickER.Tests.TestSupport;

/// <summary>WPF 依存テストの共有ヘルパー（Application・App レベルリソースの供給と STA 実行）</summary>
/// <remarks>
/// xunit はテストクラスを並列実行するため、複数の STA テストが同時に
/// 「<see cref="Application.Current"/> が null なら生成」を行うと、二重生成の競合で
/// InvalidOperationException（同一 AppDomain では複数 Application を生成不可）が
/// 散発的に発生する。生成と初期リソース登録をロックで直列化して競合を防ぐ。
/// </remarks>
internal static class WpfApplicationTestSupport
{
    /// <summary>WPF 依存の検証を STA スレッド上で実行し、例外があれば失敗として報告する</summary>
    public static void RunSta(Action action)
    {
        Exception? captured = null;

        var thread = new Thread(() =>
        {
            try
            {
                action();
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

        captured.Should().BeNull(captured?.ToString());
    }

    /// <summary>Application 生成・リソース登録を直列化するためのロック</summary>
    private static readonly object Gate = new();

    /// <summary>XAML（BAML）ロードを直列化するためのロック</summary>
    private static readonly object XamlLoadGate = new();

    /// <summary>
    /// ダイアログ等の XAML（BAML）ロードを伴う生成を直列化して実行する。
    /// <see cref="Application.LoadComponent(object, Uri)"/> が使う System.IO.Packaging は
    /// スレッドセーフでないため、複数の STA テストクラスが同一アセンブリのダイアログを
    /// 並列に生成すると PackagePart の内部リスト操作が競合して散発的に失敗する。
    /// </summary>
    /// <param name="factory">ダイアログ等を生成するファクトリ（InitializeComponent を含む）</param>
    public static T LoadXamlComponent<T>(Func<T> factory)
    {
        lock (XamlLoadGate)
        {
            return factory();
        }
    }

    /// <summary>Application を必要なら生成し、BAML が参照する App レベルリソースを登録する</summary>
    public static void EnsureApplicationResources()
    {
        lock (Gate)
        {
            if (Application.Current is null)
            {
                _ = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            }

            var resources = Application.Current!.Resources;

            if (!resources.Contains("BoolToVisibilityConverter"))
            {
                resources.Add("BoolToVisibilityConverter", new BooleanToVisibilityConverter());
            }

            if (!resources.Contains("NullToVisibilityConverter"))
            {
                resources.Add("NullToVisibilityConverter", new NullToVisibilityConverter());
            }

            if (!resources.Contains("NullToBooleanConverter"))
            {
                resources.Add("NullToBooleanConverter", new NullToBooleanConverter());
            }

            if (!resources.Contains("CountToVisibilityConverter"))
            {
                resources.Add("CountToVisibilityConverter", new CountToVisibilityConverter());
            }
        }
    }

    /// <summary>
    /// 永続化を一時フォルダへ隔離したうえで、画面外に実 <c>MainWindow</c> を表示して検証を実行する。
    /// </summary>
    /// <param name="assert">表示済みウィンドウに対する検証</param>
    /// <param name="seed">ウィンドウ生成前に設定ファイルを用意する処理（不要なら null）</param>
    /// <remarks>
    /// <para>
    /// 束縛・配線・フォーカスの検証は、束縛先を間違えても WPF が無言で何もしないため
    /// ヘッドレスな VM テストでは守れず、実ウィンドウの <c>Show</c> を要する。
    /// </para>
    /// <para>
    /// <c>MainWindow</c> の ctor は実 <c>%LOCALAPPDATA%</c> の作業状態を復元し、<c>Close</c> の
    /// 自動保存が書き戻すため、永続化先を一時フォルダへ隔離して実ユーザーデータの読み書きを断つ。
    /// </para>
    /// <para>
    /// 画面外へ置くのは開発者のデスクトップを妨げないためだが、<b>全画面表示を扱うテストだけは
    /// 実際に主モニタへ最大化される</b>（最大化する以上は避けられない）。<c>ShowActivated=false</c> の
    /// ままなのでフォアグラウンドは奪わず、直後に解除される。
    /// </para>
    /// </remarks>
    public static void RunInIsolatedWindow(
        Action<MainViewModel, MainWindow> assert,
        Action<string>? seed = null
    )
    {
        Exception? captured = null;

        var folder = Path.Combine(
            Path.GetTempPath(),
            "quicker-window-" + Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(folder);

        try
        {
            seed?.Invoke(folder);

            var thread = new Thread(() =>
            {
                try
                {
                    EnsureApplicationResources();

                    var vm = new MainViewModel();
                    vm.UsePersistenceForTests(
                        new GuiAppSettingsStore(folder),
                        Path.Combine(folder, "last_diagram.json")
                    );
                    var window = CreateMainWindow(vm);

                    // 画面外・非アクティブで表示する（開発者のデスクトップを妨げない）
                    window.WindowStartupLocation = WindowStartupLocation.Manual;
                    window.Left = -4000;
                    window.Top = -4000;
                    window.ShowActivated = false;

                    window.Show();
                    window.UpdateLayout();
                    DoEvents();

                    try
                    {
                        assert(vm, window);
                    }
                    finally
                    {
                        window.Close();
                        DoEvents();
                    }
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
        }
        finally
        {
            try
            {
                Directory.Delete(folder, recursive: true);
            }
            catch
            {
                // 後始末の失敗はテスト結果に影響させない
            }
        }

        captured.Should().BeNull(captured?.ToString());
    }

    /// <summary>
    /// <c>MainWindow</c> を XAML（BAML）ロードの直列化つきで生成する。テストから
    /// <c>new MainWindow(...)</c> を直に書かず、必ずこの窓口を通すこと。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>MainWindow</c> の <c>InitializeComponent</c> は BAML を読む。WPF の XAML 解析は
    /// 内部の <see cref="System.Collections.Concurrent.ConcurrentDictionary{TKey, TValue}"/> を
    /// 破壊的に触る経路があり、複数の STA テストクラスが同時に読むと
    /// <c>XamlParseException: The given key '…' was not present in the dictionary.</c>
    /// （内側は <see cref="KeyNotFoundException"/>）で散発的に落ちる。
    /// </para>
    /// <para>
    /// 全件実行では再現しないが、CLAUDE.md が勧める単一テストクラスの <c>--filter</c> 実行では
    /// 実際に 6 回中 3〜4 回落ちた（2026-09-20 実測）。ダイアログ側は
    /// <see cref="LoadXamlComponent{T}(Func{T})"/> で既に直列化されていたので、
    /// <c>MainWindow</c> も同じゲートへ通して塞ぐ。
    /// </para>
    /// </remarks>
    public static MainWindow CreateMainWindow(MainViewModel viewModel) =>
        LoadXamlComponent(() => new MainWindow(viewModel));

    /// <summary>保留中のディスパッチャ処理（束縛・レイアウト）を流し切る</summary>
    public static void DoEvents()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(
            DispatcherPriority.ApplicationIdle,
            new Action(() => frame.Continue = false)
        );
        Dispatcher.PushFrame(frame);
    }
}
