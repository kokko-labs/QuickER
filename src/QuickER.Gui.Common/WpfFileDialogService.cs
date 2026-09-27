using System.IO;
using Microsoft.Win32;
using QuickER.Gui.Abstractions;

namespace QuickER.Gui.Common;

/// <summary><see cref="Microsoft.Win32"/> のダイアログを用いた <see cref="IFileDialogService"/> の既定実装</summary>
/// <remarks>
/// ファイル選択も<b>必ずオーナー付きで表示する</b>（<see cref="DialogOwner.Resolve"/>＝メッセージボックスと
/// 同じ解決を共有する）。オーナーを与えないと、モードレスで開いた機能ウィンドウ（AI モック生成の
/// フォルダ選択など）の背面へ回り込み、呼び出し元は応答待ちのまま止まって見える。
/// </remarks>
public sealed class WpfFileDialogService : IFileDialogService
{
    /// <summary>オーナーを解決してダイアログを表示する（オーナーが無いときは引数なしの呼び出しへ倒す）</summary>
    /// <remarks>
    /// <c>null</c> を渡す実装挙動には頼らない（オーナーに <c>null</c> を渡すと例外になる版があり得るため）。
    /// </remarks>
    private static bool ShowWithOwner(CommonDialog dialog)
    {
        var owner = DialogOwner.Resolve();

        return (owner is null ? dialog.ShowDialog() : dialog.ShowDialog(owner)) == true;
    }

    /// <inheritdoc />
    public FileDialogResult? PickOpenFile(string filter)
    {
        var dialog = new OpenFileDialog { Filter = filter };

        return ShowWithOwner(dialog)
            ? new FileDialogResult(dialog.FileName, dialog.FilterIndex)
            : null;
    }

    /// <inheritdoc />
    public IReadOnlyList<string> PickOpenFiles(string filter)
    {
        var dialog = new OpenFileDialog { Filter = filter, Multiselect = true };

        return ShowWithOwner(dialog) ? dialog.FileNames : Array.Empty<string>();
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

        return ShowWithOwner(dialog)
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

        return ShowWithOwner(dialog) ? dialog.FolderName : null;
    }
}
