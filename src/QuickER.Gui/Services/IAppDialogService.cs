using System.Windows;

namespace QuickER.Services;

/// <summary>印刷ダイアログの確定結果（サイズモード・ヘッダのタイトル・印刷日時の印字有無）</summary>
/// <param name="SizeMode">縮小フィット／原寸大の選択</param>
/// <param name="Title">ヘッダに表示する図のタイトル（空欄ならヘッダへ印字しない）</param>
/// <param name="IncludeTimestamp">ヘッダに印刷日時を印字するかどうか</param>
public sealed record PrintOptions(PrintSizeMode SizeMode, string Title, bool IncludeTimestamp);

/// <summary>
/// アプリ固有のモーダルダイアログ（印刷オプション）の表示を抽象化するインターフェース
/// </summary>
/// <remarks>
/// メッセージボックスは <see cref="Gui.Abstractions.IDialogService"/>、ファイル選択は
/// <see cref="Gui.Abstractions.IFileDialogService"/> が担う。DB 接続・スキーマ同期など機能固有の
/// ダイアログは各フィーチャーモジュール側（QuickER.Db.UI 等）が提示シームとして持つ。
/// ViewModel から <c>Views.*</c> への直接依存を除去し、単体テストではスタブへ差し替える。
/// </remarks>
public interface IAppDialogService
{
    /// <summary>印刷オプション（サイズモード・タイトル・日時印字）の選択ダイアログを表示する（キャンセル時は null）</summary>
    /// <param name="defaultTitle">タイトル入力欄の初期値（最後に保存／読込した文書名。未保存なら null）</param>
    PrintOptions? ShowPrintOptionsDialog(string? defaultTitle);
}

/// <summary>WPF の <c>Views.*</c> ウィンドウを用いた <see cref="IAppDialogService"/> の既定実装</summary>
public sealed class WpfAppDialogService : IAppDialogService
{
    /// <inheritdoc />
    public PrintOptions? ShowPrintOptionsDialog(string? defaultTitle)
    {
        var dialog = new Views.PrintOptionsDialog(defaultTitle)
        {
            Owner = Application.Current?.MainWindow,
        };

        return dialog.ShowDialog() == true ? dialog.Result : null;
    }
}
