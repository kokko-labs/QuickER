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

    /// <summary>先頭メッセージと複数行の詳細（一覧）を、広い読み取り専用領域を持つ情報ダイアログで表示する</summary>
    /// <param name="message">上部に表示する要約メッセージ</param>
    /// <param name="details">読み取り専用領域に表示する複数行の詳細（一覧本文）</param>
    /// <param name="title">ダイアログのタイトル</param>
    void ShowInformationDetails(string message, string details, string title);

    /// <summary>先頭メッセージと複数行の詳細（一覧）を、エラー表示の詳細ダイアログで表示する</summary>
    /// <param name="message">上部に表示する要約メッセージ</param>
    /// <param name="details">読み取り専用領域に表示する複数行の詳細（一覧本文）</param>
    /// <param name="title">ダイアログのタイトル</param>
    void ShowErrorDetails(string message, string details, string title);
}
