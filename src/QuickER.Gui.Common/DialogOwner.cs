using System.Linq;
using System.Windows;

namespace QuickER.Gui.Common;

/// <summary>モーダル（メッセージボックス・ファイル選択）の親にするウィンドウを解決する共有ヘルパー</summary>
/// <remarks>
/// <para>
/// モーダルは必ずオーナー付きで表示する。オーナーを与えないと、モードレスで開いた機能ウィンドウ
/// （AI モック生成など）の背面へ回り込んで見えなくなり、呼び出し元は応答待ちのまま止まる
/// （CLAUDE.md の不変条件「モーダルは必ずオーナー付きで出し、実行中フラグを落としてから出す」）。
/// </para>
/// <para>
/// 解決を 1 箇所へ集約するのは、<see cref="MessageBoxDialogService"/> と
/// <see cref="WpfFileDialogService"/> が別々に持つと片方だけ直し忘れて同じ症状が戻るため。
/// Z 順は実行時の挙動でテストからは守れないので、守れるのは「両者が同じ解決を通すこと」だけになる。
/// </para>
/// </remarks>
public static class DialogOwner
{
    /// <summary>モーダルの親にするウィンドウを返す（アクティブなウィンドウ→メインウィンドウの順・無ければ <c>null</c>）</summary>
    /// <remarks>
    /// 表示済み（<see cref="FrameworkElement.IsLoaded"/>）のウィンドウだけを返す。未表示のウィンドウを
    /// オーナーにすると WPF が例外を投げるため。アプリが非アクティブでアクティブなウィンドウが無い場合は
    /// メインウィンドウへ倒れるので、モードレスの機能ウィンドウより背面に出ることはあり得る。
    /// </remarks>
    public static Window? Resolve()
    {
        var application = Application.Current;

        if (application is null)
        {
            return null;
        }

        var active = application
            .Windows.OfType<Window>()
            .FirstOrDefault(window => window.IsActive && window.IsLoaded);

        if (active is not null)
        {
            return active;
        }

        return application.MainWindow is { IsLoaded: true } main ? main : null;
    }
}
