using System.Diagnostics;
using QuickER.Gui.Abstractions;

namespace QuickER.Services;

/// <summary>
/// WPF アプリを再起動する <see cref="IApplicationRestartService"/> の実装。
/// </summary>
/// <remarks>
/// メインウィンドウを閉じて終了処理（<c>Closing</c> の自動保存・子ダイアログ終了）を通し、
/// 終了条件（OnMainWindowClose）でアプリを終了させる。新しいインスタンスの起動は正常終了の最後
/// （<see cref="WpfApplicationExit.Pending"/>＝<c>App.OnExit</c>）で行うため、終了がキャンセルされても
/// 二重起動しない。同じ終了で更新の適用も要求されていれば、再起動は更新プログラムに任せる
/// （扱いの正本は <see cref="PostExitActions"/>）。
/// </remarks>
public sealed class WpfApplicationRestartService : IApplicationRestartService
{
    /// <inheritdoc />
    public void Restart()
    {
        // 現行プロセスが完全に終了（自動保存・DI 破棄）した後に、新しいインスタンスを起動するよう求める
        // （起動処理は Program が起動時に登録した StartNewInstance）
        WpfApplicationExit.Pending.RequestRelaunch();

        // メインウィンドウを閉じて Closing（自動保存・子ダイアログ終了）を確実に通す
        WpfApplicationExit.RequestClose();
    }

    /// <summary>新しいインスタンスを起動する（再起動の起動処理として Program が 1 回だけ登録する）</summary>
    /// <remarks>
    /// 現在の実行ファイル（配布時は QuickER.exe のアプリホスト）のパスを起動する。取得できなければ何もしない。
    /// 言語切替の再起動と、更新の適用に失敗したときの代わりの再起動が、この 1 つを通る
    /// </remarks>
    public static void StartNewInstance()
    {
        var exePath = Environment.ProcessPath;

        if (string.IsNullOrEmpty(exePath))
        {
            return;
        }

        Process.Start(new ProcessStartInfo(exePath) { UseShellExecute = true });
    }
}
