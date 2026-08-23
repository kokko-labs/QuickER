using System.Windows;

namespace QuickER.Gui.Common;

/// <summary>
/// 要約メッセージと複数行の詳細（一覧）を、広い読み取り専用領域で提示するモーダルダイアログ。
/// </summary>
/// <remarks>
/// 一覧形式の処理結果（生成診断・PackageReference 案内など）を、
/// 幅の狭い標準 <see cref="MessageBox"/> に代えて見やすく表示するために用いる。
/// 表示のみで状態を持たないため <c>ViewModel</c> は設けず、コンストラクタで各コントロールへ流し込む
/// （情報／エラー／警告確認の区別はヘッダのアイコン記号と色だけで表す）。
/// 続行確認（OK／キャンセル・警告アイコン）として使う場合は
/// <see cref="CreateWarningConfirmation"/> で生成する＝一覧が長く標準 MessageBox に収まらない確認向け。
/// </remarks>
public partial class InformationDetailsDialog : Window
{
    /// <summary>
    /// 続行確認（警告アイコン＋OK／キャンセル）のダイアログを生成する。
    /// <see cref="Window.ShowDialog"/> の戻り値が true なら OK が選択されたことを表す。
    /// </summary>
    /// <remarks>
    /// キャンセルボタンが追加される分、Esc の割り当てを OK からキャンセルへ付け替える
    /// （情報／エラー表示では唯一のボタンである OK が Esc を兼ねる）。
    /// </remarks>
    /// <param name="message">上部に表示する要約メッセージ（確認の導入文・注記・問い）</param>
    /// <param name="details">読み取り専用領域に表示する複数行の詳細（一覧本文・畳まず全件でよい）</param>
    /// <param name="title">ウィンドウタイトル</param>
    public static InformationDetailsDialog CreateWarningConfirmation(
        string message,
        string details,
        string title
    )
    {
        var dialog = new InformationDetailsDialog(message, details, title, isError: false);

        // 続行前の注意＝Warning 意味論（すでに発生した失敗の ✖ とは使い分ける）
        dialog.HeaderIcon.Text = "⚠";
        dialog.HeaderIcon.Foreground = System.Windows.Media.Brushes.DarkOrange;
        dialog.OkButton.IsCancel = false;
        dialog.CancelButton.Content = QuickER.Gui.Common.Resources.Strings.DetailsDialog_Cancel;
        dialog.CancelButton.IsCancel = true;
        dialog.CancelButton.Visibility = Visibility.Visible;

        return dialog;
    }

    /// <summary>要約メッセージ・詳細本文・タイトル・種別（情報／エラー）を受け取って初期化する</summary>
    /// <param name="message">上部に表示する要約メッセージ</param>
    /// <param name="details">読み取り専用領域に表示する複数行の詳細（一覧本文）</param>
    /// <param name="title">ウィンドウタイトル</param>
    /// <param name="isError">エラー表示なら true（エラーアイコン＋エラー色）。情報なら false（情報アイコン）</param>
    /// <param name="copyButtonText">
    /// 詳細をクリップボードへコピーするボタンの文言。null／空なら該当ボタンを表示しない（既定＝従来の OK のみ）
    /// </param>
    public InformationDetailsDialog(
        string message,
        string details,
        string title,
        bool isError,
        string? copyButtonText = null
    )
    {
        InitializeComponent();

        Title = title;
        MessageText.Text = message;
        DetailsText.Text = details;

        // 情報／エラーで軽量に見分ける（記号と色のみ・凝った装飾はしない）
        if (isError)
        {
            // すでに発生した失敗の報告＝Error 意味論（続行前の注意を表す警告 ⚠ とは使い分ける）
            HeaderIcon.Text = "✖"; // ✖
            HeaderIcon.Foreground = System.Windows.Media.Brushes.IndianRed;
        }
        else
        {
            HeaderIcon.Text = "ℹ"; // ℹ
            HeaderIcon.Foreground = System.Windows.Media.Brushes.SteelBlue;
        }

        // コピーボタンは文言が与えられたときだけ見せる（クラッシュ報告など、詳細の持ち出しが要る用途向け）
        if (!string.IsNullOrEmpty(copyButtonText))
        {
            CopyButton.Content = copyButtonText;
            CopyButton.Visibility = Visibility.Visible;
        }
    }

    /// <summary>OK ボタン押下でダイアログを閉じる</summary>
    private void OnOkClick(object sender, RoutedEventArgs e) => DialogResult = true;

    /// <summary>キャンセルボタン押下でダイアログを閉じる（続行確認モードのみ表示される）</summary>
    private void OnCancelClick(object sender, RoutedEventArgs e) => DialogResult = false;

    /// <summary>詳細本文をクリップボードへコピーする</summary>
    private void OnCopyDetailsClick(object sender, RoutedEventArgs e)
    {
        try
        {
            Clipboard.SetText(DetailsText.Text);
        }
        catch (System.Runtime.InteropServices.ExternalException)
        {
            // 他プロセスがクリップボードをロックしている場合の失敗は無視する
            // （報告を妨げないよう、ダイアログ自体は表示したままにする）
        }
    }
}
