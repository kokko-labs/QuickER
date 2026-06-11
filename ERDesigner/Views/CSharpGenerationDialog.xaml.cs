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

    /// <summary>名前空間と出力先の初期値を指定してダイアログを生成する</summary>
    public CSharpGenerationDialog(string namespaceName, string outputFilePath = "ErDesignerEntities.g.cs")
    {
        InitializeComponent();
        ViewModel = new CSharpGenerationDialogViewModel(namespaceName, outputFilePath)
        {
            CloseAction = result =>
            {
                DialogResult = result;
                Close();
            },
            BrowseOutputFileAction = BrowseOutputFile,
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
}
