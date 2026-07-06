using System.IO;
using Microsoft.Win32;
using QuickER.Gui.Abstractions;

namespace QuickER.AI.UI;

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
    public IReadOnlyList<string> PickOpenFiles(string filter)
    {
        var dialog = new OpenFileDialog { Filter = filter, Multiselect = true };

        return dialog.ShowDialog() == true ? dialog.FileNames : Array.Empty<string>();
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
