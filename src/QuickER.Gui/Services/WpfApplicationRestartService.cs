using System.Diagnostics;
using System.Windows;
using QuickER.Gui.Abstractions;

namespace QuickER.Services;

/// <summary>
/// WPF アプリを再起動する <see cref="IApplicationRestartService"/> の実装。
/// </summary>
/// <remarks>
/// メインウィンドウを閉じて終了処理（<c>Closing</c> の自動保存・子ダイアログ終了）を通し、
/// 既定の終了モード（OnLastWindowClose）でアプリを終了させる。新しいインスタンスの起動は
/// <see cref="Application.Exit"/>（正常終了時のみ発火）で行うため、終了がキャンセルされても二重起動しない。
/// </remarks>
public sealed class WpfApplicationRestartService : IApplicationRestartService
{
    /// <inheritdoc />
    public void Restart()
    {
        var app = Application.Current;

        if (app is null)
        {
            return;
        }

        // 現在の実行ファイル（配布時は QuickER.exe のアプリホスト）のパス。取得できなければ再起動はしない
        var exePath = Environment.ProcessPath;

        // 現行プロセスが完全に終了（自動保存・DI 破棄）した後に、新しいインスタンスを起動する。
        // Exit は正常終了時のみ発火するため、閉じるのがキャンセルされた場合は再起動されない（二重起動を防ぐ）。
        void OnExit(object? sender, ExitEventArgs e)
        {
            app.Exit -= OnExit;

            if (!string.IsNullOrEmpty(exePath))
            {
                Process.Start(new ProcessStartInfo(exePath) { UseShellExecute = true });
            }
        }

        app.Exit += OnExit;

        // メインウィンドウを閉じて Closing（自動保存・子ダイアログ終了）を確実に通す。
        // 最後のウィンドウが閉じれば既定の終了モードでアプリが終了する。
        if (app.MainWindow is not null)
        {
            app.MainWindow.Close();
        }
        else
        {
            app.Shutdown();
        }
    }
}
