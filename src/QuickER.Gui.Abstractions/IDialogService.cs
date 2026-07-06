namespace QuickER.Gui.Abstractions;

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
