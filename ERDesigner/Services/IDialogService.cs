using System.Windows;

namespace ERDesigner.Services;

/// <summary>ViewModel からの確認・通知ダイアログ表示を抽象化するインターフェース</summary>
/// <remarks>単体テストではスタブへ差し替え、UI を表示せずユーザー応答の分岐を検証する</remarks>
public interface IDialogService
{
    /// <summary>OK / キャンセルの確認ダイアログを表示する</summary>
    /// <returns>OK が選択された場合 true</returns>
    bool Confirm(string message, string title);

    /// <summary>破壊的操作向けに警告アイコン付きの OK / キャンセル確認ダイアログを表示する</summary>
    /// <returns>OK が選択された場合 true</returns>
    bool ConfirmWarning(string message, string title);

    /// <summary>情報メッセージを表示する</summary>
    void ShowInformation(string message, string title);

    /// <summary>エラーメッセージを表示する</summary>
    void ShowError(string message, string title);
}

/// <summary><see cref="MessageBox"/> を用いた既定の実装</summary>
public sealed class MessageBoxDialogService : IDialogService
{
    /// <inheritdoc />
    public bool Confirm(string message, string title) =>
        MessageBox.Show(message, title, MessageBoxButton.OKCancel, MessageBoxImage.Question) == MessageBoxResult.OK;

    /// <inheritdoc />
    public bool ConfirmWarning(string message, string title) =>
        MessageBox.Show(message, title, MessageBoxButton.OKCancel, MessageBoxImage.Warning) == MessageBoxResult.OK;

    /// <inheritdoc />
    public void ShowInformation(string message, string title) => MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Information);

    /// <inheritdoc />
    public void ShowError(string message, string title) => MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Error);
}
