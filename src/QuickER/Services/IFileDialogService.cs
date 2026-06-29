using Microsoft.Win32;

namespace QuickER.Services;

/// <summary>ファイル選択ダイアログの結果（選択パスとフィルター選択位置）</summary>
/// <param name="Path">選択されたファイルのフルパス</param>
/// <param name="FilterIndex">選択されたフィルターの 1 始まりインデックス（拡張子判定に使用）</param>
public sealed record FileDialogResult(string Path, int FilterIndex);

/// <summary>ファイルを開く / 保存する選択ダイアログの表示を抽象化するインターフェース</summary>
/// <remarks>ViewModel から <c>Microsoft.Win32</c> への直接依存を除去し、単体テストではスタブへ差し替える</remarks>
public interface IFileDialogService
{
    /// <summary>ファイルを開くダイアログを表示する（キャンセル時は null）</summary>
    FileDialogResult? PickOpenFile(string filter);

    /// <summary>ファイルを保存するダイアログを表示する（キャンセル時は null）</summary>
    FileDialogResult? PickSaveFile(string filter, string defaultExt);
}

/// <summary><see cref="Microsoft.Win32"/> のダイアログを用いた <see cref="IFileDialogService"/> の既定実装</summary>
public sealed class WpfFileDialogService : IFileDialogService
{
    /// <inheritdoc />
    public FileDialogResult? PickOpenFile(string filter)
    {
        var dialog = new OpenFileDialog { Filter = filter };

        return dialog.ShowDialog() == true
            ? new FileDialogResult(dialog.FileName, dialog.FilterIndex)
            : null;
    }

    /// <inheritdoc />
    public FileDialogResult? PickSaveFile(string filter, string defaultExt)
    {
        var dialog = new SaveFileDialog { Filter = filter, DefaultExt = defaultExt };

        return dialog.ShowDialog() == true
            ? new FileDialogResult(dialog.FileName, dialog.FilterIndex)
            : null;
    }
}
