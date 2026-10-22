using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
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

    /// <summary>
    /// AI の HTML のスクリプトが作った blob のダウンロード（<c>run.bat</c>）は、プレビューが取り消し、ファイルはどこにもできない
    /// </summary>
    [Fact(DisplayName = "プレビューは、ページのスクリプトが始めたダウンロードを取り消す")]
    public void Preview_CancelsScriptBlobDownload()
    {
        var mock = Path.Combine(_folder, "mock");
        var downloads = Path.Combine(_folder, "downloads");
        Directory.CreateDirectory(mock);
        Directory.CreateDirectory(downloads);
        File.WriteAllText(
            Path.Combine(mock, "A.html"),
            """
            <!DOCTYPE html><html><head><title>A</title></head><body><p>mock</p>
            <script>
            const b = new Blob(['@echo pwned'], { type: 'application/octet-stream' });
            const a = document.createElement('a');
            a.href = URL.createObjectURL(b);
            a.download = 'run.bat';
            document.body.appendChild(a);
            a.click();
            </script></body></html>
            """
        );
        var observed = new List<bool>();
        var available = false;

        WpfApplicationTestSupport.RunSta(() =>
        {
            var (panel, window) = ShowPanel(Path.Combine(_folder, "userdata"));
            available = panel.IsPreviewAvailable;

            if (available)
            {
                // パネルのハンドラの後に呼ばれる観測用。取り消されていなければ、実際の保存先をテストのフォルダへ向ける
                panel.WebView.CoreWebView2.DownloadStarting += (_, e) =>
                {
                    observed.Add(e.Cancel);
                    e.ResultFilePath = Path.Combine(downloads, "run.bat");
                };
                panel.Navigate(new Uri(Path.Combine(mock, "A.html")), mock);
                Pump(() => observed.Count > 0, TimeSpan.FromSeconds(30));
                Pump(() => false, TimeSpan.FromSeconds(2));
            }

            window.Close();
            WpfApplicationTestSupport.DoEvents();
        });

        available
            .Should()
            .BeTrue("WebView2 のランタイムが要る（無い環境ではスキップせずに失敗させる）");
        observed
            .Should()
            .NotBeEmpty("ダウンロードは始まりかけて DownloadStarting まで届く")
            .And.AllBeEquivalentTo(true);
        Directory.EnumerateFileSystemEntries(downloads).Should().BeEmpty();
    }

    /// <summary>
    /// 拒否の応答は初期化のときに 1 つ作って使い回す。使い回しても、許可しないサブリソース要求はどれもネットワークへ出ない
    /// （ローカルの待ち受けへの接続が 1 本も無い）
    /// </summary>
    [Fact(
        DisplayName = "使い回した拒否の応答で、許可しないサブリソース要求はどれもネットワークへ出ない"
    )]
    public void Preview_ReusedBlockedResponse_BlocksEveryRequest()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var connections = 0;
        _ = Task.Run(
            async () =>
            {
                try
                {
                    while (true)
                    {
                        using var client = await listener.AcceptTcpClientAsync(
                            TestContext.Current.CancellationToken
                        );
                        Interlocked.Increment(ref connections);
                    }
                }
                catch (Exception)
                {
                    // 待ち受けを止めたら終わる
                }
            },
            TestContext.Current.CancellationToken
        );

        var mock = Path.Combine(_folder, "mock");
        Directory.CreateDirectory(mock);
        var origin = $"http://127.0.0.1:{port}";
        File.WriteAllText(
            Path.Combine(mock, "A.html"),
            $$"""
            <!DOCTYPE html><html><head><title>A</title>
            <link rel="stylesheet" href="{{origin}}/s.css"></head><body>
            <img src="{{origin}}/a.png"><img src="{{origin}}/b.png"><img src="{{origin}}/c.png">
            <script>fetch('{{origin}}/d').catch(() => {}); fetch('{{origin}}/e').catch(() => {});</script>
            </body></html>
            """
        );
        var answered = new List<bool>();
        var available = false;
        var stillAvailable = false;

        try
        {
            WpfApplicationTestSupport.RunSta(() =>
            {
                var (panel, window) = ShowPanel(Path.Combine(_folder, "userdata"));
                available = panel.IsPreviewAvailable;

                if (available)
                {
                    // パネルのハンドラの後に呼ばれる観測用（拒否の応答が付いたかを見る）
                    panel.WebView.CoreWebView2.WebResourceRequested += (_, e) =>
                    {
                        if (e.Request.Uri.StartsWith(origin, StringComparison.Ordinal))
                        {
                            answered.Add(e.Response is not null);
                        }
                    };
                    panel.Navigate(new Uri(Path.Combine(mock, "A.html")), mock);
                    Pump(() => answered.Count >= 6, TimeSpan.FromSeconds(30));
                    Pump(() => false, TimeSpan.FromSeconds(2));
                    stillAvailable = panel.IsPreviewAvailable;
                }

                window.Close();
                WpfApplicationTestSupport.DoEvents();
            });
        }
        finally
        {
            listener.Stop();
        }

        available
            .Should()
            .BeTrue("WebView2 のランタイムが要る（無い環境ではスキップせずに失敗させる）");
        answered.Should().HaveCountGreaterThanOrEqualTo(6).And.AllBeEquivalentTo(true);
        connections.Should().Be(0, "拒否の応答を返した要求はネットワークへ出ない");
        stillAvailable
            .Should()
            .BeTrue("使い回しで応答を付けられない要求は無い（止める経路に入らない）");
    }

    [Fact(
        DisplayName = "拒否の応答を作れないときは、プレビューを使えない扱いにする（許可しない要求を止められないため）"
    )]
    public void Preview_BlockedResponseUnavailable_DisablesPreview()
    {
        var available = true;

        WpfApplicationTestSupport.RunSta(() =>
        {
            WpfApplicationTestSupport.EnsureApplicationResources();
            var panel = WpfApplicationTestSupport.LoadXamlComponent(() => new MockPreviewPanel());
            panel.UserDataFolder = Path.Combine(_folder, "userdata");
            panel.BlockedResponseFactory = _ => throw new InvalidOperationException("injected");
            var window = ShowOffscreen(panel);

            var init = panel.EnsureWebViewAsync();
            Pump(() => init.IsCompleted, TimeSpan.FromSeconds(60));
            available = panel.IsPreviewAvailable;

            window.Close();
            WpfApplicationTestSupport.DoEvents();
        });

        available.Should().BeFalse();
    }

    /// <summary>
    /// 初期化は通ったが、許可しないサブリソース要求に拒否の応答を付けられなかったときは、その場でプレビューを止めて
    /// 使えない扱いへ切り替え、以後の表示の要求も受け付けない（その要求を黙って通し続けない）
    /// </summary>
    /// <remarks>
    /// 差し替え口が null を返すと、初期化では例外にならず（初期化の時点で使えない扱いにする経路は通らない）、
    /// 要求の途中で応答を付けられない経路（<c>OnWebResourceRequested</c> の catch → <c>StopPreview</c>）へ入る。
    /// 要求先は閉じたばかりのループバックのポートで、要求が漏れても応答は得られない。
    /// </remarks>
    [Fact(
        DisplayName = "要求の途中で拒否の応答を付けられないときは、プレビューを止めて使えない表示へ切り替え、以後の表示を受け付けない"
    )]
    public void Preview_BlockedResponseMissingMidRequest_StopsPreview()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();

        var mock = Path.Combine(_folder, "mock");
        Directory.CreateDirectory(mock);
        var pageA = Path.Combine(mock, "A.html");
        File.WriteAllText(
            pageA,
            $"""
            <!DOCTYPE html><html><head><title>A</title></head><body>
            <img src="http://127.0.0.1:{port}/x.png"></body></html>
            """
        );
        File.WriteAllText(
            Path.Combine(mock, "B.html"),
            "<!DOCTYPE html><html><head><title>B</title></head><body>B</body></html>"
        );
        var initialized = false;
        var stopped = false;
        var webViewVisibility = Visibility.Visible;
        var placeholderVisibility = Visibility.Visible;
        var unavailableVisibility = Visibility.Collapsed;
        var webViewVisibilityAfterLaterNavigate = Visibility.Visible;
        Uri? sourceAfterLaterNavigate = null;
        var fileChangesAfterStop = new List<string>();

        WpfApplicationTestSupport.RunSta(() =>
        {
            WpfApplicationTestSupport.EnsureApplicationResources();
            var panel = WpfApplicationTestSupport.LoadXamlComponent(() => new MockPreviewPanel());
            panel.UserDataFolder = Path.Combine(_folder, "userdata");
            panel.BlockedResponseFactory = _ => null!;
            var window = ShowOffscreen(panel);

            var init = panel.EnsureWebViewAsync();
            Pump(() => init.IsCompleted, TimeSpan.FromSeconds(60));
            initialized = init.IsCompletedSuccessfully && panel.IsPreviewAvailable;

            if (initialized)
            {
                panel.Navigate(new Uri(pageA), mock);
                Pump(() => !panel.IsPreviewAvailable, TimeSpan.FromSeconds(30));
                stopped = !panel.IsPreviewAvailable;
                webViewVisibility = panel.WebView.Visibility;
                placeholderVisibility = panel.EmptyPlaceholder.Visibility;
                unavailableVisibility = panel.UnavailablePanel.Visibility;

                // 止めた後の表示の要求は無視される（遷移も現在ファイルの通知も起きない）
                panel.CurrentFileChanged += (_, name) => fileChangesAfterStop.Add(name);
                panel.Navigate(new Uri(Path.Combine(mock, "B.html")), mock);
                Pump(() => false, TimeSpan.FromSeconds(1));
                sourceAfterLaterNavigate = panel.CurrentSource;
                webViewVisibilityAfterLaterNavigate = panel.WebView.Visibility;
            }

            window.Close();
            WpfApplicationTestSupport.DoEvents();
        });

        initialized
            .Should()
            .BeTrue(
                "WebView2 のランタイムが要り、null の応答では初期化は失敗しない（ランタイムが無い環境ではスキップせずに失敗させる）"
            );
        stopped.Should().BeTrue("応答を付けられなかった要求でプレビューを止める");
        webViewVisibility.Should().Be(Visibility.Collapsed);
        placeholderVisibility.Should().Be(Visibility.Collapsed);
        unavailableVisibility.Should().Be(Visibility.Visible, "使えないことを案内する");
        sourceAfterLaterNavigate.Should().Be(new Uri(pageA), "止めた後の表示の要求は受け付けない");
        webViewVisibilityAfterLaterNavigate.Should().Be(Visibility.Collapsed);
        fileChangesAfterStop.Should().BeEmpty();
    }

    /// <summary>パネルを画面の外に出して初期化を待つ</summary>
    private static (MockPreviewPanel Panel, Window Window) ShowPanel(string userData)
    {
        WpfApplicationTestSupport.EnsureApplicationResources();
        var panel = WpfApplicationTestSupport.LoadXamlComponent(() => new MockPreviewPanel());
        panel.UserDataFolder = userData;
        var window = ShowOffscreen(panel);
        var init = panel.EnsureWebViewAsync();
        Pump(() => init.IsCompleted, TimeSpan.FromSeconds(60));
        return (panel, window);
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
