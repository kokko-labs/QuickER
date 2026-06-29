using System.IO;
using Microsoft.Win32;

namespace QuickER.Services;

/// <summary>ファイル選択ダイアログの結果（選択パスとフィルター選択位置）</summary>
/// <param name="Path">選択されたファイルのフルパス</param>
/// <param name="FilterIndex">選択されたフィルターの 1 始まりインデックス（拡張子判定に使用）</param>
public sealed record FileDialogResult(string Path, int FilterIndex);

/// <summary>ファイル / フォルダ選択ダイアログの表示を抽象化するインターフェース</summary>
/// <remarks>ViewModel から <c>Microsoft.Win32</c> への直接依存を除去し、単体テストではスタブへ差し替える</remarks>
public interface IFileDialogService
{
    /// <summary>ファイルを開くダイアログを表示する（キャンセル時は null）</summary>
    FileDialogResult? PickOpenFile(string filter);

    /// <summary>ファイルを保存するダイアログを表示する（キャンセル時は null）</summary>
    /// <param name="initialFileName">初期表示するファイル名（省略可）</param>
    /// <param name="initialDirectory">初期表示するフォルダ（省略可）</param>
    FileDialogResult? PickSaveFile(
        string filter,
        string defaultExt,
        string? initialFileName = null,
        string? initialDirectory = null
    );

    /// <summary>フォルダ選択ダイアログを表示する（キャンセル時は null）</summary>
    /// <param name="initialDirectory">初期表示するフォルダ（省略可）</param>
    string? PickFolder(string title, string? initialDirectory = null);
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
    public FileDialogResult? PickSaveFile(
        string filter,
        string defaultExt,
        string? initialFileName = null,
        string? initialDirectory = null
    )
    {
        var dialog = new SaveFileDialog { Filter = filter, DefaultExt = defaultExt };

        if (!string.IsNullOrWhiteSpace(initialFileName))
        {
            dialog.FileName = initialFileName;
        }

        if (!string.IsNullOrWhiteSpace(initialDirectory) && Directory.Exists(initialDirectory))
        {
            dialog.InitialDirectory = initialDirectory;
        }

        return dialog.ShowDialog() == true
            ? new FileDialogResult(dialog.FileName, dialog.FilterIndex)
            : null;
    }

    /// <inheritdoc />
    public string? PickFolder(string title, string? initialDirectory = null)
    {
        var dialog = new OpenFolderDialog { Title = title };

        if (!string.IsNullOrWhiteSpace(initialDirectory) && Directory.Exists(initialDirectory))
        {
            dialog.InitialDirectory = initialDirectory;
        }

        return dialog.ShowDialog() == true ? dialog.FolderName : null;
    }
}
