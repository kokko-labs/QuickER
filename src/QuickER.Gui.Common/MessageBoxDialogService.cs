using System.Linq;
using System.Windows;
using QuickER.Gui.Abstractions;

namespace QuickER.Gui.Common;

/// <summary><see cref="MessageBox"/> を用いた <see cref="IDialogService"/> の既定の実装</summary>
/// <remarks>
/// モーダルは必ずオーナー付きで表示する（<see cref="ResolveOwner"/>）。オーナーを与えないと、
/// モードレスで開いた機能ウィンドウ（AI モック生成など）の背面へ回り込んで見えなくなり、
/// 呼び出し元は応答待ちのまま止まる。
/// </remarks>
public sealed class MessageBoxDialogService : IDialogService
{
    /// <inheritdoc />
    public bool Confirm(string message, string title) =>
        Show(message, title, MessageBoxButton.OKCancel, MessageBoxImage.Question)
        == MessageBoxResult.OK;

    /// <inheritdoc />
    public bool ConfirmWarning(string message, string title) =>
        Show(message, title, MessageBoxButton.OKCancel, MessageBoxImage.Warning)
        == MessageBoxResult.OK;

    /// <inheritdoc />
    public bool ConfirmWarningDetails(string message, string details, string title)
    {
        var dialog = InformationDetailsDialog.CreateWarningConfirmation(message, details, title);
        dialog.Owner = ResolveOwner();
        return dialog.ShowDialog() == true;
    }

    /// <inheritdoc />
    public void ShowInformation(string message, string title) =>
        Show(message, title, MessageBoxButton.OK, MessageBoxImage.Information);

    /// <inheritdoc />
    public void ShowError(string message, string title) =>
        Show(message, title, MessageBoxButton.OK, MessageBoxImage.Error);

    /// <inheritdoc />
    public void ShowInformationDetails(string message, string details, string title) =>
        ShowDetails(message, details, title, isError: false);

    /// <inheritdoc />
    public void ShowErrorDetails(string message, string details, string title) =>
        ShowDetails(message, details, title, isError: true);

    /// <summary>要約＋詳細の情報ダイアログをモーダル表示する（情報／エラー共通の内部ヘルパー）</summary>
    private static void ShowDetails(string message, string details, string title, bool isError) =>
        new InformationDetailsDialog(message, details, title, isError)
        {
            Owner = ResolveOwner(),
        }.ShowDialog();

    /// <summary>オーナーを解決して <see cref="MessageBox"/> を表示する（解決できなければオーナーなし）</summary>
    private static MessageBoxResult Show(
        string message,
        string title,
        MessageBoxButton button,
        MessageBoxImage image
    )
    {
        var owner = ResolveOwner();

        return owner is null
            ? MessageBox.Show(message, title, button, image)
            : MessageBox.Show(owner, message, title, button, image);
    }

    /// <summary>モーダルの親にするウィンドウを解決する（アクティブなウィンドウ→メインウィンドウの順）</summary>
    /// <remarks>
    /// 表示済み（<see cref="FrameworkElement.IsLoaded"/>）のウィンドウだけを返す。未表示のウィンドウを
    /// オーナーにすると WPF が例外を投げるため。アプリが非アクティブでアクティブなウィンドウが無い場合は
    /// メインウィンドウへ倒れるので、モードレスの機能ウィンドウより背面に出ることはあり得る。
    /// </remarks>
    private static Window? ResolveOwner()
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
