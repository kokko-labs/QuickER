using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using AwesomeAssertions;
using QuickER.AI.Mock;
using QuickER.Tests.TestSupport;

namespace QuickER.Tests.AI.Mock;

/// <summary>
/// モックのプレビュー（<see cref="MockPreviewPanel"/>）を実際の WebView2 ランタイムで動かして確かめる
/// </summary>
/// <remarks>
/// ランタイムが無い環境では、黙ってスキップせずに失敗させる（CI の windows-latest にはランタイムが入っている。
/// スキップに化けると、プレビューの遮断が効いているかを誰も確かめない状態になる）。
/// </remarks>
public sealed class MockPreviewPanelRuntimeTests : IDisposable
{
    private readonly string _folder = Path.Combine(
        Path.GetTempPath(),
        "QuickERTests",
        "preview-runtime-" + Guid.NewGuid().ToString("N")
    );

    public MockPreviewPanelRuntimeTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_folder, recursive: true);
        }
        catch (IOException)
        {
            // WebView2 のプロセスが少し遅れて手放すことがある（後始末の失敗は無視する）
        }
        catch (UnauthorizedAccessException)
        {
            // 同上
        }
    }

    /// <summary>
    /// 既定の置き場所（実行ファイルの隣）は、インストール先が更新のたびに入れ替わる・書き込めない場所になる。
    /// プレビューは指定した置き場所（既定は %LOCALAPPDATA%\QuickER\WebView2）へユーザーデータを作る
    /// </summary>
    [Fact(DisplayName = "プレビューは、指定したユーザーデータの置き場所で WebView2 を初期化する")]
    public void Preview_UsesConfiguredUserDataFolder()
    {
        var userData = Path.Combine(_folder, "userdata");
        var available = false;

        WpfApplicationTestSupport.RunSta(() =>
        {
            WpfApplicationTestSupport.EnsureApplicationResources();
            var panel = WpfApplicationTestSupport.LoadXamlComponent(() => new MockPreviewPanel());
            panel.UserDataFolder = userData;
            var window = ShowOffscreen(panel);

            var init = panel.EnsureWebViewAsync();
            Pump(() => init.IsCompleted, TimeSpan.FromSeconds(60));
            available = init.IsCompletedSuccessfully && panel.IsPreviewAvailable;

            window.Close();
            WpfApplicationTestSupport.DoEvents();
        });

        available
            .Should()
            .BeTrue("WebView2 のランタイムが要る（無い環境ではスキップせずに失敗させる）");
        Directory
            .EnumerateFileSystemEntries(userData)
            .Should()
            .NotBeEmpty("ユーザーデータは指定した置き場所に作られる");
        MockPreviewPanel
            .DefaultUserDataFolder.Should()
            .EndWith(Path.Combine("QuickER", "WebView2"));
    }

    /// <summary>画面の外にウィンドウを出してパネルを載せる（WebView2 はウィンドウに載っていないと初期化できない）</summary>
    private static Window ShowOffscreen(FrameworkElement content)
    {
        var window = new Window
        {
            Content = content,
            Width = 400,
            Height = 300,
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = -4000,
            Top = -4000,
            ShowActivated = false,
            ShowInTaskbar = false,
        };
        window.Show();
        return window;
    }

    /// <summary>ディスパッチャを回しながら条件を待つ</summary>
    private static void Pump(Func<bool> done, TimeSpan timeout)
    {
        var watch = Stopwatch.StartNew();

        while (!done() && watch.Elapsed < timeout)
        {
            var frame = new DispatcherFrame();
            var timer = new DispatcherTimer(
                TimeSpan.FromMilliseconds(50),
                DispatcherPriority.Background,
                (sender, _) =>
                {
                    ((DispatcherTimer)sender!).Stop();
                    frame.Continue = false;
                },
                Dispatcher.CurrentDispatcher
            );
            timer.Start();
            Dispatcher.PushFrame(frame);
        }
    }
}
