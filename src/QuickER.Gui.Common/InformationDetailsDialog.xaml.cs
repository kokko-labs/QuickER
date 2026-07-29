using System.Windows;

namespace QuickER.Gui.Common;

/// <summary>
/// 要約メッセージと複数行の詳細（一覧）を、広い読み取り専用領域で提示するモーダルダイアログ。
/// </summary>
/// <remarks>
/// 一覧形式の処理結果（生成診断・PackageReference 案内・変換不可カラム一覧など）を、
/// 幅の狭い標準 <see cref="MessageBox"/> に代えて見やすく表示するために用いる。
/// 表示のみで状態を持たないため <c>ViewModel</c> は設けず、コンストラクタで各コントロールへ流し込む
/// （情報／エラーの区別はヘッダのアイコン記号と色だけで表す）。
/// </remarks>
public partial class InformationDetailsDialog : Window
{
    /// <summary>要約メッセージ・詳細本文・タイトル・種別（情報／エラー）を受け取って初期化する</summary>
    /// <param name="message">上部に表示する要約メッセージ</param>
    /// <param name="details">読み取り専用領域に表示する複数行の詳細（一覧本文）</param>
    /// <param name="title">ウィンドウタイトル</param>
    /// <param name="isError">エラー表示なら true（エラーアイコン＋エラー色）。情報なら false（情報アイコン）</param>
    public InformationDetailsDialog(string message, string details, string title, bool isError)
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
    }

    /// <summary>OK ボタン押下でダイアログを閉じる</summary>
    private void OnOkClick(object sender, RoutedEventArgs e) => DialogResult = true;
}
