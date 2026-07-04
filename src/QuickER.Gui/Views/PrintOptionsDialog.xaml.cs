using System.Windows;
using QuickER.Services;

namespace QuickER.Views;

/// <summary>印刷オプション（サイズモード・タイトル・日時印字）を選択するモーダルダイアログ</summary>
/// <remarks>
/// 入力項目が少なく状態も単純なため ViewModel は設けず、コードビハインドで確定結果を保持する
/// （OK 確定時に各入力コントロールから <see cref="Result"/> を組み立てる）
/// </remarks>
public partial class PrintOptionsDialog : Window
{
    /// <summary>OK 確定時に組み立てた印刷オプション（キャンセル時は null のまま）</summary>
    public PrintOptions? Result { get; private set; }

    /// <summary>タイトル初期値を受け取ってダイアログを初期化する（既定は 1 ページ縮小フィット・日時印字あり）</summary>
    /// <param name="defaultTitle">タイトル入力欄の初期値（未保存なら null）</param>
    public PrintOptionsDialog(string? defaultTitle = null)
    {
        InitializeComponent();

        TitleTextBox.Text = defaultTitle ?? string.Empty;
    }

    /// <summary>OK ボタン押下で各入力を確定し、ダイアログを閉じる</summary>
    private void OnOkClick(object sender, RoutedEventArgs e)
    {
        var sizeMode =
            ActualSizeRadio.IsChecked == true ? PrintSizeMode.ActualSize : PrintSizeMode.FitToPage;

        Result = new PrintOptions(
            sizeMode,
            TitleTextBox.Text.Trim(),
            TimestampCheckBox.IsChecked == true
        );

        DialogResult = true;
    }
}
