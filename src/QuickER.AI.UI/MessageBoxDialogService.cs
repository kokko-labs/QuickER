using System.Windows;
using QuickER.Gui.Abstractions;

namespace QuickER.AI.UI;

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
}
