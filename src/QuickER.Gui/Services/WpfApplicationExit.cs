using System.Windows;

namespace QuickER.Services;

/// <summary>
/// WPF アプリの通常の終了経路への入口（終了の要求と、終了の最後に行う処理の登録先）。
/// </summary>
/// <remarks>
/// 登録先 <see cref="Pending"/> はプロセスに 1 つで、<c>App.OnExit</c>（正常終了でだけ呼ばれる）が実行する。
/// 言語切替の再起動（<see cref="WpfApplicationRestartService"/>）と更新の適用（<see cref="UpdateService"/>）が
/// 同じ登録先を使うことで、両方が要求されたときの扱いを <see cref="PostExitActions.Run"/> の 1 か所で決める。
/// </remarks>
public static class WpfApplicationExit
{
    /// <summary>正常終了の最後に行う処理の登録先</summary>
    public static PostExitActions Pending { get; } = new();

    /// <summary>メインウィンドウを閉じて通常の終了経路（<c>Closing</c> の自動保存・終了連鎖）を通す</summary>
    /// <remarks>
    /// 終了条件は OnMainWindowClose（App.xaml）なので、メインウィンドウが閉じればアプリが終わる。
    /// メインウィンドウが無い（起動途中）ときだけ直接終了させる。
    /// </remarks>
    public static void RequestClose()
    {
        var app = Application.Current;

        if (app is null)
        {
            return;
        }

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
