using System.Windows;
using QuickER.Gui.Abstractions;

namespace QuickER.Gui.Common;

/// <summary><see cref="MessageBox"/> を用いた <see cref="IDialogService"/> の既定の実装</summary>
public sealed class MessageBoxDialogService : IDialogService
{
    /// <inheritdoc />
    public bool Confirm(string message, string title) =>
        MessageBox.Show(message, title, MessageBoxButton.OKCancel, MessageBoxImage.Question)
        == MessageBoxResult.OK;

    /// <inheritdoc />
    public bool ConfirmWarning(string message, string title) =>
        MessageBox.Show(message, title, MessageBoxButton.OKCancel, MessageBoxImage.Warning)
        == MessageBoxResult.OK;

    /// <inheritdoc />
    public void ShowInformation(string message, string title) =>
        MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Information);

    /// <inheritdoc />
    public void ShowError(string message, string title) =>
        MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Error);

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
            Owner = Application.Current?.MainWindow,
        }.ShowDialog();
}
