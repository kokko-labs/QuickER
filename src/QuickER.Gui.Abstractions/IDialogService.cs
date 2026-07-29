namespace QuickER.Gui.Abstractions;

/// <summary>ViewModel からの確認・通知ダイアログ表示を抽象化するインターフェース</summary>
/// <remarks>
/// 単体テストではスタブへ差し替え、UI を表示せずユーザー応答の分岐を検証する。
/// アイコンの使い分けは「深刻度」ではなく「メッセージ種別」で選ぶ（Windows UX ガイドラインの原則）：
/// Error＝すでに発生した失敗の報告／Warning＝取り返しのつかない結果を伴う続行確認／
/// Question＝ルーチンの確認／Information＝完了・案内。
/// Warning をエラー報告の「和らげ」に使わないこと。
/// </remarks>
public interface IDialogService
{
    /// <summary>ルーチンの確認向けに OK / キャンセルの確認ダイアログを表示する（Question アイコン）</summary>
    /// <remarks>取り返しのつかない結果を伴う続行確認は <see cref="ConfirmWarning"/> を使う</remarks>
    /// <returns>OK が選択された場合 true</returns>
    bool Confirm(string message, string title);

    /// <summary>取り返しのつかない結果を伴う続行確認向けに、警告アイコン付きの OK / キャンセル確認ダイアログを表示する（Warning アイコン）</summary>
    /// <remarks>実 DB の変更・未保存変更の破棄・データ喪失の可能性など、続行すると元に戻せない操作の確認に限って使う</remarks>
    /// <returns>OK が選択された場合 true</returns>
    bool ConfirmWarning(string message, string title);

    /// <summary>完了・案内の情報メッセージを表示する（Information アイコン）</summary>
    void ShowInformation(string message, string title);

    /// <summary>すでに発生した失敗を報告するエラーメッセージを表示する（Error アイコン）</summary>
    void ShowError(string message, string title);

    /// <summary>先頭メッセージと複数行の詳細（一覧）を、広い読み取り専用領域を持つ情報ダイアログで表示する（完了・案内向け）</summary>
    /// <param name="message">上部に表示する要約メッセージ</param>
    /// <param name="details">読み取り専用領域に表示する複数行の詳細（一覧本文）</param>
    /// <param name="title">ダイアログのタイトル</param>
    void ShowInformationDetails(string message, string details, string title);

    /// <summary>先頭メッセージと複数行の詳細（一覧）を、エラー表示の詳細ダイアログで表示する（すでに発生した失敗の報告向け）</summary>
    /// <param name="message">上部に表示する要約メッセージ</param>
    /// <param name="details">読み取り専用領域に表示する複数行の詳細（一覧本文）</param>
    /// <param name="title">ダイアログのタイトル</param>
    void ShowErrorDetails(string message, string details, string title);
}
