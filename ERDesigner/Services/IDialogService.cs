using System.Windows;

namespace ERDesigner.Services;

/// <summary>
/// ViewModel からの確認・通知ダイアログ表示を抽象化します。
/// 単体テストではスタブに差し替えることで、UI を表示せずにユーザー応答の分岐を検証できます。
/// </summary>
public interface IDialogService
{
    /// <summary>OK / キャンセルの確認ダイアログを表示します。</summary>
    /// <returns>OK が選択された場合 true。</returns>
    bool Confirm(string message, string title);

    /// <summary>破壊的操作など注意が必要な操作向けに、警告アイコン付きの OK / キャンセル確認ダイアログを表示します。</summary>
    /// <returns>OK が選択された場合 true。</returns>
    bool ConfirmWarning(string message, string title);

    /// <summary>情報メッセージを表示します。</summary>
    void ShowInformation(string message, string title);

    /// <summary>エラーメッセージを表示します。</summary>
    void ShowError(string message, string title);
}

/// <summary><see cref="MessageBox"/> を用いた既定の実装です。</summary>
public sealed class MessageBoxDialogService : IDialogService
{
    public bool Confirm(string message, string title) =>
        MessageBox.Show(message, title, MessageBoxButton.OKCancel, MessageBoxImage.Question) == MessageBoxResult.OK;

    public bool ConfirmWarning(string message, string title) =>
        MessageBox.Show(message, title, MessageBoxButton.OKCancel, MessageBoxImage.Warning) == MessageBoxResult.OK;

    public void ShowInformation(string message, string title) => MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Information);

    public void ShowError(string message, string title) => MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Error);
}
