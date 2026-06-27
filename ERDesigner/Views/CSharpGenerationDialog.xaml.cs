using System.IO;
using System.Windows;
using ERDesigner.ViewModels;
using Microsoft.Win32;

namespace ERDesigner.Views;

/// <summary>C# コード生成ダイアログのコードビハインド</summary>
public partial class CSharpGenerationDialog : Window
{
    /// <summary>このダイアログの ViewModel</summary>
    public CSharpGenerationDialogViewModel ViewModel { get; }

    /// <summary>ダイアログを生成する（設定は永続化ストアから復元される）</summary>
    public CSharpGenerationDialog()
    {
        InitializeComponent();
        ViewModel = new CSharpGenerationDialogViewModel
        {
            CloseAction = result =>
            {
                DialogResult = result;
                Close();
            },
            BrowseOutputFileAction = BrowseOutputFile,
            BrowseOutputFolderAction = BrowseOutputFolder,
        };
        DataContext = ViewModel;
    }

    /// <summary>保存ダイアログで生成先ファイルを選択する</summary>
    /// <returns>選択したファイルパス キャンセル時は null</returns>
    private static string? BrowseOutputFile(string currentPath)
    {
        var initialDirectory = Path.GetDirectoryName(currentPath);
        var fileName = Path.GetFileName(currentPath);
        var dialog = new SaveFileDialog
        {
            Filter = "C# Generated Code (*.g.cs)|*.g.cs",
            DefaultExt = ".g.cs",
            FileName = string.IsNullOrWhiteSpace(fileName) ? "ErDesignerEntities.g.cs" : fileName,
        };

        if (!string.IsNullOrWhiteSpace(initialDirectory) && Directory.Exists(initialDirectory))
        {
            dialog.InitialDirectory = initialDirectory;
        }

        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    /// <summary>フォルダ選択ダイアログで生成先フォルダを選択する</summary>
    /// <returns>選択したフォルダパス キャンセル時は null</returns>
    private static string? BrowseOutputFolder(string currentPath)
    {
        var dialog = new OpenFolderDialog { Title = "出力先フォルダを選択" };

        if (!string.IsNullOrWhiteSpace(currentPath) && Directory.Exists(currentPath))
        {
            dialog.InitialDirectory = currentPath;
        }

        return dialog.ShowDialog() == true ? dialog.FolderName : null;
    }
}
