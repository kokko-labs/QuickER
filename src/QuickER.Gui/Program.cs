using System;
using System.Windows;
using QuickER.Services;
using Velopack;

namespace QuickER
{
    /// <summary>
    /// アプリケーションのエントリポイント。Velopack のインストール/更新フックを
    /// WPF 初期化より前に処理してから <see cref="App"/> を起動する。
    /// </summary>
    /// <remarks>
    /// 既定の App.xaml 由来 <c>Main</c> は使わず（csproj で
    /// <c>EnableDefaultApplicationDefinition=false</c>）、ここを <c>StartupObject</c> に指定する。
    /// <see cref="VelopackApp.Build"/>().Run() はインストール直後・更新適用時の各フックを処理し、
    /// 通常起動時はそのまま制御を返す（Velopack 公式手順）。
    /// </remarks>
    internal static class Program
    {
        /// <summary>プロセスのエントリポイント（WPF アプリより前に Velopack を初期化する）</summary>
        [STAThread]
        private static void Main(string[] args)
        {
            // 非 UI スレッドや WPF 初期化前（Velopack フック・単一インスタンス制御の段）で落ちた場合、
            // プロセスは CLR により終了しダイアログは出せない。証跡としてクラッシュログだけ残す。
            AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            {
                if (e.ExceptionObject is Exception exception)
                {
                    CrashHandlingService.WriteCrashLog(
                        exception,
                        CrashHandlingService.ResolveAppVersion()
                    );
                }
            };

            // インストール/更新フックを WPF 初期化より前に処理する（Velopack 公式手順）。
            // 未インストール実行時は何もせずそのまま返る。
            VelopackApp.Build().Run();

            // 単一インスタンス制御。既に起動済みなら、そちらのウィンドウをアクティブ化して
            // 自分は WPF を初期化せず即終了する（二重起動しない）
            using var singleInstance = SingleInstanceGuard.TryAcquire();

            if (singleInstance is null)
            {
                return;
            }

            App app = new();
            app.InitializeComponent();

            // 後続インスタンスからのアクティブ化要求を受けたら、UI スレッドで前面へ出す
            singleInstance.ListenForActivation(() =>
                app.Dispatcher.BeginInvoke(() => ActivateMainWindow(app))
            );

            app.Run();
        }

        /// <summary>メインウィンドウを前面へ出す（最小化されていれば復元する）</summary>
        private static void ActivateMainWindow(App app)
        {
            var window = app.MainWindow;

            // 起動直後でウィンドウ生成前なら何もしない（次の要求で拾う）
            if (window is null)
            {
                return;
            }

            if (window.WindowState == WindowState.Minimized)
            {
                window.WindowState = WindowState.Normal;
            }

            window.Show();
            window.Activate();
        }
    }
}
