namespace QuickER.Gui.Abstractions;

/// <summary>ViewModel からの確認・通知ダイアログ表示を抽象化するインターフェース</summary>
/// <remarks>
/// <para>
/// 単体テストではスタブへ差し替え、UI を表示せずユーザー応答の分岐を検証する。
/// アイコンの使い分けは「深刻度」ではなく「メッセージ種別」で選ぶ（Windows UX ガイドラインの原則）：
/// Error＝すでに発生した失敗の報告／Warning＝取り返しのつかない結果を伴う続行確認／
/// Question＝ルーチンの確認／Information＝完了・案内。
/// Warning をエラー報告の「和らげ」に使わないこと。
/// </para>
/// <para>
/// 完了通知の<b>提示先</b>は「外部とのやり取りか、ER 図ファイル自身の読み書きか」で選ぶ：
/// 外部形式の入出力（エクスポート／インポート）・DB 取込・C# コード取込は<b>このダイアログ（モーダル）</b>、
/// ER 図の保存・開くは<b>メインウィンドウのステータスバー</b>の一時通知（<c>MainViewModel.NotifyStatus</c>）。
/// 失敗の報告は提示先に依らず常にモーダル。
/// </para>
/// <para>
/// 完了に添える<b>内訳・警告</b>がある場合は、提示先に依らず
/// <see cref="ShowInformationDetails"/>（モーダルの詳細ダイアログ）で見せる。
/// 単文の完了に大型ダイアログは出さない（内訳が無ければ <see cref="ShowInformation"/>）。
/// </para>
/// <para>
/// <b>確認</b>に添える内訳（判断材料の一覧）がある場合は <see cref="ConfirmWarningDetails"/>
/// （スクロール可能な詳細領域を持つ確認ダイアログ）で見せる＝一覧は畳まず全件を渡してよい。
/// 標準 MessageBox はスクロールしないため、件数上限のある一覧（<c>DialogItemList.Format</c>）を
/// <see cref="ConfirmWarning"/> の本文へ載せる形は避ける（二節構成では上限があっても画面からあふれ得る）。
/// </para>
/// </remarks>
public interface IDialogService
{
    /// <summary>ルーチンの確認向けに OK / キャンセルの確認ダイアログを表示する（Question アイコン）</summary>
    /// <remarks>取り返しのつかない結果を伴う続行確認は <see cref="ConfirmWarning"/> を使う</remarks>
    /// <returns>OK が選択された場合 true</returns>
    bool Confirm(string message, string title);

    /// <summary>取り返しのつかない結果を伴う続行確認向けに、警告アイコン付きの OK / キャンセル確認ダイアログを表示する（Warning アイコン）</summary>
    /// <remarks>
    /// 実 DB の変更・未保存変更の破棄・データ喪失の可能性など、続行すると元に戻せない操作の確認に限って使う。
    /// 判断材料の一覧を添える確認は <see cref="ConfirmWarningDetails"/> を使う
    /// </remarks>
    /// <returns>OK が選択された場合 true</returns>
    bool ConfirmWarning(string message, string title);

    /// <summary>
    /// 先頭メッセージと複数行の詳細（一覧）を、スクロール可能な詳細領域を持つ警告確認ダイアログで提示し、
    /// 続行確認（OK / キャンセル）を取る
    /// </summary>
    /// <remarks>
    /// <see cref="ConfirmWarning"/> と同じ Warning 意味論で、判断材料の一覧が長く
    /// 標準 MessageBox（スクロール不可）に収まらない確認に使う。一覧は畳まず全件を渡してよい
    /// </remarks>
    /// <param name="message">上部に表示する要約メッセージ（確認の導入文・注記・問い）</param>
    /// <param name="details">読み取り専用領域に表示する複数行の詳細（一覧本文）</param>
    /// <param name="title">ダイアログのタイトル</param>
    /// <returns>OK が選択された場合 true</returns>
    bool ConfirmWarningDetails(string message, string details, string title);

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
